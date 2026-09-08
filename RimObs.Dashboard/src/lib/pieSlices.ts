import { selfTimes } from './frameTable';
import type { TreeNode } from './frameTree';

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

    const total = [...bySection.values()].reduce((sum, us) => sum + us, 0);
    if (total <= 0) return [];

    const sorted = [...bySection.entries()].sort((a, b) => b[1] - a[1]);
    const head = sorted.slice(0, topN);
    const tailUs = sorted.slice(topN).reduce((sum, [, us]) => sum + us, 0);

    const slices: PieSlice[] = head.map(([id, us]) => ({
        sectionId: id,
        label: names.get(id)?.name ?? `#${id}`,
        subsystem: names.get(id)?.subsystem ?? null,
        selfUs: us,
        share: us / total,
    }));

    if (tailUs > 0) {
        slices.push({
            sectionId: OTHER_SECTION_ID,
            label: otherLabel,
            subsystem: null,
            selfUs: tailUs,
            share: tailUs / total,
        });
    }
    return slices;
}

export interface Arc {
    slice: PieSlice;
    path: string;
}

/** Donut arcs for a 100x100 viewBox, starting at twelve o'clock and going clockwise. */
export function donutArcs(slices: readonly PieSlice[], inner = 27, outer = 46): Arc[] {
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
