import { describe, it, expect } from 'vitest';
import {
    selfTimes,
    buildTreeRows,
    buildInvertedRows,
    keysToNode,
    allExpandableKeys,
    rowKey,
    labelFor,
    ROOT_KEY,
    UNPROFILED,
    type TableOptions,
} from './frameTable';
import type { TreeNode } from './frameTree';
import { NO_PARENT } from './frameTree';

const NAMES = new Map([
    [10, { name: 'Root_Play.Update', subsystem: 'render' }],
    [20, { name: 'TickList.Tick', subsystem: 'tick' }],
    [30, { name: 'Pawn.Tick', subsystem: 'ai' }],
]);

/** [sectionId, parentIndex, startUs, durUs] */
function tree(rows: [number, number, number, number][]): TreeNode[] {
    return rows.map(([sectionId, parentIndex, startUs, durUs], i) => ({
        sectionId,
        nodeId: i + 1,
        parentIndex,
        depth: 0,
        startUs,
        durUs,
        endUs: startUs + durUs,
    }));
}

const opts = (o: Partial<TableOptions> = {}): TableOptions => ({
    names: NAMES,
    expanded: new Set<string>(),
    ...o,
});

describe('selfTimes', () => {
    it('subtracts direct children from the parent', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 400],
            [20, 0, 400, 300],
        ]);
        expect(selfTimes(nodes)).toEqual([300, 400, 300]);
    });

    // a parent and its children are timed by separate Stopwatch reads.
    it('floors at zero when children overrun the parent', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 100],
            [20, 0, 0, 150],
        ]);
        expect(selfTimes(nodes)[0]).toBe(0);
    });
});

describe('buildTreeRows', () => {
    it('returns nothing for an empty frame', () => {
        expect(buildTreeRows([], opts())).toEqual([]);
    });

    it('groups siblings that share a section into one row', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 300],
            [20, 0, 300, 200],
        ]);
        const rows = buildTreeRows(nodes, opts({ expanded: new Set([rowKey(ROOT_KEY, 10)]) }));
        const tick = rows.find((r) => r.sectionId === 20)!;
        expect(tick.calls).toBe(2);
        expect(tick.totalUs).toBe(500);
    });

    it('leaves children out until the parent is expanded', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 300],
        ]);
        expect(buildTreeRows(nodes, opts()).map((r) => r.sectionId)).toEqual([10]);
        const open = buildTreeRows(nodes, opts({ expanded: new Set([rowKey(ROOT_KEY, 10)]) }));
        expect(open.map((r) => r.sectionId)).toContain(20);
    });

    it('adds an Unprofiled row for the parent time no child claimed', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 300],
        ]);
        const rows = buildTreeRows(nodes, opts({ expanded: new Set([rowKey(ROOT_KEY, 10)]) }));
        const un = rows.find((r) => r.sectionId === UNPROFILED)!;
        expect(un.totalUs).toBe(700);
        expect(labelFor(UNPROFILED, NAMES)).toBe('Unprofiled');
    });

    // DoSingleTick nests ticks inside ticks; without folding that draws a staircase.
    it('absorbs a child that repeats its parent section', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 800],
            [20, 1, 0, 500],
            [30, 2, 0, 200],
        ]);
        const expanded = new Set([rowKey(ROOT_KEY, 10), '/10/20']);
        const rows = buildTreeRows(nodes, opts({ expanded }));
        expect(rows.filter((r) => r.sectionId === 20)).toHaveLength(1);
        expect(rows.some((r) => r.sectionId === 30)).toBe(true);
    });

    it('keeps the repeat nested when folding is off', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 800],
            [20, 1, 0, 500],
        ]);
        const expanded = new Set([rowKey(ROOT_KEY, 10), '/10/20']);
        const rows = buildTreeRows(nodes, opts({ expanded, foldRecursion: false }));
        expect(rows.filter((r) => r.sectionId === 20).length).toBeGreaterThan(1);
    });

    it('sorts by the chosen column in both directions', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 100],
            [20, NO_PARENT, 0, 900],
        ]);
        expect(buildTreeRows(nodes, opts()).map((r) => r.sectionId)).toEqual([20, 10]);
        expect(buildTreeRows(nodes, opts({ ascending: true })).map((r) => r.sectionId)).toEqual([
            10, 20,
        ]);
        expect(
            buildTreeRows(nodes, opts({ sortColumn: 'label', ascending: true })).map(
                (r) => r.sectionId,
            ),
        ).toEqual([10, 20]);
    });

    it('filters rows by a case-insensitive label search', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 100],
            [20, NO_PARENT, 0, 900],
        ]);
        const rows = buildTreeRows(nodes, opts({ search: 'ticklist' }));
        expect(rows.map((r) => r.sectionId)).toEqual([20]);
    });

    // without this a match one level down is invisible until you expand by hand.
    it('finds a match inside a collapsed subtree', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [30, 0, 0, 400],
        ]);
        expect(buildTreeRows(nodes, opts()).map((r) => r.sectionId)).toEqual([10]);
        const rows = buildTreeRows(nodes, opts({ search: 'pawn' }));
        expect(rows.map((r) => r.sectionId)).toEqual([30]);
    });

    it('carries the node indices so a row can reach the flame view', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [10, NO_PARENT, 1000, 500],
        ]);
        expect(buildTreeRows(nodes, opts())[0].nodes).toEqual([0, 1]);
    });
});

