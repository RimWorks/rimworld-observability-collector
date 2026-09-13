<script lang="ts">
    import type { StatusResponse, AutoInstrumentCounters } from '../api';
    import { t } from '../i18n';
    import { relativeTime, rate } from '../format';
    import Icon from './Icon.svelte';
    import Logo from './Logo.svelte';
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

    // one copy of the bindings, read by both the tooltip and the ? overlay.
    const KEY_GROUPS = [
        { id: 'transport', key: 'flamegraph.keys.transport' },
        { id: 'canvas', key: 'flamegraph.keys' },
        { id: 'search', key: 'flamegraph.keys.search' },
    ];
    let keysText = $derived(
        `${KEY_GROUPS.map((g) => t(g.key)).join(' ')} ${t('flamegraph.keys.hint')}`,
    );

    let keysOpen = $state(false);
    let keysEl = $state<HTMLDialogElement | null>(null);

    // jsdom 25 has no showModal; the optional call keeps the overlay testable there.
    $effect(() => {
        if (keysOpen) keysEl?.showModal?.();
    });

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
        <p class="what">{t('nav.flamegraph.what')}</p>
        <Tooltip text={keysText} placement="bottom">
            <span
                class="help"
                role="img"
                aria-label={t('flamegraph.keys.title')}
                data-testid="keys-help"
            >
                <Icon name="info" size={13} />
            </span>
        </Tooltip>
    </div>

    <div class="right">
        {#if patching}
            <div class="patching" data-testid="patch-progress">
                <span class="patchlabel">{t('topbar.patching')}</span>
                <span class="track"><span class="fill" style="width: {patchPct}%"></span></span>
                <span class="mono pct">{Math.round(patchPct)}%</span>
            </div>
            <span class="rule"></span>
        {/if}

        {#if tps !== null || fps !== null}
            <div class="vitals" data-testid="vitals">
                {#if tps !== null}
                    <div class="vital" data-testid="vital-tps">
                        <b class="mono">{rate(tps)}</b><span
                            >{t('overview.tps')}{#if tickMs !== null}<em class="ms mono"
                                    >{tickMs.toFixed(2)}ms</em
                                >{/if}</span
                        >
                    </div>
                {/if}
                {#if fps !== null}
                    <div class="vital" data-testid="vital-fps">
                        <b class="mono">{rate(fps)}</b><span
                            >{t('overview.fps')}{#if frameMs !== null}<em class="ms mono"
                                    >{frameMs.toFixed(2)}ms</em
                                >{/if}</span
                        >
                    </div>
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
    <dialog
        class="keys"
        bind:this={keysEl}
        onclose={() => (keysOpen = false)}
        aria-label={t('flamegraph.keys.title')}
        data-testid="keys-dialog"
    >
        <h2>{t('flamegraph.keys.title')}</h2>
        <ul>
            {#each KEY_GROUPS as group (group.id)}
                <li data-testid="keys-{group.id}">{t(group.key)}</li>
            {/each}
        </ul>
        <p class="hint">{t('flamegraph.keys.hint')}</p>
    </dialog>
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
        z-index: 5;
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
    .what {
        margin: 0;
        font-size: 0.76rem;
        line-height: 1.3;
        color: var(--text-faint);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        transition:
            color var(--t-fast) var(--ease-out),
            background var(--t-fast) var(--ease-out);
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
        color: var(--cyan-soft);
        border: 1px solid var(--border);
        background: var(--bg-surface);
        border-radius: 99px;
        padding: var(--s-1) var(--s-3);
    }
    .patching {
        display: flex;
        gap: var(--s-2);
        align-items: center;
        font-size: var(--f-small, 11.5px);
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
        height: 100%;
        background: var(--cyan);
        border-radius: 2px;
        transition: width 300ms linear;
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
    .vital .ms {
        font-style: normal;
        text-transform: none;
        letter-spacing: 0;
        margin-left: var(--s-1);
        color: var(--text-dim);
        font-variant-numeric: tabular-nums;
    }
    .help {
        display: grid;
        place-items: center;
        flex: none;
        color: var(--text-faint);
    }
    .help:hover {
        color: var(--text-dim);
    }
    .keys {
        width: min(30rem, calc(100vw - 2rem));
        margin: auto;
        padding: var(--s-5);
        color: var(--text);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-lg);
        box-shadow: 0 18px 48px rgb(0 0 0 / 55%);
    }
    .keys::backdrop {
        background: rgb(0 0 0 / 55%);
    }
    .keys h2 {
        margin: 0 0 var(--s-3);
        font-family: var(--font-display);
        font-size: 1.05rem;
        letter-spacing: 0.03em;
    }
    .keys ul {
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
    .hint {
        margin: var(--s-4) 0 0;
        font-size: 0.76rem;
        color: var(--text-faint);
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
        border: 1px solid var(--border-soft);
        border-radius: 99px;
        padding: var(--s-1) var(--s-3);
        background: var(--bg-surface);
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
        .patching {
            display: flex;
            gap: var(--s-2);
            align-items: center;
            font-size: var(--f-small, 11.5px);
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
            height: 100%;
            background: var(--cyan);
            border-radius: 2px;
            transition: width 300ms linear;
        }
        .patching .pct {
            min-width: 3ch;
            color: var(--text);
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
