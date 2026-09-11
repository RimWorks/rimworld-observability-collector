import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import SettingsPopover from './SettingsPopover.svelte';
import { liveConfig } from '../liveConfig.svelte';
import type { StatusResponse } from '../api';

const SESSION = {
    id: 'abc123',
    started_utc: '2026-09-07T20:00:00Z',
    library_version: '1.0.0',
    game_version: '',
    is_current: true,
};

const withCounters = {
    schema_version: 8,
    status: 'running',
    version: '1.2.3',
    session: SESSION,
    receive: {
        last_batch_utc: '2026-09-07T20:01:00Z',
        section_count: 30,
        total_gc_events: 7,
        total_batches: 101,
        total_samples: 10964,
        total_bytes: 279057,
    },
} as unknown as StatusResponse;

const withSession = {
    schema_version: 6,
    status: 'running',
    version: '1.2.3',
    session: SESSION,
    receive: { last_batch_utc: '2026-09-07T20:01:00Z' },
} as unknown as StatusResponse;

function configDoc(over: Record<string, unknown> = {}) {
    return {
        schema_version: 1,
        sampling: { frame_ring_capacity: 3000, max_capture_depth: 8, ring_capacity: 16384 },
        auto_instrument: {
            enabled: false,
            filters: '',
            ignore: '',
            mute_trivial: true,
            max_targets: 8192,
        },
        session: { pending_name: '', prompt_for_name: true },
        ...over,
    };
}

// tests override this to script the auto-instrument activity counters.
let autoStatusBody: (() => Record<string, unknown>) | null = null;

/** Answers /api/v1/config with a real document and records every POST body. */
function mockConfig(doc: Record<string, unknown> = configDoc()) {
    let current = doc;
    autoStatusBody = null;
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : (input as Request).url;
        if (autoStatusBody && url.endsWith('/api/v1/instrumentation/auto')) {
            return new Response(JSON.stringify({ schema_version: 1, auto: autoStatusBody() }), {
                status: 200,
            });
        }
        if (url.includes('/api/v1/instrumentation/assemblies')) {
            return new Response(
                JSON.stringify({
                    schema_version: 1,
                    assemblies: ['Assembly-CSharp', 'Cosmere.Core'],
                }),
                { status: 200 },
            );
        }
        if (url.includes('/api/v1/instrumentation/auto/preview')) {
            return new Response(
                JSON.stringify({
                    schema_version: 1,
                    preview: {
                        matched: 22411,
                        eligible: 12003,
                        skippedTrivial: 9001,
                        skippedIgnored: 700,
                        skippedBlocklisted: 400,
                        skippedAlreadyInstrumented: 200,
                        skippedOverCap: 107,
                    },
                }),
                { status: 200 },
            );
        }
        if (url.includes('/api/v1/config')) {
            if (init?.method === 'POST') current = JSON.parse(init.body as string);
            return new Response(JSON.stringify(current), { status: 200 });
        }
        return new Response('{}', { status: 200 });
    }) as unknown as typeof fetch;
}

function configPosts(): Record<string, never>[] {
    return (globalThis.fetch as unknown as { mock: { calls: unknown[][] } }).mock.calls
        .filter((c) => {
            const url = typeof c[0] === 'string' ? c[0] : (c[0] as Request).url;
            return (
                url.includes('/api/v1/config') &&
                (c[1] as RequestInit | undefined)?.method === 'POST'
            );
        })
        .map((c) => JSON.parse((c[1] as RequestInit).body as string));
}

beforeEach(() => {
    globalThis.fetch = vi.fn(
        () => Promise.resolve(new Response('{}', { status: 200 })) as Promise<Response>,
    );
});

const status = {
    schema_version: 6,
    status: 'running',
    version: '1.2.3',
    session: null,
    receive: null,
    exporters: {
        prometheus_enabled: true,
        prometheus_health: {
            last_scrape_utc: '2026-09-08T13:00:00Z',
            last_sample_count: 42,
            total_errors: 0,
            last_error: null,
        },
    },
} as unknown as StatusResponse;

