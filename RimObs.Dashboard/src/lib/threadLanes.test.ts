import { describe, it, expect } from 'vitest';
import { laneBands, laneBusyNs, laneLabel, laneRow, orderLanes, ThreadRole } from './threadLanes';
import type { ThreadLane } from './api';
import type { FrameNodes, TreeNode } from './frameTree';

const t = (over: Partial<ThreadLane> = {}): ThreadLane => ({
    id: 1,
    name: '',
    role: ThreadRole.UnityJob,
    busy_ns: 0,
    ...over,
});

describe('laneLabel', () => {
    it('uses the name the library sent', () => {
        expect(laneLabel(t({ name: 'PathfindingWorker' }))).toBe('PathfindingWorker');
    });

    // unity's job pool leaves its workers unnamed, so the id is all we have.
    it('falls back to the id when there is no name', () => {
        expect(laneLabel(t({ id: 14 }))).toBe('Thread 14');
    });

    // unity never names the main thread, and the call tree's scope chip says MainThread.
    it('always labels the main lane MainThread', () => {
        expect(laneLabel(t({ id: 1, name: '', role: ThreadRole.Main }))).toBe('MainThread');
        expect(laneLabel(t({ id: 1, name: 'Unity Main', role: ThreadRole.Main }))).toBe(
            'MainThread',
        );
    });
});

describe('orderLanes', () => {
    it('anchors the main thread first', () => {
        const lanes = orderLanes([
            t({ id: 3 }),
            t({ id: 1, name: 'Main', role: ThreadRole.Main }),
            t({ id: 2 }),
        ]);
        expect(lanes[0].id).toBe(1);
    });

    it('orders the rest by id so lanes do not jump between polls', () => {
        const lanes = orderLanes([t({ id: 5 }), t({ id: 2 }), t({ id: 9 })]);
        expect(lanes.map((l) => l.id)).toEqual([2, 5, 9]);
    });

    it('drops our own sender thread', () => {
        const lanes = orderLanes([t({ id: 1 }), t({ id: 2, role: ThreadRole.RimObs })]);
        expect(lanes.map((l) => l.id)).toEqual([1]);
    });

    // a dropped registration packet leaves a worker stamped role 0 forever.
    it('keeps only the lowest-id role-0 lane as main, in the same order either way', () => {
        const forward = orderLanes([
            t({ id: 1, name: 'Main', role: ThreadRole.Main }),
            t({ id: 99, role: ThreadRole.Main }),
            t({ id: 4 }),
        ]);
        const reversed = orderLanes([
            t({ id: 4 }),
            t({ id: 99, role: ThreadRole.Main }),
            t({ id: 1, name: 'Main', role: ThreadRole.Main }),
        ]);
        expect(forward.map((l) => l.id)).toEqual([1, 4, 99]);
        expect(reversed.map((l) => l.id)).toEqual([1, 4, 99]);
        expect(forward.filter((l) => l.role === ThreadRole.Main).map((l) => l.id)).toEqual([1]);
    });

    it('leaves the input array alone', () => {
        const input = [t({ id: 5 }), t({ id: 2 })];
        orderLanes(input);
        expect(input.map((l) => l.id)).toEqual([5, 2]);
    });
});

// node i: id, parent id (-1 for a root), start in us, duration in us, and its lane.
function nodes(rows: [number, number, number, number, number][]): FrameNodes {
    return {
        section_ids: rows.map(() => 1),
        parent_ids: rows.map(() => -1),
        node_ids: rows.map((r) => r[0]),
        parent_node_ids: rows.map((r) => r[1]),
        start_us: rows.map((r) => r[2]),
        dur_us: rows.map((r) => r[3]),
        thread_ids: rows.map((r) => r[4]),
    };
}

