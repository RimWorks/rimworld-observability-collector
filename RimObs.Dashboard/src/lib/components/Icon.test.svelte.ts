import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/svelte';
import Icon, { ICONS, type IconName } from './Icon.svelte';

describe('Icon', () => {
    it('renders an svg at the requested size', () => {
        const { container } = render(Icon, { name: 'flame', size: 24 });

        const svg = container.querySelector('svg')!;
        expect(svg.getAttribute('width')).toBe('24');
        expect(svg.getAttribute('height')).toBe('24');
    });

    it('renders a different glyph per name', () => {
        const flame = render(Icon, { name: 'flame' }).container.innerHTML;
        const gauge = render(Icon, { name: 'gauge' }).container.innerHTML;

        expect(flame).not.toBe(gauge);
    });

    it.each(Object.keys(ICONS) as IconName[])('resolves %s to a glyph', (name) => {
        const { container } = render(Icon, { name });

        expect(container.querySelector('svg')).toBeTruthy();
    });
});
