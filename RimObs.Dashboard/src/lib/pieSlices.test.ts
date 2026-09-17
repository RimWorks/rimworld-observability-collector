import { describe, it, expect } from 'vitest';
import {
    pieSlices,
    pieModSlices,
    pieSectionDrillSlices,
    pieModDrillSlices,
    donutArcs,
    OTHER_SECTION_ID,
    OTHER_MOD_KEY,
} from './pieSlices';
import { buildModRows, type SectionNames } from './modGroups';
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

describe('pieModSlices', () => {
    const modNames: SectionNames = new Map([
        [1, { name: 'author.mod.work', subsystem: null, assembly: 'AuthorMod' }],
        [2, { name: 'author.mod.more', subsystem: null, assembly: 'AuthorMod' }],
        [
            3,
            {
                name: 'Verse.TickManager.DoSingleTick',
                subsystem: 'tick',
                assembly: 'Assembly-CSharp',
            },
        ],
        [
            4,
            {
                name: 'UnityEngine.Camera.Render',
                subsystem: 'render',
                assembly: 'UnityEngine.CoreModule',
            },
        ],
        [5, { name: 'Unity.Camera.Cull', subsystem: 'render', assembly: null }],
    ]);

    it('sums self time per mod, biggest first', () => {
        const slices = pieModSlices(
            [node(1, 0, 30), node(2, 40, 20), node(3, 70, 70), node(4, 150, 10)],
            modNames,
        );

        expect(slices.map((s) => s.key)).toEqual(['RimWorld', 'AuthorMod', 'Unity']);
        expect(slices[1].selfUs).toBe(50);
    });

    it('folds everything past topN into one other slice', () => {
        const slices = pieModSlices(
            [node(3, 0, 60), node(1, 70, 30), node(5, 110, 10)],
            modNames,
            1,
            'rest',
        );

        expect(slices).toHaveLength(2);
        expect(slices[1]).toMatchObject({ key: OTHER_MOD_KEY, label: 'rest', selfUs: 40 });
        expect(slices.reduce((n, s) => n + s.share, 0)).toBeCloseTo(1, 10);
    });

    it('returns nothing for an empty frame', () => {
        expect(pieModSlices([], modNames)).toEqual([]);
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

// one frame, read two ways: the pie has to say the same thing the by-mod tree says.
describe('pieModSlices against the by-mod tree', () => {
    const frameNames: SectionNames = new Map([
        [
            1,
            {
                name: 'Verse.TickManager.DoSingleTick',
                subsystem: 'tick',
                assembly: 'Assembly-CSharp',
            },
        ],
        [2, { name: 'Verse.TickList.Tick', subsystem: 'tick', assembly: 'Assembly-CSharp' }],
        [3, { name: 'author.mod.Work', subsystem: null, assembly: 'AuthorMod' }],
        [4, { name: 'author.mod.Inner', subsystem: null, assembly: 'AuthorMod' }],
        [5, { name: 'other.mod.Scan', subsystem: 'ai', assembly: 'OtherMod' }],
        [
            6,
            {
                name: 'UnityEngine.Camera.Render',
                subsystem: 'render',
                assembly: 'UnityEngine.CoreModule',
            },
        ],
    ]);

    // each mod's work sits in its own root subtree, the way a frame really splits up.
    const frame = [
        node(1, 0, 4000),
        node(2, 500, 1500, 0),
        node(3, 4000, 3000),
        node(4, 4200, 1200, 2),
        node(5, 7000, 2000),
        node(6, 9000, 1000),
    ];

    it('top-N plus other adds up to the same mod totals the tree shows', () => {
        // pieModSlices buckets on self time, so compare against selfUs, not totalUs.
        const totals = new Map(buildModRows(frame, frameNames).map((r) => [r.key, r.selfUs]));
        const slices = pieModSlices(frame, frameNames, 3, 'other');

        expect(slices.map((s) => s.key)).toEqual([
            'RimWorld',
            'AuthorMod',
            'OtherMod',
            OTHER_MOD_KEY,
        ]);
        for (const slice of slices.slice(0, 3)) {
            expect(slice.selfUs, slice.key).toBe(totals.get(slice.key));
        }
        // the tail is every mod past the top three, here Unity on its own.
        expect(slices[3].selfUs).toBe(totals.get('Unity'));
        expect(slices.reduce((n, s) => n + s.selfUs, 0)).toBe(10_000);
        expect(slices.reduce((n, s) => n + s.share, 0)).toBeCloseTo(1, 10);
    });

    it('drops the tail once topN covers every mod', () => {
        const slices = pieModSlices(frame, frameNames, 8, 'other');

        expect(slices.map((s) => s.key)).toEqual(['RimWorld', 'AuthorMod', 'OtherMod', 'Unity']);
        expect(slices.map((s) => s.selfUs)).toEqual([4000, 3000, 2000, 1000]);
    });
});

// clicking into a slice re-scopes the donut: a section to its subtree, a mod to its sections.
describe('pie drill slices', () => {
    const names = new Map([
        [1, { name: 'Root', subsystem: null, assembly: 'Assembly-CSharp' }],
        [2, { name: 'ChildA', subsystem: null, assembly: 'Assembly-CSharp' }],
        [3, { name: 'ChildB', subsystem: null, assembly: 'ModAsm' }],
        [4, { name: 'Elsewhere', subsystem: null, assembly: 'ModAsm' }],
    ]);
    const nodes: TreeNode[] = [
        {
            sectionId: 1,
            nodeId: 1,
            parentIndex: NO_PARENT,
            depth: 0,
            startUs: 0,
            durUs: 100,
            endUs: 100,
            allocBytes: 0,
        },
        {
            sectionId: 2,
            nodeId: 2,
            parentIndex: 0,
            depth: 1,
            startUs: 0,
            durUs: 40,
            endUs: 40,
            allocBytes: 0,
        },
        {
            sectionId: 3,
            nodeId: 3,
            parentIndex: 0,
            depth: 1,
            startUs: 40,
            durUs: 30,
            endUs: 70,
            allocBytes: 0,
        },
        {
            sectionId: 4,
            nodeId: 4,
            parentIndex: NO_PARENT,
            depth: 0,
            startUs: 100,
            durUs: 50,
            endUs: 150,
            allocBytes: 0,
        },
    ];

    it('scopes a section drill to the subtree and sums to its total', () => {
        const slices = pieSectionDrillSlices(nodes, names, 1, 8, 'other');
        const total = slices.reduce((a, s) => a + s.selfUs, 0);
        expect(total).toBe(100);
        expect(slices.map((s) => s.sectionId).sort()).toEqual([1, 2, 3]);
    });

    it('scopes a mod drill to that mods sections only', () => {
        const slices = pieModDrillSlices(nodes, names, 'ModAsm', 8, 'other');
        expect(slices.map((s) => s.sectionId).sort()).toEqual([3, 4]);
        expect(slices.reduce((a, s) => a + s.selfUs, 0)).toBe(80);
    });
});
