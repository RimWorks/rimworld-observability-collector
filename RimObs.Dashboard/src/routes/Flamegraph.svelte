<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api, ApiError, type StatusResponse, type HotspotsResponse } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import type { FrameResponse, FrameStripData, BundleFramesResponse } from '../lib/frameTree';
    import DataState from '../lib/components/DataState.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import FrameTimeline from '../lib/components/FrameTimeline.svelte';
    import FrameStrip from '../lib/components/FrameStrip.svelte';
    import CallTreePanel from '../lib/components/CallTreePanel.svelte';
    import { buildFrameTree } from '../lib/frameTree';
    import { flattenCallNodes, sessionTotalUs } from '../lib/sessionTree';
    import type { CallTreeResponse } from '../lib/api';
    import { buildBars, stepOrdinal } from '../lib/frameStrip';
    import { recordCut, visibleCuts } from '../lib/frameCuts';
    import { ns, count, gradeFromShare } from '../lib/format';
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

    // the whole spread against the frame budget, coloured by how much of it each one eats
    const PERCENTILES = [
        { key: 'median_us', label: 'flamegraph.p50' },
        { key: 'p75_us', label: 'flamegraph.p75' },
        { key: 'p90_us', label: 'flamegraph.p90' },
        { key: 'p99_us', label: 'flamegraph.p99' },
    ] as const;

    const RATES = [
        { ms: 16, label: '60/s' },
        { ms: 25, label: '40/s' },
        { ms: 50, label: '20/s' },
        { ms: 100, label: '10/s' },
        { ms: 250, label: '4/s' },
        { ms: 500, label: '2/s' },
        { ms: 1000, label: '1/s' },
    ];

    const LIVE = 'live';
    const NO_DROPS = { pre_frame_samples: 0, late_samples: 0 };

    let rateMs = $state(16);
    let framesRes = $state<Resource<FrameResponse> | null>(null);
    let source = $state(LIVE);
    let imported = $state<{ token: string; label: string } | null>(null);
    let importedFrames = $state<BundleFramesResponse | null>(null);
    let importedNames = $state(new Map<number, { name: string; subsystem: string | null }>());
    let frameIndex = $state(0);
    let importError = $state('');
    let paused = $state(false);
    // set while paused or stepping. null means follow the newest frame.
    let pinnedOrdinal = $state<number | null>(null);
    let pinnedRes = $state<FrameResponse | null>(null);
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
        const res = new Resource<FrameResponse>(() => api.frames(), rateMs);
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
        pinnedOrdinal = ordinal;
        if (ordinal === null) {
            pinnedRes = null;
            return;
        }
        try {
            pinnedRes = await api.frameAt(ordinal);
        } catch {
            // the ring evicted it between the click and the fetch. fall back to live.
            pinnedOrdinal = null;
            pinnedRes = null;
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

    const sectionsRes = new Resource(() => api.allSections(), 10000);
    let treeScope = $state<'frame' | 'session'>('frame');
    // the session tree follows the same rate control as the frame poll, so it needs a fresh
    // Resource whenever that rate changes. it costs about the same as one /frames/latest.
    let sessionTreeRes = $state<Resource<CallTreeResponse> | null>(null);
    $effect(() => {
        const res = new Resource<CallTreeResponse>(() => api.callTree(12, 24), rateMs);
        sessionTreeRes = res;
        res.start();
        return () => res.stop();
    });
    onMount(() => {
        sectionsRes.start();
        hotspotsRes.start();
        statusRes.start();
        baselineRes.start();
    });
    onDestroy(() => {
        sectionsRes.stop();
        hotspotsRes.stop();
        statusRes.stop();
        baselineRes.stop();
    });

    async function openBundle(e: Event) {
        const input = e.currentTarget as HTMLInputElement;
        const file = input.files?.[0];
        if (!file) return;
        importError = '';
        // holds an import nobody owns yet. cleared once it is handed to `imported`, so the
        // finally deletes it on every path that bails, including a throw after the upload won.
        let orphan = '';
        try {
            const res = await api.importBundle(file);
            orphan = res.token;
            if (!res.contents.includes('frames.json')) {
                importError = t('flamegraph.source.noFrames');
                return;
            }
            const [frames, hotspots] = await Promise.all([
                api.importedFrames(res.token),
                api.importedHotspots(res.token),
            ]);
            if (frames.frames.length === 0) {
                importError = t('flamegraph.source.emptyFrames');
                return;
            }
            importedFrames = frames;
            importedNames = new Map(
                hotspots.hotspots.map((h) => [h.id, { name: h.name, subsystem: h.subsystem }]),
            );
            const previous = imported?.token;
            imported = { token: res.token, label: String(res.manifest.session_id ?? file.name) };
            orphan = '';
            if (previous) void api.deleteImport(previous);
            frameIndex = Math.max(0, frames.frames.length - 1);
            source = res.token;
        } catch (err) {
            importError = err instanceof ApiError ? err.message : String(err);
        } finally {
            if (orphan) void api.deleteImport(orphan);
            input.value = '';
        }
    }

    let orphanCount = $state(0);
    let selectedNode = $state(-1);
    let timeline = $state<{ focusNode: (i: number) => void } | null>(null);
    // while pinned the page reads a specific ordinal instead of whatever the poller last saw.
    let liveRes = $derived(
        pinned ? (pinnedRes ?? framesRes?.data ?? null) : (framesRes?.data ?? null),
    );
    let liveOrdinal = $derived(framesRes?.data?.frame?.capture_ordinal ?? null);
    let frame = $derived(
        live ? (liveRes?.frame ?? null) : (importedFrames?.frames[frameIndex] ?? null),
    );
    let stats = $derived(live ? (liveRes?.stats ?? null) : (importedFrames?.stats ?? null));
    let dropped = $derived((live ? liveRes?.dropped : importedFrames?.dropped) ?? NO_DROPS);
    let stopwatchFrequency = $derived(
        (live ? liveRes?.stopwatch_frequency : importedFrames?.stopwatch_frequency) ?? 0,
    );
    let names = $derived(
        live
            ? new Map(
                  (sectionsRes.data?.sections ?? []).map((s) => [
                      s.id,
                      { name: s.name, subsystem: s.subsystem },
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

    let overheadLine = $derived.by(() => {
        const parts: string[] = [];
        if (PER_SAMPLE_OVERHEAD_NS > 0) {
            const pctSuffix =
                overhead.percent > 0
                    ? ` (~${percent2(overhead.percent)} ${t('flamegraph.overhead.offrame')})`
                    : '';
            parts.push(`${t('flamegraph.overhead')} ${nsPerScopeText()} ns/scope${pctSuffix}`);
        }
        if (timerResNs > 0) {
            parts.push(`${t('flamegraph.timerres')} ${timerResText(timerResNs)} ns`);
        }
        return parts.join(' · ');
    });
</script>

<svelte:window onkeydown={handleTransportKey} />

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
                {#if imported}<option value={imported.token}>{imported.label}</option>{/if}
            </select>
        </label>
        <label class="filebtn">
            <input type="file" accept=".zip" onchange={openBundle} />
            {t('flamegraph.source.import')}
        </label>
        {#if live}
            <label class="picker">
                <span class="dim">{t('flamegraph.rate')}</span>
                <select bind:value={rateMs}>
                    {#each RATES as r (r.ms)}<option value={r.ms}>{r.label}</option>{/each}
                </select>
            </label>
        {/if}

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
        </div>
    {/if}

    {#if live && stripBars.length > 0}
        <FrameStrip
            ordinals={strip.ordinals}
            durationsUs={strip.durations_us}
            cutOrdinals={shownCuts}
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
            <span class="cell">{t('flamegraph.nodes')} <b>{frame?.node_count ?? 0}</b></span><span
                class="cell"
                >{t('flamegraph.duration')}
                <b
                    class:warn={budgetSeverity(frame?.duration_us ?? 0) === 1}
                    data-testid="frame-duration">{ns((frame?.duration_us ?? 0) * 1000)}</b
                ></span
            ><span class="cell"
                >{t('flamegraph.median')} <b>{ns((stats?.median_us ?? 0) * 1000)}</b></span
            ><span class="cell">{t('flamegraph.p99')} <b>{ns((stats?.p99_us ?? 0) * 1000)}</b></span
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
                <b class:warn={orphanCount > 0} data-testid="drop-orphans">{count(orphanCount)}</b
                ></span
            >
        </p>

        <div class="stage">
            <div class="gutter">
                <div class="lane">GC &mdash;</div>
                <div class="lane main">
                    MainThread
                    <small class="mono">{ns((frame?.duration_us ?? 0) * 1000)}</small>
                </div>
            </div>
            <div class="canvas">
                <FrameTimeline
                    bind:this={timeline}
                    {frame}
                    {names}
                    bind:orphanCount
                    bind:selectedNode
                />
            </div>
        </div>

        <CallTreePanel
            nodes={treeNodes}
            {names}
            bind:scope={treeScope}
            percentiles={treeScope === 'session' ? percentiles : undefined}
            selectedNode={treeScope === 'session' ? -1 : selectedNode}
            frameDurationUs={treeScope === 'session'
                ? sessionTotalUs(sessionRoots)
                : (frame?.duration_us ?? 0)}
            baselineUs={treeScope === 'session' ? NO_BASELINE : baselineUs}
            onSelect={(i) => {
                if (treeScope === 'session') return;
                selectedNode = i;
                timeline?.focusNode(i);
            }}
        />
    </DataState>

    <p class="foot mono">
        {t('flamegraph.ringsize')}
        {count(stats?.frame_count ?? 0)} &middot; {t('flamegraph.keys')} &middot; {t(
            'flamegraph.keys.transport',
        )}
    </p>
    <p class="foot mono" data-testid="frame-overhead">
        {#if deltaUs !== null}<span
                class="delta"
                class:warn={deltaSeverity(deltaUs) === 1}
                class:cool={deltaSeverity(deltaUs) === -1}>&Delta; {deltaText(deltaUs)}</span
            >{overheadLine ? ' · ' : ''}{/if}{overheadLine}
    </p>
</div>

<style>
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
        padding: 6px 12px;
        border-bottom: 1px solid var(--border-soft);
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
        padding: 6px 12px;
        font-size: var(--f-small);
        color: var(--text-dim);
        border-bottom: 1px solid var(--border-soft);
    }
    .avg b {
        color: var(--text);
        font-weight: 500;
    }
    .cell + .cell::before {
        content: ' · ';
        color: var(--border-strong);
    }

    .stage {
        display: grid;
        grid-template-columns: var(--gut) 1fr;
        background: var(--bg-void);
        border-bottom: 1px solid var(--border);
        height: 600px;
        overflow: auto;
        resize: vertical;
    }
    .gutter {
        border-right: 1px solid var(--border);
        padding: calc(24px * var(--f)) var(--s-2) var(--s-2) var(--s-2);
        font-size: var(--f-ui);
        background: var(--bg-base);
    }
    .lane {
        color: var(--text-dim);
        padding: 2px 0;
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
    .foot .delta {
        display: inline-block;
        width: 90px;
    }
    .foot:first-of-type {
        border-top: 1px solid var(--border);
        margin-top: var(--s-2);
        padding-top: var(--s-2);
    }
</style>
