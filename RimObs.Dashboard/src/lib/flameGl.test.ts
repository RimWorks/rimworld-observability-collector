import { describe, it, expect } from 'vitest';
import {
    FLOATS_PER_INSTANCE,
    clipUniforms,
    packColor,
    packInstances,
    parseCssColor,
    quadRectPx,
} from './flameGl';
import { ROW_HEIGHT, readTheme, type DrawOptions } from './frameDraw';
import type { Quad } from './frameLayout';

function quad(over: Partial<Quad> = {}): Quad {
    return {
        depth: 0,
        startUs: 0,
        endUs: 100,
        totalUs: 100,
        sectionId: 1,
        count: 1,
        firstIndex: 0,
        ...over,
    };
}

function opts(over: Partial<DrawOptions> = {}): DrawOptions {
    return {
        view: { startUs: 0, endUs: 1000 },
        widthPx: 600,
        heightPx: 200,
        dpr: 1,
        theme: readTheme(document.body),
        label: () => 'x',
        subsystem: () => null,
        hoverIndex: -1,
        focusIndex: -1,
        ...over,
    };
}

function instanceAt(data: Float32Array, i: number): number[] {
    const at = i * FLOATS_PER_INSTANCE;
    return Array.from(data.slice(at, at + FLOATS_PER_INSTANCE - 1));
}

function colorAt(data: Float32Array, i: number): number[] {
    const bytes = new Uint8Array(data.buffer, (i * FLOATS_PER_INSTANCE + 5) * 4, 4);
    return Array.from(bytes);
}

describe('packColor', () => {
    it('lays rgba out as four bytes, alpha last', () => {
        expect(packColor(1, 2, 3, 1)).toBe(((255 << 24) | (3 << 16) | (2 << 8) | 1) >>> 0);
    });

    it('clamps and rounds out-of-range channels', () => {
        expect(packColor(-5, 300, 7.6, 2)).toBe(((255 << 24) | (8 << 16) | (255 << 8) | 0) >>> 0);
    });
});

describe('parseCssColor', () => {
    it('reads hex, rgb and rgba', () => {
        expect(parseCssColor('#0a141e')).toEqual([10, 20, 30, 1]);
        expect(parseCssColor('rgb(10, 20, 30)')).toEqual([10, 20, 30, 1]);
        expect(parseCssColor('rgba(10, 20, 30, 0.45)')).toEqual([10, 20, 30, 0.45]);
    });

    it('falls back to grey on garbage', () => {
        expect(parseCssColor('not a color')).toEqual([128, 128, 128, 1]);
    });
});

describe('clipUniforms', () => {
    it('turns a view span into pixels per microsecond', () => {
        expect(clipUniforms({ startUs: 100, endUs: 1100 }, 500)).toEqual({
            startUs: 100,
            pxPerUs: 0.5,
        });
    });

    it('reports zero scale for an empty span instead of infinity', () => {
        expect(clipUniforms({ startUs: 5, endUs: 5 }, 500).pxPerUs).toBe(0);
    });
});

describe('quadRectPx', () => {
    const u = clipUniforms({ startUs: 0, endUs: 1000 }, 500);

    it('maps a quad inside the view', () => {
        expect(quadRectPx(200, 400, u, 500)).toEqual({ x: 100, w: 100 });
    });

    it('clips a straddler to the canvas', () => {
        expect(quadRectPx(-500, 400, u, 500)).toEqual({ x: 0, w: 200 });
        expect(quadRectPx(800, 5000, u, 500)).toEqual({ x: 400, w: 100 });
    });

    it('keeps a sub-pixel quad visible at one pixel', () => {
        expect(quadRectPx(200, 200.5, u, 500)).toEqual({ x: 100, w: 1 });
    });

    it('culls a quad entirely past the right edge', () => {
        expect(quadRectPx(2000, 3000, u, 500)).toBeNull();
    });
});

describe('packInstances', () => {
    it('packs one instance per quad, in microseconds and rows', () => {
        const packed = packInstances([quad({ startUs: 10, endUs: 90, depth: 2 })], opts());
        expect(packed.count).toBe(1);
        expect(instanceAt(packed.data, 0)).toEqual([10, 90, 2 * ROW_HEIGHT, ROW_HEIGHT - 1, 0]);
        expect(colorAt(packed.data, 0)[3]).toBe(255);
    });

    it('packs nothing for an empty quad set', () => {
        const packed = packInstances([], opts());
        expect(packed.count).toBe(0);
        // data is a reused grow-only scratch, so only count carries meaning.
    });

    it('drops a quad whose row is below the canvas', () => {
        const packed = packInstances([quad({ depth: 50 })], opts({ heightPx: 40 }));
        expect(packed.count).toBe(0);
    });

    it('dims a non-match while a search is active', () => {
        const quads = [quad({ sectionId: 1 }), quad({ sectionId: 2, depth: 1 })];
        const packed = packInstances(quads, opts({ matchSectionIds: new Set([1]) }));
        expect(colorAt(packed.data, 0)[3]).toBe(255);
        expect(colorAt(packed.data, 1)[3]).toBe(Math.round(0.32 * 255));
    });

    it('puts lane bands and gaps ahead of the quads, in pixel space', () => {
        const packed = packInstances(
            [quad()],
            opts({
                laneBands: [{ rows: 2 }, { rows: 3 }],
                gaps: [{ startUs: 10, endUs: 20, missing: 2 }],
            }),
        );
        // zebra fill for band 1, its boundary line, one gap band, then the quad
        expect(packed.count).toBe(4);
        expect(instanceAt(packed.data, 0)).toEqual([0, 600, 2 * ROW_HEIGHT, 3 * ROW_HEIGHT, 1]);
        expect(instanceAt(packed.data, 1)).toEqual([0, 600, 2 * ROW_HEIGHT, 1, 1]);
        expect(instanceAt(packed.data, 2)).toEqual([10, 20, 0, 200, 0]);
        expect(instanceAt(packed.data, 3)[4]).toBe(0);
    });

    it('ignores a single lane band and a gap that misses no frames', () => {
        const packed = packInstances(
            [quad()],
            opts({ laneBands: [{ rows: 4 }], gaps: [{ startUs: 10, endUs: 20, missing: 0 }] }),
        );
        expect(packed.count).toBe(1);
    });
});