describe('SettingsPopover', () => {
    it('keeps the panel closed until the gear is clicked', async () => {
        render(SettingsPopover, { status });

        expect(screen.queryByRole('dialog')).toBeNull();

        await fireEvent.click(screen.getByTestId('settings-gear'));

        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());
        expect(screen.getByText('1.2.3')).toBeTruthy();
    });

    it('closes on escape', async () => {
        render(SettingsPopover, { status });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());

        await fireEvent.keyDown(globalThis.document, { key: 'Escape' });

        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    });

    it('closes when a pointer lands outside the panel', async () => {
        render(SettingsPopover, { status });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());

        await fireEvent.pointerDown(globalThis.document.body);

        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    });

    it('stays open when the pointer lands inside the panel', async () => {
        render(SettingsPopover, { status });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        const panel = await screen.findByRole('dialog');

        await fireEvent.pointerDown(panel);

        expect(screen.queryByRole('dialog')).toBeTruthy();
    });

    it('shows the prometheus health block only while the exporter is on', async () => {
        const { unmount } = render(SettingsPopover, { status });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await waitFor(() => expect(screen.getByText('/metrics')).toBeTruthy());
        unmount();

        const off = {
            ...status,
            exporters: { ...status.exporters, prometheus_enabled: false },
        } as unknown as StatusResponse;
        render(SettingsPopover, { status: off });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());

        expect(screen.queryByText('/metrics')).toBeNull();
    });
});

// session details and the bundle export moved here when the Overview page was cut. only the
// current session can be exported, so the gear is the one place that already names it.
describe('SettingsPopover session block', () => {
    async function open(s: StatusResponse) {
        render(SettingsPopover, { status: s });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await screen.findByRole('dialog');
    }

    it('shows the session it would export', async () => {
        await open(withSession);

        expect(screen.getByText('abc123')).toBeInTheDocument();
    });

    it('opens the bundle export form from the gear', async () => {
        await open(withSession);
        expect(screen.queryByText(/optional contents/i)).toBeNull();

        await fireEvent.click(screen.getByTestId('open-export'));

        await waitFor(() => expect(screen.getByText(/optional contents/i)).toBeInTheDocument());
    });

    it('offers no export when there is no session', async () => {
        await open(status);

        expect(screen.queryByTestId('open-export')).toBeNull();
        expect(screen.getByText('No session connected')).toBeInTheDocument();
    });
});

describe('SettingsPopover counters', () => {
    it('carries the counters the header dropped', async () => {
        render(SettingsPopover, { status: withCounters });
        await fireEvent.click(screen.getByLabelText(/settings/i));

        await waitFor(() => expect(screen.getByTestId('kv-batches')).toBeTruthy());
        expect(screen.getByTestId('kv-batches').textContent).toContain('101');
        expect(screen.getByTestId('kv-samples').textContent).toContain('10,964');
        expect(screen.getByTestId('kv-sections').textContent).toContain('30');
        expect(screen.getByTestId('kv-gc').textContent).toContain('7');
        expect(screen.getByTestId('kv-bytes').textContent).toMatch(/KB|B/);
    });

    it('omits the counter rows when the collector reported no receive block', async () => {
        render(SettingsPopover, {
            status: { ...withCounters, receive: null } as unknown as StatusResponse,
        });
        await fireEvent.click(screen.getByLabelText(/settings/i));

        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());
        expect(screen.queryByTestId('kv-batches')).toBeNull();
    });
});

