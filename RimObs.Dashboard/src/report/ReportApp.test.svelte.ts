import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { render, screen } from '@testing-library/svelte';
import ReportApp from './ReportApp.svelte';

const raw = {
    manifest: { session_id: 's1', collector_version: '1.0', created_utc: '2026-01-01T00:00:00Z' },
    session_summary: { ticks: 10 },
    gc_events: { count: 2 },
};

describe('report tabs', () => {
    it('exposes the tab bar as a tablist', () => {
        render(ReportApp, { raw });

        expect(screen.getByRole('tablist')).toBeInTheDocument();
        expect(screen.getAllByRole('tab').length).toBe(6);
    });

    it('marks the active tab selected and points it at its panel', () => {
        render(ReportApp, { raw });

        const summary = screen.getByRole('tab', { name: 'Summary' });
        expect(summary).toHaveAttribute('aria-selected', 'true');

        const panel = screen.getByRole('tabpanel');
        expect(summary).toHaveAttribute('aria-controls', panel.id);
        expect(panel).toHaveAttribute('aria-labelledby', summary.id);
    });

    it('moves selection when another tab is clicked', async () => {
        render(ReportApp, { raw });

        const gc = screen.getByRole('tab', { name: 'GC' });
        gc.click();
        await Promise.resolve();

        expect(gc).toHaveAttribute('aria-selected', 'true');
        expect(screen.getByRole('tab', { name: 'Summary' })).toHaveAttribute(
            'aria-selected',
            'false',
        );
    });

    it('hides a tab whose section is missing from the bundle', () => {
        render(ReportApp, { raw });

        expect(screen.queryByRole('tab', { name: 'Patches' })).toBeNull();
    });
});

// theme.css would base64 nine fontsource files into the single-file report, so it reads
// tokens.css and the palette has to come from tokens, never the old light hexes.
describe('report styling', () => {
    const source = readFileSync('src/report/ReportApp.svelte', 'utf-8');

    it('imports the token file and not the font-carrying theme', () => {
        const main = readFileSync('src/report/main.ts', 'utf-8');

        expect(main).toContain('tokens.css');
        expect(main).not.toContain('theme.css');
    });

    it('styles from tokens', () => {
        expect(source).toContain('var(--bg-base)');
        expect(source).toContain('var(--text-dim)');
        expect(source).toContain('var(--font-ui)');
    });

    it('keeps no light-palette hexes anywhere in the report', () => {
        const files = [
            'src/report/ReportApp.svelte',
            'src/report/lib/sections/SummaryTab.svelte',
            'src/report/lib/sections/GcTab.svelte',
        ];

        for (const file of files) {
            const css = readFileSync(file, 'utf-8');
            for (const hex of ['#666', '#ddd', '#444', '#2563eb', '#888']) {
                expect(css).not.toContain(hex);
            }
        }
    });

    it('does not mark the active tab by border colour alone', () => {
        const active = source.slice(source.indexOf('.tabs button.active'));

        expect(active).toContain('background:');
    });
});
