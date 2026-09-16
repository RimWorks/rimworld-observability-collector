import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import NameSessionPrompt from './NameSessionPrompt.svelte';

function open() {
    const onName = vi.fn();
    const onSkip = vi.fn();
    const onDisable = vi.fn();
    render(NameSessionPrompt, { sessionId: '0196cedfc1f3', onName, onSkip, onDisable });
    return { onName, onSkip, onDisable };
}

// jsdom has no showModal, so stub it and watch that the dialog actually goes modal.
let showModal: ReturnType<typeof vi.fn>;

beforeEach(() => {
    showModal = vi.fn();
    (HTMLDialogElement.prototype as unknown as { showModal: unknown }).showModal = showModal;
});

afterEach(() => {
    delete (HTMLDialogElement.prototype as unknown as { showModal?: unknown }).showModal;
});

describe('NameSessionPrompt', () => {
    // a styled div lets Tab walk into the page behind it; only showModal traps focus.
    it('opens modally so focus is trapped', () => {
        open();

        expect(screen.getByTestId('name-session-prompt').tagName).toBe('DIALOG');
        expect(showModal).toHaveBeenCalled();
    });

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

    it('skips without naming, on the button', async () => {
        const { onSkip } = open();

        await fireEvent.click(screen.getByTestId('prompt-skip'));

        expect(onSkip).toHaveBeenCalledTimes(1);
    });

    it('skips on Escape', async () => {
        const { onSkip } = open();

        await fireEvent.keyDown(window, { key: 'Escape' });

        expect(onSkip).toHaveBeenCalledTimes(1);
    });

    // the native close event is what Escape fires in a real browser.
    it('skips when the dialog closes natively', async () => {
        const { onSkip } = open();

        await fireEvent(screen.getByTestId('name-session-prompt'), new Event('close'));

        expect(onSkip).toHaveBeenCalledTimes(1);
    });

    it('skips on a backdrop click', async () => {
        const { onSkip } = open();

        await fireEvent.click(screen.getByTestId('name-session-prompt'));

        expect(onSkip).toHaveBeenCalledTimes(1);
    });

    it('stays open when the click lands inside the dialog', async () => {
        const { onSkip } = open();

        await fireEvent.click(screen.getByTestId('prompt-session-name'));

        expect(onSkip).not.toHaveBeenCalled();
    });

    it('offers a way to stop being asked at all', async () => {
        const { onDisable } = open();

        await fireEvent.click(screen.getByTestId('prompt-disable'));

        expect(onDisable).toHaveBeenCalled();
    });
});
