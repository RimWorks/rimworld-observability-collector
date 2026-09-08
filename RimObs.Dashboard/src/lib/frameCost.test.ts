import { describe, it, expect } from 'vitest';
import {
    FRAME_BUDGET_US,
    speedMultiplier,
    tickBudgetUs,
    PER_SAMPLE_OVERHEAD_NS,
    OVERHEAD_SEED,
    DELTA_DEAD_BAND_US,
    shareOfFrame,
    shareOfBudget,
    percent,
    estimateOverheadUs,
    smoothOverhead,
    timerResolutionNs,
    percent2,
    nsPerScopeText,
    timerResText,
    budgetSeverity,
    deltaSeverity,
} from './frameCost';

describe('FRAME_BUDGET_US', () => {
    // Verse.TickManager.TickManagerUpdate: `clock.ElapsedMilliseconds > 45.454544f` ends the
    // tick loop, which is 1000 / TickManager.WorstAllowedFPS.
    it("is RimWorld's own 45.45 ms tick-loop bailout", () => {
        expect(FRAME_BUDGET_US).toBeCloseTo(45454.5, 1);
        expect(FRAME_BUDGET_US).toBeCloseTo(1_000_000 / 22, 0);
    });
});

describe('shareOfFrame', () => {
    it('matches the live example: 959us of a 4045us frame is 23.7%', () => {
        expect(percent(shareOfFrame(959, 4045))).toBe('23.7%');
    });

    it('returns 0 for a zero frame denominator', () => {
        expect(shareOfFrame(100, 0)).toBe(0);
    });

    it('returns 0 for a negative frame denominator', () => {
        expect(shareOfFrame(100, -50)).toBe(0);
    });

    it('returns 0 for a non-finite numerator or denominator', () => {
        expect(shareOfFrame(NaN, 4045)).toBe(0);
        expect(shareOfFrame(Infinity, 4045)).toBe(0);
        expect(shareOfFrame(100, NaN)).toBe(0);
        expect(shareOfFrame(100, Infinity)).toBe(0);
    });

    it('does not special-case a negative numerator', () => {
        expect(shareOfFrame(-100, 1000)).toBeCloseTo(-10, 5);
    });
});

describe('shareOfBudget', () => {
    it('matches the live example: 959us against the frame budget is 2.1%', () => {
        expect(percent(shareOfBudget(959))).toBe('2.1%');
    });

    it('returns 0 for a non-finite input', () => {
        expect(shareOfBudget(NaN)).toBe(0);
        expect(shareOfBudget(Infinity)).toBe(0);
    });

    it('does not special-case a negative duration', () => {
        expect(shareOfBudget(-4545.45)).toBeCloseTo(-10, 3);
    });

    it('does not clamp at 100% for a frame over the frame budget', () => {
        expect(percent(shareOfBudget(54_545.4))).toBe('120.0%');
    });
});

describe('percent', () => {
    it('formats non-finite values as a dash', () => {
        expect(percent(NaN)).toBe('-');
        expect(percent(Infinity)).toBe('-');
    });

    it('formats exactly zero without a fraction', () => {
        expect(percent(0)).toBe('0%');
    });

    it('uses 1 decimal at and above the 1% boundary', () => {
        expect(percent(1)).toBe('1.0%');
        expect(percent(120)).toBe('120.0%');
    });

    it('uses 2 decimals at and above the 0.1% boundary, below 1%', () => {
        expect(percent(0.1)).toBe('0.10%');
        expect(percent(0.5)).toBe('0.50%');
    });

    it('uses 3 decimals below the 0.1% boundary', () => {
        expect(percent(0.05)).toBe('0.050%');
    });
});

describe('estimateOverheadUs', () => {
    it('keeps the benchmarked constant positive', () => {
        expect(PER_SAMPLE_OVERHEAD_NS).toBeGreaterThan(0);
    });

    it('is zero for zero nodes', () => {
        expect(estimateOverheadUs(0)).toBe(0);
    });

    it('is zero for a non-finite or negative node count', () => {
        expect(estimateOverheadUs(NaN)).toBe(0);
        expect(estimateOverheadUs(Infinity)).toBe(0);
        expect(estimateOverheadUs(-5)).toBe(0);
    });

    it('matches the live 9-node example: 0.66096us, 0.016% of frame, 0.001% of budget', () => {
        const overheadUs = estimateOverheadUs(9);
        expect(overheadUs).toBeCloseTo(0.66096, 5);
        expect(percent(shareOfFrame(overheadUs, 4045))).toBe('0.016%');
        expect(percent(shareOfBudget(overheadUs))).toBe('0.001%');
    });

    // pathological node count, just to guard the constant from drifting to zero.
    it('scales linearly for a pathological node count', () => {
        expect(estimateOverheadUs(100_000)).toBeCloseTo(7344, 5);
    });

    it('can read over 100% of the frame budget, proving the readout is not clamped', () => {
        expect(shareOfBudget(estimateOverheadUs(1_000_000))).toBeGreaterThan(100);
    });
});

