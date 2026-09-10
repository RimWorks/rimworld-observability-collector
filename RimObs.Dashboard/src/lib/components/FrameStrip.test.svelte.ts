import { readFileSync } from 'node:fs';
import { describe, it, expect, beforeAll, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import FrameStrip from './FrameStrip.svelte';
import { FRAME_BUDGET_US } from '../frameCost';

// jsdom's canvas has no getContext, so without this mock the draw path never runs.
vi.mock('../stripDraw', async (importOriginal) => {
    const actual = await importOriginal<typeof import('../stripDraw')>();
    return { ...actual, drawStrip: vi.fn() };
});

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

const realGetContext = HTMLCanvasElement.prototype.getContext;

beforeEach(() => {
    HTMLCanvasElement.prototype.getContext = (() => ({}) as unknown) as never;
});

afterEach(() => {
    HTMLCanvasElement.prototype.getContext = realGetContext;
});

function stubRect(el: HTMLElement, width = 200, height = 100): void {
    el.getBoundingClientRect = () =>
        ({
            left: 0,
            top: 0,
            right: width,
            bottom: height,
            width,
            height,
            x: 0,
            y: 0,
            toJSON() {},
        }) as DOMRect;
}

const ORDINALS = Array.from({ length: 10 }, (_, i) => i);
const DURATIONS = ORDINALS.map(() => 1000);
/** a ring exactly as big as the frames held, so the strip is full and spans the panel */
const FULL = ORDINALS.length;

describe('FrameStrip tooltip', () => {
    it('shows nothing until the cursor enters the strip', () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS });
        expect(screen.queryByTestId('strip-tooltip')).toBeNull();
    });

    it('shows the hovered frame ordinal and duration on mousemove', async () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: FULL });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);
        await fireEvent.mouseMove(canvas, { clientX: 100, clientY: 10 });
        const tip = screen.getByTestId('strip-tooltip');
        expect(tip.textContent).toContain('#5');
    });

    it('hides again once the cursor leaves the strip', async () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: FULL });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);
        await fireEvent.mouseMove(canvas, { clientX: 100, clientY: 10 });
        expect(screen.queryByTestId('strip-tooltip')).not.toBeNull();
        await fireEvent.mouseLeave(canvas);
        expect(screen.queryByTestId('strip-tooltip')).toBeNull();
    });

    // slots come from the ring's capacity, so a part-filled ring leaves empty strip to the
    // right of the newest frame. hovering it selects nothing rather than the last bar.
    it('shows nothing when hovering past the newest frame of a part-filled ring', async () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: 200 });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);

        await fireEvent.mouseMove(canvas, { clientX: 150, clientY: 10 });

        expect(screen.queryByTestId('strip-tooltip')).toBeNull();
    });

    // jsdom reports 0 width for every element, so this exercises clampTooltipX's
    // wider-than-container fallback, not a real measured clamp.
    it('keeps the tooltip element inside the strip when hovering the left edge', async () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: FULL });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);
        await fireEvent.mouseMove(canvas, { clientX: 0, clientY: 10 });
        const tip = screen.getByTestId('strip-tooltip');
        const left = Number.parseFloat(tip.style.left);
        expect(left).toBeGreaterThanOrEqual(0);
    });
});

describe('FrameStrip budget legend', () => {
    it('derives the shown budget from FRAME_BUDGET_US, not a hardcoded 16.7ms', () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS });
        const expectedMs = (FRAME_BUDGET_US / 1000).toFixed(1);
        expect(screen.getByText(new RegExp(`\\(${expectedMs} ms\\)`))).toBeTruthy();
    });
});

// the gridlines are painted on the canvas under the bars now, so the DOM axis is labels only.
// a border here would put a rule back on top of every bar, which is the thing that looked bad.
describe('FrameStrip axis', () => {
    const css = readFileSync('src/lib/components/FrameStrip.svelte', 'utf-8').split('<style>')[1];

    function rule(selector: string): string {
        return new RegExp(`\\${selector}\\s*\\{([^}]*)\\}`).exec(css)?.[1] ?? '';
    }

    it('draws no rule of its own in the label gutter', () => {
        expect(rule('.axis span')).not.toContain('border-top');
    });

    it('stays in the flow so it sits in the gutter, not over the canvas', () => {
        expect(rule('.axis')).toContain('position: relative');
    });
});
