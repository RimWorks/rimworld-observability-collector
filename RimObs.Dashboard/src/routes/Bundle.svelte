<script lang="ts">
    import { api } from '../lib/api';
    import BundleExportForm from '../lib/components/BundleExportForm.svelte';
    import Card from '../lib/components/Card.svelte';
    import Icon from '../lib/components/Icon.svelte';
    import { t } from '../lib/i18n';
    import { relativeTime } from '../lib/format';
    import { onMount } from 'svelte';

    let currentSessionId = $state<string | null>(null);
    let exportError = $state<string | null>(null);
    let importing = $state(false);
    let importError = $state<string | null>(null);
    let dragOver = $state(false);
    let importedToken = $state<string | null>(null);
    let importedManifest = $state<Record<string, unknown> | null>(null);
    let importedContents = $state<string[]>([]);
    let reportOpen = $state(false);
    let confirmingDiscard = $state(false);

    onMount(async () => {
        try {
            const s = await api.currentSession();
            currentSessionId = s?.session?.id ?? null;
        } catch {
            currentSessionId = null;
        }
    });

    let manifestRows = $derived.by(() => {
        const m = importedManifest;
        if (!m) return [] as { key: string; value: string }[];
        const created = typeof m.created_utc === 'string' ? relativeTime(m.created_utc) : '-';
        return [
            { key: t('bundle.import.kv.session'), value: String(m.session_id ?? '-') },
            { key: t('bundle.import.kv.collector'), value: String(m.collector_version ?? '-') },
            { key: t('bundle.import.kv.created'), value: created },
            { key: t('bundle.import.kv.schema'), value: String(m.schema_version ?? '-') },
        ];
    });
    let hasReport = $derived(importedContents.includes('report.html'));

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

    async function processFile(file: File | undefined) {
        if (!file) return;
        importing = true;
        importError = null;
        try {
            const r = await api.importBundle(file);
            importedToken = r.token;
            importedManifest = r.manifest;
            importedContents = r.contents;
            confirmingDiscard = false;
        } catch {
            importError = t('bundle.import.error');
        } finally {
            importing = false;
        }
    }

    function handleFile(event: Event) {
        const target = event.target as HTMLInputElement;
        processFile(target.files?.[0]);
    }

    function handleDrop(event: DragEvent) {
        event.preventDefault();
        dragOver = false;
        processFile(event.dataTransfer?.files?.[0]);
    }

    async function discardImport() {
        if (importedToken) await api.deleteImport(importedToken);
        importedToken = null;
        importedManifest = null;
        importedContents = [];
        reportOpen = false;
        confirmingDiscard = false;
    }

    function onKey(event: KeyboardEvent) {
        if (event.key === 'Escape' && reportOpen) reportOpen = false;
    }

    function trapFocus(node: HTMLElement) {
        const previous = document.activeElement as HTMLElement | null;
        const focusable = (): HTMLElement[] =>
            Array.from(
                node.querySelectorAll<HTMLElement>(
                    'button, [href], iframe, input, [tabindex]:not([tabindex="-1"])',
                ),
            ).filter((el) => !el.hasAttribute('disabled'));
        focusable()[0]?.focus();
        function onKeydown(event: KeyboardEvent) {
            if (event.key !== 'Tab') return;
            const items = focusable();
            if (items.length === 0) return;
            const first = items[0];
            const last = items[items.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        }
        node.addEventListener('keydown', onKeydown);
        return {
            destroy() {
                node.removeEventListener('keydown', onKeydown);
                previous?.focus();
            },
        };
    }
</script>

<svelte:window onkeydown={onKey} />

<div class="page">
    <header class="intro">
        <h1>{t('bundle.title')}</h1>
        <p>{t('bundle.subtitle')}</p>
    </header>

    <div class="cols">
        <Card title={t('bundle.export.title')}>
            {#if currentSessionId === null}
                <p class="empty">{t('bundle.export.noSession')}</p>
            {:else}
                <BundleExportForm sessionId={currentSessionId} onExport={handleExport} />
                {#if exportError}
                    <p class="error" role="alert">{exportError}</p>
                {/if}
            {/if}
        </Card>

        <Card title={t('bundle.import.title')}>
            {#if importedToken === null}
                <label
                    class="drop"
                    class:over={dragOver}
                    ondragover={(e) => {
                        e.preventDefault();
                        dragOver = true;
                    }}
                    ondragleave={() => (dragOver = false)}
                    ondrop={handleDrop}
                >
                    <input type="file" accept=".zip" onchange={handleFile} disabled={importing} />
                    <span class="drop-icon"><Icon name="upload" size={26} /></span>
                    {#if importing}
                        <span class="drop-main">{t('bundle.import.importing')}</span>
                    {:else}
                        <span class="drop-main">{t('bundle.import.dropHint')}</span>
                        <span class="drop-cta">{t('bundle.import.choose')}</span>
                    {/if}
                </label>
                {#if importError}
                    <p class="error" role="alert">{importError}</p>
                {/if}
            {:else}
                <div class="loaded">
                    <div class="section">
                        <span class="section-label">{t('bundle.import.manifest')}</span>
                        <div class="kv-grid">
                            {#each manifestRows as row (row.key)}
                                <div class="kv">
                                    <span class="k">{row.key}</span>
                                    <span class="v mono" title={row.value}>{row.value}</span>
                                </div>
                            {/each}
                        </div>
                    </div>

                    <div class="section">
                        <span class="section-label">{t('bundle.import.contents')}</span>
                        <ul class="files">
                            {#each importedContents as name (name)}
                                <li>
                                    <a
                                        href={api.getImportFileUrl(importedToken, name)}
                                        download={name}
                                    >
                                        <Icon name="download" size={14} />
                                        <span class="mono">{name}</span>
                                    </a>
                                </li>
                            {/each}
                        </ul>
                    </div>

                    <div class="actions">
                        <button
                            class="primary"
                            onclick={() => (reportOpen = true)}
                            disabled={!hasReport}
                        >
                            <Icon name="external" size={15} />
                            {t('bundle.import.openReport')}
                        </button>
                        {#if confirmingDiscard}
                            <span class="confirm-prompt" role="status"
                                >{t('bundle.import.discardConfirm')}</span
                            >
                            <button class="danger" onclick={discardImport}
                                >{t('bundle.import.discard')}</button
                            >
                            <button class="ghost" onclick={() => (confirmingDiscard = false)}
                                >{t('bundle.import.cancel')}</button
                            >
                        {:else}
                            <button class="danger" onclick={() => (confirmingDiscard = true)}
                                >{t('bundle.import.discard')}</button
                            >
                        {/if}
                    </div>
                </div>
            {/if}
        </Card>
    </div>
</div>

{#if reportOpen && importedToken}
    <div
        class="scrim"
        role="presentation"
        onclick={(e) => {
            if (e.target === e.currentTarget) reportOpen = false;
        }}
    >
        <div
            class="modal"
            role="dialog"
            aria-modal="true"
            aria-label={t('bundle.import.reportTitle')}
            use:trapFocus
        >
            <header class="modal-bar">
                <span>{t('bundle.import.reportTitle')}</span>
                <button class="close" onclick={() => (reportOpen = false)}>
                    {t('bundle.import.close')}
                </button>
            </header>
            <iframe
                title={t('bundle.import.reportTitle')}
                src={api.getImportFileUrl(importedToken, 'report.html')}
            ></iframe>
        </div>
    </div>
{/if}

<style>
    .page {
        display: grid;
        gap: var(--s-5);
    }
    .intro h1 {
        font-size: 1.4rem;
    }
    .intro p {
        margin: var(--s-1) 0 0;
        color: var(--text-dim);
        font-size: 0.9rem;
        max-width: 60ch;
    }
    .cols {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
        gap: var(--s-5);
        align-items: start;
    }
    .empty {
        margin: 0;
        color: var(--text-faint);
        font-size: 0.88rem;
    }
    .error {
        margin: var(--s-4) 0 0;
        padding: var(--s-3) var(--s-3);
        font-size: 0.82rem;
        color: var(--bad);
        background: color-mix(in srgb, var(--bad) 12%, transparent);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
    }

    .drop {
        position: relative;
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-6) var(--s-4);
        text-align: center;
        border: 1.5px dashed var(--border-strong);
        border-radius: var(--r-md);
        cursor: pointer;
        color: var(--text-dim);
        transition:
            border-color var(--t-base) var(--ease-out),
            background var(--t-base) var(--ease-out),
            color var(--t-base) var(--ease-out);
    }
    .drop:hover,
    .drop.over {
        border-color: var(--cyan);
        background: color-mix(in srgb, var(--cyan) 8%, transparent);
        color: var(--text);
    }
    .drop input {
        position: absolute;
        inset: 0;
        opacity: 0;
        cursor: pointer;
    }
    .drop-icon {
        color: var(--cyan);
        display: flex;
    }
    .drop-main {
        font-size: 0.88rem;
    }
    .drop-cta {
        font-size: 0.74rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--cyan-soft);
        border: 1px solid var(--border);
        border-radius: 99px;
        padding: var(--s-1) var(--s-3);
    }

    .loaded {
        display: flex;
        flex-direction: column;
        gap: var(--s-4);
    }
    .section {
        display: flex;
        flex-direction: column;
        gap: var(--s-2);
    }
    .section-label {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--text-faint);
    }
    .kv-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
        gap: var(--s-1) var(--s-5);
    }
    .kv {
        display: flex;
        justify-content: space-between;
        gap: var(--s-4);
        padding: var(--s-2) 0;
        border-bottom: 1px solid var(--border-soft);
        align-items: baseline;
    }
    .k {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-faint);
    }
    .v {
        font-size: 0.84rem;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .files {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
    }
    .files a {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-2) var(--s-2);
        border-radius: var(--r-sm);
        font-size: 0.82rem;
        color: var(--text-dim);
        transition:
            background var(--t-fast) var(--ease-out),
            color var(--t-fast) var(--ease-out);
    }
    .files a:hover {
        background: var(--bg-elev);
        color: var(--cyan-soft);
    }
    .actions {
        display: flex;
        gap: var(--s-2);
        flex-wrap: wrap;
    }
    button {
        display: inline-flex;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-2) var(--s-4);
        font-family: var(--font-ui);
        font-size: 0.82rem;
        font-weight: 500;
        border-radius: var(--r-md);
        cursor: pointer;
        transition:
            border-color var(--t-fast) var(--ease-out),
            background var(--t-fast) var(--ease-out),
            color var(--t-fast) var(--ease-out);
    }
    .primary {
        color: var(--cyan-soft);
        background: color-mix(in srgb, var(--cyan) 14%, var(--bg-elev));
        border: 1px solid var(--border);
    }
    .primary:hover:not(:disabled) {
        border-color: var(--cyan);
        background: color-mix(in srgb, var(--cyan) 20%, var(--bg-elev));
    }
    .primary:disabled {
        opacity: 0.45;
        cursor: not-allowed;
    }
    .danger {
        color: var(--text-dim);
        background: var(--bg-elev);
        border: 1px solid var(--border);
    }
    .danger:hover {
        border-color: var(--bad);
        color: var(--bad);
    }
    .ghost {
        color: var(--text-dim);
        background: transparent;
        border: 1px solid var(--border);
    }
    .ghost:hover {
        border-color: var(--border-strong);
        color: var(--text);
    }
    .confirm-prompt {
        align-self: center;
        font-size: 0.82rem;
        color: var(--text-dim);
    }

    .scrim {
        position: fixed;
        inset: 0;
        z-index: 1000;
        background: color-mix(in srgb, var(--bg-void) 78%, transparent);
        display: flex;
        padding: var(--s-7);
        animation: row-in var(--t-base) var(--ease-out);
    }
    .modal {
        flex: 1;
        display: flex;
        flex-direction: column;
        background: var(--bg-surface);
        border: 1px solid var(--border-strong);
        border-radius: var(--r-lg);
        box-shadow: var(--shadow-card);
        overflow: hidden;
    }
    .modal-bar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--s-3) var(--s-4);
        border-bottom: 1px solid var(--border-soft);
        font-family: var(--font-display);
        font-size: 0.85rem;
        letter-spacing: 0.04em;
        color: var(--text-dim);
    }
    .close {
        padding: var(--s-1) var(--s-3);
        color: var(--text-dim);
        background: var(--bg-elev);
        border: 1px solid var(--border);
    }
    .close:hover {
        border-color: var(--border-strong);
        color: var(--text);
    }
    .modal iframe {
        flex: 1;
        border: none;
        background: #fff;
    }

    @media (max-width: 640px) {
        .scrim {
            padding: var(--s-4);
        }
    }

    @media (max-width: 900px) {
        .cols {
            grid-template-columns: 1fr;
        }
    }
</style>
