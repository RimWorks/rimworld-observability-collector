import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import App from './App.svelte';

beforeEach(() => {
    vi.stubGlobal(
        'fetch',
        vi.fn(async () => ({ ok: true, status: 200, json: async () => ({}) })),
    );
});

afterEach(() => {
    vi.unstubAllGlobals();
    globalThis.location.hash = '';
});

// the three-page router is gone. the flamegraph is the whole application, so the shell owes
// the user that page whatever the hash says, and owes them no navigation to anywhere else.
describe('App shell', () => {
    it('renders the flamegraph as the only page', async () => {
        render(App);

        expect(await screen.findByTestId('frame-budget')).toBeInTheDocument();
    });

    it('still renders the flamegraph for a hash left over from a cut route', async () => {
        globalThis.location.hash = '#/comparison';
        render(App);

        expect(await screen.findByTestId('frame-budget')).toBeInTheDocument();
    });

    it('offers no navigation', async () => {
        render(App);
        await screen.findByTestId('frame-budget');

        expect(screen.queryByRole('navigation')).toBeNull();
        expect(screen.queryByRole('link')).toBeNull();
    });
});
