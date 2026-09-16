import { readFileSync } from 'node:fs';
import { describe, it, expect, afterEach, vi } from 'vitest';
import { render, screen, fireEvent, cleanup, waitFor } from '@testing-library/svelte';
import TooltipHost from './TooltipHost.test.svelte';

afterEach(() => cleanup());

// jsdom has no layout, so Floating UI is stubbed. These cover the wiring: that the bubble is
// placed by the library rather than by a hand-rolled offset, with flip and shift switched on.
vi.mock('@floating-ui/dom', () => ({
    computePosition: vi.fn(async () => ({ x: 42, y: 84, placement: 'bottom', middlewareData: {} })),
    autoUpdate: (_a: unknown, _b: unknown, update: () => void) => {
        update();
        return () => {};
    },
    flip: () => ({ name: 'flip' }),
    shift: () => ({ name: 'shift' }),
    offset: () => ({ name: 'offset' }),
}));

const { computePosition } = await import('@floating-ui/dom');

describe('Tooltip', () => {
    it('shows on hover and hides on leave', async () => {
        render(TooltipHost, { text: 'why 45 ms' });
        const wrap = screen.getByText('anchor').parentElement!;
        await fireEvent.mouseEnter(wrap);
        expect(screen.getByRole('tooltip')).toHaveTextContent('why 45 ms');
        await fireEvent.mouseLeave(wrap);
        expect(screen.queryByRole('tooltip')).toBeNull();
    });

    it('places the bubble where the positioner put it', async () => {
        render(TooltipHost, { text: 'why' });
        await fireEvent.mouseEnter(screen.getByText('anchor').parentElement!);
        await waitFor(() => expect(screen.getByRole('tooltip').style.left).toBe('42px'));
        expect(screen.getByRole('tooltip').style.top).toBe('84px');
    });

    // the whole point of the library: the old bubble ran off the top of the window.
    it('asks for flip and shift so a bubble near an edge is moved, not clipped', async () => {
        render(TooltipHost, { text: 'why' });
        await fireEvent.mouseEnter(screen.getByText('anchor').parentElement!);
        await waitFor(() => expect(computePosition).toHaveBeenCalled());
        const opts = vi.mocked(computePosition).mock.calls[0][2]!;
        expect(opts.strategy).toBe('fixed');
        expect(opts.middleware?.map((m) => (m as { name: string }).name)).toEqual([
            'offset',
            'flip',
            'shift',
        ]);
    });

    it('renders the content snippet in the bubble when one is passed', async () => {
        render(TooltipHost, { text: 'plain', rich: true });
        await fireEvent.mouseEnter(screen.getByText('anchor').parentElement!);
        const bubble = screen.getByRole('tooltip');
        expect(bubble).toContainElement(screen.getByTestId('tt-rich'));
        expect(bubble).not.toHaveTextContent('plain');
    });

    it('falls back to text when no content snippet is passed', async () => {
        render(TooltipHost, { text: 'plain' });
        await fireEvent.mouseEnter(screen.getByText('anchor').parentElement!);
        expect(screen.getByRole('tooltip')).toHaveTextContent('plain');
        expect(screen.queryByTestId('tt-rich')).toBeNull();
    });

    it('is a tab stop for a plain-text trigger', () => {
        render(TooltipHost, { text: 'why' });
        expect(screen.getByText('anchor').parentElement!.getAttribute('tabindex')).toBe('0');
    });

    it('adds no second tab stop in front of a wrapped button', () => {
        render(TooltipHost, { text: 'why', button: true });
        const wrap = screen.getByRole('button', { name: 'anchor' }).parentElement!;
        expect(wrap).toHaveClass('tt-wrap');
        expect(wrap.hasAttribute('tabindex')).toBe(false);
    });

    it('stays a tab stop around a disabled button', () => {
        render(TooltipHost, { text: 'why', button: true, disabled: true });
        const wrap = screen.getByRole('button', { name: 'anchor' }).parentElement!;
        expect(wrap.getAttribute('tabindex')).toBe('0');
    });

    // the Apply button in the settings popover flips disabled as the form goes dirty and saves.
    // A DOM read is not reactive, so those call sites tell the tooltip via childFocusable.
    it('follows the wrapped button when disabled flips', async () => {
        const props = { text: 'why', button: true, disabled: true, childFocusable: false };
        const { rerender } = render(TooltipHost, props);
        const wrap = screen.getByRole('button', { name: 'anchor' }).parentElement!;
        expect(wrap.getAttribute('tabindex')).toBe('0');

        await rerender({ ...props, disabled: false, childFocusable: true });
        await waitFor(() => expect(wrap.hasAttribute('tabindex')).toBe(false));

        await rerender(props);
        await waitFor(() => expect(wrap.getAttribute('tabindex')).toBe('0'));
    });

    it('hides on Escape', async () => {
        render(TooltipHost, { text: 'why' });
        await fireEvent.mouseEnter(screen.getByText('anchor').parentElement!);
        expect(screen.getByRole('tooltip')).toBeInTheDocument();
        await fireEvent.keyDown(document.body, { key: 'Escape' });
        expect(screen.queryByRole('tooltip')).toBeNull();
    });

    // an open hover tooltip used to eat every Escape on the page: no timeline refit, no search clear.
    it('lets the Escape it handles reach everyone else', async () => {
        const outer = vi.fn();
        globalThis.addEventListener('keydown', outer);
        try {
            render(TooltipHost, { text: 'why' });
            await fireEvent.mouseEnter(screen.getByText('anchor').parentElement!);
            await fireEvent.keyDown(document.body, { key: 'Escape' });
            expect(screen.queryByRole('tooltip')).toBeNull();
            expect(outer).toHaveBeenCalledTimes(1);
        } finally {
            globalThis.removeEventListener('keydown', outer);
        }
    });

    // even focused, the tooltip never owns Escape: the same press must still reach the popover
    // or dialog behind it. a swallow here once made the settings popover need two presses.
    it('passes the Escape through even when focus is inside the trigger', async () => {
        const outer = vi.fn();
        globalThis.addEventListener('keydown', outer);
        try {
            render(TooltipHost, { text: 'why', button: true });
            const btn = screen.getByRole('button', { name: 'anchor' });
            btn.focus();
            await waitFor(() => expect(screen.getByRole('tooltip')).toBeInTheDocument());
            await fireEvent.keyDown(btn, { key: 'Escape' });
            expect(screen.queryByRole('tooltip')).toBeNull();
            expect(outer).toHaveBeenCalledTimes(1);
        } finally {
            globalThis.removeEventListener('keydown', outer);
        }
    });

    // the focused one used to stopImmediatePropagation the hovered one's listener off the page,
    // leaving a bubble open that still ate clicks.
    it('closes every open tooltip on Escape, whatever order they opened in', async () => {
        const a = render(TooltipHost, { text: 'focused', button: true });
        const b = render(TooltipHost, { text: 'hovered' });
        const bubbleA = () => a.container.querySelector('[role="tooltip"]');
        const bubbleB = () => b.container.querySelector('[role="tooltip"]');
        a.container.querySelector('button')!.focus();
        await waitFor(() => expect(bubbleA()).not.toBeNull());
        await fireEvent.mouseEnter(b.container.querySelector('.tt-wrap')!);
        expect(bubbleB()).not.toBeNull();

        await fireEvent.keyDown(document.body, { key: 'Escape' });
        expect(bubbleA()).toBeNull();
        expect(bubbleB()).toBeNull();
    });

    // a hoverable bubble sits fixed at z-50 over the toolbar and eats the clicks on Pause/step,
    // and inside a <label> it flips the setting it was explaining.
    it('gives the bubble no pointer surface at all', async () => {
        render(TooltipHost, { text: 'why' });
        await fireEvent.mouseEnter(screen.getByText('anchor').parentElement!);
        expect(screen.getByRole('tooltip')).toBeInTheDocument();

        // vitest does not inject component css into jsdom, so the rules are read from source.
        const css = readFileSync('src/lib/components/Tooltip.svelte', 'utf8');
        const bubble = css.match(/\.tt-bubble\s*\{[^}]*\}/)![0];
        expect(bubble).toMatch(/pointer-events\s*:\s*none/);
        expect(css).not.toMatch(/\.tt-bubble[^{]*::before/);
    });

    it('describes its anchor while open', async () => {
        render(TooltipHost, { text: 'why' });
        const wrap = screen.getByText('anchor').parentElement!;
        await fireEvent.focus(wrap);
        expect(wrap.getAttribute('aria-describedby')).toBe(screen.getByRole('tooltip').id);
    });

    // aria relations do not inherit: on the wrapper, a focused button announces nothing.
    it('describes the wrapped button, not the wrapper', async () => {
        render(TooltipHost, { text: 'why', button: true });
        const btn = screen.getByRole('button', { name: 'anchor' });
        const wrap = btn.parentElement!;
        await fireEvent.mouseEnter(wrap);
        expect(btn.getAttribute('aria-describedby')).toBe(screen.getByRole('tooltip').id);
        expect(wrap.hasAttribute('aria-describedby')).toBe(false);

        await fireEvent.mouseLeave(wrap);
        expect(btn.hasAttribute('aria-describedby')).toBe(false);
    });

    it('moves the description onto the child when it becomes focusable', async () => {
        const props = { text: 'why', button: true, disabled: true, childFocusable: false };
        const { rerender } = render(TooltipHost, props);
        const btn = screen.getByRole('button', { name: 'anchor' });
        const wrap = btn.parentElement!;
        await fireEvent.mouseEnter(wrap);
        expect(wrap.getAttribute('aria-describedby')).toBe(screen.getByRole('tooltip').id);

        await rerender({ ...props, disabled: false, childFocusable: true });
        await waitFor(() =>
            expect(btn.getAttribute('aria-describedby')).toBe(screen.getByRole('tooltip').id),
        );
        expect(wrap.hasAttribute('aria-describedby')).toBe(false);
    });
});
