import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import NameSessionPrompt from './NameSessionPrompt.svelte';

function open() {
    const onName = vi.fn();
    const onSkip = vi.fn();
    const onDisable = vi.fn();
    render(NameSessionPrompt, { sessionId: '0196cedfc1f3', onName, onSkip, onDisable });
    return { onName, onSkip, onDisable };
}

describe('NameSessionPrompt', () => {
    it('shows which session it is asking about', () => {
        open();

        expect(screen.getByTestId('name-session-prompt').textContent).toContain('0196cedfc1f3');
    });

    it('hands back the typed name', async () => {
        const { onName } = open();

        await fireEvent.input(screen.getByTestId('prompt-session-name'), {
            target: { value: 'Late game 12x' },
        });
        await fireEvent.click(screen.getByTestId('prompt-save'));

        expect(onName).toHaveBeenCalledWith('Late game 12x');
    });

    // naming a session the empty string is the same as not naming it, so it must not submit.
    it('cannot submit an empty name', () => {
        open();

        expect((screen.getByTestId('prompt-save') as HTMLButtonElement).disabled).toBe(true);
    });

    it('skips without naming, on the button and on Escape', async () => {
        const { onSkip } = open();

        await fireEvent.click(screen.getByTestId('prompt-skip'));
        await fireEvent.keyDown(window, { key: 'Escape' });

        expect(onSkip).toHaveBeenCalledTimes(2);
    });

    it('offers a way to stop being asked at all', async () => {
        const { onDisable } = open();

        await fireEvent.click(screen.getByTestId('prompt-disable'));

        expect(onDisable).toHaveBeenCalled();
    });
});
