<script lang="ts">
    import { api, type HotspotsResponse, type SectionTimeseriesResponse } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import LineChart from '../lib/components/LineChart.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import { ns, count, gradeFromShare } from '../lib/format';
    import { t } from '../lib/i18n';
    import { onMount, onDestroy } from 'svelte';

    const res = new Resource<HotspotsResponse>(() => api.hotspots(100), 3000);
    onMount(() => res.start());
    onDestroy(() => res.stop());

    let rows = $derived(res.data?.hotspots ?? []);
    let max = $derived(rows.reduce((m, h) => Math.max(m, h.total_ns), 1));

    let expandedId = $state<number | null>(null);
    let trend = $state<SectionTimeseriesResponse | null>(null);
    let trendLoading = $state(false);

    async function toggle(id: number) {
        if (expandedId === id) {
            expandedId = null;
            return;
        }
        expandedId = id;
        trend = null;
        trendLoading = true;
        try {
            const data = await api.sectionTimeseries(id);
            if (expandedId === id) trend = data;
        } finally {
            if (expandedId === id) trendLoading = false;
        }
    }

    let trendX = $derived.by(() => {
        const points = trend?.points ?? [];
        if (points.length === 0) return [];
        const last = points[points.length - 1].t;
        return points.map((p) => p.t - last);
    });
    let trendSeries = $derived.by(() => [
        {
            label: t('hotspots.trend.mean'),
            values: (trend?.points ?? []).map((p) => p.mean_ns),
            stroke: '--cyan',
            fill: 'rgba(57, 196, 212, 0.12)',
        },
    ]);
</script>

<DataState
    state={res.state}
    error={res.error}
    empty={rows.length === 0}
    onretry={() => res.refresh()}
>
    <p class="hint">{t('hotspots.hint')}</p>
    <div class="table">
        <div class="head">
            <Tooltip text={t('tip.hotspots.section')}>{t('hotspots.col.section')}</Tooltip>
            <Tooltip text={t('tip.hotspots.total')} align="end">{t('hotspots.col.total')}</Tooltip>
            <Tooltip text={t('tip.hotspots.mean')} align="end">{t('hotspots.col.mean')}</Tooltip>
            <Tooltip text={t('tip.hotspots.p50')} align="end">{t('hotspots.col.p50')}</Tooltip>
            <Tooltip text={t('tip.hotspots.p95')} align="end">{t('hotspots.col.p95')}</Tooltip>
            <Tooltip text={t('tip.hotspots.p99')} align="end">{t('hotspots.col.p99')}</Tooltip>
            <Tooltip text={t('tip.hotspots.samples')} align="end"
                >{t('hotspots.col.samples')}</Tooltip
            >
        </div>
        {#each rows as h (h.id)}
            {@const share = h.total_ns / max}
            <button
                class="rowline"
                class:expanded={expandedId === h.id}
                aria-expanded={expandedId === h.id}
                onclick={() => toggle(h.id)}
            >
                <div class="section">
                    <span class="name mono">{h.name}</span>
                    <span class="bar"
                        ><span class="fill g{gradeFromShare(share)}" style="width: {share * 100}%"
                        ></span></span
                    >
                </div>
                <span class="num strong mono">{ns(h.total_ns)}</span>
                <span class="num mono">{ns(h.mean_ns)}</span>
                <span class="num mono">{ns(h.p50_ns)}</span>
                <span class="num mono">{ns(h.p95_ns)}</span>
                <span class="num warn mono">{ns(h.p99_ns)}</span>
                <span class="num dim mono">{count(h.sample_count)}</span>
            </button>
            {#if expandedId === h.id}
                <div class="trend">
                    <div class="trend-head">
                        <span class="trend-title">{t('hotspots.trend.title')}</span>
                        <span class="trend-meta dim mono"
                            >min {ns(h.min_ns)} / max {ns(h.max_ns)}</span
                        >
                    </div>
                    {#if trendLoading}
                        <p class="trend-state dim">{t('hotspots.trend.loading')}</p>
                    {:else if (trend?.points.length ?? 0) === 0}
                        <p class="trend-state dim">{t('hotspots.trend.empty')}</p>
                    {:else}
                        <LineChart
                            x={trendX}
                            series={trendSeries}
                            height={180}
                            format={(n) => ns(n)}
                            xFormat={(n) => `${n}s`}
                        />
                    {/if}
                </div>
            {/if}
        {/each}
    </div>
</DataState>

<style>
    .hint {
        margin: 0 0 var(--s-2);
        font-size: 0.78rem;
        color: var(--text-faint);
    }
    .table {
        display: flex;
        flex-direction: column;
    }
    .head,
    .rowline {
        display: grid;
        grid-template-columns: minmax(0, 2.4fr) repeat(6, minmax(64px, 0.7fr));
        gap: var(--s-3);
        align-items: center;
        padding: var(--s-2) var(--s-4);
    }
    .head {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--text-faint);
        border-bottom: 1px solid var(--border);
        position: sticky;
        top: 0;
        background: var(--bg-base);
    }
    .rowline {
        border: none;
        border-bottom: 1px solid var(--border-soft);
        background: transparent;
        color: inherit;
        font: inherit;
        text-align: left;
        width: 100%;
        cursor: pointer;
    }
    .rowline:hover,
    .rowline.expanded {
        background: var(--bg-surface);
    }
    .num {
        text-align: right;
        font-size: 0.85rem;
    }
    .strong {
        color: var(--text);
        font-weight: 600;
    }
    .warn {
        color: var(--warn, var(--text));
    }
    .dim {
        color: var(--text-faint);
    }
    .section {
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
    }
    .name {
        font-size: 0.84rem;
        color: var(--text);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .bar {
        height: 4px;
        border-radius: 99px;
        background: var(--border-soft);
        overflow: hidden;
    }
    .fill {
        display: block;
        height: 100%;
        border-radius: 99px;
    }
    .trend {
        padding: var(--s-3) var(--s-4) var(--s-4);
        border-bottom: 1px solid var(--border-soft);
        background: var(--bg-surface);
    }
    .trend-head {
        display: flex;
        justify-content: space-between;
        align-items: baseline;
        margin-bottom: var(--s-2);
    }
    .trend-title {
        font-size: 0.78rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    .trend-meta {
        font-size: 0.78rem;
    }
    .trend-state {
        margin: 0;
        font-size: 0.82rem;
    }
    .g0 {
        background: var(--grade-0);
    }
    .g1 {
        background: var(--grade-1);
    }
    .g2 {
        background: var(--grade-2);
    }
    .g3 {
        background: var(--grade-3);
    }
    .g4 {
        background: var(--grade-4);
    }

    @media (max-width: 900px) {
        .head,
        .rowline {
            grid-template-columns: minmax(0, 2.2fr) repeat(3, minmax(60px, 0.8fr));
            gap: var(--s-2);
            padding: var(--s-2) var(--s-2);
        }
        .head > :global(*:nth-child(4)),
        .head > :global(*:nth-child(5)),
        .head > :global(*:nth-child(7)),
        .rowline > :global(*:nth-child(4)),
        .rowline > :global(*:nth-child(5)),
        .rowline > :global(*:nth-child(7)) {
            display: none;
        }
    }
    @media (max-width: 560px) {
        .head,
        .rowline {
            grid-template-columns: minmax(0, 1fr) minmax(64px, auto);
        }
        .head > :global(*:nth-child(3)),
        .head > :global(*:nth-child(6)),
        .rowline > :global(*:nth-child(3)),
        .rowline > :global(*:nth-child(6)) {
            display: none;
        }
    }
</style>
