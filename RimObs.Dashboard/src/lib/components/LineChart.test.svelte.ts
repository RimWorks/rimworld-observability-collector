import { describe, it, expect, vi, beforeAll } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import LineChart from './LineChart.svelte';

const built = vi.hoisted(() => ({ opts: null as unknown }));

// jsdom canvases have no 2d context, so uplot cannot build. the aria label is ours, not its.
vi.mock('uplot', () => ({
    default: class {
        constructor(opts: unknown) {
            built.opts = opts;
        }
        setData() {}
        setSize() {}
        destroy() {}
    },
}));
vi.mock('uplot/dist/uPlot.min.css', () => ({}));

beforeAll(() => {
    globalThis.ResizeObserver ??= class {
        observe() {
            // the stub exists so jsdom has a ResizeObserver, it never needs to react
        }
        unobserve() {
            // the stub exists so jsdom has a ResizeObserver, it never needs to react
        }
        disconnect() {
            // the stub exists so jsdom has a ResizeObserver, it never needs to react
        }
    } as unknown as typeof ResizeObserver;
});

const SERIES = [
    { label: 'mean', values: [3, 1, 5, 4], stroke: '--accent' },
    { label: 'p99', values: [10, 40, 20], stroke: '--warn' },
];

describe('LineChart accessibility', () => {
    it('exposes the host as an image with a label naming every series', () => {
        render(LineChart, { x: [0, 1, 2, 3], series: SERIES, format: (n: number) => `${n} ms` });

        const host = screen.getByRole('img');
        const label = host.getAttribute('aria-label') ?? '';
        expect(label).toContain('Line chart');
        for (const s of SERIES) expect(label).toContain(s.label);
    });

    it('reports the min, max and last value of each series', () => {
        render(LineChart, { x: [0, 1, 2, 3], series: SERIES, format: (n: number) => `${n} ms` });

        const label = screen.getByRole('img').getAttribute('aria-label') ?? '';
        expect(label).toContain('mean: lowest 1 ms, highest 5 ms, latest 4 ms');
        expect(label).toContain('p99: lowest 10 ms, highest 40 ms, latest 20 ms');
    });

    it('says so when a series carries no finite values', () => {
        render(LineChart, {
            x: [0, 1],
            series: [{ label: 'empty', values: [NaN, NaN], stroke: '--accent' }],
        });

        expect(screen.getByRole('img')).toHaveAttribute(
            'aria-label',
            'Line chart. empty: no data.',
        );
    });

    it('refreshes the label when the data changes', async () => {
        const { rerender } = render(LineChart, {
            x: [0, 1],
            series: [{ label: 'mean', values: [1, 2], stroke: '--accent' }],
        });
        expect(screen.getByRole('img').getAttribute('aria-label')).toContain('latest 2');

        await rerender({
            x: [0, 1],
            series: [{ label: 'mean', values: [1, 9], stroke: '--accent' }],
        });
        expect(screen.getByRole('img').getAttribute('aria-label')).toContain('latest 9');
    });
});

// the cursor hook runs per pointer move, so reading the plot's offsets there forces layout on
// every one. they only move on a resize, so they are read in ready and setSize instead.
describe('LineChart layout reads', () => {
    interface Hooks {
        ready: ((u: unknown) => void)[];
        setSize: ((u: unknown) => void)[];
        setCursor: ((u: unknown) => void)[];
    }

    it('reads no plot offsets while the cursor moves', () => {
        render(LineChart, { x: [0, 1, 2, 3], series: SERIES });
        const { hooks } = built.opts as { hooks: Hooks };

        let reads = 0;
        const count = (v: number) => () => {
            reads++;
            return v;
        };
        const over = {};
        for (const [key, value] of Object.entries({
            offsetLeft: 0,
            offsetTop: 0,
            offsetWidth: 600,
            offsetHeight: 240,
        })) {
            Object.defineProperty(over, key, { get: count(value) });
        }
        const u = {
            over,
            cursor: { idx: 1, left: 10, top: 10 },
            data: [
                [0, 1, 2, 3],
                [3, 1, 5, 4],
            ],
        };

        hooks.ready[0](u);
        expect(reads).toBe(4);

        hooks.setCursor[0](u);
        hooks.setCursor[0](u);
        expect(reads).toBe(4);

        hooks.setSize[0](u);
        expect(reads).toBe(8);
    });

    // the tooltip rebuild and its offsetWidth/Height reads only happen when the hovered
    // index changes; a same-index move is arithmetic only.
    it('rebuilds and measures the tooltip only when the index changes', () => {
        const { container } = render(LineChart, { x: [0, 1, 2, 3], series: SERIES });
        const { hooks } = built.opts as { hooks: Hooks };

        const tip = container.querySelector('.tt') as HTMLElement;
        let sizeReads = 0;
        for (const key of ['offsetWidth', 'offsetHeight']) {
            Object.defineProperty(tip, key, {
                get: () => {
                    sizeReads++;
                    return 40;
                },
            });
        }
        const u = {
            over: { offsetLeft: 0, offsetTop: 0, offsetWidth: 600, offsetHeight: 240 },
            cursor: { idx: 1, left: 10, top: 10 },
            data: [
                [0, 1, 2, 3],
                [3, 1, 5, 4],
            ],
        };

        hooks.ready[0](u);
        hooks.setCursor[0](u);
        expect(sizeReads).toBe(2);

        hooks.setCursor[0](u);
        hooks.setCursor[0](u);
        expect(sizeReads).toBe(2);

        u.cursor.idx = 2;
        hooks.setCursor[0](u);
        expect(sizeReads).toBe(4);
    });
});
