import { describe, it, expect } from 'vitest';
import { mergePatches, liveSectionIds } from './livePatches';
import type { InstrumentationPatchEntry, LivePatchEntry } from './api';

function row(
    id: number,
    livePatchId: number | null,
    lastStatus: InstrumentationPatchEntry['lastStatus'] = 'active',
): InstrumentationPatchEntry {
    return {
        id,
        typeFullName: 'Verse.PathFinder',
        methodName: `M${id}`,
        paramTypesJoined: '',
        createdUtc: '2026-09-08T00:00:00Z',
        lastStatus,
        lastError: null,
        livePatchId,
    };
}

function live(patchId: number, sectionId: number, status = 'active'): LivePatchEntry {
    return { patchId, sectionId, status, signature: `Verse.PathFinder:M${patchId}()` };
}

describe('mergePatches', () => {
    it('takes the section id and status from the live entry', () => {
        const merged = mergePatches([row(1, 7)], [live(7, 42)]);

        expect(merged[0].sectionId).toBe(42);
        expect(merged[0].status).toBe('active');
    });

    // lastStatus is only written at replay, so a patch that fell off mid-session still
    // reads active in storage. the game not listing it is the truth.
    it('reports stale when storage says active but the game does not list it', () => {
        const merged = mergePatches([row(1, 7, 'active')], []);

        expect(merged[0].status).toBe('stale');
        expect(merged[0].sectionId).toBeNull();
    });

    it('keeps pending for a row that was never applied', () => {
        const merged = mergePatches([row(1, null, 'pending')], []);

        expect(merged[0].status).toBe('pending');
    });

    it('does not match a row whose live id is null against live patch ids', () => {
        const merged = mergePatches([row(1, null, 'active')], [live(1, 42)]);

        expect(merged[0].sectionId).toBeNull();
        expect(merged[0].status).toBe('stale');
    });

    it('treats an unknown live status as stale', () => {
        const merged = mergePatches([row(1, 7)], [live(7, 42, 'refused')]);

        expect(merged[0].status).toBe('stale');
    });

    it('handles a missing live array', () => {
        expect(mergePatches([row(1, 7)], undefined)[0].status).toBe('stale');
    });
});

describe('liveSectionIds', () => {
    it('indexes only the active patches by section', () => {
        const merged = mergePatches(
            [row(1, 7), row(2, 8), row(3, null, 'pending')],
            [live(7, 42), live(8, 43, 'stale')],
        );

        const index = liveSectionIds(merged);

        expect([...index.keys()]).toEqual([42]);
        expect(index.get(42)?.id).toBe(1);
    });
});
