import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import SettingsPopover from './SettingsPopover.svelte';
import type { StatusResponse } from '../api';

const status = {
    schema_version: 6,
    status: 'running',
    version: '1.2.3',
    session: null,
    receive: null,
    exporters: {
        prometheus_enabled: true,
        prometheus_health: {
            last_scrape_utc: '2026-09-08T13:00:00Z',
            last_sample_count: 42,
            total_errors: 0,
            last_error: null,
        },
    },
} as unknown as StatusResponse;

describe('SettingsPopover', () => {
    it('keeps the panel closed until the gear is clicked', async () => {
        render(SettingsPopover, { status });

        expect(screen.queryByRole('dialog')).toBeNull();

        await fireEvent.click(screen.getByTestId('settings-gear'));

        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());
        expect(screen.getByText('1.2.3')).toBeTruthy();
    });

    it('closes on escape', async () => {
        render(SettingsPopover, { status });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());

        await fireEvent.keyDown(globalThis.document, { key: 'Escape' });

        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    });

    it('closes when a pointer lands outside the panel', async () => {
        render(SettingsPopover, { status });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());

        await fireEvent.pointerDown(globalThis.document.body);

        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    });

    it('stays open when the pointer lands inside the panel', async () => {
        render(SettingsPopover, { status });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        const panel = await screen.findByRole('dialog');

        await fireEvent.pointerDown(panel);

        expect(screen.queryByRole('dialog')).toBeTruthy();
    });

    it('shows the prometheus health block only while the exporter is on', async () => {
        const { unmount } = render(SettingsPopover, { status });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await waitFor(() => expect(screen.getByText('/metrics')).toBeTruthy());
        unmount();

        const off = {
            ...status,
            exporters: { ...status.exporters, prometheus_enabled: false },
        } as unknown as StatusResponse;
        render(SettingsPopover, { status: off });
        await fireEvent.click(screen.getByTestId('settings-gear'));
        await waitFor(() => expect(screen.getByRole('dialog')).toBeTruthy());

        expect(screen.queryByText('/metrics')).toBeNull();
    });
});
