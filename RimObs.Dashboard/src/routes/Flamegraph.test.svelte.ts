import { describe, it, expect, vi, beforeAll, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/svelte';
import Flamegraph from './Flamegraph.svelte';
import { sectionSearch } from '../lib/sectionSearchState.svelte';

// under fake timers the rAF draw loop actually fires, and jsdom has no 2d context to give it.
vi.mock('../lib/stripDraw', () => ({ drawStrip: vi.fn() }));

vi.mock('../lib/frameDraw', async (importOriginal) => {
    const actual = await importOriginal<typeof import('../lib/frameDraw')>();
    return { ...actual, drawTimeline: vi.fn() };
});

const realGetContext = HTMLCanvasElement.prototype.getContext;

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
        p75_us: 20000,
        p90_us: 46000,
        p99_us: 99000,
        min_us: 8000,
        max_us: 40000,
    },
    dropped: { pre_frame_samples: 12, late_samples: 0 },
    strip: { ordinals: [4319, 4320, 4321], durations_us: [5000, 40000, 16200] },
};

// section 10 is the frame's root, so a baseline for it makes the delta column render.
const BASELINE_BODY = { frames: 128, median_us: { 10: 1000, 30: 100 } };

const HOTSPOTS_BODY = {
    schema_version: 6,
    hotspots: [
        {
            id: 10,
            name: 'root',
            subsystem: 'tick',
            sample_count: 5,
            total_ns: 3_000_000,
            mean_ns: 600_000,
            min_ns: 1000,
            max_ns: 900_000,
            p50_ns: 2_000_000,
            p95_ns: 2_500_000,
            p99_ns: 2_900_000,
        },
    ],
};

const TIMESERIES_BODY = {
    schema_version: 6,
    section_id: 10,
    // empty on purpose: uplot cannot paint under jsdom, and the drawer's own behaviour is what
    // this covers. the chart itself is exercised by a real browser, not here.
    points: [],
};

const CALL_TREE_BODY = {
    schema_version: 6,
    roots: [
        {
            id: 10,
            name: 'root',
            call_count: 5,
            total_ns: 3_000_000,
            is_other: false,
            children: [],
        },
    ],
};

const SECTIONS_BODY = {
    schema_version: 6,
    sections: [
        { id: 10, name: 'Verse.Root_Play.Update', subsystem: 'render' },
        { id: 30, name: 'Verse.TickList.Tick', subsystem: 'tick' },
    ],
};

const PATCHES_BODY = {
    schema_version: 6,
    conflicts_known: true,
    conflicts: [
        {
            section: 'Verse.Root_Play.Update',
            target_method: 'Verse.Root_Play.Update',
            other_owner: 'RocketMan',
            patch_type: 1,
            priority: 0,
            patch_method: 'RocketMan.Patch',
        },
        {
            section: 'Verse.Root_Play.Update',
            target_method: 'Verse.Root_Play.Update',
            other_owner: 'Dubs',
            patch_type: 1,
            priority: 0,
            patch_method: 'Dubs.Patch',
        },
    ],
};

const INSTRUMENTATION_BODY = {
    schema_version: 6,
    persisted: [
        {
            id: 3,
            typeFullName: 'Verse.TickList',
            methodName: 'Tick',
            paramTypesJoined: '',
            createdUtc: '',
            lastStatus: 'active',
            lastError: null,
            livePatchId: 5,
        },
    ],
    live: [{ patchId: 5, sectionId: 30, signature: 'Verse.TickList:Tick()', status: 'active' }],
};

const BUNDLE_FRAMES_BODY = {
    schema_version: 6,
    session_id: 'sess-imported',
    stopwatch_frequency: 10_000_000,
    frames: [
        { ...FRAMES_BODY.frame, capture_ordinal: 900 },
        { ...FRAMES_BODY.frame, capture_ordinal: 901 },
    ],
    stats: FRAMES_BODY.stats,
    dropped: { pre_frame_samples: 0, late_samples: 3 },
};

const BUNDLE_HOTSPOTS_BODY = {
    hotspots: [
        {
            id: 10,
            name: 'Verse.Root_Play.Update',
            sample_count: 4,
            total_ns: 900,
            subsystem: 'ui',
        },
        { id: 30, name: 'Verse.TickList.Tick', sample_count: 2, total_ns: 400, subsystem: 'ai' },
    ],
};

type StubFrame = (typeof FRAMES_BODY)['frame'];

// each older frame sits 20ms earlier on the same axis, which is what the timeline needs to
// place them side by side instead of on top of each other.
function shiftFrame(frame: StubFrame, ordinal: number, deltaUs: number): StubFrame {
    return {
        ...frame,
        capture_ordinal: ordinal,
        start_us: frame.start_us + deltaUs,
        end_us: frame.end_us + deltaUs,
        nodes: { ...frame.nodes, start_us: frame.nodes.start_us.map((v) => v + deltaUs) },
    };
}

// the window backfill: three frames ending at whatever /frames/latest was stubbed with.
function frameRangeBody(latest: unknown, url: string) {
    const body = latest as typeof FRAMES_BODY;
    if (!body.frame) return { ...body, frames: [] };
    const newest = body.frame.capture_ordinal;
    const asked = Number(new URL(url, 'http://x').searchParams.get('from') ?? newest - 2);
    const frames: StubFrame[] = [];
    for (let o = Math.max(asked, newest - 2); o <= newest; o++) {
        frames.push(shiftFrame(body.frame, o, (o - newest) * 20000));
    }
    return { ...body, frames };
}

function frameAtBody(url: string) {
    const ordinal = Number(url.split('/').pop());
    return {
        ...FRAMES_BODY,
        frame: { ...FRAMES_BODY.frame, capture_ordinal: ordinal },
    };
}

const CONFIG_BODY = {
    schema_version: 6,
    sampling: { frame_ring_capacity: 5000 },
};

const IMPORT_BODY = {
    token: 'tok-1',
    manifest: { session_id: 'sess-imported' },
    contents: ['manifest.json', 'frames.json', 'hotspots.json'],
};

// String() on a Request gives "[object Request]", not the url the test wants to match.
function requestUrl(input: RequestInfo | URL): string {
    if (typeof input === 'string') return input;
    if (input instanceof URL) return input.href;
    return input.url;
}

