import { describe, it, expect } from 'vitest';
import { buildPatchIndex } from './patchIndex';
import type { PatchConflict } from './api';

function conflict(section: string, owner: string, target = 'Verse.Thing.Tick'): PatchConflict {
    return {
        section,
        target_method: target,
        other_owner: owner,
        patch_type: 1,
        priority: 0,
        patch_method: `${owner}.Patch`,
    };
}

describe('buildPatchIndex', () => {
    it('returns an empty index for no conflicts', () => {
        expect(buildPatchIndex([]).size).toBe(0);
    });

    it('groups owners under their section', () => {
        const index = buildPatchIndex([
            conflict('verse.tick', 'RocketMan'),
            conflict('verse.tick', 'Dubs'),
            conflict('verse.draw', 'Dubs'),
        ]);

        expect(index.get('verse.tick')).toEqual(['Dubs', 'RocketMan']);
        expect(index.get('verse.draw')).toEqual(['Dubs']);
    });

    it('counts an owner once even when it patches several targets in one section', () => {
        const index = buildPatchIndex([
            conflict('verse.tick', 'RocketMan', 'Verse.Thing.Tick'),
            conflict('verse.tick', 'RocketMan', 'Verse.Thing.TickRare'),
        ]);

        expect(index.get('verse.tick')).toEqual(['RocketMan']);
    });

    it('skips rows with no section or no owner', () => {
        const index = buildPatchIndex([conflict('', 'Dubs'), conflict('verse.tick', '')]);

        expect(index.size).toBe(0);
    });
});
