<script lang="ts">
    import {
        api,
        ApiError,
        type InstrumentationPatchesResponse,
        type InstrumentationSearchResponse,
        type MethodDescriptor,
    } from '../api';
    import { Resource } from '../poll.svelte';
    import { mergePatches, type MergedPatch } from '../livePatches';
    import Tooltip from './Tooltip.svelte';
    import { t } from '../i18n';
    import { onMount, onDestroy } from 'svelte';

    let { onPatchesChange }: { onPatchesChange?: (p: MergedPatch[]) => void } = $props();

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
    let active = $derived(mergePatches(patches.data?.persisted ?? [], patches.data?.live));

    $effect(() => onPatchesChange?.(active));

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

    export async function remove(id: number) {
        await api.instrumentationUnpatch(id);
        await patches.refresh();
    }
</script>

{#if unavailable}
    <p class="blank">{t('instrumentation.unavailable')}</p>
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
            {#if searchLoading}{t('status.loading')}{/if}
        </span>
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
    {/if}

    <h3>
        {t('instrumentation.active.title')}
        <span class="count mono">{active.length}</span>
    </h3>
    {#if active.length === 0}
        <p class="blank">{t('instrumentation.active.empty')}</p>
    {:else}
        <ul class="rows" data-testid="active-patches">
            {#each active as p (p.id)}
                <li>
                    <span class="mono sig"
                        >{p.typeFullName}.{p.methodName}({p.paramTypesJoined})</span
                    >
                    <Tooltip text={t(`tip.instrumentation.${p.status}`)}>
                        <span class="pill pill-{p.status}" data-testid="patch-status"
                            >{t(`instrumentation.status.${p.status}`)}</span
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
    h3 {
        display: flex;
        align-items: baseline;
        gap: var(--s-2);
        margin: 0;
        padding: var(--s-3) var(--rail) var(--s-2);
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--text-dim);
        border-bottom: 1px solid var(--border-soft);
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
        display: flex;
        align-items: center;
        gap: var(--s-2);
        padding: var(--s-2) var(--rail);
        border-bottom: 1px solid var(--border-soft);
    }
    .search input {
        flex: 1;
        min-width: 0;
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
        color: var(--text-faint);
        font-size: 0.85rem;
    }
    .pill {
        font-size: 0.68rem;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        padding: 1px 6px;
        border-radius: 99px;
        border: 1px solid var(--border);
        color: var(--text-faint);
    }
    .pill-active {
        color: var(--good);
        border-color: color-mix(in srgb, var(--good) 45%, transparent);
    }
    .pill-stale {
        color: var(--bad);
        border-color: color-mix(in srgb, var(--bad) 45%, transparent);
    }
    button {
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        color: var(--text-dim);
        font-size: 0.76rem;
        padding: 2px 8px;
        cursor: pointer;
    }
    button:hover {
        border-color: var(--cyan);
        color: var(--cyan);
    }
</style>
