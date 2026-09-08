<script lang="ts" module>
    import Gauge from '@lucide/svelte/icons/gauge';
    import Flame from '@lucide/svelte/icons/flame';
    import ListTree from '@lucide/svelte/icons/list-tree';
    import MemoryStick from '@lucide/svelte/icons/memory-stick';
    import ChartLine from '@lucide/svelte/icons/chart-line';
    import Layers from '@lucide/svelte/icons/layers';
    import ScrollText from '@lucide/svelte/icons/scroll-text';
    import TriangleAlert from '@lucide/svelte/icons/triangle-alert';
    import GitCompareArrows from '@lucide/svelte/icons/git-compare-arrows';
    import Settings from '@lucide/svelte/icons/settings';
    import Dot from '@lucide/svelte/icons/dot';
    import ChevronRight from '@lucide/svelte/icons/chevron-right';
    import ChevronDown from '@lucide/svelte/icons/chevron-down';
    import ExternalLink from '@lucide/svelte/icons/external-link';
    import Syringe from '@lucide/svelte/icons/syringe';
    import Download from '@lucide/svelte/icons/download';
    import Upload from '@lucide/svelte/icons/upload';

    const ICONS = {
        gauge: Gauge,
        flame: Flame,
        tree: ListTree,
        memory: MemoryStick,
        metric: ChartLine,
        stack: Layers,
        logs: ScrollText,
        alert: TriangleAlert,
        compare: GitCompareArrows,
        cog: Settings,
        dot: Dot,
        chevron: ChevronRight,
        chevronDown: ChevronDown,
        external: ExternalLink,
        probe: Syringe,
        download: Download,
        upload: Upload,
    };
    export type IconName = keyof typeof ICONS;
    export { ICONS };
</script>

<script lang="ts">
    let { name, size = 18 }: { name: IconName; size?: number } = $props();

    const Glyph = $derived(ICONS[name]);
</script>

<Glyph {size} strokeWidth={1.6} aria-hidden="true" />
