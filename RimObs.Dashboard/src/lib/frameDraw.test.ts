import { describe, it, expect } from 'vitest';
import { drawTimeline, readTheme, ROW_HEIGHT, type DrawOptions, type DrawTheme } from './frameDraw';
import type { Quad } from './frameLayout';

interface Call {
    op: string;
    args: unknown[];
}

function recorder(): { ctx: CanvasRenderingContext2D; calls: Call[] } {
    const calls: Call[] = [];
    const push =
        (op: string) =>
        (...args: unknown[]): void => {
            calls.push({ op, args });
        };
    let fillStyle = '#000';
    let strokeStyle = '#000';
    let font = '';
    const stub = {
        save: push('save'),
        restore: push('restore'),
        scale: push('scale'),
        clearRect: push('clearRect'),
        fillRect: push('fillRect'),
        fillText: push('fillText'),
        strokeRect: push('strokeRect'),
        measureText: (text: string) => ({ width: text.length * 7 }),
        set fillStyle(v: string) {
            fillStyle = v;
            calls.push({ op: 'fillStyle', args: [v] });
        },
        get fillStyle() {
            return fillStyle;
        },
        set strokeStyle(v: string) {
            strokeStyle = v;
            calls.push({ op: 'strokeStyle', args: [v] });
        },
        get strokeStyle() {
            return strokeStyle;
        },
        set font(v: string) {
            font = v;
        },
        get font() {
            return font;
        },
        lineWidth: 1,
        textBaseline: 'alphabetic',
    };
    return { ctx: stub as unknown as CanvasRenderingContext2D, calls };
}

// finds the fillStyle in effect when the nth fillRect fired, by scanning back to
// the closest preceding fillStyle assignment.
function fillStyleForRect(calls: Call[], rectIndex: number): string {
    const rectPositions = calls.reduce<number[]>((acc, c, i) => {
        if (c.op === 'fillRect') acc.push(i);
        return acc;
    }, []);
    const at = rectPositions[rectIndex];
    for (let i = at; i >= 0; i--) {
        if (calls[i].op === 'fillStyle') return calls[i].args[0] as string;
    }
    throw new Error('no fillStyle recorded before fillRect');
}

function luminanceOf(rgbOrHex: string): number {
    const nums = rgbOrHex.match(/\d+(?:\.\d+)?/g);
    if (nums && rgbOrHex.startsWith('rgb')) {
        const [r, g, b] = nums.map(Number);
        const lin = [r, g, b].map((v) => {
            const c = v / 255;
            return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
        });
        return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2];
    }
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(rgbOrHex);
    if (!m) return 0;
    const [r, g, b] = [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)];
    const lin = [r, g, b].map((v) => {
        const c = v / 255;
        return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    });
    return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2];
}

const THEME: DrawTheme = {
    background: '#0b0e14',
    collapsed: '#28344a',
    inkLight: '#d4dded',
    inkDark: '#080a0f',
    hue: { tick: '#39c4d4', ai: '#7c9cf5', render: '#e8b53e', ui: '#b184e8' },
    hueNone: '#5c6b85',
    font: '11px monospace',
};

function opts(over: Partial<DrawOptions> = {}): DrawOptions {
    return {
        view: { startUs: 0, endUs: 1000 },
        widthPx: 1000,
        heightPx: 400,
        dpr: 1,
        theme: THEME,
        label: () => 'Verse.TickList.Tick',
        subsystem: () => 'tick',
        hoverIndex: -1,
        focusIndex: -1,
        ...over,
    };
}

function quad(over: Partial<Quad> = {}): Quad {
    return {
        depth: 0,
        startUs: 0,
        endUs: 500,
        totalUs: 500,
        sectionId: 1,
        count: 1,
        firstIndex: 0,
        ...over,
    };
}

