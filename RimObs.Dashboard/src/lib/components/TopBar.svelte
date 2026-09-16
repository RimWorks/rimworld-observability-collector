<script lang="ts">
    import type { StatusResponse, AutoInstrumentCounters } from '../api';
    import { t } from '../i18n';
    import { relativeTime, rate } from '../format';
    import { FRAME_BUDGET_US, tickBudgetUs } from '../frameCost';
    import Icon from './Icon.svelte';
    import Logo from './Logo.svelte';
    import Dialog from './Dialog.svelte';
    import SettingsPopover from './SettingsPopover.svelte';
    import Tooltip from './Tooltip.svelte';
    import { liveVitals } from '../vitals.svelte';

    let {
        status,
        auto = null,
    }: { status: StatusResponse | null; auto?: AutoInstrumentCounters | null } = $props();

    // patch progress while the worker drains its queue; gone once everything is applied.
    let patching = $derived(auto !== null && auto.pending > 0 && auto.matched > 0);
    let patchDone = $derived(auto ? auto.matched - auto.pending : 0);
    let patchPct = $derived(auto && auto.matched > 0 ? (patchDone / auto.matched) * 100 : 0);

    let online = $derived(status?.status === 'running');
    let connected = $derived(!!status?.session);
    let r = $derived(status?.receive ?? null);
    // the frame poll is the fresher source; status is the fallback for an imported bundle or a
    // session with no flamegraph mounted yet.
    let tps = $derived(liveVitals.tps ?? r?.tps ?? null);
    let fps = $derived(liveVitals.fps ?? r?.fps ?? null);
    // real measured costs: tick from the collector's DoSingleTick ema, frame from the ring median.
    let tickMs = $derived(liveVitals.tickMs);
    let frameMs = $derived(
        liveVitals.frameMedianUs !== null ? liveVitals.frameMedianUs / 1000 : null,
    );

    // budget share, the spread meter's idiom: a full bar means the budget is spent.
    let tickShare = $derived(tickMs !== null ? tickMs / (tickBudgetUs(tps) / 1000) : null);
    let frameShare = $derived(frameMs !== null ? frameMs / (FRAME_BUDGET_US / 1000) : null);
    const shareWidth = (share: number) => Math.min(100, share * 100);
    // whole-cost shares, not section heat: trouble means nearing or blowing the budget,
    // or a healthy 10ms frame (22% of budget) would paint orange.
    const costHue = (share: number) => {
        if (share < 0.8) return 'var(--border-strong)';
        return share <= 1 ? 'var(--warn)' : 'var(--bad)';
    };

    // one copy of the bindings, read by the ? overlay.
    const KEY_GROUPS = [
        { id: 'transport', key: 'flamegraph.keys.transport' },
        { id: 'canvas', key: 'flamegraph.keys' },
        { id: 'search', key: 'flamegraph.keys.search' },
    ];

    let keysOpen = $state(false);

    function keydown(e: KeyboardEvent): void {
        const el = e.target as HTMLElement | null;
        if (
            el &&
            (el.tagName === 'INPUT' ||
                el.tagName === 'SELECT' ||
                el.tagName === 'TEXTAREA' ||
                el.isContentEditable)
        )
            return;
        if (e.key === '?') {
            e.preventDefault();
            keysOpen = !keysOpen;
        } else if (e.key === 'Escape' && keysOpen) {
            e.preventDefault();
            keysOpen = false;
        }
    }
</script>

<svelte:window onkeydown={keydown} />

