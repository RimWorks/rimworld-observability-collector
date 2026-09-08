import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import PieChart from './PieChart.svelte';
import { OTHER_SECTION_ID, type PieSlice } from '../pieSlices';

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

    it('selects the section behind a legend entry', async () => {
        const onSelect = vi.fn();
        render(PieChart, { slices, onSelect });

        await fireEvent.click(screen.getByText('Verse.MapDrawer.Draw', { selector: 'span.name' }));

        expect(onSelect).toHaveBeenCalledWith(2);
    });

    // "other" is many sections, so there is nothing to select.
    it('does not select the other slice', async () => {
        const onSelect = vi.fn();
        render(PieChart, { slices, onSelect });

        await fireEvent.click(screen.getByText('other', { selector: 'span.name' }));

        expect(onSelect).not.toHaveBeenCalled();
    });

    it('renders nothing but an empty chart for no slices', () => {
        const { container } = render(PieChart, { slices: [] });

        expect(container.querySelectorAll('path')).toHaveLength(0);
    });
});