function mockFetch(
    frames: unknown = FRAMES_BODY,
    importBody: unknown = IMPORT_BODY,
    importStatus = 200,
) {
    globalThis.fetch = vi.fn((input: RequestInfo | URL) => {
        const url = requestUrl(input);
        let body: unknown;
        let status = 200;
        if (url.includes('/api/v1/frames/clear')) body = { frame_count: 0 };
        if (/\/api\/v1\/frames\/\d+$/.test(url)) body = frameAtBody(url);
        else if (url.includes('/api/v1/frames?')) body = frameRangeBody(frames, url);
        else if (url.includes('/api/v1/config')) body = CONFIG_BODY;
        else if (url.includes('/frames/baseline')) body = BASELINE_BODY;
        else if (url.includes('/call_tree')) body = CALL_TREE_BODY;
        else if (url.includes('/sessions/current/hotspots')) body = HOTSPOTS_BODY;
        else if (url.includes('/sessions/current/patches')) body = PATCHES_BODY;
        else if (url.includes('/instrumentation/patches')) body = INSTRUMENTATION_BODY;
        else if (url.includes('/timeseries')) body = TIMESERIES_BODY;
        else if (url.includes('/file/frames.json')) body = BUNDLE_FRAMES_BODY;
        else if (url.includes('/file/hotspots.json')) body = BUNDLE_HOTSPOTS_BODY;
        else if (url.includes('/api/v1/import/bundle')) {
            body = importBody;
            status = importStatus;
        } else if (url.includes('/frames/latest')) body = frames;
        else body = SECTIONS_BODY;
        return Promise.resolve(
            new Response(JSON.stringify(body), {
                status,
                headers: { 'content-type': 'application/json' },
            }),
        );
    }) as unknown as typeof fetch;
}

// the tab footer starts collapsed, so anything reading the tree has to open the drawer first
async function openTree() {
    await screen.findByTestId('tab-tree');
    await fireEvent.click(screen.getByTestId('tab-tree'));
}

// instrumentation and comparison moved out of their own <details> and into the same footer
async function openFooterTab(id: 'instrumentation' | 'comparison') {
    await screen.findByTestId(`tab-${id}`);
    await fireEvent.click(screen.getByTestId(`tab-${id}`));
}

function jsonResponse(body: unknown) {
    return Promise.resolve(
        new Response(JSON.stringify(body), {
            status: 200,
            headers: { 'content-type': 'application/json' },
        }),
    );
}

function frameCalls() {
    return vi.mocked(fetch).mock.calls.filter((c) => String(c[0]).includes('/frames/latest'))
        .length;
}

function deletedTokens() {
    return vi
        .mocked(fetch)
        .mock.calls.filter((c) => (c[1] as RequestInit | undefined)?.method === 'DELETE')
        .map((c) => String(c[0]).split('/').pop());
}

async function openFile(getByLabelText: (m: RegExp) => HTMLElement, name = 'session.rimobs.zip') {
    const file = new File(['zip'], name, { type: 'application/zip' });
    await fireEvent.change(getByLabelText(/open bundle/i), { target: { files: [file] } });
}

