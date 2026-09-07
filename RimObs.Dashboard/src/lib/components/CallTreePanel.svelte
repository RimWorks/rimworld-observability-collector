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
    import { deltaSeverity } from '../frameCost';
    import { t } from '../i18n';
    import { SvelteSet } from 'svelte/reactivity';

    let {
        nodes,
        names,
        selectedNode = -1,
        frameDurationUs = 0,
        baselineUs = new Map<number, number>(),
        onSelect,
    }: {
        nodes: readonly TreeNode[];
        names: Map<number, { name: string; subsystem: string | null }>;
        selectedNode?: number;
        frameDurationUs?: number;
        baselineUs?: Map<number, number>;
        onSelect?: (nodeIndex: number) => void;
    } = $props();

    let expanded = $state(new SvelteSet<string>());
    let sortColumn = $state<SortColumn>('total');
    let ascending = $state(false);
    let inverted = $state(false);
    let foldRecursion = $state(true);
    let search = $state('');
    const TABS = [
        { id: 'tree', label: 'tree.tab.tree' },
        { id: 'pie', label: 'tree.tab.pie' },
        { id: 'alloc', label: 'tree.tab.alloc' },
        { id: 'vram', label: 'tree.tab.vram' },
    ] as const;
    let activeTab = $state<(typeof TABS)[number]['id']>('tree');

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

    function share(totalUs: number): number {
        if (!(frameDurationUs > 0)) return 0;
        return Math.min(100, (totalUs / frameDurationUs) * 100);
    }

    function deltaText(row: TableRow): string {
        const base = baselineUs.get(row.sectionId);
        if (base === undefined) return '';
        const d = (row.totalUs - base) / 1000;
        return `${d >= 0 ? '+' : ''}${d.toFixed(1)}`;
    }

    function deltaClass(row: TableRow): string {
        const base = baselineUs.get(row.sectionId);
        if (base === undefined) return 'flat';
        return deltaSeverity(row.totalUs - base) === 1
            ? 'up'
            : deltaSeverity(row.totalUs - base) === -1
              ? 'dn'
              : 'flat';
    }

    function arrow(column: SortColumn): string {
        if (sortColumn !== column) return '';
        return ascending ? ' ↑' : ' ↓';
    }
</script>

