<script lang="ts">
    import { api, ApiError, type SessionInfo, type ComparisonResponse } from '../lib/api';
    import Card from '../lib/components/Card.svelte';
    import Icon from '../lib/components/Icon.svelte';
    import { ns, count, metricKind } from '../lib/format';
    import { signedNs, signedPercent, deltaTone } from '../lib/comparison';
    import { t } from '../lib/i18n';
    import { onMount } from 'svelte';

    type ImportedBundle = { value: string; label: string };

    let sessions = $state<SessionInfo[]>([]);
    let sessionsError = $state('');
    let imports = $state<ImportedBundle[]>([]);
    let importing = $state(false);
    let importError = $state('');
    let base = $state('');
    let head = $state('');
    let result = $state<ComparisonResponse | null>(null);
    let loading = $state(false);
    let error = $state('');

    onMount(async () => {
        try {
            const res = await api.sessions();
            sessions = res.sessions;
            if (sessions.length > 0) head = sessions[0].id;
            if (sessions.length > 1) base = sessions[1].id;
        } catch (e) {
            sessionsError = e instanceof ApiError ? e.message : String(e);
        }
    });

    async function importBundle(e: Event) {
        const input = e.currentTarget as HTMLInputElement;
        const file = input.files?.[0];
        if (!file) return;
        importing = true;
        importError = '';
        try {
            const res = await api.importBundle(file);
            const sessionId = String(res.manifest.session_id ?? file.name);
            const value = `bundle:${res.token}`;
            imports = [
                {
                    value,
                    label: t('comparison.importedLabel', '{id} (imported)').replace(
                        '{id}',
                        sessionId,
                    ),
                },
                ...imports.filter((b) => b.value !== value),
            ];
            if (!base) base = value;
            else if (!head) head = value;
        } catch (err) {
            importError = err instanceof ApiError ? err.message : String(err);
        } finally {
            importing = false;
            input.value = '';
        }
    }

    async function runCompare() {
        if (!base || !head || base === head) return;
        loading = true;
        error = '';
        try {
            result = await api.compareSessions(base, head);
        } catch (e) {
            error = e instanceof ApiError ? e.message : String(e);
            result = null;
        } finally {
            loading = false;
        }
    }

    let canCompare = $derived(base !== '' && head !== '' && base !== head && !loading);
    let regressionCount = $derived(
        result ? result.hotspots.filter((h) => h.likely_regression_candidate).length : 0,
    );
</script>

