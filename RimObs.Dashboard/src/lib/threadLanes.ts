import type { ThreadLane } from './api';
import { NO_PARENT, type FrameNodes } from './frameTree';

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
    const own = new Set<number>();
    for (let i = 0; i < lanes.length; i++) {
        if (lanes[i] === laneId) own.add(nodes.node_ids[i]);
    }
    let us = 0;
    for (let i = 0; i < lanes.length; i++) {
        if (lanes[i] !== laneId) continue;
        const parent = nodes.parent_node_ids[i];
        if (parent !== NO_PARENT && own.has(parent)) continue;
        us += nodes.dur_us[i];
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
