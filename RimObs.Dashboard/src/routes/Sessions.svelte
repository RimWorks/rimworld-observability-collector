<script lang="ts">
    import { api, type SessionsResponse, type SessionSummaryResponse } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import Card from '../lib/components/Card.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import { count, bytes, ns, relativeTime } from '../lib/format';
    import { t } from '../lib/i18n';
    import { onMount, onDestroy } from 'svelte';

    const list = new Resource<SessionsResponse>(() => api.sessions(), 4000);
    const summary = new Resource<SessionSummaryResponse>(() => api.sessionSummary(), 4000);

    onMount(() => {
        list.start();
        summary.start();
    });
    onDestroy(() => {
        list.stop();
        summary.stop();
    });

    let sessions = $derived(list.data?.sessions ?? []);
    let current = $derived(summary.data ?? null);

    async function exportSession(s: { id: string }) {
        const result = await api.exportBundle({ sessionId: s.id, includes: [], force: false });
        if (result.kind !== 'ok') return;
        const url = URL.createObjectURL(result.blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${s.id}.rimobs.zip`;
        a.click();
        URL.revokeObjectURL(url);
    }
</script>

<DataState
    state={list.state}
    error={list.error}
    empty={sessions.length === 0}
    onretry={() => list.refresh()}
>
    {#if current}
        <Card title={t('sessions.summary')}>
            <div class="grid">
                <div class="kv">
                    <Tooltip text={t('tip.sessions.id')}
                        ><span class="k">{t('sessions.kv.id')}</span></Tooltip
                    >
                    <span class="v mono">{current.session.id}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.started')}
                        ><span class="k">{t('sessions.kv.started')}</span></Tooltip
                    >
                    <span class="v mono">{relativeTime(current.session.started_utc)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.library')}
                        ><span class="k">{t('sessions.kv.library')}</span></Tooltip
                    >
                    <span class="v mono">{current.session.library_version}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.sections')}
                        ><span class="k">{t('sessions.kv.sections')}</span></Tooltip
                    >
                    <span class="v mono">{count(current.section_count)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.metrics')}
                        ><span class="k">{t('sessions.kv.metrics')}</span></Tooltip
                    >
                    <span class="v mono">{count(current.metric_count)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.batches')}
                        ><span class="k">{t('sessions.kv.batches')}</span></Tooltip
                    >
                    <span class="v mono">{count(current.total_batches)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.samples')}
                        ><span class="k">{t('sessions.kv.samples')}</span></Tooltip
                    >
                    <span class="v mono">{count(current.total_samples)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.sectionTime')}
                        ><span class="k">{t('sessions.kv.sectionTime')}</span></Tooltip
                    >
                    <span class="v mono">{ns(current.total_section_ns)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.gc')}
                        ><span class="k">{t('sessions.kv.gc')}</span></Tooltip
                    >
                    <span class="v mono">{count(current.total_gc_events)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.allocations')}
                        ><span class="k">{t('sessions.kv.allocations')}</span></Tooltip
                    >
                    <span class="v mono">{count(current.total_allocations)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.bytes')}
                        ><span class="k">{t('sessions.kv.bytes')}</span></Tooltip
                    >
                    <span class="v mono">{bytes(current.total_bytes)}</span>
                </div>
                <div class="kv">
                    <Tooltip text={t('tip.sessions.lastBatch')}
                        ><span class="k">{t('sessions.kv.lastBatch')}</span></Tooltip
                    >
                    <span class="v mono"
                        >{current.last_batch_utc ? relativeTime(current.last_batch_utc) : '-'}</span
                    >
                </div>
            </div>
        </Card>
    {/if}

    <Card title={t('sessions.list')}>
        <div class="table">
            <div class="head">
                <span>{t('sessions.col.id')}</span>
                <span>{t('sessions.col.started')}</span>
                <span>{t('sessions.col.library')}</span>
                <span>{t('sessions.col.game')}</span>
                <span aria-hidden="true"></span>
            </div>
            {#each sessions as s (s.id)}
                <div class="rowline" class:current={s.is_current}>
                    <span class="name mono"
                        >{s.id}{#if s.is_current}<span class="badge">{t('sessions.current')}</span
                            >{/if}</span
                    >
                    <span class="cell mono">{relativeTime(s.started_utc)}</span>
                    <span class="cell mono">{s.library_version}</span>
                    <span class="cell mono">{s.game_version || '-'}</span>
                    <button
                        type="button"
                        onclick={() => exportSession(s)}
                        disabled={!s.is_current}
                        title={s.is_current
                            ? 'Export this session'
                            : 'Only the current session can be exported'}
                    >
                        Export bundle
                    </button>
                </div>
            {/each}
        </div>
    </Card>
</DataState>

<style>
    .grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
        gap: var(--s-2) var(--s-5);
    }
    .kv {
        display: flex;
        justify-content: space-between;
        gap: var(--s-4);
        padding: var(--s-2) 0;
        border-bottom: 1px solid var(--border-soft);
        align-items: baseline;
    }
    .k {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    .v {
        font-size: 0.86rem;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .table {
        display: flex;
        flex-direction: column;
        overflow-x: auto;
    }
    .head,
    .rowline {
        display: grid;
        grid-template-columns: minmax(0, 2fr) repeat(3, minmax(96px, 1fr)) auto;
        gap: var(--s-3);
        align-items: center;
        padding: var(--s-2) var(--s-2);
        min-width: 480px;
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
    .rowline.current {
        background: color-mix(in srgb, var(--cyan) 8%, transparent);
    }
    .name {
        font-size: 0.84rem;
        color: var(--text);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        display: flex;
        align-items: center;
        gap: var(--s-2);
    }
    .badge {
        font-size: 0.62rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--cyan);
        border: 1px solid var(--border);
        border-radius: 99px;
        padding: var(--s-0) var(--s-2);
    }
    .cell {
        font-size: 0.82rem;
        color: var(--text-dim);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    @media (max-width: 820px) {
        .grid {
            grid-template-columns: 1fr;
        }
        .head,
        .rowline {
            grid-template-columns: minmax(0, 1.6fr) minmax(80px, 1fr) auto;
            gap: var(--s-2);
        }
        .head > :global(*:nth-child(3)),
        .head > :global(*:nth-child(4)),
        .rowline > :global(*:nth-child(3)),
        .rowline > :global(*:nth-child(4)) {
            display: none;
        }
    }
</style>
