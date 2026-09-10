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

/** Frames the strip lays out for, whether or not that many have arrived yet. */
export const DEFAULT_STRIP_SLOTS = 2000;

/**
 * Width of one frame's slot. Divided by the ring's capacity rather than by how many frames
 * have arrived, so a bar is the same width at frame 5 and at frame 1999. Deliberately not
 * floored at a pixel: flooring makes the slots sum wider than the panel, which pushes the
 * newest frames off the canvas once the ring passes the panel's pixel width.
 */
export function slotWidthPx(widthPx: number, slots: number): number {
    if (widthPx <= 0) return 0;
    return widthPx / Math.max(1, slots);
}

/**
 * Which bar a click at `x` lands on. Bars fill fixed slots oldest-left, so a click on the
 * empty strip past the newest frame selects nothing.
 */
export function barIndexAt(x: number, widthPx: number, count: number, slots = count): number {
    if (count <= 0 || widthPx <= 0) return -1;
    const bw = slotWidthPx(widthPx, slots);
    if (bw <= 0) return -1;
    const index = Math.floor(x / bw);
    if (index < 0 || index >= count) return -1;
    return index;
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

export interface BudgetColorScale {
    good: string;
    warn: string;
    bad: string;
    badDeep: string;
}

function hexToRgb(hex: string): [number, number, number] {
    const h = hex.replace('#', '');
    return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];
}

function rgbToHex(r: number, g: number, b: number): string {
    const clamp = (n: number) =>
        Math.round(Math.min(255, Math.max(0, n)))
            .toString(16)
            .padStart(2, '0');
    return `#${clamp(r)}${clamp(g)}${clamp(b)}`;
}

function lerpHex(a: string, b: string, t: number): string {
    const [ar, ag, ab] = hexToRgb(a);
    const [br, bg, bb] = hexToRgb(b);
    return rgbToHex(ar + (br - ar) * t, ag + (bg - ag) * t, ab + (bb - ab) * t);
}

// comfortably under budget stays flat green; past this ratio it starts climbing to amber.
const BUDGET_COMFORTABLE_RATIO = 0.6;
// amber turns to red between budget and 1.5x budget, then keeps darkening to 3x before it clamps.
const BUDGET_OVER_RATIO = 1.5;
const BUDGET_FAR_OVER_RATIO = 3;

/**
 * Colors a bar by how close its duration sits to the frame budget: green under it, amber
 * approaching it, red past it, and darker red the further over it runs.
 */
export function barColor(durationUs: number, budgetUs: number, colors: BudgetColorScale): string {
    if (!(budgetUs > 0)) return colors.good;
    const ratio = Math.max(0, durationUs) / budgetUs;
    if (ratio <= BUDGET_COMFORTABLE_RATIO) return colors.good;
    if (ratio <= 1) {
        return lerpHex(
            colors.good,
            colors.warn,
            (ratio - BUDGET_COMFORTABLE_RATIO) / (1 - BUDGET_COMFORTABLE_RATIO),
        );
    }
    if (ratio <= BUDGET_OVER_RATIO) {
        return lerpHex(colors.warn, colors.bad, (ratio - 1) / (BUDGET_OVER_RATIO - 1));
    }
    if (ratio <= BUDGET_FAR_OVER_RATIO) {
        return lerpHex(
            colors.bad,
            colors.badDeep,
            (ratio - BUDGET_OVER_RATIO) / (BUDGET_FAR_OVER_RATIO - BUDGET_OVER_RATIO),
        );
    }
    return colors.badDeep;
}

/**
 * Clamps a cursor-following tooltip's center x so it never hangs off either edge of the strip.
 * Falls back to dead center when the tooltip itself is wider than the container.
 */
export function clampTooltipX(x: number, tooltipWidthPx: number, containerWidthPx: number): number {
    if (containerWidthPx <= 0) return 0;
    const half = tooltipWidthPx / 2;
    const max = Math.max(half, containerWidthPx - half);
    return Math.min(max, Math.max(half, x));
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
