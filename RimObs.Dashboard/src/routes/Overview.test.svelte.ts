import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest';
import { render, screen, fireEvent, cleanup, waitFor } from '@testing-library/svelte';
import Overview from './Overview.svelte';
import type { StatusResponse } from '../lib/api';

const STATUS = {
    schema_version: 6,
    status: 'running',
    version: '1.0.0',
    session: {
        id: 'abc123',
        started_utc: '2026-09-07T20:00:00Z',
        library_version: '1.0.0',
        game_version: '',
        is_current: true,
    },
    receive: {
        total_batches: 10,
        total_samples: 20,
        total_bytes: 30,
        last_batch_utc: '2026-09-07T20:01:00Z',
        tps: 60,
        fps: 144,
        section_count: 29,
        total_gc_events: 3,
        total_allocations: 0,
    },
} as unknown as StatusResponse;

beforeEach(() => {
    globalThis.fetch = vi.fn(
        () => Promise.resolve(new Response('{}', { status: 200 })) as Promise<Response>,
    );
});
afterEach(() => cleanup());

describe('Overview', () => {
    // export moved here when the Bundle page was cut; only the current session can be
    // exported, so it belongs on the card that already names it.
    it('opens the bundle export form from the session card', async () => {
        render(Overview, { status: STATUS });
        expect(screen.queryByText(/optional contents/i)).toBeNull();

        await fireEvent.click(screen.getByTestId('open-export'));

        await waitFor(() => expect(screen.getByText(/optional contents/i)).toBeInTheDocument());
    });

    it('shows the session it would export', () => {
        render(Overview, { status: STATUS });
        expect(screen.getByText('abc123')).toBeInTheDocument();
    });

    it('offers no export when there is no session', () => {
        render(Overview, { status: { ...STATUS, session: null } as unknown as StatusResponse });
        expect(screen.queryByTestId('open-export')).toBeNull();
    });
});
