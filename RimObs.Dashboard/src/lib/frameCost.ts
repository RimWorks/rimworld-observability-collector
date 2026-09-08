/**
 * Verse.TickManager.TickManagerUpdate breaks out of its tick loop once the frame has spent
 * 45.454544 ms in it (TickManager.WorstAllowedFPS is 22), and hands the frame back to
 * rendering. That is RimWorld's own definition of a frame that ran long, and it does not
 * move with the speed setting.
 */
export const FRAME_BUDGET_US = 45454.5;

/** RimWorld's simulation clock at Normal speed. */
export const BASE_TPS = 60;

/** Every value Verse.TickManager.TickRateMultiplier can return, paused aside. */
export const TICK_MULTIPLIERS = [1, 3, 6, 12, 15, 18, 150] as const;

/**
 * The speed setting the game is on, guessed from the TPS it is achieving. Ratio distance, not
 * linear, because the legal multipliers run from 1 to 150. A game falling behind its target
 * reads as the next speed down, which is the one case this cannot tell apart.
 */
export function speedMultiplier(tps: number | null | undefined): number {
    if (tps == null || !Number.isFinite(tps) || tps <= 0) return 1;
    let best = TICK_MULTIPLIERS[0];
    let bestDistance = Infinity;
    for (const multiplier of TICK_MULTIPLIERS) {
        const distance = Math.abs(Math.log(tps / (multiplier * BASE_TPS)));
        if (distance < bestDistance) {
            bestDistance = distance;
            best = multiplier;
        }
    }
    return best;
}

/** How long one tick may take to sustain the current speed. */
export function tickBudgetUs(tps: number | null | undefined): number {
    return 1_000_000 / (BASE_TPS * speedMultiplier(tps));
}

/**
 * cost of one enabled Profiler.Start/Stop pair. source of truth is
 * tests/RimObs.Library.Tests/ProfilerOverheadTests.cs; rerun it if this moves.
 */
export const PER_SAMPLE_OVERHEAD_NS = 73.44;

export function shareOfFrame(durUs: number, frameDurationUs: number): number {
    if (!Number.isFinite(durUs) || !Number.isFinite(frameDurationUs) || frameDurationUs <= 0) {
        return 0;
    }
    return (durUs / frameDurationUs) * 100;
}

export function shareOfBudget(durUs: number): number {
    if (!Number.isFinite(durUs)) return 0;
    return (durUs / FRAME_BUDGET_US) * 100;
}

export function percent(value: number): string {
    if (!Number.isFinite(value)) return '-';
    if (value === 0) return '0%';
    const abs = Math.abs(value);
    let decimals = 3;
    if (abs >= 1) decimals = 1;
    else if (abs >= 0.1) decimals = 2;
    return `${value.toFixed(decimals)}%`;
}

export function estimateOverheadUs(nodeCount: number): number {
    if (!Number.isFinite(nodeCount) || nodeCount < 0) return 0;
    return (nodeCount * PER_SAMPLE_OVERHEAD_NS) / 1000;
}

// Neo's EWMA factor and refresh cadence; smoothOverhead reproduces its gate exactly.
export const OVERHEAD_SMOOTHING = 0.3;
export const OVERHEAD_REFRESH_MS = 1000;

export interface OverheadState {
    percent: number;
    atMs: number;
}

export const OVERHEAD_SEED: OverheadState = { percent: 0, atMs: Number.NEGATIVE_INFINITY };

/**
 * once-per-second EWMA gate, matching Neo.PCUI.ProfilerSummaryViewModel.Refresh(). the
 * first sample seeds the average instead of lerping up from zero.
 */
export function smoothOverhead(
    prev: OverheadState,
    samplePercent: number,
    nowMs: number,
): OverheadState {
    if (nowMs - prev.atMs < OVERHEAD_REFRESH_MS) return prev;
    const sample = Number.isFinite(samplePercent) ? samplePercent : 0;
    const next =
        prev.percent <= 0 ? sample : prev.percent + (sample - prev.percent) * OVERHEAD_SMOOTHING;
    return { percent: next, atMs: nowMs };
}

export function timerResolutionNs(stopwatchFrequency: number): number {
    if (!Number.isFinite(stopwatchFrequency) || stopwatchFrequency <= 0) return 0;
    return 1e9 / stopwatchFrequency;
}

/** Neo's {0:0.00} format: always two decimals. separate from percent(), which bands by magnitude. */
export function percent2(value: number): string {
    if (!Number.isFinite(value)) return '-';
    return `${value.toFixed(2)}%`;
}

export function nsPerScopeText(): string {
    return String(Math.round(PER_SAMPLE_OVERHEAD_NS));
}

/** Neo's {0:0.#} format: at most one decimal, no trailing zero. */
export function timerResText(ns: number): string {
    if (!Number.isFinite(ns)) return '-';
    return (Math.round(ns * 10) / 10).toString();
}

// Neo's dead band, 0.5ms converted to our microseconds.
export const DELTA_DEAD_BAND_US = 500;

export function budgetSeverity(frameDurationUs: number): 0 | 1 {
    if (!Number.isFinite(frameDurationUs)) return 0;
    return frameDurationUs > FRAME_BUDGET_US ? 1 : 0;
}

export function deltaSeverity(deltaUs: number): -1 | 0 | 1 {
    if (!Number.isFinite(deltaUs)) return 0;
    if (deltaUs >= DELTA_DEAD_BAND_US) return 1;
    if (deltaUs <= -DELTA_DEAD_BAND_US) return -1;
    return 0;
}
