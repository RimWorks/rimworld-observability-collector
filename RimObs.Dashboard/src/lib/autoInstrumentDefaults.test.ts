import { describe, it, expect, beforeEach } from 'vitest';
import {
    initialFilters,
    rememberFilters,
    mergePatterns,
    DEFAULT_FILTERS,
} from './autoInstrumentDefaults';

beforeEach(() => localStorage.clear());

describe('mergePatterns', () => {
    it('folds the ignore list into the filters as negated lines', () => {
        expect(mergePatterns('Verse.*\nRimWorld.*', 'Verse.Log::*\n!Already.Negated')).toBe(
            'Verse.*\nRimWorld.*\n!Verse.Log::*\n!Already.Negated',
        );
    });

    it('passes filters through untouched when the ignore is empty', () => {
        expect(mergePatterns('Verse.*', '')).toBe('Verse.*');
    });

    it('keeps comments and blank ignore lines out of the merge', () => {
        expect(mergePatterns('Verse.*', '# note\n\nVerse.Log::*')).toBe('Verse.*\n!Verse.Log::*');
    });
});

describe('auto-instrument defaults', () => {
    it('seeds the example patterns with the standing exclusions on a first run', () => {
        expect(initialFilters('', '')).toBe(DEFAULT_FILTERS);
        expect(DEFAULT_FILTERS).toContain('!*::get_*');
        expect(DEFAULT_FILTERS).toContain('!*::GetHashCode');
    });

    it('merges what the collector already holds across both old fields', () => {
        expect(initialFilters('Assembly-CSharp!Verse.Thing::*', 'Verse.Log::*')).toBe(
            'Assembly-CSharp!Verse.Thing::*\n!Verse.Log::*',
        );
    });

    it('remembers what you typed last', () => {
        rememberFilters('Cosmere!Cosmere.*::*\n!Cosmere!Cosmere.Log::*');

        expect(initialFilters('', '')).toBe('Cosmere!Cosmere.*::*\n!Cosmere!Cosmere.Log::*');
    });

    // clearing the box means "instrument nothing". reseeding the example would fight the user.
    it('keeps a deliberately emptied field empty', () => {
        rememberFilters('');

        expect(initialFilters('', '')).toBe('');
    });
});
