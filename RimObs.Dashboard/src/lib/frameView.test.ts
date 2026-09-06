import { describe, it, expect } from 'vitest';
import {
    fitView,
    clampView,
    zoomAbout,
    panBy,
    hitTest,
    moveFocus,
    type ViewRange,
    type Focus,
} from './frameView';
import { buildFrameTree, type FrameData, type TreeNode } from './frameTree';

function node(
    depth: number,
    startUs: number,
    durUs: number,
    sectionId: number,
    nodeId: number,
    parentIndex = -1,
): TreeNode {
    return { sectionId, nodeId, parentIndex, depth, startUs, durUs, endUs: startUs + durUs };
}

// root [0,100) index 0; children [10,30) index 1 and [50,70) index 3; grandchild [15,20) index 2
// second, unrelated root [200,250) index 4 with its own child [210,220) index 5
const TREE: TreeNode[] = [
    node(0, 0, 100, 10, 100),
    node(1, 10, 20, 20, 200, 0),
    node(2, 15, 5, 30, 300, 1),
    node(1, 50, 20, 40, 400, 0),
    node(0, 200, 50, 50, 500),
    node(1, 210, 10, 60, 600, 4),
];

describe('fitView', () => {
    it('spans the whole frame', () => {
        expect(fitView(160)).toEqual({ startUs: 0, endUs: 160 });
    });
});

describe('clampView', () => {
    it('leaves a view that already fits', () => {
        expect(clampView({ startUs: 10, endUs: 20 }, 100)).toEqual({ startUs: 10, endUs: 20 });
    });

    it('slides a view that ran off the end back inside the frame', () => {
        expect(clampView({ startUs: 95, endUs: 115 }, 100)).toEqual({ startUs: 80, endUs: 100 });
    });

    it('slides a view that ran off the start back inside the frame', () => {
        expect(clampView({ startUs: -20, endUs: 0 }, 100)).toEqual({ startUs: 0, endUs: 20 });
    });

    it('never lets the span exceed the frame', () => {
        expect(clampView({ startUs: -50, endUs: 300 }, 100)).toEqual({ startUs: 0, endUs: 100 });
    });

    it('never lets the span go below half a microsecond', () => {
        const out = clampView({ startUs: 10, endUs: 10.1 }, 100);
        expect(out.endUs - out.startUs).toBeCloseTo(0.5);
    });
});

describe('zoomAbout', () => {
    it('keeps the anchor at the same fraction of the view', () => {
        const view: ViewRange = { startUs: 0, endUs: 100 };
        const out = zoomAbout(view, 25, 0.5);
        expect(out.endUs - out.startUs).toBeCloseTo(50);
        const before = (25 - view.startUs) / 100;
        const after = (25 - out.startUs) / (out.endUs - out.startUs);
        expect(after).toBeCloseTo(before);
    });

    it('widens the view when the factor is above one', () => {
        const out = zoomAbout({ startUs: 25, endUs: 75 }, 50, 2);
        expect(out.endUs - out.startUs).toBeCloseTo(100);
    });
});

describe('panBy', () => {
    it('shifts both ends by the same amount', () => {
        expect(panBy({ startUs: 10, endUs: 20 }, 5)).toEqual({ startUs: 15, endUs: 25 });
    });
});

describe('hitTest', () => {
    it('finds the node at a depth containing the time', () => {
        expect(hitTest(TREE, 1, 15)).toBe(1);
        expect(hitTest(TREE, 1, 55)).toBe(3);
        expect(hitTest(TREE, 2, 17)).toBe(2);
    });

    it('returns -1 in a gap', () => {
        expect(hitTest(TREE, 1, 40)).toBe(-1);
    });

    it('returns -1 below the deepest row', () => {
        expect(hitTest(TREE, 5, 15)).toBe(-1);
    });

    it('treats a node span as half open so touching neighbours do not both hit', () => {
        expect(hitTest(TREE, 1, 30)).toBe(-1);
        expect(hitTest(TREE, 1, 10)).toBe(1);
    });
});

describe('moveFocus', () => {
    it('steps right to the next node in the same row', () => {
        expect(moveFocus(TREE, 1, 'right')).toBe(3);
        expect(TREE[3].sectionId).toBe(40);
    });

    it('stays put at the last node in a row instead of crossing into a sibling root', () => {
        expect(moveFocus(TREE, 3, 'right')).toBe(3);
    });

    it('steps left to the previous node in the same row', () => {
        expect(moveFocus(TREE, 3, 'left')).toBe(1);
        expect(TREE[1].sectionId).toBe(20);
    });

    it('stays put at the first node in a row', () => {
        expect(moveFocus(TREE, 1, 'left')).toBe(1);
    });

    it('steps up to the parent', () => {
        expect(moveFocus(TREE, 2, 'up')).toBe(1);
        expect(TREE[1].sectionId).toBe(20);
    });

    it('steps up to its own parent even when that is not index minus one', () => {
        expect(moveFocus(TREE, 3, 'up')).toBe(0);
    });

    it('stays put at a root', () => {
        expect(moveFocus(TREE, 0, 'up')).toBe(0);
    });

    it('steps down to the first child', () => {
        expect(moveFocus(TREE, 1, 'down')).toBe(2);
        expect(TREE[2].sectionId).toBe(30);
    });

    it('stays put at a leaf', () => {
        expect(moveFocus(TREE, 2, 'down')).toBe(2);
    });
});

describe('Focus', () => {
    it('round-trips a Focus through hitTest', () => {
        const focus: Focus = { depth: 2, atUs: 17 };
        expect(hitTest(TREE, focus.depth, focus.atUs)).toBe(2);
    });
});

// regression guard: the old flamegraph mixed session-clock and frame-relative time.
describe('with a non-zero frame origin', () => {
    const ORIGIN = 5_000_000;
    const frameData: FrameData = {
        capture_ordinal: 1,
        start_us: ORIGIN,
        end_us: ORIGIN + 100,
        duration_us: 100,
        node_count: 2,
        nodes: {
            section_ids: [10, 20],
            parent_ids: [-1, 10],
            node_ids: [1, 2],
            parent_node_ids: [-1, 1],
            start_us: [ORIGIN, ORIGIN + 10],
            dur_us: [100, 20],
        },
    };
    const { nodes } = buildFrameTree(frameData);

    it('fits the view to frame-relative bounds, not the origin', () => {
        expect(fitView(frameData.duration_us)).toEqual({ startUs: 0, endUs: 100 });
    });

    it('hits the expected node with a frame-relative time', () => {
        expect(hitTest(nodes, 1, 15)).toBe(1);
    });

    it('misses when passed the absolute session time instead', () => {
        expect(hitTest(nodes, 1, ORIGIN + 15)).toBe(-1);
    });

    it('keeps a clamped view inside [0, duration], never near the origin', () => {
        const out = clampView({ startUs: ORIGIN - 10, endUs: ORIGIN + 10 }, frameData.duration_us);
        expect(out.startUs).toBeGreaterThanOrEqual(0);
        expect(out.endUs).toBeLessThanOrEqual(100);
    });
});
