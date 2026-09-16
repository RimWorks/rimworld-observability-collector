<script lang="ts">
    import type { Snippet } from 'svelte';
    import { t } from '../i18n';

    let {
        title,
        testid,
        width = '28rem',
        closeTestid,
        onDismiss,
        body,
        actions,
    }: {
        title: string;
        testid: string;
        width?: string;
        closeTestid?: string;
        onDismiss: () => void;
        body: Snippet;
        actions?: Snippet<[() => void]>;
    } = $props();

    let open = $state(true);
    let el = $state<HTMLDialogElement | null>(null);

    // jsdom 25 has no showModal; the optional call keeps the dialog testable there.
    $effect(() => {
        if (!open) return;
        el?.showModal?.();
        el?.querySelector<HTMLElement>(
            'input, select, textarea, button, [href], [tabindex]:not([tabindex="-1"])',
        )?.focus();
    });

    function dismiss(): void {
        if (!open) return;
        // close() runs the native close steps, which hand focus back to the opener.
        // The close event re-enters here to unmount.
        if (el?.open) {
            el.close();
            return;
        }
        open = false;
        onDismiss();
    }

    function keydown(e: KeyboardEvent): void {
        if (e.key === 'Escape') dismiss();
    }
</script>

<svelte:window onkeydown={keydown} />

{#if open}
    <dialog
        class="dialog"
        style="--dialog-w: {width}"
        aria-label={title}
        bind:this={el}
        onclose={dismiss}
        oncancel={dismiss}
        onclick={(e) => {
            if (e.target === el) dismiss();
        }}
        data-testid={testid}
    >
        <div class="body">
            <div class="head">
                <h2>{title}</h2>
                {#if closeTestid}
                    <button
                        type="button"
                        class="close"
                        aria-label={t('common.close')}
                        onclick={dismiss}
                        data-testid={closeTestid}>&times;</button
                    >
                {/if}
            </div>
            {@render body()}
            {#if actions}
                <div class="actions">{@render actions(dismiss)}</div>
            {/if}
        </div>
    </dialog>
{/if}

<style>
    .dialog {
        width: min(var(--dialog-w), calc(100vw - 2rem));
        margin: auto;
        padding: 0;
        color: var(--text);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-lg);
        box-shadow: 0 18px 48px rgb(0 0 0 / 55%);
    }
    .dialog::backdrop {
        background: rgb(0 0 0 / 55%);
    }
    /* padding lives here so a click on the dialog element itself is a backdrop click */
    .body {
        display: flex;
        flex-direction: column;
        gap: var(--s-3);
        padding: var(--s-5);
    }
    .head {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--s-3);
    }
    h2 {
        margin: 0;
        font-family: var(--font-display);
        font-size: 1.05rem;
        letter-spacing: 0.03em;
    }
    .close {
        font: inherit;
        font-size: 1.1rem;
        line-height: 1;
        color: var(--text-dim);
        background: none;
        border: 0;
        padding: 2px 6px;
        cursor: pointer;
    }
    .close:hover {
        color: var(--text);
    }
    .actions {
        display: flex;
        align-items: center;
        justify-content: flex-end;
        gap: var(--s-2);
        margin-top: var(--s-2);
    }
    .actions :global(button) {
        font: inherit;
        font-size: 0.82rem;
        color: var(--text-dim);
        background: var(--bg-base);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: var(--s-2) var(--s-4);
        cursor: pointer;
    }
    .actions :global(.primary) {
        color: var(--bg-void);
        background: var(--cyan);
        border-color: var(--cyan);
        font-weight: 600;
    }
    .actions :global(.quiet) {
        border-color: transparent;
        background: none;
        color: var(--text-faint);
        padding-left: 0;
    }
    .actions :global(button:disabled) {
        opacity: var(--o-disabled);
        cursor: default;
    }
    .actions :global(.spacer) {
        flex: 1;
    }
</style>
