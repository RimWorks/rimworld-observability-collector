import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import Panel from './InstrumentationPanel.svelte';

afterEach(() => vi.unstubAllGlobals());

function mockApi(opts: { available?: boolean; live?: unknown[] } = {}) {
    const available = opts.available ?? true;
    const live = opts.live ?? [{ patchId: 5, sectionId: 42, signature: 'A:B()', status: 'active' }];
    vi.stubGlobal(
        'fetch',
        vi.fn(async (url: string) => {
            if (!available && typeof url === 'string' && url.includes('/instrumentation/')) {
                return {
                    ok: false,
                    status: 503,
                    statusText: 'unavailable',
                    json: async () => ({}),
                };
            }
            if (typeof url === 'string' && url.includes('/instrumentation/search')) {
                return {
                    ok: true,
                    status: 200,
                    json: async () => ({
                        schema_version: 2,
                        results: [
                            {
                                typeFullName: 'Verse.PathFinder',
                                methodName: 'FindPath',
                                signature: 'PathFinder:FindPath(IntVec3,IntVec3)',
                                paramTypeFullNames: ['Verse.IntVec3', 'Verse.IntVec3'],
                                assemblyName: 'Assembly-CSharp',
                            },
                        ],
                    }),
                };
            }
            if (typeof url === 'string' && url.includes('/instrumentation/patches')) {
                return {
                    ok: true,
                    status: 200,
                    json: async () => ({
                        schema_version: 2,
                        persisted: [
                            {
                                id: 1,
                                typeFullName: 'A',
                                methodName: 'B',
                                paramTypesJoined: '',
                                createdUtc: '',
                                lastStatus: 'active',
                                lastError: null,
                                livePatchId: 5,
                            },
                        ],
                        live,
                    }),
                };
            }
            return { ok: true, status: 200, json: async () => ({}) };
        }),
    );
}

describe('InstrumentationPanel', () => {
    it('renders the unavailable empty state when collector returns 503', async () => {
        mockApi({ available: false });
        render(Panel);
        expect(await screen.findByText(/Live instrumentation is unavailable/)).toBeInTheDocument();
    });

    it('searches and renders matching methods', async () => {
        mockApi();
        const { container } = render(Panel);
        const box = container.querySelector('input[type=search]')! as HTMLInputElement;
        await fireEvent.input(box, { target: { value: 'Path' } });
        await waitFor(() => {
            expect(screen.getByText(/PathFinder:FindPath/)).toBeInTheDocument();
        });
    });

    it('lists active patches', async () => {
        mockApi();
        render(Panel);
        await waitFor(() => {
            expect(screen.getByText(/active patches/i)).toBeInTheDocument();
        });
        expect(await screen.findByTestId('patch-status')).toHaveTextContent(/active/i);
    });

    // storage only writes lastStatus at replay, so a row the game no longer lists is stale
    // even though it says active.
    it('shows stale when the game does not list a stored patch', async () => {
        mockApi({ live: [] });
        render(Panel);
        expect(await screen.findByTestId('patch-status')).toHaveTextContent(/stale/i);
    });

    it('reports the merged patches to its parent', async () => {
        mockApi();
        const seen: unknown[][] = [];
        render(Panel, { onPatchesChange: (p: unknown[]) => seen.push(p) });
        await waitFor(() => expect(seen.at(-1)).toHaveLength(1));
        expect((seen.at(-1) as { sectionId: number }[])[0].sectionId).toBe(42);
    });
});
