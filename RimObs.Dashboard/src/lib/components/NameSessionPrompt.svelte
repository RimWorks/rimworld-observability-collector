<script lang="ts">
    import Dialog from './Dialog.svelte';
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
</script>

<Dialog
    title={t('session.prompt.title')}
    testid="name-session-prompt"
    onDismiss={onSkip}
    width="26rem"
>
    {#snippet body()}
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
    {/snippet}

    {#snippet actions(dismiss: () => void)}
        <button type="button" class="quiet" onclick={onDisable} data-testid="prompt-disable"
            >{t('session.prompt.never')}</button
        >
        <span class="spacer"></span>
        <button type="button" onclick={dismiss} data-testid="prompt-skip"
            >{t('session.prompt.skip')}</button
        >
        <button
            type="button"
            class="primary"
            disabled={name.trim() === ''}
            onclick={() => onName(name.trim())}
            data-testid="prompt-save">{t('session.prompt.save')}</button
        >
    {/snippet}
</Dialog>

<style>
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
</style>
