<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api, type RegistrySection } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import { t } from '../lib/i18n';

    const res = new Resource<{ schema_version: number; sections: RegistrySection[] }>(
        () => api.allSections(),
        5000,
    );
    onMount(() => res.start());
    onDestroy(() => res.stop());

    const NULL_SUBSYSTEM = '__unset__';

    function readFilterFromUrl(): string | null | 'all' {
        const raw = new URLSearchParams(window.location.search).get('subsystem');
        if (raw === null) return 'all';
        if (raw === NULL_SUBSYSTEM) return null;
        return raw;
    }

    function writeFilterToUrl(filter: string | null | 'all'): void {
        const params = new URLSearchParams(window.location.search);
        if (filter === 'all') {
            params.delete('subsystem');
        } else {
            params.set('subsystem', filter === null ? NULL_SUBSYSTEM : filter);
        }
        const qs = params.toString();
        window.history.replaceState(null, '', qs ? `?${qs}` : window.location.pathname);
    }

    let activeFilter = $state<string | null | 'all'>(readFilterFromUrl());

    let sections = $derived(res.data?.sections ?? []);

    let subsystems = $derived.by(() => {
        const seen = new Set<string>();
        const result: Array<string | null> = [];
        for (const s of sections) {
            const key = s.subsystem ?? '\x00';
            if (!seen.has(key)) {
                seen.add(key);
                result.push(s.subsystem);
            }
        }
        return result;
    });

    let filtered = $derived.by(() => {
        if (activeFilter === 'all') return sections;
        return sections.filter((s) => s.subsystem === activeFilter);
    });

    function chipLabel(sub: string | null): string {
        return sub === null ? t('sections.filter.unset') : sub;
    }

    function isActive(sub: string | null | 'all'): boolean {
        return activeFilter === sub;
    }
</script>

<div class="page">
    {#if sections.length > 1}
        <div class="chips" role="group" aria-label={t('sections.filter.label')}>
            <button
                class="chip"
                class:active={isActive('all')}
                onclick={() => {
                    activeFilter = 'all';
                    writeFilterToUrl('all');
                }}
            >
                {t('sections.filter.all')}
            </button>
            {#each subsystems as sub (sub ?? '\x00')}
                <button
                    class="chip"
                    class:active={isActive(sub)}
                    onclick={() => {
                        activeFilter = sub;
                        writeFilterToUrl(sub);
                    }}
                >
                    {chipLabel(sub)}
                </button>
            {/each}
        </div>
    {/if}

    <DataState
        state={res.state}
        error={res.error}
        empty={sections.length === 0}
        emptyTitle={t('sections.empty')}
        onretry={() => res.refresh()}
    >
        <ul class="list">
            {#each filtered as section (section.id)}
                <li class="row">
                    <span class="name mono">{section.name}</span>
                    {#if section.subsystem}
                        <span class="sub">{section.subsystem}</span>
                    {/if}
                </li>
            {/each}
        </ul>
    </DataState>
</div>

<style>
    .page {
        display: flex;
        flex-direction: column;
        gap: var(--s-4);
    }
    .chips {
        display: flex;
        flex-wrap: wrap;
        gap: var(--s-2);
    }
    .chip {
        padding: var(--s-1) var(--s-3);
        border-radius: 99px;
        border: 1px solid var(--border);
        background: var(--bg-surface);
        color: var(--text-dim);
        font-family: var(--font-ui);
        font-size: 0.78rem;
        cursor: pointer;
        transition:
            background var(--t-fast) var(--ease-out),
            color var(--t-fast) var(--ease-out),
            border-color var(--t-fast) var(--ease-out);
    }
    .chip:hover {
        background: var(--bg-elev);
        color: var(--text);
    }
    .chip.active {
        background: color-mix(in srgb, var(--cyan) 14%, var(--bg-surface));
        border-color: var(--border-strong);
        color: var(--text);
    }
    .list {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: 0;
        border: 1px solid var(--border-soft);
        border-radius: var(--r-lg);
        overflow: hidden;
        background: var(--bg-surface);
    }
    .row {
        display: flex;
        align-items: center;
        gap: var(--s-4);
        padding: var(--s-2) var(--s-4);
        border-bottom: 1px solid var(--border-soft);
        transition: background var(--t-fast) var(--ease-out);
    }
    .row:last-child {
        border-bottom: none;
    }
    .row:hover {
        background: var(--bg-elev);
    }
    .name {
        font-size: 0.84rem;
        color: var(--text);
        flex: 1;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .sub {
        font-size: 0.72rem;
        color: var(--text-faint);
        white-space: nowrap;
        flex-shrink: 0;
    }
</style>
