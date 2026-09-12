import { describe, it, expect } from 'vitest';
import { foldFrame, layoutFrame, quadIndexForNode, type LayoutOptions } from './frameLayout';
import type { TreeNode } from './frameTree';
import { laneBands, laneRow, ThreadRole } from './threadLanes';
import { aggregateNodes } from './frameSeries';

function node(depth: number, startUs: number, durUs: number, sectionId = 1): TreeNode {
    return {
        sectionId,
        nodeId: 0,
        parentIndex: -1,
        depth,
        startUs,
        durUs,
        endUs: startUs + durUs,
    };
}

// 1000 us across 1000 px: one microsecond is one pixel, so the numbers read directly
const OPTS: LayoutOptions = {
    viewStartUs: 0,
    viewEndUs: 1000,
    widthPx: 1000,
    maxDepth: 32,
    minWidthPx: 2,
    minVisibleDurationUs: 0,
};

describe('foldFrame', () => {
    it('keeps a node at or above the threshold', () => {
        const folded = foldFrame([node(0, 0, 10)], 10);
        expect(folded[0]).toBe(0);
    });

    it('folds a node below the threshold', () => {
        const folded = foldFrame([node(0, 0, 9)], 10);
        expect(folded[0]).toBe(1);
    });

    it('folds the whole subtree under a folded node', () => {
        // a child cannot outlast its parent, but clock skew can make it look that way.
        // the subtree must go regardless, or a folded parent leaves a floating child.
        const tree = [node(0, 0, 5), { ...node(1, 0, 100), parentIndex: 0 }];
        const folded = foldFrame(tree, 10);
        expect(Array.from(folded)).toEqual([1, 1]);
    });

    it('keeps a deep node whose ancestors all survive', () => {
        const tree = [node(0, 0, 100), { ...node(1, 0, 50), parentIndex: 0 }];
        expect(Array.from(foldFrame(tree, 10))).toEqual([0, 0]);
    });

    it('folds nothing at a zero threshold', () => {
        const tree = [node(0, 0, 100), node(1, 0, 0)];
        expect(Array.from(foldFrame(tree, 0))).toEqual([0, 0]);
    });
});