function cardValue(label: string) {
    const cell = [...document.querySelectorAll('.cell')].find((e) =>
        e.textContent?.trim().toLowerCase().startsWith(label.toLowerCase()),
    );
    return cell?.textContent ?? '';
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
    it('reads every headline number off the frame it drew', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());
        expect(cardValue('Duration')).toContain('16.20 ms');
        expect(cardValue('Nodes')).toContain('2');
        expect(cardValue('Median')).toContain('5.00 ms');
        expect(cardValue('p99')).toContain('99.00 ms');
    });

    it('renders the timeline widget', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());
    });

    // the rate control is gone: a frame the dashboard never asked for is a frame it cannot show.
    it('offers no poll rate control', () => {
        render(Flamegraph);
        expect(screen.queryByLabelText(/rate/i)).toBeNull();
    });

    it('renders no iframe', async () => {
        const { container } = render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());
        expect(container.querySelector('iframe')).toBeNull();
    });

    // the keyboard help moved into the header's info tooltip, covered by TopBar.test. what this
    // page still owes is that it does not keep a second copy as a standing paragraph.
    it('no longer prints the keyboard help as a standing paragraph', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());
        expect(screen.queryByText(/arrow keys move between bars/i)).toBeNull();
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

    it('polls once per frame', async () => {
        vi.useFakeTimers();
        render(Flamegraph);
        await vi.advanceTimersByTimeAsync(0);

        const base = frameCalls();
        await vi.advanceTimersByTimeAsync(160);

        // 16ms cadence, so ten polls in 160ms. the timer is not exact, so allow one either way.
        expect(frameCalls() - base).toBeGreaterThanOrEqual(9);
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
        // the footer renders before the first poll, so wait for the share, not the constant.
        await waitFor(() =>
            expect(screen.getByTestId('footer-overhead')).toHaveTextContent('0.02% of frame'),
        );
        expect(screen.getByTestId('footer-overhead')).toHaveTextContent('73 ns/scope');
    });

    it('renders the timer resolution when the session reports a stopwatch frequency', async () => {
        mockFetch({ ...FRAMES_BODY, stopwatch_frequency: 10_000_000 });
        render(Flamegraph);
        await waitFor(() =>
            expect(screen.getByTestId('footer-timerres')).toHaveTextContent('timer res 100 ns'),
        );
    });

    it('omits timer res when the session reports no stopwatch frequency', async () => {
        mockFetch({ ...FRAMES_BODY, stopwatch_frequency: 0 });
        render(Flamegraph);
        await waitFor(() =>
            expect(screen.getByTestId('footer-overhead')).toHaveTextContent('73 ns/scope'),
        );
        expect(screen.queryByTestId('footer-timerres')).toBeNull();
    });

    it('marks the Duration stat as a warning once the frame runs over the frame budget', async () => {
        mockFetch({ ...FRAMES_BODY, frame: { ...FRAMES_BODY.frame, duration_us: 50_000 } });
        render(Flamegraph);
        await waitFor(() => expect(screen.getByTestId('frame-duration')).toBeInTheDocument());
        expect(screen.getByTestId('frame-duration').className).toContain('warn');
    });

    it('leaves the Duration stat unwarned under the frame budget', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByTestId('frame-duration')).toBeInTheDocument());
        expect(screen.getByTestId('frame-duration').className).not.toContain('warn');
    });

    it('shows a delta once a second frame has been seen, colored by severity', async () => {
        vi.useFakeTimers();
        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 1, duration_us: 1000 },
        });
        render(Flamegraph);
        await vi.advanceTimersByTimeAsync(0);
        expect(screen.queryByTestId('footer-delta')).toBeNull();

        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 2, duration_us: 1600 },
        });
        await vi.advanceTimersByTimeAsync(250);

        const line = screen.getByTestId('footer-delta');
        expect(line.textContent).toContain('Δ');
        expect(line.textContent).toContain('+');
        expect(line.className).toContain('warn');
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

        const line = screen.getByTestId('footer-delta');
        expect(line.textContent).toContain('Δ');
        expect(line.className).not.toContain('warn');
        expect(line.className).not.toContain('cool');
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

        const line = screen.getByTestId('footer-delta');
        expect(line.textContent).toContain('Δ');
        expect(line.textContent).toContain('-');
        expect(line.className).toContain('cool');
        expect(line.className).not.toContain('warn');
        expect(line.textContent).not.toMatch(/ {2,}/);
    });

    it('keeps the status footer mounted while the tab drawer opens and closes', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByTestId('status-footer')).toBeInTheDocument());

        await openTree();
        expect(screen.getByTestId('status-footer')).toBeInTheDocument();

        await fireEvent.click(screen.getByTestId('tab-tree'));
        expect(screen.getByTestId('status-footer')).toBeInTheDocument();
    });

    it('renders the overhead readout with no stray space and no timer res when absent', async () => {
        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, node_count: 9, duration_us: 4045 },
        });
        render(Flamegraph);
        await waitFor(() =>
            expect(screen.getByTestId('footer-overhead').textContent).toBe(
                'overhead 73 ns/scope (~0.02% of frame)',
            ),
        );
        expect(screen.queryByTestId('footer-timerres')).toBeNull();
    });

    it('scrubs through an imported bundle instead of polling', async () => {
        const { getByTestId, getByLabelText } = render(Flamegraph);
        const file = new File(['zip'], 'session.rimobs.zip', { type: 'application/zip' });
        await fireEvent.change(getByLabelText(/open bundle/i), { target: { files: [file] } });

        const scrub = (await waitFor(() => getByTestId('frame-scrub'))) as HTMLInputElement;
        expect(scrub.value).toBe('1');
        expect(scrub.getAttribute('max')).toBe('1');
        await waitFor(() => expect(screen.getByText('901')).toBeInTheDocument());

        await fireEvent.input(scrub, { target: { value: '0' } });
        await waitFor(() => expect(screen.getByText('900')).toBeInTheDocument());

        await fireEvent.input(scrub, { target: { value: scrub.getAttribute('max')! } });
        await waitFor(() => expect(screen.getByText('901')).toBeInTheDocument());
    });

    it('reads drops and stats from the bundle, not the live poller', async () => {
        const { getByLabelText } = render(Flamegraph);
        const file = new File(['zip'], 'session.rimobs.zip', { type: 'application/zip' });
        await fireEvent.change(getByLabelText(/open bundle/i), { target: { files: [file] } });

        await screen.findByTestId('frame-scrub');
        expect(screen.getByTestId('drop-late')).toHaveTextContent('3');
        expect(screen.getByTestId('drop-preframe')).toHaveTextContent('0');
    });

    it('colors imported bars by the bundle hotspots subsystem, not the live sections', async () => {
        const { drawTimeline } = await import('../lib/frameDraw');
        const { getByLabelText } = render(Flamegraph);
        await waitFor(() => expect(vi.mocked(drawTimeline)).toHaveBeenCalled());
        const callsBeforeImport = vi.mocked(drawTimeline).mock.calls.length;

        const file = new File(['zip'], 'session.rimobs.zip', { type: 'application/zip' });
        await fireEvent.change(getByLabelText(/open bundle/i), { target: { files: [file] } });
        await screen.findByTestId('frame-scrub');

        await waitFor(() =>
            expect(vi.mocked(drawTimeline).mock.calls.length).toBeGreaterThan(callsBeforeImport),
        );
        const opts = vi.mocked(drawTimeline).mock.lastCall![2];
        expect(opts.subsystem({ sectionId: 10 } as never)).toBe('ui');
        expect(opts.subsystem({ sectionId: 30 } as never)).toBe('ai');
    });

    it('switching to a bundle stops the live poller, and switching back resumes it', async () => {
        // the clock has to be fake before render, or the poller's interval is a real one and
        // advancing the fake clock proves nothing about whether it stopped.
        vi.useFakeTimers();
        const { getByLabelText } = render(Flamegraph);
        const file = new File(['zip'], 'session.rimobs.zip', { type: 'application/zip' });
        await fireEvent.change(getByLabelText(/open bundle/i), { target: { files: [file] } });
        await screen.findByTestId('frame-scrub');

        const afterImport = frameCalls();
        await vi.advanceTimersByTimeAsync(2000);
        expect(frameCalls()).toBe(afterImport);

        const sourceSelect = getByLabelText(/^Source$/i) as HTMLSelectElement;
        await fireEvent.change(sourceSelect, { target: { value: 'live' } });
        await vi.advanceTimersByTimeAsync(0);
        expect(frameCalls()).toBeGreaterThan(afterImport);
    });

    it('refuses a bundle with no frames.json and stays live', async () => {
        mockFetch(FRAMES_BODY, { ...IMPORT_BODY, contents: ['manifest.json', 'hotspots.json'] });
        const { getByLabelText } = render(Flamegraph);
        const file = new File(['zip'], 'session.rimobs.zip', { type: 'application/zip' });
        await fireEvent.change(getByLabelText(/open bundle/i), { target: { files: [file] } });

        expect(await screen.findByRole('alert')).toHaveTextContent(/no frames\.json/i);
        expect(screen.queryByTestId('frame-scrub')).toBeNull();
        expect((getByLabelText(/^Source$/i) as HTMLSelectElement).value).toBe('live');

        // real timers on purpose: the poller's interval was created before this line, so a
        // fake clock installed now would never fire it.
        const base = frameCalls();
        await waitFor(() => expect(frameCalls()).toBeGreaterThan(base));
    });

    // one import feeds both jobs now, so a bundle is kept as a comparison source rather than
    // deleted the moment a second one arrives. the collector expires the tokens itself.
    it('keeps every import as a source instead of deleting the previous one', async () => {
        let n = 0;
        const realFetch = globalThis.fetch;
        globalThis.fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
            // the per-file URLs sit under the same prefix, so match the upload by method.
            if (init?.method === 'POST') {
                n += 1;
                return Promise.resolve(
                    new Response(JSON.stringify({ ...IMPORT_BODY, token: `tok-${n}` }), {
                        status: 200,
                        headers: { 'content-type': 'application/json' },
                    }),
                );
            }
            return realFetch(input, init);
        }) as unknown as typeof fetch;

        const { getByLabelText } = render(Flamegraph);
        await openFile(getByLabelText);
        await screen.findByTestId('frame-scrub');

        await openFile(getByLabelText, 'other.rimobs.zip');

        const source = () => getByLabelText(/^Source$/i) as HTMLSelectElement;
        await waitFor(() => expect(source().options).toHaveLength(3));
        expect(deletedTokens()).toEqual([]);
    });

    // a frameless bundle cannot be scrubbed, but it is still a valid comparison source.
    it('keeps a bundle with no frames.json without offering it as a frame source', async () => {
        mockFetch(FRAMES_BODY, { ...IMPORT_BODY, contents: ['manifest.json'] });
        const { getByLabelText } = render(Flamegraph);
        await openFile(getByLabelText);

        expect(await screen.findByRole('alert')).toHaveTextContent(/no frames\.json/i);
        const source = getByLabelText(/^Source$/i) as HTMLSelectElement;
        expect(source.options).toHaveLength(1);
        expect(source.value).toBe('live');
        expect(deletedTokens()).toEqual([]);
    });

    // the orphan cleanup still has to fire when the upload wins but a later request throws.
    it('deletes the upload when reading its frames fails', async () => {
        const realFetch = globalThis.fetch;
        globalThis.fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
            if (requestUrl(input).includes('/file/frames.json'))
                return Promise.resolve(new Response('nope', { status: 500 }));
            return realFetch(input, init);
        }) as unknown as typeof fetch;

        const { getByLabelText } = render(Flamegraph);
        await openFile(getByLabelText);

        await waitFor(() => expect(deletedTokens()).toEqual(['tok-1']));
    });

    it('keeps a bundle whose frame ring is empty and stays live', async () => {
        const realFetch = globalThis.fetch;
        globalThis.fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
            if (requestUrl(input).includes('/file/frames.json'))
                return Promise.resolve(
                    new Response(JSON.stringify({ ...BUNDLE_FRAMES_BODY, frames: [] }), {
                        status: 200,
                        headers: { 'content-type': 'application/json' },
                    }),
                );
            return realFetch(input, init);
        }) as unknown as typeof fetch;

        const { getByLabelText } = render(Flamegraph);
        await openFile(getByLabelText);

        expect(await screen.findByRole('alert')).toHaveTextContent(/empty frame ring/i);
        expect(screen.queryByTestId('frame-scrub')).toBeNull();
        const source = getByLabelText(/^Source$/i) as HTMLSelectElement;
        expect(source.options).toHaveLength(1);
        expect(source.value).toBe('live');
        expect(deletedTokens()).toEqual([]);
    });

    // the bundle's newest frame usually reuses an ordinal the poller already drew, so the
    // effect short-circuits and the delta keeps a value computed from two live frames.
    it('drops the live delta when the source switches to a bundle', async () => {
        const { getByLabelText } = render(Flamegraph);
        await waitFor(() => expect(screen.getByTestId('status-footer')).toBeInTheDocument());

        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 4322, duration_us: 30000 },
        });
        await waitFor(() => expect(screen.getByTestId('footer-delta')).toBeInTheDocument());

        await openFile(getByLabelText);
        await screen.findByTestId('frame-scrub');
        expect(screen.queryByTestId('footer-delta')).toBeNull();
    });

    it('draws the frame strip from the strip endpoint', async () => {
        render(Flamegraph);
        expect(await screen.findByTestId('frame-strip')).toBeInTheDocument();
    });

    // pause pins the view, it does not stop the poller. the strip has to keep filling or you
    // cannot see the spike you paused to go and look at. Neo's IsPaused works the same way.
    it('pins the displayed frame while paused, then follows the newest again', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());

        await fireEvent.click(screen.getByTestId('pause'));
        expect(screen.getByTestId('paused-badge')).toBeInTheDocument();

        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 9999 },
        });
        const before = frameCalls();
        await waitFor(() => expect(frameCalls()).toBeGreaterThan(before));
        expect(screen.queryByText('9999')).toBeNull();
        expect(screen.getByText('4321')).toBeInTheDocument();

        await fireEvent.click(screen.getByTestId('pause'));
        expect(screen.queryByTestId('paused-badge')).toBeNull();
        await waitFor(() => expect(screen.getByText('9999')).toBeInTheDocument());
    });

    // Neo freezes the history with the frame and marks the gap on resume. a strip that keeps
    // filling while paused hides the fact that the run either side of the pause is not continuous.
    it('freezes the frame history while paused and resumes from live after', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());
        const strip = screen.getByTestId('frame-strip');

        await fireEvent.click(screen.getByTestId('pause'));
        expect(strip).toBeInTheDocument();

        mockFetch({
            ...FRAMES_BODY,
            frame: { ...FRAMES_BODY.frame, capture_ordinal: 9999 },
            strip: { ordinals: [9998, 9999], durations_us: [1000, 2000] },
        });
        const before = frameCalls();
        await waitFor(() => expect(frameCalls()).toBeGreaterThan(before));
        expect(screen.getByText('4321')).toBeInTheDocument();

        await fireEvent.click(screen.getByTestId('pause'));
        await waitFor(() => expect(screen.getByText('9999')).toBeInTheDocument());
    });

    // the frame ring cannot produce per-section percentiles, so session scope reads them from
    // /hotspots. they are the one thing the cut Hotspots page had that the tree did not.
    it('shows per-section percentiles only in session scope', async () => {
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
        expect(screen.queryByTestId('tree-p50')).toBeNull();

        await fireEvent.click(screen.getByTestId('scope-session'));
        await waitFor(() => expect(screen.getAllByTestId('tree-p50').length).toBeGreaterThan(0));
        expect(screen.getAllByTestId('tree-p50')[0].textContent).toContain('ms');
    });

    // the readout grades each percentile against the 45.45ms frame budget, so a spread that
    // crosses the budget has to change colour rather than all read the same.
    it('colours each percentile by its share of the frame budget', async () => {
        render(Flamegraph);
        // wait for real stats: the element renders with a zero placeholder before they land
        await waitFor(() => expect(screen.getByTestId('stat-p99_us').textContent).toContain('ms'));
        // 5ms of 45.45 is 11%, 99ms is over it entirely
        const low = screen.getByTestId('stat-median_us').className;
        const high = screen.getByTestId('stat-p99_us').className;
        expect(low).not.toBe(high);
        expect(high).toContain('g4');
    });

    it('shows the whole percentile spread, not just median and p99', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByTestId('stat-p75_us')).toBeInTheDocument());
        expect(screen.getByTestId('stat-p90_us')).toBeInTheDocument();
    });

    // the per-second trend drill-down, ported from the Hotspots page. session scope only:
    // it is a session ring and says nothing about the one frame on screen.
    it('opens a per-section trend drawer in session scope', async () => {
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
        expect(screen.queryByTestId('trend-toggle')).toBeNull();

        await fireEvent.click(screen.getByTestId('scope-session'));
        await waitFor(() =>
            expect(screen.getAllByTestId('trend-toggle').length).toBeGreaterThan(0),
        );

        await fireEvent.click(screen.getAllByTestId('trend-toggle')[0]);
        await waitFor(() =>
            expect(screen.getAllByTestId('trend-toggle')[0].getAttribute('aria-expanded')).toBe(
                'true',
            ),
        );
        expect(screen.getByText(/no samples in the last five minutes/i)).toBeInTheDocument();
    });

    // the 128-frame median means nothing against a session total, so only a session
    // comparison fills that column. until one runs it stays blank.
    it('blanks the delta column in session scope', async () => {
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-delta').length).toBeGreaterThan(0));
        // frame scope has a real per-frame baseline for section 10, so it prints a delta
        await waitFor(() =>
            expect(screen.getAllByTestId('tree-delta')[0].textContent?.trim()).not.toBe(''),
        );

        await fireEvent.click(screen.getByTestId('scope-session'));
        await waitFor(() =>
            expect(screen.getAllByTestId('tree-delta')[0].textContent?.trim()).toBe(''),
        );
    });

    // a session comparison is the one comparand session totals can be measured against.
    it('fills the session-scope delta column from a comparison result', async () => {
        const realFetch = globalThis.fetch;
        globalThis.fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
            const url = requestUrl(input);
            if (url === '/api/v1/sessions')
                return jsonResponse({
                    sessions: [
                        { id: 'head-session', is_current: true },
                        { id: 'base-session', is_current: false },
                    ],
                });
            if (url.startsWith('/api/v1/sessions/compare'))
                return jsonResponse({
                    disclaimer: 'd',
                    warnings: [],
                    timing: {
                        base_total_ns: 1,
                        head_total_ns: 1,
                        delta_ns: 0,
                        delta_percent: 0,
                        base_mean_ns: 1,
                        head_mean_ns: 1,
                        delta_mean_ns: 0,
                        base_sample_count: 1,
                        head_sample_count: 1,
                    },
                    // id 999 is deliberately wrong; the name is what has to resolve it to 10
                    hotspots: [
                        {
                            id: 999,
                            name: 'Verse.Root_Play.Update',
                            owner: 'core',
                            status: 'regressed',
                            base_total_ns: 1_000,
                            head_total_ns: 2_000,
                            delta_ns: 1_000,
                            delta_percent: 100,
                            base_mean_ns: 1,
                            head_mean_ns: 2,
                            likely_regression_candidate: false,
                        },
                    ],
                    mod_costs: [],
                    metrics: [],
                    load_order: { added: [], removed: [], common: [] },
                });
            return realFetch(input, init);
        }) as unknown as typeof fetch;

        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-delta').length).toBeGreaterThan(0));

        await openFooterTab('comparison');
        await waitFor(() => expect(screen.getByRole('button', { name: 'Compare' })).toBeEnabled());

        await fireEvent.click(screen.getByRole('button', { name: 'Compare' }));

        // the scope toolbar belongs to the tree, so it is not on screen while compare is open
        await openTree();

        // the result flips the tree to session scope, where the delta is now measurable
        await waitFor(() => expect(screen.getByTestId('scope-session')).toHaveClass('on'));
        await waitFor(() =>
            expect(
                screen.getAllByTestId('tree-delta').some((e) => e.textContent?.trim() !== ''),
            ).toBe(true),
        );
    });

    // the chip names the thread the tree belongs to, so it has to carry that thread's time.
    it('shows the thread total in the call tree thread chip', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByTestId('thread-chip')).toBeInTheDocument());
        const chip = screen.getByTestId('thread-chip');
        expect(chip).toHaveTextContent('MainThread');
        expect(chip).toHaveTextContent(screen.getByTestId('frame-duration').textContent!);
    });

    // the timeline holds its zoom in component state, so a single render where frame is null
    // tears it down and loses the zoom. pausing must never blank the stage.
    it('keeps a frame on screen while the pinned fetch is in flight', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());
        const canvas = screen.getAllByRole('img')[0];

        await fireEvent.click(screen.getByTestId('pause'));

        expect(screen.queryByText(/no frames yet/i)).toBeNull();
        expect(screen.getByTestId('frame-duration')).toBeInTheDocument();
        await waitFor(() => expect(screen.getByTestId('paused-badge')).toBeInTheDocument());
        expect(screen.getAllByRole('img')[0]).toBe(canvas);
    });

    // stepping reads a specific ordinal, so the card must follow the step, not the poller.
    it('steps to an older frame and shows that ordinal', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());

        await fireEvent.click(screen.getByTestId('step-older'));
        await waitFor(() => expect(screen.getByText('4320')).toBeInTheDocument());
        expect(screen.getByTestId('paused-badge')).toBeInTheDocument();
    });

    it('jumping to newest clears the pause and returns to the live frame', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());
        await fireEvent.click(screen.getByTestId('step-older'));
        await waitFor(() => expect(screen.getByText('4320')).toBeInTheDocument());

        await fireEvent.click(screen.getByTestId('jump-newest'));
        expect(screen.queryByTestId('paused-badge')).toBeNull();
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());
    });

    it('Space pauses from the window, PageDown steps older', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());

        await fireEvent.keyDown(window, { key: ' ' });
        expect(screen.getByTestId('paused-badge')).toBeInTheDocument();

        await fireEvent.keyDown(window, { key: 'PageDown' });
        await waitFor(() => expect(screen.getByText('4320')).toBeInTheDocument());
    });

    // the rate picker is a <select>; Space in it must pick an option, not pause the page.
    it('ignores Space typed into a form control', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());
        const picker = screen.getByLabelText(/source/i);
        await fireEvent.keyDown(picker, { key: ' ' });
        expect(screen.queryByTestId('paused-badge')).toBeNull();
    });

    it('badges a tree row with the number of other mods patching it', async () => {
        mockFetch();
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));

        const badges = await screen.findAllByTestId('patch-badge');
        expect(badges).toHaveLength(1);
        expect(badges[0].textContent?.trim()).toBe('2');
        expect(badges[0].closest('tr')?.textContent).toContain('Verse.Root_Play.Update');
    });

    // the canvas right-click path itself needs real geometry, which jsdom does not give a
    // canvas. liveSectionIds covers the lookup; this covers the panel being wired in at all.
    it('carries the instrumentation panel and its active patches', async () => {
        mockFetch();
        render(Flamegraph);
        await openFooterTab('instrumentation');
        await waitFor(() => expect(screen.getByTestId('instrumentation-panel')).toBeTruthy());

        const rows = await screen.findAllByTestId('active-patches');
        expect(rows[0].textContent).toContain('Verse.TickList.Tick');
        expect(await screen.findByTestId('patch-status')).toHaveTextContent(/active/i);
    });

    it('draws the pie tab from the frames own sections', async () => {
        mockFetch();
        render(Flamegraph);
        await waitFor(() => expect(screen.getByTestId('tab-tree')).toBeTruthy());
        await fireEvent.click(screen.getByTestId('tab-tree'));
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));

        await fireEvent.click(screen.getByRole('button', { name: /^pie$/i }));

        const chart = await screen.findByTestId('pie-chart');
        expect(chart.querySelectorAll('path').length).toBeGreaterThan(0);
        expect(screen.queryByTestId('tab-soon')).toBeNull();
    });

    it('shows no context menu until a bar is right-clicked', async () => {
        mockFetch();
        render(Flamegraph);
        await openFooterTab('instrumentation');
        await waitFor(() => expect(screen.getByTestId('instrumentation-panel')).toBeTruthy());

        expect(screen.queryByTestId('flame-context')).toBeNull();
    });

    it('hides the strip and transport for an imported bundle', async () => {
        const { getByLabelText } = render(Flamegraph);
        await openFile(getByLabelText);
        await screen.findByTestId('frame-scrub');
        expect(screen.queryByTestId('frame-strip')).toBeNull();
        expect(screen.queryByTestId('pause')).toBeNull();
    });

    it('renders the call tree panel with a row for each section in the frame', async () => {
        render(Flamegraph);
        await openTree();
        await screen.findByTestId('call-tree-panel');
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
        expect(screen.getByText('Verse.Root_Play.Update')).toBeInTheDocument();
    });

    it('filters the tree by the search box', async () => {
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
        await fireEvent.input(screen.getByTestId('tree-search'), { target: { value: 'ticklist' } });
        await waitFor(() => expect(screen.getAllByTestId('tree-row')).toHaveLength(1));
    });

    it('expand all reveals the nested section, collapse all hides it again', async () => {
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
        expect(screen.queryByText('Verse.TickList.Tick')).toBeNull();

        await fireEvent.click(screen.getByTestId('expand-all'));
        await waitFor(() => expect(screen.getByText('Verse.TickList.Tick')).toBeInTheDocument());

        await fireEvent.click(screen.getByTestId('collapse-all'));
        await waitFor(() => expect(screen.queryByText('Verse.TickList.Tick')).toBeNull());
    });

    it('sorting by self flips direction on a second click', async () => {
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
        const self = screen.getByTestId('sort-self');
        await fireEvent.click(self);
        expect(self.textContent).toContain('\u2193');
        await fireEvent.click(self);
        expect(self.textContent).toContain('\u2191');
    });

    it('starts with the tab footer collapsed and no tab selected', async () => {
        render(Flamegraph);
        await screen.findByTestId('call-tree-panel');

        expect(screen.queryByTestId('tree-drawer')).toBeNull();
        for (const id of ['tree', 'pie', 'alloc', 'vram']) {
            expect(screen.getByTestId(`tab-${id}`)).toHaveAttribute('aria-expanded', 'false');
        }
    });

    it('expands the drawer over the flamegraph when a tab is picked', async () => {
        render(Flamegraph);
        await openTree();

        expect(screen.getByTestId('tree-drawer')).toBeInTheDocument();
        expect(screen.getByTestId('tab-tree')).toHaveAttribute('aria-expanded', 'true');
    });

    it('collapses again when the active tab is picked a second time', async () => {
        render(Flamegraph);
        await openTree();

        await fireEvent.click(screen.getByTestId('tab-tree'));

        expect(screen.queryByTestId('tree-drawer')).toBeNull();
    });

    it('collapses from the close button', async () => {
        render(Flamegraph);
        await openTree();

        await fireEvent.click(screen.getByTestId('tree-close'));

        expect(screen.queryByTestId('tree-drawer')).toBeNull();
    });

    // the half of Neo's link it calls ExpandCallTreeToNode.
    it('clicking a tree row selects it and drives the flame view', async () => {
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
        await fireEvent.click(screen.getByText('Verse.Root_Play.Update'));
        await waitFor(() => expect(document.querySelector('tr.selected')).toBeInTheDocument());
    });

    // the row-to-flame link has to survive the drawer closing over it and coming back.
    it('keeps the selected row after the drawer is closed and reopened', async () => {
        render(Flamegraph);
        await openTree();
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
        await fireEvent.click(screen.getByText('Verse.Root_Play.Update'));
        await waitFor(() => expect(document.querySelector('tr.selected')).toBeInTheDocument());

        await fireEvent.click(screen.getByTestId('tree-close'));
        expect(screen.queryByTestId('tree-drawer')).toBeNull();
        await openTree();

        await waitFor(() => expect(document.querySelector('tr.selected')).toBeInTheDocument());
    });

    it('offers all four profiler tabs and honestly labels the ones with no collector', async () => {
        render(Flamegraph);
        await screen.findByTestId('call-tree-panel');
        for (const id of ['tree', 'pie', 'alloc', 'vram']) {
            expect(screen.getByTestId(`tab-${id}`)).toBeInTheDocument();
        }
        await fireEvent.click(screen.getByTestId('tab-vram'));
        expect(screen.getByTestId('tab-soon')).toBeInTheDocument();
        expect(screen.queryAllByTestId('tree-row')).toHaveLength(0);

        await fireEvent.click(screen.getByTestId('tab-tree'));
        await waitFor(() => expect(screen.getAllByTestId('tree-row').length).toBeGreaterThan(0));
    });

    // the whole point of the multi-frame view: one axis, many frames, not one frame per draw.
    // the window is fetched so a zoom out has somewhere to go, but landing on all of it
    // would bury the frame the user is looking at.
    it('backfills a window of frames but rests on the selected one', async () => {
        render(Flamegraph);
        await screen.findByTestId('frame-ruler');
        await waitFor(() =>
            expect(screen.getByTestId('frame-span')).toHaveTextContent('over 1 frames'),
        );
        expect(
            vi.mocked(fetch).mock.calls.some((c) => String(c[0]).includes('/api/v1/frames?')),
        ).toBe(true);
    });

    // the axis stays absolute across the whole window, but the resting view is one frame of it.
    it('rests on the selected frame while the window stays behind it', async () => {
        const { drawTimeline } = await import('../lib/frameDraw');
        render(Flamegraph);
        await waitFor(() => expect(vi.mocked(drawTimeline)).toHaveBeenCalled());
        const opts = vi.mocked(drawTimeline).mock.lastCall![2];

        expect(opts.view.startUs).toBe(0);
        expect(opts.view.endUs).toBe(16200);

        // the two older stub frames are still loaded, reaching back 40ms on the same axis.
        await waitFor(() =>
            expect(screen.getByTestId('frame-span')).toHaveTextContent('over 1 frames'),
        );
        expect(
            vi.mocked(fetch).mock.calls.some((c) => String(c[0]).includes('/api/v1/frames?')),
        ).toBe(true);
    });

    it('rests on the frame the user stepped back to', async () => {
        render(Flamegraph);
        await screen.findByText('4321');
        await fireEvent.click(screen.getByTestId('step-older'));
        await screen.findByText('4320');
        await waitFor(() =>
            expect(screen.getByTestId('frame-span')).toHaveTextContent('over 1 frames'),
        );
    });

    it('rules the flame canvas with microsecond marks', async () => {
        render(Flamegraph);
        const ruler = await screen.findByTestId('frame-ruler');
        expect(ruler.children.length).toBe(5);
    });

    it('shows the import error and stays live when the import request fails', async () => {
        mockFetch(FRAMES_BODY, { message: 'boom' }, 500);
        const { getByLabelText } = render(Flamegraph);
        const file = new File(['zip'], 'session.rimobs.zip', { type: 'application/zip' });
        await fireEvent.change(getByLabelText(/open bundle/i), { target: { files: [file] } });

        expect(await screen.findByRole('alert')).toHaveTextContent(/import failed: 500/i);
        expect(screen.queryByTestId('frame-scrub')).toBeNull();
        expect((getByLabelText(/^Source$/i) as HTMLSelectElement).value).toBe('live');
    });
});

