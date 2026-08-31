<script lang="ts">
    import Icon, { type IconName } from './Icon.svelte';
    import Tooltip from './Tooltip.svelte';
    let {
        label,
        value,
        icon,
        tooltip,
    }: {
        label: string;
        value: string;
        icon: IconName;
        tooltip?: string;
    } = $props();
</script>

<div class="stat">
    <div class="ic"><Icon name={icon} size={16} /></div>
    <div class="meta">
        {#if tooltip}
            <Tooltip text={tooltip}><span class="label">{label}</span></Tooltip>
        {:else}
            <span class="label">{label}</span>
        {/if}
        <span class="value mono">{value}</span>
    </div>
</div>

<style>
    .stat {
        display: flex;
        gap: var(--s-3);
        align-items: flex-start;
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-lg);
        padding: var(--s-4) var(--s-4);
        box-shadow: var(--shadow-card);
    }
    .ic {
        color: var(--text-faint);
        display: grid;
        place-items: center;
        flex-shrink: 0;
    }
    .meta {
        display: flex;
        flex-direction: column;
        min-width: 0;
    }
    .label {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--text-faint);
    }
    .value {
        font-size: 1.5rem;
        font-weight: 600;
        line-height: 1.2;
        color: var(--text);
    }
</style>
