import { t } from './i18n';

const CONFIRM_MS = 1200;

/**
 * Makes a stat cell click-to-copy. The copied string is the cell's own text, so the label
 * rides along with the number: "p99 12.690 ms".
 */
export function copyable(node: HTMLElement): { destroy(): void } {
    const ownTitle = node.getAttribute('title');
    let timer: ReturnType<typeof setTimeout> | undefined;

    node.setAttribute('role', 'button');
    node.setAttribute('tabindex', '0');
    node.classList.add('copyable');
    if (ownTitle === null) node.setAttribute('title', t('copy.hint'));

    function confirm(): void {
        node.classList.add('copied');
        node.setAttribute('title', t('copy.copied'));
        clearTimeout(timer);
        timer = setTimeout(() => {
            node.classList.remove('copied');
            if (ownTitle === null) node.setAttribute('title', t('copy.hint'));
            else node.setAttribute('title', ownTitle);
        }, CONFIRM_MS);
    }

    function copy(): void {
        const text = (node.textContent ?? '').replace(/\s+/g, ' ').trim();
        if (!text) return;
        const written = navigator.clipboard?.writeText(text);
        if (written === undefined) return;
        void written.then(confirm, () => {});
    }

    function onKey(e: KeyboardEvent): void {
        if (e.key !== 'Enter' && e.key !== ' ') return;
        e.preventDefault();
        e.stopPropagation();
        copy();
    }

    node.addEventListener('click', copy);
    node.addEventListener('keydown', onKey);

    return {
        destroy() {
            clearTimeout(timer);
            node.removeEventListener('click', copy);
            node.removeEventListener('keydown', onKey);
        },
    };
}