describe('Flamegraph clear history', () => {
    it('offers the clear button beside the mode segment', async () => {
        mockFetch();
        render(Flamegraph);

        expect(await screen.findByTestId('clear-ring')).toBeInTheDocument();
    });

    it('posts to the clear endpoint when pressed', async () => {
        mockFetch();
        render(Flamegraph);
        await screen.findByTestId('clear-ring');

        await fireEvent.click(screen.getByTestId('clear-ring'));

        const posted = (globalThis.fetch as unknown as { mock: { calls: unknown[][] } }).mock.calls
            .map((c) => ({ url: requestUrl(c[0] as RequestInfo), init: c[1] as RequestInit }))
            .filter((c) => c.url.includes('/api/v1/frames/clear'));
        expect(posted.length).toBeGreaterThan(0);
        expect(posted[0].init?.method).toBe('POST');
    });

    // a paused view is pinned to a frame the clear is about to delete, so clearing resumes.
    it('resumes a paused view rather than pinning it to deleted history', async () => {
        mockFetch();
        render(Flamegraph);
        await screen.findByTestId('pause');
        await fireEvent.click(screen.getByTestId('pause'));
        expect(screen.getByTestId('paused-badge')).toBeInTheDocument();

        await fireEvent.click(screen.getByTestId('clear-ring'));

        expect(screen.queryByTestId('paused-badge')).toBeNull();
    });
});

