import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import StatusFooter from './StatusFooter.svelte';
import { t } from '../i18n';

const BASE = {
    ringHeld: 2000,
    ringCapacity: 5000,
    overheadText: 'overhead 73 ns/scope',
    timerResLine: 'timer res 100 ns',
    deltaUs: 600,
    deltaSeverity: 1 as const,
    deltaText: '+600 ns',
};

describe('StatusFooter', () => {
    it('shows how many frames the ring holds against its capacity', () => {
        render(StatusFooter, BASE);
        expect(screen.getByTestId('footer-ring')).toHaveTextContent('2,000/5,000');
    });

    // nothing reports process memory to the dashboard yet. the point of this test is that the
    // readout stays empty of numbers rather than inventing one, whatever the locale calls it.
    it('shows memory as unavailable rather than a fabricated number', () => {
        render(StatusFooter, BASE);
        const memory = screen.getByTestId('footer-memory');

        expect(memory).toHaveTextContent(t('footer.memory.unavailable'));
        expect(memory.textContent).not.toMatch(/\d/);
    });

    it('renders the overhead readout it was handed', () => {
        render(StatusFooter, BASE);
        expect(screen.getByTestId('footer-overhead')).toHaveTextContent('overhead 73 ns/scope');
    });

    it('omits the overhead readout when the caller has no overhead text', () => {
        render(StatusFooter, { ...BASE, overheadText: '' });
        expect(screen.queryByTestId('footer-overhead')).toBeNull();
    });

    it('renders the timer resolution readout it was handed', () => {
        render(StatusFooter, BASE);
        expect(screen.getByTestId('footer-timerres')).toHaveTextContent('timer res 100 ns');
    });

    it('omits the timer resolution readout when the caller has none', () => {
        render(StatusFooter, { ...BASE, timerResLine: '' });
        expect(screen.queryByTestId('footer-timerres')).toBeNull();
    });

    it('colors the delta by severity and omits it when there is no prior frame yet', () => {
        const { rerender } = render(StatusFooter, BASE);
        expect(screen.getByTestId('footer-delta').className).toContain('warn');

        rerender({ ...BASE, deltaSeverity: -1 });
        expect(screen.getByTestId('footer-delta').className).toContain('cool');

        rerender({ ...BASE, deltaUs: null, deltaText: '' });
        expect(screen.queryByTestId('footer-delta')).toBeNull();
    });

    it('gives each readout a descriptive tooltip', async () => {
        render(StatusFooter, BASE);
        for (const testid of [
            'footer-ring',
            'footer-memory',
            'footer-overhead',
            'footer-timerres',
            'footer-delta',
        ]) {
            const readout = screen.getByTestId(testid);
            await fireEvent.mouseEnter(readout.closest('.tt-wrap')!);
            expect(readout.closest('.tt-wrap')?.querySelector('[role="tooltip"]')).not.toBeNull();
            await fireEvent.mouseLeave(readout.closest('.tt-wrap')!);
        }
    });
});
