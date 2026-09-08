<script lang="ts">
    import Tooltip from './Tooltip.svelte';
    let { text, placement = 'top' }: { text: string; placement?: 'top' | 'bottom' } = $props();
</script>

<Tooltip {text} {placement}><span>anchor</span></Tooltip>
