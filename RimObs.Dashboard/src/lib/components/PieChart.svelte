<script lang="ts">
    import {
        donutArcs,
        OTHER_SECTION_ID,
        OTHER_MOD_KEY,
        type ModSlice,
        type PieSlice,
    } from '../pieSlices';
    import { hashedSectionColor } from '../frameDraw';
    import { ns } from '../format';
    import { percent2 } from '../frameCost';

    type Slice = PieSlice | ModSlice;

    let {
        slices,
        onPick,
    }: { slices: readonly Slice[]; onPick?: (slice: PieSlice | ModSlice) => void } = $props();

    let arcs = $derived(donutArcs(slices));
    let hovered = $state<string | null>(null);

    function isSection(slice: Slice): slice is PieSlice {
        return 'sectionId' in slice;
    }

    function keyOf(slice: Slice): string {
        return isSection(slice) ? `s${slice.sectionId}` : `m${slice.key}`;
    }

    function isOther(slice: Slice): boolean {
        return isSection(slice)
            ? slice.sectionId === OTHER_SECTION_ID
            : slice.key === OTHER_MOD_KEY;
    }

    // mods carry no subsystem, so they hash their key into the palette. slice order churns
    // every poll, so the color has to come from the key, not the position.
    function hashKey(key: string): number {
        let h = 0;
        for (let i = 0; i < key.length; i++) h = (h * 31 + key.charCodeAt(i)) | 0;
        return h;
    }

    function colorFor(slice: Slice): string {
        if (isOther(slice)) return 'var(--sub-none)';
        if (!isSection(slice)) return hashedSectionColor(hashKey(slice.key));
        return slice.subsystem
            ? `var(--sub-${slice.subsystem}, var(--sub-none))`
            : 'var(--sub-none)';
    }

    function pick(slice: Slice): void {
        if (!isOther(slice)) onPick?.(slice);
    }
</script>

<div class="pie" data-testid="pie-chart">
    <svg viewBox="0 0 100 100" role="img" aria-label="self time by section">
        {#each arcs as arc (keyOf(arc.slice))}
            <path
                d={arc.path}
                fill={colorFor(arc.slice)}
                class:dim={hovered !== null && hovered !== keyOf(arc.slice)}
                class:pickable={!isOther(arc.slice)}
                role="button"
                aria-label={arc.slice.label}
                tabindex={isOther(arc.slice) ? undefined : 0}
                data-testid="pie-arc"
                onmouseenter={() => (hovered = keyOf(arc.slice))}
                onmouseleave={() => (hovered = null)}
                onclick={() => pick(arc.slice)}
                onkeydown={(e) => e.key === 'Enter' && pick(arc.slice)}
                ><title>{arc.slice.label}</title></path
            >
        {/each}
    </svg>

    <ul class="legend">
        {#each slices as slice (keyOf(slice))}
            <li class:dim={hovered !== null && hovered !== keyOf(slice)}>
                <button
                    type="button"
                    onmouseenter={() => (hovered = keyOf(slice))}
                    onmouseleave={() => (hovered = null)}
                    onclick={() => pick(slice)}
                    disabled={isOther(slice)}
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
    path.pickable {
        cursor: pointer;
    }
</style>
