import { selfTimes } from './frameTable';
import { NO_PARENT, type TreeNode } from './frameTree';

export type SectionNames = Map<
    number,
    { name: string; subsystem: string | null; assembly?: string | null }
>;

export const RIMWORLD_MOD = 'RimWorld';
export const UNITY_MOD = 'Unity';
export const UNKNOWN_MOD = 'unknown';

export interface ModRow {
    key: string;
    totalUs: number;
    selfUs: number;
    allocBytes: number;
    calls: number;
    /** sections that landed in this mod, so a row can expand to them */
    sectionIds: number[];
}

/**
 * The mod a section belongs to, always its declaring assembly. Section names are
 * `packageId.name`, and a packageId has no fixed shape, so the name cannot be split back.
 */
export function modKeyFor(assembly: string | null): string {
    if (!assembly) return UNKNOWN_MOD;
    if (assembly.startsWith('Assembly-CSharp')) return RIMWORLD_MOD;
    if (assembly.startsWith('UnityEngine')) return UNITY_MOD;
    return assembly;
}

export function modKeys(nodes: readonly TreeNode[], names: SectionNames): string[] {
    return nodes.map((n) => modKeyFor(names.get(n.sectionId)?.assembly ?? null));
}

/** true when some ancestor already counted this node's total under the same key. */
function nestedUnderSame(
    nodes: readonly TreeNode[],
    i: number,
    keyAt: (index: number) => string | number,
): boolean {
    let p = nodes[i].parentIndex;
    for (let hops = 0; hops < 256; hops++) {
        if (p === NO_PARENT || p < 0 || p >= nodes.length) return false;
        if (keyAt(p) === keyAt(i)) return true;
        p = nodes[p].parentIndex;
    }
    return false;
}

/** true when an ancestor runs the same section, so this node's inclusive totals repeat it. */
export function nestedInSameSection(nodes: readonly TreeNode[], i: number): boolean {
    return nestedUnderSame(nodes, i, (n) => nodes[n].sectionId);
}

/** Per-mod time and allocation for one frame, biggest total first. */
export function buildModRows(nodes: readonly TreeNode[], names: SectionNames): ModRow[] {
    const self = selfTimes(nodes);
    const keys = modKeys(nodes, names);

    const rows = new Map<string, ModRow>();
    const sections = new Map<string, Set<number>>();
    for (let i = 0; i < nodes.length; i++) {
        const key = keys[i];
        let row = rows.get(key);
        if (!row) {
            row = { key, totalUs: 0, selfUs: 0, allocBytes: 0, calls: 0, sectionIds: [] };
            rows.set(key, row);
            sections.set(key, new Set());
        }
        row.selfUs += self[i];
        row.calls += nodes[i].calls ?? 1;
        sections.get(key)?.add(nodes[i].sectionId);
        // total and alloc are inclusive, so only the outermost node of a mod may add them.
        if (!nestedUnderSame(nodes, i, (n) => keys[n])) {
            row.totalUs += nodes[i].durUs;
            row.allocBytes += nodes[i].allocBytes ?? 0;
        }
    }

    const out = [...rows.values()];
    for (const row of out) {
        row.sectionIds = [...(sections.get(row.key) ?? [])].sort((a, b) => a - b);
    }
    return out.sort((a, b) => b.totalUs - a.totalUs || a.key.localeCompare(b.key));
}
