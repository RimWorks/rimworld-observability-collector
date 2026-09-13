import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { copyable } from './copyable';
import { t } from './i18n';

const writeText = vi.fn<(text: string) => Promise<void>>();

beforeEach(() => {
    vi.useFakeTimers();
    writeText.mockReset();
    writeText.mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
        value: { writeText },
        configurable: true,
    });
});

afterEach(() => {
    vi.useRealTimers();
    document.body.innerHTML = '';
});

function cell(html: string): HTMLElement {
    const node = document.createElement('span');
    node.innerHTML = html;
    document.body.append(node);
    copyable(node);
    return node;
}

describe('copyable', () => {
    it('copies the label and the value as one line', async () => {
        const node = cell('p99\n        <b>12.690 ms</b>');
        node.click();
        await vi.runAllTimersAsync();

        expect(writeText).toHaveBeenCalledWith('p99 12.690 ms');
    });

    it('confirms the copy without moving anything, then goes back', async () => {
        const node = cell('p99 <b>12.690 ms</b>');
        node.click();
        await vi.waitFor(() => expect(node.classList.contains('copied')).toBe(true));
        expect(node.getAttribute('title')).toBe(t('copy.copied'));

        await vi.runAllTimersAsync();
        expect(node.classList.contains('copied')).toBe(false);
        expect(node.getAttribute('title')).toBe(t('copy.hint'));
    });

    it('keeps the cells own title text instead of overwriting it', async () => {
        const node = document.createElement('span');
        node.title = 'peak alloc explained';
        node.textContent = 'peak alloc 1.2 MB/m';
        document.body.append(node);
        copyable(node);
        expect(node.getAttribute('title')).toBe('peak alloc explained');

        node.click();
        await vi.runAllTimersAsync();
        expect(node.getAttribute('title')).toBe('peak alloc explained');
    });

    it('reads and copies from the keyboard', async () => {
        const node = cell('ring <b>2,000</b>');
        expect(node.getAttribute('role')).toBe('button');
        expect(node.getAttribute('tabindex')).toBe('0');

        for (const key of ['Enter', ' ']) {
            node.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
        }
        await vi.runAllTimersAsync();
        expect(writeText).toHaveBeenCalledTimes(2);
        expect(writeText).toHaveBeenLastCalledWith('ring 2,000');
    });

    it('keeps space off the document, so the transport does not also toggle pause', async () => {
        const transport = vi.fn();
        document.addEventListener('keydown', transport);
        const node = cell('p99 <b>12.690 ms</b>');

        node.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
        await vi.runAllTimersAsync();

        document.removeEventListener('keydown', transport);
        expect(writeText).toHaveBeenCalledTimes(1);
        expect(transport).not.toHaveBeenCalled();
    });

    it('ignores keys that are not the button keys', async () => {
        const node = cell('ring <b>2,000</b>');
        node.dispatchEvent(new KeyboardEvent('keydown', { key: 'a', bubbles: true }));
        await vi.runAllTimersAsync();
        expect(writeText).not.toHaveBeenCalled();
    });

    it('leaves no confirmation when the clipboard is unavailable', async () => {
        Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
        const node = cell('ring <b>2,000</b>');
        node.click();
        await vi.runAllTimersAsync();
        expect(node.classList.contains('copied')).toBe(false);
    });
});