describe('drawTimeline', () => {
    it('clears before it paints', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad()], opts());
        expect(calls[0].op).toBe('save');
        const clearIdx = calls.findIndex((c) => c.op === 'clearRect');
        const rectIdx = calls.findIndex((c) => c.op === 'fillRect');
        expect(clearIdx).toBeGreaterThanOrEqual(0);
        expect(clearIdx).toBeLessThan(rectIdx);
    });

    it('scales by dpr', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad()], opts({ dpr: 2 }));
        const scaleCall = calls.find((c) => c.op === 'scale');
        expect(scaleCall?.args).toEqual([2, 2]);
    });

    it('draws one rect per quad', () => {
        const { ctx, calls } = recorder();
        const quads = [
            quad({ depth: 0, startUs: 0, endUs: 100 }),
            quad({ depth: 1, startUs: 200, endUs: 300 }),
            quad({ depth: 2, startUs: 400, endUs: 500 }),
        ];
        drawTimeline(ctx, quads, opts());
        expect(calls.filter((c) => c.op === 'fillRect')).toHaveLength(3);
    });

    it('calls save and restore exactly once on a path that paints rects', () => {
        const { ctx, calls } = recorder();
        const quads = [
            quad({ depth: 0, startUs: 0, endUs: 100 }),
            quad({ depth: 1, startUs: 200, endUs: 300 }),
        ];
        drawTimeline(ctx, quads, opts());
        expect(calls.filter((c) => c.op === 'save')).toHaveLength(1);
        expect(calls.filter((c) => c.op === 'restore')).toHaveLength(1);
    });

    it('draws each row 1px shorter than ROW_HEIGHT for a visible gap', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad()], opts());
        const rect = calls.find((c) => c.op === 'fillRect');
        expect(rect?.args[3]).toBe(ROW_HEIGHT - 1);
    });

    it('maps time to x and depth to y', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad({ depth: 2, startUs: 100, endUs: 200 })], opts());
        const rect = calls.find((c) => c.op === 'fillRect');
        expect(rect?.args[0]).toBeCloseTo(100);
        expect(rect?.args[1]).toBeCloseTo(2 * ROW_HEIGHT);
        expect(rect?.args[2]).toBeCloseTo(100);
    });

    it('never draws narrower than one pixel', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad({ startUs: 500, endUs: 500.01 })], opts());
        const rect = calls.find((c) => c.op === 'fillRect');
        expect(rect?.args[2]).toBeCloseTo(1);
    });

    it('still paints a zero-duration quad at the one-pixel floor', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad({ startUs: 500, endUs: 500 })], opts());
        const rect = calls.find((c) => c.op === 'fillRect');
        expect(rect?.args[0]).toBeCloseTo(500);
        expect(rect?.args[2]).toBeCloseTo(1);
    });

    it('labels a wide enough quad', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad()], opts());
        const text = calls.find((c) => c.op === 'fillText');
        expect(text?.args[0]).toBe('Verse.TickList.Tick');
    });

    it('centers the label vertically in its row', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad({ depth: 2 })], opts({ heightPx: 200 }));
        const text = calls.find((c) => c.op === 'fillText');
        expect(text?.args[2]).toBeCloseTo(2 * ROW_HEIGHT + ROW_HEIGHT / 2);
    });

    it('skips a label that does not fit its own quad', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad({ endUs: 5 })], opts());
        expect(calls.some((c) => c.op === 'fillText')).toBe(false);
        expect(calls.some((c) => c.op === 'fillRect')).toBe(true);
    });

    it('skips a label that would overlap the one already drawn in that row', () => {
        const { ctx, calls } = recorder();
        const label = 'x'.repeat(56);
        const quads = [quad({ startUs: 0, endUs: 400 }), quad({ startUs: 400, endUs: 800 })];
        drawTimeline(ctx, quads, opts({ label: () => label }));
        expect(calls.filter((c) => c.op === 'fillText')).toHaveLength(1);
    });

    it('labels a collapsed run with its count', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [quad({ count: 312, sectionId: -1 })], opts());
        const text = calls.find((c) => c.op === 'fillText');
        expect(text?.args[0]).toBe('Verse.TickList.Tick (312)');
    });

    it('draws no fillRect for an empty layout', () => {
        const { ctx, calls } = recorder();
        drawTimeline(ctx, [], opts());
        expect(calls.some((c) => c.op === 'fillRect')).toBe(false);
        expect(calls.filter((c) => c.op === 'save')).toHaveLength(1);
        expect(calls.filter((c) => c.op === 'restore')).toHaveLength(1);
    });

    describe('clipping to the canvas', () => {
        it('paints nothing for a quad entirely left of the view', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad({ startUs: -500, endUs: -100 })], opts());
            expect(calls.some((c) => c.op === 'fillRect')).toBe(false);
        });

        it('clips a quad straddling the view start to x=0 and shrinks the width', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad({ startUs: -100, endUs: 200 })], opts());
            const rect = calls.find((c) => c.op === 'fillRect');
            expect(rect?.args[0]).toBeCloseTo(0);
            expect(rect?.args[2]).toBeCloseTo(200);
        });

        it('clips a quad running past the view end at widthPx', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad({ startUs: 900, endUs: 1200 })], opts());
            const rect = calls.find((c) => c.op === 'fillRect');
            expect(rect?.args[0]).toBeCloseTo(900);
            expect(rect?.args[2]).toBeCloseTo(100);
        });

        it('applies the one-pixel floor to the clipped width, not the raw width', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad({ startUs: -1000, endUs: 0.5 })], opts());
            const rect = calls.find((c) => c.op === 'fillRect');
            expect(rect?.args[0]).toBeCloseTo(0);
            expect(rect?.args[2]).toBeCloseTo(1);
        });

        it('positions a clipped label at the visible left edge, not the raw offscreen x', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad({ startUs: -500, endUs: 800 })], opts());
            const text = calls.find((c) => c.op === 'fillText');
            expect(text?.args[1]).toBeCloseTo(4);
        });

        it('checks label fit against the clipped width, not the full quad width', () => {
            const { ctx, calls } = recorder();
            // unclipped this quad is 950px wide; clipped to the view it is only 50px, too narrow for the label.
            drawTimeline(ctx, [quad({ startUs: -900, endUs: 50 })], opts());
            expect(calls.some((c) => c.op === 'fillText')).toBe(false);
            const rect = calls.find((c) => c.op === 'fillRect');
            expect(rect?.args[2]).toBeCloseTo(50);
        });
    });

    it('resets label overlap tracking per depth row across a multi-row layout', () => {
        // pins the assumption that quads arrive grouped depth-then-start (frameLayout.ts).
        const { ctx, calls } = recorder();
        const label = 'x'.repeat(56);
        const quads = [
            quad({ depth: 0, startUs: 0, endUs: 400 }),
            quad({ depth: 0, startUs: 400, endUs: 800 }),
            quad({ depth: 1, startUs: 0, endUs: 400 }),
            quad({ depth: 1, startUs: 400, endUs: 800 }),
        ];
        drawTimeline(ctx, quads, opts({ label: () => label }));
        expect(calls.filter((c) => c.op === 'fillText')).toHaveLength(2);
    });

    describe('hoverIndex and focusIndex', () => {
        it('lightens the fill of the hovered quad', () => {
            const plain = recorder();
            drawTimeline(plain.ctx, [quad()], opts());
            const plainFill = fillStyleForRect(plain.calls, 0);

            const hovered = recorder();
            drawTimeline(hovered.ctx, [quad()], opts({ hoverIndex: 0 }));
            const hoverFill = fillStyleForRect(hovered.calls, 0);

            expect(hoverFill).not.toBe(plainFill);
            expect(luminanceOf(hoverFill)).toBeGreaterThan(luminanceOf(plainFill));
        });

        it('outlines the focused quad', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad()], opts({ focusIndex: 0 }));
            expect(calls.some((c) => c.op === 'strokeRect')).toBe(true);
        });

        it('draws the focus ring 2px wide', () => {
            const { ctx } = recorder();
            drawTimeline(ctx, [quad()], opts({ focusIndex: 0 }));
            expect(ctx.lineWidth).toBe(2);
        });

        it('lightens the fill of the focused quad', () => {
            const plain = recorder();
            drawTimeline(plain.ctx, [quad()], opts());
            const plainFill = fillStyleForRect(plain.calls, 0);

            const focused = recorder();
            drawTimeline(focused.ctx, [quad()], opts({ focusIndex: 0 }));
            const focusFill = fillStyleForRect(focused.calls, 0);

            expect(focusFill).not.toBe(plainFill);
            expect(luminanceOf(focusFill)).toBeGreaterThan(luminanceOf(plainFill));
        });

        it('outlines a clipped focused quad at the clipped bounds, not the raw offscreen ones', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad({ startUs: -100, endUs: 200 })], opts({ focusIndex: 0 }));
            const stroke = calls.find((c) => c.op === 'strokeRect');
            expect(stroke?.args).toEqual([1, 1, 198, ROW_HEIGHT - 3]);
        });

        it('draws no outline when nothing is focused', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad()], opts());
            expect(calls.some((c) => c.op === 'strokeRect')).toBe(false);
        });

        it('ignores an out-of-range hoverIndex or focusIndex', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad()], opts({ hoverIndex: 99, focusIndex: 99 }));
            expect(calls.some((c) => c.op === 'strokeRect')).toBe(false);
        });
    });

    describe('fill color', () => {
        it('lifts fill luminance with depth for the same subsystem', () => {
            const shallow = recorder();
            drawTimeline(shallow.ctx, [quad({ depth: 0 })], opts({ heightPx: 200 }));
            const shallowFill = fillStyleForRect(shallow.calls, 0);

            const deep = recorder();
            drawTimeline(deep.ctx, [quad({ depth: 5 })], opts({ heightPx: 200 }));
            const deepFill = fillStyleForRect(deep.calls, 0);

            expect(luminanceOf(deepFill)).toBeGreaterThan(luminanceOf(shallowFill));
        });

        it('uses the subsystem hue instead of always falling back to hueNone', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad()], opts({ subsystem: () => 'render' }));
            expect(fillStyleForRect(calls, 0)).toBe('rgb(235, 190, 85)');

            const none = recorder();
            drawTimeline(none.ctx, [quad()], opts({ subsystem: () => null }));
            expect(fillStyleForRect(none.calls, 0)).not.toBe('rgb(235, 190, 85)');
        });

        it('only switches to the collapsed color when sectionId is negative', () => {
            const { ctx, calls } = recorder();
            drawTimeline(
                ctx,
                [quad({ count: 9, sectionId: 3 })],
                opts({ subsystem: () => 'render' }),
            );
            expect(fillStyleForRect(calls, 0)).toBe('rgb(235, 190, 85)');

            const collapsed = recorder();
            drawTimeline(
                collapsed.ctx,
                [quad({ count: 9, sectionId: -1 })],
                opts({ subsystem: () => 'render' }),
            );
            expect(fillStyleForRect(collapsed.calls, 0)).toBe('rgb(66, 76, 96)');
        });
    });

    describe('heightPx culling', () => {
        it('skips a row that starts at or past heightPx', () => {
            const { ctx, calls } = recorder();
            const quads = [
                quad({ depth: 0, startUs: 0, endUs: 400 }),
                quad({ depth: 5, startUs: 0, endUs: 400 }),
            ];
            drawTimeline(ctx, quads, opts({ heightPx: 50 }));
            expect(calls.filter((c) => c.op === 'fillRect')).toHaveLength(1);
        });

        it('still draws a row that only partially overhangs heightPx', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad({ depth: 1, startUs: 0, endUs: 400 })], opts({ heightPx: 20 }));
            expect(calls.filter((c) => c.op === 'fillRect')).toHaveLength(1);
        });
    });

    describe('degenerate inputs', () => {
        it('balances save/restore and paints nothing for a zero-width view', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad()], opts({ view: { startUs: 100, endUs: 100 } }));
            expect(calls.filter((c) => c.op === 'save')).toHaveLength(1);
            expect(calls.filter((c) => c.op === 'restore')).toHaveLength(1);
            expect(calls.some((c) => c.op === 'fillRect')).toBe(false);
        });

        it('balances save/restore and paints nothing for a zero widthPx', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad()], opts({ widthPx: 0 }));
            expect(calls.filter((c) => c.op === 'save')).toHaveLength(1);
            expect(calls.filter((c) => c.op === 'restore')).toHaveLength(1);
            expect(calls.some((c) => c.op === 'fillRect')).toBe(false);
        });

        it('balances save/restore and paints nothing for a zero heightPx', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [quad()], opts({ heightPx: 0 }));
            expect(calls.filter((c) => c.op === 'save')).toHaveLength(1);
            expect(calls.filter((c) => c.op === 'restore')).toHaveLength(1);
            expect(calls.some((c) => c.op === 'fillRect')).toBe(false);
        });

        it('paints normally with an empty quad list and dpr 1', () => {
            const { ctx, calls } = recorder();
            drawTimeline(ctx, [], opts({ dpr: 1 }));
            expect(calls.find((c) => c.op === 'scale')?.args).toEqual([1, 1]);
        });
    });
});

