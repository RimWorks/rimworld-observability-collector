<script lang="ts">
    import { onMount, onDestroy, untrack } from 'svelte';
    import {
        api,
        ApiError,
        type StatusResponse,
        type HotspotsResponse,
        type GcResponse,
        type PatchesResponse,
        type ComparisonResponse,
    } from '../lib/api';
    import { summarize } from '../lib/gc';
    import { buildPatchIndex } from '../lib/patchIndex';
    import InstrumentationPanel from '../lib/components/InstrumentationPanel.svelte';
    import { liveSectionIds, type MergedPatch } from '../lib/livePatches';
    import { Resource } from '../lib/poll.svelte';
    import { liveVitals } from '../lib/vitals.svelte';
    import type {
        FrameData,
        FrameResponse,
        FrameStripData,
        BundleFramesResponse,
    } from '../lib/frameTree';
    import DataState from '../lib/components/DataState.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import FrameTimeline from '../lib/components/FrameTimeline.svelte';
    import FrameStrip from '../lib/components/FrameStrip.svelte';
    import CallTreePanel from '../lib/components/CallTreePanel.svelte';
    import ComparisonPanel from '../lib/components/ComparisonPanel.svelte';
    import NewSessionDialog from '../lib/components/NewSessionDialog.svelte';
    import StatusFooter from '../lib/components/StatusFooter.svelte';
    import { comparisonBaselineUs } from '../lib/comparison';
    import { buildFrameTree } from '../lib/frameTree';
    import {
        buildSeries,
        pushFrame,
        MAX_WINDOW_FRAMES,
        type SeriesCacheEntry,
    } from '../lib/frameSeries';
    import { flattenCallNodes, sessionTotalUs } from '../lib/sessionTree';
    import type { CallTreeResponse, ThreadLane } from '../lib/api';
    import { buildBars, stepOrdinal, DEFAULT_STRIP_SLOTS } from '../lib/frameStrip';
    import { liveConfig } from '../lib/liveConfig.svelte';
    import { sectionSearch } from '../lib/sectionSearchState.svelte';
    import { SvelteSet } from 'svelte/reactivity';
    import ThreadFilter from '../lib/components/ThreadFilter.svelte';
    import { recordCut, visibleCuts } from '../lib/frameCuts';
    import { buildFrameExport, exportFileName } from '../lib/frameExport';
    import { ns, count, bytes, gradeFromShare, sectionLabel } from '../lib/format';
    import {
        estimateOverheadUs,
        shareOfFrame,
        smoothOverhead,
        OVERHEAD_SEED,
        PER_SAMPLE_OVERHEAD_NS,
        nsPerScopeText,
        percent2,
        timerResolutionNs,
        timerResText,
        budgetSeverity,
        deltaSeverity,
        FRAME_BUDGET_US,
        tickBudgetUs,
        speedMultiplier,
    } from '../lib/frameCost';
    import { t } from '../lib/i18n';
    import {
        laneBands,
        laneBusyNs,
        laneLabel,
        lanesFromNodes,
        orderLanes,
        ThreadRole,
    } from '../lib/threadLanes';
    import { MAX_DEPTH } from '../lib/frameLayout';
    import { ROW_HEIGHT } from '../lib/frameDraw';
    import { userPrefs } from '../lib/userPrefs.svelte';

    // the whole spread against the frame budget, coloured by how much of it each one eats
    const PERCENTILES = [
        { key: 'median_us', label: 'flamegraph.p50' },
        { key: 'p75_us', label: 'flamegraph.p75' },
        { key: 'p90_us', label: 'flamegraph.p90' },
        { key: 'p99_us', label: 'flamegraph.p99' },
    ] as const;

    const LIVE = 'live';
    const NO_DROPS = { pre_frame_samples: 0, late_samples: 0 };

    // one import serves both jobs: a bundle with frames.json becomes a scrubbable source, and
    // every bundle becomes a comparison source. the collector expires the tokens after 30 min.
    interface ImportedBundle {
        token: string;
        label: string;
        frames: BundleFramesResponse | null;
        names: Map<number, { name: string; subsystem: string | null }>;
    }

    // one poll per frame. there is no rate control any more: the collector is on loopback and
    // a frame the dashboard never asked for is a frame it cannot show.
    const FRAME_POLL_MS = 16;
    let framesRes = $state<Resource<FrameResponse> | null>(null);
    let source = $state(LIVE);
    let imports = $state<ImportedBundle[]>([]);
    let frameIndex = $state(0);
    let importError = $state('');
    let importing = $state(false);
    let comparison = $state<ComparisonResponse | null>(null);

    let scrubbable = $derived(imports.filter((b) => b.frames !== null));
    let active = $derived(imports.find((b) => b.token === source) ?? null);
    let importedFrames = $derived(active?.frames ?? null);
    let importedNames = $derived(
        active?.names ?? new Map<number, { name: string; subsystem: string | null }>(),
    );
    let comparisonSources = $derived(
        imports.map((b) => ({ value: `bundle:${b.token}`, label: b.label })),
    );
    let paused = $state(false);
    // set while paused or stepping. null means follow the newest frame.
    let pinnedOrdinal = $state<number | null>(null);
    let pinnedRes = $state<FrameResponse | null>(null);
    const MAIN_FALLBACK: ThreadLane = {
        id: 0,
        name: 'MainThread',
        role: ThreadRole.Main,
        busy_ns: 0,
    };
    const NO_STRIP: FrameStripData = { ordinals: [], durations_us: [] };
    // the baseline is a median over 128 frames, so it says nothing about a session total.
    const NO_BASELINE = new Map<number, number>();

    let live = $derived(source === LIVE);
    let pinned = $derived(live && pinnedOrdinal !== null);

    // Resource takes its interval at construction, so a rate change means a fresh
    // instance; the cleanup return stops the old poller's timer.
    $effect(() => {
        if (source !== LIVE) {
            framesRes = null;
            return;
        }
        const res = new Resource<FrameResponse>(() => api.frames(), FRAME_POLL_MS);
        framesRes = res;
        res.start();
        return () => res.stop();
    });

    // rides along on /frames/latest, so the strip costs no second request. pausing freezes it
    // with the frame, and resuming leaves a cut mark where the gap is.
    let frozenStrip = $state<FrameStripData | null>(null);
    let frozenRoots = $state<CallTreeResponse['roots'] | null>(null);
    let cutOrdinals = $state<number[]>([]);
    let strip = $derived(frozenStrip ?? framesRes?.data?.strip ?? NO_STRIP);
    let stripBars = $derived(buildBars(strip.ordinals, strip.durations_us));
    // a cut that has scrolled out of the strip's window is no longer a gap anyone can see.
    let shownCuts = $derived(visibleCuts(cutOrdinals, strip.ordinals));

    // One gate. Everything that pauses goes through pauseAt, everything that resumes goes
    // through resume, so no caller can freeze half the page.
    function pauseAt(ordinal: number | null): void {
        if (!paused) {
            frozenStrip = framesRes?.data?.strip ?? null;
            frozenRoots = liveRoots;
        }

        paused = true;
        void showOrdinal(ordinal ?? pinnedOrdinal ?? liveOrdinal);
    }

    // a shareable json snapshot: one frame, or everything the collector's ring still holds.
    function saveExport(
        kind: 'frame' | 'ring',
        frames: FrameData[],
        stopwatchFrequency: number,
    ): void {
        if (frames.length === 0) return;
        const payload = buildFrameExport(
            kind,
            frames,
            framesRes?.data?.threads ?? [],
            names,
            stopwatchFrequency,
            dropped,
            liveConfig.autoInstrument,
        );
        const blob = new Blob([JSON.stringify(payload)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = exportFileName(
            kind,
            kind === 'frame' ? (frame?.capture_ordinal ?? null) : null,
        );
        a.click();
        URL.revokeObjectURL(url);
    }

    function exportFrame(): void {
        if (frame) saveExport('frame', [frame], stopwatchFrequency);
    }

    async function exportRing(): Promise<void> {
        const range = await api.frameRange(undefined, 0);
        saveExport('ring', range.frames, range.stopwatch_frequency);
    }

    function resume(): void {
        if (paused) {
            cutOrdinals = recordCut(cutOrdinals, frozenStrip?.ordinals ?? []);
            frozenStrip = null;
            frozenRoots = null;
        }

        paused = false;
        void showOrdinal(null);
    }

    async function showOrdinal(ordinal: number | null): Promise<void> {
        // every user-driven selection funnels through here, and live-follow never does, so
        // this is the one place a refit belongs.
        timeline?.refit();
        pinnedOrdinal = ordinal;
        if (ordinal === null) {
            pinnedRes = null;
            pinnedWindow = [];
            return;
        }
        await fetchPinned(ordinal);
    }

    async function fetchPinned(ordinal: number): Promise<void> {
        try {
            // one request for the whole window ending at the pin, so the flame keeps its
            // context instead of collapsing to the single frame under the cursor.
            const range = await api.frameRange(ordinal - MAX_WINDOW_FRAMES + 1);
            if (pinnedOrdinal !== ordinal) return;
            const at = range.frames.find((f) => f.capture_ordinal === ordinal);
            if (!at) throw new Error('evicted');
            pinnedWindow = range.frames.filter((f) => f.capture_ordinal <= ordinal);
            pinnedRes = { ...range, frame: at };
        } catch {
            if (pinnedOrdinal !== ordinal) return;
            // the ring evicted it between the click and the fetch. fall back to live.
            pinnedOrdinal = null;
            pinnedRes = null;
            pinnedWindow = [];
        }
    }

    function step(delta: number): void {
        const next = stepOrdinal(stripBars, pinnedOrdinal ?? liveOrdinal, delta);
        if (next === null) return;
        pauseAt(next);
    }

    function jumpToNewest(): void {
        resume();
    }

    function togglePause(): void {
        if (paused) {
            resume();
        } else {
            pauseAt(null);
        }
    }

    function handleTransportKey(e: KeyboardEvent): void {
        const el = e.target as HTMLElement | null;
        if (el && (el.tagName === 'INPUT' || el.tagName === 'SELECT' || el.tagName === 'TEXTAREA'))
            return;
        if (e.key === ' ') {
            e.preventDefault();
            togglePause();
        } else if (e.key === 'PageDown') {
            e.preventDefault();
            step(-1);
        } else if (e.key === 'PageUp') {
            e.preventDefault();
            step(1);
        } else if (e.key === 'Home' && e.shiftKey) {
            e.preventDefault();
            jumpToNewest();
        }
    }

    // 128 frames of nodes is real work, so it gets its own slow poll.
    const baselineRes = new Resource(() => api.frameBaseline(), 5000);
    let baselineUs = $derived(
        new Map(Object.entries(baselineRes.data?.median_us ?? {}).map(([k, v]) => [Number(k), v])),
    );

    // the speed setting is guessed from the TPS the collector already reports.
    const statusRes = new Resource<StatusResponse>(() => api.status(), 1000);
    let tps = $derived(statusRes.data?.receive?.tps ?? null);

    // per-section percentiles for the session-scope columns; the frame ring cannot produce them.
    const hotspotsRes = new Resource<HotspotsResponse>(() => api.hotspots(200), 5000);
    let percentiles = $derived(
        new Map(
            (hotspotsRes.data?.hotspots ?? []).map((h) => [
                h.id,
                { p50Us: h.p50_ns / 1000, p95Us: h.p95_ns / 1000, p99Us: h.p99_ns / 1000 },
            ]),
        ),
    );

    const gcRes = new Resource<GcResponse>(() => api.gc(200), 4000);
    let peakAllocRate = $derived(summarize(gcRes.data?.events ?? []).peakAllocRate);
    let gcOrdinals = $derived((gcRes.data?.events ?? []).map((e) => e.frame_ordinal));

    // which other mods patch each instrumented method. the set only changes when mods load,
    // so this polls slowly and just feeds a badge.
    const patchesRes = new Resource<PatchesResponse>(() => api.patches(), 15000);
    let patchOwners = $derived(buildPatchIndex(patchesRes.data?.conflicts ?? []));

    // the panel owns the poll; the flamegraph only needs to know which sections are live so a
    // bar can offer to un-instrument itself.
    let panel = $state<InstrumentationPanel | null>(null);
    let livePatches = $state<MergedPatch[]>([]);
    let liveBySection = $derived(liveSectionIds(livePatches));
    const sectionsRes = new Resource(() => api.allSections(), 10000);
    // read once: the ring capacity only moves when someone edits it in Settings, and the
    // status footer just needs a number to divide the held count by.
    let ringCapacity = $derived(liveConfig.ringCapacity);
    let treeScope = $state<'frame' | 'session'>('frame');
    let treeOpen = $state(false);
    let startingSession = $state(false);
    let askingNewSession = $state(false);

    // a session is one launch of the game, so a genuinely new one means restarting it. the name
    // is parked in the collector's config first: this process is about to die with the game, and
    // the next session to come up claims it.
    async function confirmNewSession(name: string, save: boolean): Promise<void> {
        askingNewSession = false;
        startingSession = true;
        try {
            resume();
            await api.restartGame(name, save);
            cutOrdinals = [];
        } finally {
            startingSession = false;
        }
    }

    let clearing = $state(false);

    // throws away the capture history the strip draws from. resumes through the one gate first,
    // because a paused view is pinned to a frame the collector is about to forget, and the cuts
    // go too: they mark gaps in a history that no longer exists.
    // one click arms, the second clears. the ring is the only copy of what you just captured,
    // and this button sits next to New session in identical styling.
    let confirmingClear = $state(false);

    function askClearRing(): void {
        if (!confirmingClear) {
            confirmingClear = true;
            return;
        }
        confirmingClear = false;
        void clearRing();
    }

    async function clearRing(): Promise<void> {
        clearing = true;
        try {
            resume();
            await api.clearFrames();
            cutOrdinals = [];
            await framesRes?.refresh();
        } finally {
            clearing = false;
        }
    }

    // the header sits outside this route, so the per-frame vitals go through a shared store
    // rather than a prop chain. an imported bundle has none, so the header falls back to status.
    $effect(() => {
        if (!live) {
            liveVitals.clear();
            return;
        }
        liveVitals.set(framesRes?.data?.vitals);
    });

    // the session tree keeps pace with the frame poll. it costs about the same as one
    // /frames/latest.
    let sessionTreeRes = $state<Resource<CallTreeResponse> | null>(null);
    $effect(() => {
        const res = new Resource<CallTreeResponse>(() => api.callTree(12, 24), FRAME_POLL_MS);
        sessionTreeRes = res;
        res.start();
        return () => res.stop();
    });
    onMount(() => {
        // one backfill so the window starts full instead of growing a frame per poll.
        api.frameRange()
            .then((r) => {
                if (liveWindow.length === 0) liveWindow = r.frames ?? [];
            })
            .catch(() => undefined);
        api.config()
            .then((c) => {
                liveConfig.setRingCapacity(c.sampling.frame_ring_capacity);
                liveConfig.setAutoInstrument(c.auto_instrument);
            })
            .catch(() => undefined);
        patchesRes.start();
        sectionsRes.start();
        hotspotsRes.start();
        gcRes.start();
        statusRes.start();
        baselineRes.start();
    });
    onDestroy(() => {
        patchesRes.stop();
        sectionsRes.stop();
        hotspotsRes.stop();
        gcRes.stop();
        statusRes.stop();
        baselineRes.stop();
    });

    async function openBundle(e: Event) {
        const input = e.currentTarget as HTMLInputElement;
        const file = input.files?.[0];
        if (!file) return;
        importError = '';
        importing = true;
        // holds an import nobody owns yet. cleared once it lands in `imports`, so the finally
        // deletes it on every path that bails, including a throw after the upload won.
        let orphan = '';
        try {
            const res = await api.importBundle(file);
            orphan = res.token;
            const label = String(res.manifest.session_id ?? file.name);

            // a bundle without frames is still a comparison source, so it is kept either way.
            let frames: BundleFramesResponse | null = null;
            let names = new Map<number, { name: string; subsystem: string | null }>();
            if (!res.contents.includes('frames.json')) {
                importError = t('flamegraph.source.noFrames');
            } else {
                const [loaded, hotspots] = await Promise.all([
                    api.importedFrames(res.token),
                    api.importedHotspots(res.token),
                ]);
                if (loaded.frames.length === 0) {
                    importError = t('flamegraph.source.emptyFrames');
                } else {
                    frames = loaded;
                    names = new Map(
                        hotspots.hotspots.map((h) => [
                            h.id,
                            { name: sectionLabel(h.name), subsystem: h.subsystem },
                        ]),
                    );
                }
            }

            imports = [
                ...imports.filter((b) => b.token !== res.token),
                { token: res.token, label, frames, names },
            ];
            orphan = '';
            if (frames) {
                frameIndex = Math.max(0, frames.frames.length - 1);
                source = res.token;
            }
        } catch (err) {
            importError = err instanceof ApiError ? err.message : String(err);
        } finally {
            if (orphan) void api.deleteImport(orphan);
            importing = false;
            input.value = '';
        }
    }

    let selectedNode = $state(-1);
    let timeline = $state<{
        focusNode: (i: number) => void;
        refit: () => void;
        stepMatch: (delta: 1 | -1) => number | null;
    } | null>(null);

    // stepping while live is futile: the next poll moves the frame out from under whatever you
    // just landed on. so a step pins the frame it found.
    function stepToMatch(delta: 1 | -1): void {
        const ordinal = timeline?.stepMatch(delta) ?? null;
        if (ordinal !== null && ordinal !== pinnedOrdinal) pauseAt(ordinal);
    }
    let searchInputEl = $state<HTMLInputElement | null>(null);

    let searchScopeText = $derived(
        sectionSearch.scope === 'frame'
            ? t('flamegraph.search.nodes')
            : t('flamegraph.search.nodesInFrames').replace('{m}', String(sectionSearch.frameCount)),
    );

    let searchStatusText = $derived.by(() => {
        if (!sectionSearch.active) return '';
        const base = `${sectionSearch.nodeCount} ${searchScopeText}`;
        if (sectionSearch.unsampledCount === 0) return base;
        const only = t('flamegraph.search.registeredOnly').replace(
            '{n}',
            String(sectionSearch.unsampledCount),
        );
        return `${base}, ${only}`;
    });

    function handleSearchKeydown(event: KeyboardEvent): void {
        if (event.key === 'Enter') {
            event.preventDefault();
            stepToMatch(event.shiftKey ? -1 : 1);
        } else if (event.key === 'Escape') {
            event.preventDefault();
            sectionSearch.clear();
            searchInputEl?.blur();
        }
    }

    // '/' or ctrl/cmd+f focuses search; neither is bound by the canvas or the transport bar.
    function handleSearchHotkey(event: KeyboardEvent): boolean {
        const el = event.target as HTMLElement | null;
        if (el && (el.tagName === 'INPUT' || el.tagName === 'SELECT' || el.tagName === 'TEXTAREA'))
            return false;
        if (event.key === '/' || ((event.ctrlKey || event.metaKey) && event.key === 'f')) {
            event.preventDefault();
            searchInputEl?.focus();
            return true;
        }
        return false;
    }

    // one svelte:window per component, so search gets first refusal and transport takes the rest.
    function handleWindowKey(event: KeyboardEvent): void {
        if (handleSearchHotkey(event)) return;
        handleTransportKey(event);
    }
    // while pinned the page reads a specific ordinal instead of whatever the poller last saw.
    let liveRes = $derived(
        pinned ? (pinnedRes ?? framesRes?.data ?? null) : (framesRes?.data ?? null),
    );
    let liveOrdinal = $derived(framesRes?.data?.frame?.capture_ordinal ?? null);
    let frame = $derived(
        live ? (liveRes?.frame ?? null) : (importedFrames?.frames[frameIndex] ?? null),
    );

    // the live poll still carries one frame; the window is accumulated here so the timeline
    // spans many without a second request per tick.
    let liveWindow = $state<FrameData[]>([]);
    let pinnedWindow = $state<FrameData[]>([]);
    $effect(() => {
        if (!live || pinned) return;
        liveWindow = pushFrame(liveWindow, framesRes?.data?.frame ?? null);
    });

    // lanes drain up to a window behind, so a pin parked on a recent frame may have fetched
    // it mid-flight. refresh it on each poll until it is old enough that no lane can add to it.
    const PIN_REFRESH_WINDOW = 32;
    $effect(() => {
        const newest = framesRes?.data?.stats?.newest_ordinal ?? 0;
        const ordinal = pinnedOrdinal;
        if (!live || ordinal === null || newest - ordinal > PIN_REFRESH_WINDOW) return;
        void fetchPinned(ordinal);
    });
    let bundleWindow = $derived(
        importedFrames
            ? importedFrames.frames.slice(
                  Math.max(0, frameIndex - MAX_WINDOW_FRAMES + 1),
                  frameIndex + 1,
              )
            : [],
    );
    let windowFrames = $derived(live ? (pinned ? pinnedWindow : liveWindow) : bundleWindow);

    // keyed by capture ordinal, so a frame is only turned into a tree once no matter how
    // many polls it stays in the window.
    const treeCache = new Map<number, SeriesCacheEntry>();
    let series = $derived.by(() => {
        if (treeCache.size > 4 * MAX_WINDOW_FRAMES) treeCache.clear();
        return buildSeries(windowFrames, treeCache);
    });
    let polledLanes = $derived(orderLanes(framesRes?.data?.threads ?? []));
    // a bundle carries no thread list, so its lanes come back off the nodes' own thread ids.
    // with neither, draw the main lane the page has always drawn.
    let allLanes = $derived(polledLanes.length > 0 ? polledLanes : lanesFromNodes(series.nodes));
    // a lane starts drawn and stays that way unless the filter panel turns it off; seeded
    // tracks what has been offered so the effect cannot undo a click.
    const seededLanes = new Set<number>();
    const selectedLanes = new SvelteSet<number>();
    $effect(() => {
        for (const lane of allLanes) {
            if (seededLanes.has(lane.id)) continue;
            seededLanes.add(lane.id);
            selectedLanes.add(lane.id);
        }
    });
    let laneCallCounts = $derived.by(() => {
        const counts = new Map<number, number>();
        for (const id of frame?.nodes.thread_ids ?? []) counts.set(id, (counts.get(id) ?? 0) + 1);
        return counts;
    });
    function laneStats(lane: ThreadLane): { calls: number; busyNs: number } {
        if (!frame) return { calls: 0, busyNs: 0 };
        if (!frame.nodes.thread_ids?.length) {
            // a v8 frame carries no lane data, and everything it holds rides the main band.
            return lane.role === ThreadRole.Main
                ? { calls: frame.node_count, busyNs: frame.duration_us * 1000 }
                : { calls: 0, busyNs: 0 };
        }
        return {
            calls: laneCallCounts.get(lane.id) ?? 0,
            busyNs: laneBusyNs(frame.nodes, lane.id),
        };
    }
    // strict rule: a lane draws only when the current frame holds its calls. main stays.
    let visibleLanes = $derived.by(() => {
        if (allLanes.length === 0) return [MAIN_FALLBACK];
        if (userPrefs.mainThreadOnly) return allLanes.filter((l) => l.role === ThreadRole.Main);
        return allLanes.filter(
            (l) =>
                selectedLanes.has(l.id) &&
                (l.role === ThreadRole.Main || (laneCallCounts.get(l.id) ?? 0) > 0),
        );
    });
    // one band per visible lane. the canvas and the gutter read the same offsets, so a lane
    // label always sits level with the flame it names.
    let bands = $derived(laneBands(visibleLanes, series.nodes, MAX_DEPTH));
    // the call tree stays on one frame, so its row indices need rebasing onto the window.
    let currentEntry = $derived(
        series.entries.find((e) => e.ordinal === frame?.capture_ordinal) ?? null,
    );
    let orphanCount = $derived(currentEntry?.orphanCount ?? 0);
    let treeSelection = $derived(
        currentEntry &&
            selectedNode >= currentEntry.nodeStart &&
            selectedNode < currentEntry.nodeEnd
            ? selectedNode - currentEntry.nodeStart
            : -1,
    );
    let stats = $derived(live ? (liveRes?.stats ?? null) : (importedFrames?.stats ?? null));
    let dropped = $derived((live ? liveRes?.dropped : importedFrames?.dropped) ?? NO_DROPS);

    // drops that stopped an hour ago are not news, so the badge watches the last half second of
    // polls and goes quiet again once the counters hold still.
    const DROP_WINDOW_POLLS = 32;
    let dropWindow = $state<number[]>([]);
    $effect(() => {
        if (!live || !liveRes) return;
        const total = dropped.pre_frame_samples + dropped.late_samples;
        untrack(() => {
            dropWindow = [...dropWindow, total].slice(-DROP_WINDOW_POLLS);
        });
    });
    let lossy = $derived(
        dropWindow.length > 1 && dropWindow[dropWindow.length - 1] > dropWindow[0],
    );
    let stopwatchFrequency = $derived(
        (live ? liveRes?.stopwatch_frequency : importedFrames?.stopwatch_frequency) ?? 0,
    );
    let names = $derived(
        live
            ? new Map(
                  (sectionsRes.data?.sections ?? []).map((s) => [
                      s.id,
                      { name: sectionLabel(s.name), subsystem: s.subsystem },
                  ]),
              )
            : importedNames,
    );
    let timerResNs = $derived(timerResolutionNs(stopwatchFrequency));
    // the timeline builds this too, but a shared derived keeps the row indices and the bar
    // indices talking about the same array.
    let frameNodes = $derived(frame ? buildFrameTree(frame).nodes : []);
    let liveRoots = $derived(sessionTreeRes?.data?.roots ?? []);
    let sessionRoots = $derived(frozenRoots ?? liveRoots);
    let treeNodes = $derived(treeScope === 'session' ? flattenCallNodes(sessionRoots) : frameNodes);
    // session totals compare only against another session's totals, which is why the rolling
    // 128-frame baseline never reaches this scope.
    let sessionBaselineUs = $derived(
        comparison ? comparisonBaselineUs(comparison.hotspots, names) : NO_BASELINE,
    );

    let overhead = $state(OVERHEAD_SEED);
    let deltaUs = $state<number | null>(null);
    let lastOrdinal = -1;
    let lastDurationUs: number | null = null;

    // a bundle's newest frame often carries the ordinal the poller just drew, so without
    // this the delta and overhead stay at their live values while bundle data is on screen.
    $effect(() => {
        void source;
        lastOrdinal = -1;
        lastDurationUs = null;
        deltaUs = null;
    });

    // smoothOverhead gates the actual update to once a second; this just feeds it.
    $effect(() => {
        if (frame === null || frame.capture_ordinal === lastOrdinal) return;
        const sample = shareOfFrame(estimateOverheadUs(frame.node_count), frame.duration_us);
        overhead = smoothOverhead(overhead, sample, performance.now());
        deltaUs = lastDurationUs === null ? null : frame.duration_us - lastDurationUs;
        lastDurationUs = frame.duration_us;
        lastOrdinal = frame.capture_ordinal;
    });

    function deltaText(us: number): string {
        const sign = us > 0 ? '+' : us < 0 ? '-' : '';
        return `${sign}${ns(Math.abs(us) * 1000)}`;
    }

    // split so the status footer can give overhead and timer resolution their own tooltips
    // instead of one blurb covering two different facts.
    let overheadText = $derived.by(() => {
        if (PER_SAMPLE_OVERHEAD_NS <= 0) return '';
        const frameUs = frame?.duration_us ?? 0;
        if (overhead.percent <= 0 || frameUs <= 0)
            return `${t('flamegraph.overhead')} ${nsPerScopeText()} ns/scope`;
        // lead with the per-frame total, marked a floor: filtered scopes still pay entry
        // cost the node count cannot see, so per-scope alone reads as free.
        const estUs = (overhead.percent / 100) * frameUs;
        return `${t('flamegraph.overhead')} ≥${ns(Math.round(estUs * 1000))} (~${percent2(overhead.percent)} ${t('flamegraph.overhead.offrame')}, ${nsPerScopeText()} ns/scope)`;
    });
    let timerResLine = $derived(
        timerResNs > 0 ? `${t('flamegraph.timerres')} ${timerResText(timerResNs)} ns` : '',
    );
</script>

<svelte:window onkeydown={handleWindowKey} />

<div class="profiler">
    <div class="bar">
        {#if live}
            <button type="button" onclick={togglePause} data-testid="pause">
                {paused ? t('flamegraph.resume') : t('flamegraph.pause')}
            </button>
            <button
                type="button"
                class="icon"
                onclick={() => step(-1)}
                aria-label={t('flamegraph.older')}
                data-testid="step-older">&#9664;</button
            >
        {/if}
        <span class="ord mono"
            >{t('flamegraph.ordinal')} <b>{frame?.capture_ordinal ?? '--'}</b></span
        >
        {#if live}
            <button
                type="button"
                class="icon"
                onclick={() => step(1)}
                aria-label={t('flamegraph.newer')}
                data-testid="step-newer">&#9654;</button
            >
            <button
                type="button"
                class="icon"
                onclick={jumpToNewest}
                aria-label={t('flamegraph.newest')}
                data-testid="jump-newest">&#9654;&#9654;</button
            >
            {#if paused}<span class="paused" data-testid="paused-badge"
                    >{t('flamegraph.paused')}</span
                >{/if}
        {/if}

        <label class="picker">
            <span class="dim">{t('flamegraph.source')}</span>
            <select bind:value={source}>
                <option value={LIVE}>{t('flamegraph.source.live')}</option>
                {#each scrubbable as b (b.token)}<option value={b.token}>{b.label}</option>{/each}
            </select>
        </label>
        <label class="filebtn" class:busy={importing}>
            <input type="file" accept=".zip" onchange={openBundle} disabled={importing} />
            {importing ? t('comparison.importing') : t('flamegraph.source.import')}
        </label>
        <span class="readout mono">
            {#each PERCENTILES as p (p.key)}
                {@const v = stats?.[p.key] ?? 0}
                {t(p.label)}
                <b class="g{gradeFromShare(v / FRAME_BUDGET_US)}" data-testid="stat-{p.key}"
                    >{ns(v * 1000)}</b
                >
                &middot;
            {/each}
            <Tooltip text={t('tip.flamegraph.budget')}>
                <span class="mono" data-testid="frame-budget"
                    >{t('flamegraph.budget')} <b>{ns(FRAME_BUDGET_US * 1000)}</b></span
                >
            </Tooltip>
            &middot;
            <Tooltip
                text={t('tip.flamegraph.tickBudget').replace('{n}', String(speedMultiplier(tps)))}
            >
                <span class="mono" data-testid="tick-budget"
                    >{t('flamegraph.tickBudget')} <b>{ns(tickBudgetUs(tps) * 1000)}</b></span
                >
            </Tooltip>
            {#if peakAllocRate > 0}
                &middot;
                <span class="mono" data-testid="alloc-rate"
                    >{t('flamegraph.allocRate')} <b>{bytes(peakAllocRate)}/m</b></span
                >
            {/if}
        </span>
    </div>

    {#if importError}<p class="import-error" role="alert">{importError}</p>{/if}

    {#if live}
        <div class="modes">
            <div class="seg" role="group" aria-label={t('flamegraph.mode')}>
                <button type="button" class="on" data-testid="mode-time"
                    >{t('flamegraph.mode.time')}</button
                >
                <button
                    type="button"
                    class="off"
                    disabled
                    title={t('tree.soon')}
                    data-testid="mode-alloc">{t('flamegraph.mode.alloc')}</button
                >
            </div>
            <span class="rightpair">
                <button
                    type="button"
                    class="clearring"
                    onclick={exportFrame}
                    data-testid="export-frame">{t('flamegraph.exportFrame')}</button
                >
                <button
                    type="button"
                    class="clearring"
                    onclick={() => void exportRing()}
                    data-testid="export-ring">{t('flamegraph.exportRing')}</button
                >
                <button
                    type="button"
                    class="clearring"
                    onclick={() => timeline?.resetView()}
                    data-testid="reset-view">{t('flamegraph.resetView')}</button
                >
                <Tooltip text={t('tip.flamegraph.newSession')}>
                    <button
                        type="button"
                        class="clearring"
                        onclick={() => (askingNewSession = true)}
                        disabled={startingSession}
                        data-testid="new-session">{t('flamegraph.newSession')}</button
                    >
                </Tooltip>
                <Tooltip text={t('tip.flamegraph.clearRing')}>
                    <button
                        type="button"
                        class="clearring"
                        class:armed={confirmingClear}
                        onclick={askClearRing}
                        onblur={() => (confirmingClear = false)}
                        disabled={clearing}
                        data-testid="clear-ring"
                        >{confirmingClear
                            ? t('flamegraph.clearRing.confirm').replace(
                                  '{n}',
                                  count(stats?.frame_count ?? 0),
                              )
                            : t('flamegraph.clearRing')}</button
                    >
                </Tooltip>
            </span>
        </div>
    {/if}

    {#if live && stripBars.length > 0}
        <FrameStrip
            ordinals={strip.ordinals}
            durationsUs={strip.durations_us}
            cutOrdinals={shownCuts}
            {gcOrdinals}
            slots={ringCapacity ?? DEFAULT_STRIP_SLOTS}
            selectedOrdinal={pinnedOrdinal ?? liveOrdinal}
            onSelect={(o) => pauseAt(o)}
        />
    {/if}

    {#if !live && importedFrames && importedFrames.frames.length > 0}
        <label class="scrub">
            <span class="dim">
                {t('flamegraph.frameOf')
                    .replace('{n}', String(frameIndex + 1))
                    .replace('{total}', String(importedFrames.frames.length))}
            </span>
            <input
                type="range"
                min="0"
                max={importedFrames.frames.length - 1}
                bind:value={frameIndex}
                data-testid="frame-scrub"
            />
        </label>
    {/if}

    <DataState
        state={live ? (framesRes?.state ?? 'loading') : 'ok'}
        error={live ? (framesRes?.error ?? '') : ''}
        empty={frame === null}
        emptyTitle={t('flamegraph.empty')}
        emptyHint={t('flamegraph.empty.hint')}
    >
        <p class="avg mono" data-testid="frame-drops">
            <span class="stats">
                <span class="cell">{t('flamegraph.nodes')} <b>{frame?.node_count ?? 0}</b></span
                ><span class="cell"
                    >{t('flamegraph.duration')}
                    <b
                        class:warn={budgetSeverity(frame?.duration_us ?? 0) === 1}
                        data-testid="frame-duration">{ns((frame?.duration_us ?? 0) * 1000)}</b
                    ></span
                ><span class="cell"
                    >{t('flamegraph.median')} <b>{ns((stats?.median_us ?? 0) * 1000)}</b></span
                ><span class="cell"
                    >{t('flamegraph.p99')} <b>{ns((stats?.p99_us ?? 0) * 1000)}</b></span
                ><span class="cell"
                    >{t('flamegraph.dropped.late')}
                    <b class:warn={dropped.late_samples > 0} data-testid="drop-late"
                        >{count(dropped.late_samples)}</b
                    ></span
                ><span class="cell"
                    >{t('flamegraph.dropped.preframe')}
                    <b data-testid="drop-preframe">{count(dropped.pre_frame_samples)}</b></span
                ><span class="cell"
                    >{t('flamegraph.dropped.orphans')}
                    <b class:warn={orphanCount > 0} data-testid="drop-orphans"
                        >{count(orphanCount)}</b
                    ></span
                >{#if lossy}<span
                        class="cell lossy"
                        data-testid="lossy-badge"
                        title={t('flamegraph.lossy.hint')}
                        >{t('flamegraph.lossy')}
                        <b data-testid="lossy-count"
                            >{count(dropped.late_samples + dropped.pre_frame_samples)}</b
                        ></span
                    >{/if}</span
            >
            <span class="find" data-testid="section-search">
                <label class="lbl" for="section-search-input">{t('flamegraph.search.label')}</label>
                <span class="box">
                    <svg class="glass" viewBox="0 0 12 12" aria-hidden="true">
                        <circle cx="5" cy="5" r="3.25" /><path d="M7.4 7.4 10 10" />
                    </svg>
                    <input
                        id="section-search-input"
                        type="search"
                        class="q"
                        bind:this={searchInputEl}
                        bind:value={sectionSearch.query}
                        onkeydown={handleSearchKeydown}
                        placeholder={t('flamegraph.search.placeholder')}
                        data-testid="section-search-input"
                    />
                </span>
                <span class="seg" role="group" aria-label={t('flamegraph.search.scopeLabel')}>
                    <button
                        type="button"
                        aria-pressed={sectionSearch.scope === 'frame'}
                        onclick={() => (sectionSearch.scope = 'frame')}
                        data-testid="section-search-scope-frame"
                        >{t('flamegraph.search.thisFrame')}</button
                    ><button
                        type="button"
                        aria-pressed={sectionSearch.scope === 'window'}
                        onclick={() => (sectionSearch.scope = 'window')}
                        data-testid="section-search-scope-window"
                        >{t('flamegraph.search.allFrames')}</button
                    >
                </span>
                <label class="check">
                    <input
                        type="checkbox"
                        bind:checked={sectionSearch.filterMode}
                        data-testid="section-search-filter"
                    />
                    {t('flamegraph.search.hideNonMatches')}
                </label>
                <span class="tally">
                    <span class="cell"
                        ><b class:none={sectionSearch.nodeCount === 0}>{sectionSearch.nodeCount}</b>
                        {searchScopeText}</span
                    ><span
                        class="cell dim"
                        class:empty={sectionSearch.unsampledCount === 0}
                        data-testid="section-search-unsampled"
                        >{t('flamegraph.search.notSampled').replace(
                            '{n}',
                            String(sectionSearch.unsampledCount),
                        )}</span
                    >
                </span>
                <button
                    type="button"
                    class="step"
                    onclick={() => stepToMatch(-1)}
                    disabled={sectionSearch.occurrenceCount === 0}
                    aria-label={t('flamegraph.search.prev')}
                    data-testid="section-search-prev"
                >
                    <svg viewBox="0 0 12 12" aria-hidden="true"
                        ><path d="M7.5 2.5 4 6l3.5 3.5" /></svg
                    >
                </button>
                <button
                    type="button"
                    class="step"
                    onclick={() => stepToMatch(1)}
                    disabled={sectionSearch.occurrenceCount === 0}
                    aria-label={t('flamegraph.search.next')}
                    data-testid="section-search-next"
                >
                    <svg viewBox="0 0 12 12" aria-hidden="true"
                        ><path d="M4.5 2.5 8 6l-3.5 3.5" /></svg
                    >
                </button>
            </span>
        </p>
        <div
            class="sr-only"
            aria-live="polite"
            aria-atomic="true"
            data-testid="section-search-status"
        >
            {searchStatusText}
        </div>

        <div class="stage" class:split={treeOpen}>
            <div class="gutter">
                <div class="lane gc">GC &mdash;</div>
                {#each visibleLanes as lane, i (lane.id)}
                    {@const stats = laneStats(lane)}
                    <div
                        class="lane"
                        class:main={lane.role === ThreadRole.Main}
                        style="height: {(bands.bands[i]?.rows ?? 1) * ROW_HEIGHT}px"
                        data-testid="lane-{lane.id}"
                    >
                        {laneLabel(lane)}
                        <small class="mono">{count(stats.calls)} &middot; {ns(stats.busyNs)}</small>
                    </div>
                {/each}
            </div>
            <div class="canvas">
                <FrameTimeline
                    bind:this={timeline}
                    {series}
                    {bands}
                    {names}
                    selectedOrdinal={pinnedOrdinal ?? liveOrdinal}
                    bind:selectedNode
                />
            </div>
        </div>

        <CallTreePanel
            nodes={treeNodes}
            {names}
            bind:scope={treeScope}
            percentiles={treeScope === 'session' ? percentiles : undefined}
            {patchOwners}
            selectedNode={treeScope === 'session' ? -1 : treeSelection}
            frameDurationUs={treeScope === 'session'
                ? sessionTotalUs(sessionRoots)
                : (frame?.duration_us ?? 0)}
            baselineUs={treeScope === 'session' ? sessionBaselineUs : baselineUs}
            bind:open={treeOpen}
            instrumentation={instrumentationPanel}
            comparison={comparisonPanel}
            threads={threadFilterPanel}
            onSelect={(i) => {
                if (treeScope === 'session' || !currentEntry) return;
                timeline?.focusNode(currentEntry.nodeStart + i);
            }}
        />
    </DataState>

    {#snippet instrumentationPanel()}
        <div class="footerpanel" data-testid="instrumentation-panel">
            <InstrumentationPanel bind:this={panel} onPatchesChange={(p) => (livePatches = p)} />
        </div>
    {/snippet}

    {#snippet threadFilterPanel()}
        <div class="footerpanel" data-testid="thread-filter-panel">
            <ThreadFilter
                threads={allLanes}
                nodes={frame?.nodes ?? null}
                frameNs={(frame?.duration_us ?? 0) * 1000}
                selected={selectedLanes}
            />
        </div>
    {/snippet}

    {#snippet comparisonPanel()}
        <div class="footerpanel" data-testid="comparison-panel">
            <ComparisonPanel
                imports={comparisonSources}
                onResult={(r) => {
                    comparison = r;
                    if (r) treeScope = 'session';
                }}
            />
        </div>
    {/snippet}
</div>

{#if askingNewSession}
    <NewSessionDialog onConfirm={confirmNewSession} onCancel={() => (askingNewSession = false)} />
{/if}

<StatusFooter
    ringHeld={stats?.frame_count ?? 0}
    {ringCapacity}
    {overheadText}
    {timerResLine}
    {deltaUs}
    deltaSeverity={deltaUs !== null ? deltaSeverity(deltaUs) : 0}
    deltaText={deltaUs !== null ? deltaText(deltaUs) : ''}
/>

<style>
    .footerpanel {
        padding: var(--s-3) var(--rail) var(--s-5);
    }
    .profiler {
        --f: 1.08;
        --f-body: calc(13.5px * var(--f));
        --f-ui: calc(12.5px * var(--f));
        --f-small: calc(11.5px * var(--f));
        --f-tiny: calc(10.5px * var(--f));
        --gut: calc(112px * var(--f));
        display: flex;
        flex-direction: column;
        min-height: 0;
        font-size: var(--f-body);
        /* clears both fixed footers (tab bar + status strip) so the last panel is never stuck
           behind them */
        padding-bottom: 64px;
    }
    .mono {
        font-family: var(--font-mono);
    }
    .dim {
        color: var(--text-dim);
    }
    .warn {
        color: var(--warn);
    }
    .lossy {
        color: var(--warn);
        border: 1px solid var(--warn);
        border-radius: 3px;
        padding: 0 6px;
        margin-left: 6px;
    }
    .lossy b {
        color: var(--warn);
    }
    .cool {
        color: var(--good);
    }

    .bar {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        flex-wrap: wrap;
        padding: 6px 12px;
        border-bottom: 1px solid var(--border-soft);
    }
    .bar > button {
        font: inherit;
        font-size: var(--f-ui);
        color: var(--text);
        background: var(--bg-surface-2);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 9px;
        cursor: pointer;
    }
    .bar > button.icon {
        font-family: var(--font-mono);
        padding: 3px 7px;
    }
    .bar > button:hover {
        border-color: var(--border-strong);
    }
    .ord {
        font-size: var(--f-ui);
        color: var(--text-dim);
    }
    .ord b {
        color: var(--text);
        font-weight: 500;
    }
    .paused {
        color: var(--warn);
        font-size: var(--f-small);
    }
    .picker {
        display: flex;
        align-items: center;
        gap: var(--s-1);
        font-size: var(--f-ui);
    }
    .picker select {
        font: inherit;
        font-size: var(--f-ui);
        color: var(--text);
        background: var(--bg-surface-2);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 2px 6px;
        max-width: 150px;
    }
    .filebtn {
        position: relative;
        display: inline-flex;
        align-items: center;
        font-size: var(--f-ui);
        color: var(--text);
        background: var(--bg-surface-2);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 9px;
        cursor: pointer;
    }
    .filebtn:hover {
        border-color: var(--border-strong);
    }
    .filebtn.busy {
        opacity: 0.6;
        cursor: progress;
    }
    .filebtn:focus-within {
        box-shadow: var(--ring-focus);
    }
    .filebtn input {
        position: absolute;
        width: 1px;
        height: 1px;
        opacity: 0;
        pointer-events: none;
    }
    .readout {
        margin-left: auto;
        font-size: var(--f-small);
        color: var(--text-dim);
    }
    .readout b {
        color: var(--text);
        font-weight: 500;
    }
    .readout b.g0 {
        color: var(--grade-0);
    }
    .readout b.g1 {
        color: var(--grade-1);
    }
    .readout b.g2 {
        color: var(--grade-2);
    }
    .readout b.g3 {
        color: var(--grade-3);
    }
    .readout b.g4 {
        color: var(--grade-4);
    }

    .modes {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        padding: 6px 12px;
        border-bottom: 1px solid var(--border-soft);
    }
    .rightpair {
        display: inline-flex;
        align-items: center;
        gap: var(--s-2);
        margin-left: auto;
    }
    .clearring {
        font: inherit;
        font-size: var(--f-ui, 12px);
        color: var(--text-dim);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 10px;
        cursor: pointer;
    }
    .clearring:hover:not(:disabled) {
        color: var(--text);
        border-color: var(--border-strong);
    }
    .clearring.armed {
        color: var(--bad);
        border-color: var(--bad);
    }
    .clearring:disabled {
        color: var(--text-ghost);
        cursor: default;
    }
    .seg {
        display: flex;
        border: 1px solid var(--border);
        overflow: hidden;
        background: var(--bg-surface-2);
    }
    .seg button {
        font: inherit;
        font-size: var(--f-ui);
        color: var(--text-faint);
        background: none;
        border: 0;
        border-radius: 0;
        padding: 3px 12px;
        cursor: pointer;
    }
    .seg button.on {
        background: color-mix(in srgb, var(--cyan) 20%, var(--bg-elev));
        color: var(--cyan-soft);
        font-weight: 500;
    }
    .seg button[disabled] {
        cursor: not-allowed;
    }
    .import-error {
        color: var(--bad);
        font-size: var(--f-ui);
        padding: 6px 12px;
        border-bottom: 1px solid var(--border-soft);
    }
    .scrub {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        padding: 6px 12px;
        font-size: var(--f-ui);
        border-bottom: 1px solid var(--border-soft);
    }
    .scrub input {
        flex: 1;
    }

    .avg {
        display: flex;
        align-items: center;
        gap: 16px;
        min-height: 44px;
        padding: 6px 12px;
        font-size: var(--f-small);
        color: var(--text-dim);
        border-bottom: 1px solid var(--border-soft);
    }
    /* the cells stay one inline run so `.cell + .cell::before` keeps drawing the separators. */
    .stats {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .find {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-left: auto;
        flex: none;
    }
    .find .lbl {
        color: var(--text-dim);
        white-space: nowrap;
    }
    /* a real field: bordered and filled, because a borderless input on a bar of labels reads
       as one more label. */
    .find .box {
        display: flex;
        align-items: center;
        gap: 4px;
        height: 28px;
        padding: 0 8px;
        border: 1px solid var(--border);
        border-radius: 3px;
        background: var(--bg-surface);
        transition: border-color var(--t-fast) var(--ease-out);
    }
    .find .box:hover {
        border-color: var(--border-strong);
    }
    .find .box:focus-within {
        border-color: var(--cyan);
    }
    .find .glass {
        width: 12px;
        height: 12px;
        flex: none;
        fill: none;
        stroke: var(--text-dim);
        stroke-width: 1;
        stroke-linecap: round;
    }
    .find .box:focus-within .glass {
        stroke: var(--cyan);
    }
    .find .q {
        width: 16ch;
        padding: 0;
        border: 0;
        background: none;
        font: inherit;
        color: var(--text);
        caret-color: var(--cyan);
    }
    .find .q:focus {
        outline: none;
    }
    .find .q::placeholder {
        color: var(--text-faint);
    }
    .find .q::-webkit-search-cancel-button {
        display: none;
    }
    /* both options visible: a lone toggle cannot say what its other state is. */
    .find .seg {
        display: flex;
        border: 1px solid var(--border);
        border-radius: 3px;
        overflow: hidden;
    }
    .find .seg button {
        height: 28px;
        padding: 0 10px;
        border: 0;
        background: var(--bg-surface);
        font: inherit;
        color: var(--text-dim);
        white-space: nowrap;
        cursor: pointer;
        transition: background var(--t-fast) var(--ease-out);
    }
    .find .seg button + button {
        border-left: 1px solid var(--border);
    }
    .find .seg button:hover {
        background: var(--bg-surface-2);
        color: var(--text);
    }
    .find .seg button[aria-pressed='true'] {
        background: var(--bg-elev);
        color: var(--text);
        box-shadow: inset 0 -2px 0 var(--cyan);
    }
    .find .check {
        display: flex;
        align-items: center;
        gap: 4px;
        height: 28px;
        color: var(--text-dim);
        white-space: nowrap;
        cursor: pointer;
    }
    .find .check:hover {
        color: var(--text);
    }
    .find b.none {
        color: var(--text-dim);
    }
    /* fixed box with steady digits: the count changes several times a second while live and
       everything beside it used to slide with it. */
    .find .tally {
        min-width: 24ch;
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
    }
    .find .tally .empty {
        visibility: hidden;
    }
    .find .step {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 28px;
        height: 28px;
        padding: 0;
        border: 1px solid var(--border);
        border-radius: 3px;
        background: var(--bg-surface);
        color: var(--text-dim);
        cursor: pointer;
    }
    .find .step:hover:not(:disabled) {
        background: var(--bg-surface-2);
        color: var(--text);
    }
    .find .step:disabled {
        color: var(--text-ghost);
        cursor: default;
    }
    .find .step svg {
        width: 12px;
        height: 12px;
        fill: none;
        stroke: currentColor;
        stroke-width: 1.5;
        stroke-linecap: round;
        stroke-linejoin: round;
    }
    .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
    }
    .find .step svg {
        width: 12px;
        height: 12px;
        fill: none;
        stroke: currentColor;
        stroke-width: 1;
        stroke-linecap: round;
        stroke-linejoin: round;
    }
    .avg b {
        color: var(--text);
        font-weight: 500;
    }
    .cell + .cell::before {
        content: ' · ';
        color: var(--text-ghost);
    }

    .stage {
        display: grid;
        grid-template-columns: var(--gut) 1fr;
        background: var(--bg-void);
        border-bottom: 1px solid var(--border);
        /* the flame owns whatever the chrome leaves; the drawer takes its cut in split mode */
        height: calc(100vh - var(--chrome-h));
        min-height: 160px;
        overflow: auto;
        /* drag pans instead; bars just eat width next to the flame. */
        scrollbar-width: none;
    }
    .stage::-webkit-scrollbar {
        display: none;
    }
    .stage.split {
        height: calc(100vh - var(--drawer-h) - var(--chrome-h));
    }
    .gutter {
        border-right: 1px solid var(--border);
        padding: 0 var(--s-2) var(--s-2) var(--s-2);
        font-size: var(--f-ui);
        background: var(--bg-base);
    }
    .lane {
        color: var(--text-dim);
        overflow: hidden;
    }
    /* the timeline's ruler is one row tall, and canvas row 0 starts directly under it. */
    .lane.gc {
        height: 18px;
    }
    .lane.main {
        color: var(--text);
        font-weight: 500;
    }
    .lane small {
        display: block;
        color: var(--text-faint);
        font-size: var(--f-small);
    }
    .canvas {
        min-width: 0;
    }

    .foot {
        font-size: var(--f-small);
        color: var(--text-faint);
        padding: var(--s-1) 12px;
    }
    .foot:first-of-type {
        border-top: 1px solid var(--border);
        margin-top: var(--s-2);
        padding-top: var(--s-2);
    }
</style>
