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
    import { StreamResource } from '../lib/stream.svelte';
    import { liveVitals } from '../lib/vitals.svelte';
    import type {
        FrameData,
        FrameResponse,
        FrameStripData,
        BundleFramesResponse,
    } from '../lib/frameTree';
    import DataState from '../lib/components/DataState.svelte';
    import Icon from '../lib/components/Icon.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import FrameTimeline from '../lib/components/FrameTimeline.svelte';
    import FrameStrip from '../lib/components/FrameStrip.svelte';
    import CallTreePanel from '../lib/components/CallTreePanel.svelte';
    import ComparisonPanel from '../lib/components/ComparisonPanel.svelte';
    import NewSessionDialog from '../lib/components/NewSessionDialog.svelte';
    import StatusFooter from '../lib/components/StatusFooter.svelte';
    import { comparisonBaselineUs } from '../lib/comparison';
    import {
        buildSeries,
        frameTreeNodes,
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
    import { lodFloorUs, refinementFor } from '../lib/lod';
    import { buildFrameExport, buildTimelineExport, exportFileName } from '../lib/frameExport';
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
        framePollMs,
    } from '../lib/frameCost';
    import { t } from '../lib/i18n';
    import { HASHED_COLORS, SUBSYSTEMS, SUBSYSTEM_TOKENS } from '../lib/frameDraw';
    import {
        laneBands,
        laneDepths,
        laneLabel,
        lanesFromEntries,
        orderLanes,
        recentLaneIds,
        ThreadRole,
        windowLaneStats,
    } from '../lib/threadLanes';
    import { MAX_DEPTH } from '../lib/frameLayout';
    import { ROW_HEIGHT } from '../lib/frameDraw';
    import { userPrefs } from '../lib/userPrefs.svelte';
    import { uiSignals } from '../lib/uiSignals.svelte';

    // the whole spread against the frame budget, coloured by how much of it each one eats
    const PERCENTILES = [
        { key: 'median_us', label: 'flamegraph.p50' },
        { key: 'p75_us', label: 'flamegraph.p75' },
        { key: 'p90_us', label: 'flamegraph.p90' },
        { key: 'p99_us', label: 'flamegraph.p99' },
    ] as const;

    // heuristic: boehm pauses scale with allocation rate, and 1 GB/m is where they get ugly.
    const ALLOC_WARN_BPM = 1024 ** 3;

    // sqrt scale to budget + 30%: a healthy 1-10 ms colony fills the left half instead of
    // rendering as a speck, and a tail past the max clips to the rail end.
    const METER_MAX_US = FRAME_BUDGET_US * 1.3;
    function meterPos(us: number): number {
        return Math.min(100, Math.sqrt(Math.max(0, us) / METER_MAX_US) * 100);
    }
    // healthy is silent: grades 0-1 draw in neutral steel so color is reserved for trouble.
    function meterHue(grade: number): string {
        return grade <= 1 ? 'var(--border-strong)' : `var(--grade-${grade})`;
    }
    function meterFill(p50: number, p99: number): string {
        const a = gradeFromShare(p50 / FRAME_BUDGET_US);
        const b = gradeFromShare(p99 / FRAME_BUDGET_US);
        if (meterHue(a) === meterHue(b)) return meterHue(a);
        return `linear-gradient(90deg, ${meterHue(a)}, ${meterHue(b)})`;
    }

    const LIVE = 'live';
    const NO_DROPS = {
        pre_frame_samples: 0,
        late_samples: 0,
        orphaned_samples: 0,
        transport_lost_batches: 0,
        library_ring_samples: 0,
    };

    // one import serves both jobs: a bundle with frames.json becomes a scrubbable source, and
    // every bundle becomes a comparison source. the collector expires the tokens after 30 min.
    interface ImportedBundle {
        token: string;
        label: string;
        frames: BundleFramesResponse | null;
        names: Map<number, { name: string; subsystem: string | null; assembly: string | null }>;
    }

    // one poll per frame while frames are small; flood-sized frames back the poll off, or
    // parsing them eats the frame budget and the whole page lags.
    const FRAME_POLL_MS = 16;
    let pollMs = $state(FRAME_POLL_MS);
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
        active?.names ??
            new Map<number, { name: string; subsystem: string | null; assembly: string | null }>(),
    );
    let comparisonSources = $derived(
        imports.map((b) => ({ value: `bundle:${b.token}`, label: b.label })),
    );
    let paused = $state(false);
    // set while paused or stepping. null means follow the newest frame.
    let pinnedOrdinal = $state<number | null>(null);
    let pinnedRes = $state<FrameResponse | null>(null);
    // a strip drag pins an inclusive ordinal range instead of one frame.
    let pinnedRange = $state<{ from: number; to: number } | null>(null);
    // why a pin went away, shown until dismissed or timed out.
    let pinNotice = $state<string | null>(null);
    let noticeTimer: ReturnType<typeof setTimeout> | undefined;
    const MAIN_FALLBACK: ThreadLane = {
        id: 0,
        name: 'MainThread',
        role: ThreadRole.Main,
        busy_ns: 0,
    };
    const NO_STRIP: FrameStripData = { ordinals: [], durations_us: [], alloc_bytes: [] };
    // the baseline is a median over 128 frames, so it says nothing about a session total.
    const NO_BASELINE = new Map<number, number>();

    const LEGEND = [
        ...SUBSYSTEMS.map((name, i) => ({ name, swatch: `var(${SUBSYSTEM_TOKENS[i]})` })),
        // an untagged section hashes to its own color, so its swatch shows three of them
        // rather than pretending the palette is one hue.
        {
            name: 'untagged',
            swatch: `linear-gradient(90deg, ${HASHED_COLORS[0]} 0 33%, ${HASHED_COLORS[1]} 33% 66%, ${HASHED_COLORS[2]} 66%)`,
        },
    ];

    let live = $derived(source === LIVE);
    let pinned = $derived(live && pinnedOrdinal !== null);

    $effect(() => {
        if (source !== LIVE) {
            framesRes = null;
            return;
        }
        // pushed, not polled: one SSE event per sealed-frame window, with the old poll as
        // the automatic fallback while the stream is down.
        const res = new StreamResource<FrameResponse>(
            '/api/v1/stream',
            () => api.frames(),
            untrack(() => pollMs),
            undefined,
            0,
        );
        framesRes = res;
        res.start();
        return () => res.stop();
    });

    // retuned in place: rebuilding the poller resets its data and flashes the loading state.
    $effect(() => {
        const nodeCount = framesRes?.data?.frame?.node_count;
        if (nodeCount === undefined) return;
        const next = framePollMs(nodeCount);
        if (next !== untrack(() => pollMs)) pollMs = next;
    });
    $effect(() => {
        // fallback-only retune for the frame lane. never the session tree: retuning it to
        // the frame cadence once made the fallback fetch 4.4MB of call_tree at 69ms intervals.
        framesRes?.setIntervalMs(pollMs);
    });

    // rides along on /frames/latest, so the strip costs no second request. pausing freezes it
    // with the frame, and resuming leaves a cut mark where the gap is.
    let frozenStrip = $state<FrameStripData | null>(null);
    let stripMode = $state<'time' | 'alloc'>('time');
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

    // drilling in the bottom pane means reading, and reading live data that shifts every
    // frame is useless: the panel bumps this to pin whatever is on screen.
    let lastPauseSignal = uiSignals.pauseLive;
    $effect(() => {
        const n = uiSignals.pauseLive;
        if (n === lastPauseSignal) return;
        lastPauseSignal = n;
        untrack(() => {
            if (live && !paused && frame !== null) pauseAt(frame.capture_ordinal);
        });
    });

    // grouping by mods, the spike you clicked is only useful next to the mod rows, so the
    // strip opens the tree tab for you.
    function pickStripFrame(ordinal: number): void {
        pauseAt(ordinal);
        if (userPrefs.treeGroupMode === 'mods') uiSignals.openTreeTab += 1;
    }

    function pauseRange(fromOrdinal: number, toOrdinal: number): void {
        if (!paused) {
            frozenStrip = framesRes?.data?.strip ?? null;
            frozenRoots = liveRoots;
        }

        paused = true;
        pinnedRange = { from: fromOrdinal, to: toOrdinal };
        timeline?.refit();
        void fetchRange(fromOrdinal, toOrdinal);
    }

    // one range download at a time: a wide drag is megabytes, and the pin refresh used to
    // stack a second copy on top of a fetch still in flight. state so the stage can dim.
    let rangeInFlight = $state(false);
    // the stage shrinks to its lanes, so the drawer reads this to claim whatever is left.
    let stageH = $state(0);
    // the chrome rows wrap on a narrow window, so measure them instead of summing by hand.
    let chromeRowsH = $state(0);
    let avgH = $state(0);
    let chromeStyle = $derived(
        chromeRowsH > 0
            ? `--chrome-measured: calc(${chromeRowsH + avgH}px + var(--chrome-fixed-h));`
            : '',
    );
    // the duration floor each pinned frame was fetched at, for zoom-in refinement.
    const pinnedLod = new Map<number, number>();

    // a wide selection fetches coarse first; zooming in refetches the visible slice finer.
    function selectionFloorUs(fromOrdinal: number, toOrdinal: number): number {
        let spanUs = 0;
        for (const bar of stripBars) {
            if (bar.ordinal >= fromOrdinal && bar.ordinal <= toOrdinal) spanUs += bar.durationUs;
        }
        return lodFloorUs(spanUs);
    }

    async function fetchRange(fromOrdinal: number, toOrdinal: number): Promise<void> {
        if (rangeInFlight) return;
        rangeInFlight = true;
        try {
            const floorUs = selectionFloorUs(fromOrdinal, toOrdinal);
            const range = await api.frameRange(fromOrdinal, toOrdinal - fromOrdinal + 1, floorUs);
            if (pinnedRange?.from !== fromOrdinal || pinnedRange?.to !== toOrdinal) return;
            const frames = range.frames.filter(
                (f) => f.capture_ordinal >= fromOrdinal && f.capture_ordinal <= toOrdinal,
            );
            const at = frames.at(-1);
            if (!at) {
                // the ring evicted the whole range between the drag and the fetch. a copy
                // already on screen stays; only an empty view falls back to live.
                const shown = pinnedRes?.frame?.capture_ordinal ?? -1;
                if (shown >= fromOrdinal && shown <= toOrdinal) {
                    noticeCached(fromOrdinal);
                    return;
                }
                resume();
                noticePin('flamegraph.pinEvicted', fromOrdinal);
                return;
            }
            pinnedLod.clear();
            const servedFloor = range.lod_min_dur_us ?? 0;
            for (const f of frames) pinnedLod.set(f.capture_ordinal, servedFloor);
            pinnedOrdinal = at.capture_ordinal;
            pinnedWindow = frames;
            pinnedRes = { ...range, frame: at };
            // the view still points at the pre-drag time region; refit so the fit lands on
            // the range that just arrived.
            timeline?.refit();
        } catch {
            if (pinnedRange?.from !== fromOrdinal || pinnedRange?.to !== toOrdinal) return;
            // a refresh hiccup keeps the frames already on screen. with none of them in this
            // range, holding would leave the paused badge over live data.
            const shown = pinnedRes?.frame?.capture_ordinal ?? -1;
            if (shown >= fromOrdinal && shown <= toOrdinal) return;
            resume();
            noticePin('flamegraph.pinFailed', fromOrdinal);
        } finally {
            rangeInFlight = false;
        }
    }

    // zoom refinement: pull the visible ordinals back at the finer floor the view now needs.
    async function refineView(view: { startUs: number; endUs: number }): Promise<void> {
        if (!live || !pinnedRange || rangeInFlight) return;
        const spans = pinnedWindow.map((f) => ({
            ordinal: f.capture_ordinal,
            startUs: f.start_us,
            endUs: f.end_us,
        }));
        const wanted = refinementFor(view, spans, pinnedLod);
        if (!wanted) return;
        rangeInFlight = true;
        try {
            const range = await api.frameRange(
                wanted.from,
                wanted.to - wanted.from + 1,
                wanted.floorUs,
            );
            if (!pinnedRange) return;
            const finer = new Map(range.frames.map((f) => [f.capture_ordinal, f]));
            const servedFloor = range.lod_min_dur_us ?? 0;
            pinnedWindow = pinnedWindow.map((f) => {
                const next = finer.get(f.capture_ordinal);
                if (!next) return f;
                pinnedLod.set(f.capture_ordinal, servedFloor);
                treeCache.delete(f.capture_ordinal);
                return next;
            });
        } catch {
            // refinement is best effort; the coarse frames are already on screen.
        } finally {
            rangeInFlight = false;
        }
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
            // the filter that produced these frames, not whatever this collector runs now.
            live ? liveConfig.autoInstrument : (importedFrames?.auto_instrument ?? null),
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
        // count=0 means "server default" (64), not "everything"; ask for the whole ring.
        const range = await api.frameRange(undefined, ringCapacity ?? DEFAULT_STRIP_SLOTS);
        saveExport('ring', range.frames, range.stopwatch_frequency);
    }

    // every ring frame as a summary row; tick sections ride along so the file answers
    // convergence questions without any node data.
    async function exportTimeline(): Promise<void> {
        const tickIds = [...names.entries()]
            .filter(([, s]) => s.name.includes('TickManager.DoSingleTick'))
            .map(([id]) => id);
        const summaries = await api.frameSummaries(tickIds);
        const payload = buildTimelineExport(
            summaries,
            names,
            live ? liveConfig.autoInstrument : (importedFrames?.auto_instrument ?? null),
        );
        const blob = new Blob([JSON.stringify(payload)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = exportFileName('timeline', null);
        a.click();
        URL.revokeObjectURL(url);
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
        cachedNoticeFor = null;
        pinnedRange = null;
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
            if (!at) {
                // the ring evicted it between the click and the fetch. a copy already on
                // screen stays; only an empty view falls back to live.
                if (pinnedRes?.frame?.capture_ordinal === ordinal) {
                    noticeCached(ordinal);
                    return;
                }
                resume();
                noticePin('flamegraph.pinEvicted', ordinal);
                return;
            }
            pinnedWindow = range.frames.filter((f) => f.capture_ordinal <= ordinal);
            pinnedRes = { ...range, frame: at };
        } catch {
            if (pinnedOrdinal !== ordinal) return;
            // a refresh hiccup keeps the frame already on screen. with nothing pinned for this
            // ordinal, holding would leave the paused badge over live data.
            if (pinnedRes?.frame?.capture_ordinal === ordinal) return;
            resume();
            noticePin('flamegraph.pinFailed', ordinal);
        }
    }

    function noticePin(key: string, ordinal: number): void {
        pinNotice = t(key).replace('{n}', String(ordinal));
        clearTimeout(noticeTimer);
        noticeTimer = setTimeout(() => (pinNotice = null), 6000);
    }

    // once per pin: the stats poll re-runs the eviction check every tick.
    let cachedNoticeFor = $state<number | null>(null);
    function noticeCached(ordinal: number): void {
        if (cachedNoticeFor === ordinal) return;
        cachedNoticeFor = ordinal;
        noticePin('flamegraph.pinCached', ordinal);
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
        // inside an open modal the transport must not move, and Space on a focused button is
        // the button's activation, not a pause toggle behind the scrim.
        if (el instanceof HTMLElement && el.closest('dialog[open]') != null) return;
        if (e.key === ' ' && el instanceof HTMLElement && el.tagName === 'BUTTON') return;
        if (e.key === ' ') {
            e.preventDefault();
            togglePause();
        } else if (e.key === 'PageDown') {
            e.preventDefault();
            step(-1);
        } else if (e.key === 'PageUp') {
            e.preventDefault();
            step(1);
        } else if (e.key === 'Home') {
            e.preventDefault();
            jumpToNewest();
        }
    }

    // 128 frames of nodes is real work, so it gets its own slow poll.
    const baselineRes = new StreamResource(
        '/api/v1/stream',
        () => api.frameBaseline(),
        10000,
        undefined,
        0,
        'baseline',
    );
    let baselineUs = $derived(
        new Map(Object.entries(baselineRes.data?.median_us ?? {}).map(([k, v]) => [Number(k), v])),
    );

    // the speed setting is guessed from the TPS the collector already reports.
    const statusRes = new StreamResource<StatusResponse>(
        '/api/v1/stream',
        () => api.status(),
        2000,
        undefined,
        0,
        'status',
    );
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

    const gcRes = new StreamResource<GcResponse>(
        '/api/v1/stream',
        () => api.gc(200),
        8000,
        undefined,
        0,
        'gc',
    );
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
    const sectionsRes = new StreamResource(
        '/api/v1/stream',
        () => api.allSections(),
        20000,
        undefined,
        0,
        'sections',
    );
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

    // one click arms, the second clears: the ring is the only copy of the capture, and the
    // clear resumes through the one gate first so a pinned view never outlives its frame.
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
        liveVitals.set(framesRes?.data?.vitals, framesRes?.data?.stats?.median_us);
    });

    // pushed on the stream's slow lane, and only while the drawer is open: the session tree
    // json is 15MB+ late-session, and an unwatched 15MB parse per push froze the tab.
    let sessionTreeRes = $state<Resource<CallTreeResponse> | null>(null);
    $effect(() => {
        if (!treeOpen) {
            sessionTreeRes = null;
            return;
        }
        const res = new StreamResource<CallTreeResponse>(
            '/api/v1/stream',
            () => api.callTree(12, 24),
            5000,
            undefined,
            0,
            'call_tree',
        );
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
        input.value = '';
        await importBundleFile(file);
    }

    // dropping a bundle anywhere on the stage is the profiler convention; the button stays.
    let draggingBundle = $state(false);
    function stageDragOver(e: DragEvent): void {
        if (!e.dataTransfer?.types.includes('Files')) return;
        e.preventDefault();
        draggingBundle = true;
    }
    function stageDrop(e: DragEvent): void {
        e.preventDefault();
        draggingBundle = false;
        const file = e.dataTransfer?.files?.[0];
        if (!file) return;
        if (!file.name.endsWith('.zip')) {
            importError = t('flamegraph.source.notBundle').replace('{name}', file.name);
            return;
        }
        void importBundleFile(file);
    }

    async function importBundleFile(file: File) {
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
            let names = new Map<
                number,
                { name: string; subsystem: string | null; assembly: string | null }
            >();
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
                            // a bundle's hotspots carry no assembly, so by-mod grouping skips them.
                            { name: sectionLabel(h.name), subsystem: h.subsystem, assembly: null },
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
        }
    }

    let selectedNode = $state(-1);
    let timeline = $state<{
        focusNode: (i: number) => void;
        refit: () => void;
        resetView: () => void;
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
    let liveWindow = $state.raw<FrameData[]>([]);
    let pinnedWindow = $state.raw<FrameData[]>([]);
    $effect(() => {
        if (!live || pinned) return;
        const next = pushFrame(liveWindow, framesRes?.data?.frame ?? null);
        if (next !== liveWindow) pruneTreeCache(next);
        liveWindow = next;
    });

    // the cache gains an entry per frame at 30/s; without eviction it retains every frame
    // of the session and major-gc marking grows with it (the "cpu rises over time" leak).
    function pruneTreeCache(window: FrameData[]): void {
        if (treeCache.size <= window.length + MAX_WINDOW_FRAMES) return;
        const keep = new Set<number>();
        for (const f of window) keep.add(f.capture_ordinal);
        for (const f of pinnedWindow) keep.add(f.capture_ordinal);
        for (const key of treeCache.keys()) if (!keep.has(key)) treeCache.delete(key);
    }

    // a pinned recent frame may still be taking lane fills, so it refreshes until sealed.
    // cadence-capped: refetching a whole range on every 16ms poll re-downloaded it 60x/s.
    const PIN_REFRESH_WINDOW = 64;
    const PIN_REFRESH_MS = 250;
    let lastPinRefresh = 0;
    $effect(() => {
        const newest = framesRes?.data?.stats?.newest_ordinal ?? 0;
        const ordinal = pinnedOrdinal;
        if (!live || ordinal === null || newest - ordinal > PIN_REFRESH_WINDOW) return;
        const now = performance.now();
        if (now - lastPinRefresh < PIN_REFRESH_MS) return;
        lastPinRefresh = now;
        const range = pinnedRange;
        if (range) {
            void fetchRange(range.from, range.to);
            return;
        }
        void fetchPinned(ordinal);
    });

    // outside that window nothing refetches, so the polled stats are the only thing left that
    // sees the ring wrap past the pin. without this it goes stale under the paused badge.
    $effect(() => {
        const stats = framesRes?.data?.stats;
        // a range pin holds up to pinnedOrdinal, so that is the last frame to age out.
        const newest = pinnedOrdinal;
        if (!live || newest === null || !stats) return;
        // ordinals skip frames that carried no samples, so newest - frame_count reads high
        // and calls a held pin evicted. the collector serves the real oldest, -1 when empty.
        if (stats.oldest_ordinal <= newest) return;
        // the frame on screen is a client-side copy, so eviction does not end the pause.
        // say so once and stay frozen; resume is the user's move.
        noticeCached(pinnedRange?.from ?? newest);
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
    let allLanes = $derived(
        polledLanes.length > 0 ? polledLanes : lanesFromEntries(series.entries),
    );
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
    // a lane draws while it has nodes in the newest few drawn frames: workers sample
    // sporadically, so one-frame strictness blanked them, and window-wide lingered too long.
    const RECENT_FRAMES = 10;
    let mainLaneId = $derived(allLanes.find((l) => l.role === ThreadRole.Main)?.id ?? 0);
    // the gutter counts the same recent window that decides visibility, so a drawn lane
    // never reads 0 | 0 just because it skipped the frame under the c‍ursor.
    let laneWindowStats = $derived(windowLaneStats(series.entries, RECENT_FRAMES, mainLaneId));
    function laneStats(lane: ThreadLane): { calls: number; busyNs: number } {
        return laneWindowStats.get(lane.id) ?? { calls: 0, busyNs: 0 };
    }
    let recentLanes = $derived(recentLaneIds(series.entries, RECENT_FRAMES));
    let visibleLanes = $derived.by(() => {
        if (allLanes.length === 0) return [MAIN_FALLBACK];
        if (userPrefs.mainThreadOnly) return allLanes.filter((l) => l.role === ThreadRole.Main);
        return allLanes.filter(
            (l) => selectedLanes.has(l.id) && (l.role === ThreadRole.Main || recentLanes.has(l.id)),
        );
    });
    // one band per visible lane. the canvas and the gutter read the same offsets, so a lane
    // label always sits level with the flame it names.
    let bands = $derived(laneBands(visibleLanes, laneDepths(series.entries), MAX_DEPTH));
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
    let p50v = $derived(stats?.median_us ?? 0);
    let p75v = $derived(stats?.p75_us ?? 0);
    let p90v = $derived(stats?.p90_us ?? 0);
    let p99v = $derived(stats?.p99_us ?? 0);
    let clearConfirmText = $derived(
        t('flamegraph.clearRing.confirm').replace('{n}', count(stats?.frame_count ?? 0)),
    );
    // an older bundle can be missing a counter the current build knows about, so fill the gaps
    // rather than trust the shape: one absent key turns the total into NaN.
    let dropped = $derived({ ...NO_DROPS, ...(live ? liveRes?.dropped : importedFrames?.dropped) });

    let dropTotal = $derived(
        dropped.pre_frame_samples +
            dropped.late_samples +
            dropped.orphaned_samples +
            dropped.library_ring_samples,
    );

    // drops that stopped an hour ago are not news, so the badge watches the last half second of
    // polls and goes quiet again once the counters hold still.
    const DROP_WINDOW_POLLS = 32;
    // in milliseconds, not polls: the poll interval moves with frame size now, and the ring
    // counter only steps on the 5s session-meta heartbeat.
    const RING_HOLD_MS = 6000;
    let dropWindow = $state<number[]>([]);
    let lastRing = $state(-1);
    let ringQuietMs = $state(RING_HOLD_MS);
    $effect(() => {
        if (!live) {
            untrack(() => {
                dropWindow = [];
                lastRing = -1;
                ringQuietMs = RING_HOLD_MS;
            });
            return;
        }
        if (!liveRes) return;
        const fast =
            dropped.pre_frame_samples +
            dropped.late_samples +
            dropped.orphaned_samples +
            dropped.transport_lost_batches;
        const ring = dropped.library_ring_samples;
        untrack(() => {
            dropWindow = [...dropWindow, fast].slice(-DROP_WINDOW_POLLS);
            ringQuietMs = lastRing >= 0 && ring > lastRing ? 0 : ringQuietMs + pollMs;
            lastRing = ring;
        });
    });
    // an import is a still picture, so there is no climb to watch: any drops in it are the news.
    let lossy = $derived(
        live
            ? (dropWindow.length > 1 && dropWindow[dropWindow.length - 1] > dropWindow[0]) ||
                  ringQuietMs < RING_HOLD_MS
            : dropTotal > 0,
    );
    let stopwatchFrequency = $derived(
        (live ? liveRes?.stopwatch_frequency : importedFrames?.stopwatch_frequency) ?? 0,
    );
    let names = $derived(
        live
            ? new Map(
                  (sectionsRes.data?.sections ?? []).map((s) => [
                      s.id,
                      {
                          name: sectionLabel(s.name),
                          subsystem: s.subsystem,
                          assembly: s.assembly ?? null,
                      },
                  ]),
              )
            : importedNames,
    );
    let timerResNs = $derived(timerResolutionNs(stopwatchFrequency));
    // the timeline builds this too, but a shared derived keeps the row indices and the bar
    // indices talking about the same array.
    let frameNodes = $derived(frame ? frameTreeNodes(frame, treeCache) : []);
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

<div class="profiler" style="--stage-h: {stageH}px; {chromeStyle}" data-testid="profiler">
    <div class="chrome" bind:clientHeight={chromeRowsH} data-testid="chrome">
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
                    {#each scrubbable as b (b.token)}<option value={b.token}>{b.label}</option
                        >{/each}
                </select>
            </label>
            <label class="filebtn" class:busy={importing}>
                <input type="file" accept=".zip" onchange={openBundle} disabled={importing} />
                {importing ? t('comparison.importing') : t('flamegraph.source.import')}
            </label>
            <span class="readout mono">
                {#snippet spreadTip()}
                    <table class="spread-tip">
                        <tbody>
                            {#each PERCENTILES as p (p.key)}
                                {@const v = stats?.[p.key] ?? 0}
                                <tr>
                                    <td>{t(p.label)}</td>
                                    <td
                                        class="g{gradeFromShare(v / FRAME_BUDGET_US)}"
                                        data-testid="tip-{p.key}">{ns(v * 1000)}</td
                                    >
                                </tr>
                            {/each}
                        </tbody>
                    </table>
                    <p class="spread-foot">
                        {t('flamegraph.tickBudget')} <b>{ns(tickBudgetUs(tps) * 1000)}</b>, {t(
                            'flamegraph.budget',
                        )} <b>{ns(FRAME_BUDGET_US * 1000)}</b>
                    </p>
                    <p class="spread-foot">
                        {t('tip.flamegraph.tickBudget').replace(
                            '{n}',
                            String(speedMultiplier(tps)),
                        )}
                    </p>
                    <p class="spread-foot">{t('tip.flamegraph.budget')}</p>
                {/snippet}
                <!-- no stats yet means no confident green zeros over an empty page -->
                {#if stats !== null}
                    <Tooltip text={t('tip.flamegraph.p50')} tabindex={-1}>
                        <span class="mono"
                            >{t('flamegraph.p50')}
                            <b
                                class="g{gradeFromShare(p50v / FRAME_BUDGET_US)}"
                                data-testid="stat-median_us">{ns(p50v * 1000)}</b
                            ></span
                        >
                    </Tooltip>
                    <Tooltip text={t('tip.flamegraph.p99')} tabindex={-1}>
                        <span class="mono"
                            >{t('flamegraph.p99')}
                            <b
                                class="p99 g{gradeFromShare(p99v / FRAME_BUDGET_US)}"
                                data-testid="stat-p99_us">{ns(p99v * 1000)}</b
                            ></span
                        >
                    </Tooltip>
                    <Tooltip content={spreadTip}>
                        <span
                            class="spread"
                            role="img"
                            aria-label={t('flamegraph.spread.aria')
                                .replace('{p50}', ns(p50v * 1000))
                                .replace('{p99}', ns(p99v * 1000))
                                .replace('{budget}', ns(FRAME_BUDGET_US * 1000))}
                            data-testid="spread-meter"
                        >
                            <span class="rail"></span>
                            <span class="scale-lbl" style="left: {meterPos(tickBudgetUs(tps))}%"
                                >{t('flamegraph.tickBudget')}</span
                            >
                            <span
                                class="scale-mark"
                                style="left: {meterPos(tickBudgetUs(tps))}%"
                                data-testid="tick-budget"
                            ></span>
                            <span class="scale-lbl" style="left: {meterPos(FRAME_BUDGET_US)}%"
                                >{t('flamegraph.budget')}</span
                            >
                            <span
                                class="scale-mark budget"
                                style="left: {meterPos(FRAME_BUDGET_US)}%"
                                data-testid="frame-budget"
                            ></span>
                            <span
                                class="band"
                                style="left: {meterPos(p50v)}%; width: {Math.max(
                                    0.5,
                                    meterPos(p99v) - meterPos(p50v),
                                )}%; background: {meterFill(p50v, p99v)}"
                                data-testid="spread-band"
                            ></span>
                            <span class="band-notch" style="left: {meterPos(p75v)}%"></span>
                            <span class="band-notch" style="left: {meterPos(p90v)}%"></span>
                        </span>
                    </Tooltip>
                {/if}
                {#if peakAllocRate > 0}
                    <Tooltip text={t('flamegraph.allocRate.hint')}>
                        <span
                            class="mono"
                            class:lossy={peakAllocRate > ALLOC_WARN_BPM}
                            data-testid="alloc-rate"
                            >{t('flamegraph.allocRate')} <b>{bytes(peakAllocRate)}/m</b></span
                        >
                    </Tooltip>
                {/if}
            </span>
        </div>

        {#if importError}<p class="import-error" role="alert">{importError}</p>{/if}

        {#if pinNotice !== null}
            <p class="pin-evicted" role="status" data-testid="pin-evicted">
                {pinNotice}
                <button
                    type="button"
                    class="dismiss"
                    aria-label={t('flamegraph.pinEvicted.dismiss')}
                    onclick={() => {
                        clearTimeout(noticeTimer);
                        pinNotice = null;
                    }}
                    data-testid="pin-evicted-dismiss">&times;</button
                >
            </p>
        {/if}

        {#if live}
            <div class="modes">
                <div class="seg" role="group" aria-label={t('flamegraph.mode')}>
                    <button
                        type="button"
                        class={stripMode === 'time' ? 'on' : 'off'}
                        aria-pressed={stripMode === 'time'}
                        onclick={() => (stripMode = 'time')}
                        data-testid="mode-time">{t('flamegraph.mode.time')}</button
                    >
                    <button
                        type="button"
                        class={stripMode === 'alloc' ? 'on' : 'off'}
                        aria-pressed={stripMode === 'alloc'}
                        onclick={() => (stripMode = 'alloc')}
                        data-testid="mode-alloc">{t('flamegraph.mode.alloc')}</button
                    >
                </div>
                <span class="rightpair">
                    <Tooltip text={t('flamegraph.exportFrame')}>
                        <button
                            type="button"
                            class="clearring iconbtn"
                            aria-label={t('flamegraph.exportFrame')}
                            onclick={exportFrame}
                            data-testid="export-frame"><Icon name="exportFrame" size={15} /></button
                        >
                    </Tooltip>
                    <Tooltip text={t('flamegraph.exportRing')}>
                        <button
                            type="button"
                            class="clearring iconbtn"
                            aria-label={t('flamegraph.exportRing')}
                            onclick={() => void exportRing()}
                            data-testid="export-ring"><Icon name="exportRing" size={15} /></button
                        >
                    </Tooltip>
                    <Tooltip text={t('flamegraph.exportTimeline')}>
                        <button
                            type="button"
                            class="clearring iconbtn"
                            aria-label={t('flamegraph.exportTimeline')}
                            onclick={() => void exportTimeline()}
                            data-testid="export-timeline"
                            ><Icon name="exportTimeline" size={15} /></button
                        >
                    </Tooltip>
                    <Tooltip text={t('flamegraph.resetView')}>
                        <button
                            type="button"
                            class="clearring iconbtn"
                            aria-label={t('flamegraph.resetView')}
                            onclick={() => {
                                resume();
                                timeline?.resetView();
                            }}
                            data-testid="reset-view"><Icon name="fit" size={15} /></button
                        >
                    </Tooltip>
                    <span class="sep" aria-hidden="true"></span>
                    <Tooltip
                        text={t('tip.flamegraph.newSession')}
                        childFocusable={!startingSession}
                    >
                        <button
                            type="button"
                            class="clearring destructive iconbtn"
                            aria-label={t('flamegraph.newSession')}
                            onclick={() => (askingNewSession = true)}
                            disabled={startingSession}
                            data-testid="new-session"
                            ><Icon name="restart" size={15} /><span class="btnlabel"
                                >{t('flamegraph.newSession')}</span
                            ></button
                        >
                    </Tooltip>
                    <Tooltip
                        text={confirmingClear ? clearConfirmText : t('tip.flamegraph.clearRing')}
                        childFocusable={!clearing}
                    >
                        <button
                            type="button"
                            class="clearring destructive iconbtn"
                            class:armed={confirmingClear}
                            aria-label={confirmingClear
                                ? clearConfirmText
                                : t('flamegraph.clearRing')}
                            onclick={askClearRing}
                            onblur={() => (confirmingClear = false)}
                            disabled={clearing}
                            data-testid="clear-ring"
                            ><Icon name="trash" size={15} /><span class="btnlabel"
                                >{confirmingClear
                                    ? clearConfirmText
                                    : t('flamegraph.clearRing')}</span
                            ></button
                        >
                    </Tooltip>
                    <!-- swapping aria-label on a focused button announces nothing, so the arm goes here -->
                    <span
                        class="sr-only"
                        role="status"
                        aria-live="polite"
                        data-testid="clear-ring-status"
                        >{confirmingClear ? clearConfirmText : ''}</span
                    >
                </span>
            </div>
        {/if}

        {#if live && stripBars.length > 0}
            <FrameStrip
                ordinals={strip.ordinals}
                durationsUs={strip.durations_us}
                allocBytes={strip.alloc_bytes ?? []}
                mode={stripMode}
                cutOrdinals={shownCuts}
                {gcOrdinals}
                slots={ringCapacity ?? DEFAULT_STRIP_SLOTS}
                selectedOrdinal={pinnedOrdinal ?? liveOrdinal}
                selectedRange={pinnedRange}
                onSelect={pickStripFrame}
                onSelectRange={pauseRange}
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
    </div>

    {#snippet nodesTip()}
        <span class="tipline"
            >{t('flamegraph.dropped.orphans')}
            <b class:warn={orphanCount > 0} data-testid="drop-orphans">{count(orphanCount)}</b
            ></span
        >
    {/snippet}

    {#snippet captureTip()}
        <p class="tiplead">{t('flamegraph.lossy.hint')}</p>
        <span class="tipline"
            >{t('flamegraph.dropped.late')}
            <b class:warn={dropped.late_samples > 0} data-testid="drop-late"
                >{count(dropped.late_samples)}</b
            ></span
        >
        <span class="tipline"
            >{t('flamegraph.dropped.orphaned')}
            <b class:warn={dropped.orphaned_samples > 0} data-testid="drop-orphaned"
                >{count(dropped.orphaned_samples)}</b
            ></span
        >
        <span class="tipline"
            >{t('flamegraph.dropped.transport')}
            <b class:warn={dropped.transport_lost_batches > 0} data-testid="drop-transport"
                >{count(dropped.transport_lost_batches)}</b
            ></span
        >
        <span class="tipline"
            >{t('flamegraph.dropped.ring')}
            <b class:warn={dropped.library_ring_samples > 0} data-testid="drop-ring"
                >{count(dropped.library_ring_samples)}</b
            ></span
        >
        <span class="tipline"
            >{t('flamegraph.dropped.preframe')}
            <b data-testid="drop-preframe">{count(dropped.pre_frame_samples)}</b></span
        >
        <p class="tiphint">{t('flamegraph.captureHealth.remedy')}</p>
    {/snippet}

    <DataState
        state={live ? (framesRes?.state ?? 'loading') : 'ok'}
        error={live ? (framesRes?.error ?? '') : ''}
        empty={frame === null}
        emptyTitle={t('flamegraph.empty')}
        emptyHint={t('flamegraph.empty.hint')}
        onretry={() => void framesRes?.refresh()}
    >
        <p class="avg mono" bind:clientHeight={avgH} data-testid="frame-drops">
            {#if lossy}
                <Tooltip content={captureTip} tabindex={-1}>
                    <button
                        type="button"
                        class="lossy chip"
                        data-testid="lossy-badge"
                        onclick={() => (uiSignals.settingsOpen = true)}
                        >{t('flamegraph.lossy')}
                        <b data-testid="lossy-count">{count(dropTotal)}</b></button
                    >
                </Tooltip>
            {/if}
            <span class="stats">
                <span class="cell"
                    ><Tooltip content={nodesTip} tabindex={-1}
                        ><span>{t('flamegraph.nodes')} <b>{frame?.node_count ?? 0}</b></span
                        ></Tooltip
                    ></span
                ><span class="cell"
                    >{t('flamegraph.duration')}
                    <b
                        class:warn={budgetSeverity(frame?.duration_us ?? 0) === 1}
                        data-testid="frame-duration">{ns((frame?.duration_us ?? 0) * 1000)}</b
                    ></span
                ></span
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
                <!-- scope, filter, tally and steppers only mean something mid-search -->
                {#if sectionSearch.query.trim() !== ''}
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
                            ><b class:none={sectionSearch.nodeCount === 0}
                                >{sectionSearch.nodeCount}</b
                            >
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
                {/if}
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

        <ul class="legend" data-testid="subsystem-legend">
            {#each LEGEND as entry (entry.name)}
                <li data-testid="legend-{entry.name}">
                    <i class="swatch" style="background: {entry.swatch}"></i>
                    {t(`flamegraph.legend.${entry.name}`)}
                </li>
            {/each}
        </ul>

        <div
            class="stage"
            class:split={treeOpen}
            class:busy={rangeInFlight}
            class:dragover={draggingBundle}
            aria-busy={rangeInFlight}
            bind:clientHeight={stageH}
            ondragover={stageDragOver}
            ondragleave={() => (draggingBundle = false)}
            ondrop={stageDrop}
            data-testid="stage"
        >
            {#if draggingBundle}
                <span class="stage-busy mono" data-testid="stage-drop-hint"
                    >{t('flamegraph.source.dropHint')}</span
                >
            {/if}
            {#if rangeInFlight}
                <span class="stage-busy mono" data-testid="stage-busy-note"
                    >{t('flamegraph.rangeBusy')}</span
                >
            {/if}
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
                        <small class="mono">{count(stats.calls)} | {ns(stats.busyNs)}</small>
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
                    selectedRange={pinnedRange}
                    onViewChange={(v) => void refineView(v)}
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
            groupDisabledHint={live ? undefined : t('tree.mod.bundleHint')}
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
        --gut: calc(112px * var(--f));
        display: flex;
        flex-direction: column;
        min-height: 0;
        font-size: var(--f-body);
        padding-bottom: 16px;
    }
    /* one measured box so the stage knows how tall the chrome rows actually wrapped to */
    .chrome {
        display: flex;
        flex-direction: column;
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
        flex: none;
        color: var(--warn);
        border: 1px solid var(--border);
        border-radius: 3px;
        padding: 0 6px;
    }
    .lossy.chip {
        background: none;
        font: inherit;
        cursor: pointer;
    }
    .lossy.chip:hover {
        background: color-mix(in srgb, var(--warn) 12%, transparent);
    }
    .tiphint {
        margin: var(--s-1) 0 0;
        color: var(--text-faint);
        font-size: 0.7rem;
        max-width: 36ch;
    }
    .tiplead {
        margin: 0 0 var(--s-1);
        max-width: 36ch;
    }
    .tipline {
        display: block;
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
        /* stepping frames is the most-hammered control here, so it gets a 28px target */
        padding: 4px 8px;
        min-width: 28px;
        min-height: 28px;
    }
    .bar > button:hover {
        border-color: var(--text-dim);
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
        border-color: var(--text-dim);
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
    /* healthy is silent: only grades 2+ get a hue, so amber means something. */
    .readout b.g0,
    .readout b.g1 {
        color: var(--text);
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
    .readout b.p99 {
        font-weight: 600;
    }
    .readout {
        display: inline-flex;
        align-items: center;
        gap: var(--s-3);
    }
    .spread {
        position: relative;
        display: inline-block;
        width: 170px;
        height: 16px;
        cursor: help;
    }
    .spread .rail {
        position: absolute;
        left: 0;
        right: 0;
        top: 7px;
        height: 2px;
        background: var(--border);
    }
    .spread .band {
        position: absolute;
        top: 5px;
        height: 6px;
        border-radius: 2px;
    }
    .spread .band-notch {
        position: absolute;
        top: 6px;
        width: 1px;
        height: 4px;
        background: rgba(11, 14, 20, 0.55);
    }
    .spread .scale-mark {
        position: absolute;
        top: 3px;
        width: 1px;
        height: 10px;
        background: var(--text-ghost);
    }
    .spread .scale-mark.budget {
        background: var(--grade-4);
        opacity: 0.75;
    }
    .spread .scale-lbl {
        position: absolute;
        top: -12px;
        font-size: var(--f-tiny);
        color: var(--text-faint);
        transform: translateX(-50%);
    }
    .spread-tip {
        border-collapse: collapse;
    }
    .spread-tip td {
        padding: 0 0 0 var(--s-4);
        text-align: right;
        font-family: var(--font-mono);
    }
    .spread-tip td:first-child {
        padding: 0;
        color: var(--text-faint);
        text-align: left;
    }
    .spread-tip td.g0,
    .spread-tip td.g1 {
        color: var(--text);
    }
    .spread-tip td.g2 {
        color: var(--grade-2);
    }
    .spread-tip td.g3 {
        color: var(--grade-3);
    }
    .spread-tip td.g4 {
        color: var(--grade-4);
    }
    .spread-foot {
        margin: var(--s-1) 0 0;
        color: var(--text-faint);
        font-size: 0.7rem;
        max-width: 34ch;
    }

    .modes {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        gap: var(--s-2);
        padding: 6px 12px;
        border-bottom: 1px solid var(--border-soft);
    }
    .rightpair {
        display: inline-flex;
        align-items: center;
        flex-wrap: wrap;
        gap: var(--s-2);
        margin-left: auto;
    }
    .sep {
        width: 1px;
        height: 18px;
        background: var(--border);
    }
    .clearring {
        font: inherit;
        font-size: var(--f-ui);
        color: var(--text-dim);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 10px;
        cursor: pointer;
    }
    .clearring:hover:not(:disabled) {
        color: var(--text);
        border-color: var(--text-dim);
    }
    .clearring.armed {
        color: var(--bad);
        border-color: var(--bad);
    }
    .iconbtn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: 5px;
        min-width: 28px;
        min-height: 28px;
        padding: 5px;
    }
    .iconbtn .btnlabel {
        padding-right: 2px;
    }
    .clearring:disabled {
        color: var(--text-ghost);
        cursor: default;
    }
    /* rests dim so danger does not shout all session; hover and arming bring the full red. */
    .destructive {
        color: color-mix(in srgb, var(--bad) 55%, var(--text-dim));
        border-color: color-mix(in srgb, var(--bad) 25%, var(--border-strong));
    }
    .destructive:hover:not(:disabled) {
        color: var(--bad);
        background: color-mix(in srgb, var(--bad) 14%, var(--bg-surface));
        border-color: var(--bad);
    }
    .destructive.armed {
        color: var(--text);
        background: var(--bad-deep);
        border-color: var(--bad-deep);
    }
    .destructive:disabled {
        color: color-mix(in srgb, var(--bad) 50%, var(--bg-surface));
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
    .import-error {
        color: var(--bad);
        font-size: var(--f-ui);
        padding: 6px 12px;
        border-bottom: 1px solid var(--border-soft);
    }
    .pin-evicted {
        display: flex;
        align-items: center;
        gap: 8px;
        color: var(--text-dim);
        font-size: var(--f-ui);
        padding: 6px 12px;
        border-bottom: 1px solid var(--border-soft);
    }
    .pin-evicted .dismiss {
        background: none;
        border: none;
        color: inherit;
        cursor: pointer;
        font-size: var(--f-ui);
        line-height: 1;
        padding: 0 4px;
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
        flex-wrap: wrap;
        gap: 8px 16px;
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
        border-color: var(--text-dim);
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
        content: ' | ';
        color: var(--text-ghost);
    }

    .legend {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        margin-top: var(--s-2);
        gap: var(--s-3);
        min-height: 22px;
        margin: 0;
        padding: 0 12px;
        list-style: none;
        font-size: var(--f-tiny);
        color: var(--text-faint);
    }
    .legend li {
        display: flex;
        align-items: center;
        gap: var(--s-1);
        white-space: nowrap;
    }
    .legend .swatch {
        width: 8px;
        height: 8px;
        border-radius: 2px;
    }

    .stage {
        position: relative;
        display: grid;
        grid-template-columns: var(--gut) 1fr;
        background: var(--bg-void);
        border-bottom: 1px solid var(--border);
        /* with no drawer open the stage owns the page; in split mode it hugs its lanes
           so the drawer can claim the slack */
        height: calc(100vh - var(--chrome-measured, var(--chrome-h)));
        min-height: 160px;
        overflow: auto;
        /* drag pans instead; bars just eat width next to the flame. */
        scrollbar-width: none;
    }
    .stage::-webkit-scrollbar {
        display: none;
    }
    .stage.busy {
        opacity: 0.6;
        cursor: progress;
    }
    .stage.dragover {
        outline: 2px dashed var(--cyan);
        outline-offset: -2px;
    }
    .stage-busy {
        position: absolute;
        top: var(--s-2);
        left: 50%;
        transform: translateX(-50%);
        z-index: 5;
        padding: 2px 10px;
        font-size: var(--f-small);
        color: var(--text-dim);
        background: var(--bg-elev);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
    }
    .stage.split {
        height: auto;
        max-height: calc(100vh - var(--drawer-h) - var(--chrome-measured, var(--chrome-h)));
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
