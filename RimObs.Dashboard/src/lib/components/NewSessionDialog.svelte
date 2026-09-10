<script lang="ts">
    import { t } from '../i18n';
    import { MAX_SESSION_NAME } from '../sessionLabel';

    let {
        onConfirm,
        onCancel,
    }: {
        onConfirm: (name: string, save: boolean) => void;
        onCancel: () => void;
    } = $props();

    let name = $state('');
    let save = $state(true);
    let dialogEl = $state<HTMLDivElement | null>(null);

    $effect(() => {
        dialogEl?.querySelector('input')?.focus();
    });

    function keydown(e: KeyboardEvent): void {
        if (e.key === 'Escape') onCancel();
    }
</script>

<svelte:window onkeydown={keydown} />

<div class="scrim" role="presentation" onclick={onCancel} data-testid="new-session-scrim"></div>
<div
    class="dialog"
    role="dialog"
    aria-modal="true"
    aria-label={t('flamegraph.newSession')}
    bind:this={dialogEl}
    data-testid="new-session-dialog"
>
    <h2>{t('flamegraph.newSession')}</h2>
    <p class="why">{t('session.restart.explain')}</p>

    <label class="field">
        <span>{t('session.name')}</span>
        <input
            type="text"
            bind:value={name}
            maxlength={MAX_SESSION_NAME}
            placeholder={t('session.name.placeholder')}
            data-testid="new-session-name"
        />
    </label>

    <label class="check">
        <input type="checkbox" bind:checked={save} data-testid="new-session-save" />
        {t('session.restart.save')}
    </label>
    <p class="warn">{t('session.restart.unsaved')}</p>

    <div class="actions">
        <button type="button" onclick={onCancel} data-testid="new-session-cancel"
            >{t('common.cancel')}</button
        >
        <button
            type="button"
            class="primary"
            onclick={() => onConfirm(name.trim(), save)}
            data-testid="new-session-confirm">{t('session.restart.confirm')}</button
        >
    </div>
</div>

<style>
    .scrim {
        position: fixed;
        inset: 0;
        background: rgb(0 0 0 / 55%);
        z-index: 60;
    }
    .dialog {
        position: fixed;
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        z-index: 61;
        width: min(28rem, calc(100vw - 2rem));
        display: flex;
        flex-direction: column;
        gap: var(--s-3);
        padding: var(--s-5);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-lg);
        box-shadow: 0 18px 48px rgb(0 0 0 / 55%);
    }
    h2 {
        margin: 0;
        font-family: var(--font-display);
        font-size: 1.05rem;
        letter-spacing: 0.03em;
    }
    .why,
    .warn {
        margin: 0;
        font-size: 0.78rem;
        color: var(--text-faint);
        line-height: 1.4;
    }
    .warn {
        color: var(--warn);
    }
    .field {
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
        font-size: 0.78rem;
        color: var(--text-dim);
    }
    .field input {
        font: inherit;
        font-size: 0.9rem;
        color: var(--text);
        background: var(--bg-base);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: var(--s-2);
    }
    .check {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        font-size: 0.82rem;
        color: var(--text-dim);
    }
    .actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--s-2);
        margin-top: var(--s-2);
    }
    .actions button {
        font: inherit;
        font-size: 0.82rem;
        color: var(--text-dim);
        background: var(--bg-base);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: var(--s-2) var(--s-4);
        cursor: pointer;
    }
    .actions .primary {
        color: var(--bg-void);
        background: var(--cyan);
        border-color: var(--cyan);
        font-weight: 600;
    }
</style>