describe('ink contrast against every fill', () => {
    function contrastRatio(l1: number, l2: number): number {
        const lighter = Math.max(l1, l2);
        const darker = Math.min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    type QuadState = 'plain' | 'hovered' | 'focused';

    function inkAndFillFor(
        quadOver: Partial<Quad>,
        state: QuadState,
        subsystem: () => string | null,
    ): { fill: string; ink: string; stroke: string | null } {
        const { ctx, calls } = recorder();
        drawTimeline(
            ctx,
            [quad(quadOver)],
            opts({
                heightPx: 200,
                subsystem,
                hoverIndex: state === 'hovered' ? 0 : -1,
                focusIndex: state === 'focused' ? 0 : -1,
            }),
        );
        const fillStyles = calls
            .filter((c) => c.op === 'fillStyle')
            .map((c) => c.args[0] as string);
        const stroke = calls.find((c) => c.op === 'strokeStyle')?.args[0] as string | undefined;
        return { fill: fillStyles[0], ink: fillStyles[1], stroke: stroke ?? null };
    }

    const SUBSYSTEMS_UNDER_TEST: Array<[string, () => string | null]> = [
        ['tick', () => 'tick'],
        ['ai', () => 'ai'],
        ['render', () => 'render'],
        ['ui', () => 'ui'],
        ['none', () => null],
    ];

    const STATES: QuadState[] = ['plain', 'hovered', 'focused'];

    for (const [name, subsystem] of SUBSYSTEMS_UNDER_TEST) {
        for (let depth = 0; depth <= 5; depth++) {
            for (const state of STATES) {
                it(`clears 3:1 for ${name} at depth ${depth}${state === 'plain' ? '' : ' ' + state}`, () => {
                    const { fill, ink, stroke } = inkAndFillFor({ depth }, state, subsystem);
                    expect(
                        contrastRatio(luminanceOf(fill), luminanceOf(ink)),
                    ).toBeGreaterThanOrEqual(3);
                    if (state === 'focused') expect(stroke).not.toBeNull();
                    if (stroke !== null) {
                        expect(
                            contrastRatio(luminanceOf(fill), luminanceOf(stroke)),
                        ).toBeGreaterThanOrEqual(3);
                    }
                });
            }
        }
    }

    for (let depth = 0; depth <= 5; depth++) {
        for (const state of STATES) {
            it(`clears 3:1 for a collapsed run at depth ${depth}${state === 'plain' ? '' : ' ' + state}`, () => {
                const { fill, ink, stroke } = inkAndFillFor(
                    { depth, count: 9, sectionId: -1 },
                    state,
                    () => null,
                );
                expect(contrastRatio(luminanceOf(fill), luminanceOf(ink))).toBeGreaterThanOrEqual(
                    3,
                );
                if (state === 'focused') expect(stroke).not.toBeNull();
                if (stroke !== null) {
                    expect(
                        contrastRatio(luminanceOf(fill), luminanceOf(stroke)),
                    ).toBeGreaterThanOrEqual(3);
                }
            });
        }
    }
});

describe('readTheme', () => {
    it('reads the subsystem and ink tokens off the element', () => {
        const el = document.createElement('div');
        el.style.setProperty('--sub-tick', '#111111');
        el.style.setProperty('--sub-ai', '#222222');
        el.style.setProperty('--sub-render', '#333333');
        el.style.setProperty('--sub-ui', '#444444');
        el.style.setProperty('--sub-none', '#555555');
        el.style.setProperty('--bg-surface', '#666666');
        el.style.setProperty('--border', '#777777');
        el.style.setProperty('--text', '#888888');
        el.style.setProperty('--bg-void', '#999999');
        el.style.setProperty('--font-mono', 'Test Mono');
        document.body.appendChild(el);

        const theme = readTheme(el);
        expect(theme).toEqual({
            background: '#666666',
            collapsed: '#777777',
            inkLight: '#888888',
            inkDark: '#999999',
            hue: { tick: '#111111', ai: '#222222', render: '#333333', ui: '#444444' },
            hueNone: '#555555',
            font: '500 11px Test Mono',
        });
        el.remove();
    });

    it('falls back to grey for every subsystem hue when tokens are absent', () => {
        // no per-subsystem fallback hex exists, so an unstyled element collapses every hue to the same grey.
        const el = document.createElement('div');
        document.body.appendChild(el);

        const theme = readTheme(el);
        expect(theme).toEqual({
            background: '#131925',
            collapsed: '#28344a',
            inkLight: '#d4dded',
            inkDark: '#080a0f',
            hue: { tick: '#5c6b85', ai: '#5c6b85', render: '#5c6b85', ui: '#5c6b85' },
            hueNone: '#5c6b85',
            font: '500 11px monospace',
        });
        el.remove();
    });
});
