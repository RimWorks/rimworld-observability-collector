<script lang="ts">
    import type { Snippet } from 'svelte';
    import type { LoadState } from '../poll.svelte';
    import { t } from '../i18n';
    import Icon from './Icon.svelte';

    let {
        state,
        error = '',
        empty = false,
        emptyTitle,
        emptyHint,
        onretry,
        children,
    }: {
        state: LoadState;
        error?: string;
        empty?: boolean;
        emptyTitle?: string;
        emptyHint?: string;
        onretry?: () => void;
        children: Snippet;
    } = $props();
</script>

{#if state === 'loading'}
    <div class="msg" role="status" aria-live="polite">
        <Icon name="dot" size={28} />
        <span>{t('status.loading')}</span>
    </div>
{:else if state === 'error'}
    <div class="msg err" role="alert" aria-live="assertive">
        <Icon name="alert" size={28} />
        <span>{t('common.error')}</span>
        <code>{error}</code>
        {#if onretry}<button onclick={onretry}>{t('common.retry')}</button>{/if}
    </div>
{:else if empty}
    <div class="msg">
        <Icon name="dot" size={28} />
        <span>{emptyTitle ?? t('common.empty')}</span>
        <p>{emptyHint ?? t('common.empty.hint')}</p>
    </div>
{:else}
    {@render children()}
{/if}

<style>
    .msg {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-7) var(--s-4);
        color: var(--text-dim);
        text-align: center;
    }
    .msg p {
        margin: 0;
        max-width: 26rem;
        color: var(--text-faint);
        font-size: 0.85rem;
    }
    .msg code {
        color: var(--text-faint);
        font-size: 0.78rem;
    }
    .err {
        color: var(--bad);
    }
    button {
        margin-top: var(--s-2);
        background: var(--bg-elev);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        padding: var(--s-2) var(--s-4);
        cursor: pointer;
        font-family: var(--font-ui);
        transition: border-color var(--t-fast) var(--ease-out);
    }
    button:hover {
        border-color: var(--cyan);
    }
</style>
