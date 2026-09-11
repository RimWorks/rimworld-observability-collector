import { describe, it, expect } from 'vitest';
import { lodFloorUs, refinementFor } from './lod';

const entry = (ordinal: number, startUs: number, endUs: number) => ({ ordinal, startUs, endUs });

describe('lodFloorUs', () => {
    it('is zero for a small span, where full detail is cheap', () => {
        expect(lodFloorUs(200_000)).toBe(0);
    });

    it('scales with the span so a selection stays around 4000 drawable slots', () => {
        expect(lodFloorUs(4_000_000)).toBe(1000);
    });
});

describe('refinementFor', () => {
    const FRAMES = [entry(10, 0, 20_000), entry(11, 20_000, 40_000), entry(12, 40_000, 60_000)];

    it('asks for the visible ordinals at the finer floor once the view needs it', () => {
        const lod = new Map([
            [10, 1000],
            [11, 1000],
            [12, 1000],
        ]);
        const r = refinementFor({ startUs: 18_000, endUs: 42_000 }, FRAMES, lod);

        expect(r).toEqual({ from: 10, to: 12, floorUs: 0 });
    });

    it('returns null while the loaded detail already covers the view', () => {
        const lod = new Map([
            [10, 0],
            [11, 0],
            [12, 0],
        ]);
        expect(refinementFor({ startUs: 18_000, endUs: 42_000 }, FRAMES, lod)).toBeNull();
    });

    it('returns null when the view is no finer than the loaded floor', () => {
        const lod = new Map([
            [10, 1000],
            [11, 1000],
            [12, 1000],
        ]);
        expect(refinementFor({ startUs: 0, endUs: 8_000_000 }, FRAMES, lod)).toBeNull();
    });

    it('only names frames that overlap the view', () => {
        const lod = new Map([
            [10, 1000],
            [11, 0],
            [12, 1000],
        ]);
        const r = refinementFor({ startUs: 0, endUs: 19_000 }, FRAMES, lod);

        expect(r).toEqual({ from: 10, to: 10, floorUs: 0 });
    });
});
