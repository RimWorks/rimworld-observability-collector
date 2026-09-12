import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import ComparisonPanel from './ComparisonPanel.svelte';
import { t } from '../i18n';
import { sessionsStore } from '../sessions.svelte';
import type { ComparisonResponse } from '../api';

const SESSIONS = {
    sessions: [
        { id: 'live-session', is_current: true },
        { id: 'older-session', is_current: false },
    ],
};

function emptyComparison() {
    return {
        disclaimer: 'A delta shows what changed, not why.',
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
    const fetchMock = vi.fn(async (url: string) => {
        if (url === '/api/v1/sessions') {
            return { ok: true, status: 200, json: async () => SESSIONS } as Response;
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

afterEach(() => sessionsStore.reset());
afterEach(() => vi.unstubAllGlobals());

const IMPORTS = [{ value: 'bundle:tok-1', label: 'imported-2026-05-20' }];

// the panel no longer imports anything itself. the flamegraph owns the one import and hands
// the results down, so both features share a single uploaded bundle.
describe('ComparisonPanel', () => {
    it('has no bundle import of its own', async () => {
        mockFetch();
        const { container } = render(ComparisonPanel, { imports: IMPORTS });
        await screen.findAllByRole('option', { name: 'imported-2026-05-20' });

        expect(container.querySelector('input[type="file"]')).toBeNull();
    });

    it('offers every handed-in bundle as a source in both pickers', async () => {
        mockFetch();
        render(ComparisonPanel, { imports: IMPORTS });

        const options = await screen.findAllByRole('option', { name: 'imported-2026-05-20' });
        expect(options).toHaveLength(2);
    });

    it('compares an imported bundle against a session', async () => {
        const { compareUrls } = mockFetch();
        const { container } = render(ComparisonPanel, { imports: IMPORTS });
        await screen.findAllByRole('option', { name: 'imported-2026-05-20' });
        // sessions arrive from the shared store, a tick later than the imports prop
        await screen.findAllByRole('option', { name: /older-session/ });

        const selects = container.querySelectorAll('select');
        await fireEvent.change(selects[0], { target: { value: 'bundle:tok-1' } });
        await fireEvent.change(selects[1], { target: { value: 'older-session' } });
        await fireEvent.click(screen.getByRole('button', { name: 'Compare' }));

        await waitFor(() => expect(compareUrls).toHaveLength(1));
        expect(compareUrls[0]).toContain('base=bundle%3Atok-1');
        expect(compareUrls[0]).toContain('head=older-session');
    });

    // the flamegraph turns the result into the session-scope delta column, so it has to arrive.
    it('hands the result to onResult, and null when cleared', async () => {
        mockFetch();
        const seen: (ComparisonResponse | null)[] = [];
        render(ComparisonPanel, { imports: IMPORTS, onResult: (r) => seen.push(r) });
        await screen.findAllByRole('option', { name: 'imported-2026-05-20' });
        // the default base and head come from the sessions the store loads
        await screen.findAllByRole('option', { name: /older-session/ });

        await fireEvent.click(screen.getByRole('button', { name: 'Compare' }));
        await waitFor(() => expect(seen).toHaveLength(1));
        expect(seen[0]?.timing.delta_ns).toBe(500);

        await fireEvent.click(await screen.findByTestId('clear-compare'));
        expect(seen[1]).toBeNull();
    });

    // the disclaimer used to sit in the page body as a standing paragraph, pushing the
    // results down every time. it now lives in a tooltip off an info icon in the header.
    it('does not render the disclaimer as a body paragraph', async () => {
        mockFetch();
        render(ComparisonPanel, { imports: IMPORTS });
        await screen.findAllByRole('option', { name: 'imported-2026-05-20' });

        await fireEvent.click(screen.getByRole('button', { name: 'Compare' }));
        await waitFor(() =>
            expect(screen.queryByText(/a delta shows what changed, not why/i)).not.toBeInTheDocument(),
        );
    });

    it('shows an info icon in the panel header, labelled for screen readers', async () => {
        mockFetch();
        render(ComparisonPanel, { imports: IMPORTS });

        const trigger = screen.getByRole('img', { name: 'What comparisons mean' });
        expect(trigger).toBeInTheDocument();
        expect(trigger.closest('[tabindex]')?.getAttribute('tabindex')).toBe('0');
    });

    it('reveals the disclaimer text in a tooltip on focus of the info icon', async () => {
        mockFetch();
        render(ComparisonPanel, { imports: IMPORTS });

        const trigger = screen.getByRole('img', { name: 'What comparisons mean' });
        expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();

        await fireEvent.focus(trigger.closest('[tabindex]') ?? trigger);
        const tooltip = await screen.findByRole('tooltip');
        expect(tooltip.textContent).toMatch(/a delta shows what changed, not why/i);
    });
});

// the card under "Compare sessions" carried standing help as a body paragraph. it belongs with
// the disclaimer, behind the header's info icon, not taking vertical space on every render.
describe('ComparisonPanel standing help', () => {
    it('does not render the import hint as a body paragraph', async () => {
        mockFetch();
        const { container } = render(ComparisonPanel, { imports: [] });
        // wait for the picker, not the card title: the title renders even on the error branch,
        // so asserting against it would pass without the body ever rendering.
        await waitFor(() => expect(container.querySelector('.picker')).toBeTruthy());

        const paragraphs = [...container.querySelectorAll('p')].map((p) => p.textContent);
        expect(paragraphs).not.toContain(t('comparison.importHint'));
    });

    it('carries the import hint in the header tooltip', async () => {
        mockFetch();
        render(ComparisonPanel, { imports: [] });
        const trigger = await screen.findByLabelText(t('comparison.pickInfo'));

        await fireEvent.focus(trigger.closest('[tabindex]') ?? trigger);

        const tip = await screen.findByRole('tooltip');
        expect(tip.textContent).toContain(t('comparison.importHint'));
        expect(tip.textContent).toContain(t('comparison.disclaimer'));
    });
});

// the gear renames, the compare pickers read, and they are different components. before the
// shared store the pickers kept whatever names they loaded on mount, so a rename never showed.
describe('ComparisonPanel session names', () => {
    it('labels a session by its name instead of its id', async () => {
        mockFetch();
        sessionsStore.items = [
            { id: '0196cedfc1f3', name: 'Late game 12x', is_current: true },
            { id: 'older-session', name: '', is_current: false },
        ] as never;
        render(ComparisonPanel, { imports: [] });

        expect(await screen.findAllByRole('option', { name: /Late game 12x/ })).not.toHaveLength(0);
        expect(screen.queryAllByRole('option', { name: /^0196cedfc1f3/ })).toHaveLength(0);
    });

    it('still shows the id for a session with no name', async () => {
        mockFetch();
        sessionsStore.items = [{ id: 'older-session', name: '', is_current: false }] as never;
        render(ComparisonPanel, { imports: [] });

        expect(await screen.findAllByRole('option', { name: /older-session/ })).not.toHaveLength(0);
    });
});
