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

    it('describes its anchor while open', async () => {
        render(TooltipHost, { text: 'why' });
        const wrap = screen.getByText('anchor').parentElement!;
        await fireEvent.focus(wrap);
        expect(wrap.getAttribute('aria-describedby')).toBe(screen.getByRole('tooltip').id);
    });
});
