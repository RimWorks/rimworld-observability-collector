import type { TreeNode } from './frameTree';
import { NO_PARENT } from './frameTree';

export type SortColumn = 'total' | 'self' | 'calls' | 'label';

export const ROOT_KEY = '';
/** Neo shows the parent's own unattributed time as a row. Same idea, same name. */
export const UNPROFILED = -1;

export interface TableRow {
    key: string;
    sectionId: number;
    depth: number;
    totalUs: number;
    selfUs: number;
    calls: number;
    hasChildren: boolean;
    expanded: boolean;
    /** node indices this row aggregates, so clicking a row can reach the flame view */
    nodes: number[];
}

export interface TableOptions {
    names: Map<number, { name: string; subsystem: string | null }>;
    expanded: ReadonlySet<string>;
    sortColumn?: SortColumn;
    ascending?: boolean;
    foldRecursion?: boolean;
    search?: string;
}

export function rowKey(parentKey: string, sectionId: number): string {
    return `${parentKey}/${sectionId}`;
}

export function labelFor(
    sectionId: number,
    names: Map<number, { name: string; subsystem: string | null }>,
): string {
    if (sectionId === UNPROFILED) return 'Unprofiled';
    return names.get(sectionId)?.name ?? `section ${sectionId}`;
}

/** durUs minus everything its direct children accounted for. */
export function selfTimes(nodes: readonly TreeNode[]): number[] {
    const self = nodes.map((n) => n.durUs);
    for (let i = 0; i < nodes.length; i++) {
        const p = nodes[i].parentIndex;
        if (p !== NO_PARENT && p >= 0 && p < self.length) self[p] -= nodes[i].durUs;
    }
    // clock skew between a parent and its children can push this a hair below zero.
    for (let i = 0; i < self.length; i++) if (self[i] < 0) self[i] = 0;
    return self;
}

function childrenOf(nodes: readonly TreeNode[]): number[][] {
    const kids: number[][] = nodes.map(() => []);
    for (let i = 0; i < nodes.length; i++) {
        const p = nodes[i].parentIndex;
        if (p !== NO_PARENT && p >= 0 && p < kids.length) kids[p].push(i);
    }
    return kids;
}

export function compareRows(
    a: TableRow,
    b: TableRow,
    column: SortColumn,
    ascending: boolean,
    names: Map<number, { name: string; subsystem: string | null }>,
): number {
    let n: number;
    if (column === 'self') n = a.selfUs - b.selfUs;
    else if (column === 'calls') n = a.calls - b.calls;
    else if (column === 'label')
        n = labelFor(a.sectionId, names).localeCompare(labelFor(b.sectionId, names));
    else n = a.totalUs - b.totalUs;
    if (!ascending) n = -n;
    // ties break on label so the order is stable between polls.
    if (n !== 0) return n;
    return labelFor(a.sectionId, names).localeCompare(labelFor(b.sectionId, names));
}

function newRow(key: string, sectionId: number, expanded: ReadonlySet<string>): TableRow {
    return {
        key,
        sectionId,
        depth: 0,
        totalUs: 0,
        selfUs: 0,
        calls: 0,
        hasChildren: false,
        expanded: expanded.has(key),
        nodes: [],
    };
}

// with foldRecursion a child repeating its parent's section is absorbed rather than nested,
// which stops DoSingleTick's repeated ticks from drawing a staircase.
function aggregateLevel(
    nodes: readonly TreeNode[],
    kids: readonly number[][],
    self: readonly number[],
    parents: readonly number[],
    parentKey: string,
    parentSectionId: number,
    opts: TableOptions,
): TableRow[] {
    const fold = opts.foldRecursion ?? true;
    const bySection = new Map<number, TableRow>();
    let unattributed = 0;

    const visit = (index: number, absorbing: boolean): void => {
        for (const child of kids[index]) {
            const sectionId = nodes[child].sectionId;
            if (fold && sectionId === parentSectionId) {
                const row = bySection.get(sectionId);
                if (row) {
                    row.selfUs += self[child];
                    row.calls += nodes[child].calls ?? 1;
                    row.nodes.push(child);
                }
                visit(child, true);
                continue;
            }
            let row = bySection.get(sectionId);
            if (!row) {
                row = newRow(rowKey(parentKey, sectionId), sectionId, opts.expanded);
                bySection.set(sectionId, row);
            }
            row.totalUs += nodes[child].durUs;
            row.selfUs += self[child];
            row.calls += nodes[child].calls ?? 1;
            row.nodes.push(child);
            if (kids[child].length > 0) row.hasChildren = true;
        }
        if (!absorbing) unattributed += self[index];
    };

    for (const p of parents) visit(p, false);

    const rows = [...bySection.values()];
    if (unattributed > 0 && parents.length > 0) {
        const row = newRow(rowKey(parentKey, UNPROFILED), UNPROFILED, opts.expanded);
        row.totalUs = unattributed;
        row.selfUs = unattributed;
        row.expanded = false;
        rows.push(row);
    }
    rows.sort((a, b) =>
        compareRows(a, b, opts.sortColumn ?? 'total', opts.ascending ?? false, opts.names),
    );
    return rows;
}

