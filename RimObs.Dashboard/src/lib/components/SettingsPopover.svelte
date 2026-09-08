<script lang="ts">
    import { computePosition, autoUpdate, flip, shift, offset } from '@floating-ui/dom';
    import type { StatusResponse } from '../api';
    import { t, getLang, LANGUAGES } from '../i18n';
    import { userPrefs } from '../userPrefs.svelte';
    import Icon from './Icon.svelte';
    import Tooltip from './Tooltip.svelte';

    let { status }: { status: StatusResponse | null } = $props();

    const GAP_PX = 8;
    const EDGE_PX = 8;

    let open = $state(false);
    let btnEl = $state<HTMLElement | null>(null);
    let panelEl = $state<HTMLElement | null>(null);

    // fixed strategy so the topbar's stacking context cannot clip the panel, same as Tooltip.
    $effect(() => {
        const anchor = btnEl;
        const panel = panelEl;
        if (!open || !anchor || !panel) return;

        const stop = autoUpdate(anchor, panel, () => {
            void computePosition(anchor, panel, {
                strategy: 'fixed',
                placement: 'bottom-end',
                middleware: [offset(GAP_PX), flip(), shift({ padding: EDGE_PX })],
            }).then(({ x, y }) => {
                panel.style.left = `${x}px`;
                panel.style.top = `${y}px`;
            });
        });
        return stop;
    });

    $effect(() => {
        if (!open) return;
        function onKey(e: KeyboardEvent): void {
            if (e.key === 'Escape') {
                open = false;
                btnEl?.focus();
            }
        }
        function onPointer(e: PointerEvent): void {
            const target = e.target as Node;
            if (btnEl?.contains(target) || panelEl?.contains(target)) return;
            open = false;
        }
        globalThis.addEventListener('keydown', onKey);
        globalThis.addEventListener('pointerdown', onPointer);
        return () => {
            globalThis.removeEventListener('keydown', onKey);
            globalThis.removeEventListener('pointerdown', onPointer);
        };
    });

    let prom = $derived(status?.exporters);
    let health = $derived(prom?.prometheus_health);
</script>

<button
    class="gear"
    class:open
    bind:this={btnEl}
    type="button"
    aria-label={t('settings.title')}
    aria-expanded={open}
    onclick={() => (open = !open)}
    data-testid="settings-gear"
>
    <Icon name="cog" size={16} />
</button>

