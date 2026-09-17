import { selfTimes } from './frameTable';
import type { TreeNode } from './frameTree';
import { buildModRows, modKeyFor, type SectionNames } from './modGroups';

export interface PieSlice {
    sectionId: number;
    label: string;
    subsystem: string | null;
    selfUs: number;
    /** 0..1 of the measured total, not of the frame */
    share: number;
}

export const OTHER_SECTION_ID = -1;

type Names = Map<number, { name: string; subsystem: string | null }>;

interface Folded<K> {
    head: [K, number][];
    tailUs: number;
    total: number;
}

/** biggest first, the top N kept as-is and everything past it summed into one tail. */
function foldTail<K>(
    byKey: Map<K, number>,
    topN: number,
    tie: (a: K, b: K) => number,
): Folded<K> | null {
    let total = 0;
    for (const us of byKey.values()) total += us;
    if (total <= 0) return null;

    const sorted = [...byKey.entries()].sort((a, b) => b[1] - a[1] || tie(a[0], b[0]));
    return {
        head: sorted.slice(0, topN),
        tailUs: sorted.slice(topN).reduce((sum, [, us]) => sum + us, 0),
        total,
    };
}

/**
 * Self time per section, biggest first, with everything past `topN` folded into one slice.
 * Self rather than total, because total double counts every parent.
 */
export function pieSlices(
    nodes: readonly TreeNode[],
    names: Names,
    topN = 8,
    otherLabel = 'other',
): PieSlice[] {
    if (nodes.length === 0 || topN < 1) return [];

    const self = selfTimes(nodes);
    const bySection = new Map<number, number>();
    for (let i = 0; i < nodes.length; i++) {
        if (self[i] <= 0) continue;
        const id = nodes[i].sectionId;
        bySection.set(id, (bySection.get(id) ?? 0) + self[i]);
    }

    const folded = foldTail(bySection, topN, () => 0);
    if (!folded) return [];

    const slices: PieSlice[] = folded.head.map(([id, us]) => ({
        sectionId: id,
        label: names.get(id)?.name ?? `#${id}`,
        subsystem: names.get(id)?.subsystem ?? null,
        selfUs: us,
        share: us / folded.total,
    }));

    if (folded.tailUs > 0) {
        slices.push({
            sectionId: OTHER_SECTION_ID,
            label: otherLabel,
            subsystem: null,
            selfUs: folded.tailUs,
            share: folded.tailUs / folded.total,
        });
    }
    return slices;
}

export interface ModSlice {
    key: string;
    label: string;
    selfUs: number;
    /** 0..1 of the measured total, not of the frame */
    share: number;
}

export const OTHER_MOD_KEY = '';

/** pieSlices, bucketed by mod instead of by section. */
export function pieModSlices(
    nodes: readonly TreeNode[],
    names: SectionNames,
    topN = 8,
    otherLabel = 'other',
): ModSlice[] {
    if (nodes.length === 0 || topN < 1) return [];

    const byMod = new Map<string, number>();
    for (const row of buildModRows(nodes, names)) {
        if (row.selfUs > 0) byMod.set(row.key, row.selfUs);
    }

    const folded = foldTail(byMod, topN, (a, b) => a.localeCompare(b));
    if (!folded) return [];

    const slices: ModSlice[] = folded.head.map(([key, us]) => ({
        key,
        label: key,
        selfUs: us,
        share: us / folded.total,
    }));

    if (folded.tailUs > 0) {
        slices.push({
            key: OTHER_MOD_KEY,
            label: otherLabel,
            selfUs: folded.tailUs,
            share: folded.tailUs / folded.total,
        });
    }
    return slices;
}

export interface Arc<T = PieSlice> {
    slice: T;
    path: string;
}

/** Donut arcs for a 100x100 viewBox, starting at twelve o'clock and going clockwise. */
export function donutArcs<T extends { share: number }>(
    slices: readonly T[],
    inner = 27,
    outer = 46,
): Arc<T>[] {
    const cx = 50;
    const cy = 50;
    let from = -Math.PI / 2;

    return slices.map((slice) => {
        // a lone full-circle slice cannot be drawn as one arc, so nudge it just short of 360.
        const sweep = Math.min(slice.share, 0.9999) * Math.PI * 2;
        const to = from + sweep;
        const large = sweep > Math.PI ? 1 : 0;

        const p = (r: number, a: number) => `${cx + r * Math.cos(a)} ${cy + r * Math.sin(a)}`;
        const path =
            `M ${p(outer, from)} A ${outer} ${outer} 0 ${large} 1 ${p(outer, to)} ` +
            `L ${p(inner, to)} A ${inner} ${inner} 0 ${large} 0 ${p(inner, from)} Z`;

        from = to;
        return { slice, path };
    });
}

/** node indexes whose ancestor-or-self chain carries the section, on the full array. */
function underSection(nodes: readonly TreeNode[], sectionId: number): boolean[] {
    const inTree = new Array<boolean>(nodes.length).fill(false);
    for (let i = 0; i < nodes.length; i++) {
        if (nodes[i].sectionId === sectionId) {
            inTree[i] = true;
            continue;
        }
        const parent = nodes[i].parentIndex;
        if (parent >= 0 && inTree[parent]) inTree[i] = true;
    }
    return inTree;
}

/** the composition of one section's subtree: its self time plus each descendant section. */
export function pieSectionDrillSlices(
    nodes: readonly TreeNode[],
    names: Names,
    sectionId: number,
    topN = 8,
    otherLabel = 'other',
): PieSlice[] {
    if (nodes.length === 0 || topN < 1) return [];

    const inTree = underSection(nodes, sectionId);
    const self = selfTimes(nodes);
    const bySection = new Map<number, number>();
    for (let i = 0; i < nodes.length; i++) {
        if (!inTree[i] || self[i] <= 0) continue;
        const id = nodes[i].sectionId;
        bySection.set(id, (bySection.get(id) ?? 0) + self[i]);
    }
    return finishSectionSlices(bySection, names, topN, otherLabel);
}

/** the sections inside one mod, self-time basis, for the mods pie drill. */
export function pieModDrillSlices(
    nodes: readonly TreeNode[],
    names: SectionNames,
    modKey: string,
    topN = 8,
    otherLabel = 'other',
): PieSlice[] {
    if (nodes.length === 0 || topN < 1) return [];

    const self = selfTimes(nodes);
    const bySection = new Map<number, number>();
    for (let i = 0; i < nodes.length; i++) {
        if (self[i] <= 0) continue;
        const id = nodes[i].sectionId;
        if (modKeyFor(names.get(id)?.assembly ?? null) !== modKey) continue;
        bySection.set(id, (bySection.get(id) ?? 0) + self[i]);
    }
    return finishSectionSlices(bySection, names, topN, otherLabel);
}

function finishSectionSlices(
    bySection: Map<number, number>,
    names: Names,
    topN: number,
    otherLabel: string,
): PieSlice[] {
    const folded = foldTail(bySection, topN, () => 0);
    if (!folded) return [];

    const slices: PieSlice[] = folded.head.map(([id, us]) => ({
        sectionId: id,
        label: names.get(id)?.name ?? `#${id}`,
        subsystem: names.get(id)?.subsystem ?? null,
        selfUs: us,
        share: us / folded.total,
    }));

    if (folded.tailUs > 0) {
        slices.push({
            sectionId: OTHER_SECTION_ID,
            label: otherLabel,
            subsystem: null,
            selfUs: folded.tailUs,
            share: folded.tailUs / folded.total,
        });
    }
    return slices;
}
