<script lang="ts">
    import Tooltip from './Tooltip.svelte';
    import { count } from '../format';
    import { t } from '../i18n';

    let {
        ringHeld,
        ringCapacity,
        overheadText,
        timerResLine,
        deltaUs,
        deltaSeverity,
        deltaText,
    }: {
        ringHeld: number;
        ringCapacity: number | null;
        overheadText: string;
        timerResLine: string;
        deltaUs: number | null;
        deltaSeverity: -1 | 0 | 1;
        deltaText: string;
    } = $props();
</script>

<p class="status mono" data-testid="status-footer">
    <Tooltip text={t('tip.footer.ring')}>
        <span data-testid="footer-ring">
            {t('flamegraph.ringsize')}
            <b>{count(ringHeld)}{ringCapacity !== null ? `/${count(ringCapacity)}` : ''}</b>
        </span>
    </Tooltip>
    &middot;
    <Tooltip text={t('tip.footer.memory')}>
        <span data-testid="footer-memory">
            {t('footer.memory')} <b>{t('footer.memory.unavailable')}</b>
        </span>
    </Tooltip>
    {#if overheadText}
        &middot;
        <Tooltip text={t('flamegraph.overhead.hint')}>
            <span data-testid="footer-overhead">{overheadText}</span>
        </Tooltip>
    {/if}
    {#if timerResLine}
        &middot;
        <Tooltip text={t('tip.footer.timerres')}>
            <span data-testid="footer-timerres">{timerResLine}</span>
        </Tooltip>
    {/if}
    {#if deltaUs !== null}
        &middot;
        <Tooltip text={t('tip.footer.delta')}>
            <span
                class="delta"
                class:warn={deltaSeverity === 1}
                class:cool={deltaSeverity === -1}
                data-testid="footer-delta">&Delta; {deltaText}</span
            >
        </Tooltip>
    {/if}
</p>

<style>
    .status {
        position: fixed;
        left: 0;
        right: 0;
        bottom: 0;
        z-index: 41;
        margin: 0;
        display: flex;
        align-items: center;
        gap: var(--s-1);
        flex-wrap: wrap;
        height: var(--status-h);
        padding: 0 12px;
        font-size: 0.7rem;
        line-height: var(--status-h);
        color: var(--text-faint);
        background: var(--bg-void);
        border-top: 1px solid var(--border-soft);
    }
    .status b {
        color: var(--text-dim);
        font-weight: 500;
    }
    .delta {
        color: var(--text-dim);
    }
    .delta.warn {
        color: var(--warn);
    }
    .delta.cool {
        color: var(--good);
    }
</style>