<div class="page">
    <Card title={t('comparison.pick', 'Select sessions to compare')}>
        {#if sessionsError}
            <p class="err">{sessionsError}</p>
        {:else}
            <div class="picker">
                <label class="field">
                    <span class="lbl">{t('comparison.base', 'Baseline')}</span>
                    <select bind:value={base}>
                        <option value="" disabled
                            >{t('comparison.choose', 'Choose a source')}</option
                        >
                        <optgroup label={t('comparison.sourceSessions', 'Sessions')}>
                            {#each sessions as s (s.id)}
                                <option value={s.id}
                                    >{s.id}{s.is_current
                                        ? ` (${t('sessions.current', 'current')})`
                                        : ''}</option
                                >
                            {/each}
                        </optgroup>
                        {#if imports.length > 0}
                            <optgroup label={t('comparison.sourceBundles', 'Imported bundles')}>
                                {#each imports as b (b.value)}
                                    <option value={b.value}>{b.label}</option>
                                {/each}
                            </optgroup>
                        {/if}
                    </select>
                </label>
                <Icon name="compare" size={20} />
                <label class="field">
                    <span class="lbl">{t('comparison.head', 'Comparison target')}</span>
                    <select bind:value={head}>
                        <option value="" disabled
                            >{t('comparison.choose', 'Choose a source')}</option
                        >
                        <optgroup label={t('comparison.sourceSessions', 'Sessions')}>
                            {#each sessions as s (s.id)}
                                <option value={s.id}
                                    >{s.id}{s.is_current
                                        ? ` (${t('sessions.current', 'current')})`
                                        : ''}</option
                                >
                            {/each}
                        </optgroup>
                        {#if imports.length > 0}
                            <optgroup label={t('comparison.sourceBundles', 'Imported bundles')}>
                                {#each imports as b (b.value)}
                                    <option value={b.value}>{b.label}</option>
                                {/each}
                            </optgroup>
                        {/if}
                    </select>
                </label>
                <button class="run" onclick={runCompare} disabled={!canCompare}>
                    {loading
                        ? t('comparison.comparing', 'Comparing…')
                        : t('comparison.run', 'Compare')}
                </button>
            </div>
            <div class="import">
                <label class="import-btn" class:busy={importing}>
                    <Icon name="upload" size={16} />
                    <span
                        >{importing
                            ? t('comparison.importing', 'Importing…')
                            : t('comparison.importBundle', 'Import bundle to compare')}</span
                    >
                    <input type="file" accept=".zip" onchange={importBundle} disabled={importing} />
                </label>
                <span class="import-hint"
                    >{t(
                        'comparison.importHint',
                        'Add an exported .rimobs.zip as a comparison source.',
                    )}</span
                >
            </div>
            {#if importError}
                <p class="err">{importError}</p>
            {/if}
            {#if base !== '' && base === head}
                <p class="hint">{t('comparison.samePair', 'Pick two different sources.')}</p>
            {/if}
        {/if}
    </Card>

    {#if error}
        <Card><p class="err">{error}</p></Card>
    {/if}

    {#if result}
        <p class="disclaimer" role="note">
            <Icon name="alert" size={16} />
            {t('comparison.disclaimer', result.disclaimer)}
        </p>

        {#if result.warnings.length > 0}
            <Card title={t('comparison.warnings', 'Confidence warnings')}>
                <ul class="warnings">
                    {#each result.warnings as w (w)}
                        <li>{w}</li>
                    {/each}
                </ul>
            </Card>
        {/if}

        <Card title={t('comparison.timing', 'Tick timing comparison')}>
            <div class="stats">
                <div class="stat">
                    <span class="k">{t('comparison.totalTime', 'Total section time')}</span>
                    <span class="v"
                        >{ns(result.timing.base_total_ns)} → {ns(result.timing.head_total_ns)}</span
                    >
                    <span
                        class="d {deltaTone(
                            result.timing.delta_ns > 0
                                ? 'regressed'
                                : result.timing.delta_ns < 0
                                  ? 'improved'
                                  : 'unchanged',
                        )}"
                        >{signedNs(result.timing.delta_ns)} ({signedPercent(
                            result.timing.delta_percent,
                        )})</span
                    >
                </div>
                <div class="stat">
                    <span class="k">{t('comparison.meanTime', 'Mean per sample')}</span>
                    <span class="v"
                        >{ns(result.timing.base_mean_ns)} → {ns(result.timing.head_mean_ns)}</span
                    >
                    <span
                        class="d {deltaTone(
                            result.timing.delta_mean_ns > 0
                                ? 'regressed'
                                : result.timing.delta_mean_ns < 0
                                  ? 'improved'
                                  : 'unchanged',
                        )}">{signedNs(result.timing.delta_mean_ns)}</span
                    >
                </div>
                <div class="stat">
                    <span class="k">{t('comparison.samples', 'Samples')}</span>
                    <span class="v"
                        >{count(result.timing.base_sample_count)} → {count(
                            result.timing.head_sample_count,
                        )}</span
                    >
                </div>
                <div class="stat">
                    <span class="k"
                        >{t('comparison.candidates', 'Likely regression candidates')}</span
                    >
                    <span class="v">{count(regressionCount)}</span>
                </div>
            </div>
        </Card>

        <Card title={t('comparison.hotspots', 'Hotspot deltas')}>
            <div class="table">
                <div class="head row5">
                    <span>{t('comparison.col.section', 'Section')}</span>
                    <span>{t('comparison.col.owner', 'Owner')}</span>
                    <span class="num">{t('comparison.col.base', 'Base')}</span>
                    <span class="num">{t('comparison.col.head', 'Head')}</span>
                    <span class="num">{t('comparison.col.delta', 'Delta')}</span>
                </div>
                {#each result.hotspots.slice(0, 50) as h (h.name)}
                    <div class="rowline row5" class:flag={h.likely_regression_candidate}>
                        <span class="name mono"
                            >{h.name}{#if h.likely_regression_candidate}<span class="badge"
                                    >{t(
                                        'comparison.candidate',
                                        'Likely regression candidate',
                                    )}</span
                                >{/if}</span
                        >
                        <span class="cell mono">{h.owner}</span>
                        <span class="cell num mono">{ns(h.base_total_ns)}</span>
                        <span class="cell num mono">{ns(h.head_total_ns)}</span>
                        <span class="cell num mono d {deltaTone(h.status)}"
                            >{signedNs(h.delta_ns)} ({signedPercent(h.delta_percent)})</span
                        >
                    </div>
                {/each}
            </div>
        </Card>

        <Card title={t('comparison.modCosts', 'Mod cost changes')}>
            <div class="table">
                <div class="head row4">
                    <span>{t('comparison.col.owner', 'Owner')}</span>
                    <span class="num">{t('comparison.col.base', 'Base')}</span>
                    <span class="num">{t('comparison.col.head', 'Head')}</span>
                    <span class="num">{t('comparison.col.delta', 'Delta')}</span>
                </div>
                {#each result.mod_costs as m (m.owner)}
                    <div class="rowline row4" class:flag={m.likely_regression_candidate}>
                        <span class="name mono"
                            >{m.owner}{#if m.likely_regression_candidate}<span class="badge"
                                    >{t(
                                        'comparison.candidate',
                                        'Likely regression candidate',
                                    )}</span
                                >{/if}</span
                        >
                        <span class="cell num mono">{ns(m.base_total_ns)}</span>
                        <span class="cell num mono">{ns(m.head_total_ns)}</span>
                        <span class="cell num mono d {deltaTone(m.status)}"
                            >{signedNs(m.delta_ns)} ({signedPercent(m.delta_percent)})</span
                        >
                    </div>
                {/each}
            </div>
        </Card>

        {#if result.metrics.length > 0}
            <Card title={t('comparison.metrics', 'Metric changes')}>
                <div class="table">
                    <div class="head row5">
                        <span>{t('comparison.col.metric', 'Metric')}</span>
                        <span>{t('comparison.col.kind', 'Kind')}</span>
                        <span class="num">{t('comparison.col.base', 'Base')}</span>
                        <span class="num">{t('comparison.col.head', 'Head')}</span>
                        <span class="num">{t('comparison.col.delta', 'Delta')}</span>
                    </div>
                    {#each result.metrics as m (m.name)}
                        <div class="rowline row5">
                            <span class="name mono">{m.name}</span>
                            <span class="cell mono">{metricKind(m.kind)}</span>
                            <span class="cell num mono">{count(m.base_value)}</span>
                            <span class="cell num mono">{count(m.head_value)}</span>
                            <span class="cell num mono d {deltaTone(m.status)}"
                                >{m.delta_value >= 0 ? '+' : ''}{count(m.delta_value)} ({signedPercent(
                                    m.delta_percent,
                                )})</span
                            >
                        </div>
                    {/each}
                </div>
            </Card>
        {/if}

        <Card title={t('comparison.loadOrder', 'Load order changes')}>
            <p class="hint">
                {t(
                    'comparison.loadOrderNote',
                    'Owners are derived from section name prefixes, not the game load order.',
                )}
            </p>
            <div class="lo">
                <div class="lo-col">
                    <span class="lo-h new"
                        >{t('comparison.added', 'Added')} ({result.load_order.added.length})</span
                    >
                    {#each result.load_order.added as o (o)}<span class="chip new">{o}</span>{/each}
                </div>
                <div class="lo-col">
                    <span class="lo-h gone"
                        >{t('comparison.removed', 'Removed')} ({result.load_order.removed
                            .length})</span
                    >
                    {#each result.load_order.removed as o (o)}<span class="chip gone">{o}</span
                        >{/each}
                </div>
                <div class="lo-col">
                    <span class="lo-h"
                        >{t('comparison.common', 'In both')} ({result.load_order.common
                            .length})</span
                    >
                    {#each result.load_order.common as o (o)}<span class="chip">{o}</span>{/each}
                </div>
            </div>
        </Card>
    {/if}
</div>

<style>
    .page {
        display: flex;
        flex-direction: column;
        gap: var(--s-4);
    }
    .picker {
        display: flex;
        align-items: flex-end;
        gap: var(--s-4);
        flex-wrap: wrap;
    }
    .field {
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
        min-width: 220px;
    }
    .lbl {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    select {
        background: var(--bg-elev);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        padding: var(--s-2) var(--s-2);
        font-family: var(--font-ui);
    }
    .run {
        background: var(--bg-elev);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        padding: var(--s-2) var(--s-4);
        cursor: pointer;
        font-family: var(--font-ui);
        transition: border-color var(--t-fast) var(--ease-out);
    }
    .run:hover:not(:disabled) {
        border-color: var(--cyan);
    }
    .run:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }
    .import {
        display: flex;
        align-items: center;
        gap: var(--s-3);
        margin-top: var(--s-4);
        flex-wrap: wrap;
    }
    .import-btn {
        display: inline-flex;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-2) var(--s-3);
        border: 1px dashed var(--border);
        border-radius: var(--r-md);
        color: var(--text-dim);
        font-size: 0.82rem;
        cursor: pointer;
        transition:
            border-color var(--t-fast) var(--ease-out),
            color var(--t-fast) var(--ease-out);
    }
    .import-btn:hover {
        border-color: var(--cyan);
        color: var(--cyan);
    }
    .import-btn.busy {
        opacity: 0.6;
        cursor: progress;
    }
    .import-btn input {
        display: none;
    }
    .import-hint {
        font-size: 0.78rem;
        color: var(--text-faint);
    }
    .disclaimer {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        margin: 0;
        padding: var(--s-2) var(--s-4);
        font-size: 0.82rem;
        color: var(--text-dim);
        background: color-mix(in srgb, var(--warn) 8%, transparent);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
    }
    .warnings {
        margin: 0;
        padding-left: var(--s-4);
        color: var(--text-dim);
        font-size: 0.84rem;
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
    }
    .stats {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
        gap: var(--s-3) var(--s-5);
    }
    .stat {
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
        padding: var(--s-2) 0;
        border-bottom: 1px solid var(--border-soft);
    }
    .stat .k {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    .stat .v {
        font-size: 0.9rem;
        color: var(--text);
    }
    .table {
        display: flex;
        flex-direction: column;
        overflow-x: auto;
    }
    .head,
    .rowline {
        display: grid;
        gap: var(--s-3);
        align-items: center;
        padding: var(--s-2) var(--s-2);
        min-width: 560px;
    }
    .row5 {
        grid-template-columns: minmax(0, 2.2fr) minmax(80px, 1fr) repeat(3, minmax(96px, 1fr));
    }
    .row4 {
        grid-template-columns: minmax(0, 2.2fr) repeat(3, minmax(96px, 1fr));
    }
    .head {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--text-faint);
        border-bottom: 1px solid var(--border);
    }
    .rowline {
        border-bottom: 1px solid var(--border-soft);
    }
    .rowline.flag {
        background: color-mix(in srgb, var(--warn) 8%, transparent);
    }
    .num {
        text-align: right;
        justify-self: end;
    }
    .name {
        font-size: 0.84rem;
        color: var(--text);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        display: flex;
        align-items: center;
        gap: var(--s-2);
    }
    .cell {
        font-size: 0.82rem;
        color: var(--text-dim);
    }
    .badge {
        font-size: 0.62rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: var(--warn);
        border: 1px solid var(--border);
        border-radius: 99px;
        padding: var(--s-0) var(--s-2);
        white-space: nowrap;
    }
    .d.up {
        color: var(--bad);
    }
    .d.down {
        color: var(--good);
    }
    .d.flat {
        color: var(--text-faint);
    }
    .d.new {
        color: var(--cyan);
    }
    .d.gone {
        color: var(--good);
    }
    .lo {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
        gap: var(--s-4);
    }
    .lo-col {
        display: flex;
        flex-direction: column;
        gap: var(--s-2);
    }
    .lo-h {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    .lo-h.new {
        color: var(--cyan);
    }
    .lo-h.gone {
        color: var(--text-faint);
    }
    .chip {
        font-size: 0.78rem;
        font-family: var(--font-mono, monospace);
        color: var(--text-dim);
        border: 1px solid var(--border-soft);
        border-radius: var(--r-md);
        padding: var(--s-1) var(--s-2);
    }
    .chip.new {
        border-color: var(--border-strong);
        color: var(--cyan);
    }
    .chip.gone {
        border-color: var(--border-strong);
        color: var(--text-faint);
    }
    .hint {
        margin: 0 0 var(--s-2);
        font-size: 0.8rem;
        color: var(--text-faint);
    }
    .err {
        color: var(--bad);
        font-size: 0.85rem;
    }
    .name {
        min-width: 0;
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--s-1);
    }
    @media (max-width: 900px) {
        .row5 {
            grid-template-columns: minmax(0, 2fr) minmax(90px, 1fr);
        }
        .row4 {
            grid-template-columns: minmax(0, 2fr) minmax(90px, 1fr);
        }
        .row5 > :nth-child(2),
        .row5 > :nth-child(3),
        .row5 > :nth-child(4),
        .row4 > :nth-child(2),
        .row4 > :nth-child(3) {
            display: none;
        }
    }
</style>