// instrumentation and comparison used to be two <details> stacked under the flamegraph, which
// pushed the page down and read as part of the frame. they are footer panels now.
describe('Flamegraph footer panels', () => {
    it('offers instrumentation and comparison as footer tabs', async () => {
        mockFetch();
        render(Flamegraph);

        expect(await screen.findByTestId('tab-instrumentation')).toBeInTheDocument();
        expect(screen.getByTestId('tab-comparison')).toBeInTheDocument();
    });

    it('keeps both panels out of the page until their tab is picked', async () => {
        mockFetch();
        render(Flamegraph);
        await screen.findByTestId('tab-instrumentation');

        expect(screen.queryByTestId('instrumentation-panel')).toBeNull();
        expect(screen.queryByTestId('comparison-panel')).toBeNull();
    });

    it('shows the comparison panel only on its own tab', async () => {
        mockFetch();
        render(Flamegraph);
        await openFooterTab('comparison');

        expect(await screen.findByTestId('comparison-panel')).toBeInTheDocument();
        expect(screen.queryByTestId('instrumentation-panel')).toBeNull();
    });

    // the scope and search controls describe the frame tree, so they have no meaning next to
    // the instrumentation panel.
    it('hides the tree toolbar while a panel tab is open', async () => {
        mockFetch();
        render(Flamegraph);
        await openFooterTab('instrumentation');

        expect(screen.queryByTestId('scope-session')).toBeNull();
        expect(screen.queryByTestId('tree-search')).toBeNull();
    });
});

