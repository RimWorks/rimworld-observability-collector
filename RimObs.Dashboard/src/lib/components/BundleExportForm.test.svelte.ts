import { render, fireEvent } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import BundleExportForm from './BundleExportForm.svelte';
import { api } from '../api';

vi.mock('../api', () => ({
    api: { estimateBundle: vi.fn() },
}));

const estimateBundle = vi.mocked(api.estimateBundle);

const CAP = 25 * 1024 * 1024;

function underCap() {
    estimateBundle.mockResolvedValue({
        kind: 'ok',
        estimatedBytes: 1024,
        capBytes: CAP,
        exceedsSoftCap: false,
    });
}

function overCap() {
    estimateBundle.mockResolvedValue({
        kind: 'ok',
        estimatedBytes: 30 * 1024 * 1024,
        capBytes: CAP,
        exceedsSoftCap: true,
    });
}

describe('BundleExportForm', () => {
    beforeEach(() => {
        estimateBundle.mockReset();
        underCap();
    });

    it('renders include toggles for each optional content key', () => {
        const { getByLabelText } = render(BundleExportForm, {
            sessionId: 'sess-1',
            onExport: vi.fn(),
        });
        expect(getByLabelText(/Allocations/i)).toBeTruthy();
        expect(getByLabelText(/GC events/i)).toBeTruthy();
        expect(getByLabelText(/Call hierarchy/i)).toBeTruthy();
        expect(getByLabelText(/Patches/i)).toBeTruthy();
        expect(getByLabelText(/Metrics SQLite/i)).toBeTruthy();
        expect(getByLabelText(/^Frames$/i)).toBeTruthy();
    });

    it('calls onExport with selected includes', async () => {
        const handler = vi.fn();
        const { getByLabelText, getByRole } = render(BundleExportForm, {
            sessionId: 'sess-1',
            onExport: handler,
        });

        await fireEvent.click(getByLabelText(/Allocations/i));
        await fireEvent.click(getByLabelText(/GC events/i));
        await fireEvent.click(getByRole('button', { name: /Download bundle/i }));

        expect(handler).toHaveBeenCalledOnce();
        const call = handler.mock.calls[0][0];
        expect(call.includes).toEqual(expect.arrayContaining(['allocations', 'gc-events']));
        expect(call.sessionId).toBe('sess-1');
        expect(call.force).toBe(false);
    });

    it('shows the running size estimate', async () => {
        const { findByText } = render(BundleExportForm, {
            sessionId: 'sess-1',
            onExport: vi.fn(),
        });
        expect(await findByText(/1\.0 KB/)).toBeTruthy();
    });

    it('hides the force toggle while the estimate is under the soft cap', async () => {
        const { findByText, queryByLabelText } = render(BundleExportForm, {
            sessionId: 'sess-1',
            onExport: vi.fn(),
        });
        await findByText(/1\.0 KB/);
        expect(queryByLabelText(/Export anyway/i)).toBeNull();
    });

    it('reveals the force toggle and a warning when the estimate exceeds the soft cap', async () => {
        overCap();
        const { findByLabelText, findByRole } = render(BundleExportForm, {
            sessionId: 'sess-1',
            onExport: vi.fn(),
        });
        expect(await findByLabelText(/Export anyway/i)).toBeTruthy();
        expect(await findByRole('alert')).toBeTruthy();
    });

    it('sends force=true when the over-cap override is checked', async () => {
        overCap();
        const handler = vi.fn();
        const { findByLabelText, getByRole } = render(BundleExportForm, {
            sessionId: 'sess-1',
            onExport: handler,
        });
        await fireEvent.click(await findByLabelText(/Export anyway/i));
        await fireEvent.click(getByRole('button', { name: /Download bundle/i }));
        expect(handler.mock.calls[0][0].force).toBe(true);
    });

    it('disables the button and shows a busy label while the export is in flight', async () => {
        let resolveExport!: () => void;
        const pending = new Promise<void>((resolve) => {
            resolveExport = resolve;
        });
        const handler = vi.fn(() => pending);
        const { getByRole } = render(BundleExportForm, {
            sessionId: 'sess-1',
            onExport: handler,
        });

        await fireEvent.click(getByRole('button', { name: /Download bundle/i }));

        const busy = getByRole('button', { name: /Preparing/i }) as HTMLButtonElement;
        expect(busy.disabled).toBe(true);

        resolveExport();
        await pending;
    });
});
