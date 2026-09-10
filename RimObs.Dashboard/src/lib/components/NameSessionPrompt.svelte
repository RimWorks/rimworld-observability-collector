<script lang="ts">
    import { t } from '../i18n';
    import { MAX_SESSION_NAME } from '../sessionLabel';

    let {
        sessionId,
        onName,
        onSkip,
        onDisable,
    }: {
        sessionId: string;
        onName: (name: string) => void;
        onSkip: () => void;
        onDisable: () => void;
    } = $props();

    let name = $state('');
    let el = $state<HTMLDivElement | null>(null);

    $effect(() => {
        el?.querySelector('input')?.focus();
    });

    function keydown(e: KeyboardEvent): void {
        if (e.key === 'Escape') onSkip();
    }
</script>

<svelte:window onkeydown={keydown} />

<div class="scrim" role="presentation" onclick={onSkip}></div>
<div
    class="dialog"
    role="dialog"
    aria-modal="true"
    aria-label={t('session.prompt.title')}
    bind:this={el}
    data-testid="name-session-prompt"
>
    <h2>{t('session.prompt.title')}</h2>
    <p class="why">{t('session.prompt.explain')}</p>
    <p class="sid mono">{sessionId}</p>

    <input
        type="text"
        bind:value={name}
        maxlength={MAX_SESSION_NAME}
        placeholder={t('session.name.placeholder')}
        aria-label={t('session.name')}
        data-testid="prompt-session-name"
    />

    <div class="actions">
        <button type="button" class="quiet" onclick={onDisable} data-testid="prompt-disable"
            >{t('session.prompt.never')}</button
        >
        <span class="spacer"></span>
        <button type="button" onclick={onSkip} data-testid="prompt-skip"
            >{t('session.prompt.skip')}</button
        >
        <button
            type="button"
            class="primary"
            disabled={name.trim() === ''}
            onclick={() => onName(name.trim())}
            data-testid="prompt-save">{t('session.prompt.save')}</button
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
        width: min(26rem, calc(100vw - 2rem));
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
    .why {
        margin: 0;
        font-size: 0.78rem;
        color: var(--text-faint);
        line-height: 1.4;
    }
    .sid {
        margin: 0;
        font-size: 0.72rem;
        color: var(--text-faint);
        overflow: hidden;
        text-overflow: ellipsis;
    }
    input {
        font: inherit;
        font-size: 0.9rem;
        color: var(--text);
        background: var(--bg-base);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: var(--s-2);
    }
    .actions {
        display: flex;
        align-items: center;
        gap: var(--s-2);
    }
    .spacer {
        flex: 1;
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
    .actions .quiet {
        border-color: transparent;
        background: none;
        color: var(--text-faint);
        padding-left: 0;
    }
    .actions .primary {
        color: var(--bg-void);
        background: var(--cyan);
        border-color: var(--cyan);
        font-weight: 600;
    }
    .actions .primary:disabled {
        opacity: 0.45;
        cursor: default;
    }
</style>
