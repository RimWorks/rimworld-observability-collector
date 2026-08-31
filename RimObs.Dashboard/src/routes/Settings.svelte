<script lang="ts">
    import { api, type StatusResponse } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import Card from '../lib/components/Card.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import { t, getLang, LANGUAGES } from '../lib/i18n';
    import { userPrefs } from '../lib/userPrefs.svelte';
    import { onMount, onDestroy } from 'svelte';

    const res = new Resource<StatusResponse>(() => api.status(), 10000);
    onMount(() => res.start());
    onDestroy(() => res.stop());

    let status = $derived(res.data);
</script>

<Card title={t('settings.title')}>
    <div class="rows">
        <div class="kv">
            <Tooltip text={t('tip.settings.version')}
                ><span class="k">{t('settings.version')}</span></Tooltip
            >
            <span class="v mono">{status?.version ?? '-'}</span>
        </div>
        <div class="kv">
            <Tooltip text={t('tip.settings.schema')}
                ><span class="k">{t('settings.schema')}</span></Tooltip
            >
            <span class="v mono">{status?.schema_version ?? '-'}</span>
        </div>
        <div class="kv">
            <Tooltip text={t('tip.settings.language')}
                ><span class="k">{t('settings.language')}</span></Tooltip
            >
            <select
                aria-label={t('settings.language')}
                class="v lang"
                value={getLang()}
                onchange={(e) => userPrefs.setLang((e.currentTarget as HTMLSelectElement).value)}
            >
                {#each LANGUAGES as l (l.code)}
                    <option value={l.code}>{l.label}</option>
                {/each}
            </select>
        </div>
        {#if status?.update?.available}
            <div class="kv update">
                <span class="k">{t('settings.update')}</span>
                <a class="v mono" href={status.update.url} target="_blank" rel="noreferrer"
                    >{status.update.latest_version}</a
                >
            </div>
        {/if}
    </div>
</Card>

<Card title={t('settings.exporters')}>
    {#if status?.exporters}
        <div class="rows">
            <div class="kv">
                <Tooltip text={t('tip.settings.prometheus')}
                    ><span class="k">{t('settings.prometheus')}</span></Tooltip
                >
                <span class="v" class:on={status.exporters.prometheus_enabled}>
                    {status.exporters.prometheus_enabled
                        ? t('settings.exporter.enabled')
                        : t('settings.exporter.disabled')}
                </span>
            </div>
            {#if status.exporters.prometheus_enabled}
                <div class="kv">
                    <span class="k">{t('settings.exporter.endpoint')}</span>
                    <span class="v mono">/metrics</span>
                </div>
                <div class="kv">
                    <span class="k">{t('settings.exporter.last_scrape')}</span>
                    <span class="v mono"
                        >{status.exporters.prometheus_health.last_scrape_utc ?? '-'}</span
                    >
                </div>
                <div class="kv">
                    <span class="k">{t('settings.exporter.sample_count')}</span>
                    <span class="v mono"
                        >{status.exporters.prometheus_health.last_sample_count}</span
                    >
                </div>
                {#if status.exporters.prometheus_health.total_errors > 0}
                    <div class="kv">
                        <span class="k">{t('settings.exporter.errors')}</span>
                        <span class="v mono err"
                            >{status.exporters.prometheus_health.total_errors} ·
                            {status.exporters.prometheus_health.last_error ?? ''}</span
                        >
                    </div>
                {/if}
            {/if}
        </div>
    {:else}
        <p class="muted">{t('settings.exporter.unavailable')}</p>
    {/if}
</Card>

<Card title={t('settings.behavior')}>
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
</Card>

<style>
    .rows {
        display: flex;
        flex-direction: column;
    }
    .kv {
        display: flex;
        justify-content: space-between;
        gap: var(--s-4);
        padding: var(--s-3) 0;
        border-bottom: 1px solid var(--border-soft);
        align-items: baseline;
    }
    .kv:last-child {
        border-bottom: none;
    }
    .k {
        font-size: 0.78rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    .v {
        font-size: 0.88rem;
    }
    .update .v {
        color: var(--cyan-soft);
    }
    .v.on {
        color: var(--cyan);
    }
    .v.err {
        color: var(--bad);
    }
    .muted {
        font-size: 0.85rem;
        color: var(--text-faint);
        margin: 0;
    }
    .lang {
        background: var(--bg-elev);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        padding: var(--s-1) var(--s-2);
        font-family: var(--font-ui);
        font-size: 0.85rem;
        cursor: pointer;
        transition: border-color var(--t-fast) var(--ease-out);
    }
    .lang:hover {
        border-color: var(--cyan);
    }
    .toggle {
        display: flex;
        gap: var(--s-3);
        align-items: flex-start;
        cursor: pointer;
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
        font-size: 0.92rem;
        color: var(--text);
    }
    .toggle-hint {
        font-size: 0.78rem;
        color: var(--text-faint);
        line-height: 1.4;
    }
</style>
