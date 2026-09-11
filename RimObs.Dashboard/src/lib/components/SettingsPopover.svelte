<script lang="ts">
    import { computePosition, autoUpdate, flip, shift, offset } from '@floating-ui/dom';
    import {
        api,
        type AutoInstrumentCounters,
        type RimObsConfig,
        type StatusResponse,
    } from '../api';
    import { t, getLang, LANGUAGES } from '../i18n';
    import { relativeTime, count, bytes } from '../format';
    import { userPrefs } from '../userPrefs.svelte';
    import { MIN_RING, MAX_RING, clampRing } from '../ringCapacity';
    import { liveConfig } from '../liveConfig.svelte';
    import {
        initialFilters,
        initialIgnore,
        rememberFilters,
        rememberIgnore,
    } from '../autoInstrumentDefaults';
    import { MIN_DEPTH, MAX_DEPTH, clampDepth } from '../captureDepth';
    import {
        MIN_SAMPLE_RING,
        MAX_SAMPLE_RING,
        MIN_MAX_TARGETS,
        MAX_MAX_TARGETS,
        clampSampleRing,
        clampMaxTargets,
    } from '../firehose';
    import { previewBand, isDirty, type PreviewBand } from '../autoPreview';
    import type { AutoPreviewCounts } from '../api';
    import { MAX_SESSION_NAME, sessionLabel } from '../sessionLabel';
    import { sessionsStore } from '../sessions.svelte';
    import BundleExportForm from './BundleExportForm.svelte';
    import Icon from './Icon.svelte';
    import Tooltip from './Tooltip.svelte';

    let { status }: { status: StatusResponse | null } = $props();

    let exporting = $state(false);
    let exportError = $state<string | null>(null);

    // only the current session can be exported, so the form needs no session picker.
    async function handleExport(p: { sessionId: string; includes: string[]; force: boolean }) {
        exportError = null;
        const result = await api.exportBundle(p);
        if (result.kind === 'over_cap') {
            const est = (result.estimatedBytes / 1_048_576).toFixed(1);
            const cap = (result.capBytes / 1_048_576).toFixed(1);
            exportError = `Bundle would be ${est} MB (cap ${cap} MB). Tick "${t('bundle.export.force')}" to override.`;
            return;
        }
        if (result.kind === 'error') {
            exportError = result.message;
            return;
        }
        const url = URL.createObjectURL(result.blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${p.sessionId}.rimobs.zip`;
        a.click();
        URL.revokeObjectURL(url);
    }

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

    let sessions = $derived(sessionsStore.items);
    let pastSessions = $derived(sessions.filter((s) => !s.is_current));
    let renameError = $derived(sessionsStore.error);

    let config = $state<RimObsConfig | null>(null);
    let saving = $state(false);
    let saveError = $state('');

    let auto = $derived(config?.auto_instrument);
    // the example patterns are real starting values, not just placeholder text, and what you
    // type is remembered locally so an empty collector never wipes it.
    let filtersValue = $derived(initialFilters(auto?.filters ?? ''));
    let ignoreValue = $derived(initialIgnore(auto?.ignore ?? ''));

    async function loadConfig(): Promise<void> {
        try {
            const next = await api.config();
            // an older collector can answer without the blocks the controls bind to. treating
            // that as "no config" disables them instead of blanking the whole pane.
            config = next.sampling && next.auto_instrument && next.session ? next : null;
        } catch {
            config = null;
        }
    }

    // the filters used to reach the live game on blur, so a wide pattern stalled loading before
    // you could see how wide it was. now blur only counts; Apply is what patches.
    let draftFilters = $state<string | null>(null);
    let draftIgnore = $state<string | null>(null);
    let preview = $state<AutoPreviewCounts | null>(null);
    let previewing = $state(false);
    let previewFailed = $state(false);

    let editedFilters = $derived(draftFilters ?? filtersValue);
    let editedIgnore = $derived(draftIgnore ?? ignoreValue);
    let dirty = $derived(
        isDirty(editedFilters, editedIgnore, auto?.filters ?? '', auto?.ignore ?? ''),
    );
    let band = $derived<PreviewBand>(previewBand(preview?.eligible ?? 0));

    async function runPreview(): Promise<void> {
        previewing = true;
        previewFailed = false;
        try {
            preview = await api.instrumentationAutoPreview(editedFilters, editedIgnore);
        } catch {
            preview = null;
            previewFailed = true;
        } finally {
            previewing = false;
        }
    }

    async function applyFilters(): Promise<void> {
        const f = editedFilters;
        const g = editedIgnore;
        rememberFilters(f);
        rememberIgnore(g);
        await save((c) => {
            c.auto_instrument.filters = f;
            c.auto_instrument.ignore = g;
        });
        draftFilters = null;
        draftIgnore = null;
        preview = null;
    }

    let autoStatus = $state<AutoInstrumentCounters | null>(null);

    // read-only, so a control blip just means the fold stays empty until the next open.
    async function loadAutoStatus(): Promise<void> {
        try {
            autoStatus = (await api.instrumentationAuto()).auto;
        } catch {
            autoStatus = null;
        }
    }

    // re-read before every write: the config document carries keys this build does not know
    // about, and the collector may have changed them while the pane sat open.
    async function save(mutate: (next: RimObsConfig) => void): Promise<void> {
        saving = true;
        saveError = '';
        try {
            const next = await api.config();
            mutate(next);
            next.sampling.frame_ring_capacity = clampRing(next.sampling.frame_ring_capacity);
            next.sampling.max_capture_depth = clampDepth(next.sampling.max_capture_depth);
            next.sampling.ring_capacity = clampSampleRing(next.sampling.ring_capacity);
            next.auto_instrument.max_targets = clampMaxTargets(next.auto_instrument.max_targets);
            config = await api.saveConfig(next);
            // the strip sizes its slots from the ring capacity and read it once at mount, so
            // without this a resize left the bars filling a fraction of the panel for good.
            liveConfig.setRingCapacity(config.sampling.frame_ring_capacity);
            liveConfig.setAutoInstrument(config.auto_instrument);
        } catch (err) {
            saveError = (err as Error).message;
        } finally {
            saving = false;
        }
    }
</script>

<button
    class="gear"
    class:open
    bind:this={btnEl}
    type="button"
    aria-label={t('settings.title')}
    aria-expanded={open}
    onclick={() => {
        open = !open;
        if (open) {
            if (config === null) void loadConfig();
            void loadAutoStatus();
            void sessionsStore.load();
        }
    }}
    data-testid="settings-gear"
>
    <Icon name="cog" size={16} />
</button>

{#if open}
    <div class="panel" bind:this={panelEl} role="dialog" aria-label={t('settings.title')}>
        <section class="group">
            <h3>{t('overview.session')}</h3>
            {#if status?.session}
                <div class="field">
                    <Tooltip text={t('tip.settings.sessionName')} align="stretch">
                        <span class="label">{t('session.name')}</span>
                    </Tooltip>
                    <input
                        class="text"
                        type="text"
                        maxlength={MAX_SESSION_NAME}
                        placeholder={t('session.name.placeholder')}
                        value={sessions.find((s) => s.id === status?.session?.id)?.name ?? ''}
                        onchange={(e) =>
                            sessionsStore.rename(status!.session!.id, e.currentTarget.value)}
                        aria-label={t('session.name')}
                        data-testid="session-name"
                    />
                </div>

                <details class="fold">
                    <summary>
                        <Icon name="chevron" size={13} />
                        <span>{t('settings.sessionDetails')}</span>
                    </summary>
                    <dl class="readout">
                        <dt>{t('overview.kv.id')}</dt>
                        <dd class="mono">{status.session.id}</dd>
                        <dt>{t('overview.kv.library')}</dt>
                        <dd class="mono">{status.session.library_version}</dd>
                        <dt>{t('overview.kv.started')}</dt>
                        <dd>{new Date(status.session.started_utc).toLocaleString()}</dd>
                        <dt>{t('overview.kv.lastBatch')}</dt>
                        <dd>{relativeTime(status.receive?.last_batch_utc ?? null)}</dd>
                    </dl>
                </details>

                {#if exporting}
                    <BundleExportForm sessionId={status.session.id} onExport={handleExport} />
                    {#if exportError}
                        <p class="error" role="alert">{exportError}</p>
                    {/if}
                {:else}
                    <button
                        class="wide"
                        type="button"
                        onclick={() => (exporting = true)}
                        data-testid="open-export"
                    >
                        <Icon name="download" size={14} />
                        {t('bundle.export.title')}
                    </button>
                {/if}
            {:else}
                <p class="muted">{t('overview.noSession')}</p>
            {/if}

            {#if pastSessions.length > 0}
                <details class="fold">
                    <summary>
                        <Icon name="chevron" size={13} />
                        <span>{t('session.past')}</span>
                        <span class="tally mono">{pastSessions.length}</span>
                    </summary>
                    <div data-testid="past-sessions">
                        {#each pastSessions as session (session.id)}
                            <div class="field row">
                                <Tooltip text={session.id}>
                                    <span class="label trunc">{sessionLabel(session)}</span>
                                </Tooltip>
                                <input
                                    class="text"
                                    type="text"
                                    maxlength={MAX_SESSION_NAME}
                                    placeholder={t('session.name.placeholder')}
                                    value={session.name}
                                    onchange={(e) =>
                                        sessionsStore.rename(session.id, e.currentTarget.value)}
                                    aria-label={`${t('session.name')} ${session.id}`}
                                    data-testid="past-session-name"
                                />
                            </div>
                        {/each}
                    </div>
                </details>
            {/if}
            {#if renameError}
                <p class="error" data-testid="rename-error">{renameError}</p>
            {/if}
        </section>

        <section class="group">
            <h3>{t('settings.group.profiling')}</h3>

            <div class="costly">
                <label class="switch">
                    <input
                        type="checkbox"
                        checked={auto?.enabled ?? false}
                        disabled={config === null || saving}
                        onchange={(e) => {
                            const on = (e.currentTarget as HTMLInputElement).checked;
                            void save((c) => (c.auto_instrument.enabled = on));
                        }}
                        data-testid="auto-instrument"
                    />
                    <Tooltip text={t('tip.settings.autoInstrument')} align="stretch">
                        <span class="label">{t('settings.autoInstrument')}</span>
                    </Tooltip>
                </label>
                <span class="cost">
                    <Icon name="alert" size={13} />
                    {t('settings.autoInstrument.cost')}
                </span>
            </div>

            <div class="field">
                <Tooltip text={t('tip.settings.autoInstrument.filters')} align="stretch">
                    <span class="label">{t('settings.autoInstrument.filters')}</span>
                </Tooltip>
                <textarea
                    class="text mono"
                    rows="3"
                    spellcheck="false"
                    placeholder={t('settings.autoInstrument.filters.placeholder')}
                    disabled={config === null || saving || !auto?.enabled}
                    value={editedFilters}
                    oninput={(e) => (draftFilters = e.currentTarget.value)}
                    onblur={() => void runPreview()}
                    aria-label={t('settings.autoInstrument.filters')}
                    data-testid="auto-filters"></textarea>
            </div>

            <div class="field">
                <Tooltip text={t('tip.settings.autoInstrument.ignore')} align="stretch">
                    <span class="label">{t('settings.autoInstrument.ignore')}</span>
                </Tooltip>
                <textarea
                    class="text mono"
                    rows="2"
                    spellcheck="false"
                    placeholder={t('settings.autoInstrument.ignore.placeholder')}
                    disabled={config === null || saving || !auto?.enabled}
                    value={editedIgnore}
                    oninput={(e) => (draftIgnore = e.currentTarget.value)}
                    onblur={() => void runPreview()}
                    aria-label={t('settings.autoInstrument.ignore')}
                    data-testid="auto-ignore"></textarea>
            </div>

            <div class="field preview">
                <p class="count {band}" data-testid="auto-preview">
                    {#if previewing}
                        {t('settings.autoInstrument.preview.checking')}
                    {:else if previewFailed}
                        {t('settings.autoInstrument.preview.failed')}
                    {:else if preview === null}
                        &nbsp;
                    {:else if preview.matched === 0}
                        {t('settings.autoInstrument.preview.none')}
                    {:else}
                        {t('settings.autoInstrument.preview')
                            .replace('{eligible}', count(preview.eligible))
                            .replace('{matched}', count(preview.matched))}
                        {#if band === 'bad'}
                            <span class="heavy">{t('settings.autoInstrument.preview.heavy')}</span>
                        {/if}
                    {/if}
                </p>
                <Tooltip text={t('tip.settings.autoInstrument.apply')} align="stretch">
                    <button
                        type="button"
                        class="apply"
                        onclick={() => void applyFilters()}
                        disabled={config === null || saving || !auto?.enabled || !dirty}
                        data-testid="auto-apply">{t('settings.autoInstrument.apply')}</button
                    >
                </Tooltip>
            </div>

            <label class="switch">
                <input
                    type="checkbox"
                    checked={auto?.mute_trivial ?? true}
                    disabled={config === null || saving || !auto?.enabled}
                    onchange={(e) => {
                        const on = (e.currentTarget as HTMLInputElement).checked;
                        void save((c) => (c.auto_instrument.mute_trivial = on));
                    }}
                    data-testid="auto-mute-trivial"
                />
                <Tooltip text={t('tip.settings.autoMuteTrivial')} align="stretch">
                    <span class="label">{t('settings.autoMuteTrivial')}</span>
                </Tooltip>
            </label>

            {#if auto?.enabled && autoStatus}
                <details class="fold">
                    <summary>
                        <Icon name="chevron" size={13} />
                        <span>{t('settings.autoInstrument.status')}</span>
                        <span class="tally">{t('settings.readonly')}</span>
                    </summary>
                    <dl class="readout">
                        <dt>{t('settings.autoInstrument.matched')}</dt>
                        <dd class="mono" data-testid="auto-matched">{count(autoStatus.matched)}</dd>
                        <dt>{t('settings.autoInstrument.instrumented')}</dt>
                        <dd class="mono" data-testid="auto-instrumented">
                            {count(autoStatus.instrumented)}
                        </dd>
                        <dt>{t('settings.autoInstrument.muted')}</dt>
                        <dd class="mono" data-testid="auto-muted">{count(autoStatus.muted)}</dd>
                        <dt>{t('settings.autoInstrument.skippedTrivial')}</dt>
                        <dd class="mono" data-testid="auto-skipped-trivial">
                            {count(autoStatus.skippedTrivial)}
                        </dd>
                        <dt>{t('settings.autoInstrument.skippedOther')}</dt>
                        <dd class="mono" data-testid="auto-skipped-other">
                            {count(autoStatus.skippedOther)}
                        </dd>
                        {#if autoStatus.truncated}
                            <dt>{t('settings.autoInstrument.skippedOverCap')}</dt>
                            <dd class="mono bad" data-testid="auto-skipped-over-cap">
                                {count(autoStatus.skippedOverCap)}
                            </dd>
                        {/if}
                        <dt>{t('settings.autoInstrument.refused')}</dt>
                        <dd class="mono" data-testid="auto-refused">{count(autoStatus.refused)}</dd>
                        <dt>{t('settings.autoInstrument.pending')}</dt>
                        <dd class="mono" data-testid="auto-pending">{count(autoStatus.pending)}</dd>
                    </dl>
                    {#if autoStatus.truncated}
                        <p class="truncated" role="alert" data-testid="auto-truncated">
                            {t('settings.autoInstrument.truncated')
                                .replace('{skipped}', count(autoStatus.skippedOverCap))
                                .replace('{cap}', count(autoStatus.maxTargets))}
                        </p>
                    {/if}
                </details>
            {/if}

            <div class="field row">
                <Tooltip text={t('tip.settings.maxDepth')}>
                    <span class="label">{t('settings.maxDepth')}</span>
                </Tooltip>
                <input
                    class="text num mono"
                    type="number"
                    min={MIN_DEPTH}
                    max={MAX_DEPTH}
                    step="1"
                    disabled={config === null || saving}
                    value={config?.sampling.max_capture_depth ?? ''}
                    onchange={(e) => {
                        const v = Number(e.currentTarget.value);
                        void save((c) => (c.sampling.max_capture_depth = v));
                    }}
                    aria-label={t('settings.maxDepth')}
                    data-testid="max-depth"
                />
            </div>

            <div class="field row">
                <Tooltip text={t('tip.settings.ringCapacity')}>
                    <span class="label">{t('settings.ringCapacity')}</span>
                </Tooltip>
                <input
                    class="text num mono"
                    type="number"
                    min={MIN_RING}
                    max={MAX_RING}
                    step="100"
                    disabled={config === null || saving}
                    value={config?.sampling.frame_ring_capacity ?? ''}
                    onchange={(e) => {
                        const v = Number(e.currentTarget.value);
                        void save((c) => (c.sampling.frame_ring_capacity = v));
                    }}
                    aria-label={t('settings.ringCapacity')}
                    data-testid="ring-capacity"
                />
            </div>

            <div class="field row">
                <Tooltip text={t('tip.settings.sampleRing')}>
                    <span class="label">{t('settings.sampleRing')}</span>
                </Tooltip>
                <input
                    class="text num mono"
                    type="number"
                    min={MIN_SAMPLE_RING}
                    max={MAX_SAMPLE_RING}
                    step="1024"
                    disabled={config === null || saving}
                    value={config?.sampling.ring_capacity ?? ''}
                    onchange={(e) => {
                        const v = Number(e.currentTarget.value);
                        void save((c) => (c.sampling.ring_capacity = v));
                    }}
                    aria-label={t('settings.sampleRing')}
                    data-testid="sample-ring"
                />
            </div>

            <div class="field row">
                <Tooltip text={t('tip.settings.maxTargets')}>
                    <span class="label">{t('settings.maxTargets')}</span>
                </Tooltip>
                <input
                    class="text num mono"
                    type="number"
                    min={MIN_MAX_TARGETS}
                    max={MAX_MAX_TARGETS}
                    step="1000"
                    disabled={config === null || saving}
                    value={config?.auto_instrument.max_targets ?? ''}
                    onchange={(e) => {
                        const v = Number(e.currentTarget.value);
                        void save((c) => (c.auto_instrument.max_targets = v));
                    }}
                    aria-label={t('settings.maxTargets')}
                    data-testid="max-targets"
                />
            </div>

            {#if saveError}
                <p class="error" role="alert" data-testid="config-error">
                    {t('settings.saveFailed')}: {saveError}
                </p>
            {/if}
        </section>

        <section class="group">
            <h3>{t('overview.collector')}</h3>
            {#if status?.update?.available}
                <a class="update" href={status.update.url} target="_blank" rel="noreferrer">
                    <Icon name="external" size={13} />
                    <span>{t('settings.update')}</span>
                    <span class="mono">{status.update.latest_version}</span>
                </a>
            {/if}
            <details class="fold">
                <summary>
                    <Icon name="chevron" size={13} />
                    <span>{t('settings.collectorStatus')}</span>
                    <span class="tally">{t('settings.readonly')}</span>
                </summary>
                <dl class="readout">
                    <dt>{t('overview.kv.status')}</dt>
                    <dd class="mono">{status?.status ?? '-'}</dd>
                    <dt>{t('settings.version')}</dt>
                    <dd class="mono">{status?.version ?? '-'}</dd>
                    <dt>{t('settings.schema')}</dt>
                    <dd class="mono">{status?.schema_version ?? '-'}</dd>
                    {#if status?.receive}
                        <dt>{t('overview.sections')}</dt>
                        <dd class="mono" data-testid="kv-sections">
                            {count(status.receive.section_count)}
                        </dd>
                        <dt>{t('overview.gc')}</dt>
                        <dd class="mono" data-testid="kv-gc">
                            {count(status.receive.total_gc_events)}
                        </dd>
                        <dt>{t('overview.batches')}</dt>
                        <dd class="mono" data-testid="kv-batches">
                            {count(status.receive.total_batches)}
                        </dd>
                        <dt>{t('overview.samples')}</dt>
                        <dd class="mono" data-testid="kv-samples">
                            {count(status.receive.total_samples)}
                        </dd>
                        <dt>{t('overview.bytes')}</dt>
                        <dd class="mono" data-testid="kv-bytes">
                            {bytes(status.receive.total_bytes)}
                        </dd>
                    {/if}
                    {#if prom}
                        <dt>
                            <Tooltip text={t('tip.settings.prometheus')}>
                                <span>{t('settings.prometheus')}</span>
                            </Tooltip>
                        </dt>
                        <dd class:on={prom.prometheus_enabled}>
                            {prom.prometheus_enabled
                                ? t('settings.exporter.enabled')
                                : t('settings.exporter.disabled')}
                        </dd>
                        {#if prom.prometheus_enabled && health}
                            <dt>{t('settings.exporter.endpoint')}</dt>
                            <dd class="mono">/metrics</dd>
                            <dt>{t('settings.exporter.last_scrape')}</dt>
                            <dd class="mono">{health.last_scrape_utc ?? '-'}</dd>
                            <dt>{t('settings.exporter.sample_count')}</dt>
                            <dd class="mono">{health.last_sample_count}</dd>
                            {#if health.total_errors > 0}
                                <dt>{t('settings.exporter.errors')}</dt>
                                <dd class="mono bad">
                                    {health.total_errors} · {health.last_error ?? ''}
                                </dd>
                            {/if}
                        {/if}
                    {:else}
                        <dt>{t('settings.exporters')}</dt>
                        <dd>{t('settings.exporter.unavailable')}</dd>
                    {/if}
                </dl>
            </details>
        </section>

        <section class="group">
            <h3>{t('settings.group.dashboard')}</h3>

            <div class="field row">
                <Tooltip text={t('tip.settings.language')}>
                    <span class="label">{t('settings.language')}</span>
                </Tooltip>
                <select
                    aria-label={t('settings.language')}
                    class="text lang"
                    value={getLang()}
                    onchange={(e) =>
                        userPrefs.setLang((e.currentTarget as HTMLSelectElement).value)}
                >
                    {#each LANGUAGES as l (l.code)}
                        <option value={l.code}>{l.label}</option>
                    {/each}
                </select>
            </div>

            <label class="switch">
                <input
                    type="checkbox"
                    checked={config?.session.prompt_for_name ?? false}
                    disabled={config === null || saving}
                    onchange={(e) => {
                        const on = (e.currentTarget as HTMLInputElement).checked;
                        void save((c) => (c.session.prompt_for_name = on));
                    }}
                    data-testid="prompt-for-name"
                />
                <Tooltip text={t('tip.settings.promptForName')} align="stretch">
                    <span class="label">{t('settings.promptForName')}</span>
                </Tooltip>
            </label>

            <label class="switch">
                <input
                    type="checkbox"
                    checked={userPrefs.closeOnDisconnect}
                    onchange={(e) =>
                        userPrefs.setCloseOnDisconnect(
                            (e.currentTarget as HTMLInputElement).checked,
                        )}
                    data-testid="close-on-disconnect"
                />
                <Tooltip text={t('tip.settings.closeOnDisconnect')} align="stretch">
                    <span class="label">{t('settings.close_on_disconnect')}</span>
                </Tooltip>
            </label>

            <label class="switch">
                <input
                    type="checkbox"
                    checked={userPrefs.mainThreadOnly}
                    onchange={(e) =>
                        userPrefs.setMainThreadOnly((e.currentTarget as HTMLInputElement).checked)}
                    data-testid="main-thread-only"
                />
                <Tooltip text={t('tip.threads.mainOnly')} align="stretch">
                    <span class="label">{t('threads.mainOnly')}</span>
                </Tooltip>
            </label>
        </section>
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
        width: 372px;
        max-height: calc(100vh - var(--topbar-h) - var(--s-5));
        overflow-y: auto;
        overscroll-behavior: contain;
        background: var(--bg-elev);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        box-shadow: 0 12px 32px -6px rgba(0, 0, 0, 0.55);
        scrollbar-color: var(--border-strong) transparent;
    }
    .panel ::selection {
        background: var(--cyan);
        color: var(--bg-void);
    }

    .group {
        padding: var(--s-3) var(--s-4) var(--s-4);
        border-bottom: 1px solid var(--border-soft);
    }
    .group:last-child {
        border-bottom: none;
    }
    h3 {
        margin: 0 0 var(--s-3);
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--text-faint);
        font-weight: 600;
    }

    /* one control: its label sits above its input, and every control is on a surface the
       read-only grid below never uses. that difference is the whole point of the layout. */
    .field {
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
        margin-bottom: var(--s-3);
    }
    .field.row {
        flex-direction: row;
        align-items: center;
        justify-content: space-between;
        gap: var(--s-3);
    }
    .label {
        font-size: 0.8rem;
        color: var(--text-dim);
        cursor: help;
        text-decoration: underline;
        text-decoration-style: dotted;
        text-decoration-color: var(--border-strong);
        text-underline-offset: 3px;
    }
    .text {
        width: 100%;
        font: inherit;
        font-size: 0.82rem;
        color: var(--text);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: var(--s-1) var(--s-2);
        caret-color: var(--cyan);
    }
    .text:hover:not(:disabled) {
        border-color: var(--border-strong);
    }
    .preview {
        gap: var(--s-2);
    }
    .count {
        margin: 0;
        font-size: 0.76rem;
        color: var(--text-dim);
        min-height: 1.1em;
    }
    .count.warn {
        color: var(--warn);
    }
    .count.bad {
        color: var(--bad);
    }
    .heavy {
        display: block;
        color: var(--text-dim);
    }
    .apply {
        font: inherit;
        font-size: 0.78rem;
        color: var(--text);
        background: var(--bg-surface-2);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 10px;
        cursor: pointer;
    }
    .apply:hover:not(:disabled) {
        border-color: var(--cyan);
        color: var(--cyan);
    }
    .apply:disabled {
        color: var(--text-ghost);
        cursor: default;
    }
    .text:disabled {
        color: var(--text-ghost);
        cursor: not-allowed;
    }
    .text::placeholder {
        color: var(--text-faint);
        opacity: 0.75;
    }
    textarea.text {
        resize: vertical;
        line-height: 1.5;
    }
    .num {
        width: 6.5rem;
        flex: none;
        text-align: right;
    }
    .lang {
        width: auto;
        flex: none;
        cursor: pointer;
    }

    .switch {
        display: flex;
        gap: var(--s-2);
        align-items: center;
        cursor: pointer;
        margin-bottom: var(--s-3);
    }
    .switch input {
        cursor: pointer;
    }

    /* auto-instrumentation patches thousands of methods, so it does not get to look like the
       language picker. the tint and the badge are the only warning before the game stutters. */
    .costly {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--s-2);
        margin-bottom: var(--s-3);
        padding: var(--s-2) var(--s-3);
        border: 1px solid color-mix(in srgb, var(--warn) 35%, var(--border));
        border-radius: var(--r-md);
        background: color-mix(in srgb, var(--warn) 8%, var(--bg-surface));
    }
    .costly .switch {
        margin-bottom: 0;
    }
    .cost {
        display: inline-flex;
        align-items: center;
        gap: var(--s-1);
        flex: none;
        font-size: 0.7rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--warn);
    }

    .fold {
        margin-bottom: var(--s-3);
        border: 1px solid var(--border-soft);
        border-radius: var(--r-md);
        background: var(--bg-surface);
    }
    .fold summary {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-2) var(--s-3);
        font-size: 0.78rem;
        color: var(--text-dim);
        cursor: pointer;
        list-style: none;
    }
    .fold summary::-webkit-details-marker {
        display: none;
    }
    .fold summary :global(svg) {
        flex: none;
        transition: transform var(--t-fast) var(--ease-out);
    }
    .fold[open] summary :global(svg) {
        transform: rotate(90deg);
    }
    .fold summary:hover {
        color: var(--text);
    }
    .tally {
        margin-left: auto;
        font-size: 0.7rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    .fold > :global(*:not(summary)) {
        padding: 0 var(--s-3) var(--s-3);
    }
    .fold .field:last-child {
        margin-bottom: 0;
    }

    /* read-only. no borders, no boxes, nothing that invites a click. */
    .readout {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--s-1) var(--s-3);
        margin: 0;
        font-size: 0.78rem;
    }
    .readout dt {
        color: var(--text-faint);
        white-space: nowrap;
    }
    .readout dd {
        margin: 0;
        text-align: right;
        word-break: break-all;
        font-variant-numeric: tabular-nums;
    }
    .readout dd.on {
        color: var(--cyan);
    }
    .readout dd.bad {
        color: var(--bad);
    }
    .truncated {
        margin: var(--s-2) 0 0;
        font-size: 0.76rem;
        color: var(--bad);
    }

    .update {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        margin-bottom: var(--s-3);
        padding: var(--s-2) var(--s-3);
        border: 1px solid color-mix(in srgb, var(--cyan) 30%, var(--border));
        border-radius: var(--r-md);
        font-size: 0.8rem;
        color: var(--cyan-soft);
    }
    .update span:last-child {
        margin-left: auto;
    }

    .wide {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: var(--s-2);
        width: 100%;
        background: var(--bg-surface);
        font-size: 0.82rem;
        padding: var(--s-2) var(--s-3);
    }
    .wide:hover {
        border-color: var(--cyan);
    }

    .error {
        margin: 0 0 var(--s-2);
        color: var(--bad);
        font-size: 0.78rem;
    }
    .muted {
        font-size: 0.82rem;
        color: var(--text-faint);
        margin: 0;
    }
    .trunc {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
</style>
