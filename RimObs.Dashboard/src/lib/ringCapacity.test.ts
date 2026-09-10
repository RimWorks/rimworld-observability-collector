import { describe, it, expect } from 'vitest';
import { clampRing, MIN_RING, MAX_RING } from './ringCapacity';

describe('clampRing', () => {
    it('keeps a value inside the range', () => {
        expect(clampRing(2000)).toBe(2000);
    });

    it('floors a value below the minimum', () => {
        expect(clampRing(1)).toBe(MIN_RING);
    });

    // the whole ring ships on every frame poll, so the ceiling is a payload budget.
    it('caps a value above the maximum', () => {
        expect(clampRing(1_000_000)).toBe(MAX_RING);
    });

    it('rounds a fractional value', () => {
        expect(clampRing(2000.6)).toBe(2001);
    });

    it('falls back to the minimum for a value that is not a number', () => {
        expect(clampRing(Number.NaN)).toBe(MIN_RING);
    });
});
