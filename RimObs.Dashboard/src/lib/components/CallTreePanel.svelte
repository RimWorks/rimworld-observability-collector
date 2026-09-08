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
    import { api, type SectionTimeseriesResponse } from '../api';
    import LineChart from './LineChart.svelte';
    import { deltaSeverity } from '../frameCost';
    import { t } from '../i18n';
    import { SvelteSet } from 'svelte/reactivity';
    import Tooltip from './Tooltip.svelte';
    import Icon from './Icon.svelte';
    import PieChart from './PieChart.svelte';
    import { pieSlices } from '../pieSlices';

    let {
        nodes,
        names,
        selectedNode = -1,
        frameDurationUs = 0,
        baselineUs = new Map<number, number>(),
        onSelect,
        scope = $bindable('frame'),
        percentiles = new Map<number, { p50Us: number; p95Us: number; p99Us: number }>(),
        patchOwners = new Map<string, string[]>(),
    }: {
        nodes: readonly TreeNode[];
        names: Map<number, { name: string; subsystem: string | null }>;
        selectedNode?: number;
        frameDurationUs?: number;
        baselineUs?: Map<number, number>;
        onSelect?: (nodeIndex: number) => void;
        scope?: 'frame' | 'session';
        /** per-section percentiles over the whole session; empty in frame scope */
        percentiles?: Map<number, { p50Us: number; p95Us: number; p99Us: number }>;
        /** other mods patching each section's target method, keyed by section name */
        patchOwners?: Map<string, string[]>;
    } = $props();

    function ownersFor(sectionId: number): string[] {
        const name = names.get(sectionId)?.name;
        return (name && patchOwners.get(name)) || [];
    }

    const SCOPES = [
        { id: 'frame', label: 'tree.scope.frame' },
        { id: 'session', label: 'tree.scope.session' },
    ] as const;

    let expanded = $state(new SvelteSet<string>());

    // the per-second trend the Hotspots page used to own. session scope only: it is a session
    // ring, and it says nothing about the single frame on screen.
    let trendSectionId = $state<number | null>(null);
    let trend = $state<SectionTimeseriesResponse | null>(null);
    let trendLoading = $state(false);

    async function toggleTrend(sectionId: number): Promise<void> {
        if (trendSectionId === sectionId) {
            trendSectionId = null;
            return;
        }
        trendSectionId = sectionId;
        trend = null;
        trendLoading = true;
        try {
            const data = await api.sectionTimeseries(sectionId);
            if (trendSectionId === sectionId) trend = data;
        } finally {
            if (trendSectionId === sectionId) trendLoading = false;
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
            label: t('tree.trend.mean'),
            values: (trend?.points ?? []).map((p) => p.mean_ns),
            stroke: '--cyan',
            fill: 'rgba(57, 196, 212, 0.12)',
        },
    ]);
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

    let slices = $derived(
        activeTab === 'pie' ? pieSlices(nodes, names, 8, t('tree.pie.other')) : [],
    );

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
        <span class="chip" data-testid="thread-chip">
            <i></i>MainThread
            <b class="mono">{ns(frameDurationUs * 1000)}</b>
        </span>
    </div>

    <div class="bar">
        <span class="seg" role="group" aria-label={t('tree.scope')}>
            {#each SCOPES as s (s.id)}
                <button
                    type="button"
                    class:on={scope === s.id}
                    onclick={() => (scope = s.id)}
                    data-testid="scope-{s.id}">{t(s.label)}</button
                >
            {/each}
        </span>
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

    {#if activeTab === 'pie'}
        {#if slices.length === 0}
            <p class="empty" data-testid="pie-empty">{t('tree.empty')}</p>
        {:else}
            <PieChart
                {slices}
                onSelect={(sectionId) => {
                    const row = rows.find((r) => r.sectionId === sectionId);
                    if (row && row.nodes.length > 0) onSelect?.(row.nodes[0]);
                }}
            />
        {/if}
    {:else if activeTab !== 'tree'}
        <p class="empty" data-testid="tab-soon">{t('tree.soon')}</p>
    {:else if rows.length === 0}
        <p class="empty" data-testid="tree-empty">{t('tree.empty')}</p>
    {:else}
        <table>
            <colgroup>
                <col class="c-bar" />
                <col class="c-pct" />
                <col class="c-total" />
                <col class="c-self" />
                <col class="c-delta" />
                <col class="c-calls" />
                {#if scope === 'session'}
                    <col class="c-pctile" />
                    <col class="c-pctile" />
                    <col class="c-pctile" />
                {/if}
                <col class="c-alloc" />
                <col />
            </colgroup>
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
                    {#if scope === 'session'}
                        <th class="num">{t('tree.col.p50')}</th>
                        <th class="num">{t('tree.col.p95')}</th>
                        <th class="num">{t('tree.col.p99')}</th>
                    {/if}
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
                        {#if scope === 'session'}
                            {@const p = percentiles.get(row.sectionId)}
                            <td class="num" data-testid="tree-p50">{p ? ns(p.p50Us * 1000) : ''}</td
                            >
                            <td class="num">{p ? ns(p.p95Us * 1000) : ''}</td>
                            <td class="num">{p ? ns(p.p99Us * 1000) : ''}</td>
                        {/if}
                        <td class="num dim">&mdash;</td>
                        <td class="name" style="padding-left:{row.depth * 14 + 4}px">
                            {#if row.hasChildren}
                                <button
                                    type="button"
                                    class="twist"
                                    onclick={() => toggle(row)}
                                    aria-expanded={row.expanded}
                                    aria-label={labelFor(row.sectionId, names)}
                                >
                                    <Icon
                                        name={row.expanded ? 'chevronDown' : 'chevron'}
                                        size={13}
                                    />
                                </button>
                            {:else}
                                <span class="twist"></span>
                            {/if}
                            <button
                                type="button"
                                class="label"
                                onclick={() => row.nodes.length > 0 && onSelect?.(row.nodes[0])}
                                >{labelFor(row.sectionId, names)}</button
                            >
                            {#if ownersFor(row.sectionId).length > 0}
                                <Tooltip
                                    text={`${t('tip.tree.patched')} ${ownersFor(row.sectionId).join(', ')}`}
                                >
                                    <span class="patched" data-testid="patch-badge">
                                        <Icon name="probe" size={11} />
                                        {ownersFor(row.sectionId).length}
                                    </span>
                                </Tooltip>
                            {/if}
                            {#if scope === 'session'}
                                <button
                                    type="button"
                                    class="trend-toggle"
                                    aria-expanded={trendSectionId === row.sectionId}
                                    onclick={() => toggleTrend(row.sectionId)}
                                    data-testid="trend-toggle">{t('tree.trend.open')}</button
                                >
                            {/if}
                        </td>
                    </tr>
                    {#if scope === 'session' && trendSectionId === row.sectionId}
                        <tr class="trend-row">
                            <td colspan="11">
                                {#if trendLoading}
                                    <p class="trend-state">{t('tree.trend.loading')}</p>
                                {:else if (trend?.points.length ?? 0) === 0}
                                    <p class="trend-state">{t('tree.trend.empty')}</p>
                                {:else}
                                    <LineChart
                                        x={trendX}
                                        series={trendSeries}
                                        height={160}
                                        format={(n) => ns(n)}
                                        xFormat={(n) => `${n}s`}
                                    />
                                {/if}
                            </td>
                        </tr>
                    {/if}
                {/each}
            </tbody>
        </table>
    {/if}
</div>

<style>
    .panel {
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
        border-radius: 0;
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
    .chip b {
        color: var(--text);
        font-weight: 500;
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
    .seg {
        display: flex;
        border: 1px solid var(--border);
        overflow: hidden;
        background: var(--bg-surface-2);
    }
    .seg button {
        border: 0;
        border-radius: 0;
        background: none;
        color: var(--text-faint);
        padding: 3px 10px;
    }
    .seg button.on {
        background: color-mix(in srgb, var(--cyan) 20%, var(--bg-elev));
        color: var(--cyan-soft);
        font-weight: 500;
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
    .bar > button {
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
        table-layout: fixed;
        border-collapse: collapse;
        font-size: var(--f-small, 12px);
    }
    .c-bar {
        width: 54px;
    }
    .c-pct,
    .c-delta,
    .c-calls,
    .c-alloc {
        width: 75px;
    }
    .c-total,
    .c-self {
        width: 100px;
    }
    .c-pctile {
        width: 80px;
    }
    .patched {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        margin-left: var(--s-2);
        padding: 0 5px;
        border: 1px solid var(--border);
        border-radius: 99px;
        font-size: 0.68rem;
        line-height: 1.5;
        color: var(--text-faint);
    }
    .trend-toggle {
        margin-left: var(--s-2);
        font: inherit;
        font-size: var(--f-tiny, 11px);
        color: var(--text-faint);
        background: none;
        border: 0;
        border-radius: 0;
        padding: 0;
        cursor: pointer;
    }
    .trend-toggle:hover,
    .trend-toggle[aria-expanded='true'] {
        color: var(--cyan);
    }
    .trend-row > td {
        padding: var(--s-3) var(--rail) var(--s-4);
        background: var(--bg-surface);
    }
    .trend-state {
        margin: 0;
        color: var(--text-faint);
        font-size: var(--f-ui, 12px);
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
        overflow: hidden;
        text-overflow: ellipsis;
    }
    td.dim {
        color: var(--text-faint);
    }
    td.pct {
        padding-right: 0;
    }
    td.pct i {
        display: block;
        height: 7px;
        border-radius: 1px;
        background: var(--sub-none);
    }
    td.num.pct {
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
        display: inline-flex;
        align-items: center;
        width: 13px;
        height: 13px;
        vertical-align: middle;
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
