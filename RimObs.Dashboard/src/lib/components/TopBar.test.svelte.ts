import { readFileSync } from 'node:fs';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import TopBar from './TopBar.svelte';
import type { StatusResponse } from '../api';
import { liveVitals } from '../vitals.svelte';
import { t } from '../i18n';

const SESSION_ID = '2ff995a13f244a2e8985c89a7d53e470';

function status(over: Record<string, unknown> = {}): StatusResponse {
    return {
        schema_version: 8,
        status: 'running',
        version: '1.0.0',
        session: { id: SESSION_ID, library_version: '1.0.0', is_current: true },
        receive: {
            tps: 0,
            fps: 239.98,
            total_batches: 101,
            total_samples: 10964,
            total_bytes: 279057,
            section_count: 30,
            total_gc_events: 7,
            last_batch_utc: new Date().toISOString(),
        },
        ...over,
    } as unknown as StatusResponse;
}

describe('TopBar vitals', () => {
    it('shows tps and fps as the only live numbers in the header', () => {
        render(TopBar, { status: status() });

        expect(screen.getByTestId('vital-tps').textContent).toContain('0');
        expect(screen.getByTestId('vital-fps').textContent).toContain('240');
    });

    // the plumbing counters moved to the gear, so none of them may reappear up here.
    it('shows no batch, sample or byte counters', () => {
        render(TopBar, { status: status() });
        const header = screen.getByRole('banner').textContent ?? '';

        expect(header).not.toContain('10,964');
        expect(header).not.toContain('101');
    });

    // a 32-char hex id was the widest thing in the header and no human reads it.
    it('never prints the raw session id', () => {
        render(TopBar, { status: status() });

        expect(screen.queryByText(SESSION_ID)).toBeNull();
    });

    it('drops a vital the collector has not reported', () => {
        render(TopBar, {
            status: status({ receive: { tps: null, fps: 60, last_batch_utc: null } }),
        });

        expect(screen.queryByTestId('vital-tps')).toBeNull();
        expect(screen.getByTestId('vital-fps')).toBeTruthy();
    });

    it('reports offline when the collector is not running', () => {
        render(TopBar, { status: status({ status: 'stopped', session: null }) });

        expect(screen.getByTestId('health').className).not.toContain('up');
    });
});

// a rename once dropped .crumbs while leaving .brand behind. svelte-check reported the stale
// rule as unused, which reads like dead css, so the missing one stayed missing and the header
// fell back to block layout.
describe('TopBar styles', () => {
    const GLOBAL_UTILITIES = new Set(['mono']);

    it('defines a rule for every class the markup uses', () => {
        const src = readFileSync('src/lib/components/TopBar.svelte', 'utf-8');
        const [markup, style] = src.split('<style>');

        const used = new Set<string>();
        for (const m of markup.matchAll(/class="([^"{]*)"/g)) {
            for (const c of m[1].split(/\s+/).filter(Boolean)) used.add(c);
        }
        for (const m of markup.matchAll(/class:([A-Za-z0-9_-]+)/g)) used.add(m[1]);

        const styled = new Set([...style.matchAll(/\.([A-Za-z][A-Za-z0-9_-]*)/g)].map((m) => m[1]));
        const missing = [...used].filter((c) => !styled.has(c) && !GLOBAL_UTILITIES.has(c));

        expect(missing).toEqual([]);
    });

    it('lays the lockup out in a row', () => {
        const src = readFileSync('src/lib/components/TopBar.svelte', 'utf-8');
        const crumbs = src.split('<style>')[1].match(/\.crumbs\s*\{([^}]*)\}/);

        expect(crumbs?.[1]).toContain('display: flex');
    });
});

// the frame poll runs at 60Hz and /status at 0.5Hz, so the header has to prefer the frame.
describe('TopBar vitals freshness', () => {
    afterEach(() => liveVitals.clear());

    it('prefers the per-frame vitals over the slower status poll', () => {
        liveVitals.set({ tps: 60, fps: 144, tick: 1 });
        render(TopBar, { status: status() });

        expect(screen.getByTestId('vital-tps').textContent).toContain('60');
        expect(screen.getByTestId('vital-fps').textContent).toContain('144');
    });

    it('falls back to status when no frame vitals have arrived', () => {
        render(TopBar, { status: status() });

        expect(screen.getByTestId('vital-fps').textContent).toContain('240');
    });
});

// this used to be a standing paragraph at the very bottom of the page, under Compare sessions,
// where it read as that panel's help rather than the flamegraph's.
describe('TopBar keyboard help', () => {
    it('offers the keyboard help from an icon beside the description', () => {
        render(TopBar, { status: status() });

        expect(screen.getByTestId('keys-help')).toBeTruthy();
    });

    it('labels the icon for screen readers', () => {
        render(TopBar, { status: status() });

        expect(screen.getByLabelText(t('flamegraph.keys.title'))).toBeTruthy();
    });

    it('carries both the navigation and transport keys', async () => {
        render(TopBar, { status: status() });
        const trigger = screen.getByTestId('keys-help');

        await fireEvent.focus(trigger.closest('[tabindex]') ?? trigger);

        const tip = await screen.findByRole('tooltip');
        expect(tip.textContent).toContain(t('flamegraph.keys'));
        expect(tip.textContent).toContain(t('flamegraph.keys.transport'));
    });
});