describe('buildInvertedRows', () => {
    it('ranks sections by their own self time, not their subtree', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 900],
        ]);
        const rows = buildInvertedRows(nodes, opts());
        expect(rows[0].sectionId).toBe(20);
        expect(rows[0].selfUs).toBe(900);
        expect(rows[1].selfUs).toBe(100);
    });

    it('skips sections with no self time of their own', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 500],
            [20, 0, 0, 500],
        ]);
        expect(buildInvertedRows(nodes, opts()).map((r) => r.sectionId)).toEqual([20]);
    });

    it('marks a row as having callers only when it has a parent', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 400],
        ]);
        const rows = buildInvertedRows(nodes, opts());
        expect(rows.find((r) => r.sectionId === 20)!.hasChildren).toBe(true);
        expect(rows.find((r) => r.sectionId === 10)!.hasChildren).toBe(false);
    });
});

describe('keysToNode', () => {
    it('walks the section path from the root down', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 400],
            [30, 1, 0, 100],
        ]);
        expect(keysToNode(nodes, 2)).toEqual(['/10', '/10/20', '/10/20/30']);
    });

    it('returns just the root key for a root node', () => {
        expect(keysToNode(tree([[10, NO_PARENT, 0, 5]]), 0)).toEqual(['/10']);
    });

    it('collects every key in the frame for expand-all', () => {
        const nodes = tree([
            [10, NO_PARENT, 0, 1000],
            [20, 0, 0, 400],
        ]);
        expect([...allExpandableKeys(nodes)].sort()).toEqual(['/10', '/10/20']);
    });
});

describe('search matches subsystem', () => {
    const NAMES = new Map([
        [1, { name: 'Verse.TickManager.DoSingleTick', subsystem: 'tick' }],
        [2, { name: 'Verse.MapDrawer.DrawMapMesh', subsystem: 'render' }],
    ]);
    const NODES = [
        { sectionId: 1, nodeId: 1, parentIndex: -1, depth: 0, startUs: 0, durUs: 100, endUs: 100 },
        {
            sectionId: 2,
            nodeId: 2,
            parentIndex: -1,
            depth: 0,
            startUs: 100,
            durUs: 100,
            endUs: 200,
        },
    ];

    it('narrows to a subsystem the name never mentions', () => {
        const rows = buildTreeRows(NODES, {
            names: NAMES,
            expanded: new Set<string>(),
            search: 'render',
        });
        expect(rows).toHaveLength(1);
        expect(rows[0].sectionId).toBe(2);
    });

    it('still matches on the name', () => {
        const rows = buildTreeRows(NODES, {
            names: NAMES,
            expanded: new Set<string>(),
            search: 'TickManager',
        });
        expect(rows).toHaveLength(1);
        expect(rows[0].sectionId).toBe(1);
    });
});