// "sampled" is judged against the frame on screen, so this count flips several times a second
// while live-following. it used to be wrapped in an {#if}, which mounted and unmounted the cell
// and slid the search box and its buttons out from under the cursor.
describe('Flamegraph search control layout', () => {
    beforeEach(() => {
        sectionSearch.query = '';
        sectionSearch.filterMode = false;
        sectionSearch.nodeCount = 0;
        sectionSearch.unsampledCount = 0;
    });

    it('keeps the not-sampled cell mounted whether or not anything is unsampled', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());

        sectionSearch.query = 'Tick';
        sectionSearch.unsampledCount = 0;
        await waitFor(() => expect(screen.getByTestId('section-search-unsampled')).toBeTruthy());
        const hiddenWhenZero = screen.getByTestId('section-search-unsampled');
        expect(hiddenWhenZero.classList.contains('empty')).toBe(true);

        sectionSearch.unsampledCount = 2;
        await waitFor(() =>
            expect(screen.getByTestId('section-search-unsampled').classList.contains('empty')).toBe(
                false,
            ),
        );
        // same node, never remounted, so nothing beside it moved
        expect(screen.getByTestId('section-search-unsampled')).toBe(hiddenWhenZero);
    });

    it('gives the input a resting affordance so it reads as a field, not a label', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());
        const input = screen.getByTestId('section-search-input');
        expect(input.tagName).toBe('INPUT');
        expect(input.getAttribute('placeholder')).toBeTruthy();
        expect(screen.getByTestId('section-search').querySelector('svg.glass')).toBeTruthy();
    });
});