{#if open}
    <div class="panel" bind:this={panelEl} role="dialog" aria-label={t('settings.title')}>
        <div class="rows">
            <div class="kv">
                <span class="k">{t('settings.version')}</span>
                <span class="v mono">{status?.version ?? '-'}</span>
            </div>
            <div class="kv">
                <span class="k">{t('settings.schema')}</span>
                <span class="v mono">{status?.schema_version ?? '-'}</span>
            </div>
            <div class="kv">
                <span class="k">{t('settings.language')}</span>
                <select
                    aria-label={t('settings.language')}
                    class="v lang"
                    value={getLang()}
                    onchange={(e) =>
                        userPrefs.setLang((e.currentTarget as HTMLSelectElement).value)}
                >
                    {#each LANGUAGES as l (l.code)}
                        <option value={l.code}>{l.label}</option>
                    {/each}
                </select>
            </div>
            {#if status?.update?.available}
                <div class="kv">
                    <span class="k">{t('settings.update')}</span>
                    <a class="v mono link" href={status.update.url} target="_blank" rel="noreferrer"
                        >{status.update.latest_version}</a
                    >
                </div>
            {/if}
        </div>

        <h3>{t('settings.exporters')}</h3>
        {#if prom}
            <div class="rows">
                <div class="kv">
                    <Tooltip text={t('tip.settings.prometheus')}
                        ><span class="k">{t('settings.prometheus')}</span></Tooltip
                    >
                    <span class="v" class:on={prom.prometheus_enabled}>
                        {prom.prometheus_enabled
                            ? t('settings.exporter.enabled')
                            : t('settings.exporter.disabled')}
                    </span>
                </div>
                {#if prom.prometheus_enabled && health}
                    <div class="kv">
                        <span class="k">{t('settings.exporter.endpoint')}</span>
                        <span class="v mono">/metrics</span>
                    </div>
                    <div class="kv">
                        <span class="k">{t('settings.exporter.last_scrape')}</span>
                        <span class="v mono">{health.last_scrape_utc ?? '-'}</span>
                    </div>
                    <div class="kv">
                        <span class="k">{t('settings.exporter.sample_count')}</span>
                        <span class="v mono">{health.last_sample_count}</span>
                    </div>
                    {#if health.total_errors > 0}
                        <div class="kv">
                            <span class="k">{t('settings.exporter.errors')}</span>
                            <span class="v mono err"
                                >{health.total_errors} · {health.last_error ?? ''}</span
                            >
                        </div>
                    {/if}
                {/if}
            </div>
        {:else}
            <p class="muted">{t('settings.exporter.unavailable')}</p>
        {/if}

        <h3>{t('settings.behavior')}</h3>
        <label class="toggle">
            <input
                type="checkbox"
                checked={userPrefs.closeOnDisconnect}
                onchange={(e) =>
                    userPrefs.setCloseOnDisconnect((e.currentTarget as HTMLInputElement).checked)}
            />
            <span class="toggle-text">
                <span class="toggle-label">{t('settings.close_on_disconnect')}</span>
                <span class="toggle-hint">{t('settings.close_on_disconnect.hint')}</span>
            </span>
        </label>
    </div>
{/if}

<style>
    .gear {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 28px;
        height: 28px;
        padding: 0;
        border: 1px solid var(--border-soft);
        border-radius: 99px;
        background: var(--bg-surface);
        color: var(--text-dim);
        cursor: pointer;
        transition:
            color var(--t-fast) var(--ease-out),
            border-color var(--t-fast) var(--ease-out);
    }
    .gear:hover,
    .gear.open {
        color: var(--cyan);
        border-color: var(--cyan);
    }
    .panel {
        position: fixed;
        top: 0;
        left: 0;
        z-index: 50;
        width: 320px;
        max-height: calc(100vh - var(--topbar-h) - var(--s-5));
        overflow-y: auto;
        padding: var(--s-3) var(--s-4) var(--s-4);
        background: var(--bg-elev);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        box-shadow: 0 10px 28px rgba(0, 0, 0, 0.45);
    }
    h3 {
        margin: var(--s-4) 0 var(--s-1);
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--text-faint);
        font-weight: 600;
    }
    .rows {
        display: flex;
        flex-direction: column;
    }
    .kv {
        display: flex;
        justify-content: space-between;
        gap: var(--s-3);
        padding: var(--s-2) 0;
        border-bottom: 1px solid var(--border-soft);
        align-items: baseline;
    }
    .kv:last-child {
        border-bottom: none;
    }
    .k {
        font-size: 0.74rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    .v {
        font-size: 0.84rem;
        text-align: right;
        word-break: break-all;
    }
    .v.on {
        color: var(--cyan);
    }
    .v.err {
        color: var(--bad);
    }
    .link {
        color: var(--cyan-soft);
    }
    .muted {
        font-size: 0.82rem;
        color: var(--text-faint);
        margin: 0;
    }
    .lang {
        background: var(--bg-surface);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        padding: var(--s-1) var(--s-2);
        font-family: var(--font-ui);
        font-size: 0.82rem;
        cursor: pointer;
    }
    .lang:hover {
        border-color: var(--cyan);
    }
    .toggle {
        display: flex;
        gap: var(--s-3);
        align-items: flex-start;
        cursor: pointer;
        padding-top: var(--s-2);
    }
    .toggle input {
        cursor: pointer;
    }
    .toggle-text {
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
    }
    .toggle-label {
        font-size: 0.86rem;
        color: var(--text);
    }
    .toggle-hint {
        font-size: 0.76rem;
        color: var(--text-faint);
        line-height: 1.4;
    }
</style>
