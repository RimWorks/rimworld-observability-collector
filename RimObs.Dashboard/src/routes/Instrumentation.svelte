<script lang="ts">
    import {
        api,
        ApiError,
        type InstrumentationPatchesResponse,
        type InstrumentationSearchResponse,
        type MethodDescriptor,
        type InstrumentationPatchEntry,
    } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import Tooltip from '../lib/components/Tooltip.svelte';
    import { t } from '../lib/i18n';
    import { onMount, onDestroy } from 'svelte';

    let searchUnavailable = $state(false);
    let query = $state('');
    let searchResults = $state<MethodDescriptor[]>([]);
    let searchLoading = $state(false);
    const patches = new Resource<InstrumentationPatchesResponse>(
        () => api.instrumentationPatches(),
        5000,
    );

    onMount(() => patches.start());
    onDestroy(() => patches.stop());

    let unavailable = $derived(searchUnavailable || (patches.state === 'error' && !patches.data));

    let debounceHandle: ReturnType<typeof setTimeout> | null = null;
    function onInput(e: Event) {
        query = (e.target as HTMLInputElement).value;
        if (debounceHandle) clearTimeout(debounceHandle);
        debounceHandle = setTimeout(() => void runSearch(), 250);
    }

    async function runSearch() {
        if (!query.trim()) {
            searchResults = [];
            return;
        }
        searchLoading = true;
        try {
            const res: InstrumentationSearchResponse = await api.instrumentationSearch(query, 30);
            searchResults = res.results;
            searchUnavailable = false;
        } catch (e: unknown) {
            if (e instanceof ApiError && e.status === 503) {
                searchUnavailable = true;
                searchResults = [];
            }
        } finally {
            searchLoading = false;
        }
    }

    async function instrument(m: MethodDescriptor) {
        await api.instrumentationPatch({
            typeFullName: m.typeFullName,
            methodName: m.methodName,
            paramTypeFullNames: m.paramTypeFullNames,
        });
        await patches.refresh();
    }

    async function remove(id: number) {
        await api.instrumentationUnpatch(id);
        await patches.refresh();
    }

    let active = $derived<InstrumentationPatchEntry[]>(patches.data?.persisted ?? []);
</script>

