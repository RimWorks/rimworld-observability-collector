import { describe, it, expect } from 'vitest';
import { pieSlices, donutArcs, OTHER_SECTION_ID } from './pieSlices';
import type { TreeNode } from './frameTree';
import { NO_PARENT } from './frameTree';

function node(
    sectionId: number,
    startUs: number,
    durUs: number,
    parentIndex = NO_PARENT,
): TreeNode {
    return {
        sectionId,
        nodeId: sectionId * 100 + startUs,
        parentIndex,
        depth: parentIndex === NO_PARENT ? 0 : 1,
        startUs,
        durUs,
        endUs: startUs + durUs,
    };
}

const names = new Map([
    [1, { name: 'root', subsystem: 'tick' }],
    [2, { name: 'child', subsystem: 'render' }],
    [3, { name: 'other-a', subsystem: null }],
    [4, { name: 'other-b', subsystem: null }],
]);

describe('pieSlices', () => {
    it('returns nothing for an empty frame', () => {
        expect(pieSlices([], names)).toEqual([]);
    });

    // total double counts every parent, so a parent that is all child would swamp the chart.
    it('slices on self time, not total', () => {
        const slices = pieSlices([node(1, 0, 100), node(2, 0, 100, 0)], names);

        expect(slices).toHaveLength(1);
        expect(slices[0].label).toBe('child');
        expect(slices[0].share).toBe(1);
    });

    it('sums a section that appears several times', () => {
        const slices = pieSlices([node(1, 0, 30), node(1, 40, 10)], names);

        expect(slices[0].selfUs).toBe(40);
    });

    it('orders slices biggest first', () => {
        const slices = pieSlices([node(1, 0, 10), node(2, 20, 50), node(3, 80, 30)], names);

        expect(slices.map((s) => s.label)).toEqual(['child', 'other-a', 'root']);
    });

    it('folds everything past topN into one other slice', () => {
        const slices = pieSlices([node(1, 0, 40), node(2, 50, 30), node(3, 90, 20)], names, 1);

        expect(slices).toHaveLength(2);
        expect(slices[1].sectionId).toBe(OTHER_SECTION_ID);
        expect(slices[1].selfUs).toBe(50);
    });

    it('omits the other slice when nothing is left over', () => {
        const slices = pieSlices([node(1, 0, 40), node(2, 50, 30)], names, 8);

        expect(slices.map((s) => s.sectionId)).not.toContain(OTHER_SECTION_ID);
    });

    it('carries the subsystem through so a slice can be coloured', () => {
        const slices = pieSlices([node(2, 0, 10)], names);

        expect(slices[0].subsystem).toBe('render');
    });

    it('falls back to the id when a section has no registered name', () => {
        expect(pieSlices([node(99, 0, 10)], names)[0].label).toBe('#99');
    });

    it('shares always sum to one', () => {
        const slices = pieSlices([node(1, 0, 7), node(2, 10, 11), node(3, 30, 3)], names, 2);
        const sum = slices.reduce((n, s) => n + s.share, 0);

        expect(sum).toBeCloseTo(1, 10);
    });
});

describe('donutArcs', () => {
    it('produces one path per slice', () => {
        const arcs = donutArcs(pieSlices([node(1, 0, 40), node(2, 50, 30)], names));

        expect(arcs).toHaveLength(2);
        expect(arcs[0].path.startsWith('M ')).toBe(true);
    });

    // a 100% arc with identical start and end points draws nothing at all.
    it('keeps a single full slice just short of a closed circle', () => {
        const arcs = donutArcs(pieSlices([node(1, 0, 40)], names));

        expect(arcs[0].path).toContain('A');
        expect(arcs[0].path).not.toMatch(/NaN/);
    });

    it('never emits NaN for an awkward split', () => {
        const arcs = donutArcs(pieSlices([node(1, 0, 1), node(2, 5, 999)], names));

        for (const a of arcs) expect(a.path).not.toMatch(/NaN/);
    });
});
