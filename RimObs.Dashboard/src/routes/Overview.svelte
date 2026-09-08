<script lang="ts">
    import { api, type StatusResponse } from '../lib/api';
    import StatCard from '../lib/components/StatCard.svelte';
    import Card from '../lib/components/Card.svelte';
    import BundleExportForm from '../lib/components/BundleExportForm.svelte';
    import LineChart from '../lib/components/LineChart.svelte';
    import { Resource } from '../lib/poll.svelte';
    import { summarize, heapSeries } from '../lib/gc';
    import type { GcResponse } from '../lib/api';
    import { onMount, onDestroy } from 'svelte';
    import { count, bytes, rate, relativeTime } from '../lib/format';
    import { t } from '../lib/i18n';

    let { status }: { status: StatusResponse | null } = $props();
    let r = $derived(status?.receive);

    // the heap trend and generation counts, all that survived the Memory page
    const gcRes = new Resource<GcResponse>(() => api.gc(200), 4000);
    onMount(() => gcRes.start());
    onDestroy(() => gcRes.stop());
    let gcEvents = $derived(gcRes.data?.events ?? []);
    let gc = $derived(summarize(gcEvents));
    let heap = $derived(heapSeries(gcEvents));
    let heapSeriesData = $derived([
        {
            label: t('overview.heap.series'),
            values: heap.heap,
            stroke: '--cyan',
            fill: 'rgba(57, 196, 212, 0.12)',
        },
    ]);

    let exporting = $state(false);
    let exportError = $state<string | null>(null);

    // only the current session can be exported, so the form needs no session picker.
    async function handleExport(p: { sessionId: string; includes: string[]; force: boolean }) {
        exportError = null;
        const result = await api.exportBundle(p);
        if (result.kind === 'over_cap') {
            const est = (result.estimatedBytes / 1_048_576).toFixed(1);
            const cap = (result.capBytes / 1_048_576).toFixed(1);
            exportError = `Bundle would be ${est} MB (cap ${cap} MB). Tick "${t('bundle.export.force')}" to override.`;
            return;
        }
        if (result.kind === 'error') {
            exportError = result.message;
            return;
        }
        const url = URL.createObjectURL(result.blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${p.sessionId}.rimobs.zip`;
        a.click();
        URL.revokeObjectURL(url);
    }
</script>

{#if r}
    <div class="grid">
        {#if r.tps !== null}
            <StatCard
                icon="gauge"
                label={t('overview.tps')}
                value={rate(r.tps)}
                tooltip={t('tip.overview.tps')}
            />
        {/if}
        {#if r.fps !== null}
            <StatCard
                icon="gauge"
                label={t('overview.fps')}
                value={rate(r.fps)}
                tooltip={t('tip.overview.fps')}
            />
        {/if}
        <StatCard
            icon="stack"
            label={t('overview.batches')}
            value={count(r.total_batches)}
            tooltip={t('tip.overview.batches')}
        />
        <StatCard
            icon="metric"
            label={t('overview.samples')}
            value={count(r.total_samples)}
            tooltip={t('tip.overview.samples')}
        />
        <StatCard
            icon="flame"
            label={t('overview.sections')}
            value={count(r.section_count)}
            tooltip={t('tip.overview.sections')}
        />
        <StatCard
            icon="memory"
            label={t('overview.gc')}
            value={count(r.total_gc_events)}
            tooltip={t('tip.overview.gc')}
        />
        <StatCard
            icon="metric"
            label={t('overview.allocations')}
            value={count(r.total_allocations)}
            tooltip={t('tip.overview.allocations')}
        />
        <StatCard
            icon="logs"
            label={t('overview.bytes')}
            value={bytes(r.total_bytes)}
            tooltip={t('tip.overview.bytes')}
        />
    </div>

    <div class="row">
        <Card title={t('overview.session')}>
            {#if status?.session}
                <dl>
                    <dt>{t('overview.kv.id')}</dt>
                    <dd class="mono">{status.session.id}</dd>
                    <dt>{t('overview.kv.library')}</dt>
                    <dd class="mono">{status.session.library_version}</dd>
                    <dt>{t('overview.kv.started')}</dt>
                    <dd>{new Date(status.session.started_utc).toLocaleString()}</dd>
                    <dt>{t('overview.kv.lastBatch')}</dt>
                    <dd>{relativeTime(r.last_batch_utc)}</dd>
                </dl>

                <div class="export">
                    {#if exporting}
                        <BundleExportForm sessionId={status.session.id} onExport={handleExport} />
                        {#if exportError}
                            <p class="export-error" role="alert">{exportError}</p>
                        {/if}
                    {:else}
                        <button
                            type="button"
                            onclick={() => (exporting = true)}
                            data-testid="open-export">{t('bundle.export.title')}</button
                        >
                    {/if}
                </div>
            {:else}
                <p class="none">{t('overview.noSession')}</p>
            {/if}
        </Card>
        <Card title={t('overview.heap')}>
            {#if gcEvents.length === 0}
                <p class="none">{t('overview.heap.empty')}</p>
            {:else}
                <dl>
                    <dt>{t('overview.heap.current')}</dt>
                    <dd class="mono">{bytes(gc.currentHeap)}</dd>
                    <dt>{t('overview.heap.peak')}</dt>
                    <dd class="mono">{bytes(gc.peakHeap)}</dd>
                    <dt>{t('overview.heap.gens')}</dt>
                    <dd class="mono" data-testid="gc-gens">
                        {gc.perGen[0]} / {gc.perGen[1]} / {gc.perGen[2]}
                    </dd>
                </dl>
                <LineChart
                    x={heap.ticks}
                    series={heapSeriesData}
                    height={140}
                    format={(n) => bytes(n)}
                />
            {/if}
        </Card>
        <Card title={t('overview.collector')}>
            <dl>
                <dt>{t('overview.kv.status')}</dt>
                <dd class="mono">{status?.status}</dd>
                <dt>{t('overview.kv.version')}</dt>
                <dd class="mono">{status?.version}</dd>
                <dt>{t('overview.kv.update')}</dt>
                <dd>
                    {status?.update?.available
                        ? status.update.latest_version
                        : t('overview.upToDate')}
                </dd>
            </dl>
        </Card>
    </div>
{/if}

<style>
    .grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
        gap: var(--s-4);
        padding: var(--s-4) var(--rail);
    }
    .row {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
        gap: var(--s-4);
        padding: 0 var(--rail) var(--s-4);
    }
    .row :global(.card) {
        border: 1px solid var(--border-soft);
        border-radius: var(--r-lg);
        box-shadow: var(--shadow-card);
    }
    dl {
        margin: 0;
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--s-2) var(--s-4);
    }
    dt {
        color: var(--text-faint);
        font-size: 0.78rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
    }
    dd {
        margin: 0;
        text-align: right;
        word-break: break-all;
    }
    .none {
        color: var(--text-faint);
        margin: 0;
    }
    .export {
        margin-top: var(--s-4);
        padding-top: var(--s-4);
        border-top: 1px solid var(--border-soft);
    }
    .export-error {
        margin: var(--s-2) 0 0;
        color: var(--bad);
        font-size: 0.82rem;
    }

    @media (max-width: 820px) {
        .grid {
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
        }
        .row {
            grid-template-columns: 1fr;
        }
    }
</style>