<div class="panel" data-testid="call-tree-panel">
    <div class="tabs">
        {#each TABS as tab (tab.id)}
            <button
                type="button"
                class="tab"
                class:on={activeTab === tab.id}
                onclick={() => (activeTab = tab.id)}
                data-testid="tab-{tab.id}">{t(tab.label)}</button
            >
        {/each}
        <span class="chip"><i></i>MainThread</span>
    </div>

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

    {#if activeTab !== 'tree'}
        <p class="empty" data-testid="tab-soon">{t('tree.soon')}</p>
    {:else if rows.length === 0}
        <p class="empty" data-testid="tree-empty">{t('tree.empty')}</p>
    {:else}
        <table>
            <thead>
                <tr>
                    <th class="pct" aria-label="share"></th>
                    <th class="num pct">%</th>
                    <th class="num">
                        <button type="button" onclick={() => sortBy('total')}>
                            {t('tree.col.total')}{arrow('total')}
                        </button>
                    </th>
                    <th class="num">
                        <button
                            type="button"
                            onclick={() => sortBy('self')}
                            data-testid="sort-self"
                        >
                            {t('tree.col.self')}{arrow('self')}
                        </button>
                    </th>
                    <th class="num">{t('tree.col.delta')}</th>
                    <th class="num">
                        <button type="button" onclick={() => sortBy('calls')}>
                            {t('tree.col.calls')}{arrow('calls')}
                        </button>
                    </th>
                    <th class="num">{t('tree.col.alloc')}</th>
                    <th class="name">
                        <button type="button" onclick={() => sortBy('label')}>
                            {t('tree.col.label')}{arrow('label')}
                        </button>
                    </th>
                </tr>
            </thead>
            <tbody>
                {#each rows as row (row.key)}
                    <tr class:selected={row.key === selectedKey} data-testid="tree-row">
                        <td class="pct"><i style="width:{share(row.totalUs)}%"></i></td>
                        <td class="num pct">{share(row.totalUs).toFixed(1)}</td>
                        <td class="num">{ns(row.totalUs * 1000)}</td>
                        <td class="num">{ns(row.selfUs * 1000)}</td>
                        <td class="num delta {deltaClass(row)}" data-testid="tree-delta"
                            >{deltaText(row)}</td
                        >
                        <td class="num">{row.calls || ''}</td>
                        <td class="num dim">&mdash;</td>
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
                    </tr>
                {/each}
            </tbody>
        </table>
    {/if}
</div>

<style>
    .panel {
        margin-top: var(--s-2);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        background: var(--bg-base);
        overflow: hidden;
    }
    .tabs {
        display: flex;
        align-items: center;
        gap: var(--s-1);
        padding: 0 var(--s-2);
        border-bottom: 1px solid var(--border);
        background: var(--bg-surface);
    }
    .tab {
        font: inherit;
        font-size: var(--f-body, 13px);
        color: var(--text-dim);
        background: none;
        border: 0;
        border-bottom: 2px solid transparent;
        padding: 8px 13px;
        cursor: pointer;
    }
    .tab.on {
        color: var(--text);
        border-bottom-color: var(--cyan);
    }
    .chip {
        margin-left: var(--s-2);
        display: inline-flex;
        align-items: center;
        gap: var(--s-1);
        font-size: var(--f-ui, 12px);
        color: var(--text-dim);
        background: var(--bg-surface-2);
        border: 1px solid var(--border);
        border-radius: 99px;
        padding: 2px 10px;
    }
    .chip i {
        width: 7px;
        height: 7px;
        border-radius: 50%;
        background: var(--sub-tick);
    }
    .bar {
        display: flex;
        gap: var(--s-2);
        align-items: center;
        padding: 5px 8px;
        border-bottom: 1px solid var(--border);
        font-size: var(--f-ui, 12px);
        background: var(--bg-surface);
    }
    .bar input[type='search'] {
        flex: 1;
        min-width: 6rem;
        font: inherit;
        font-size: var(--f-ui, 12px);
        color: var(--text);
        background: var(--bg-void);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 8px;
    }
    .bar label {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: var(--text-dim);
        white-space: nowrap;
    }
    .bar button {
        font: inherit;
        font-size: var(--f-ui, 12px);
        color: var(--text);
        background: var(--bg-surface-2);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 9px;
        cursor: pointer;
    }
    table {
        width: 100%;
        border-collapse: collapse;
        font-size: var(--f-small, 12px);
    }
    thead th {
        position: sticky;
        top: 0;
        z-index: 1;
        background: var(--bg-base);
        text-align: right;
        font-weight: 500;
        color: var(--text-dim);
        padding: 4px 8px;
        border-bottom: 1px solid var(--border);
        font-size: var(--f-ui, 12px);
        white-space: nowrap;
    }
    thead th.name,
    thead th.pct:first-child {
        text-align: left;
    }
    th button {
        font: inherit;
        color: inherit;
        background: none;
        border: 0;
        padding: 0;
        cursor: pointer;
    }
    tbody td {
        padding: 2px 8px;
        border-bottom: 1px solid var(--border-soft);
        white-space: nowrap;
    }
    td.num {
        text-align: right;
        font-family: var(--font-mono);
    }
    td.name {
        width: 100%;
    }
    td.dim {
        color: var(--text-faint);
    }
    td.pct {
        width: 54px;
        padding-right: 0;
    }
    th.pct {
        width: 54px;
    }
    td.pct i {
        display: block;
        height: 7px;
        border-radius: 1px;
        background: var(--sub-none);
    }
    td.num.pct {
        width: 44px;
        padding-right: 6px;
        color: var(--text-dim);
    }
    td.delta.up {
        color: var(--bad);
    }
    td.delta.dn {
        color: var(--good);
    }
    td.delta.flat {
        color: var(--text-faint);
    }
    tr.selected {
        background: var(--bg-surface-2);
    }
    tbody tr:hover {
        background: var(--bg-surface);
    }
    .twist {
        display: inline-block;
        width: 13px;
        font: inherit;
        color: var(--text-faint);
        background: none;
        border: 0;
        padding: 0;
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
    .empty {
        padding: var(--s-3);
        color: var(--text-dim);
        font-size: var(--f-ui, 12px);
    }
</style>