describe('laneBusyNs', () => {
    it('sums the lane root nodes', () => {
        const f = nodes([
            [10, -1, 0, 400, 1],
            [11, -1, 500, 400, 1],
        ]);
        expect(laneBusyNs(f, 1)).toBe(800_000);
    });

    // a child sits inside its parent's span, so counting both bills the same time twice.
    it('does not double-count a nested child', () => {
        const f = nodes([
            [10, -1, 0, 800, 1],
            [11, 10, 100, 500, 1],
            [12, 11, 150, 100, 1],
        ]);
        expect(laneBusyNs(f, 1)).toBe(800_000);
    });

    // a worker's scope is opened by the main thread, so its parent lives on another lane.
    it('counts a node whose parent ran on another lane', () => {
        const f = nodes([
            [10, -1, 0, 800, 1],
            [20, 10, 200, 100, 2],
        ]);
        expect(laneBusyNs(f, 2)).toBe(100_000);
    });

    // the flame re-parents an unresolved parent id onto the innermost open container, so
    // reading the parent chain would bill this span twice and push the lane past 100%.
    it('does not double-count an orphan nested inside a counted root', () => {
        const f = nodes([
            [10, -1, 0, 1000, 1],
            [11, 99, 200, 500, 1],
        ]);
        expect(laneBusyNs(f, 1)).toBe(1_000_000);
    });

    // the review repro: an orphan re-parented onto ANOTHER lane's node still sits inside its
    // own lane's root span, so lane 2 is exactly one frame busy, never 105%.
    it('never bills a lane past its covered wall clock', () => {
        const f = nodes([
            [10, -1, 0, 1000, 1],
            [20, -1, 0, 1000, 2],
            [30, 10, 150, 300, 1],
            [40, 98, 200, 50, 2],
        ]);
        expect(laneBusyNs(f, 1)).toBe(1_000_000);
        expect(laneBusyNs(f, 2)).toBe(1_000_000);
    });

    it('counts partially overlapping spans once', () => {
        const f = nodes([
            [10, -1, 0, 600, 1],
            [11, -1, 400, 600, 1],
        ]);
        expect(laneBusyNs(f, 1)).toBe(1_000_000);
    });

    it('is zero for a lane with no nodes in the frame', () => {
        expect(laneBusyNs(nodes([[10, -1, 0, 800, 1]]), 7)).toBe(0);
    });

    // a v8 bundle predates per-node thread ids, so no lane can claim any time.
    it('is zero for every lane when thread_ids is empty', () => {
        const f = nodes([
            [10, -1, 0, 800, 1],
            [20, -1, 0, 100, 2],
        ]);
        f.thread_ids = [];
        expect(laneBusyNs(f, 1)).toBe(0);
        expect(laneBusyNs(f, 2)).toBe(0);
    });
});

const MAX_DEPTH = 128;
const main = (id = 1) => t({ id, role: ThreadRole.Main });
const n = (depth: number, laneId?: number): TreeNode => ({
    depth,
    laneId,
    sectionId: 1,
    nodeId: 1,
    parentIndex: -1,
    startUs: 0,
    durUs: 1,
    endUs: 1,
});

describe('laneBands', () => {
    it('stacks each lane below the rows the one above it needs', () => {
        const bands = laneBands(
            [main(), t({ id: 2 })],
            [n(0, 1), n(2, 1), n(0, 2), n(1, 2)],
            MAX_DEPTH,
        );
        expect(bands.offsets.get(1)).toBe(0);
        expect(bands.offsets.get(2)).toBe(3);
        expect(bands.rows).toBe(5);
        expect(bands.bands.map((b) => b.rows)).toEqual([3, 2]);
    });

    // the gutter draws a row for it either way, so the band has to hold that row open.
    it('gives a lane with nothing in the window one row', () => {
        const bands = laneBands([main(), t({ id: 2 })], [n(0, 1)], MAX_DEPTH);
        expect(bands.offsets.get(2)).toBe(1);
        expect(bands.rows).toBe(2);
    });

    it('leaves out a lane nobody selected', () => {
        const bands = laneBands([main()], [n(0, 1), n(0, 2)], MAX_DEPTH);
        expect(bands.offsets.has(2)).toBe(false);
        expect(laneRow(bands, n(0, 2))).toBe(-1);
    });

    // a v8 bundle sends no thread ids at all, and the page has always drawn those on main.
    it('draws nodes with no lane in the main band', () => {
        const bands = laneBands([main(), t({ id: 2 })], [n(0), n(1), n(0, 2)], MAX_DEPTH);
        expect(laneRow(bands, n(1))).toBe(1);
        expect(bands.offsets.get(2)).toBe(2);
    });

    it('drops nodes with no lane when main is deselected', () => {
        const bands = laneBands([t({ id: 2 })], [n(0), n(0, 2)], MAX_DEPTH);
        expect(laneRow(bands, n(0))).toBe(-1);
        expect(laneRow(bands, n(0, 2))).toBe(0);
    });

    it('caps a band at the layout depth limit', () => {
        const bands = laneBands([main(), t({ id: 2 })], [n(500, 1), n(0, 2)], 4);
        expect(bands.rows).toBe(5);
        expect(bands.offsets.get(2)).toBe(4);
    });

    it('is empty when every lane is off', () => {
        expect(laneBands([], [n(0, 1)], MAX_DEPTH).rows).toBe(0);
    });
});

describe('laneRow', () => {
    it('falls back to the node depth with no bands at all', () => {
        expect(laneRow(undefined, n(3, 9))).toBe(3);
    });
});
