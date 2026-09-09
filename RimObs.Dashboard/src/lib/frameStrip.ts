import { FRAME_BUDGET_US } from './frameCost';

// Neo normalises every bar against 66.667ms (15fps) and clamps, so a 200ms stall and a 70ms
// one both read as full height. The budget line is what gives the strip its scale.
export const STRIP_FULL_SCALE_US = 66666.4;

export interface StripBar {
    ordinal: number;
    durationUs: number;
    /** 0..1 against STRIP_FULL_SCALE_US */
    height: number;
    overBudget: boolean;
}

export function barHeight(durationUs: number): number {
    if (!(durationUs > 0)) return 0;
    return Math.min(1, durationUs / STRIP_FULL_SCALE_US);
}

/** Fraction of the strip height the budget line sits at. */
export function budgetLine(budgetUs = FRAME_BUDGET_US): number {
    return Math.min(1, budgetUs / STRIP_FULL_SCALE_US);
}

export function buildBars(
    ordinals: readonly number[],
    durationsUs: readonly number[],
    budgetUs = FRAME_BUDGET_US,
): StripBar[] {
    const n = Math.min(ordinals.length, durationsUs.length);
    const bars = new Array<StripBar>(n);
    for (let i = 0; i < n; i++) {
        const durationUs = durationsUs[i];
        bars[i] = {
            ordinal: ordinals[i],
            durationUs,
            height: barHeight(durationUs),
            overBudget: durationUs > budgetUs,
        };
    }
    return bars;
}

/**
 * Which bar a click at `x` lands on. Bars are laid out oldest-left, and the strip keeps them
 * at least a pixel wide so a full 2000-frame ring is still clickable on a narrow panel.
 */
export function barIndexAt(x: number, widthPx: number, count: number): number {
    if (count <= 0 || widthPx <= 0) return -1;
    const index = Math.floor((x / widthPx) * count);
    if (index < 0 || index >= count) return -1;
    return index;
}

export function barWidthPx(widthPx: number, count: number): number {
    if (count <= 0) return 0;
    return Math.max(1, widthPx / count);
}

/**
 * Step by `delta` frames through the strip, clamped to its ends. Returns the ordinal to show,
 * or null when the strip is empty.
 */
export function stepOrdinal(
    bars: readonly StripBar[],
    currentOrdinal: number | null,
    delta: number,
): number | null {
    if (bars.length === 0) return null;
    if (currentOrdinal === null) return bars[bars.length - 1].ordinal;
    let index = bars.findIndex((b) => b.ordinal === currentOrdinal);
    // an ordinal that aged out of the ring lands us at the oldest frame still held.
    if (index < 0) index = currentOrdinal < bars[0].ordinal ? 0 : bars.length - 1;
    const next = Math.min(bars.length - 1, Math.max(0, index + delta));
    return bars[next].ordinal;
}

/** Neo labels the history axis in FPS, which is what a reader actually thinks in. */
export const FPS_GRID = [15, 20, 30, 60, 120] as const;

export interface GridLine {
    fps: number;
    ms: number;
    /** 0..1 from the bottom */
    at: number;
}

export function gridLines(): GridLine[] {
    const out: GridLine[] = [];
    for (const fps of FPS_GRID) {
        const ms = 1000 / fps;
        // 1000/15 lands a hair over full scale, so pin it to the ceiling rather than drop it.
        out.push({ fps, ms, at: Math.min(1, (ms * 1000) / STRIP_FULL_SCALE_US) });
    }
    return out;
}

/** Reserved pixel height below the bar baseline for the GC tick lane. */
export const GC_BAND_PX = 8;

/**
 * Bar indices whose frame had a collection, deduped per frame so several GCs in one frame
 * still draw one mark, and silently dropping ordinals the strip's ring already evicted.
 */
export function gcMarkIndices(bars: readonly StripBar[], gcOrdinals: readonly number[]): number[] {
    if (gcOrdinals.length === 0) return [];
    const ordinals = new Set(gcOrdinals);
    const indices: number[] = [];
    for (let i = 0; i < bars.length; i++) {
        if (ordinals.has(bars[i].ordinal)) indices.push(i);
    }
    return indices;
}
