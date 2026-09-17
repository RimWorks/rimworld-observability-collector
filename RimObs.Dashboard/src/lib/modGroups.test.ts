import { describe, it, expect } from 'vitest';
import {
    modKeyFor,
    buildModRows,
    nestedInSameSection,
    RIMWORLD_MOD,
    UNITY_MOD,
    UNKNOWN_MOD,
    type SectionNames,
} from './modGroups';
import type { TreeNode } from './frameTree';
import { NO_PARENT } from './frameTree';

function node(
    sectionId: number,
    startUs: number,
    durUs: number,
    parentIndex = NO_PARENT,
    allocBytes = 0,
    calls?: number,
): TreeNode {
    return {
        sectionId,
        nodeId: sectionId * 100 + startUs,
        parentIndex,
        depth: parentIndex === NO_PARENT ? 0 : 1,
        startUs,
        durUs,
        endUs: startUs + durUs,
        allocBytes,
        calls,
    };
}

// names as the collector really serves them; id 5 is a Unity phase marker, which registers
// under UnityPhasePack.EngineAssembly.
const names: SectionNames = new Map([
    [1, { name: 'author.mod.work', subsystem: null, assembly: 'AuthorMod' }],
    [2, { name: 'author.mod.inner', subsystem: null, assembly: 'AuthorMod' }],
    [3, { name: 'Verse.TickManager.DoSingleTick', subsystem: 'tick', assembly: 'Assembly-CSharp' }],
    [
        4,
        {
            name: 'UnityEngine.Camera.Render',
            subsystem: 'render',
            assembly: 'UnityEngine.CoreModule',
        },
    ],
    [5, { name: 'Unity.Camera.Cull', subsystem: 'render', assembly: 'UnityEngine' }],
    [6, { name: 'other.mod.work', subsystem: null, assembly: 'OtherMod' }],
]);

describe('modKeyFor', () => {
    it('buckets a mod section by its declaring assembly', () => {
        expect(modKeyFor('AuthorMod')).toBe('AuthorMod');
    });

    it('buckets the game assembly to RimWorld', () => {
        expect(modKeyFor('Assembly-CSharp')).toBe(RIMWORLD_MOD);
        expect(modKeyFor('Assembly-CSharp-firstpass')).toBe(RIMWORLD_MOD);
    });

    it('buckets engine assemblies to Unity', () => {
        expect(modKeyFor('UnityEngine.CoreModule')).toBe(UNITY_MOD);
        expect(modKeyFor('UnityEngine')).toBe(UNITY_MOD);
    });

    it('buckets a section with no assembly to unknown', () => {
        expect(modKeyFor(null)).toBe(UNKNOWN_MOD);
        expect(modKeyFor('')).toBe(UNKNOWN_MOD);
    });
});

describe('buildModRows', () => {
    it('sums self, total and alloc per mod', () => {
        const rows = buildModRows(
            [node(3, 0, 100, NO_PARENT, 900), node(1, 10, 40, 0, 300)],
            names,
        );

        expect(rows.map((r) => r.key)).toEqual([RIMWORLD_MOD, 'AuthorMod']);
        expect(rows[0]).toMatchObject({ totalUs: 100, selfUs: 60, allocBytes: 900, calls: 1 });
        expect(rows[1]).toMatchObject({ totalUs: 40, selfUs: 40, allocBytes: 300, calls: 1 });
    });

    // calls sum over every node, nested ones included, unlike total and alloc.
    it('sums calls across every node of a mod', () => {
        const rows = buildModRows(
            [node(1, 0, 100, NO_PARENT, 0, 4), node(2, 10, 40, 0, 0, 7), node(3, 60, 20, 0)],
            names,
        );

        expect(rows.find((r) => r.key === 'AuthorMod')?.calls).toBe(11);
        expect(rows.find((r) => r.key === RIMWORLD_MOD)?.calls).toBe(1);
    });

    // a mod nested under itself would otherwise count its own time twice.
    it('does not double count a child into its parent when both are the same mod', () => {
        const rows = buildModRows(
            [node(1, 0, 100, NO_PARENT, 500), node(2, 10, 40, 0, 200)],
            names,
        );

        expect(rows).toHaveLength(1);
        expect(rows[0]).toMatchObject({
            key: 'AuthorMod',
            totalUs: 100,
            selfUs: 100,
            allocBytes: 500,
        });
    });

    it('does not double count across a foreign mod in between', () => {
        const rows = buildModRows([node(1, 0, 100), node(3, 10, 60, 0), node(2, 20, 30, 1)], names);

        expect(rows.find((r) => r.key === 'AuthorMod')?.totalUs).toBe(100);
        expect(rows.find((r) => r.key === RIMWORLD_MOD)?.totalUs).toBe(60);
    });

    it('carries the member section ids', () => {
        const rows = buildModRows([node(2, 0, 50), node(1, 60, 50), node(4, 120, 10)], names);

        expect(rows.find((r) => r.key === 'AuthorMod')?.sectionIds).toEqual([1, 2]);
        expect(rows.find((r) => r.key === UNITY_MOD)?.sectionIds).toEqual([4]);
    });

    it('sorts by total, biggest first', () => {
        const rows = buildModRows([node(5, 0, 10), node(6, 20, 90), node(3, 120, 50)], names);

        expect(rows.map((r) => r.key)).toEqual(['OtherMod', RIMWORLD_MOD, UNITY_MOD]);
    });

    it('returns nothing for an empty frame', () => {
        expect(buildModRows([], names)).toEqual([]);
    });
});