// each of these pins a rule this control has already broken once. the labels went ambiguous
// because a lone toggle cannot show its other state, and the input read as static text.
describe('Flamegraph search control affordance', () => {
    beforeEach(() => {
        sectionSearch.query = '';
        sectionSearch.scope = 'window';
        sectionSearch.filterMode = false;
    });

    it('gives the input a visible label, not just a placeholder', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());

        const input = screen.getByTestId('section-search-input');
        const label = document.querySelector(`label[for="${input.id}"]`);
        expect(input.id).toBeTruthy();
        expect(label?.textContent?.trim()).toBeTruthy();
    });

    it('shows both scope options so the choice explains itself', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());

        const frame = screen.getByTestId('section-search-scope-frame');
        const window_ = screen.getByTestId('section-search-scope-window');
        expect(frame.textContent?.trim()).toBeTruthy();
        expect(window_.textContent?.trim()).toBeTruthy();
        expect(frame.getAttribute('aria-pressed')).toBe('false');
        expect(window_.getAttribute('aria-pressed')).toBe('true');

        await fireEvent.click(frame);
        await waitFor(() => expect(sectionSearch.scope).toBe('frame'));
        expect(frame.getAttribute('aria-pressed')).toBe('true');
        expect(window_.getAttribute('aria-pressed')).toBe('false');
    });

    it('labels the filter checkbox with what it does', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());

        const box = screen.getByTestId('section-search-filter') as HTMLInputElement;
        expect(box.type).toBe('checkbox');
        expect(box.closest('label')?.textContent?.trim()).toBeTruthy();

        await fireEvent.click(box);
        await waitFor(() => expect(sectionSearch.filterMode).toBe(true));
    });

    it('gives the steppers accessible names since they are icon-only', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());

        expect(screen.getByTestId('section-search-prev').getAttribute('aria-label')).toBeTruthy();
        expect(screen.getByTestId('section-search-next').getAttribute('aria-label')).toBeTruthy();
    });
});

