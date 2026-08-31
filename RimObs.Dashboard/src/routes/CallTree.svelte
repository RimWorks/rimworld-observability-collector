<script lang="ts">
    import { api, type CallTreeResponse } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import CallTreeRow from '../lib/components/CallTreeRow.svelte';
    import { t } from '../lib/i18n';
    import { onMount, onDestroy } from 'svelte';

    const res = new Resource<CallTreeResponse>(() => api.callTree(10, 16), 4000);
    onMount(() => res.start());
    onDestroy(() => res.stop());

    let roots = $derived(res.data?.roots ?? []);
    let totalNs = $derived(roots.reduce((s, r) => s + r.total_ns, 0));
</script>

<DataState
    state={res.state}
    error={res.error}
    empty={roots.length === 0}
    onretry={() => res.refresh()}
>
    <div class="legend">
        <span>{t('calltree.title')}</span>
        <span class="cols">
            <span>{t('calltree.share')}</span>
            <span>{t('calltree.calls')}</span>
            <span>{t('calltree.total')}</span>
        </span>
    </div>
    <div class="tree">
        {#each roots as root (root.id)}
            <CallTreeRow node={root} parentNs={totalNs} />
        {/each}
    </div>
</DataState>

<style>
    .legend {
        display: flex;
        justify-content: space-between;
        align-items: center;
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--text-faint);
        padding: 0 var(--s-2) var(--s-2);
        border-bottom: 1px solid var(--border);
        margin-bottom: var(--s-2);
    }
    .cols {
        display: flex;
        gap: var(--s-5);
    }
    .tree {
        display: flex;
        flex-direction: column;
        overflow-x: auto;
    }
</style>