describe('layoutFrame', () => {
    it('returns nothing for an empty tree', () => {
        expect(layoutFrame([], OPTS)).toEqual([]);
    });

    it('leaves a wide node alone', () => {
        const quads = layoutFrame([node(0, 100, 400)], OPTS);
        expect(quads).toHaveLength(1);
        expect(quads[0]).toMatchObject({ depth: 0, startUs: 100, endUs: 500, count: 1 });
    });

    it('merges a run of adjacent narrow siblings into one quad', () => {
        const tree = [node(1, 10, 1), node(1, 11, 1), node(1, 12, 1)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads).toHaveLength(1);
        expect(quads[0]).toMatchObject({ depth: 1, startUs: 10, endUs: 13, count: 3 });
    });

    it('breaks a run where the gap is wider than the threshold', () => {
        const tree = [node(1, 10, 1), node(1, 11, 1), node(1, 500, 1), node(1, 501, 1)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads).toHaveLength(2);
        expect(quads[0]).toMatchObject({ startUs: 10, endUs: 12, count: 2 });
        expect(quads[1]).toMatchObject({ startUs: 500, endUs: 502, count: 2 });
        expect(quads[0].firstIndex).toBe(0);
        expect(quads[1].firstIndex).toBe(2);
    });

    it('never merges across depths', () => {
        const tree = [node(1, 10, 1), node(2, 10, 1)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads).toHaveLength(2);
        expect(quads.map((q) => q.depth)).toEqual([1, 2]);
    });

    it('keeps the section id when a run is all one section, and drops it when mixed', () => {
        const same = layoutFrame([node(1, 10, 1, 5), node(1, 11, 1, 5)], OPTS);
        expect(same[0]).toMatchObject({ sectionId: 5, count: 2 });
        const mixed = layoutFrame([node(1, 10, 1, 5), node(1, 11, 1, 6)], OPTS);
        expect(mixed[0]).toMatchObject({ sectionId: -1, count: 2 });
    });

    it('drops nodes that end before the view starts or begin after it ends', () => {
        const tree = [node(0, 0, 50), node(0, 200, 100), node(0, 2000, 100)];
        const quads = layoutFrame(tree, { ...OPTS, viewStartUs: 100, viewEndUs: 1000 });
        expect(quads).toHaveLength(1);
        expect(quads[0]).toMatchObject({ startUs: 200 });
    });

    it('drops nodes deeper than maxDepth', () => {
        const tree = [node(0, 0, 500), node(1, 0, 400), node(2, 0, 300)];
        const quads = layoutFrame(tree, { ...OPTS, maxDepth: 2 });
        expect(quads.map((q) => q.depth)).toEqual([0, 1]);
    });

    it('reveals nodes that were collapsed once the view zooms in', () => {
        const tree = [node(1, 10, 1), node(1, 11, 1), node(1, 12, 1)];
        const zoomed = layoutFrame(tree, { ...OPTS, viewStartUs: 9, viewEndUs: 14 });
        expect(zoomed).toHaveLength(3);
        expect(zoomed.every((q) => q.count === 1)).toBe(true);
    });

    it('drops a stretch where every node folds', () => {
        const tree = [node(1, 10, 1), node(1, 11, 1), node(1, 12, 1)];
        const quads = layoutFrame(tree, { ...OPTS, minVisibleDurationUs: 2 });
        expect(quads).toHaveLength(0);
    });

    // minWidthPx 4 makes both 3us survivors narrow, so they are collapse candidates.
    // collapse-first would give count 3 and totalUs 7; fold-first gives 2 and 6.
    it('collapses only the survivors when fold takes some of a run', () => {
        const tree = [node(1, 10, 3), node(1, 14, 1), node(1, 16, 3)];
        const quads = layoutFrame(tree, { ...OPTS, minWidthPx: 4, minVisibleDurationUs: 2 });
        expect(quads).toHaveLength(1);
        expect(quads[0].count).toBe(2);
        expect(quads[0].totalUs).toBe(6);
    });

    // the folded node sits in the gap. drop it first and the 7us gap breaks the run,
    // merge first and it bridges.
    it('does not let a folded node bridge two runs', () => {
        const tree = [node(1, 0, 3), node(1, 6, 1), node(1, 10, 3)];
        const quads = layoutFrame(tree, { ...OPTS, minWidthPx: 4, minVisibleDurationUs: 2 });
        expect(quads).toHaveLength(2);
    });

    it('treats a node exactly at the width threshold as wide', () => {
        const tree = [node(1, 10, 2), node(1, 12, 2)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads).toHaveLength(2);
        expect(quads.every((q) => q.count === 1)).toBe(true);
    });

    it('merges across a gap exactly at the threshold', () => {
        const tree = [node(1, 10, 1), node(1, 13, 1)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads).toHaveLength(1);
        expect(quads[0].count).toBe(2);
    });

    it('returns nothing for a degenerate view or canvas', () => {
        const tree = [node(0, 0, 10)];
        expect(layoutFrame(tree, { ...OPTS, viewEndUs: 0 })).toEqual([]);
        expect(layoutFrame(tree, { ...OPTS, widthPx: 0 })).toEqual([]);
    });

    it('returns quads in depth then start order regardless of push order', () => {
        const tree = [node(1, 10, 1), node(2, 10, 1), node(2, 500, 400)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads.map((q) => [q.depth, q.startUs])).toEqual([
            [1, 10],
            [2, 10],
            [2, 500],
        ]);
    });

    it('sums the wall time inside a collapsed run', () => {
        const tree = [node(1, 10, 1), node(1, 12, 1)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads[0].totalUs).toBe(2);
        expect(quads[0].endUs - quads[0].startUs).toBe(3);
    });
});

