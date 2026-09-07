import { describe, it, expect } from 'vitest';
import { drawStrip, type StripTheme } from './stripDraw';
import { buildBars } from './frameStrip';

const THEME: StripTheme = {
    background: '#000',
    bar: '#bar',
    over: '#over',
    selected: '#sel',
    line: '#line',
};

interface Rect {
    x: number;
    y: number;
    w: number;
    h: number;
    fill: string;
}

function fakeCtx() {
    const rects: Rect[] = [];
    let fillStyle = '';
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
        beginPath: () => {},
        moveTo: () => {},
        lineTo: () => {},
        stroke: () => {},
    };
    return { ctx: ctx as unknown as CanvasRenderingContext2D, rects };
}

const opts = (selectedOrdinal: number | null = null) => ({
    widthPx: 100,
    heightPx: 40,
    dpr: 1,
    selectedOrdinal,
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
        drawStrip(ctx, buildBars([1, 2], [1000, 40_000]), opts());
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
        // full scale on a 40px strip is the whole height
        expect(rects[1].h).toBeCloseTo(40, 5);
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
});
