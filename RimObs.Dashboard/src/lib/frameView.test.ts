import { describe, it, expect } from 'vitest';
import {
    fitView,
    clampView,
    zoomAbout,
    panBy,
    moveFocus,
    scrollContentPx,
    scrollLeftPx,
    viewFromScrollLeft,
    type ViewRange,
} from './frameView';
import { type TreeNode } from './frameTree';

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

const BOUNDS: ViewRange = { startUs: 0, endUs: 100 };

describe('fitView', () => {
    it('spans the whole window', () => {
        expect(fitView({ startUs: 0, endUs: 160 })).toEqual({ startUs: 0, endUs: 160 });
    });

    it('starts at the window origin, not at zero', () => {
        expect(fitView({ startUs: 5_000_000, endUs: 5_000_160 })).toEqual({
            startUs: 5_000_000,
            endUs: 5_000_160,
        });
    });
});

describe('clampView', () => {
    it('leaves a view that already fits', () => {
        expect(clampView({ startUs: 10, endUs: 20 }, BOUNDS)).toEqual({ startUs: 10, endUs: 20 });
    });

    it('slides a view that ran off the end back inside the frame', () => {
        expect(clampView({ startUs: 95, endUs: 115 }, BOUNDS)).toEqual({ startUs: 80, endUs: 100 });
    });

    it('slides a view that ran off the start back inside the frame', () => {
        expect(clampView({ startUs: -20, endUs: 0 }, BOUNDS)).toEqual({ startUs: 0, endUs: 20 });
    });

    it('never lets the span exceed the frame', () => {
        expect(clampView({ startUs: -50, endUs: 300 }, BOUNDS)).toEqual({ startUs: 0, endUs: 100 });
    });

    it('never lets the span go below half a microsecond', () => {
        const out = clampView({ startUs: 10, endUs: 10.1 }, BOUNDS);
        expect(out.endUs - out.startUs).toBeCloseTo(0.5);
    });

    it('clamps against a window that does not start at zero', () => {
        const bounds = { startUs: 5_000_000, endUs: 5_000_100 };
        expect(clampView({ startUs: 0, endUs: 20 }, bounds)).toEqual({
            startUs: 5_000_000,
            endUs: 5_000_020,
        });
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

describe('the scrollbar proxy', () => {
    const bounds: ViewRange = { startUs: 1000, endUs: 2000 };

    it('makes the spacer as many times the track as the view is zoomed in', () => {
        expect(scrollContentPx({ startUs: 1000, endUs: 1100 }, bounds, 600)).toBe(6000);
    });

    it('never makes the spacer narrower than the track', () => {
        expect(scrollContentPx({ startUs: 1000, endUs: 2000 }, bounds, 600)).toBe(600);
    });

    it('pins the thumb to the left at the start of the window', () => {
        const content = scrollContentPx({ startUs: 1000, endUs: 1100 }, bounds, 600);
        expect(scrollLeftPx({ startUs: 1000, endUs: 1100 }, bounds, 600, content)).toBe(0);
    });

    it('pins the thumb to the right at the end of the window', () => {
        const view = { startUs: 1900, endUs: 2000 };
        const content = scrollContentPx(view, bounds, 600);
        expect(scrollLeftPx(view, bounds, 600, content)).toBe(content - 600);
    });

    // the spacer is capped at a million px, so past that the offset has to be scaled by the
    // travel the thumb actually has, not by the raw window.
    it('still reaches the right edge once the spacer hits its cap', () => {
        const view = { startUs: 1999.5, endUs: 2000 };
        const content = scrollContentPx(view, bounds, 600);
        expect(content).toBe(1_000_000);
        expect(scrollLeftPx(view, bounds, 600, content)).toBe(content - 600);
    });

    it('round-trips a view through the scroll offset', () => {
        const view = { startUs: 1400, endUs: 1500 };
        const content = scrollContentPx(view, bounds, 600);
        const left = scrollLeftPx(view, bounds, 600, content);
        const back = viewFromScrollLeft(left, view, bounds, 600, content);
        expect(back.startUs).toBeCloseTo(1400, 1);
        expect(back.endUs).toBeCloseTo(1500, 1);
    });

    it('keeps a scroll past the end inside the window', () => {
        const view = { startUs: 1400, endUs: 1500 };
        const content = scrollContentPx(view, bounds, 600);
        const back = viewFromScrollLeft(content * 10, view, bounds, 600, content);
        expect(back.endUs).toBeLessThanOrEqual(2000);
    });
});
