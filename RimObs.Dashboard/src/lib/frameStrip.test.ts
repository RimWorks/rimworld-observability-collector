import { describe, it, expect } from 'vitest';
import {
    barHeight,
    budgetLine,
    buildBars,
    barIndexAt,
    barWidthPx,
    stepOrdinal,
    STRIP_FULL_SCALE_US,
    gridLines,
} from './frameStrip';

const bars = (ordinals: number[]) =>
    buildBars(
        ordinals,
        ordinals.map(() => 1000),
    );

describe('barHeight', () => {
    it('is a fraction of the 66.67ms full scale', () => {
        expect(barHeight(STRIP_FULL_SCALE_US / 2)).toBeCloseTo(0.5, 5);
        expect(barHeight(16667)).toBeCloseTo(0.25, 2);
    });

    // a 200ms stall and a 70ms one both read as full height, matching Neo.
    it('clamps anything past full scale to 1', () => {
        expect(barHeight(STRIP_FULL_SCALE_US * 3)).toBe(1);
    });

    it('treats zero, negative and NaN as no bar', () => {
        expect(barHeight(0)).toBe(0);
        expect(barHeight(-5)).toBe(0);
        expect(barHeight(Number.NaN)).toBe(0);
    });
});

describe('budgetLine', () => {
    // 45.45ms of the strip's 66.67ms full scale.
    it('puts the frame budget at just over two thirds height', () => {
        expect(budgetLine()).toBeCloseTo(0.682, 2);
    });

    it('clamps a budget past full scale', () => {
        expect(budgetLine(STRIP_FULL_SCALE_US * 2)).toBe(1);
    });
});

describe('buildBars', () => {
    it('marks bars over the budget', () => {
        const out = buildBars([1, 2], [40_000, 50_000]);
        expect(out[0].overBudget).toBe(false);
        expect(out[1].overBudget).toBe(true);
    });

    it('stops at the shorter of the two arrays', () => {
        expect(buildBars([1, 2, 3], [100])).toHaveLength(1);
    });
});

describe('barIndexAt', () => {
    it('maps x across the strip to a bar', () => {
        expect(barIndexAt(0, 100, 10)).toBe(0);
        expect(barIndexAt(55, 100, 10)).toBe(5);
        expect(barIndexAt(99.9, 100, 10)).toBe(9);
    });

    it('returns -1 outside the strip or with nothing to hit', () => {
        expect(barIndexAt(100, 100, 10)).toBe(-1);
        expect(barIndexAt(-1, 100, 10)).toBe(-1);
        expect(barIndexAt(10, 100, 0)).toBe(-1);
        expect(barIndexAt(10, 0, 5)).toBe(-1);
    });
});

describe('barWidthPx', () => {
    // a full 2000-frame ring on a 600px panel is 0.3px a bar, which would vanish.
    it('never goes below a pixel', () => {
        expect(barWidthPx(600, 2000)).toBe(1);
        expect(barWidthPx(600, 60)).toBe(10);
        expect(barWidthPx(600, 0)).toBe(0);
    });
});

describe('stepOrdinal', () => {
    it('starts at the newest when nothing is selected', () => {
        expect(stepOrdinal(bars([5, 6, 7]), null, -1)).toBe(7);
    });

    it('steps older and newer', () => {
        const b = bars([5, 6, 7]);
        expect(stepOrdinal(b, 7, -1)).toBe(6);
        expect(stepOrdinal(b, 6, 1)).toBe(7);
    });

    it('clamps at both ends instead of wrapping', () => {
        const b = bars([5, 6, 7]);
        expect(stepOrdinal(b, 5, -1)).toBe(5);
        expect(stepOrdinal(b, 7, 1)).toBe(7);
    });

    // stepping older for a while lets the ring evict the frame you were on.
    it('lands on the oldest held frame when the current one aged out', () => {
        expect(stepOrdinal(bars([50, 51, 52]), 3, -1)).toBe(50);
    });

    it('lands on the newest when the current ordinal is past the ring', () => {
        expect(stepOrdinal(bars([50, 51, 52]), 999, 1)).toBe(52);
    });

    it('returns null for an empty strip', () => {
        expect(stepOrdinal([], 5, -1)).toBeNull();
    });
});

describe('gridLines', () => {
    it('places every FPS line inside the strip', () => {
        const lines = gridLines();
        expect(lines.map((l) => l.fps)).toEqual([15, 20, 30, 60, 120]);
        expect(lines.every((l) => l.at > 0 && l.at <= 1)).toBe(true);
    });

    // 15 FPS is 66.7ms, which is exactly full scale, so it must sit at the very top.
    it('puts 15 FPS at the ceiling and 60 FPS at a quarter', () => {
        const by = new Map(gridLines().map((l) => [l.fps, l.at]));
        expect(by.get(15)).toBeCloseTo(1, 2);
        expect(by.get(60)).toBeCloseTo(0.25, 2);
    });
});
