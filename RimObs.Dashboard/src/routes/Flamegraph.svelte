<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import type { FrameResponse } from '../lib/frameTree';
    import DataState from '../lib/components/DataState.svelte';
    import StatCard from '../lib/components/StatCard.svelte';
    import FrameTimeline from '../lib/components/FrameTimeline.svelte';
    import { ns, count } from '../lib/format';
    import { t } from '../lib/i18n';

    const RATES = [
        { ms: 100, label: '10/s' },
        { ms: 250, label: '4/s' },
        { ms: 500, label: '2/s' },
        { ms: 1000, label: '1/s' },
    ];

    let rateMs = $state(250);
    let framesRes = $state<Resource<FrameResponse> | null>(null);

    // Resource takes its interval at construction, so a rate change means a fresh
    // instance; the cleanup return stops the old poller's timer.
    $effect(() => {
        const res = new Resource<FrameResponse>(() => api.frames(), rateMs);
        framesRes = res;
        res.start();
        return () => res.stop();
    });

    const sectionsRes = new Resource(() => api.allSections(), 10000);
    onMount(() => sectionsRes.start());
    onDestroy(() => sectionsRes.stop());

    let names = $derived(
        new Map(
            (sectionsRes.data?.sections ?? []).map((s) => [
                s.id,
                { name: s.name, subsystem: s.subsystem },
            ]),
        ),
    );

    let orphanCount = $state(0);
    let frame = $derived(framesRes?.data?.frame ?? null);
    let stats = $derived(framesRes?.data?.stats ?? null);
    let dropped = $derived(framesRes?.data?.dropped ?? { pre_frame_samples: 0, late_samples: 0 });
</script>

<div class="page">
    <p class="hint">{t('flamegraph.hint')}</p>

    <div class="controls">
        <label class="picker">
            <span class="dim">{t('flamegraph.rate')}</span>
            <select bind:value={rateMs}>
                {#each RATES as r (r.ms)}
                    <option value={r.ms}>{r.label}</option>
                {/each}
            </select>
        </label>
    </div>

    <DataState
        state={framesRes?.state ?? 'loading'}
        error={framesRes?.error ?? ''}
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

        <FrameTimeline {frame} {names} bind:orphanCount />
        <p class="hint">{t('flamegraph.keys')}</p>

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
    .drops-hint {
        margin: var(--s-2) 0 0;
        font-size: 0.74rem;
        color: var(--text-faint);
    }
</style>
