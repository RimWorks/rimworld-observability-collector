<script lang="ts">
    import { donutArcs, OTHER_SECTION_ID, type PieSlice } from '../pieSlices';
    import { ns } from '../format';
    import { percent2 } from '../frameCost';

    let {
        slices,
        onSelect,
    }: { slices: readonly PieSlice[]; onSelect?: (sectionId: number) => void } = $props();

    let arcs = $derived(donutArcs(slices));
    let hovered = $state<number | null>(null);

    function colorFor(slice: PieSlice): string {
        if (slice.sectionId === OTHER_SECTION_ID) return 'var(--sub-none)';
        return slice.subsystem
            ? `var(--sub-${slice.subsystem}, var(--sub-none))`
            : 'var(--sub-none)';
    }

    function pick(sectionId: number): void {
        if (sectionId !== OTHER_SECTION_ID) onSelect?.(sectionId);
    }
</script>

<div class="pie" data-testid="pie-chart">
    <svg viewBox="0 0 100 100" role="img" aria-label="self time by section">
        {#each arcs as arc (arc.slice.sectionId)}
            <path
                d={arc.path}
                fill={colorFor(arc.slice)}
                class:dim={hovered !== null && hovered !== arc.slice.sectionId}
                role="presentation"
                onmouseenter={() => (hovered = arc.slice.sectionId)}
                onmouseleave={() => (hovered = null)}><title>{arc.slice.label}</title></path
            >
        {/each}
    </svg>

    <ul class="legend">
        {#each slices as slice (slice.sectionId)}
            <li class:dim={hovered !== null && hovered !== slice.sectionId}>
                <button
                    type="button"
                    onmouseenter={() => (hovered = slice.sectionId)}
                    onmouseleave={() => (hovered = null)}
                    onclick={() => pick(slice.sectionId)}
                    disabled={slice.sectionId === OTHER_SECTION_ID}
                >
                    <span class="swatch" style="background: {colorFor(slice)}"></span>
                    <span class="name mono">{slice.label}</span>
                    <span class="pct mono">{percent2(slice.share * 100)}</span>
                    <span class="us mono">{ns(slice.selfUs * 1000)}</span>
                </button>
            </li>
        {/each}
    </ul>
</div>

<style>
    .pie {
        display: grid;
        grid-template-columns: 180px minmax(0, 1fr);
        gap: var(--s-4);
        align-items: center;
        padding: var(--s-4) var(--rail);
    }
    svg {
        width: 180px;
        height: 180px;
    }
    path {
        stroke: var(--bg-base);
        stroke-width: 0.8;
        transition: opacity var(--t-fast) var(--ease-out);
        cursor: pointer;
    }
    path.dim,
    li.dim {
        opacity: 0.35;
    }
    .legend {
        list-style: none;
        margin: 0;
        padding: 0;
        min-width: 0;
    }
    .legend button {
        display: grid;
        grid-template-columns: 10px minmax(0, 1fr) 56px 72px;
        gap: var(--s-2);
        align-items: center;
        width: 100%;
        padding: 2px var(--s-1);
        background: none;
        border: 0;
        border-radius: var(--r-sm);
        color: inherit;
        font: inherit;
        font-size: 0.78rem;
        text-align: left;
        cursor: pointer;
    }
    .legend button:disabled {
        cursor: default;
    }
    .legend button:hover:not(:disabled) {
        background: var(--bg-surface);
    }
    .swatch {
        width: 10px;
        height: 10px;
        border-radius: 2px;
    }
    .name {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        direction: rtl;
        text-align: left;
    }
    .pct,
    .us {
        text-align: right;
        color: var(--text-dim);
    }

    @media (max-width: 820px) {
        .pie {
            grid-template-columns: 1fr;
            justify-items: center;
        }
    }
</style>