// stepping while live-following lands on a frame the next poll immediately replaces, so a
// step has to pin the frame it found.
describe('Flamegraph search stepping pauses', () => {
    beforeEach(() => {
        sectionSearch.query = '';
        sectionSearch.scope = 'window';
        sectionSearch.occurrenceCount = 0;
    });

    it('pauses on the frame the step landed in', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());
        expect(screen.queryByTestId('paused-badge')).toBeNull();

        sectionSearch.query = 'Tick';
        await waitFor(() => expect(sectionSearch.occurrenceCount).toBeGreaterThan(0));

        await fireEvent.click(screen.getByTestId('section-search-next'));

        await waitFor(() => expect(screen.getByTestId('paused-badge')).toBeTruthy());
    });

    it('leaves the live view alone when there is nothing to step to', async () => {
        render(Flamegraph);
        await waitFor(() => expect(screen.getByRole('application')).toBeInTheDocument());

        sectionSearch.query = 'nothingmatchesthis';
        await waitFor(() => expect(sectionSearch.occurrenceCount).toBe(0));

        expect(screen.getByTestId('section-search-next')).toBeDisabled();
        expect(screen.queryByTestId('paused-badge')).toBeNull();
    });
});

describe('Flamegraph frame history selection', () => {
    it('pins the clicked frame and paints it as the selected bar', async () => {
        const { drawStrip } = await import('../lib/stripDraw');
        render(Flamegraph);
        await waitFor(() => expect(screen.getByText('4321')).toBeInTheDocument());

        const canvas = screen
            .getByTestId('frame-strip')
            .querySelector('canvas') as HTMLCanvasElement;
        canvas.getBoundingClientRect = () =>
            ({ left: 0, top: 0, right: 200, bottom: 100, width: 200, height: 100 }) as DOMRect;

        await fireEvent.click(canvas, { clientX: 0, clientY: 10 });

        await waitFor(() => expect(screen.getByTestId('paused-badge')).toBeInTheDocument());
        await waitFor(() => {
            const calls = vi.mocked(drawStrip).mock.calls;
            expect(calls.at(-1)?.[2].selectedOrdinal).toBe(4319);
        });
    });
});