describe('SettingsPopover session naming', () => {
    function mockSessions(body: unknown) {
        globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = typeof input === 'string' ? input : (input as Request).url;
            if (url.includes('/api/v1/sessions') && init?.method !== 'POST') {
                return new Response(JSON.stringify(body), { status: 200 });
            }
            return new Response('{}', { status: 200 });
        }) as unknown as typeof fetch;
    }

    it('offers a name field for the current session', async () => {
        mockSessions({ sessions: [{ id: 'abc123', name: 'Colony A', is_current: true }] });
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByLabelText(/settings/i));

        const field = (await screen.findByTestId('session-name')) as HTMLInputElement;
        expect(field.value).toBe('Colony A');
    });

    it('posts a rename when the field changes', async () => {
        mockSessions({ sessions: [{ id: 'abc123', name: '', is_current: true }] });
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByLabelText(/settings/i));
        const field = await screen.findByTestId('session-name');

        await fireEvent.change(field, { target: { value: 'Before mods' } });

        const posts = (
            globalThis.fetch as unknown as { mock: { calls: unknown[][] } }
        ).mock.calls.filter((c) => (c[1] as RequestInit | undefined)?.method === 'POST');
        expect(posts.length).toBeGreaterThan(0);
        expect(JSON.parse((posts[0][1] as RequestInit).body as string)).toEqual({
            name: 'Before mods',
        });
    });

    it('lists past sessions so they can be renamed too', async () => {
        mockSessions({
            sessions: [
                { id: 'abc123', name: '', is_current: true },
                { id: 'old-1', name: 'Yesterday', is_current: false },
            ],
        });
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByLabelText(/settings/i));

        const rows = await screen.findAllByTestId('past-session-name');
        expect(rows).toHaveLength(1);
        expect((rows[0] as HTMLInputElement).value).toBe('Yesterday');
    });

    // a body with no sessions array used to assign undefined and crash the whole popover.
    it('survives a sessions response with no sessions array', async () => {
        mockSessions({});
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByLabelText(/settings/i));

        expect(await screen.findByTestId('session-name')).toBeTruthy();
        expect(screen.queryByTestId('past-sessions')).toBeNull();
    });
});

// every one of these controls writes straight to /api/v1/config, which is the only path the
// game-side library reads. a control that renders but never posts is a setting that silently
// does nothing.
// the counters are the only signal that auto-instrumentation actually did anything, so the
// pane has to show them once the collector reports auto_instrument.enabled.
describe('SettingsPopover auto-instrument status', () => {
    function mockAuto(auto: Record<string, number | boolean>) {
        globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
            const url = typeof input === 'string' ? input : (input as Request).url;
            if (url.includes('/api/v1/instrumentation/auto')) {
                return new Response(JSON.stringify({ schema_version: 1, auto }), { status: 200 });
            }
            if (url.includes('/api/v1/config')) {
                return new Response(
                    JSON.stringify(
                        configDoc({
                            auto_instrument: {
                                enabled: true,
                                filters: '',
                                ignore: '',
                                mute_trivial: true,
                            },
                        }),
                    ),
                    { status: 200 },
                );
            }
            return new Response('{}', { status: 200 });
        }) as unknown as typeof fetch;
    }

    it('shows the runner counters once auto-instrumentation is on', async () => {
        mockAuto({
            matched: 10,
            instrumented: 4,
            muted: 1,
            skippedTrivial: 3,
            skippedOther: 2,
            refused: 0,
            pending: 0,
        });
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));

        await waitFor(() => expect(screen.getByTestId('auto-matched')).toBeTruthy());
        expect(screen.getByTestId('auto-matched').textContent).toContain('10');
        expect(screen.getByTestId('auto-instrumented').textContent).toContain('4');
    });

    // a wide filter loses everything past the cap, and folding that into "other" hides it.
    it('names the cap when the applied scan was truncated', async () => {
        mockAuto({
            matched: 60000,
            instrumented: 8192,
            muted: 0,
            skippedTrivial: 0,
            skippedOther: 51808,
            skippedOverCap: 51808,
            maxTargets: 8192,
            truncated: true,
            refused: 0,
            pending: 0,
        });
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));

        await waitFor(() => expect(screen.getByTestId('auto-truncated')).toBeTruthy());
        expect(screen.getByTestId('auto-skipped-over-cap').textContent).toContain('51,808');
        expect(screen.getByTestId('auto-truncated').textContent).toContain('8,192');
    });

    it('hides the truncation line when nothing hit the cap', async () => {
        mockAuto({
            matched: 10,
            instrumented: 4,
            muted: 1,
            skippedTrivial: 3,
            skippedOther: 2,
            skippedOverCap: 0,
            maxTargets: 8192,
            truncated: false,
            refused: 0,
            pending: 0,
        });
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));

        await waitFor(() => expect(screen.getByTestId('auto-matched')).toBeTruthy());
        expect(screen.queryByTestId('auto-truncated')).toBeNull();
        expect(screen.queryByTestId('auto-skipped-over-cap')).toBeNull();
    });

    it('omits the counters block when the collector cannot reach the game', async () => {
        globalThis.fetch = vi.fn(
            async () => new Response('', { status: 503 }),
        ) as unknown as typeof fetch;
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));

        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());
        expect(screen.queryByTestId('auto-matched')).toBeNull();
    });
});

