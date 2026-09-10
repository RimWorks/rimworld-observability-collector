import { describe, it, expect } from 'vitest';
import {
    searchCatalog,
    matchOccurrences,
    stepCursor,
    resolveCursorIndex,
    hideMask,
} from './sectionSearch';
import type { TreeNode } from './frameTree';

const NAMES = new Map([
    [10, { name: 'Verse.TickManager.DoSingleTick', subsystem: 'tick' }],
    [30, { name: 'Verse.TickList.Tick', subsystem: 'tick' }],
    [161, { name: 'Unity.Camera.Cull', subsystem: 'render' }],
]);

function node(over: Partial<TreeNode>): TreeNode {
    return {
        sectionId: 0,
        nodeId: 0,
        parentIndex: -1,
        depth: 0,
        startUs: 0,
        durUs: 0,
        endUs: 0,
        ...over,
    };
}

describe('searchCatalog', () => {
    it('matches case-insensitively over the whole catalog, not just sampled sections', () => {
        const results = searchCatalog('cull', NAMES, new Set());
        expect(results).toHaveLength(1);
        expect(results[0]).toEqual({
            id: 161,
            name: 'Unity.Camera.Cull',
            subsystem: 'render',
            sampled: false,
        });
    });

    // this is the exact case the feature was built for: a section is registered but never
    // fires in the current frame, and search has to say so instead of "0 matches".
    it('flags a registered section with zero nodes as unsampled, not absent', () => {
        const results = searchCatalog('Camera.Cull', NAMES, new Set([10]));
        expect(results).toHaveLength(1);
        expect(results[0].sampled).toBe(false);
    });

    it('flags a match as sampled when its section fired in the current frame', () => {
        const results = searchCatalog('TickList', NAMES, new Set([30]));
        expect(results[0].sampled).toBe(true);
    });

    it('returns nothing for a blank query', () => {
        expect(searchCatalog('   ', NAMES, new Set())).toEqual([]);
    });

    it('returns nothing when nothing matches', () => {
        expect(searchCatalog('nonexistent', NAMES, new Set())).toEqual([]);
    });
});

describe('matchOccurrences', () => {
    it('collects every node whose section matches, time-ordered', () => {
        const nodes = [
            node({ sectionId: 30, startUs: 100 }),
            node({ sectionId: 10, startUs: 0 }),
            node({ sectionId: 30, startUs: 50 }),
        ];
        const occ = matchOccurrences(nodes, new Set([30]));
        expect(occ.map((o) => o.nodeIndex)).toEqual([2, 0]);
    });

    it('is empty for a catalog-only match with no nodes in the window', () => {
        const nodes = [node({ sectionId: 10, startUs: 0 })];
        expect(matchOccurrences(nodes, new Set([161]))).toEqual([]);
    });
});

describe('stepCursor', () => {
    const occ = matchOccurrences(
        [
            node({ sectionId: 30, startUs: 0 }),
            node({ sectionId: 30, startUs: 100 }),
            node({ sectionId: 30, startUs: 200 }),
        ],
        new Set([30]),
    );

    it('starts at the first occurrence going forward', () => {
        expect(stepCursor(occ, null, 1)).toEqual({ sectionId: 30, startUs: 0 });
    });

    it('starts at the last occurrence going backward', () => {
        expect(stepCursor(occ, null, -1)).toEqual({ sectionId: 30, startUs: 200 });
    });

    it('wraps from the last match back to the first', () => {
        const atLast = { sectionId: 30, startUs: 200 };
        expect(stepCursor(occ, atLast, 1)).toEqual({ sectionId: 30, startUs: 0 });
    });

    it('wraps from the first match back to the last going backward', () => {
        const atFirst = { sectionId: 30, startUs: 0 };
        expect(stepCursor(occ, atFirst, -1)).toEqual({ sectionId: 30, startUs: 200 });
    });

    // the live window slides every poll, so a cursor pinned to an evicted node must recover
    // instead of throwing or silently resetting to the first match every time.
    it('recovers to the nearest surviving occurrence when the cursor node was evicted', () => {
        const evicted = { sectionId: 30, startUs: 9999 };
        expect(stepCursor(occ, evicted, 1)).toEqual({ sectionId: 30, startUs: 200 });
    });
});

describe('resolveCursorIndex', () => {
    it('finds the live node index for a stable cursor', () => {
        const nodes = [node({ sectionId: 30, startUs: 0 }), node({ sectionId: 30, startUs: 100 })];
        expect(resolveCursorIndex(nodes, { sectionId: 30, startUs: 100 })).toBe(1);
    });

    it('returns -1 once the cursor node has scrolled out of the window', () => {
        const nodes = [node({ sectionId: 30, startUs: 0 })];
        expect(resolveCursorIndex(nodes, { sectionId: 30, startUs: 999 })).toBe(-1);
    });
});

describe('hideMask', () => {
    it('keeps a matching leaf and every ancestor on its path', () => {
        // root(0) -> mid(1) -> leaf(2, matches) ; root(0) -> other(3, no match)
        const nodes = [
            node({ sectionId: 1, parentIndex: -1 }),
            node({ sectionId: 2, parentIndex: 0 }),
            node({ sectionId: 30, parentIndex: 1 }),
            node({ sectionId: 3, parentIndex: 0 }),
        ];
        const hidden = hideMask(nodes, new Set([30]));
        expect(Array.from(hidden)).toEqual([0, 0, 0, 1]);
    });

    it('hides everything when nothing matches', () => {
        const nodes = [node({ sectionId: 1 }), node({ sectionId: 2 })];
        expect(Array.from(hideMask(nodes, new Set([999])))).toEqual([1, 1]);
    });
});
