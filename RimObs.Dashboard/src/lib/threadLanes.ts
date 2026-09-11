import type { ThreadLane } from './api';

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
            t.role === ThreadRole.Main && t.id !== mainId
                ? { ...t, role: ThreadRole.UnityJob }
                : t,
        )
        .sort((a, b) => {
            const rank = (t: ThreadLane) => (t.role === ThreadRole.Main ? 0 : 1);
            return rank(a) - rank(b) || a.id - b.id;
        });
}

/** Both arguments are nanoseconds; the frame duration from the api is microseconds. */
export function busyFraction(t: ThreadLane, frameNs: number): number {
    if (frameNs <= 0) return 0;
    return Math.min(1, Math.max(0, t.busy_ns / frameNs));
}