describe('SettingsPopover profiling controls', () => {
    async function open(doc = configDoc()) {
        mockConfig(doc);
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await screen.findByRole('dialog');
        await waitFor(() =>
            expect((screen.getByTestId('max-depth') as HTMLInputElement).value).not.toBe(''),
        );
    }

    it('shows the capture depth the collector reported', async () => {
        await open(configDoc({ sampling: { frame_ring_capacity: 3000, max_capture_depth: 12 } }));

        expect((screen.getByTestId('max-depth') as HTMLInputElement).value).toBe('12');
    });

    it('posts a new capture depth', async () => {
        await open();

        await fireEvent.change(screen.getByTestId('max-depth'), { target: { value: '20' } });

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ sampling: { max_capture_depth: 20 } });
    });

    it('clamps a capture depth past the library stack before posting', async () => {
        await open();

        await fireEvent.change(screen.getByTestId('max-depth'), { target: { value: '900' } });

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ sampling: { max_capture_depth: 64 } });
    });

    it('posts the auto-instrument toggle', async () => {
        await open();

        await fireEvent.click(screen.getByTestId('auto-instrument'));

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ auto_instrument: { enabled: true } });
    });

    it('keeps the filters box disabled until auto-instrumentation is on', async () => {
        await open();

        expect((screen.getByTestId('auto-filters') as HTMLTextAreaElement).disabled).toBe(true);
    });

    // typing used to write straight to the live collector, so a filter as wide as
    // Assembly-CSharp!* reached the game before you could see it matched 22,000 methods.
    it('writes nothing to the collector while you type a filter', async () => {
        await open(
            configDoc({
                auto_instrument: { enabled: true, filters: '', ignore: '', mute_trivial: true },
            }),
        );

        await fireEvent.input(screen.getByTestId('auto-filters'), {
            target: { value: 'Assembly-CSharp!Verse.Map::*' },
        });
        await fireEvent.blur(screen.getByTestId('auto-filters'));

        expect(configPosts()).toHaveLength(0);
    });

    it('counts the match on blur without patching anything', async () => {
        await open(
            configDoc({
                auto_instrument: { enabled: true, filters: '', ignore: '', mute_trivial: true },
            }),
        );

        await fireEvent.input(screen.getByTestId('auto-filters'), {
            target: { value: 'Assembly-CSharp!Verse.Map::*' },
        });
        await fireEvent.blur(screen.getByTestId('auto-filters'));

        await waitFor(() =>
            expect(
                vi
                    .mocked(globalThis.fetch)
                    .mock.calls.filter((c) =>
                        String(c[0]).includes('/api/v1/instrumentation/auto/preview'),
                    ),
            ).not.toHaveLength(0),
        );
        expect(configPosts()).toHaveLength(0);
    });

    // the first cut of this read preview.Eligible while the collector sends camelCase, so the
    // line rendered blank. asserting the fetch fired was not enough; assert the number lands.
    it('renders the counted number, not just the request', async () => {
        await open(
            configDoc({
                auto_instrument: { enabled: true, filters: '', ignore: '', mute_trivial: true },
            }),
        );

        await fireEvent.input(screen.getByTestId('auto-filters'), {
            target: { value: 'Assembly-CSharp!*' },
        });
        await fireEvent.blur(screen.getByTestId('auto-filters'));

        const line = await screen.findByTestId('auto-preview');
        await waitFor(() => expect(line.textContent).toContain('12,003'));
        expect(line.textContent).toContain('22,411');
    });

    it('marks a wide filter as the expensive one', async () => {
        await open(
            configDoc({
                auto_instrument: { enabled: true, filters: '', ignore: '', mute_trivial: true },
            }),
        );

        await fireEvent.input(screen.getByTestId('auto-filters'), {
            target: { value: 'Assembly-CSharp!*' },
        });
        await fireEvent.blur(screen.getByTestId('auto-filters'));

        const line = await screen.findByTestId('auto-preview');
        await waitFor(() => expect(line.classList.contains('bad')).toBe(true));
        expect(line.textContent).toContain('stall loading');
    });

    it('posts the one box with its negated lines and clears the legacy ignore', async () => {
        await open(
            configDoc({
                auto_instrument: { enabled: true, filters: '', ignore: '', mute_trivial: true },
            }),
        );

        await fireEvent.input(screen.getByTestId('auto-filters'), {
            target: { value: 'Assembly-CSharp!Verse.Map::*\n!Assembly-CSharp!Verse.Log::*' },
        });
        await fireEvent.click(screen.getByTestId('auto-apply'));

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({
            auto_instrument: {
                filters: 'Assembly-CSharp!Verse.Map::*\n!Assembly-CSharp!Verse.Log::*',
                ignore: '',
            },
        });
    });

    // an empty list on the collector is seeded with a suggested default, which legitimately
    // leaves Apply live. both lists are set here so nothing is seeded and clean means clean.
    it('leaves Apply disabled until the filters differ from what is applied', async () => {
        await open(
            configDoc({
                auto_instrument: {
                    enabled: true,
                    filters: 'Assembly-CSharp!Verse.Map::*',
                    ignore: 'Assembly-CSharp!Verse.Log::*',
                    mute_trivial: true,
                },
            }),
        );

        expect(screen.getByTestId('auto-apply')).toBeDisabled();

        await fireEvent.input(screen.getByTestId('auto-filters'), {
            target: { value: 'Assembly-CSharp!*' },
        });

        await waitFor(() => expect(screen.getByTestId('auto-apply')).not.toBeDisabled());
    });

    it('posts the auto-mute toggle', async () => {
        await open(
            configDoc({
                auto_instrument: { enabled: true, filters: '', ignore: '', mute_trivial: true },
            }),
        );

        await fireEvent.click(screen.getByTestId('auto-mute-trivial'));

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ auto_instrument: { mute_trivial: false } });
    });

    it('posts the frame buffer and clamps it', async () => {
        await open();

        await fireEvent.change(screen.getByTestId('ring-capacity'), { target: { value: '99999' } });

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ sampling: { frame_ring_capacity: 20000 } });
    });

    it('posts the sample ring rounded up to a power of two', async () => {
        await open();

        await fireEvent.change(screen.getByTestId('sample-ring'), { target: { value: '70000' } });

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ sampling: { ring_capacity: 131072 } });
    });

    it('posts the scan cap and floors it at one', async () => {
        await open();

        await fireEvent.change(screen.getByTestId('max-targets'), { target: { value: '0' } });

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ auto_instrument: { max_targets: 1 } });
    });

    it('posts the session naming prompt toggle', async () => {
        await open();

        await fireEvent.click(screen.getByTestId('prompt-for-name'));

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ session: { prompt_for_name: false } });
    });

    it('keeps unknown config keys the collector sent', async () => {
        await open(configDoc({ storage: { sqlite_journal_mode: 'WAL' } }));

        await fireEvent.change(screen.getByTestId('max-depth'), { target: { value: '4' } });

        await waitFor(() => expect(configPosts()).toHaveLength(1));
        expect(configPosts()[0]).toMatchObject({ storage: { sqlite_journal_mode: 'WAL' } });
    });

    it('disables every config control when the collector answers with nothing usable', async () => {
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await screen.findByRole('dialog');

        await waitFor(() =>
            expect((screen.getByTestId('max-depth') as HTMLInputElement).disabled).toBe(true),
        );
        expect((screen.getByTestId('auto-instrument') as HTMLInputElement).disabled).toBe(true);
    });

    it('reports a failed save instead of pretending it landed', async () => {
        await open();
        globalThis.fetch = vi.fn(
            async () => new Response('nope', { status: 500, statusText: 'Server Error' }),
        ) as unknown as typeof fetch;

        await fireEvent.change(screen.getByTestId('max-depth'), { target: { value: '4' } });

        await waitFor(() => expect(screen.getByTestId('config-error')).toBeTruthy());
    });
});

