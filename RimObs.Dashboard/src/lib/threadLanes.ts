import type { ThreadLane } from './api';

/** Mirrors RimObs.Wire.ThreadRole. */
export const ThreadRole = {
    Main: 0,
    UnityJob: 1,
    Mod: 2,
    RimObs: 3,
} as const;

/** Unity's job pool leaves its workers unnamed, so the id is the only label available. */
export function laneLabel(t: ThreadLane): string {
    return t.name !== '' ? t.name : `Thread ${t.id}`;
}

/** Main anchored first, then by id, so a lane keeps its row between polls. */
export function orderLanes(threads: ThreadLane[]): ThreadLane[] {
    return threads
        .filter((t) => t.role !== ThreadRole.RimObs)
        .sort((a, b) => {
            if (a.role === ThreadRole.Main) return -1;
            if (b.role === ThreadRole.Main) return 1;
            return a.id - b.id;
        });
}

/** Both arguments are nanoseconds; the frame duration from the api is microseconds. */
export function busyFraction(t: ThreadLane, frameNs: number): number {
    if (frameNs <= 0) return 0;
    return Math.min(1, Math.max(0, t.busy_ns / frameNs));
}
