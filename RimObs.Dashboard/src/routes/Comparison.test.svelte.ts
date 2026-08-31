import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import Comparison from './Comparison.svelte';

const MANIFEST = {
    schema_version: 1,
    session_id: 'imported-2026-05-20',
    created_utc: '2026-05-28T11:02:00Z',
    collector_version: '0.4.0',
};

const SESSIONS = {
    sessions: [
        { id: 'live-session', is_current: true },
        { id: 'older-session', is_current: false },
    ],
};

function emptyComparison() {
    return {
        disclaimer: 'Deltas indicate correlation, not causation.',
        warnings: [],
        timing: {
            base_total_ns: 1000,
            head_total_ns: 1500,
            delta_ns: 500,
            delta_percent: 50,
            base_mean_ns: 100,
            head_mean_ns: 150,
            delta_mean_ns: 50,
            base_sample_count: 10,
            head_sample_count: 10,
        },
        hotspots: [],
        mod_costs: [],
        metrics: [],
        load_order: { added: [], removed: [], common: [] },
    };
}

function mockFetch() {
    const compareUrls: string[] = [];
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
        const method = init?.method ?? 'GET';
        if (url === '/api/v1/sessions') {
            return { ok: true, status: 200, json: async () => SESSIONS } as Response;
        }
        if (url === '/api/v1/import/bundle' && method === 'POST') {
            return {
                ok: true,
                status: 200,
                json: async () => ({
                    token: 'tok-1',
                    manifest: MANIFEST,
                    contents: ['manifest.json'],
                }),
            } as Response;
        }
        if (url.startsWith('/api/v1/sessions/compare')) {
            compareUrls.push(url);
            return { ok: true, status: 200, json: async () => emptyComparison() } as Response;
        }
        return { ok: true, status: 200, json: async () => ({}) } as Response;
    });
    vi.stubGlobal('fetch', fetchMock);
    return { compareUrls };
}

afterEach(() => vi.unstubAllGlobals());

function importBundle(container: HTMLElement) {
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['zip'], 'bundle.rimobs.zip', { type: 'application/zip' });
    return fireEvent.change(input, { target: { files: [file] } });
}

describe('Comparison route bundle source', () => {
    it('adds an imported bundle as a selectable comparison source', async () => {
        mockFetch();
        const { container } = render(Comparison);

        await importBundle(container);

        const options = await screen.findAllByRole('option', {
            name: /imported-2026-05-20 \(imported\)/i,
        });
        expect(options).toHaveLength(2);
    });

    it('compares an imported bundle against a session', async () => {
        const { compareUrls } = mockFetch();
        const { container } = render(Comparison);

        await importBundle(container);
        await screen.findAllByRole('option', { name: /imported-2026-05-20 \(imported\)/i });

        const selects = container.querySelectorAll('select');
        await fireEvent.change(selects[0], { target: { value: 'bundle:tok-1' } });
        await fireEvent.change(selects[1], { target: { value: 'older-session' } });

        await fireEvent.click(screen.getByRole('button', { name: 'Compare' }));

        expect(compareUrls).toHaveLength(1);
        expect(compareUrls[0]).toContain('base=bundle%3Atok-1');
        expect(compareUrls[0]).toContain('head=older-session');
    });
});
