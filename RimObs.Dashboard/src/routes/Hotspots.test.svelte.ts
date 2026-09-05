import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import Hotspots from './Hotspots.svelte';

function mockApi() {
    vi.stubGlobal(
        'fetch',
        vi.fn(async (url: string) => {
            if (typeof url === 'string' && url.includes('/timeseries')) {
                // Empty points hit the no-data branch, avoiding a real uPlot/canvas
                // render which jsdom cannot back (matches the Memory chart test).
                return {
                    ok: true,
                    status: 200,
                    json: async () => ({
                        schema_version: 1,
                        id: 1,
                        name: 'Alpha',
                        bucket_seconds: 1,
                        points: [],
                    }),
                };
            }
            return {
                ok: true,
                status: 200,
                json: async () => ({
                    schema_version: 1,
                    hotspots: [
                        {
                            id: 1,
                            name: 'Alpha',
                            sample_count: 10,
                            total_ns: 1000,
                            mean_ns: 100,
                            min_ns: 50,
                            max_ns: 200,
                            p50_ns: 95,
                            p95_ns: 180,
                            p99_ns: 195,
                        },
                        {
                            id: 2,
                            name: 'Beta',
                            sample_count: 5,
                            total_ns: 250,
                            mean_ns: 50,
                            min_ns: 10,
                            max_ns: 80,
                            p50_ns: 48,
                            p95_ns: 75,
                            p99_ns: 79,
                        },
                    ],
                }),
            };
        }),
    );
}

afterEach(() => vi.unstubAllGlobals());

describe('Hotspots route', () => {
    it('renders a row per hotspot and grades the share bar by total share', async () => {
        mockApi();
        const { container } = render(Hotspots);

        expect(await screen.findByText('Alpha')).toBeInTheDocument();
        expect(screen.getByText('Beta')).toBeInTheDocument();

        // Alpha holds the max total_ns -> share 1.0 -> top grade
        expect(container.querySelector('.fill.g4')).not.toBeNull();
        // Beta share 0.25 -> grade 3
        expect(container.querySelector('.fill.g3')).not.toBeNull();
    });

    it('expands a clicked row and renders its trend panel', async () => {
        mockApi();
        const { container } = render(Hotspots);

        const firstRow = await screen.findByText('Alpha');
        const button = firstRow.closest('button');
        expect(button).not.toBeNull();

        await fireEvent.click(button!);

        // min/max move into the expanded detail panel
        expect(await screen.findByText(/min 50 ns/)).toBeInTheDocument();
        expect(button!.getAttribute('aria-expanded')).toBe('true');
        expect(container.querySelector('.trend')).not.toBeNull();
    });

    // Regression: the trend chart previously referenced `--accent` / `--accent-soft`,
    // which are not declared on :root in theme.css, so getComputedStyle resolved them
    // to "" and uPlot drew an invisible line. Lock the contract: every css-var token
    // the trend series passes to LineChart must be a globally-defined token.
    it('uses chart stroke/fill tokens that are defined in theme.css', () => {
        const here = dirname(fileURLToPath(import.meta.url));
        const themeCss = readFileSync(resolve(here, '../lib/theme.css'), 'utf-8');
        const hotspotsSrc = readFileSync(resolve(here, './Hotspots.svelte'), 'utf-8');

        const rootBlock = /:root\s*\{([\s\S]*?)\}/.exec(themeCss);
        expect(rootBlock, ':root block in theme.css').not.toBeNull();
        const definedTokens = new Set<string>();
        for (const m of rootBlock![1].matchAll(/(--[a-z0-9]+(?:-[a-z0-9]+)*)\s*:/gi)) {
            definedTokens.add(m[1]);
        }

        const tokenPattern = /(?:stroke|fill):\s*'(--[a-z0-9-]+)'/g;
        const usedTokens = [...hotspotsSrc.matchAll(tokenPattern)].map((m) => m[1]);
        expect(usedTokens.length, 'Hotspots references at least one chart css-var').toBeGreaterThan(
            0,
        );
        for (const token of usedTokens) {
            expect(definedTokens, `${token} must be defined on :root in theme.css`).toContain(
                token,
            );
        }
    });
});

function hotspot(id: number, name: string, subsystem: string | null, totalNs: number) {
    return {
        id,
        name,
        subsystem,
        sample_count: 10,
        total_ns: totalNs,
        mean_ns: totalNs / 10,
        min_ns: 1,
        max_ns: totalNs,
        p50_ns: 1,
        p95_ns: 2,
        p99_ns: 3,
    };
}

function mockHotspots(hotspots: unknown[]) {
    vi.stubGlobal(
        'fetch',
        vi.fn(async () => ({
            ok: true,
            status: 200,
            json: async () => ({ schema_version: 1, hotspots }),
        })),
    );
}

// frame sections and tick sections share one ranked list, so the subsystem is the only
// thing telling a reader which clock a row is on.
describe('Hotspots subsystem filter', () => {
    it('renders a subsystem badge on each row', async () => {
        globalThis.history.replaceState({}, '', '/');
        mockHotspots([
            hotspot(1, 'Verse.TickList.Tick', 'tick', 5000),
            hotspot(2, 'Verse.Root_Play.Update', 'render', 9000),
        ]);
        const { container } = render(Hotspots);

        await screen.findByText('Verse.TickList.Tick');

        const badges = [...container.querySelectorAll('.rowline .sub')].map((e) => e.textContent);
        expect(badges).toEqual(['tick', 'render']);
    });

    it('filters rows to one subsystem when its chip is clicked', async () => {
        globalThis.history.replaceState({}, '', '/');
        mockHotspots([
            hotspot(1, 'Verse.TickList.Tick', 'tick', 5000),
            hotspot(2, 'Verse.Root_Play.Update', 'render', 9000),
        ]);
        render(Hotspots);

        await screen.findByText('Verse.TickList.Tick');
        await fireEvent.click(screen.getByRole('button', { name: 'tick' }));

        expect(screen.getByText('Verse.TickList.Tick')).toBeInTheDocument();
        expect(screen.queryByText('Verse.Root_Play.Update')).not.toBeInTheDocument();
    });

    it('initializes the filter from ?subsystem= URL param', async () => {
        globalThis.history.replaceState({}, '', '/?subsystem=render');
        mockHotspots([
            hotspot(1, 'Verse.TickList.Tick', 'tick', 5000),
            hotspot(2, 'Verse.Root_Play.Update', 'render', 9000),
        ]);
        render(Hotspots);

        expect(await screen.findByText('Verse.Root_Play.Update')).toBeInTheDocument();
        expect(screen.queryByText('Verse.TickList.Tick')).not.toBeInTheDocument();
    });

    it('writes the filter to the URL when a chip is clicked', async () => {
        globalThis.history.replaceState({}, '', '/');
        mockHotspots([
            hotspot(1, 'Verse.TickList.Tick', 'tick', 5000),
            hotspot(2, 'Verse.Root_Play.Update', 'render', 9000),
        ]);
        render(Hotspots);

        await screen.findByText('Verse.TickList.Tick');
        await fireEvent.click(screen.getByRole('button', { name: 'render' }));

        expect(new URLSearchParams(globalThis.location.search).get('subsystem')).toBe('render');
    });
});
