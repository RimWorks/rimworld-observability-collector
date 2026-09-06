import { describe, it, expect, beforeAll, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import FrameTimeline from './FrameTimeline.svelte';
import { layoutFrame, quadIndexForNode } from '../frameLayout';
import { fitView } from '../frameView';
import { buildFrameTree, type FrameData } from '../frameTree';
import { drawTimeline } from '../frameDraw';

// jsdom's canvas has no getContext, so without this mock the draw path never runs and
// nothing drawTimeline is handed can be asserted on.
vi.mock('../frameDraw', async (importOriginal) => {
    const actual = await importOriginal<typeof import('../frameDraw')>();
    return { ...actual, drawTimeline: vi.fn() };
});

// jsdom has no canvas and no ResizeObserver, and the component must survive both.
beforeAll(() => {
    globalThis.ResizeObserver ??= class {
        observe() {}
        unobserve() {}
        disconnect() {}
    } as unknown as typeof ResizeObserver;
});

const realGetContext = HTMLCanvasElement.prototype.getContext;

beforeEach(() => {
    HTMLCanvasElement.prototype.getContext = (() => ({}) as unknown) as never;
    vi.mocked(drawTimeline).mockClear();
});

afterEach(() => {
    HTMLCanvasElement.prototype.getContext = realGetContext;
});

function waitForFrame(): Promise<void> {
    return new Promise((resolve) => requestAnimationFrame(() => resolve()));
}

function stubRect(el: HTMLElement, width = 600, height = 100): void {
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

// jsdom has no PointerEvent constructor, so build a plain Event with the fields the
// handler reads; dispatch only cares about event.type, not the class.
function firePointerMove(el: Element, clientX: number, clientY: number): Promise<boolean> {
    const event = new Event('pointermove', { bubbles: true, cancelable: true });
    Object.assign(event, { clientX, clientY, pointerId: 1 });
    return fireEvent(el, event);
}

const NAMES = new Map([
    [10, { name: 'Verse.TickManager.DoSingleTick', subsystem: 'tick' }],
    [30, { name: 'Verse.TickList.Tick', subsystem: 'tick' }],
]);

const FRAME: FrameData = {
    capture_ordinal: 1234,
    start_us: 1000,
    end_us: 17200,
    duration_us: 16200,
    node_count: 3,
    nodes: {
        section_ids: [30, 30, 10],
        parent_ids: [10, 10, -1],
        node_ids: [2, 3, 1],
        parent_node_ids: [1, 1, -1],
        start_us: [1100, 1600, 1000],
        dur_us: [400, 900, 16200],
    },
};

// 4 narrow depth-1 children collapse into run quad index 1, but their tree indices are 1-4.
// picking the LAST one (tree index 4) rules out the coincidence tree index 1 would allow.
const NARROW_FRAME: FrameData = {
    capture_ordinal: 1,
    start_us: 0,
    end_us: 1000,
    duration_us: 1000,
    node_count: 5,
    nodes: {
        section_ids: [10, 30, 30, 30, 30],
        parent_ids: [-1, 10, 10, 10, 10],
        node_ids: [1, 2, 3, 4, 5],
        parent_node_ids: [-1, 1, 1, 1, 1],
        start_us: [0, 10, 11, 12, 13],
        dur_us: [1000, 1, 1, 1, 1],
    },
};

// root plus 5 evenly spaced children, none narrow enough to merge. zooming into the middle
// child drops the others one at a time as the view narrows, so quad counts differ mid-zoom.
const STAGGERED_FRAME: FrameData = {
    capture_ordinal: 1,
    start_us: 0,
    end_us: 10000,
    duration_us: 10000,
    node_count: 6,
    nodes: {
        section_ids: [10, 30, 30, 30, 30, 30],
        parent_ids: [-1, 10, 10, 10, 10, 10],
        node_ids: [1, 2, 3, 4, 5, 6],
        parent_node_ids: [-1, 1, 1, 1, 1, 1],
        start_us: [0, 0, 2000, 4000, 6000, 8000],
        dur_us: [10000, 1000, 1000, 1000, 1000, 1000],
    },
};

describe('FrameTimeline', () => {
    it('shows an empty state before the first frame', () => {
        render(FrameTimeline, { frame: null, names: NAMES });
        expect(screen.queryByRole('application')).toBeNull();
        expect(screen.getByTestId('frame-empty')).toBeInTheDocument();
    });

    it('exposes the canvas as a focusable application widget', () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        expect(canvas.tagName).toBe('CANVAS');
        expect(canvas).toHaveAttribute('tabindex', '0');
        expect(canvas).toHaveAttribute('aria-roledescription', 'frame timeline');
    });

    // the label used to interpolate ordinal/duration/count, re-announcing at 4Hz.
    it('has a stable aria-label that does not change with the frame, while focus still announces', async () => {
        const { rerender } = render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        const label = canvas.getAttribute('aria-label') ?? '';
        expect(label).toBe('current frame');
        expect(screen.getByRole('status')).toHaveTextContent('');

        await rerender({
            frame: { ...FRAME, capture_ordinal: 9999, duration_us: 33000, node_count: 7 },
            names: NAMES,
        });
        expect(canvas.getAttribute('aria-label')).toBe(label);

        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        expect(screen.getByRole('status')).toHaveTextContent('Verse.TickList.Tick');
    });

    it('reports the orphan count back to its parent', async () => {
        const orphaned: FrameData = {
            ...FRAME,
            nodes: { ...FRAME.nodes, parent_node_ids: [1, 99, -1] },
        };
        const props = $state({ frame: orphaned, names: NAMES, orphanCount: 0 });
        render(FrameTimeline, props);
        expect(props.orphanCount).toBe(1);
    });

    it('announces the focused node in a polite live region', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        expect(screen.getByRole('status')).toHaveTextContent('Verse.TickList.Tick');
    });

    // the two siblings share a name and a parent, so only their durations tell them apart.
    // that is the whole point: the array cannot address them, the intervals can.
    it('steps between the repeated siblings with the arrow keys', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        expect(screen.getByRole('status')).toHaveTextContent('400');
        await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
        expect(screen.getByRole('status')).toHaveTextContent('900');
    });

    // regression: hitTest is half-open, so a dur_us: 0 node (routine at microsecond
    // resolution) never matches on atUs, and focus silently jumped back to the root.
    it('steps onto a zero-duration node instead of losing focus', async () => {
        const zeroDur: FrameData = {
            capture_ordinal: 1,
            start_us: 0,
            end_us: 1000,
            duration_us: 1000,
            node_count: 4,
            nodes: {
                section_ids: [10, 40, 50, 30],
                parent_ids: [-1, 10, 10, 10],
                node_ids: [1, 2, 3, 4],
                parent_node_ids: [-1, 1, 1, 1],
                start_us: [0, 100, 200, 300],
                dur_us: [1000, 0, 0, 100],
            },
        };
        render(FrameTimeline, { frame: zeroDur, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        expect(screen.getByRole('status')).toHaveTextContent('section 40');
        // two zero-duration siblings share a depth, so the fallback has to match on start
        // time as well; a depth-only match would step onto section 50 twice.
        await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
        expect(screen.getByRole('status')).toHaveTextContent('section 50');
        await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
        expect(screen.getByRole('status')).toHaveTextContent('Verse.TickList.Tick');
    });

    it('keeps the zoom when a later frame arrives', async () => {
        const { rerender } = render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: '+' });
        const zoomed = screen.getByTestId('frame-range').textContent;
        await rerender({ frame: { ...FRAME, capture_ordinal: 1235 }, names: NAMES });
        expect(screen.getByTestId('frame-range').textContent).toBe(zoomed);
    });

    it('restores the full frame on Escape', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        const fitted = screen.getByTestId('frame-range').textContent;
        await fireEvent.keyDown(canvas, { key: '+' });
        expect(screen.getByTestId('frame-range').textContent).not.toBe(fitted);
        await fireEvent.keyDown(canvas, { key: 'Escape' });
        expect(screen.getByTestId('frame-range').textContent).toBe(fitted);
    });

    it('refits when a new frame arrives and the user has not zoomed', async () => {
        const { rerender } = render(FrameTimeline, { frame: FRAME, names: NAMES });
        await rerender({
            frame: { ...FRAME, capture_ordinal: 1235, duration_us: 8000 },
            names: NAMES,
        });
        expect(screen.getByTestId('frame-range').textContent).toContain('8');
    });

    // unit-only: exercises quadIndexForNode directly, not the component, so it cannot catch
    // the wiring trap. the real guard is the drawTimeline-mock tests below.
    it('resolves a focused node inside a collapsed run to the run quad, not -1', () => {
        const narrow: FrameData = {
            capture_ordinal: 1,
            start_us: 0,
            end_us: 1000,
            duration_us: 1000,
            node_count: 5,
            nodes: {
                section_ids: [10, 30, 30, 30, 30],
                parent_ids: [-1, 10, 10, 10, 10],
                node_ids: [1, 2, 3, 4, 5],
                parent_node_ids: [-1, 1, 1, 1, 1],
                start_us: [0, 10, 11, 12, 13],
                dur_us: [1000, 1, 1, 1, 1],
            },
        };
        const { nodes } = buildFrameTree(narrow);
        const view = fitView(narrow.duration_us);
        const quads = layoutFrame(nodes, {
            viewStartUs: view.startUs,
            viewEndUs: view.endUs,
            widthPx: 600,
            maxDepth: 32,
            minWidthPx: 2,
            minVisibleDurationUs: (view.endUs - view.startUs) / 2000,
        });
        const run = quads.find((q) => q.depth === 1);
        expect(run).toBeDefined();
        expect(run!.count).toBeGreaterThan(1);

        // node index 3 (start 12) sits inside the run but is not its first member.
        const laterMember = nodes.find((n) => n.depth === 1 && n.startUs === 12)!;
        const resolved = quadIndexForNode(quads, laterMember);
        expect(resolved).not.toBe(-1);
        expect(quads[resolved]).toBe(run);
    });

    it('clamps the view so it never extends past the frame after repeated zoom-out', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        for (let i = 0; i < 20; i++) {
            await fireEvent.keyDown(canvas, { key: '-' });
        }
        const text = screen.getByTestId('frame-range').textContent ?? '';
        expect(text).toContain('16.2');
    });

    // focusIndex must be the QUAD index handed to drawTimeline, not the tree index.
    it('passes the run quad index, not the tree index, as focusIndex', async () => {
        render(FrameTimeline, { frame: NARROW_FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
        await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
        await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
        await waitForFrame();
        const [, quads, opts] = vi.mocked(drawTimeline).mock.calls.at(-1)!;
        expect(opts.focusIndex).toBe(quads.findIndex((q) => q.count > 1));
        expect(opts.focusIndex).not.toBe(4);
    });

    // same trap, via hoverIndex.
    it('passes the run quad index, not the tree index, as hoverIndex', async () => {
        render(FrameTimeline, { frame: NARROW_FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        // 300, not the component's own widthPx of 600: a mapping that reads widthPx
        // instead of the measured rect lands 6.7us short and misses the run entirely.
        stubRect(canvas, 300);
        await firePointerMove(canvas, 4, 20); // depth 1, atUs ~13.3: the run's last member
        await waitForFrame();
        const [, quads, opts] = vi.mocked(drawTimeline).mock.calls.at(-1)!;
        expect(opts.hoverIndex).toBe(quads.findIndex((q) => q.count > 1));
        expect(opts.hoverIndex).not.toBe(4);
    });

    // rerender re-runs the dirty effect whichever props move, so this pins the label
    // callback reading the current names map, not that names alone schedules a repaint.
    it('labels quads from the latest names map', async () => {
        const { rerender } = render(FrameTimeline, { frame: FRAME, names: NAMES });
        await waitForFrame();
        vi.mocked(drawTimeline).mockClear();
        const renamed = new Map(NAMES);
        renamed.set(30, { name: 'Verse.TickList.Renamed', subsystem: 'tick' });
        await rerender({ names: renamed });
        await waitForFrame();
        const [, quads, opts] = vi.mocked(drawTimeline).mock.calls.at(-1)!;
        const quad = quads.find((q) => q.sectionId === 30)!;
        expect(opts.label(quad)).toBe('Verse.TickList.Renamed');
    });

    // the backing store must scale by devicePixelRatio, and DrawOptions.dpr with it.
    it('sizes the backing canvas by devicePixelRatio', async () => {
        const original = window.devicePixelRatio;
        Object.defineProperty(window, 'devicePixelRatio', { value: 2, configurable: true });
        try {
            render(FrameTimeline, { frame: FRAME, names: NAMES });
            const canvas = screen.getByRole('application') as HTMLCanvasElement;
            expect(canvas.width).toBe(1200);
            expect(canvas.style.width).toBe('600px');
            await waitForFrame();
            const [, , opts] = vi.mocked(drawTimeline).mock.calls.at(-1)!;
            expect(opts.dpr).toBe(2);
        } finally {
            Object.defineProperty(window, 'devicePixelRatio', {
                value: original,
                configurable: true,
            });
        }
    });

    // nothing changed between ticks, so the dirty gate must skip the redraw.
    it('stops redrawing once nothing is dirty', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        await waitForFrame();
        vi.mocked(drawTimeline).mockClear();
        await waitForFrame();
        await waitForFrame();
        expect(drawTimeline).not.toHaveBeenCalled();
    });

    // the null-context guard must stop drawTimeline from ever being called, not
    // just stop render() from throwing (draw() runs inside a rAF callback, after render).
    it('does not throw when getContext returns null, and never calls drawTimeline', async () => {
        const original = HTMLCanvasElement.prototype.getContext;
        HTMLCanvasElement.prototype.getContext = (() => null) as never;
        try {
            expect(() => render(FrameTimeline, { frame: FRAME, names: NAMES })).not.toThrow();
            await waitForFrame();
            expect(drawTimeline).not.toHaveBeenCalled();
        } finally {
            HTMLCanvasElement.prototype.getContext = original;
        }
    });

    it('renders without throwing for a frame with no nodes', () => {
        const empty: FrameData = {
            capture_ordinal: 1,
            start_us: 0,
            end_us: 0,
            duration_us: 0,
            node_count: 0,
            nodes: {
                section_ids: [],
                parent_ids: [],
                node_ids: [],
                parent_node_ids: [],
                start_us: [],
                dur_us: [],
            },
        };
        expect(() => render(FrameTimeline, { frame: empty, names: NAMES })).not.toThrow();
    });

    it('changes the live region text between two different focused nodes', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        const first = screen.getByRole('status').textContent;
        await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
        const second = screen.getByRole('status').textContent;
        expect(second).not.toBe(first);
    });

    it('zooms to the focused node on Enter', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        const fitted = screen.getByTestId('frame-range').textContent;
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        await fireEvent.keyDown(canvas, { key: 'Enter' });
        expect(screen.getByTestId('frame-range').textContent).not.toBe(fitted);
    });

    it('refits on Home as well as Escape', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        const fitted = screen.getByTestId('frame-range').textContent;
        await fireEvent.keyDown(canvas, { key: '+' });
        await fireEvent.keyDown(canvas, { key: 'Home' });
        expect(screen.getByTestId('frame-range').textContent).toBe(fitted);
    });

    // quads must follow the animated view while a zoom is in flight, or the canvas pops.
    // real rAF drifts from performance.now() under jsdom, so drive the loop with a fake clock.
    it('lays out the interpolated view during a zoom, not the target', async () => {
        let now = 0;
        let pendingTick: FrameRequestCallback | null = null;
        const nowSpy = vi.spyOn(performance, 'now').mockImplementation(() => now);
        const rafSpy = vi
            .spyOn(globalThis, 'requestAnimationFrame')
            .mockImplementation((cb: FrameRequestCallback) => {
                pendingTick = cb;
                return 1;
            });
        const cancelSpy = vi.spyOn(globalThis, 'cancelAnimationFrame').mockImplementation(() => {});
        const runTick = (): void => {
            const cb = pendingTick!;
            pendingTick = null;
            cb(now);
        };

        try {
            render(FrameTimeline, { frame: STAGGERED_FRAME, names: NAMES });
            const canvas = screen.getByRole('application');
            runTick(); // consume the initial mount draw

            await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
            await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
            await fireEvent.keyDown(canvas, { key: 'ArrowRight' });
            runTick();
            vi.mocked(drawTimeline).mockClear();

            await fireEvent.keyDown(canvas, { key: 'Enter' }); // animStart = now
            now += 16; // one frame into the 180ms zoom
            runTick();
            const midQuads = vi.mocked(drawTimeline).mock.calls[0][1].length;

            now += 300; // past the animation
            runTick();
            const finalQuads = vi.mocked(drawTimeline).mock.calls[1][1].length;

            expect(midQuads).toBeGreaterThan(finalQuads);
        } finally {
            nowSpy.mockRestore();
            rafSpy.mockRestore();
            cancelSpy.mockRestore();
        }
    });

    // height comes from the tree's own depth, not from which rows the current zoom
    // happens to fold, or the canvas (and the page below it) resizes on every zoom step.
    it('keeps the canvas height stable when zooming reveals a folded node', async () => {
        const deep: FrameData = {
            capture_ordinal: 1,
            start_us: 0,
            end_us: 20000,
            duration_us: 20000,
            node_count: 3,
            nodes: {
                section_ids: [10, 20, 30],
                parent_ids: [-1, 10, 20],
                node_ids: [1, 2, 3],
                parent_node_ids: [-1, 1, 2],
                start_us: [0, 9000, 9500],
                dur_us: [20000, 11000, 5],
            },
        };
        render(FrameTimeline, { frame: deep, names: NAMES });
        const canvas = screen.getByRole('application');
        const before = canvas.getAttribute('height');
        for (let i = 0; i < 7; i++) {
            await fireEvent.keyDown(canvas, { key: '+' });
        }
        expect(canvas.getAttribute('height')).toBe(before);
    });

    // zoomToNode must clamp its target the same as every other view change does.
    it('clamps the target view when zooming into a node that overruns the frame', async () => {
        const overrun: FrameData = {
            capture_ordinal: 1,
            start_us: 0,
            end_us: 1000,
            duration_us: 1000,
            node_count: 2,
            nodes: {
                section_ids: [10, 20],
                parent_ids: [-1, 10],
                node_ids: [1, 2],
                parent_node_ids: [-1, 1],
                start_us: [0, 0],
                dur_us: [1000, 1500],
            },
        };
        render(FrameTimeline, { frame: overrun, names: NAMES });
        const canvas = screen.getByRole('application');
        const fitted = screen.getByTestId('frame-range').textContent;
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        await fireEvent.keyDown(canvas, { key: 'Enter' });
        expect(screen.getByTestId('frame-range').textContent).toBe(fitted);
    });

    // the "keeps the zoom" test reuses the same duration_us, so a clamp-on-
    // arrival mutant is a no-op there. a shorter frame is the case the plan actually cares about.
    it('does not clamp the view when a shorter frame arrives while zoomed into the tail', async () => {
        const { rerender } = render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        await fireEvent.keyDown(canvas, { key: 'Enter' });
        const zoomed = screen.getByTestId('frame-range').textContent;
        await rerender({
            frame: { ...FRAME, capture_ordinal: 1235, duration_us: 300 },
            names: NAMES,
        });
        expect(screen.getByTestId('frame-range').textContent).toBe(zoomed);
    });

    // vitest-setup.ts stubs matchMedia to matches: false, so this overrides it locally
    // to prove the reduced-motion branch actually skips the lerp.
    it('does not interpolate the zoom when the user prefers reduced motion', async () => {
        const original = globalThis.matchMedia;
        globalThis.matchMedia = ((query: string) =>
            ({ matches: true, media: query }) as MediaQueryList) as typeof matchMedia;
        try {
            render(FrameTimeline, { frame: FRAME, names: NAMES });
            const canvas = screen.getByRole('application');
            await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
            vi.mocked(drawTimeline).mockClear();
            await fireEvent.keyDown(canvas, { key: 'Enter' });
            await waitForFrame();
            const [, , opts] = vi.mocked(drawTimeline).mock.calls[0];
            expect(opts.view).toEqual({ startUs: 100, endUs: 500 });
        } finally {
            globalThis.matchMedia = original;
        }
    });

    // regression: other keydown tests target the canvas directly, bypassing real DOM focus.
    it('moves real DOM focus to the canvas on pointerdown, so a later keydown on activeElement reaches it', async () => {
        // real throw, matching jsdom: a reordered focus() after this would never run.
        const originalSetPointerCapture = HTMLCanvasElement.prototype.setPointerCapture;
        HTMLCanvasElement.prototype.setPointerCapture = vi.fn(() => {
            throw new Error('no pointer capture');
        });
        // the throw above is expected; stop jsdom reporting it as an unhandled window error.
        const swallowExpectedThrow = (e: ErrorEvent) => e.preventDefault();
        window.addEventListener('error', swallowExpectedThrow);
        try {
            render(FrameTimeline, { frame: FRAME, names: NAMES });
            const canvas = screen.getByRole('application');
            const event = new Event('pointerdown', { bubbles: true, cancelable: true });
            Object.assign(event, { clientX: 0, clientY: 0, pointerId: 1 });
            await fireEvent(canvas, event);
            expect(document.activeElement).toBe(canvas);

            await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
            expect(screen.getByRole('status')).toHaveTextContent('Verse.TickList.Tick');
        } finally {
            window.removeEventListener('error', swallowExpectedThrow);
            if (originalSetPointerCapture === undefined) {
                delete (HTMLCanvasElement.prototype as { setPointerCapture?: unknown })
                    .setPointerCapture;
            } else {
                HTMLCanvasElement.prototype.setPointerCapture = originalSetPointerCapture;
            }
        }
    });

    it('shows both frame and budget percentages on hover, and they differ', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        stubRect(canvas, 600);
        // depth 1, atUs ~324us: inside node0's [100, 500) span (dur_us 400 of a 16200us frame).
        await firePointerMove(canvas, 12, 20);
        await waitFor(() => expect(screen.getByText('of frame')).toBeInTheDocument());
        const ofFrame = screen.getByText('of frame').nextElementSibling?.textContent;
        const ofBudget = screen.getByText('of budget').nextElementSibling?.textContent;
        expect(ofFrame).toBe('2.5%');
        expect(ofBudget).toBe('2.4%');
        expect(ofFrame).not.toBe(ofBudget);
    });

    it('shows the same two percentages in the selected-node readout', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        const readout = screen.getByTestId('frame-selected');
        expect(readout).toHaveTextContent('2.5% of frame');
        expect(readout).toHaveTextContent('2.4% of budget');
    });

    // a long frame lets budget share pass 100% while frame share stays under it.
    it('lets a node read over 100% of budget while under 100% of frame', async () => {
        const overBudget: FrameData = {
            capture_ordinal: 1,
            start_us: 0,
            end_us: 40000,
            duration_us: 40000,
            node_count: 2,
            nodes: {
                section_ids: [10, 30],
                parent_ids: [-1, 10],
                node_ids: [1, 2],
                parent_node_ids: [-1, 1],
                start_us: [0, 0],
                dur_us: [40000, 20000],
            },
        };
        render(FrameTimeline, { frame: overBudget, names: NAMES });
        const canvas = screen.getByRole('application');
        await fireEvent.keyDown(canvas, { key: 'ArrowDown' });
        const readout = screen.getByTestId('frame-selected');
        expect(readout).toHaveTextContent('50.0% of frame');
        expect(readout).toHaveTextContent('120.0% of budget');
    });

    // nothing stops the arrow keys (or wheel-equivalent +/-) from also scrolling the page.
    it('prevents the default action for every handled key', async () => {
        render(FrameTimeline, { frame: FRAME, names: NAMES });
        const canvas = screen.getByRole('application');
        const keys = [
            'ArrowLeft',
            'ArrowRight',
            'ArrowUp',
            'ArrowDown',
            'Enter',
            'Escape',
            'Home',
            '+',
            '-',
        ];
        for (const key of keys) {
            const notPrevented = await fireEvent.keyDown(canvas, { key });
            expect(notPrevented).toBe(false);
        }
    });
});
