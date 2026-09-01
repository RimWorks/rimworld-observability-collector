<script lang="ts">
    import { api, type PatchesResponse, type PatchConflict } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import { patchType } from '../lib/format';
    import { t } from '../lib/i18n';
    import { onMount, onDestroy } from 'svelte';

    const res = new Resource<PatchesResponse>(() => api.patches(), 5000);
    onMount(() => res.start());
    onDestroy(() => res.stop());

    interface Group {
        section: string;
        target: string;
        rows: PatchConflict[];
    }

    let conflicts = $derived(res.data?.conflicts ?? []);
    let known = $derived(res.data?.conflicts_known ?? true);
    let groups = $derived.by(() => {
        const map = new Map<string, Group>();
        for (const c of conflicts) {
            let g = map.get(c.section);
            if (!g) {
                g = { section: c.section, target: c.target_method, rows: [] };
                map.set(c.section, g);
            }
            g.rows.push(c);
        }
        return [...map.values()];
    });
</script>

<DataState
    state={res.state}
    error={res.error}
    empty={conflicts.length === 0}
    emptyTitle={known ? t('patches.empty') : t('patches.unknown')}
    emptyHint={known ? t('patches.empty.hint') : t('patches.unknown.hint')}
    onretry={() => res.refresh()}
>
    <p class="intro">{t('patches.intro')}</p>

    <div class="groups">
        {#each groups as g (g.section)}
            <article class="group">
                <header>
                    <span class="label">{t('patches.section')}</span>
                    <span class="section mono">{g.section}</span>
                    <span class="target mono">{g.target}</span>
                </header>
                <div class="table">
                    <div class="head">
                        <Tooltip text={t('tip.patches.owner')}>{t('patches.col.owner')}</Tooltip>
                        <Tooltip text={t('tip.patches.type')}>{t('patches.col.type')}</Tooltip>
                        <Tooltip text={t('tip.patches.priority')} align="end"
                            >{t('patches.col.priority')}</Tooltip
                        >
                        <Tooltip text={t('tip.patches.method')}>{t('patches.col.method')}</Tooltip>
                    </div>
                    {#each g.rows as c, i (c.other_owner + c.patch_method + i)}
                        <div class="rowline">
                            <span class="owner mono">{c.other_owner}</span>
                            <Tooltip text={t('tip.patches.type')}>
                                <span class="type pt{c.patch_type}">{patchType(c.patch_type)}</span>
                            </Tooltip>
                            <span class="num mono dim">{c.priority}</span>
                            <span class="method mono dim">{c.patch_method}</span>
                        </div>
                    {/each}
                </div>
            </article>
        {/each}
    </div>
</DataState>

<style>
    .intro {
        color: var(--text-dim);
        font-size: 0.85rem;
        margin: 0 0 var(--s-4);
        max-width: 70ch;
        line-height: 1.5;
    }
    .groups {
        display: flex;
        flex-direction: column;
        gap: var(--s-4);
    }
    .group {
        border: 1px solid var(--border-soft);
        border-radius: var(--r-lg);
        background: var(--bg-surface);
        overflow: hidden;
    }
    header {
        display: flex;
        align-items: baseline;
        gap: var(--s-2);
        flex-wrap: wrap;
        padding: var(--s-3) var(--s-4);
        border-bottom: 1px solid var(--border-soft);
    }
    .label {
        font-size: 0.62rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--text-faint);
    }
    .section {
        font-size: 0.9rem;
        color: var(--text);
        font-weight: 600;
    }
    .target {
        font-size: 0.74rem;
        color: var(--text-faint);
    }
    .table {
        overflow-x: auto;
    }
    .head,
    .rowline {
        display: grid;
        grid-template-columns: minmax(120px, 1fr) minmax(80px, 0.5fr) minmax(64px, 0.4fr) minmax(
                140px,
                1.6fr
            );
        gap: var(--s-3);
        align-items: center;
        padding: var(--s-2) var(--s-4);
        min-width: 480px;
    }
    .head {
        font-size: 0.68rem;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--text-faint);
        border-bottom: 1px solid var(--border-soft);
    }
    .rowline {
        border-bottom: 1px solid var(--border-soft);
        transition: background var(--t-fast) var(--ease-out);
    }
    .rowline:last-child {
        border-bottom: none;
    }
    .rowline:hover {
        background: var(--bg-surface);
    }
    .owner {
        font-size: 0.82rem;
        color: var(--text);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .method {
        font-size: 0.76rem;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .num {
        text-align: right;
        font-size: 0.82rem;
    }
    .dim {
        color: var(--text-faint);
    }
    .type {
        font-size: 0.64rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        padding: var(--s-0) var(--s-2);
        border-radius: 99px;
        justify-self: start;
        white-space: nowrap;
    }
    .pt1 {
        background: var(--bg-elev);
        color: var(--text-dim);
    }
    .pt2 {
        background: var(--bg-elev);
        color: var(--text-dim);
    }
    .pt3 {
        background: var(--bg-elev);
        color: var(--text-dim);
    }
    .pt4 {
        background: var(--bg-elev);
        color: var(--text-dim);
    }
    .pt0,
    .pt5 {
        background: var(--border-soft);
        color: var(--text-dim);
    }

    @media (max-width: 820px) {
        .head,
        .row {
            grid-template-columns: minmax(0, 1fr) minmax(90px, 0.7fr);
            gap: var(--s-1) var(--s-2);
        }
        .head > :global(*:nth-child(3)),
        .head > :global(*:nth-child(4)),
        .row > :global(*:nth-child(3)) {
            display: none;
        }
        .row > :global(*:nth-child(4)) {
            grid-column: 1 / -1;
            color: var(--text-faint);
        }
    }
</style>
