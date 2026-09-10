import { describe, it, expect } from 'vitest';
import { clampDepth, MIN_DEPTH, MAX_DEPTH, DEFAULT_DEPTH } from './captureDepth';

describe('clampDepth', () => {
    it('keeps a value inside the range', () => {
        expect(clampDepth(8)).toBe(8);
        expect(clampDepth(MIN_DEPTH)).toBe(MIN_DEPTH);
        expect(clampDepth(MAX_DEPTH)).toBe(MAX_DEPTH);
    });

    it('pulls a value past the library stack back to the ceiling', () => {
        expect(clampDepth(999)).toBe(MAX_DEPTH);
    });

    it('refuses a depth below one, which would record nothing at all', () => {
        expect(clampDepth(0)).toBe(MIN_DEPTH);
        expect(clampDepth(-5)).toBe(MIN_DEPTH);
    });

    it('rounds a fractional depth', () => {
        expect(clampDepth(7.6)).toBe(8);
    });

    it('falls back to the default when the field is empty or unparseable', () => {
        expect(clampDepth(Number.NaN)).toBe(DEFAULT_DEPTH);
    });
});
