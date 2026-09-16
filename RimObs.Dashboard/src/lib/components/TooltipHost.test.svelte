<script lang="ts">
    import Tooltip from './Tooltip.svelte';
    let {
        text,
        placement = 'top',
        rich = false,
        button = false,
        disabled = false,
        childFocusable,
    }: {
        text: string;
        placement?: 'top' | 'bottom';
        rich?: boolean;
        button?: boolean;
        disabled?: boolean;
        childFocusable?: boolean;
    } = $props();
</script>

{#snippet richContent()}<b data-testid="tt-rich">rich bubble</b>{/snippet}

<Tooltip {text} {placement} {childFocusable} content={rich ? richContent : undefined}>
    {#if button}<button type="button" {disabled}>anchor</button>{:else}<span>anchor</span>{/if}
</Tooltip>
