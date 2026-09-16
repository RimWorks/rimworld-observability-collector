import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import CallTreePanel from './CallTreePanel.svelte';
import { NO_PARENT, type TreeNode } from '../frameTree';

const names = new Map([
    [10, { name: 'Cheap', subsystem: null }],
    [20, { name: 'Greedy', subsystem: null }],
]);

const nodes: TreeNode[] = [
    {
        sectionId: 10,
        nodeId: 1,
        parentIndex: NO_PARENT,
        depth: 0,
        startUs: 0,
        durUs: 900,
        endUs: 900,
        allocBytes: 0,
    },
    {
        sectionId: 20,
        nodeId: 2,
        parentIndex: NO_PARENT,
        depth: 0,
        startUs: 900,
        durUs: 100,
        endUs: 1000,
        allocBytes: 4096,
    },
];

describe('CallTreePanel alloc column', () => {
    it('shows bytes for a row that allocated and a dash for one that did not', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const cells = screen.getAllByTestId('tree-alloc').map((c) => c.textContent?.trim());
        // sorted by total time, so the cheap-but-long row comes first.
        expect(cells).toEqual(['—', '4.0 KB']);
    });

    it('the alloc tab reorders the same tree by bytes', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });

        await fireEvent.click(screen.getByTestId('tab-alloc'));

        expect(screen.queryByTestId('tab-soon')).toBeNull();
        const labels = screen
            .getAllByTestId('tree-row')
            .map((r) => r.querySelector('.label')?.textContent?.trim());
        expect(labels).toEqual(['Greedy', 'Cheap']);
    });
});

describe('CallTreePanel aria state', () => {
    it('marks the selected scope with aria-pressed and moves it on a click', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        expect(screen.getByTestId('scope-frame')).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByTestId('scope-session')).toHaveAttribute('aria-pressed', 'false');

        await fireEvent.click(screen.getByTestId('scope-session'));

        expect(screen.getByTestId('scope-frame')).toHaveAttribute('aria-pressed', 'false');
        expect(screen.getByTestId('scope-session')).toHaveAttribute('aria-pressed', 'true');
    });

    it('puts aria-sort on the sorted column only, and flips it on a second click', async () => {
        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-tree'));

        const selfTh = screen.getByTestId('sort-self').closest('th')!;
        const allocTh = screen.getByTestId('sort-alloc').closest('th')!;
        expect(selfTh).toHaveAttribute('aria-sort', 'none');

        await fireEvent.click(screen.getByTestId('sort-self'));
        expect(selfTh).toHaveAttribute('aria-sort', 'descending');
        expect(allocTh).toHaveAttribute('aria-sort', 'none');

        await fireEvent.click(screen.getByTestId('sort-self'));
        expect(selfTh).toHaveAttribute('aria-sort', 'ascending');
    });
});

// the vram tab polls its own endpoint; the panel renders totals and the top table.
describe('CallTreePanel vram tab', () => {
    it('renders the census once the collector has one', async () => {
        globalThis.fetch = vi.fn(() =>
            Promise.resolve(
                new Response(
                    JSON.stringify({
                        schema_version: 10,
                        collected: true,
                        driver_bytes: 1_500_000_000,
                        texture_bytes: 900_000_000,
                        mesh_bytes: 200_000_000,
                        render_target_bytes: 150_000_000,
                        texture_count: 18204,
                        mesh_count: 3311,
                        render_target_count: 41,
                        top: [{ name: 'TerrainAtlas', kind: 0, bytes: 268_435_456 }],
                        history: [],
                    }),
                    { status: 200, headers: { 'content-type': 'application/json' } },
                ),
            ),
        ) as unknown as typeof fetch;

        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-vram'));

        await waitFor(() => expect(screen.getByTestId('vram-view')).toBeTruthy());
        expect(screen.getByTestId('vram-driver').textContent).toContain('1.40 GB');
        const row = screen.getAllByTestId('vram-top-row')[0];
        expect(row.textContent).toContain('TerrainAtlas');
        expect(row.textContent).toContain('Textures');
        expect(row.textContent).toContain('256.0 MB');
    });

    it('says the census has not landed yet instead of showing zeros', async () => {
        globalThis.fetch = vi.fn(() =>
            Promise.resolve(
                new Response(JSON.stringify({ schema_version: 10, collected: false }), {
                    status: 200,
                    headers: { 'content-type': 'application/json' },
                }),
            ),
        ) as unknown as typeof fetch;

        render(CallTreePanel, { nodes, names, frameDurationUs: 1000 });
        await fireEvent.click(screen.getByTestId('tab-vram'));

        await waitFor(() => expect(screen.getByTestId('vram-waiting')).toBeTruthy());
    });
});
