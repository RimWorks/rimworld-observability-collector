import type { ThreadLane } from './api';
import { UNKNOWN_LANE, type FrameNodes, type TreeNode } from './frameTree';

/** Mirrors RimObs.Wire.ThreadRole. */
export const ThreadRole = {
    Main: 0,
    UnityJob: 1,
    Mod: 2,
    RimObs: 3,
} as const;

/**
 * Main is always "MainThread" so the gutter matches the call tree's frame scope; Unity never
 * names it and its job pool leaves workers unnamed, so the id is the only label left.
 */
export function laneLabel(t: ThreadLane): string {
    if (t.role === ThreadRole.Main) return 'MainThread';
    return t.name !== '' ? t.name : `Thread ${t.id}`;
}

/**
 * Busy time for one lane inside ONE frame; only outermost nodes count, a child is already
 * inside its parent's span. ThreadLane.busy_ns is session-cumulative and is not this number.
 */
export function laneBusyNs(nodes: FrameNodes, laneId: number): number {
    const lanes = nodes.thread_ids ?? [];
    if (lanes.length === 0) return 0;
    // busy is the union of the lane's node spans. nesting, overlap and re-parented orphans
    // all collapse into covered time, so a lane can never bill more than its wall clock.
    const n = Math.min(lanes.length, nodes.start_us.length, nodes.dur_us.length);
    const spans: Array<[number, number]> = [];
    for (let i = 0; i < n; i++) {
        if (lanes[i] !== laneId) continue;
        spans.push([nodes.start_us[i], nodes.start_us[i] + nodes.dur_us[i]]);
    }
    spans.sort((a, b) => a[0] - b[0]);
    let us = 0;
    let coveredTo = Number.NEGATIVE_INFINITY;
    for (const [start, end] of spans) {
        if (end <= coveredTo) continue;
        us += end - Math.max(start, coveredTo);
        coveredTo = end;
    }
    return us * 1000;
}

/** Main anchored first, then by id, so a lane keeps its row between polls. */
export function orderLanes(threads: ThreadLane[]): ThreadLane[] {
    const kept = threads.filter((t) => t.role !== ThreadRole.RimObs);
    // role 0 is also the collector's placeholder for a lane whose registration got dropped,
    // so only the oldest (lowest id) role-0 lane is really main.
    const mainId = kept.reduce(
        (low, t) => (t.role === ThreadRole.Main && (low === null || t.id < low) ? t.id : low),
        null as number | null,
    );
    return kept
        .map((t) =>
            t.role === ThreadRole.Main && t.id !== mainId ? { ...t, role: ThreadRole.UnityJob } : t,
        )
        .sort((a, b) => {
            const rank = (t: ThreadLane) => (t.role === ThreadRole.Main ? 0 : 1);
            return rank(a) - rank(b) || a.id - b.id;
        });
}

/**
 * A bundle ships node thread ids but no thread list, so the lanes are whatever the nodes name.
 * Lowest id wins main, which is how orderLanes ranks a live session too.
 */
export function lanesFromNodes(nodes: TreeNode[]): ThreadLane[] {
    const ids = new Set<number>();
    for (const n of nodes) {
        const id = n.laneId ?? UNKNOWN_LANE;
        if (id !== UNKNOWN_LANE) ids.add(id);
    }
    return orderLanes([...ids].map((id) => ({ id, name: '', role: ThreadRole.Main, busy_ns: 0 })));
}

export interface LaneBand {
    id: number;
    rows: number;
}

export interface LaneBands {
    bands: LaneBand[];
    /** lookup for the hot loops. a lane missing from this map does not draw at all. */
    offsets: Map<number, number>;
    rows: number;
}

/**
 * One flame band per lane, stacked in the order given. A lane with nothing in the window
 * still takes a row, so the gutter label stays level with the band it names.
 */
export function laneBands(lanes: ThreadLane[], nodes: TreeNode[], maxDepth: number): LaneBands {
    const deepest = new Map<number, number>();
    for (const n of nodes) {
        const lane = n.laneId ?? UNKNOWN_LANE;
        const depth = Math.min(n.depth, maxDepth - 1);
        if (depth > (deepest.get(lane) ?? 0)) deepest.set(lane, depth);
    }

    const bands: LaneBand[] = [];
    const offsets = new Map<number, number>();
    let rows = 0;
    for (const lane of lanes) {
        let depth = deepest.get(lane.id) ?? 0;
        offsets.set(lane.id, rows);
        if (lane.role === ThreadRole.Main) {
            // a v8 bundle has no thread ids, and the page has always drawn those on main.
            offsets.set(UNKNOWN_LANE, rows);
            depth = Math.max(depth, deepest.get(UNKNOWN_LANE) ?? 0);
        }
        const bandRows = Math.max(depth + 1, MIN_LANE_ROWS);
        bands.push({ id: lane.id, rows: bandRows });
        rows += bandRows;
    }
    return { bands, offsets, rows };
}

/** 4 rows x 18px = 72px, the first row multiple past the 65px minimum lane height. */
export const MIN_LANE_ROWS = 4;

/** canvas row for a node, or -1 when its lane is not drawn. */
export function laneRow(bands: LaneBands | undefined, node: TreeNode): number {
    if (!bands) return node.depth;
    const offset = bands.offsets.get(node.laneId ?? UNKNOWN_LANE);
    return offset === undefined ? -1 : offset + node.depth;
}