// the flamegraph strip sizes its slots from the ring capacity and read it once at mount, so
// shrinking the ring left the bars filling a fraction of the panel until a page reload.
describe('SettingsPopover ring capacity publishing', () => {
    async function open(doc = configDoc()) {
        mockConfig(doc);
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await screen.findByRole('dialog');
        await waitFor(() =>
            expect((screen.getByTestId('ring-capacity') as HTMLInputElement).value).not.toBe(''),
        );
    }

    beforeEach(() => liveConfig.setRingCapacity(null));

    it('publishes the saved ring capacity so the strip can resize', async () => {
        await open();

        await fireEvent.change(screen.getByTestId('ring-capacity'), { target: { value: '500' } });

        await waitFor(() => expect(liveConfig.ringCapacity).toBe(500));
    });
});

// accepting a suggestion removes the clicked button from the panel before the popover's
// outside-click check ran contains(), so the whole settings pane snapped shut.
describe('SettingsPopover autocomplete', () => {
    it('stays open when an autocomplete suggestion is clicked', async () => {
        mockConfig(
            configDoc({
                auto_instrument: {
                    enabled: true,
                    filters: '',
                    ignore: '',
                    mute_trivial: false,
                    max_targets: 8192,
                },
            }),
        );
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));

        const box = await screen.findByTestId('auto-filters');
        await fireEvent.input(box, { target: { value: 'cos' } });
        await waitFor(() => expect(screen.getByTestId('pattern-suggestions')).toBeInTheDocument());

        const btn = screen.getAllByRole('option')[0].querySelector('button')!;
        btn.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, cancelable: true }));

        // the dropdown closing is the flush marker; the pane itself must survive it.
        await waitFor(() => expect(screen.queryByTestId('pattern-suggestions')).toBeNull());
        expect(screen.queryByTestId('auto-filters')).not.toBeNull();
    });
});

