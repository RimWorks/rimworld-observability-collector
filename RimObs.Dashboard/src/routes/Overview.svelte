<script lang="ts">
    import type { StatusResponse } from '../lib/api';
    import StatCard from '../lib/components/StatCard.svelte';
    import Card from '../lib/components/Card.svelte';
    import { count, bytes, rate, relativeTime } from '../lib/format';
    import { t } from '../lib/i18n';

    let { status }: { status: StatusResponse | null } = $props();
    let r = $derived(status?.receive);
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
            {:else}
                <p class="none">{t('overview.noSession')}</p>
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
        margin-bottom: var(--s-4);
    }
    .row {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
        gap: var(--s-4);
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

    @media (max-width: 820px) {
        .grid {
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
        }
        .row {
            grid-template-columns: 1fr;
        }
    }
</style>
