<script lang="ts">
    import type { StatusResponse } from '../api';
    import { router } from '../router.svelte';
    import { t } from '../i18n';
    import { relativeTime } from '../format';
    import Icon from './Icon.svelte';

    let { status }: { status: StatusResponse | null } = $props();

    let online = $derived(status?.status === 'running');
    let connected = $derived(!!status?.session);
</script>

<header class="topbar">
    <div class="crumbs">
        <h1>{t(`nav.${router.current}`, router.route.title)}</h1>
    </div>

    <div class="right">
        {#if status?.update?.available}
            <a class="update" href={status.update.url ?? '#'} target="_blank" rel="noreferrer">
                <Icon name="external" size={14} />
                {status.update.latest_version}
                {t('common.available')}
            </a>
        {/if}

        <div class="session" class:connected>
            <span class="dot"></span>
            {#if connected}
                <span class="sid mono">{status?.session?.id}</span>
                <span class="ago">{relativeTime(status?.receive?.last_batch_utc ?? null)}</span>
            {:else}
                <span class="ago">{t('overview.noSession')}</span>
            {/if}
        </div>

        <div class="health" class:up={online}>
            <span class="dot"></span>
            {online ? t('status.running') : t('status.offline')}
        </div>
    </div>
</header>

<style>
    .topbar {
        grid-area: topbar;
        height: var(--topbar-h);
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 0 var(--s-5);
        border-bottom: 1px solid var(--border-soft);
        background: var(--bg-base);
        position: sticky;
        top: 0;
        z-index: 5;
    }
    h1 {
        font-size: 1.15rem;
    }
    .right {
        display: flex;
        align-items: center;
        gap: var(--s-3);
    }
    .update {
        display: inline-flex;
        align-items: center;
        gap: var(--s-1);
        font-size: 0.78rem;
        color: var(--cyan-soft);
        border: 1px solid var(--border);
        background: var(--bg-surface);
        border-radius: 99px;
        padding: var(--s-1) var(--s-3);
    }
    .session,
    .health {
        display: inline-flex;
        align-items: center;
        gap: var(--s-2);
        font-size: 0.78rem;
        color: var(--text-dim);
        border: 1px solid var(--border-soft);
        border-radius: 99px;
        padding: var(--s-1) var(--s-3);
        background: var(--bg-surface);
    }
    .sid {
        color: var(--text);
        max-width: 12rem;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .ago {
        color: var(--text-faint);
    }
    .dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: var(--text-faint);
    }
    .session.connected .dot {
        background: var(--cyan);
    }
    .health.up .dot {
        background: var(--good);
    }

    @media (max-width: 820px) {
        .topbar {
            padding: 0 var(--s-3);
        }
        h1 {
            font-size: 1rem;
        }
        .right {
            gap: var(--s-2);
            min-width: 0;
        }
        .sid {
            max-width: 6rem;
        }
    }
    @media (max-width: 560px) {
        .session,
        .update {
            display: none;
        }
    }
</style>