// the patch pump drains a big filter change over minutes, and nothing said an apply was
// still landing: the old sections kept sampling and the change looked ignored.
describe('SettingsPopover apply progress', () => {
    it('shows the pending count after apply and clears it when the pump drains', async () => {
        mockConfig(
            configDoc({
                auto_instrument: { enabled: true, filters: '', ignore: '', mute_trivial: true },
            }),
        );
        let pending = 120;
        autoStatusBody = () => ({
            matched: 500,
            instrumented: 380,
            muted: 0,
            skippedTrivial: 0,
            skippedOther: 0,
            refused: 0,
            pending,
            skippedOverCap: 0,
            maxTargets: 24000,
            truncated: false,
        });
        render(SettingsPopover, { status: withSession });
        await fireEvent.click(screen.getByTestId('settings-gear'));

        await fireEvent.input(await screen.findByTestId('auto-filters'), {
            target: { value: 'Assembly-CSharp!Verse.Map::*' },
        });
        await fireEvent.click(screen.getByTestId('auto-apply'));

        await waitFor(() =>
            expect(screen.getByTestId('auto-applying').textContent).toContain('120'),
        );

        pending = 0;
        await waitFor(() => expect(screen.queryByTestId('auto-applying')).toBeNull(), {
            timeout: 4000,
        });
    });
});
