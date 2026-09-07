<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api, ApiError } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import type { FrameResponse, FrameStripData, BundleFramesResponse } from '../lib/frameTree';
    import DataState from '../lib/components/DataState.svelte';
    import FrameTimeline from '../lib/components/FrameTimeline.svelte';
    import FrameStrip from '../lib/components/FrameStrip.svelte';
    import CallTreePanel from '../lib/components/CallTreePanel.svelte';
    import { buildFrameTree } from '../lib/frameTree';
    import { buildBars, stepOrdinal } from '../lib/frameStrip';
    import { ns, count } from '../lib/format';
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
        TICK_BUDGET_US,
    } from '../lib/frameCost';
    import { t } from '../lib/i18n';

    const RATES = [
        { ms: 100, label: '10/s' },
        { ms: 250, label: '4/s' },
        { ms: 500, label: '2/s' },
        { ms: 1000, label: '1/s' },
    ];

    const LIVE = 'live';
    const NO_DROPS = { pre_frame_samples: 0, late_samples: 0 };

    let rateMs = $state(250);
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

    // rides along on /frames/latest, so the strip costs no second request. it keeps filling
    // while paused, which is how you find the spike you paused to go looking at.
    let strip = $derived(framesRes?.data?.strip ?? NO_STRIP);
    let stripBars = $derived(buildBars(strip.ordinals, strip.durations_us));

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
        paused = true;
        void showOrdinal(next);
    }

    function jumpToNewest(): void {
        paused = false;
        void showOrdinal(null);
    }

    function togglePause(): void {
        paused = !paused;
        if (!paused) void showOrdinal(null);
        else if (pinnedOrdinal === null && liveOrdinal !== null) void showOrdinal(liveOrdinal);
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

    const sectionsRes = new Resource(() => api.allSections(), 10000);
    onMount(() => {
        sectionsRes.start();
        baselineRes.start();
    });
    onDestroy(() => {
        sectionsRes.stop();
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
    let liveRes = $derived(pinned ? pinnedRes : (framesRes?.data ?? null));
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
    let treeNodes = $derived(frame ? buildFrameTree(frame).nodes : []);

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
        <label class="picker">
            <span class="dim">{t('flamegraph.source.import')}</span>
            <input type="file" accept=".zip" onchange={openBundle} />
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
            {t('flamegraph.median')} <b>{ns((stats?.median_us ?? 0) * 1000)}</b>
            &middot; {t('flamegraph.p99')} <b>{ns((stats?.p99_us ?? 0) * 1000)}</b>
            &middot; {t('flamegraph.budget')} <b>{ns(TICK_BUDGET_US * 1000)}</b>
        </span>
    </div>

    {#if importError}<p class="import-error" role="alert">{importError}</p>{/if}

    {#if live && stripBars.length > 0}
        <FrameStrip
            ordinals={strip.ordinals}
            durationsUs={strip.durations_us}
            selectedOrdinal={pinnedOrdinal ?? liveOrdinal}
            onSelect={(o) => {
                paused = true;
                void showOrdinal(o);
            }}
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
            {selectedNode}
            frameDurationUs={frame?.duration_us ?? 0}
            {baselineUs}
            onSelect={(i) => {
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
        padding: var(--s-2) var(--s-2);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        margin-bottom: var(--s-2);
    }
    .bar button {
        font: inherit;
        font-size: var(--f-ui);
        color: var(--text);
        background: var(--bg-surface-2);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 9px;
        cursor: pointer;
    }
    .bar button.icon {
        font-family: var(--font-mono);
        padding: 3px 7px;
    }
    .bar button:hover {
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
    .picker select,
    .picker input[type='file'] {
        font: inherit;
        font-size: var(--f-ui);
        color: var(--text);
        background: var(--bg-surface-2);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 2px 6px;
        max-width: 150px;
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

    .import-error {
        color: var(--bad);
        font-size: var(--f-ui);
        padding: var(--s-2) 0;
    }
    .scrub {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-2);
        font-size: var(--f-ui);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        margin-bottom: var(--s-2);
    }
    .scrub input {
        flex: 1;
    }

    .avg {
        padding: 5px 8px;
        font-size: var(--f-small);
        color: var(--text-dim);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        margin-bottom: var(--s-2);
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
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        overflow: hidden;
    }
    .gutter {
        border-right: 1px solid var(--border);
        padding: calc(24px * var(--f)) var(--s-2) var(--s-2) var(--s-2);
        font-size: var(--f-ui);
        background: var(--bg-surface);
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
        padding: var(--s-1) var(--s-2);
    }
    .foot:first-of-type {
        border-top: 1px solid var(--border);
        margin-top: var(--s-2);
        padding-top: var(--s-2);
    }
</style>
