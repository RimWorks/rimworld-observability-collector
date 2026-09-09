<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { buildBars, barIndexAt, gridLines, GC_BAND_PX, type StripBar } from '../frameStrip';
    import { drawStrip } from '../stripDraw';
    import { ns } from '../format';
    import { t } from '../i18n';

    let {
        ordinals,
        durationsUs,
        selectedOrdinal = null,
        cutOrdinals = [],
        gcOrdinals = [],
        onSelect,
    }: {
        ordinals: readonly number[];
        durationsUs: readonly number[];
        selectedOrdinal?: number | null;
        cutOrdinals?: readonly number[];
        gcOrdinals?: readonly number[];
        onSelect?: (ordinal: number) => void;
    } = $props();

    const HEIGHT_PX = 132 + GC_BAND_PX;

    let bars = $derived(buildBars(ordinals, durationsUs));
    let canvasEl = $state<HTMLCanvasElement | null>(null);
    let hostEl = $state<HTMLDivElement | null>(null);
    let widthPx = $state(600);
    let heightPx = $state(HEIGHT_PX);
    let dpr = $state(1);
    let hoverIndex = $state(-1);
    let ro: ResizeObserver | null = null;

    function cssVar(el: Element, token: string): string {
        return getComputedStyle(el).getPropertyValue(token).trim();
    }

    function paint(): void {
        const canvas = canvasEl;
        const host = hostEl;
        if (!canvas || !host) return;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const read = (token: string, fallback: string) => cssVar(host, token) || fallback;
        canvas.width = Math.max(1, Math.round(widthPx * dpr));
        canvas.height = Math.max(1, Math.round(heightPx * dpr));
        drawStrip(ctx, bars, {
            widthPx,
            heightPx,
            dpr,
            selectedOrdinal,
            cutOrdinals,
            gcOrdinals,
            theme: {
                background: read('--bg-surface', '#131925'),
                bar: read('--sub-none', '#5c6b85'),
                over: read('--warn', '#d9a441'),
                selected: read('--text', '#d4dded'),
                line: read('--border', '#28344a'),
                cut: read('--text-faint', '#8a98b3'),
                gc: read('--bad', '#f25d63'),
            },
        });
    }

    $effect(() => {
        void bars;
        void selectedOrdinal;
        void cutOrdinals;
        void gcOrdinals;
        void widthPx;
        void heightPx;
        void dpr;
        paint();
    });

    function measure(): void {
        if (!hostEl) return;
        const box = hostEl.getBoundingClientRect();
        widthPx = Math.max(1, Math.round(box.width));
        heightPx = Math.max(1, Math.round(box.height) || HEIGHT_PX);
        dpr = globalThis.devicePixelRatio || 1;
    }

    onMount(() => {
        measure();
        if (typeof ResizeObserver !== 'undefined' && hostEl) {
            ro = new ResizeObserver(() => measure());
            ro.observe(hostEl);
        }
    });

    onDestroy(() => ro?.disconnect());

    function indexFromEvent(e: MouseEvent): number {
        const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
        return barIndexAt(e.clientX - rect.left, rect.width, bars.length);
    }

    function handleClick(e: MouseEvent): void {
        const i = indexFromEvent(e);
        if (i >= 0) onSelect?.(bars[i].ordinal);
    }

    function handleMove(e: MouseEvent): void {
        hoverIndex = indexFromEvent(e);
    }

    let hovered = $derived<StripBar | null>(bars[hoverIndex] ?? null);
</script>

<div class="strip" data-testid="frame-strip">
    <div class="head">
        <span class="dim">{t('strip.title')}</span>
        {#if hovered}
            <span class="read" data-testid="strip-hover">
                {hovered.ordinal} · {ns(hovered.durationUs * 1000)}
            </span>
        {:else}
            <span class="read dim">{t('strip.budget')}</span>
        {/if}
    </div>
    <div class="plot" style="--gc-band-px: {GC_BAND_PX}px">
        <div class="axis" aria-hidden="true">
            {#each gridLines() as line (line.fps)}
                <span style="bottom: calc({GC_BAND_PX}px + {line.at} * (100% - {GC_BAND_PX}px))">
                    <b>{line.fps} FPS</b><em>{line.ms.toFixed(1)} ms</em>
                </span>
            {/each}
        </div>
        <div class="canvaswrap" bind:this={hostEl}>
            <!-- svelte-ignore a11y_no_interactive_element_to_noninteractive_role -->
            <canvas
                bind:this={canvasEl}
                style="width:100%;height:100%"
                onclick={handleClick}
                onmousemove={handleMove}
                onmouseleave={() => (hoverIndex = -1)}
                role="img"
                aria-label={t('strip.title')}
            ></canvas>
        </div>
    </div>
</div>

<style>
    .strip {
        overflow: hidden;
        border-bottom: 1px solid var(--border-soft);
    }
    .head {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 4px 8px;
        font-size: var(--f-small, 11.5px);
        border-bottom: 1px solid var(--border-soft);
    }
    .dim {
        color: var(--text-dim);
    }
    .read {
        font-family: var(--font-mono);
        color: var(--text-dim);
    }
    .plot {
        position: relative;
        display: grid;
        grid-template-columns: var(--gut, 112px) 1fr;
        /* the extra band is the reserved GC tick lane below the bars' baseline */
        height: calc(128px + var(--gc-band-px));
        background: var(--bg-void);
    }
    .axis {
        position: relative;
    }
    .axis span {
        position: absolute;
        left: 0;
        width: 100%;
        height: 0;
        border-top: 1px dashed var(--border);
    }
    /* labels hang below their rule; at bottom they would escape the box and land on the header */
    .axis b,
    .axis em {
        position: absolute;
        top: 1px;
        font: 400 calc(10px * var(--f, 1.08)) / 1 var(--font-mono);
        font-style: normal;
        white-space: nowrap;
    }
    .axis b {
        left: 6px;
        color: var(--text-faint);
        font-weight: 400;
    }
    .axis em {
        right: 6px;
        color: var(--border-strong);
    }
    .canvaswrap {
        position: relative;
        min-width: 0;
        border-left: 1px solid var(--border);
    }
    /* the dashed rules continue across the bars so a bar can be read against them */
    .canvaswrap::before {
        content: '';
        position: absolute;
        inset: 0;
        pointer-events: none;
        z-index: 1;
        background: repeating-linear-gradient(
            to top,
            transparent 0 24.9%,
            var(--border-soft) 24.9%,
            transparent 25.1%
        );
        opacity: 0.5;
    }
    canvas {
        display: block;
        position: absolute;
        inset: 0;
        width: 100%;
        height: 100%;
        cursor: pointer;
    }
</style>
