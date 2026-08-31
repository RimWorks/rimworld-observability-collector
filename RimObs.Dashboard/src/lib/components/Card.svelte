<script lang="ts">
    import type { Snippet } from 'svelte';
    let { title, children }: { title?: string; children: Snippet } = $props();
</script>

<section class="card">
    {#if title}
        <header>
            <h3>{title}</h3>
        </header>
    {/if}
    <div class="body">{@render children()}</div>
</section>

<style>
    .card {
        background: var(--bg-surface);
        border: 1px solid var(--border-soft);
        border-radius: var(--r-lg);
        box-shadow: var(--shadow-card);
        overflow: hidden;
    }
    header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--s-4);
        padding: var(--s-3) var(--s-4);
        border-bottom: 1px solid var(--border-soft);
    }
    h3 {
        font-size: 0.8rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--text-dim);
    }
    .body {
        padding: var(--s-4);
    }
</style>
