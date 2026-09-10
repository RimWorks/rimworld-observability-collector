import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import SettingsPopover from './SettingsPopover.svelte';
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
