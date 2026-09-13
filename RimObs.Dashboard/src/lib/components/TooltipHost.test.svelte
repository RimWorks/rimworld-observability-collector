<script lang="ts">
    import Tooltip from './Tooltip.svelte';
    let {
        text,
        placement = 'top',
        rich = false,
    }: { text: string; placement?: 'top' | 'bottom'; rich?: boolean } = $props();
</script>

{#snippet richContent()}<b data-testid="tt-rich">rich bubble</b>{/snippet}

<Tooltip {text} {placement} content={rich ? richContent : undefined}><span>anchor</span></Tooltip>
