import { describe, it, expect } from 'vitest';
import { drawStrip, MARK_MIN_W, type StripTheme } from './stripDraw';
import { buildBars, GC_BAND_PX } from './frameStrip';
import { FRAME_BUDGET_US } from './frameCost';

const THEME: StripTheme = {
    background: '#000',
    good: '#00ff00',
    warn: '#ffff00',
    bad: '#ff0000',
    badDeep: '#880000',
    selected: '#sel',
    hover: '#hov',
    grid: '#grid',
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
    /** every paint in order, so tests can assert what covers what */
    const ops: string[] = [];
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
        fillRect: (x: number, y: number, w: number, h: number) => {
            ops.push('rect');
            rects.push({ x, y, w, h, fill: fillStyle });
        },
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
            ops.push('line');
            lines.push({ x1: from.x, y1: from.y, x2: x, y2: y, stroke: ctx.strokeStyle, dash });
        },
        stroke: () => {},
    };
    return { ctx: ctx as unknown as CanvasRenderingContext2D, rects, lines, ops };
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

    it('colors a comfortably-under-budget bar green and a 1.5x-over one red', () => {
        const { ctx, rects } = fakeCtx();
        // 1.5x the frame budget is the far end of the warn-to-bad lerp, so it lands exactly on bad.
        drawStrip(ctx, buildBars([1, 2], [1000, FRAME_BUDGET_US * 1.5]), opts());
        expect(rects[1].fill).toBe('#00ff00');
        expect(rects[2].fill).toBe('#ff0000');
    });

    it('keeps bars flush against each other, no inter-bar gap', () => {
        const { ctx, rects } = fakeCtx();
        // 40px wide for 4 bars is 10px a bar, well past the old >3px gap threshold.
        drawStrip(ctx, buildBars([1, 2, 3, 4], [1000, 1000, 1000, 1000]), {
            ...opts(),
            widthPx: 40,
        });
        const bars = rects.slice(1);
        for (let i = 0; i < bars.length - 1; i++) {
            expect(bars[i].x + bars[i].w).toBeCloseTo(bars[i + 1].x, 5);
        }
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

    it('draws a gc band tick plus a full-height rule for a frame that collected', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars([1, 2], [1000, 1000]), opts(null, [], [2]));
        const marks = rects.filter((r) => r.fill === '#gc');
        expect(marks).toHaveLength(2);
        expect(marks[0]).toMatchObject({ y: 0, h: 40 - GC_BAND_PX });
        expect(marks[1]).toMatchObject({ y: 40 - GC_BAND_PX, h: GC_BAND_PX });
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

describe('gridline paint order', () => {
    // a rule drawn over every bar reads as damage, not as a scale. the only thing that decides
    // this on a canvas is paint order, so that is what gets pinned.
    it('paints every gridline before any bar', () => {
        const { ctx, ops, lines } = fakeCtx();

        drawStrip(ctx, buildBars([1, 2, 3], [1000, 2000, 3000]), opts());

        // ops[0] is the background fill, so the first bar is the next rect after it.
        const firstBar = ops.indexOf('rect', 1);
        const gridCount = lines.filter((l) => l.stroke === '#grid').length;
        const gridOps = ops.slice(0, firstBar).filter((o) => o === 'line').length;

        expect(gridCount).toBeGreaterThan(0);
        expect(gridOps).toBe(gridCount);
    });

    it('draws the gridlines in the grid colour, spanning the full width', () => {
        const { ctx, lines } = fakeCtx();

        drawStrip(ctx, buildBars([1], [1000]), opts());

        const grid = lines.filter((l) => l.stroke === '#grid');
        expect(grid.length).toBeGreaterThan(0);
        for (const line of grid) {
            expect(line.x1).toBe(0);
            expect(line.x2).toBe(100);
            expect(line.y1).toBe(line.y2);
        }
    });
});

// a full ring lays 2000 bars across ~1400px, so a bar drawn at its own width is a sub-pixel
// sliver. the mark has to widen or clicking and hovering look like they did nothing.
describe('drawStrip marks', () => {
    const many = Array.from({ length: 200 }, (_, i) => i + 1);
    const flat = many.map(() => 1000);

    it('widens the selected bar to the minimum mark width', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars(many, flat), opts(7));
        const marked = rects.filter((r) => r.fill === '#sel');
        expect(marked).toHaveLength(1);
        expect(marked[0].w).toBe(MARK_MIN_W);
    });

    it('paints and widens the hovered bar', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars(many, flat), { ...opts(), hoveredOrdinal: 7 });
        const marked = rects.filter((r) => r.fill === '#hov');
        expect(marked).toHaveLength(1);
        expect(marked[0].w).toBe(MARK_MIN_W);
    });

    it('leaves the selection white when it is also hovered', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars(many, flat), { ...opts(7), hoveredOrdinal: 7 });
        expect(rects.filter((r) => r.fill === '#sel')).toHaveLength(1);
        expect(rects.filter((r) => r.fill === '#hov')).toHaveLength(0);
    });

    it('leaves unmarked bars at their own slot width', () => {
        const { ctx, rects } = fakeCtx();
        drawStrip(ctx, buildBars(many, flat), opts(7));
        const plain = rects.filter((r) => r.fill !== '#sel' && r.fill !== THEME.background);
        expect(plain.every((r) => r.w < MARK_MIN_W)).toBe(true);
    });
});
