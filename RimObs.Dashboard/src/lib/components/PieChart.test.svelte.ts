import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import PieChart from './PieChart.svelte';
import { OTHER_SECTION_ID, type PieSlice, type ModSlice } from '../pieSlices';

const slices: PieSlice[] = [
    { sectionId: 1, label: 'Verse.TickList.Tick', subsystem: 'tick', selfUs: 600, share: 0.6 },
    { sectionId: 2, label: 'Verse.MapDrawer.Draw', subsystem: 'render', selfUs: 300, share: 0.3 },
    { sectionId: OTHER_SECTION_ID, label: 'other', subsystem: null, selfUs: 100, share: 0.1 },
];

describe('PieChart', () => {
    it('draws one arc per slice', () => {
        const { container } = render(PieChart, { slices });

        expect(container.querySelectorAll('path')).toHaveLength(3);
    });

    it('colours a slice by its subsystem', () => {
        const { container } = render(PieChart, { slices });
        const paths = container.querySelectorAll('path');

        expect(paths[0].getAttribute('fill')).toBe('var(--sub-tick, var(--sub-none))');
        expect(paths[1].getAttribute('fill')).toBe('var(--sub-render, var(--sub-none))');
    });

    it('falls back to the neutral colour for the other slice', () => {
        const { container } = render(PieChart, { slices });

        expect(container.querySelectorAll('path')[2].getAttribute('fill')).toBe('var(--sub-none)');
    });

    it('lists every slice with its share', () => {
        render(PieChart, { slices });

        expect(screen.getByText('Verse.TickList.Tick', { selector: 'span.name' })).toBeTruthy();
        expect(screen.getByText('60.00%')).toBeTruthy();
    });

    it('hands the picked slice to the caller', async () => {
        const onPick = vi.fn();
        render(PieChart, { slices, onPick });

        await fireEvent.click(screen.getByText('Verse.MapDrawer.Draw', { selector: 'span.name' }));

        expect(onPick).toHaveBeenCalledWith(expect.objectContaining({ sectionId: 2 }));
    });

    // "other" is many sections, so there is nothing to pick.
    it('does not pick the other slice', async () => {
        const onPick = vi.fn();
        render(PieChart, { slices, onPick });

        await fireEvent.click(screen.getByText('other', { selector: 'span.name' }));

        expect(onPick).not.toHaveBeenCalled();
    });

    it('keeps a mod colour when the slice order changes', () => {
        const mods: ModSlice[] = Array.from({ length: 9 }, (_, i) => ({
            key: `mod.${i}`,
            label: `mod.${i}`,
            selfUs: 100,
            share: 1 / 9,
        }));
        const fillsOf = (m: ModSlice[]): Record<string, string> => {
            const { container } = render(PieChart, { slices: m });
            const paths = [...container.querySelectorAll('path')];
            return Object.fromEntries(
                paths.map((p, i) => [m[i].key, p.getAttribute('fill') ?? '']),
            );
        };

        expect(fillsOf([...mods].reverse())).toEqual(fillsOf(mods));
    });

    it('renders nothing but an empty chart for no slices', () => {
        const { container } = render(PieChart, { slices: [] });

        expect(container.querySelectorAll('path')).toHaveLength(0);
    });
});

// the donut itself must drill, not just the legend beside it.
describe('PieChart arc clicks', () => {
    it('picks the slice behind a clicked arc', async () => {
        const onPick = vi.fn();
        render(PieChart, { slices, onPick });

        const arcs = screen.getAllByTestId('pie-arc');
        await fireEvent.click(arcs[1]);

        expect(onPick).toHaveBeenCalledWith(expect.objectContaining({ sectionId: 2 }));
    });

    it('ignores a click on the other arc', async () => {
        const onPick = vi.fn();
        render(PieChart, { slices, onPick });

        const arcs = screen.getAllByTestId('pie-arc');
        await fireEvent.click(arcs[arcs.length - 1]);

        expect(onPick).not.toHaveBeenCalled();
    });
});

// tertius: tabindex -1 made the arc enter-key handler dead code for keyboard users.
describe('PieChart arc keyboard', () => {
    it('activates a focused arc with enter', async () => {
        const onPick = vi.fn();
        render(PieChart, { slices, onPick });

        const arc = screen.getAllByTestId('pie-arc')[1];
        expect(arc.getAttribute('tabindex')).toBe('0');
        await fireEvent.keyDown(arc, { key: 'Enter' });

        expect(onPick).toHaveBeenCalledWith(expect.objectContaining({ sectionId: 2 }));
    });
});
