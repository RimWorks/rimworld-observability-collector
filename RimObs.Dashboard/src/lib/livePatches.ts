import type { InstrumentationPatchEntry, LivePatchEntry } from './api';

export type PatchStatus = 'pending' | 'active' | 'stale';

export interface MergedPatch extends InstrumentationPatchEntry {
    /** the section this patch feeds, so a flame bar can be traced back to it */
    sectionId: number | null;
    /** live truth where the game answered, the stored value otherwise */
    status: PatchStatus;
}

function normalize(raw: string): PatchStatus {
    const lower = raw.toLowerCase();
    return lower === 'active' || lower === 'pending' ? lower : 'stale';
}

/**
 * Joins the stored rows to what the game currently reports. A row the game does not list is
 * not patched, whatever `lastStatus` says: storage is only written at replay time.
 */
export function mergePatches(
    persisted: readonly InstrumentationPatchEntry[],
    live: readonly LivePatchEntry[] | undefined,
): MergedPatch[] {
    const byId = new Map((live ?? []).map((l) => [l.patchId, l]));

    return persisted.map((p) => {
        const match = p.livePatchId === null ? undefined : byId.get(p.livePatchId);
        if (match === undefined) {
            return {
                ...p,
                sectionId: null,
                status: p.lastStatus === 'pending' ? 'pending' : 'stale',
            };
        }
        return { ...p, sectionId: match.sectionId, status: normalize(match.status) };
    });
}

/** Section ids the game is actively reporting, so a flame bar can offer to un-instrument. */
export function liveSectionIds(patches: readonly MergedPatch[]): Map<number, MergedPatch> {
    const index = new Map<number, MergedPatch>();
    for (const p of patches) {
        if (p.sectionId !== null && p.status === 'active') index.set(p.sectionId, p);
    }
    return index;
}
