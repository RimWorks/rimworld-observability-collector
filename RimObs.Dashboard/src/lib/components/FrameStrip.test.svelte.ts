import { readFileSync } from 'node:fs';
import { describe, it, expect, beforeAll, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import { tick } from 'svelte';
import FrameStrip from './FrameStrip.svelte';
import { drawStrip } from '../stripDraw';
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

function stubRect(el: HTMLElement, width = 200, height = 100, left = 0): void {
    const rect = () =>
        ({
            left,
            top: 0,
            right: left + width,
            bottom: height,
            width,
            height,
            x: left,
            y: 0,
            toJSON() {},
        }) as DOMRect;
    el.getBoundingClientRect = rect;
    // the component measures the wrapper, not the canvas, and caches what it read
    if (el.parentElement) el.parentElement.getBoundingClientRect = rect;
    document.dispatchEvent(new Event('scroll'));
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

// the strip streams at 30/s, so a getBoundingClientRect per pointer event forces layout on
// every one of them. the rect is cached and refreshed on resize and on an ancestor scroll.
describe('FrameStrip layout reads', () => {
    it('measures no rect while the pointer moves over it', async () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: FULL });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);
        canvas.setPointerCapture = () => {};
        const host = canvas.parentElement as HTMLElement;
        const spy = vi.spyOn(host, 'getBoundingClientRect');
        const canvasSpy = vi.spyOn(canvas, 'getBoundingClientRect');

        await fireEvent.mouseMove(canvas, { clientX: 100, clientY: 10 });
        canvas.dispatchEvent(
            new MouseEvent('pointerdown', { button: 0, clientX: 40, clientY: 10, bubbles: true }),
        );
        canvas.dispatchEvent(
            new MouseEvent('pointermove', { clientX: 120, clientY: 10, bubbles: true }),
        );
        canvas.dispatchEvent(
            new MouseEvent('pointerup', { clientX: 120, clientY: 10, bubbles: true }),
        );
        await fireEvent.click(canvas, { clientX: 100, clientY: 10 });

        expect(spy).not.toHaveBeenCalled();
        expect(canvasSpy).not.toHaveBeenCalled();
    });

    it('picks up a scrolled-away left edge on the next pointer move', async () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: FULL });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);
        await fireEvent.mouseMove(canvas, { clientX: 100, clientY: 10 });
        expect(screen.getByTestId('strip-tooltip').textContent).toContain('#5');

        // the page scrolled the strip 100px right; same bar now sits at clientX 200
        stubRect(canvas, 200, 100, 100);

        await fireEvent.mouseMove(canvas, { clientX: 200, clientY: 10 });

        expect(screen.getByTestId('strip-tooltip').textContent).toContain('#5');
    });

    // the paint deliberately draws no hover mark, so a hover that repaints is 2000 identical
    // bars of wasted canvas work per mousemove.
    it('repaints nothing when the pointer moves', async () => {
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: FULL });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);
        await tick();
        vi.mocked(drawStrip).mockClear();

        await fireEvent.mouseMove(canvas, { clientX: 100, clientY: 10 });
        await tick();
        await fireEvent.mouseMove(canvas, { clientX: 120, clientY: 10 });
        await tick();

        expect(screen.getByTestId('strip-tooltip')).not.toBeNull();
        expect(drawStrip).not.toHaveBeenCalled();
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

describe('FrameStrip selection', () => {
    it('reports the clicked frame ordinal', async () => {
        const onSelect = vi.fn();
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: FULL, onSelect });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);

        await fireEvent.click(canvas, { clientX: 100, clientY: 10 });

        expect(onSelect).toHaveBeenCalledWith(5);
    });

    it('reports a dragged ordinal range and suppresses the trailing click', async () => {
        const onSelect = vi.fn();
        const onSelectRange = vi.fn();
        render(FrameStrip, {
            ordinals: ORDINALS,
            durationsUs: DURATIONS,
            slots: FULL,
            onSelect,
            onSelectRange,
        });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);
        canvas.setPointerCapture = () => {};

        canvas.dispatchEvent(
            new MouseEvent('pointerdown', { button: 0, clientX: 40, clientY: 10, bubbles: true }),
        );
        canvas.dispatchEvent(
            new MouseEvent('pointermove', { clientX: 120, clientY: 10, bubbles: true }),
        );
        canvas.dispatchEvent(
            new MouseEvent('pointerup', { clientX: 120, clientY: 10, bubbles: true }),
        );
        await fireEvent.click(canvas, { clientX: 120, clientY: 10 });

        expect(onSelectRange).toHaveBeenCalledTimes(1);
        const [from, to] = onSelectRange.mock.calls[0];
        expect(from).toBeLessThan(to);
        expect(onSelect).not.toHaveBeenCalled();
    });

    it('a plain click still selects one frame with the range handler wired', async () => {
        const onSelect = vi.fn();
        const onSelectRange = vi.fn();
        render(FrameStrip, {
            ordinals: ORDINALS,
            durationsUs: DURATIONS,
            slots: FULL,
            onSelect,
            onSelectRange,
        });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);
        canvas.setPointerCapture = () => {};

        canvas.dispatchEvent(
            new MouseEvent('pointerdown', { button: 0, clientX: 100, clientY: 10, bubbles: true }),
        );
        canvas.dispatchEvent(
            new MouseEvent('pointerup', { clientX: 100, clientY: 10, bubbles: true }),
        );
        await fireEvent.click(canvas, { clientX: 100, clientY: 10 });

        expect(onSelectRange).not.toHaveBeenCalled();
        expect(onSelect).toHaveBeenCalledWith(5);
    });

    it('ignores a click past the newest frame of a part-filled ring', async () => {
        const onSelect = vi.fn();
        render(FrameStrip, { ordinals: ORDINALS, durationsUs: DURATIONS, slots: 200, onSelect });
        const canvas = document.querySelector('canvas') as HTMLCanvasElement;
        stubRect(canvas, 200, 100);

        await fireEvent.click(canvas, { clientX: 150, clientY: 10 });

        expect(onSelect).not.toHaveBeenCalled();
    });
});