describe('nsPerScopeText and percent2', () => {
    it('matches the live 9-node example: 73 ns/scope, 0.02% of a 4045us frame', () => {
        expect(nsPerScopeText()).toBe('73');
        expect(percent2(shareOfFrame(estimateOverheadUs(9), 4045))).toBe('0.02%');
    });

    it('always shows two decimals, unlike percent()', () => {
        expect(percent2(0)).toBe('0.00%');
        expect(percent2(0.01634)).toBe('0.02%');
    });

    it('formats a non-finite value as a dash', () => {
        expect(percent2(NaN)).toBe('-');
        expect(percent2(Infinity)).toBe('-');
    });
});

describe('timerResolutionNs and timerResText', () => {
    it('converts a stopwatch frequency to nanoseconds per tick', () => {
        expect(timerResolutionNs(10_000_000)).toBe(100);
    });

    it('is zero for a non-finite or non-positive frequency', () => {
        expect(timerResolutionNs(0)).toBe(0);
        expect(timerResolutionNs(-1)).toBe(0);
        expect(timerResolutionNs(NaN)).toBe(0);
    });

    it('formats with at most one decimal, no trailing zero', () => {
        expect(timerResText(100)).toBe('100');
        expect(timerResText(1e9 / 2_995_000_000)).toBe('0.3');
        expect(timerResText(0)).toBe('0');
    });

    it('formats a non-finite value as a dash', () => {
        expect(timerResText(NaN)).toBe('-');
        expect(timerResText(Infinity)).toBe('-');
    });
});

describe('smoothOverhead', () => {
    it('seeds on the first sample instead of lerping up from zero', () => {
        const next = smoothOverhead(OVERHEAD_SEED, 10, 0);
        expect(next).toEqual({ percent: 10, atMs: 0 });
    });

    it('does not update inside the one-second window', () => {
        const seeded = smoothOverhead(OVERHEAD_SEED, 10, 0);
        const tooSoon = smoothOverhead(seeded, 9999, 999);
        expect(tooSoon).toBe(seeded);
        const onTime = smoothOverhead(seeded, 9999, 1000);
        expect(onTime.atMs).toBe(1000);
        expect(onTime.percent).not.toBe(seeded.percent);
    });

    it('converges toward a changed sample without jumping to it in one step', () => {
        const seeded = smoothOverhead(OVERHEAD_SEED, 10, 0);
        const step = smoothOverhead(seeded, 20, 1000);
        expect(step.percent).toBeGreaterThan(10);
        expect(step.percent).toBeLessThan(20);
        expect(step.percent).toBeCloseTo(13, 5);
    });

    it('treats a non-finite sample as zero', () => {
        const seeded = smoothOverhead(OVERHEAD_SEED, 10, 0);
        const next = smoothOverhead(seeded, NaN, 1000);
        expect(next.percent).toBeCloseTo(7, 5);
    });
});

describe('budgetSeverity', () => {
    it('is 0 exactly at the frame budget and 1 just over it', () => {
        expect(budgetSeverity(45454.5)).toBe(0);
        expect(budgetSeverity(45455)).toBe(1);
    });

    it('is 0 for a non-finite duration', () => {
        expect(budgetSeverity(NaN)).toBe(0);
    });
});

describe('deltaSeverity', () => {
    it('is 0 for a non-finite delta', () => {
        expect(deltaSeverity(NaN)).toBe(0);
    });

    it('checks out at every dead-band boundary', () => {
        expect(deltaSeverity(499)).toBe(0);
        expect(deltaSeverity(DELTA_DEAD_BAND_US)).toBe(1);
        expect(deltaSeverity(-499)).toBe(0);
        expect(deltaSeverity(-DELTA_DEAD_BAND_US)).toBe(-1);
    });
});

describe('speedMultiplier', () => {
    it('reads normal speed', () => {
        expect(speedMultiplier(60)).toBe(1);
    });

    it('reads fast', () => {
        expect(speedMultiplier(180)).toBe(3);
    });

    it('reads ultrafast', () => {
        expect(speedMultiplier(900)).toBe(15);
    });

    it('reads the mapless ultrafast boost', () => {
        expect(speedMultiplier(9000)).toBe(150);
    });

    it('snaps a game running a little behind to the speed it asked for', () => {
        expect(speedMultiplier(880)).toBe(15);
    });

    it('falls back to normal with no reading', () => {
        expect(speedMultiplier(null)).toBe(1);
        expect(speedMultiplier(0)).toBe(1);
        expect(speedMultiplier(Number.NaN)).toBe(1);
    });
});

describe('tickBudgetUs', () => {
    it('gives a tick a sixtieth of a second at normal speed', () => {
        expect(tickBudgetUs(60)).toBeCloseTo(16666.7, 0);
    });

    it('shrinks the tick budget as the speed rises', () => {
        expect(tickBudgetUs(900)).toBeCloseTo(1111.1, 0);
    });
});

describe('budgetSeverity', () => {
    it('passes a frame under RimWorld own 45.45 ms bailout', () => {
        expect(budgetSeverity(45000)).toBe(0);
    });

    it('flags a frame over it', () => {
        expect(budgetSeverity(46000)).toBe(1);
    });
});
