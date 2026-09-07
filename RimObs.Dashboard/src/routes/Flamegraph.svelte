<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api, ApiError } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import type { FrameResponse, FrameStripData, BundleFramesResponse } from '../lib/frameTree';
    import DataState from '../lib/components/DataState.svelte';
    import StatCard from '../lib/components/StatCard.svelte';
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

    const sectionsRes = new Resource(() => api.allSections(), 10000);
    onMount(() => sectionsRes.start());
    onDestroy(() => sectionsRes.stop());

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

<div class="page">
    <p class="hint">{t('flamegraph.hint')}</p>

    <div class="controls">
        <label class="picker">
            <span class="dim">{t('flamegraph.source')}</span>
            <select bind:value={source}>
                <option value={LIVE}>{t('flamegraph.source.live')}</option>
                {#if imported}
                    <option value={imported.token}>{imported.label}</option>
                {/if}
            </select>
        </label>

        <label class="picker">
            <span class="dim">{t('flamegraph.source.import')}</span>
            <input type="file" accept=".zip" onchange={openBundle} />
        </label>

        {#if live}
            <div class="transport">
                <button type="button" onclick={togglePause} data-testid="pause">
                    {paused ? t('flamegraph.resume') : t('flamegraph.pause')}
                </button>
                <button
                    type="button"
                    onclick={() => step(-1)}
                    aria-label={t('flamegraph.older')}
                    data-testid="step-older">&lsaquo;</button
                >
                <button
                    type="button"
                    onclick={() => step(1)}
                    aria-label={t('flamegraph.newer')}
                    data-testid="step-newer">&rsaquo;</button
                >
                <button type="button" onclick={jumpToNewest} data-testid="jump-newest">
                    {t('flamegraph.newest')}
                </button>
                {#if paused}<span class="paused" data-testid="paused-badge"
                        >{t('flamegraph.paused')}</span
                    >{/if}
            </div>

            <label class="picker">
                <span class="dim">{t('flamegraph.rate')}</span>
                <select bind:value={rateMs}>
                    {#each RATES as r (r.ms)}
                        <option value={r.ms}>{r.label}</option>
                    {/each}
                </select>
            </label>
        {/if}
    </div>

    {#if importError}
        <p class="import-error" role="alert">{importError}</p>
    {/if}

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
        onretry={() => framesRes?.refresh()}
    >
        <div class="stats">
            <StatCard
                icon="gauge"
                label={t('flamegraph.ordinal')}
                value={String(frame?.capture_ordinal ?? 0)}
            />
            <StatCard
                icon="metric"
                label={t('flamegraph.duration')}
                value={ns((frame?.duration_us ?? 0) * 1000)}
                tone={budgetSeverity(frame?.duration_us ?? 0) === 1 ? 'warn' : undefined}
            />
            <StatCard
                icon="tree"
                label={t('flamegraph.nodes')}
                value={count(frame?.node_count ?? 0)}
            />
            <StatCard
                icon="stack"
                label={t('flamegraph.median')}
                value={ns((stats?.median_us ?? 0) * 1000)}
            />
            <StatCard
                icon="stack"
                label={t('flamegraph.p99')}
                value={ns((stats?.p99_us ?? 0) * 1000)}
            />
        </div>

        <FrameTimeline bind:this={timeline} {frame} {names} bind:orphanCount bind:selectedNode />
        <CallTreePanel
            nodes={treeNodes}
            {names}
            {selectedNode}
            onSelect={(i) => {
                selectedNode = i;
                timeline?.focusNode(i);
            }}
        />
        <p class="hint">{t('flamegraph.keys')}</p>
        {#if live}<p class="hint">{t('flamegraph.keys.transport')}</p>{/if}

        <div class="drops" data-testid="frame-drops">
            <span class="drop">
                <span class="drop-label">{t('flamegraph.dropped.late')}</span>
                <span data-testid="drop-late" class="mono" class:warn={dropped.late_samples > 0}
                    >{count(dropped.late_samples)}</span
                >
            </span>
            <span class="drop">
                <span class="drop-label">{t('flamegraph.dropped.preframe')}</span>
                <span data-testid="drop-preframe" class="mono"
                    >{count(dropped.pre_frame_samples)}</span
                >
            </span>
            <span class="drop">
                <span class="drop-label">{t('flamegraph.dropped.orphans')}</span>
                <span data-testid="drop-orphans" class="mono" class:warn={orphanCount > 0}
                    >{count(orphanCount)}</span
                >
            </span>
        </div>
        <p class="drops-hint">{t('flamegraph.dropped.hint')}</p>

        <p class="overhead-line mono" data-testid="frame-overhead">
            {#if deltaUs !== null}<span
                    class:warn={deltaSeverity(deltaUs) === 1}
                    class:cool={deltaSeverity(deltaUs) === -1}>Δ {deltaText(deltaUs)}</span
                >{overheadLine ? ' · ' : ''}{/if}{overheadLine}
        </p>
        <p class="drops-hint">{t('flamegraph.overhead.hint')}</p>
    </DataState>
</div>

<style>
    .page {
        display: flex;
        flex-direction: column;
        gap: var(--s-3);
    }
    .hint {
        margin: 0;
        font-size: 0.78rem;
        color: var(--text-faint);
    }
    .controls {
        display: flex;
        align-items: center;
        gap: var(--s-4);
    }
    .picker {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        font-size: 0.8rem;
    }
    .dim {
        color: var(--text-faint);
    }
    select {
        background: var(--bg-surface);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: var(--s-1) var(--s-2);
        font-family: var(--font-ui);
        font-size: 0.8rem;
    }
    .stats {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
        gap: var(--s-4);
        margin-bottom: var(--s-4);
    }
    .drops {
        display: flex;
        gap: var(--s-6);
        flex-wrap: wrap;
        margin-top: var(--s-4);
        padding: var(--s-3) var(--s-4);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        background: var(--bg-surface);
    }
    .drop {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        font-size: 0.82rem;
    }
    .drop-label {
        color: var(--text-faint);
    }
    .warn {
        color: var(--warn);
    }
    .cool {
        color: var(--good);
    }
    .overhead-line {
        margin: var(--s-4) 0 0;
        font-size: 0.82rem;
        color: var(--text-faint);
    }
    .drops-hint {
        margin: var(--s-2) 0 0;
        font-size: 0.74rem;
        color: var(--text-faint);
    }
    .scrub {
        display: flex;
        align-items: center;
        gap: var(--s-3);
        font-size: 0.8rem;
    }
    .scrub input[type='range'] {
        flex: 1;
        accent-color: var(--cyan);
    }
    .transport {
        display: flex;
        align-items: center;
        gap: 0.25rem;
    }
    .transport button {
        font: inherit;
        color: var(--text);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: 3px;
        padding: 0.15rem 0.5rem;
        cursor: pointer;
    }
    .paused {
        color: var(--warn);
        font-size: 0.75rem;
        margin-left: 0.25rem;
    }
    .import-error {
        margin: 0;
        font-size: 0.78rem;
        color: var(--warn);
    }
</style>
