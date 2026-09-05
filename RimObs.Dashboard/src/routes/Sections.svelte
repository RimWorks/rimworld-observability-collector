<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api, type RegistrySection } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import SubsystemFilter, {
        readFilterFromUrl,
        matchesFilter,
        type SubsystemFilterValue,
    } from '../lib/components/SubsystemFilter.svelte';
    import { t } from '../lib/i18n';

    const res = new Resource<{ schema_version: number; sections: RegistrySection[] }>(
        () => api.allSections(),
        5000,
    );
    onMount(() => res.start());
    onDestroy(() => res.stop());

    let activeFilter = $state<SubsystemFilterValue>(readFilterFromUrl());

    let sections = $derived(res.data?.sections ?? []);

    let filtered = $derived(sections.filter((s) => matchesFilter(s.subsystem, activeFilter)));
</script>

<div class="page">
    <SubsystemFilter items={sections} bind:filter={activeFilter} />

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
