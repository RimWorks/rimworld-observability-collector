<script lang="ts">
    let { data }: { data: Record<string, unknown> } = $props();
</script>

<section class="tab-pane">
    <h2>Load Order</h2>
    {#if Object.keys(data).length === 0}
        <p class="empty">No load order data.</p>
    {:else}
        <dl>
            {#each Object.entries(data) as [k, v]}
                <dt>{k}</dt>
                <dd>{typeof v === 'object' ? JSON.stringify(v) : String(v)}</dd>
            {/each}
        </dl>
    {/if}
</section>

<style>
    .tab-pane {
        padding: 1rem 0;
    }
    .empty {
        color: #888;
        font-style: italic;
    }
    dl {
        display: grid;
        grid-template-columns: max-content 1fr;
        gap: 0.25rem 1rem;
    }
    dt {
        font-weight: 600;
        color: #444;
    }
    dd {
        margin: 0;
        font-family: 'IBM Plex Mono', monospace;
    }
</style>
