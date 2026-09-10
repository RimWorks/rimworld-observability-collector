import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/svelte';
import NewSessionDialog from './NewSessionDialog.svelte';

function open(onConfirm = vi.fn(), onCancel = vi.fn()) {
    render(NewSessionDialog, { onConfirm, onCancel });
    return { onConfirm, onCancel };
}

describe('NewSessionDialog', () => {
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

    it('cancels on the button and on Escape', async () => {
        const { onCancel } = open();

        await fireEvent.click(screen.getByTestId('new-session-cancel'));
        await fireEvent.keyDown(window, { key: 'Escape' });

        expect(onCancel).toHaveBeenCalledTimes(2);
    });

    it('warns that unsaved progress is lost', () => {
        open();

        expect(screen.getByTestId('new-session-dialog').textContent).toMatch(/lost/i);
    });
});