describe('quadIndexForNode', () => {
    it('resolves a plain uncollapsed node to its own quad', () => {
        const tree = [node(0, 100, 400)];
        const quads = layoutFrame(tree, OPTS);
        const result = quadIndexForNode(quads, tree[0]);
        expect(result).toBe(0);
        expect(quads[result].firstIndex).toBe(0);
        expect(quads[result].count).toBe(1);
    });

    it('resolves both the first and a later member of a collapsed run to the run quad, not a neighbour', () => {
        const tree = [node(1, 10, 1), node(1, 11, 1), node(1, 500, 1), node(1, 501, 1)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads).toHaveLength(2);
        expect(quads[0].count).toBeGreaterThan(1);
        expect(quads[1].count).toBeGreaterThan(1);

        expect(quadIndexForNode(quads, tree[0])).toBe(0);
        expect(quadIndexForNode(quads, tree[1])).toBe(0);
        expect(quadIndexForNode(quads, tree[2])).toBe(1);
        expect(quadIndexForNode(quads, tree[3])).toBe(1);
    });

    it('resolves a folded node to -1', () => {
        const tree = [node(1, 10, 5)];
        const quads = layoutFrame(tree, { ...OPTS, minVisibleDurationUs: 10 });
        expect(quads).toHaveLength(0);
        expect(quadIndexForNode(quads, tree[0])).toBe(-1);
    });

    it('resolves a node deeper than maxDepth to -1', () => {
        const tree = [node(0, 0, 500), node(1, 0, 400), node(2, 0, 300)];
        const quads = layoutFrame(tree, { ...OPTS, maxDepth: 2 });
        expect(quadIndexForNode(quads, tree[2])).toBe(-1);
    });

    it('resolves a node entirely outside the view to -1', () => {
        const tree = [node(0, 0, 50), node(0, 200, 100), node(0, 2000, 100)];
        const quads = layoutFrame(tree, { ...OPTS, viewStartUs: 100, viewEndUs: 1000 });
        expect(quadIndexForNode(quads, tree[0])).toBe(-1);
        expect(quadIndexForNode(quads, tree[2])).toBe(-1);
    });

    it('does not let a node at the same start time but a different depth resolve to the wrong row', () => {
        const tree = [node(1, 10, 1), node(2, 10, 1)];
        const quads = layoutFrame(tree, OPTS);
        const depth1 = quadIndexForNode(quads, tree[0]);
        const depth2 = quadIndexForNode(quads, tree[1]);
        expect(quads[depth1].depth).toBe(1);
        expect(quads[depth2].depth).toBe(2);
        expect(depth1).not.toBe(depth2);
    });

    it('resolves a node starting exactly at a run boundary to -1, not the previous run', () => {
        const tree = [node(1, 10, 1), node(1, 11, 1)];
        const quads = layoutFrame(tree, OPTS);
        expect(quads).toHaveLength(1);
        expect(quads[0]).toMatchObject({ startUs: 10, endUs: 12 });

        const boundaryNode = node(1, 12, 5);
        expect(quadIndexForNode(quads, boundaryNode)).toBe(-1);
    });
});

describe('layoutFrame thread bands', () => {
    const lane = (n: TreeNode, laneId: number): TreeNode => ({ ...n, laneId });
    const bands = laneBands(
        [
            { id: 1, name: '', role: ThreadRole.Main, busy_ns: 0 },
            { id: 2, name: 'worker', role: ThreadRole.UnityJob, busy_ns: 0 },
        ],
        aggregateNodes([
            lane(node(0, 0, 100), 1),
            lane(node(1, 0, 50), 1),
            lane(node(0, 0, 100), 2),
        ]).laneDepth,
        128,
    );

    it('drops a worker quad below the rows main needs', () => {
        const tree = [lane(node(0, 0, 400), 1), lane(node(1, 0, 400), 1), lane(node(0, 0, 400), 2)];
        const quads = layoutFrame(tree, { ...OPTS, bands });
        expect(quads.map((q) => q.depth)).toEqual([0, 1, 3]);
        // the tint follows nesting inside the lane, so a band never starts out washed out.
        expect(quads.map((q) => q.nest)).toEqual([0, 1, 0]);
    });

    it('draws nothing for a lane the filter turned off', () => {
        const tree = [lane(node(0, 0, 400), 1), lane(node(0, 0, 400), 3)];
        const quads = layoutFrame(tree, { ...OPTS, bands });
        expect(quads).toHaveLength(1);
        expect(quads[0].firstIndex).toBe(0);
    });

    // two lanes both hold a depth 0, and merging them would draw one bar over the other.
    it('never collapses two lanes into one run', () => {
        const tree = [lane(node(0, 10, 1), 1), lane(node(0, 11, 1), 2), lane(node(0, 12, 1), 1)];
        const quads = layoutFrame(tree, { ...OPTS, bands });
        expect(quads.map((q) => q.depth)).toEqual([0, 3]);
        expect(quads[0].count).toBe(2);
        expect(quads[1].count).toBe(1);
    });

    it('finds the quad for a node in the second band', () => {
        const tree = [lane(node(0, 0, 400), 1), lane(node(0, 0, 400), 2)];
        const quads = layoutFrame(tree, { ...OPTS, bands });
        expect(quadIndexForNode(quads, tree[1], laneRow(bands, tree[1]))).toBe(1);
    });
});
