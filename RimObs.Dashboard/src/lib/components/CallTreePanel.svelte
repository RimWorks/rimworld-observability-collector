<script lang="ts">
    import {
        buildTreeRows,
        buildInvertedRows,
        labelFor,
        keysToNode,
        allExpandableKeys,
        type SortColumn,
        type TableRow,
    } from '../frameTable';
    import type { TreeNode } from '../frameTree';
    import { ns } from '../format';
    import { t } from '../i18n';
    import { SvelteSet } from 'svelte/reactivity';

    let {
        nodes,
        names,
        selectedNode = -1,
        onSelect,
    }: {
        nodes: readonly TreeNode[];
        names: Map<number, { name: string; subsystem: string | null }>;
        selectedNode?: number;
        onSelect?: (nodeIndex: number) => void;
    } = $props();

    let expanded = $state(new SvelteSet<string>());
    let sortColumn = $state<SortColumn>('total');
    let ascending = $state(false);
    let inverted = $state(false);
    let foldRecursion = $state(true);
    let search = $state('');

    let rows = $derived(
        (inverted ? buildInvertedRows : buildTreeRows)(nodes, {
            names,
            expanded,
            sortColumn,
            ascending,
            foldRecursion,
            search,
        }),
    );

    // a bar click opens the tree down to that node, which is the half of the link Neo calls
    // ExpandCallTreeToNode.
    $effect(() => {
        if (selectedNode < 0 || inverted) return;
        for (const key of keysToNode(nodes, selectedNode).slice(0, -1)) expanded.add(key);
    });

    let selectedKey = $derived(
        selectedNode >= 0 ? (keysToNode(nodes, selectedNode).at(-1) ?? null) : null,
    );

    function toggle(row: TableRow): void {
        if (expanded.has(row.key)) expanded.delete(row.key);
        else expanded.add(row.key);
    }

    function sortBy(column: SortColumn): void {
        if (sortColumn === column) ascending = !ascending;
        else {
            sortColumn = column;
            ascending = column === 'label';
        }
    }

    function expandAll(): void {
        for (const key of allExpandableKeys(nodes)) expanded.add(key);
    }

    function arrow(column: SortColumn): string {
        if (sortColumn !== column) return '';
        return ascending ? ' ↑' : ' ↓';
    }
</script>

<div class="panel" data-testid="call-tree-panel">
    <div class="bar">
        <input
            type="search"
            bind:value={search}
            placeholder={t('tree.search')}
            aria-label={t('tree.search')}
            data-testid="tree-search"
        />
        <label><input type="checkbox" bind:checked={inverted} /> {t('tree.inverted')}</label>
        <label><input type="checkbox" bind:checked={foldRecursion} /> {t('tree.fold')}</label>
        <button type="button" onclick={expandAll} data-testid="expand-all"
            >{t('tree.expandAll')}</button
        >
        <button type="button" onclick={() => expanded.clear()} data-testid="collapse-all">
            {t('tree.collapseAll')}
        </button>
    </div>

    {#if rows.length === 0}
        <p class="empty" data-testid="tree-empty">{t('tree.empty')}</p>
    {:else}
        <table>
            <thead>
                <tr>
                    <th class="name">
                        <button type="button" onclick={() => sortBy('label')}>
                            {t('tree.col.label')}{arrow('label')}
                        </button>
                    </th>
                    <th>
                        <button
                            type="button"
                            onclick={() => sortBy('self')}
                            data-testid="sort-self"
                        >
                            {t('tree.col.self')}{arrow('self')}
                        </button>
                    </th>
                    <th>
                        <button type="button" onclick={() => sortBy('total')}>
                            {t('tree.col.total')}{arrow('total')}
                        </button>
                    </th>
                    <th>
                        <button type="button" onclick={() => sortBy('calls')}>
                            {t('tree.col.calls')}{arrow('calls')}
                        </button>
                    </th>
                </tr>
            </thead>
            <tbody>
                {#each rows as row (row.key)}
                    <tr class:selected={row.key === selectedKey} data-testid="tree-row">
                        <td class="name" style="padding-left:{row.depth * 14 + 4}px">
                            {#if row.hasChildren}
                                <button
                                    type="button"
                                    class="twist"
                                    onclick={() => toggle(row)}
                                    aria-expanded={row.expanded}
                                    aria-label={labelFor(row.sectionId, names)}
                                    >{row.expanded ? '▾' : '▸'}</button
                                >
                            {:else}
                                <span class="twist"></span>
                            {/if}
                            <button
                                type="button"
                                class="label"
                                onclick={() => row.nodes.length > 0 && onSelect?.(row.nodes[0])}
                                >{labelFor(row.sectionId, names)}</button
                            >
                        </td>
                        <td class="num">{ns(row.selfUs * 1000)}</td>
                        <td class="num">{ns(row.totalUs * 1000)}</td>
                        <td class="num">{row.calls || ''}</td>
                    </tr>
                {/each}
            </tbody>
        </table>
    {/if}
</div>

<style>
    .panel {
        margin-top: 1rem;
        border: 1px solid var(--border);
        border-radius: 3px;
    }
    .bar {
        display: flex;
        gap: 0.5rem;
        align-items: center;
        padding: 0.4rem;
        border-bottom: 1px solid var(--border);
        font-size: 0.8rem;
    }
    .bar input[type='search'] {
        flex: 1;
        min-width: 6rem;
        font: inherit;
        color: var(--text);
        background: var(--bg-void);
        border: 1px solid var(--border);
        border-radius: 3px;
        padding: 0.15rem 0.4rem;
    }
    .bar button {
        font: inherit;
        color: var(--text);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: 3px;
        padding: 0.15rem 0.5rem;
        cursor: pointer;
    }
    table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.8rem;
    }
    th {
        text-align: right;
        font-weight: 500;
        color: var(--text-dim);
        border-bottom: 1px solid var(--border);
    }
    th.name {
        text-align: left;
    }
    th button {
        font: inherit;
        color: inherit;
        background: none;
        border: 0;
        padding: 0.3rem 0.5rem;
        cursor: pointer;
    }
    td {
        padding: 0.1rem 0.5rem;
    }
    td.num {
        text-align: right;
        font-family: var(--font-mono);
    }
    td.name {
        white-space: nowrap;
    }
    .twist {
        display: inline-block;
        width: 1rem;
        font: inherit;
        color: var(--text-dim);
        background: none;
        border: 0;
        cursor: pointer;
    }
    .label {
        font: inherit;
        color: var(--text);
        background: none;
        border: 0;
        padding: 0;
        cursor: pointer;
    }
    tr.selected {
        background: var(--bg-surface);
    }
    .empty {
        padding: 0.75rem;
        color: var(--text-dim);
        font-size: 0.8rem;
    }
</style>
