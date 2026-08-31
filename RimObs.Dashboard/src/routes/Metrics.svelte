<script lang="ts">
    import { api, type MetricsResponse } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import { count, metricKind } from '../lib/format';
    import { t } from '../lib/i18n';
    import { onMount, onDestroy } from 'svelte';

    const res = new Resource<MetricsResponse>(() => api.metrics(), 3000);
    onMount(() => res.start());
    onDestroy(() => res.stop());

    let metrics = $derived(res.data?.metrics ?? []);
</script>

<DataState
    state={res.state}
    error={res.error}
    empty={metrics.length === 0}
    onretry={() => res.refresh()}
>
    <div class="list">
        {#each metrics as m (m.id)}
            <article class="metric">
                <header>
                    <span class="name mono">{m.name}</span>
                    <Tooltip text={t('tip.metrics.kind')}>
                        <span class="kind k{m.kind}">{metricKind(m.kind)}</span>
                    </Tooltip>
                    {#if m.unit}
                        <Tooltip text={t('tip.metrics.unit')}>
                            <span class="unit">{m.unit}</span>
                        </Tooltip>
                    {/if}
                </header>
                <div class="labels">
                    {#each m.labels as l (l.canonical)}
                        <div class="label">
                            <span class="canon mono">{l.canonical || '(default)'}</span>
                            <span class="val mono">{count(l.latest_value)}</span>
                            <span class="samples mono"
                                >{count(l.total_sample_count)} {t('metrics.col.samples')}</span
                            >
                        </div>
                    {/each}
                </div>
            </article>
        {/each}
    </div>
</DataState>

<style>
    .list {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
        gap: var(--s-4);
    }
    .metric {
        border: 1px solid var(--border-soft);
        border-radius: var(--r-lg);
        background: var(--bg-surface);
        overflow: hidden;
    }
    header {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-3) var(--s-4);
        border-bottom: 1px solid var(--border-soft);
    }
    .name {
        font-size: 0.85rem;
        flex: 1;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .kind {
        font-size: 0.66rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        padding: var(--s-0) var(--s-2);
        border-radius: 99px;
    }
    .k0 {
        background: var(--bg-elev);
        color: var(--text-dim);
    }
    .k1 {
        background: var(--bg-elev);
        color: var(--text-dim);
    }
    .k2 {
        background: var(--bg-elev);
        color: var(--text-dim);
    }
    .unit {
        font-size: 0.72rem;
        color: var(--text-faint);
    }
    .labels {
        display: flex;
        flex-direction: column;
    }
    .label {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        grid-template-areas: 'canon val' 'samples val';
        gap: 0 var(--s-2);
        padding: var(--s-2) var(--s-4);
        border-bottom: 1px solid var(--border-soft);
        align-items: center;
    }
    .label:last-child {
        border-bottom: none;
    }
    .canon {
        grid-area: canon;
        overflow-wrap: anywhere;
        font-size: 0.8rem;
        color: var(--text-dim);
    }
    .val {
        grid-area: val;
        font-size: 1.15rem;
        font-weight: 600;
        text-align: right;
    }
    .samples {
        grid-area: samples;
        font-size: 0.7rem;
        color: var(--text-faint);
    }

    @media (max-width: 820px) {
        .grid {
            grid-template-columns: 1fr;
        }
    }
</style>
