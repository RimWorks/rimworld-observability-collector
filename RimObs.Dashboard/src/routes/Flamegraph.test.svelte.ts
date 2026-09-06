import { describe, it, expect, vi, beforeAll, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/svelte';
import Flamegraph from './Flamegraph.svelte';

// under fake timers the rAF draw loop actually fires, and jsdom has no 2d context to give it.
vi.mock('../lib/frameDraw', async (importOriginal) => {
    const actual = await importOriginal<typeof import('../lib/frameDraw')>();
    return { ...actual, drawTimeline: vi.fn() };
});

const realGetContext = HTMLCanvasElement.prototype.getContext;

beforeAll(() => {
    globalThis.ResizeObserver ??= class {
        observe() {}
        unobserve() {}
        disconnect() {}
    } as unknown as typeof ResizeObserver;
});

const FRAMES_BODY = {
    schema_version: 6,
    frame: {
        capture_ordinal: 4321,
        start_us: 0,
        end_us: 16200,
        duration_us: 16200,
        node_count: 2,
        nodes: {
            section_ids: [30, 10],
            parent_ids: [10, -1],
            node_ids: [2, 1],
            parent_node_ids: [1, -1],
            start_us: [100, 0],
            dur_us: [400, 16200],
        },
    },
    stats: {
        frame_count: 2000,
        // deliberately not capture_ordinal, so a card reading the wrong source shows up
        newest_ordinal: 4400,
        oldest_ordinal: 2322,
        median_us: 5000,
        p99_us: 99000,
        min_us: 8000,
        max_us: 40000,
    },
    dropped: { pre_frame_samples: 12, late_samples: 0 },
};

const SECTIONS_BODY = {
    schema_version: 6,
    sections: [
        { id: 10, name: 'Verse.Root_Play.Update', subsystem: 'render' },
        { id: 30, name: 'Verse.TickList.Tick', subsystem: 'tick' },
    ],
};

function mockFetch(frames: unknown = FRAMES_BODY) {
    globalThis.fetch = vi.fn((input: RequestInfo | URL) => {
        const url = String(input);
        const body = url.includes('/frames/latest') ? frames : SECTIONS_BODY;
        return Promise.resolve(
            new Response(JSON.stringify(body), {
                status: 200,
                headers: { 'content-type': 'application/json' },
            }),
        );
    }) as unknown as typeof fetch;
}

function frameCalls() {
    return vi.mocked(fetch).mock.calls.filter((c) => String(c[0]).includes('/frames/latest'))
        .length;
}

function cardValue(label: string) {
    return screen.getByText(label).parentElement?.textContent ?? '';
}

function cardValueEl(label: string) {
    return screen.getByText(label).parentElement?.querySelector('.value') ?? null;
}

beforeEach(() => {
    HTMLCanvasElement.prototype.getContext = (() => ({}) as unknown) as never;
    mockFetch();
});

afterEach(() => {
    HTMLCanvasElement.prototype.getContext = realGetContext;
    vi.useRealTimers();
});

describe('Flamegraph page', () => {
    it('fills every stat card from the frame it drew', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());
        expect(cardValue('Frame')).toContain('4321');
        expect(cardValue('Duration')).toContain('16.20 ms');
        expect(cardValue('Nodes')).toContain('2');
        expect(cardValue('Median')).toContain('5.00 ms');
        expect(cardValue('p99')).toContain('99.00 ms');
    });

    it('renders the timeline widget', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());
    });

    it('offers a poll rate picker', () => {
        render(Flamegraph);
        expect(screen.getByLabelText(/rate/i)).toBeInTheDocument();
    });

    it('renders no iframe', async () => {
        const { container } = render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());
        expect(container.querySelector('iframe')).toBeNull();
    });

    it('tells keyboard users how to move around the canvas', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());
        expect(screen.getByText(/arrow keys move between bars/i)).toBeInTheDocument();
    });

    it('shows the empty state when no frame has arrived', async () => {
        mockFetch({ ...FRAMES_BODY, frame: null });
        render(Flamegraph);
        expect(await screen.findByText('No frames yet')).toBeInTheDocument();
        expect(screen.queryByRole('application')).toBeNull();
    });

    it('shows every drop counter separately, zeroes included', async () => {
        render(Flamegraph);
        await screen.findByTestId('frame-drops');
        expect(screen.getByTestId('drop-late')).toHaveTextContent('0');
        expect(screen.getByTestId('drop-preframe')).toHaveTextContent('12');
        expect(screen.getByTestId('drop-orphans')).toHaveTextContent('0');
    });

    it('pairs each drop label with its own counter', async () => {
        render(Flamegraph);
        await screen.findByTestId('frame-drops');
        expect(screen.getByTestId('drop-late').parentElement).toHaveTextContent(/^Late samples/);
        expect(screen.getByTestId('drop-preframe').parentElement).toHaveTextContent(
            /^Pre-frame samples/,
        );
        expect(screen.getByTestId('drop-orphans').parentElement).toHaveTextContent(/^Orphan nodes/);
    });

    it('marks late samples as a warning', async () => {
        mockFetch({
            ...FRAMES_BODY,
            dropped: { pre_frame_samples: 12, late_samples: 7 },
        });
        render(Flamegraph);
        const late = await screen.findByTestId('drop-late');
        expect(late).toHaveTextContent('7');
        expect(late.className).toContain('warn');
    });

    it('leaves a healthy launch unwarned, pre-frame samples included', async () => {
        render(Flamegraph);
        await screen.findByTestId('frame-drops');
        expect(screen.getByTestId('drop-late').className).not.toContain('warn');
        expect(screen.getByTestId('drop-preframe').className).not.toContain('warn');
        expect(screen.getByTestId('drop-orphans').className).not.toContain('warn');
    });

    it('counts and warns on orphan nodes the timeline could not parent', async () => {
        mockFetch({
            ...FRAMES_BODY,
            frame: {
                ...FRAMES_BODY.frame,
                nodes: { ...FRAMES_BODY.frame.nodes, parent_node_ids: [77, -1] },
            },
        });
        render(Flamegraph);
        const orphans = await screen.findByTestId('drop-orphans');
        await waitFor(() => expect(orphans).toHaveTextContent('1'));
        expect(orphans.className).toContain('warn');
    });

    it('changing the rate changes how often it polls', async () => {
        vi.useFakeTimers();
        render(Flamegraph);
        await vi.advanceTimersByTimeAsync(0);

        const rateSelect = screen.getByLabelText(/rate/i) as HTMLSelectElement;
        await fireEvent.change(rateSelect, { target: { value: '1000' } });
        await vi.advanceTimersByTimeAsync(0);
        expect(rateSelect.value).toBe('1000');

        const base = frameCalls();
        await vi.advanceTimersByTimeAsync(900);
        expect(frameCalls()).toBe(base);
        await vi.advanceTimersByTimeAsync(200);
        expect(frameCalls()).toBe(base + 1);
    });

    it('stops polling once the page goes away', async () => {
        vi.useFakeTimers();
        const { unmount } = render(Flamegraph);
        await vi.advanceTimersByTimeAsync(600);

        unmount();
        const settled = frameCalls();
        await vi.advanceTimersByTimeAsync(2000);
        expect(frameCalls()).toBe(settled);
    });

    it('shows the benchmarked overhead estimate as a percent of the frame', async () => {
        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, node_count: 9, duration_us: 4045 },
        });
        render(Flamegraph);
        await waitFor(() =>
            expect(screen.getByTestId('frame-overhead')).toHaveTextContent('73 ns/scope'),
        );
        expect(screen.getByTestId('frame-overhead')).toHaveTextContent('0.02% of frame');
    });

    it('renders the timer resolution when the session reports a stopwatch frequency', async () => {
        mockFetch({ ...FRAMES_BODY, stopwatch_frequency: 10_000_000 });
        render(Flamegraph);
        await waitFor(() =>
            expect(screen.getByTestId('frame-overhead')).toHaveTextContent('timer res 100 ns'),
        );
    });

    it('omits timer res when the session reports no stopwatch frequency', async () => {
        mockFetch({ ...FRAMES_BODY, stopwatch_frequency: 0 });
        render(Flamegraph);
        await waitFor(() =>
            expect(screen.getByTestId('frame-overhead')).toHaveTextContent('73 ns/scope'),
        );
        expect(screen.getByTestId('frame-overhead')).not.toHaveTextContent('timer res');
    });

    it('marks the Duration stat as a warning once the frame runs over the tick budget', async () => {
        mockFetch({ ...FRAMES_BODY, frame: { ...FRAMES_BODY.frame, duration_us: 20_000 } });
        render(Flamegraph);
        await waitFor(() => expect(cardValueEl('Duration')).not.toBeNull());
        expect(cardValueEl('Duration')?.className).toContain('warn');
    });

    it('leaves the Duration stat unwarned under the tick budget', async () => {
        render(Flamegraph);
        await waitFor(() => expect(cardValueEl('Duration')).not.toBeNull());
        expect(cardValueEl('Duration')?.className).not.toContain('warn');
    });

    it('shows a delta once a second frame has been seen, colored by severity', async () => {
        vi.useFakeTimers();
        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 1, duration_us: 1000 },
        });
        render(Flamegraph);
        await vi.advanceTimersByTimeAsync(0);
        expect(screen.getByTestId('frame-overhead')).not.toHaveTextContent('Δ');

        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 2, duration_us: 1600 },
        });
        await vi.advanceTimersByTimeAsync(250);

        const line = screen.getByTestId('frame-overhead');
        expect(line.textContent).toContain('Δ');
        expect(line.textContent).toContain('+');
        expect(line.querySelector('.warn')).not.toBeNull();
    });

    it('leaves a small frame-to-frame wobble uncolored', async () => {
        vi.useFakeTimers();
        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 1, duration_us: 1000 },
        });
        render(Flamegraph);
        await vi.advanceTimersByTimeAsync(0);

        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 2, duration_us: 1200 },
        });
        await vi.advanceTimersByTimeAsync(250);

        const line = screen.getByTestId('frame-overhead');
        expect(line.textContent).toContain('Δ');
        expect(line.querySelector('.warn')).toBeNull();
        expect(line.querySelector('.cool')).toBeNull();
    });

    it('colors a frame-to-frame speedup cool, not warn, with no double space next to it', async () => {
        vi.useFakeTimers();
        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 1, duration_us: 1600 },
        });
        render(Flamegraph);
        await vi.advanceTimersByTimeAsync(0);

        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 2, duration_us: 1000 },
        });
        await vi.advanceTimersByTimeAsync(250);

        const line = screen.getByTestId('frame-overhead');
        expect(line.textContent).toContain('Δ');
        expect(line.textContent).toContain('-');
        expect(line.querySelector('.cool')).not.toBeNull();
        expect(line.querySelector('.warn')).toBeNull();
        expect(line.textContent).not.toMatch(/ {2,}/);
    });

    it('renders the overhead line with no stray space where the delta is absent', async () => {
        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, node_count: 9, duration_us: 4045 },
        });
        render(Flamegraph);
        await waitFor(() =>
            expect(screen.getByTestId('frame-overhead').textContent).toBe(
                'overhead 73 ns/scope (~0.02% of frame)',
            ),
        );
    });
});