// a committed drag selection must outlive the drag: the overlay stays until the caller
// clears the range, so the user can see what the paused flame is scoped to.
describe('FrameStrip committed range', () => {
    it('keeps the range highlighted without an active drag', () => {
        render(FrameStrip, {
            ordinals: ORDINALS,
            durationsUs: DURATIONS,
            slots: FULL,
            selectedRange: { from: 3, to: 7 },
        });
        expect(screen.getByTestId('strip-range')).toBeTruthy();
    });

    it('shows nothing when the range has scrolled out of the strip', () => {
        render(FrameStrip, {
            ordinals: ORDINALS,
            durationsUs: DURATIONS,
            slots: FULL,
            selectedRange: { from: 100, to: 110 },
        });
        expect(screen.queryByTestId('strip-range')).toBeNull();
    });
});

// clicking a bar picks a frame; before this the keyboard had no way to do the same thing,
// while the flame timeline right below it had a full arrow model.
describe('FrameStrip keyboard selection', () => {
    function strip(selectedOrdinal: number | null, onSelect = vi.fn()) {
        render(FrameStrip, {
            ordinals: ORDINALS,
            durationsUs: DURATIONS,
            slots: FULL,
            selectedOrdinal,
            onSelect,
        });
        return { canvas: document.querySelector('canvas') as HTMLCanvasElement, onSelect };
    }

    it('is reachable by tab and announces itself as a slider', () => {
        const { canvas } = strip(5);
        expect(canvas.getAttribute('tabindex')).toBe('0');
        expect(canvas.getAttribute('role')).toBe('slider');
        expect(canvas.getAttribute('aria-valuenow')).toBe('5');
        expect(canvas.getAttribute('aria-valuemin')).toBe('0');
        expect(canvas.getAttribute('aria-valuemax')).toBe('9');
    });

    it('steps one frame per arrow key', async () => {
        const { canvas, onSelect } = strip(5);
        await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
        expect(onSelect).toHaveBeenCalledWith(6);
        await fireEvent.keyDown(canvas, { key: 'ArrowLeft' });
        expect(onSelect).toHaveBeenLastCalledWith(4);
    });

    it('jumps ten frames per page key and clamps at the ends', async () => {
        const { canvas, onSelect } = strip(5);
        await fireEvent.keyDown(canvas, { key: 'PageDown' });
        expect(onSelect).toHaveBeenCalledWith(9);
        await fireEvent.keyDown(canvas, { key: 'PageUp' });
        expect(onSelect).toHaveBeenLastCalledWith(0);
    });

    // the page transport owns Home, so the strip must not select on it or preventDefault.
    it('leaves Home to the transport', async () => {
        const { canvas, onSelect } = strip(5);
        expect(await fireEvent.keyDown(canvas, { key: 'Home' })).toBe(true);
        expect(onSelect).not.toHaveBeenCalled();
    });

    it('jumps to the newest frame on End', async () => {
        const { canvas, onSelect } = strip(5);
        await fireEvent.keyDown(canvas, { key: 'End' });
        expect(onSelect).toHaveBeenCalledWith(9);
    });

    it('ignores keys it does not handle', async () => {
        const { canvas, onSelect } = strip(5);
        await fireEvent.keyDown(canvas, { key: 'a' });
        expect(onSelect).not.toHaveBeenCalled();
    });

    it('starts at the newest frame when nothing is selected yet', async () => {
        const { canvas, onSelect } = strip(null);
        await fireEvent.keyDown(canvas, { key: 'ArrowLeft' });
        expect(onSelect).toHaveBeenCalledWith(9);
    });
});
