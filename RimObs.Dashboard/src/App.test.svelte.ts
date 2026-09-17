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

        expect(await screen.findByTestId('pause')).toBeInTheDocument();
    });

    it('still renders the flamegraph for a hash left over from a cut route', async () => {
        globalThis.location.hash = '#/comparison';
        render(App);

        expect(await screen.findByTestId('pause')).toBeInTheDocument();
    });

    it('offers no navigation', async () => {
        render(App);
        await screen.findByTestId('pause');

        expect(screen.queryByRole('navigation')).toBeNull();
        expect(screen.queryByRole('link')).toBeNull();
    });
});

// window.close is a no-op for tabs a script did not open (ours comes from $BROWSER), so
// the disconnect path must fall back to an honest terminal screen.
describe('close on disconnect fallback', () => {
    it('shows the disconnected screen when close() is ignored', async () => {
        const close = vi.spyOn(window, 'close').mockImplementation(() => {});
        vi.useFakeTimers();
        render(App);
        // first refresh succeeds, so the app counts as having been connected.
        await vi.advanceTimersByTimeAsync(50);

        vi.stubGlobal(
            'fetch',
            vi.fn(async () => {
                throw new Error('collector gone');
            }),
        );
        await vi.advanceTimersByTimeAsync(4000 * 3 + 400);
        vi.useRealTimers();

        expect(close).toHaveBeenCalled();
        expect(screen.getByTestId('disconnected-screen')).toBeTruthy();
        close.mockRestore();
    });
});
