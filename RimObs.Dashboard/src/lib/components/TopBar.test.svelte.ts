import { readFileSync } from 'node:fs';
import { describe, it, expect, afterEach, vi } from 'vitest';
import { render, screen, fireEvent, cleanup } from '@testing-library/svelte';
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

// ka removed the tagline on 2026-09-14 and a repair round restored it once. pinned gone.
describe('TopBar chrome', () => {
    it('shows no tagline next to the product name', () => {
        render(TopBar, { status: status() });
        const header = screen.getByRole('banner').textContent ?? '';
        expect(header).not.toContain('One tick drawn as a flame graph');
        expect(document.querySelector('.what')).toBeNull();
    });
});

describe('TopBar vitals', () => {
    afterEach(() => liveVitals.clear());

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

    // the ms became a budget-share bar, the spread meter's idiom. green sliver at rest,
    // amber and wide when the tick cost eats its budget.
    it('draws the cost bars from the measured tick and frame costs', () => {
        liveVitals.set({ tps: 60, fps: 240, tick: 1, tick_ms: 1.0 }, 4120);
        render(TopBar, { status: status() });

        // healthy shares draw the neutral steel, not a grade hue.
        const tick = screen.getByTestId('tps-cost');
        expect(tick.style.width).toBe('6%');
        expect(tick.style.background).toContain('--border-strong');
        const frame = screen.getByTestId('fps-cost');
        expect(frame.style.width).toContain('9.0');
        expect(frame.style.background).toContain('--border-strong');
    });

    // regression: section heat thresholds painted a healthy 10ms tick red. whole-cost bars
    // stay neutral until the budget is nearly spent.
    it('keeps a mid-budget cost neutral and colors only near or over budget', () => {
        liveVitals.set({ tps: 60, fps: 240, tick: 1, tick_ms: 10.0 }, 4120);
        render(TopBar, { status: status() });
        expect(screen.getByTestId('tps-cost').style.background).toContain('--border-strong');
        cleanup();

        liveVitals.set({ tps: 60, fps: 240, tick: 1, tick_ms: 15.0 }, 4120);
        render(TopBar, { status: status() });
        expect(screen.getByTestId('tps-cost').style.background).toContain('--warn');
        cleanup();

        liveVitals.set({ tps: 60, fps: 240, tick: 1, tick_ms: 20.0 }, 4120);
        render(TopBar, { status: status() });
        expect(screen.getByTestId('tps-cost').style.background).toContain('--bad');
    });

    it('shows no cost bar without a measured cost', () => {
        render(TopBar, { status: status() });
        expect(screen.queryByTestId('tps-cost')).toBeNull();
        expect(screen.queryByTestId('fps-cost')).toBeNull();
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

    // the icon became words (ka, 2026-09-14): the button names itself, no tooltip needed.
    it('names the help entry in plain words', () => {
        render(TopBar, { status: status() });

        expect(screen.getByTestId('keys-help')).toHaveTextContent('Press ? for help');
    });

    // hover-only help is unreachable by keyboard or touch, so the icon is a real button.
    it('opens the overlay when the icon is clicked', async () => {
        render(TopBar, { status: status() });

        await fireEvent.click(screen.getByTestId('keys-help'));

        expect(screen.getByTestId('keys-dialog')).toBeTruthy();
    });

    // a touch user has no Escape key, so the overlay needs a control they can tap.
    it('closes the overlay from the close button', async () => {
        render(TopBar, { status: status() });
        await fireEvent.click(screen.getByTestId('keys-help'));

        await fireEvent.click(screen.getByTestId('keys-close'));

        expect(screen.queryByTestId('keys-dialog')).toBeNull();
    });

    // the shared Dialog shell is what makes the overlay modal; a bare div would let Tab escape.
    it('opens the overlay modally through the dialog shell', async () => {
        const showModal = vi.fn();
        (HTMLDialogElement.prototype as unknown as { showModal: unknown }).showModal = showModal;
        render(TopBar, { status: status() });

        await fireEvent.click(screen.getByTestId('keys-help'));

        expect(screen.getByTestId('keys-dialog').tagName).toBe('DIALOG');
        expect(showModal).toHaveBeenCalled();
        delete (HTMLDialogElement.prototype as unknown as { showModal?: unknown }).showModal;
    });

    it('closes the overlay on a backdrop click', async () => {
        render(TopBar, { status: status() });
        await fireEvent.click(screen.getByTestId('keys-help'));

        await fireEvent.click(screen.getByTestId('keys-dialog'));

        expect(screen.queryByTestId('keys-dialog')).toBeNull();
    });

    // opening with no focus inside leaves a keyboard user stranded behind the modal.
    it('focuses the first control in the overlay', async () => {
        render(TopBar, { status: status() });

        await fireEvent.click(screen.getByTestId('keys-help'));

        expect(document.activeElement).toBe(screen.getByTestId('keys-close'));
    });

    it('opens the shortcuts overlay on ? and closes it on Escape', async () => {
        render(TopBar, { status: status() });
        expect(screen.queryByTestId('keys-dialog')).toBeNull();

        await fireEvent.keyDown(globalThis.window, { key: '?' });
        expect(screen.getByTestId('keys-dialog')).toBeTruthy();

        await fireEvent.keyDown(globalThis.window, { key: 'Escape' });
        expect(screen.queryByTestId('keys-dialog')).toBeNull();
    });

    it('lists the transport, canvas and search groups in the overlay', async () => {
        render(TopBar, { status: status() });
        await fireEvent.keyDown(globalThis.window, { key: '?' });

        expect(screen.getByTestId('keys-transport').textContent).toBe(
            t('flamegraph.keys.transport'),
        );
        expect(screen.getByTestId('keys-canvas').textContent).toBe(t('flamegraph.keys'));
        expect(screen.getByTestId('keys-search').textContent).toBe(t('flamegraph.keys.search'));
    });

    // ? is shift+/ and / focuses the flamegraph search, so typing it in a field must not steal it.
    it('ignores ? typed inside an input', async () => {
        render(TopBar, { status: status() });
        const input = document.createElement('input');
        document.body.appendChild(input);

        await fireEvent.keyDown(input, { key: '?', bubbles: true });

        expect(screen.queryByTestId('keys-dialog')).toBeNull();
        input.remove();
    });

    it('shows patch progress while the worker drains and hides it when done', () => {
        const auto = {
            matched: 40,
            instrumented: 30,
            muted: 0,
            skippedTrivial: 0,
            skippedOther: 0,
            skippedOverCap: 0,
            maxTargets: 100,
            truncated: false,
            refused: 0,
            pending: 10,
        };
        const { unmount } = render(TopBar, { status: status(), auto });
        expect(screen.getByTestId('patch-progress').textContent).toContain('75%');
        const fill = screen.getByTestId('patch-progress').querySelector('.fill') as HTMLElement;
        expect(fill.style.transform).toBe('scaleX(0.75)');
        expect(fill.style.width).toBe('');
        unmount();

        render(TopBar, { status: status(), auto: { ...auto, pending: 0 } });
        expect(screen.queryByTestId('patch-progress')).toBeNull();
    });

    it('declares the patch progress styles once, not again in the media query', () => {
        const css = readFileSync('src/lib/components/TopBar.svelte', 'utf-8');
        const query = css.slice(css.indexOf('@media (max-width: 820px)'));
        expect(query).not.toContain('.patching');
        expect(css.match(/\.patching \.fill \{/g)).toHaveLength(1);
    });
});
