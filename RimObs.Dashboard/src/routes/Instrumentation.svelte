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
    <div class="search">
        <input
            type="search"
            placeholder={t('instrumentation.search.placeholder')}
            aria-label={t('instrumentation.search.placeholder')}
            value={query}
            oninput={onInput}
        />
        <span class="dim" role="status" aria-live="polite">
            {#if searchLoading}…{/if}
        </span>
    </div>

    {#if !query.trim() && searchResults.length === 0}
        <p class="empty dim">{t('instrumentation.search.empty')}</p>
    {/if}

    {#if query.trim() && searchResults.length === 0 && !searchLoading}
        <p class="empty dim">{t('instrumentation.search.noresults')}</p>
    {/if}

    {#if searchResults.length > 0}
        <ul class="results">
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
    {/if}

    <h2>{t('instrumentation.active.title')}</h2>
    {#if active.length === 0}
        <p class="empty dim">{t('instrumentation.active.empty')}</p>
    {:else}
        <ul class="active">
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
                    <button onclick={() => remove(p.id)}>{t('instrumentation.remove')}</button>
                    {#if p.lastError}
                        <span class="dim mono err">{p.lastError}</span>
                    {/if}
                </li>
            {/each}
        </ul>
    {/if}
{/if}

<style>
    .search {
        display: flex;
        gap: var(--s-2);
        align-items: center;
        margin-bottom: var(--s-3);
    }
    .search input {
        flex: 1;
        background: var(--bg-surface);
        border: 1px solid var(--border-soft);
        border-radius: var(--r-md);
        color: var(--text);
        font-family: var(--font-ui);
        font-size: 0.85rem;
        padding: var(--s-2) var(--s-3);
        transition: border-color var(--t-fast) var(--ease-out);
    }
    .search input::placeholder {
        color: var(--text-faint);
    }
    .search input:hover {
        border-color: var(--border);
    }
    button {
        background: var(--bg-elev);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        padding: var(--s-1) var(--s-4);
        font-family: var(--font-ui);
        font-size: 0.8rem;
        cursor: pointer;
        white-space: nowrap;
        transition:
            border-color var(--t-fast) var(--ease-out),
            color var(--t-fast) var(--ease-out);
    }
    button:hover {
        border-color: var(--cyan);
    }
    .results button {
        background: color-mix(in srgb, var(--cyan) 14%, var(--bg-elev));
        border-color: var(--border-strong);
        color: var(--cyan-soft);
    }
    .results button:hover {
        border-color: var(--cyan);
    }
    .active button:hover {
        border-color: var(--bad);
        color: var(--bad);
    }
    .results,
    .active {
        list-style: none;
        padding: 0;
        margin: 0;
        display: flex;
        flex-direction: column;
    }
    .results li,
    .active li {
        display: grid;
        grid-template-columns: 1fr auto auto;
        gap: var(--s-2);
        align-items: center;
        padding: var(--s-2) var(--s-1);
        border-bottom: 1px solid var(--border-soft);
        animation: row-in var(--t-base) var(--ease-out);
    }
    h2 {
        margin: var(--s-4) 0 var(--s-2);
        font-size: 0.95rem;
    }
    .dim {
        color: var(--text-dim);
    }
    .pill {
        font-size: 0.7rem;
        padding: var(--s-0) var(--s-2);
        border-radius: 99px;
        background: var(--bg-surface);
        border: 1px solid var(--border-soft);
        align-self: center;
    }
    .pill-active {
        color: var(--good);
    }
    .pill-stale {
        color: var(--warn);
    }
    .pill-pending {
        color: var(--text-faint);
    }
    .empty {
        padding: var(--s-2) 0;
    }
    .unavailable {
        padding: var(--s-4);
        color: var(--text-faint);
    }
    .active .err {
        grid-column: 1 / -1;
        color: var(--bad);
        font-size: 0.78rem;
        overflow-wrap: anywhere;
    }
    @media (max-width: 820px) {
        .results li,
        .active li {
            grid-template-columns: 1fr auto;
        }
        .active li :global(.tip) {
            grid-column: 1;
        }
    }
</style>
