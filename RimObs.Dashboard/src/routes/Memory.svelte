<script lang="ts">
    import { api, type GcResponse } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import StatCard from '../lib/components/StatCard.svelte';
    import Card from '../lib/components/Card.svelte';
    import LineChart from '../lib/components/LineChart.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import { bytes, count } from '../lib/format';
    import { pauseLabel, genLabel, summarize, heapSeries } from '../lib/gc';
    import { t } from '../lib/i18n';
    import { onMount, onDestroy } from 'svelte';

    const res = new Resource<GcResponse>(() => api.gc(200), 4000);
    onMount(() => res.start());
    onDestroy(() => res.stop());

    let events = $derived(res.data?.events ?? []);
    let maxRate = $derived(events.reduce((m, e) => Math.max(m, e.allocation_rate_bpm), 1));
    let summary = $derived(summarize(events));
    let trend = $derived(heapSeries(events));
    let chartSeries = $derived([
        {
            label: t('memory.chart.heap'),
            values: trend.heap,
            stroke: '--cyan',
            fill: 'rgba(57, 196, 212, 0.12)',
        },
    ]);
</script>

<DataState
    state={res.state}
    error={res.error}
    empty={events.length === 0}
    onretry={() => res.refresh()}
>
    <div class="stats">
        <StatCard
            icon="memory"
            label={t('memory.stat.currentHeap')}
            value={bytes(summary.currentHeap)}
            tooltip={t('tip.memory.currentHeap')}
        />
        <StatCard
            icon="memory"
            label={t('memory.stat.peakHeap')}
            value={bytes(summary.peakHeap)}
            tooltip={t('tip.memory.peakHeap')}
        />
        <StatCard
            icon="metric"
            label={t('memory.stat.peakRate')}
            value="{bytes(summary.peakAllocRate)}/m"
            tooltip={t('tip.memory.peakRate')}
        />
        <StatCard
            icon="stack"
            label={t('memory.stat.collections')}
            value="{count(summary.perGen[0])} / {count(summary.perGen[1])} / {count(
                summary.perGen[2],
            )}"
            tooltip={t('tip.memory.collections')}
        />
    </div>

    <Card title={t('memory.chart.title')}>
        {#if trend.heap.length > 1}
            <LineChart
                x={trend.ticks}
                series={chartSeries}
                height={220}
                format={(n) => bytes(n)}
                xFormat={(n) => count(n)}
            />
        {:else}
            <p class="hint">{t('memory.chart.waiting')}</p>
        {/if}
    </Card>

    <Card title={t('memory.events.title')}>
        <div class="table">
            <div class="head">
                <Tooltip text={t('tip.memory.col.gen')} align="end">{t('memory.col.gen')}</Tooltip>
                <Tooltip text={t('tip.memory.col.pause')}>{t('memory.col.pause')}</Tooltip>
                <Tooltip text={t('tip.memory.col.before')} align="end"
                    >{t('memory.col.before')}</Tooltip
                >
                <Tooltip text={t('tip.memory.col.after')} align="end"
                    >{t('memory.col.after')}</Tooltip
                >
                <Tooltip text={t('tip.memory.col.interval')} align="end"
                    >{t('memory.col.interval')}</Tooltip
                >
                <Tooltip text={t('tip.memory.col.rate')} align="end">{t('memory.col.rate')}</Tooltip
                >
                <Tooltip text={t('tip.memory.col.tick')} align="end">{t('memory.col.tick')}</Tooltip
                >
            </div>
            <!-- gc events have no stable id and rows are display-only, so index keying is safe -->
            {#each events as e, i (i)}
                {@const gn = Math.min(e.generation, 2)}
                <div class="rowline">
                    <span class="num">
                        <Tooltip text={t(`tip.gen.g${gn}`)}>
                            <em class="gen g{gn}">{genLabel(e.generation)}</em>
                        </Tooltip>
                    </span>
                    <span class="pause">{pauseLabel(e.pause_type)}</span>
                    <span class="num mono dim">{bytes(e.heap_before)}</span>
                    <span class="num mono dim">{bytes(e.heap_after)}</span>
                    <span class="num mono">{(e.duration_micros / 1000).toFixed(0)} ms</span>
                    <span class="num rate">
                        <span class="track"
                            ><span
                                class="fill"
                                style="width: {(e.allocation_rate_bpm / maxRate) * 100}%"
                            ></span></span
                        >
                        <span class="mono">{bytes(e.allocation_rate_bpm)}/m</span>
                    </span>
                    <span class="num mono dim">{count(e.ticks)}</span>
                </div>
            {/each}
        </div>
    </Card>

    <Card title={t('memory.guide.title')}>
        <dl class="guide">
            <dt><em class="gen g0">{t('memory.gen.g0')}</em></dt>
            <dd>{t('memory.guide.g0')}</dd>
            <dt><em class="gen g1">{t('memory.gen.g1')}</em></dt>
            <dd>{t('memory.guide.g1')}</dd>
            <dt><em class="gen g2">{t('memory.gen.g2')}</em></dt>
            <dd>{t('memory.guide.g2')}</dd>
        </dl>
        <p class="note">{t('memory.guide.interval')}</p>
    </Card>
</DataState>

<style>
    .stats {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: var(--s-4);
        margin-bottom: var(--s-4);
    }
    :global(.card) {
        margin-bottom: var(--s-4);
    }
    .hint,
    .note {
        color: var(--text-faint);
        font-size: 0.85rem;
        margin: 0;
    }
    .note {
        margin-top: var(--s-4);
        border-top: 1px solid var(--border-soft);
        padding-top: var(--s-4);
    }
    .head,
    .rowline {
        display: grid;
        grid-template-columns:
            72px minmax(90px, 1fr) repeat(2, minmax(90px, 1fr)) minmax(72px, 0.8fr)
            minmax(150px, 1.4fr) minmax(80px, 0.8fr);
        gap: var(--s-3);
        align-items: center;
        padding: var(--s-2) var(--s-1);
    }
    .head {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--text-faint);
        border-bottom: 1px solid var(--border);
    }
    .rowline {
        border-bottom: 1px solid var(--border-soft);
    }
    .rowline:hover {
        background: var(--bg-surface);
    }
    .num {
        text-align: right;
        font-size: 0.84rem;
    }
    .dim {
        color: var(--text-faint);
    }
    .pause {
        font-size: 0.82rem;
        color: var(--text-dim);
    }
    .gen {
        font-style: normal;
        font-family: var(--font-mono);
        padding: var(--s-0) var(--s-2);
        border-radius: 99px;
        font-size: 0.74rem;
        white-space: nowrap;
    }
    .gen.g0 {
        background: color-mix(in srgb, var(--good) 18%, transparent);
        color: var(--good);
    }
    .gen.g1 {
        background: color-mix(in srgb, var(--warn) 18%, transparent);
        color: var(--warn);
    }
    .gen.g2 {
        background: color-mix(in srgb, var(--bad) 18%, transparent);
        color: var(--bad);
    }
    .rate {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        justify-content: flex-end;
    }
    .rate .mono {
        font-size: 0.78rem;
        white-space: nowrap;
    }
    .track {
        flex: 1;
        height: 4px;
        border-radius: 99px;
        background: var(--border-soft);
        overflow: hidden;
        max-width: 80px;
    }
    .fill {
        display: block;
        height: 100%;
        background: var(--cyan);
        border-radius: 99px;
    }
    .guide {
        margin: 0;
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--s-2) var(--s-4);
        align-items: baseline;
    }
    .guide dd {
        margin: 0;
        font-size: 0.86rem;
        color: var(--text-dim);
    }

    @media (max-width: 820px) {
        .stats {
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
        }
        .head,
        .rowline {
            grid-template-columns: 58px minmax(80px, 1fr) minmax(80px, 1fr) minmax(64px, 0.8fr);
            gap: var(--s-2);
        }
        .head > :global(*:nth-child(3)),
        .head > :global(*:nth-child(6)),
        .head > :global(*:nth-child(7)),
        .rowline > :global(*:nth-child(3)),
        .rowline > :global(*:nth-child(6)),
        .rowline > :global(*:nth-child(7)) {
            display: none;
        }
    }
</style>