/** Flat display rows in draw order, walking only the subtrees that are expanded. */
export function buildTreeRows(nodes: readonly TreeNode[], opts: TableOptions): TableRow[] {
    if (nodes.length === 0) return [];
    // a search has to reach collapsed rows, or a match one level down is invisible.
    if (opts.search?.trim()) opts = { ...opts, expanded: allExpandableKeys(nodes) };
    const kids = childrenOf(nodes);
    const self = selfTimes(nodes);

    const out: TableRow[] = [];
    const walk = (parents: number[], parentKey: string, parentSectionId: number, depth: number) => {
        if (depth > 64) return;
        const level = aggregateLevel(nodes, kids, self, parents, parentKey, parentSectionId, opts);
        for (const row of level) {
            row.depth = depth;
            out.push(row);
            if (row.hasChildren && row.expanded) walk(row.nodes, row.key, row.sectionId, depth + 1);
        }
    };

    const top = new Map<number, TableRow>();
    for (let i = 0; i < nodes.length; i++) {
        if (nodes[i].parentIndex !== NO_PARENT) continue;
        const sectionId = nodes[i].sectionId;
        let row = top.get(sectionId);
        if (!row) {
            row = newRow(rowKey(ROOT_KEY, sectionId), sectionId, opts.expanded);
            top.set(sectionId, row);
        }
        row.totalUs += nodes[i].durUs;
        row.selfUs += self[i];
        row.calls += nodes[i].calls ?? 1;
        row.nodes.push(i);
        if (kids[i].length > 0) row.hasChildren = true;
    }
    const roots = [...top.values()].sort((a, b) =>
        compareRows(a, b, opts.sortColumn ?? 'total', opts.ascending ?? false, opts.names),
    );
    for (const row of roots) {
        out.push(row);
        if (row.hasChildren && row.expanded) walk(row.nodes, row.key, row.sectionId, 1);
    }
    return filterBySearch(out, opts);
}

/** Leaf-first: every section by its own self time, heaviest first. */
export function buildInvertedRows(nodes: readonly TreeNode[], opts: TableOptions): TableRow[] {
    if (nodes.length === 0) return [];
    const self = selfTimes(nodes);
    const bySection = new Map<number, TableRow>();
    for (let i = 0; i < nodes.length; i++) {
        if (self[i] <= 0) continue;
        const sectionId = nodes[i].sectionId;
        let row = bySection.get(sectionId);
        if (!row) {
            row = newRow(rowKey(ROOT_KEY, sectionId), sectionId, opts.expanded);
            bySection.set(sectionId, row);
        }
        row.selfUs += self[i];
        row.totalUs += self[i];
        row.calls += nodes[i].calls ?? 1;
        row.nodes.push(i);
        if (nodes[i].parentIndex !== NO_PARENT) row.hasChildren = true;
    }
    const rows = [...bySection.values()].sort((a, b) =>
        compareRows(a, b, opts.sortColumn ?? 'total', opts.ascending ?? false, opts.names),
    );
    return filterBySearch(rows, opts);
}

function filterBySearch(rows: TableRow[], opts: TableOptions): TableRow[] {
    const q = opts.search?.trim().toLowerCase();
    if (!q) return rows;
    return rows.filter((r) => {
        if (labelFor(r.sectionId, opts.names).toLowerCase().includes(q)) return true;
        // matching the subsystem is how you narrow to render or ai work in one keystroke
        return (opts.names.get(r.sectionId)?.subsystem ?? '').toLowerCase().includes(q);
    });
}

/** Every key on the path down to `nodeIndex`, so selecting a bar can open the tree to it. */
export function keysToNode(nodes: readonly TreeNode[], nodeIndex: number): string[] {
    const chain: number[] = [];
    for (
        let i = nodeIndex;
        i !== NO_PARENT && i >= 0 && i < nodes.length && chain.length < 64;
        i = nodes[i].parentIndex
    ) {
        chain.push(i);
    }
    chain.reverse();
    const keys: string[] = [];
    let key = ROOT_KEY;
    for (const i of chain) {
        key = rowKey(key, nodes[i].sectionId);
        keys.push(key);
    }
    return keys;
}

export function allExpandableKeys(nodes: readonly TreeNode[]): Set<string> {
    const keys = new Set<string>();
    for (let i = 0; i < nodes.length; i++) for (const k of keysToNode(nodes, i)) keys.add(k);
    return keys;
}