{#if unavailable}
    <p class="unavailable">{t('instrumentation.unavailable')}</p>
{:else}
    <div class="work">
        <section class="pane">
            <header class="pane-head">
                <h2>{t('instrumentation.search.title')}</h2>
                <span class="dim" role="status" aria-live="polite">
                    {#if searchLoading}{t('status.loading')}{/if}
                </span>
            </header>
            <div class="search">
                <input
                    type="search"
                    placeholder={t('instrumentation.search.placeholder')}
                    aria-label={t('instrumentation.search.placeholder')}
                    value={query}
                    oninput={onInput}
                />
            </div>

            {#if searchResults.length > 0}
                <ul class="rows">
                    {#each searchResults as m (m.signature + m.assemblyName)}
                        <li>
                            <span class="mono sig">{m.signature}</span>
                            <span class="dim asm">{m.assemblyName}</span>
                            <button onclick={() => instrument(m)}
                                >{t('instrumentation.results.button')}</button
                            >
                        </li>
                    {/each}
                </ul>
            {:else if query.trim() && !searchLoading}
                <p class="blank">{t('instrumentation.search.noresults')}</p>
            {:else if !query.trim()}
                <p class="blank">{t('instrumentation.search.empty')}</p>
            {/if}
        </section>

        <section class="pane">
            <header class="pane-head">
                <h2>{t('instrumentation.active.title')}</h2>
                <span class="count mono">{active.length}</span>
            </header>
            {#if active.length === 0}
                <p class="blank">{t('instrumentation.active.empty')}</p>
            {:else}
                <ul class="rows">
                    {#each active as p (p.id)}
                        <li>
                            <span class="mono sig"
                                >{p.typeFullName}.{p.methodName}({p.paramTypesJoined})</span
                            >
                            <Tooltip text={t(`tip.instrumentation.${p.lastStatus}`)}>
                                <span class="pill pill-{p.lastStatus}"
                                    >{t(`instrumentation.status.${p.lastStatus}`)}</span
                                >
                            </Tooltip>
                            <button onclick={() => remove(p.id)}
                                >{t('instrumentation.remove')}</button
                            >
                            {#if p.lastError}
                                <span class="dim mono err">{p.lastError}</span>
                            {/if}
                        </li>
                    {/each}
                </ul>
            {/if}
        </section>
    </div>
{/if}

<style>
    .work {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        min-height: calc(100vh - var(--topbar-h));
    }
    .pane {
        min-width: 0;
    }
    .pane + .pane {
        border-left: 1px solid var(--border);
    }
    .pane-head {
        display: flex;
        align-items: baseline;
        gap: var(--s-2);
        padding: var(--s-3) var(--rail);
        border-bottom: 1px solid var(--border);
        background: var(--bg-surface);
    }
    .pane-head h2 {
        font-size: 0.78rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--text-dim);
    }
    .count {
        margin-left: auto;
        font-size: 0.78rem;
        color: var(--text-faint);
    }
    .dim {
        color: var(--text-faint);
        font-size: 0.78rem;
    }
    .search {
        padding: var(--s-2) var(--rail);
        border-bottom: 1px solid var(--border-soft);
    }
    .search input {
        width: 100%;
        background: var(--bg-void);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        color: var(--text);
        font-family: var(--font-ui);
        font-size: 0.85rem;
        padding: var(--s-2) var(--s-3);
        transition: border-color var(--t-fast) var(--ease-out);
    }
    .search input::placeholder {
        color: var(--text-faint);
    }
    .search input:hover,
    .search input:focus {
        border-color: var(--border-strong);
    }
    .rows {
        list-style: none;
        margin: 0;
        padding: 0;
    }
    .rows li {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto auto;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-2) var(--rail);
        border-bottom: 1px solid var(--border-soft);
    }
    .rows li:hover {
        background: var(--bg-surface);
    }
    .sig {
        font-size: 0.8rem;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .asm {
        font-size: 0.72rem;
        white-space: nowrap;
    }
    .err {
        grid-column: 1 / -1;
        font-size: 0.72rem;
        color: var(--bad);
    }
    .blank {
        margin: var(--s-4) var(--rail);
        padding: var(--s-5) var(--s-4);
        border: 1px solid var(--border-soft);
        background: var(--bg-surface);
        color: var(--text-faint);
        font-size: 0.84rem;
        text-align: center;
    }
    .unavailable {
        margin: var(--s-5) var(--rail);
        padding: var(--s-5) var(--s-4);
        max-width: var(--measure);
        border: 1px solid var(--border-soft);
        background: var(--bg-surface);
        color: var(--text-dim);
    }
    button {
        background: var(--bg-elev);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: 3px 10px;
        font-family: var(--font-ui);
        font-size: 0.78rem;
        cursor: pointer;
        white-space: nowrap;
        transition: border-color var(--t-fast) var(--ease-out);
    }
    button:hover {
        border-color: var(--cyan);
    }
    .pill {
        font-size: 0.68rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        padding: var(--s-0) var(--s-2);
        border-radius: 99px;
        border: 1px solid var(--border);
        white-space: nowrap;
    }
    .pill-active {
        color: var(--good);
        border-color: color-mix(in srgb, var(--good) 45%, transparent);
    }
    .pill-pending {
        color: var(--warn);
        border-color: color-mix(in srgb, var(--warn) 45%, transparent);
    }
    .pill-stale {
        color: var(--bad);
        border-color: color-mix(in srgb, var(--bad) 45%, transparent);
    }

    @media (max-width: 1000px) {
        .work {
            grid-template-columns: 1fr;
        }
        .pane + .pane {
            border-left: 0;
            border-top: 1px solid var(--border);
        }
    }
</style>
