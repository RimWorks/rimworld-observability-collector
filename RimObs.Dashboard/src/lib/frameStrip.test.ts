import { describe, it, expect } from 'vitest';
import {
    barHeight,
    budgetLine,
    buildBars,
    buildAllocBars,
    allocBarHeight,
    allocGridLines,
    ALLOC_MIN_BYTES,
    ALLOC_MAX_BYTES,
    barIndexAt,
    slotSpanPx,
    slotWidthPx,
    DEFAULT_STRIP_SLOTS,
    barColor,
    clampTooltipX,
    stepOrdinal,
    STRIP_FULL_SCALE_US,
    gridLines,
    gcMarkIndices,
} from './frameStrip';

const SCALE = { good: '#00ff00', warn: '#ffff00', bad: '#ff0000', badDeep: '#800000' };

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

describe('barColor', () => {
    it('is flat green comfortably under budget', () => {
        expect(barColor(0, 1000, SCALE)).toBe(SCALE.good);
        expect(barColor(500, 1000, SCALE)).toBe(SCALE.good);
    });

    it('is exactly amber right at budget', () => {
        expect(barColor(1000, 1000, SCALE)).toBe(SCALE.warn);
    });

    it('lerps between green and amber approaching budget', () => {
        const mid = barColor(800, 1000, SCALE);
        expect(mid).not.toBe(SCALE.good);
        expect(mid).not.toBe(SCALE.warn);
    });

    it('is exactly red at 1.5x budget', () => {
        expect(barColor(1500, 1000, SCALE)).toBe(SCALE.bad);
    });

    it('keeps darkening past 1.5x budget, clamping at 3x', () => {
        expect(barColor(3000, 1000, SCALE)).toBe(SCALE.badDeep);
        expect(barColor(10_000, 1000, SCALE)).toBe(SCALE.badDeep);
    });

    it('treats a negative or zero duration as fully under budget', () => {
        expect(barColor(-500, 1000, SCALE)).toBe(SCALE.good);
    });

    it('does not blow up on a non-positive budget', () => {
        expect(barColor(500, 0, SCALE)).toBe(SCALE.good);
        expect(barColor(500, -100, SCALE)).toBe(SCALE.good);
    });
});

describe('clampTooltipX', () => {
    it('follows the cursor when there is room on both sides', () => {
        expect(clampTooltipX(50, 20, 200)).toBe(50);
    });

    it('stops at the left edge instead of hanging off it', () => {
        expect(clampTooltipX(2, 20, 200)).toBe(10);
    });

    it('stops at the right edge instead of hanging off it', () => {
        expect(clampTooltipX(198, 20, 200)).toBe(190);
    });

    it('centers when the tooltip is wider than the container', () => {
        expect(clampTooltipX(50, 300, 200)).toBe(150);
    });

    it('treats an empty container as zero', () => {
        expect(clampTooltipX(50, 20, 0)).toBe(0);
    });
});

describe('slotWidthPx', () => {
    it('divides the panel by capacity, not by frames held', () => {
        expect(slotWidthPx(2000, 2000)).toBe(1);
        expect(slotWidthPx(600, 60)).toBe(10);
    });

    // flooring at a pixel makes 2000 slots sum to 2000px. on a 600px panel that pushes every
    // frame past index 600 off the canvas, so the newest frames disappear as the ring fills.
    it('goes sub-pixel rather than overflowing the panel', () => {
        const bw = slotWidthPx(600, 2000);

        expect(bw).toBeCloseTo(0.3, 10);
        expect(bw * 2000).toBeCloseTo(600, 10);
    });

    it('sizes a slot the same however many frames have arrived', () => {
        expect(slotWidthPx(1200, DEFAULT_STRIP_SLOTS)).toBe(slotWidthPx(1200, DEFAULT_STRIP_SLOTS));
    });

    it('returns zero for a panel with no width', () => {
        expect(slotWidthPx(0, 2000)).toBe(0);
    });
});

describe('fixed slot hit testing', () => {
    it('maps x through the slot width, so a half-full strip does not stretch', () => {
        // 100 frames in a 2000-slot, 2000px strip: frame 5 owns x 5..6.
        expect(barIndexAt(5.5, 2000, 100, 2000)).toBe(5);
    });

    it('selects nothing on the empty strip past the newest frame', () => {
        expect(barIndexAt(900, 2000, 100, 2000)).toBe(-1);
    });

    it('still reaches the last frame of a full ring', () => {
        expect(barIndexAt(1999.5, 2000, 2000, 2000)).toBe(1999);
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

describe('gcMarkIndices', () => {
    it('maps a GC ordinal to its bar index', () => {
        expect(gcMarkIndices(bars([1, 2, 3, 4]), [2, 4])).toEqual([1, 3]);
    });

    it('returns nothing when no collection happened', () => {
        expect(gcMarkIndices(bars([1, 2, 3]), [])).toEqual([]);
    });

    // several collections in one frame still read as a single mark.
    it('dedupes several collections landing in the same frame', () => {
        expect(gcMarkIndices(bars([1, 2, 3]), [2, 2, 2])).toEqual([1]);
    });

    // the strip's ring already dropped this frame, so there is nothing to mark it against.
    it('drops an ordinal the ring has already evicted', () => {
        expect(gcMarkIndices(bars([5, 6, 7]), [99])).toEqual([]);
    });
});

describe('alloc mode', () => {
    it('zero bytes draws no bar', () => {
        expect(allocBarHeight(0)).toBe(0);
    });

    it('clamps the log scale to 0..1 with a visible floor', () => {
        expect(allocBarHeight(1)).toBeCloseTo(0.03);
        expect(allocBarHeight(ALLOC_MAX_BYTES * 4)).toBe(1);
        const mid = allocBarHeight(65536);
        expect(mid).toBeGreaterThan(0.4);
        expect(mid).toBeLessThan(0.6);
    });

    it('buildAllocBars carries bytes and never flags budget', () => {
        const bars = buildAllocBars([1, 2], [1024, 0]);
        expect(bars[0].allocBytes).toBe(1024);
        expect(bars[0].overBudget).toBe(false);
        expect(bars[1].height).toBe(0);
    });

    it('grid lines span 1KB..4MB inside the scale', () => {
        const lines = allocGridLines();
        expect(lines[0].bytes).toBe(1024);
        expect(lines[lines.length - 1].bytes).toBe(4 * 1024 * 1024);
        for (const line of lines) {
            expect(line.at).toBeGreaterThan(0);
            expect(line.at).toBeLessThan(1);
        }
        expect(ALLOC_MIN_BYTES).toBeLessThan(1024);
    });
});

describe('slotSpanPx', () => {
    it('adjacent sub-pixel slots tile with no gap or overlap', () => {
        const bw = 1400 / 2000;
        for (const dpr of [1, 1.25, 2]) {
            for (let i = 0; i < 50; i++) {
                const a = slotSpanPx(i, bw, dpr);
                const b = slotSpanPx(i + 1, bw, dpr);
                expect(a.x + a.w).toBeGreaterThanOrEqual(b.x - 1e-9);
                expect(a.w).toBeGreaterThanOrEqual(1 / dpr - 1e-9);
            }
        }
    });

    it('snaps to whole device pixels', () => {
        const span = slotSpanPx(3, 0.7, 2);
        expect(Math.round(span.x * 2)).toBeCloseTo(span.x * 2);
        expect(Math.round(span.w * 2)).toBeCloseTo(span.w * 2);
    });
});
