<script lang="ts">
    import Dialog from './Dialog.svelte';
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
</script>

<Dialog
    title={t('flamegraph.newSession')}
    testid="new-session-dialog"
    onDismiss={onCancel}
    width="28rem"
>
    {#snippet body()}
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
    {/snippet}

    {#snippet actions(dismiss: () => void)}
        <button type="button" onclick={dismiss} data-testid="new-session-cancel"
            >{t('common.cancel')}</button
        >
        <button
            type="button"
            class="primary"
            onclick={() => onConfirm(name.trim(), save)}
            data-testid="new-session-confirm">{t('session.restart.confirm')}</button
        >
    {/snippet}
</Dialog>

<style>
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
</style>
