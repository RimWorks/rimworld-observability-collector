import { describe, it, expect, beforeEach } from 'vitest';
import {
    initialFilters,
    initialIgnore,
    rememberFilters,
    rememberIgnore,
    DEFAULT_FILTERS,
    DEFAULT_IGNORE,
} from './autoInstrumentDefaults';

beforeEach(() => localStorage.clear());

describe('auto-instrument defaults', () => {
    // the syntax is not guessable, so a first run should land on something that works.
    it('seeds the example patterns on a first run', () => {
        expect(initialFilters('')).toBe(DEFAULT_FILTERS);
        expect(initialIgnore('')).toBe(DEFAULT_IGNORE);
    });

    it('prefers what the collector already holds', () => {
        expect(initialFilters('Assembly-CSharp!Verse.Thing::*')).toBe(
            'Assembly-CSharp!Verse.Thing::*',
        );
    });

    it('remembers what you typed last', () => {
        rememberFilters('Cosmere!Cosmere.*::*');
        rememberIgnore('Cosmere!Cosmere.Log::*');

        expect(initialFilters('')).toBe('Cosmere!Cosmere.*::*');
        expect(initialIgnore('')).toBe('Cosmere!Cosmere.Log::*');
    });

    // clearing the box means "instrument nothing". reseeding the example would fight the user.
    it('keeps a deliberately emptied field empty', () => {
        rememberFilters('');

        expect(initialFilters('')).toBe('');
    });
});
