import { describe, it, expect } from 'vitest';
import { drawStrip, type StripTheme } from './stripDraw';
import { buildBars, GC_BAND_PX } from './frameStrip';

const THEME: StripTheme = {
    background: '#000',
    bar: '#bar',
    over: '#over',
    selected: '#sel',
    line: '#line',
    cut: '#cut',
    gc: '#gc',
};

interface Line {
    x1: number;
    y1: number;
    x2: number;
    y2: number;
    stroke: string;
    dash: number[];
}

interface Rect {
    x: number;
    y: number;
    w: number;
    h: number;
    fill: string;
}

function fakeCtx() {
    const rects: Rect[] = [];
    const lines: Line[] = [];
    let fillStyle = '';
    let from = { x: 0, y: 0 };
    let dash: number[] = [];
    const ctx = {
        set fillStyle(v: string) {
            fillStyle = v;
        },
        get fillStyle() {
            return fillStyle;
        },
        strokeStyle: '',
        lineWidth: 0,
        setTransform: () => {},
        fillRect: (x: number, y: number, w: number, h: number) =>
            rects.push({ x, y, w, h, fill: fillStyle }),
        save: () => {},
        restore: () => {
            dash = [];
        },
        setLineDash: (d: number[]) => {
            dash = d;
        },
        beginPath: () => {},
        moveTo: (x: number, y: number) => {
            from = { x, y };
        },
        lineTo: (x: number, y: number) => {
            lines.push({ x1: from.x, y1: from.y, x2: x, y2: y, stroke: ctx.strokeStyle, dash });
        },
        stroke: () => {},
    };
    return { ctx: ctx as unknown as CanvasRenderingContext2D, rects, lines };
}

const opts = (
    selectedOrdinal: number | null = null,
    cutOrdinals: number[] = [],
    gcOrdinals: number[] = [],
) => ({
    widthPx: 100,
    heightPx: 40,
    dpr: 1,
    selectedOrdinal,
    cutOrdinals,
    gcOrdinals,
    theme: THEME,
});

describe('drawStrip', () => {
    it('paints the background then one rect a bar', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [1000, 2000]), opts());
        expect(rects[0]).toMatchObject({ x: 0, y: 0, w: 100, h: 40, fill: '#000' });
        expect(rects).toHaveLength(3);
    });

    it('colors an over-budget bar differently from an under-budget one', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [1000, 50_000]), opts());
        expect(rects[1].fill).toBe('#bar');
        expect(rects[2].fill).toBe('#over');
    });

    it('the selected bar wins over the over-budget color', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [1000, 40_000]), opts(2));
        expect(rects[2].fill).toBe('#sel');
    });

    it('grows the bar upward from the bottom edge', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([1], [66_666.4]), opts());
        // full scale fills the bar area, which stops short of the reserved GC band
        expect(rects[1].h).toBeCloseTo(40 - GC_BAND_PX, 5);
        expect(rects[1].y).toBeCloseTo(0, 5);
    });

    // a 2000-frame ring on a 100px strip is 0.05px a bar, which would draw nothing.
    it('keeps every bar at least a pixel wide', () => {
        const { ctx, rects } = fakeCtx();
        const n = 2000;
        const bars = buildBars(
            Array.from({ length: n }, (_, i) => i),
            Array.from({ length: n }, () => 1000),
        );
        drawStrip(ctx, bars, opts());
        expect(rects.slice(1).every((r) => r.w >= 1)).toBe(true);
    });

    it('skips zero-duration frames instead of drawing a sliver', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [0, 1000]), opts());
        expect(rects).toHaveLength(2);
    });

    it('draws nothing but the background for an empty strip', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, [], opts());
        expect(rects).toHaveLength(1);
    });

    it('draws a dashed rule where a pause cut the history', () => {
        const { ctx, lines } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2, 3, 4], [1000, 1000, 1000, 1000]), opts(null, [2]));
        const cut = lines.find((l) => l.stroke === '#cut');
        expect(cut).toBeDefined();
        expect(cut!.dash).toEqual([3, 3]);
        // four bars across 100px, the cut sits on the leading edge of the second
        expect(cut!.x1).toBeCloseTo(25.5);
        expect(cut!.y1).toBe(0);
        expect(cut!.y2).toBe(40);
    });

    it('draws no cut rule when nothing was paused', () => {
        const { ctx, lines } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [1000, 2000]), opts());
        expect(lines.some((l) => l.stroke === '#cut')).toBe(false);
    });

    it('ignores a cut ordinal that has aged out of the ring', () => {
        const { ctx, lines } = fakeCtx();
        drawStrip(ctx, buildBars([5, 6], [1000, 1000]), opts(null, [2]));
        expect(lines.some((l) => l.stroke === '#cut')).toBe(false);
    });

    // the newest bar's trailing edge is the canvas border, where a rule cannot be seen.
    it('keeps a cut on the newest bar inside the canvas', () => {
        const { ctx, lines } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [1000, 1000]), opts(null, [2]));
        const cut = lines.find((l) => l.stroke === '#cut');
        expect(cut!.x1).toBeLessThan(100);
        expect(cut!.x1).toBeGreaterThan(0);
    });

    it('draws a gc mark hanging below the baseline for a frame that collected', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [1000, 1000]), opts(null, [], [2]));
        const mark = rects.find((r) => r.fill === '#gc');
        expect(mark).toMatchObject({ y: 40 - GC_BAND_PX, h: GC_BAND_PX });
    });

    it('draws no gc marks when nothing collected', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [1000, 1000]), opts());
        expect(rects.some((r) => r.fill === '#gc')).toBe(false);
    });

    it('skips a gc mark for a frame the ring already evicted', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([5, 6], [1000, 1000]), opts(null, [], [99]));
        expect(rects.some((r) => r.fill === '#gc')).toBe(false);
    });
});
