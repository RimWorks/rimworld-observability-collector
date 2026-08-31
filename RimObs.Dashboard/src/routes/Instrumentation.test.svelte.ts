import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import Instrumentation from './Instrumentation.svelte';

afterEach(() => vi.unstubAllGlobals());

function mockApi(opts: { available?: boolean } = {}) {
    const available = opts.available ?? true;
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
                            },
                        ],
                    }),
                };
            }
            return { ok: true, status: 200, json: async () => ({}) };
        }),
    );
}

describe('Instrumentation route', () => {
    it('renders the unavailable empty state when collector returns 503', async () => {
        mockApi({ available: false });
        render(Instrumentation);
        expect(await screen.findByText(/Live instrumentation is unavailable/)).toBeInTheDocument();
    });

    it('searches and renders matching methods', async () => {
        mockApi();
        const { container } = render(Instrumentation);
        const box = container.querySelector('input[type=search]')! as HTMLInputElement;
        await fireEvent.input(box, { target: { value: 'Path' } });
        await waitFor(() => {
            expect(screen.getByText(/PathFinder:FindPath/)).toBeInTheDocument();
        });
    });

    it('lists active patches', async () => {
        mockApi();
        render(Instrumentation);
        await waitFor(() => {
            expect(screen.getByText(/active patches/i)).toBeInTheDocument();
        });
    });
});
