<script lang="ts">
    import Self from './CallTreeRow.svelte';
    import Icon from './Icon.svelte';
    import type { CallNode } from '../api';
    import { ns, count } from '../format';

    let {
        node,
        depth = 0,
        parentNs,
    }: { node: CallNode; depth?: number; parentNs: number } = $props();
    let open = $state(true);
    let hasKids = $derived(node.children.length > 0);
    let share = $derived(parentNs > 0 ? node.total_ns / parentNs : 1);
</script>

<div class="node" class:other={node.is_other} style="--depth: {depth}">
    <button class="bar-row" onclick={() => (open = !open)} disabled={!hasKids}>
        <span class="twist" class:open class:hidden={!hasKids}
            ><Icon name="chevron" size={13} /></span
        >
        <span class="name mono">{node.name || `#${node.id}`}</span>
        <span class="share-track">
            <span class="share-fill" style="width: {Math.min(share * 100, 100)}%"></span>
        </span>
        <span class="calls mono">{count(node.call_count)}</span>
        <span class="total mono">{ns(node.total_ns)}</span>
    </button>
    {#if open && hasKids}
        <div class="kids">
            {#each node.children as child (child.id)}
                <Self node={child} depth={depth + 1} parentNs={node.total_ns} />
            {/each}
        </div>
    {/if}
</div>

<style>
    .bar-row {
        display: grid;
        grid-template-columns: 16px minmax(120px, 1.7fr) minmax(80px, 1fr) 70px 90px;
        gap: var(--s-2);
        align-items: center;
        width: 100%;
        min-width: 360px;
        text-align: left;
        background: none;
        border: none;
        color: inherit;
        font: inherit;
        padding: var(--s-2) var(--s-2);
        padding-left: calc(0.5rem + var(--depth) * 1.1rem);
        border-radius: var(--r-sm);
        cursor: pointer;
        transition: background var(--t-fast) var(--ease-out);
    }
    .bar-row:disabled {
        cursor: default;
    }
    .bar-row:hover {
        background: var(--bg-surface);
    }
    .twist {
        color: var(--text-faint);
        display: grid;
        place-items: center;
        transition: transform var(--t-fast) var(--ease-out);
    }
    .twist.open {
        transform: rotate(90deg);
    }
    .twist.hidden {
        visibility: hidden;
    }
    .name {
        font-size: 0.82rem;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .other .name {
        color: var(--text-faint);
        font-style: italic;
    }
    .share-track {
        height: 6px;
        border-radius: 99px;
        background: var(--border-soft);
        overflow: hidden;
    }
    .share-fill {
        display: block;
        height: 100%;
        background: var(--cyan);
        border-radius: 99px;
    }
    .calls {
        text-align: right;
        font-size: 0.8rem;
        color: var(--text-dim);
    }
    .total {
        text-align: right;
        font-size: 0.82rem;
        font-weight: 600;
    }

    @media (max-width: 900px) {
        .bar-row {
            min-width: 0;
            grid-template-columns: 16px minmax(0, 1.6fr) 64px 78px;
            gap: var(--s-2);
        }
        .share-track {
            display: none;
        }
    }
</style>