// the mod row skips recursive repeats, so the section rows under it have to skip them too,
// or an expanded child reads bigger than the mod it sits under.
describe('nestedInSameSection', () => {
    it('flags a section nested under itself', () => {
        const nodes = [node(3, 0, 100), node(3, 10, 60, 0)];

        expect(nestedInSameSection(nodes, 0)).toBe(false);
        expect(nestedInSameSection(nodes, 1)).toBe(true);
    });

    it('flags a section nested under itself across another section', () => {
        const nodes = [node(3, 0, 100), node(1, 10, 80, 0), node(3, 20, 60, 1)];

        expect(nestedInSameSection(nodes, 2)).toBe(true);
    });

    it('leaves a distinct section alone', () => {
        const nodes = [node(3, 0, 100), node(1, 10, 60, 0)];

        expect(nestedInSameSection(nodes, 1)).toBe(false);
    });
});

// one mod's sections, so a generated subtree can be kept to a single mod.
const MOD_SECTIONS = [[1, 2], [3], [4, 5], [6]];
const ALL_SECTIONS = MOD_SECTIONS.flat();

function seeded(seed: number): () => number {
    let s = seed;
    return () => {
        s = (s * 1103515245 + 12345) & 0x7fffffff;
        return s / 0x7fffffff;
    };
}

/** a random forest, each root's subtree drawn from `pool(root)`. */
function forest(rand: () => number, pool: (rootIndex: number) => number[]): TreeNode[] {
    const nodes: TreeNode[] = [];
    const grow = (parent: number, startUs: number, durUs: number, depth: number, ids: number[]) => {
        const me = nodes.length;
        nodes.push(
            node(
                ids[Math.floor(rand() * ids.length)],
                startUs,
                durUs,
                parent,
                Math.floor(rand() * 100),
                1 + Math.floor(rand() * 3),
            ),
        );
        nodes[me].depth = depth;
        if (depth >= 3) return;
        const kids = Math.floor(rand() * 3);
        let cursor = startUs;
        for (let k = 0; k < kids; k++) {
            const d = Math.floor(durUs / (kids + 1));
            if (d < 1) break;
            grow(me, cursor, d, depth + 1, ids);
            cursor += d;
        }
    };

    let at = 0;
    const roots = 1 + Math.floor(rand() * 3);
    for (let r = 0; r < roots; r++) {
        const dur = 50 + Math.floor(rand() * 450);
        grow(NO_PARENT, at, dur, 0, pool(r));
        at += dur;
    }
    return nodes;
}

const rootTotal = (nodes: readonly TreeNode[]) =>
    nodes.reduce((sum, n) => (n.parentIndex === NO_PARENT ? sum + n.durUs : sum), 0);

describe('buildModRows accounting', () => {
    // self time is the one figure that must add up no matter how mods interleave: every
    // microsecond of the frame belongs to exactly one node, so to exactly one mod.
    it('self time adds up to the frame on any generated tree', () => {
        for (let seed = 1; seed <= 40; seed++) {
            const rand = seeded(seed);
            const nodes = forest(rand, () => ALL_SECTIONS);
            const rows = buildModRows(nodes, names);

            const self = rows.reduce((sum, r) => sum + r.selfUs, 0);
            expect(self, `seed ${seed}`).toBe(rootTotal(nodes));
        }
    });

    // totals are inclusive, so they only sum to the frame when no mod sits inside a foreign
    // one. That is the shape the by-mod tree is read in, and the one double counting breaks.
    it('totals add up to the frame when each root subtree is one mod', () => {
        for (let seed = 1; seed <= 40; seed++) {
            const rand = seeded(seed);
            const nodes = forest(rand, (r) => MOD_SECTIONS[r % MOD_SECTIONS.length]);
            const rows = buildModRows(nodes, names);

            const total = rows.reduce((sum, r) => sum + r.totalUs, 0);
            expect(total, `seed ${seed}`).toBe(rootTotal(nodes));
        }
    });

    // a mod nested in another mod still cannot claim more than the frame ran for.
    it('no mod claims more than the whole frame', () => {
        for (let seed = 1; seed <= 40; seed++) {
            const rand = seeded(seed);
            const nodes = forest(rand, () => ALL_SECTIONS);
            const frame = rootTotal(nodes);

            for (const row of buildModRows(nodes, names)) {
                expect(row.totalUs, `seed ${seed} mod ${row.key}`).toBeLessThanOrEqual(frame);
            }
        }
    });
});
