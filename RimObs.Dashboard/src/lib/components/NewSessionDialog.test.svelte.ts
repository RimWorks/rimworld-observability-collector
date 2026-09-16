import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import NewSessionDialog from './NewSessionDialog.svelte';

function open(onConfirm = vi.fn(), onCancel = vi.fn()) {
    render(NewSessionDialog, { onConfirm, onCancel });
    return { onConfirm, onCancel };
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

describe('NewSessionDialog', () => {
    // a styled div lets Tab walk into the page behind it; only showModal traps focus.
    it('opens modally so focus is trapped', () => {
        open();

        expect(screen.getByTestId('new-session-dialog').tagName).toBe('DIALOG');
        expect(showModal).toHaveBeenCalled();
    });

    it('hands back the typed name and the save choice', async () => {
        const { onConfirm } = open();

        await fireEvent.input(screen.getByTestId('new-session-name'), {
            target: { value: 'Before mods' },
        });
        await fireEvent.click(screen.getByTestId('new-session-confirm'));

        expect(onConfirm).toHaveBeenCalledWith('Before mods', true);
    });

    // Root.Shutdown does not save, so opting out has to actually reach the caller.
    it('passes save as false when the box is cleared', async () => {
        const { onConfirm } = open();

        await fireEvent.click(screen.getByTestId('new-session-save'));
        await fireEvent.click(screen.getByTestId('new-session-confirm'));

        expect(onConfirm).toHaveBeenCalledWith('', false);
    });

    it('defaults to saving, because the alternative loses a colony', () => {
        open();

        expect((screen.getByTestId('new-session-save') as HTMLInputElement).checked).toBe(true);
    });

    it('trims the name so a stray space is not a name', async () => {
        const { onConfirm } = open();

        await fireEvent.input(screen.getByTestId('new-session-name'), {
            target: { value: '   ' },
        });
        await fireEvent.click(screen.getByTestId('new-session-confirm'));

        expect(onConfirm).toHaveBeenCalledWith('', true);
    });

    it('cancels on the button', async () => {
        const { onCancel } = open();

        await fireEvent.click(screen.getByTestId('new-session-cancel'));

        expect(onCancel).toHaveBeenCalledTimes(1);
    });

    it('cancels on Escape', async () => {
        const { onCancel } = open();

        await fireEvent.keyDown(window, { key: 'Escape' });

        expect(onCancel).toHaveBeenCalledTimes(1);
    });

    // the native close event is what Escape fires in a real browser.
    it('cancels when the dialog closes natively', async () => {
        const { onCancel } = open();

        await fireEvent(screen.getByTestId('new-session-dialog'), new Event('close'));

        expect(onCancel).toHaveBeenCalledTimes(1);
    });

    it('cancels on a backdrop click', async () => {
        const { onCancel } = open();

        await fireEvent.click(screen.getByTestId('new-session-dialog'));

        expect(onCancel).toHaveBeenCalledTimes(1);
    });

    it('stays open when the click lands inside the dialog', async () => {
        const { onCancel } = open();

        await fireEvent.click(screen.getByTestId('new-session-name'));

        expect(onCancel).not.toHaveBeenCalled();
    });

    it('warns that unsaved progress is lost', () => {
        open();

        expect(screen.getByTestId('new-session-dialog').textContent).toMatch(/lost/i);
    });
});