<header class="topbar">
    <div class="crumbs">
        <div class="glyph"><Logo size={24} /></div>
        <h1>RimObs</h1>
        <button class="help" type="button" onclick={() => (keysOpen = true)} data-testid="keys-help"
            >{t('flamegraph.keys.cta')}</button
        >
    </div>

    <div class="right">
        {#if patching}
            <div class="patching" data-testid="patch-progress">
                <span class="patchlabel">{t('topbar.patching')}</span>
                <span class="track"
                    ><span class="fill" style="transform: scaleX({patchPct / 100})"></span></span
                >
                <span class="mono pct">{Math.round(patchPct)}%</span>
            </div>
            <span class="rule"></span>
        {/if}

        {#if tps !== null || fps !== null}
            <div class="vitals" data-testid="vitals">
                {#if tps !== null}
                    <Tooltip
                        text={tickMs !== null
                            ? t('overview.tps.cost')
                                  .replace('{ms}', tickMs.toFixed(2))
                                  .replace('{budget}', (tickBudgetUs(tps) / 1000).toFixed(2))
                            : t('overview.tps')}
                        placement="bottom"
                    >
                        <div class="vital" data-testid="vital-tps">
                            <span class="top"
                                ><b class="mono">{rate(tps)}</b><span class="lbl"
                                    >{t('overview.tps')}</span
                                ></span
                            >
                            {#if tickShare !== null}
                                <span class="costbar"
                                    ><span
                                        class="fill"
                                        style="width: {shareWidth(
                                            tickShare,
                                        )}%; background: {costHue(tickShare)}"
                                        data-testid="tps-cost"
                                    ></span></span
                                >
                            {/if}
                        </div>
                    </Tooltip>
                {/if}
                {#if fps !== null}
                    <Tooltip
                        text={frameMs !== null
                            ? t('overview.fps.cost')
                                  .replace('{ms}', frameMs.toFixed(2))
                                  .replace('{budget}', (FRAME_BUDGET_US / 1000).toFixed(1))
                            : t('overview.fps')}
                        placement="bottom"
                    >
                        <div class="vital" data-testid="vital-fps">
                            <span class="top"
                                ><b class="mono">{rate(fps)}</b><span class="lbl"
                                    >{t('overview.fps')}</span
                                ></span
                            >
                            {#if frameShare !== null}
                                <span class="costbar"
                                    ><span
                                        class="fill"
                                        style="width: {shareWidth(
                                            frameShare,
                                        )}%; background: {costHue(frameShare)}"
                                        data-testid="fps-cost"
                                    ></span></span
                                >
                            {/if}
                        </div>
                    </Tooltip>
                {/if}
            </div>
            <span class="rule"></span>
        {/if}

        {#if status?.update?.available}
            <a class="update" href={status.update.url ?? '#'} target="_blank" rel="noreferrer">
                <Icon name="external" size={14} />
                {status.update.latest_version}
                {t('common.available')}
            </a>
        {/if}

        <div class="health" class:up={online && connected} data-testid="health">
            <span class="dot"></span>
            {#if connected}
                {t('status.running')}
                <span class="ago">{relativeTime(r?.last_batch_utc ?? null)}</span>
            {:else}
                {online ? t('overview.noSession') : t('status.offline')}
            {/if}
        </div>

        <SettingsPopover {status} />
    </div>
</header>

{#if keysOpen}
    <Dialog
        title={t('flamegraph.keys.title')}
        testid="keys-dialog"
        closeTestid="keys-close"
        width="30rem"
        onDismiss={() => (keysOpen = false)}
    >
        {#snippet body()}
            <ul class="keylist">
                {#each KEY_GROUPS as group (group.id)}
                    <li data-testid="keys-{group.id}">{t(group.key)}</li>
                {/each}
            </ul>
        {/snippet}
    </Dialog>
{/if}

<style>
    .topbar {
        grid-area: topbar;
        gap: var(--s-3);
        height: var(--topbar-h);
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 0 var(--s-5);
        border-bottom: 1px solid var(--border-soft);
        background: var(--bg-base);
        position: sticky;
        top: 0;
        /* the settings slideout renders inside this context, so the topbar must outrank
           the bottom pane (40) and status strip (41) or the drawer is trapped under them */
        z-index: 60;
    }
    .crumbs {
        display: flex;
        align-items: center;
        gap: var(--s-3);
        min-width: 0;
    }
    .glyph {
        display: grid;
        place-items: center;
        width: 24px;
        height: 24px;
        flex: none;
    }
    h1 {
        font-family: var(--font-display);
        font-size: 1.15rem;
        letter-spacing: 0.04em;
        flex: none;
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
        color: var(--text-faint);
        border: 1px solid var(--border);
        background: var(--bg-surface);
        border-radius: var(--r-sm);
        padding: var(--s-1) var(--s-3);
    }
    .update:hover {
        color: var(--cyan-soft);
        border-color: var(--border-strong);
    }
    .patching {
        display: flex;
        gap: var(--s-2);
        align-items: center;
        font-size: var(--f-small);
        color: var(--text-dim);
    }
    .patchlabel {
        color: var(--text-dim);
    }
    .patching .track {
        width: 72px;
        height: 4px;
        overflow: hidden;
        background: var(--bg-elev);
        border-radius: 2px;
    }
    .patching .fill {
        display: block;
        width: 100%;
        height: 100%;
        background: var(--cyan);
        border-radius: 2px;
        transform-origin: left;
        transition: transform 300ms linear;
    }
    .patching .pct {
        min-width: 3ch;
        color: var(--text);
    }
    .vitals {
        display: flex;
        align-items: center;
        gap: var(--s-4);
    }
    /* number over unit, so the eye lands on the value and the label stays out of the way */
    .vital {
        display: flex;
        flex-direction: column;
        align-items: flex-end;
        line-height: 1.05;
    }
    .vital b {
        font-size: 1rem;
        font-weight: 600;
        font-variant-numeric: tabular-nums;
        color: var(--text);
    }
    .vital span {
        font-size: 0.6rem;
        text-transform: uppercase;
        letter-spacing: 0.1em;
        color: var(--text-faint);
    }
    .vital .top {
        display: flex;
        align-items: baseline;
        gap: 5px;
    }
    .vital .lbl {
        font-size: 0.6rem;
        text-transform: uppercase;
        letter-spacing: 0.1em;
        color: var(--text-faint);
    }
    .vital .costbar {
        position: relative;
        width: 64px;
        height: 3px;
        margin-top: 2px;
        background: var(--border);
        border-radius: 2px;
        overflow: hidden;
    }
    .vital .costbar .fill {
        position: absolute;
        inset: 0 auto 0 0;
        border-radius: 2px;
    }
    .help {
        flex: none;
        min-height: 28px;
        padding: 0 var(--s-2);
        border: 0;
        background: none;
        cursor: pointer;
        font-size: var(--f-small);
        color: var(--text-faint);
    }
    .help:hover {
        color: var(--text-dim);
    }
    .keylist {
        display: flex;
        flex-direction: column;
        gap: var(--s-2);
        margin: 0;
        padding: 0;
        list-style: none;
        font-size: 0.82rem;
        line-height: 1.45;
        color: var(--text-dim);
    }
    .rule {
        width: 1px;
        height: 22px;
        background: var(--border);
    }
    .health {
        display: inline-flex;
        align-items: center;
        gap: var(--s-2);
        font-size: 0.78rem;
        color: var(--text-dim);
    }
    .ago {
        color: var(--text-faint);
        font-variant-numeric: tabular-nums;
    }
    .dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: var(--text-faint);
    }
    .health.up .dot {
        background: var(--good);
    }

    @media (max-width: 820px) {
        .topbar {
            padding: 0 var(--s-3);
        }
        .right {
            gap: var(--s-2);
            min-width: 0;
        }
        .vitals {
            gap: var(--s-3);
        }
    }
    @media (max-width: 560px) {
        .update,
        .rule,
        .ago {
            display: none;
        }
    }
</style>
