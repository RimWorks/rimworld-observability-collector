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

describe('App route dispatch', () => {
    it('renders the Comparison route for #/comparison', async () => {
        globalThis.location.hash = '#/comparison';
        render(App);
        expect(await screen.findByText('Select sources to compare')).toBeInTheDocument();
    });

    // a hash naming a route that no longer exists must not render a blank shell
    it('falls back to Overview for a route that was cut', async () => {
        globalThis.location.hash = '#/logs';
        render(App);
        expect(await screen.findByRole('heading', { name: 'Overview' })).toBeInTheDocument();
    });
});
