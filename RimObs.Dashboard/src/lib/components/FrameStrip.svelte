<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import {
        buildBars,
        barIndexAt,
        stepOrdinal,
        DEFAULT_STRIP_SLOTS,
        clampTooltipX,
        gridLines,
        GC_BAND_PX,
        type StripBar,
    } from '../frameStrip';
    import { drawStrip } from '../stripDraw';
    import { ns } from '../format';
    import { t } from '../i18n';
    import { FRAME_BUDGET_US } from '../frameCost';

    let {
        ordinals,
        durationsUs,
        selectedOrdinal = null,
        cutOrdinals = [],
        gcOrdinals = [],
        slots = DEFAULT_STRIP_SLOTS,
        selectedRange = null,
        onSelect,
        onSelectRange,
    }: {
        ordinals: readonly number[];
        durationsUs: readonly number[];
        selectedOrdinal?: number | null;
        cutOrdinals?: readonly number[];
        gcOrdinals?: readonly number[];
        /** the ring's capacity. bars fill these slots left to right and never resize. */
        slots?: number;
        /** a committed drag selection; the overlay stays on it until the caller clears it. */
        selectedRange?: { from: number; to: number } | null;
        onSelect?: (ordinal: number) => void;
        /** drag across bars: the flame loads this inclusive ordinal range. */
        onSelectRange?: (fromOrdinal: number, toOrdinal: number) => void;
    } = $props();

    const HEIGHT_PX = 132 + GC_BAND_PX;

    let bars = $derived(buildBars(ordinals, durationsUs));
    let canvasEl = $state<HTMLCanvasElement | null>(null);
    let hostEl = $state<HTMLDivElement | null>(null);
    let widthPx = $state(600);
    let heightPx = $state(HEIGHT_PX);
    let dpr = $state(1);
    let hoverIndex = $state(-1);
    let hoverX = $state(0);
    let tooltipEl = $state<HTMLDivElement | null>(null);
    let tooltipWidth = $state(0);
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
            hoveredOrdinal: bars[hoverIndex]?.ordinal ?? null,
            cutOrdinals,
            gcOrdinals,
            slots,
            theme: {
                background: read('--bg-surface', '#131925'),
                good: read('--good', '#5fcf80'),
                warn: read('--warn', '#e8b53e'),
                bad: read('--bad', '#f25d63'),
                badDeep: read('--bad-deep', '#c2393e'),
                selected: read('--text', '#d4dded'),
                hover: read('--text-dim', '#93a1ba'),
                grid: read('--border-soft', '#1c2535'),
                line: read('--border', '#28344a'),
                cut: read('--text-faint', '#8a98b3'),
                // distinct hue from the budget-color ramp so a GC mark never reads as "over budget".
                gc: read('--cyan', '#39c4d4'),
            },
        });
    }

    $effect(() => {
        void bars;
        void selectedOrdinal;
        void hoverIndex;
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
        return barIndexAt(e.clientX - rect.left, rect.width, bars.length, slots);
    }

    let dragFrom = $state(-1);
    let dragTo = $state(-1);
    let dragJustEnded = false;
    let dragging = $derived(dragFrom >= 0 && dragTo >= 0 && dragFrom !== dragTo);
    // the live drag wins while it runs; otherwise the committed range mapped onto whatever
    // bars still hold its ordinals. null once the range has scrolled out of the strip.
    let rangeIndices = $derived.by<{ lo: number; hi: number } | null>(() => {
        if (dragging) return { lo: Math.min(dragFrom, dragTo), hi: Math.max(dragFrom, dragTo) };
        if (!selectedRange) return null;
        const lo = bars.findIndex((b) => b.ordinal >= selectedRange.from);
        if (lo < 0 || bars[lo].ordinal > selectedRange.to) return null;
        let hi = lo;
        while (hi + 1 < bars.length && bars[hi + 1].ordinal <= selectedRange.to) hi++;
        return { lo, hi };
    });

    function handlePointerDown(e: PointerEvent): void {
        if (e.button !== 0) return;
        const i = indexFromEvent(e);
        if (i < 0) return;
        dragFrom = i;
        dragTo = i;
        (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
    }

    function handlePointerMove(e: PointerEvent): void {
        if (dragFrom < 0) return;
        const i = indexFromEvent(e);
        if (i >= 0) dragTo = i;
    }

    function handlePointerUp(): void {
        if (dragging) {
            const a = bars[Math.min(dragFrom, dragTo)];
            const b = bars[Math.max(dragFrom, dragTo)];
            if (a && b) {
                onSelectRange?.(a.ordinal, b.ordinal);
                // the click that follows pointerup would immediately re-pin a single frame.
                dragJustEnded = true;
            }
        }
        dragFrom = -1;
        dragTo = -1;
    }

    function handleClick(e: MouseEvent): void {
        if (dragJustEnded) {
            dragJustEnded = false;
            return;
        }
        const i = indexFromEvent(e);
        if (i >= 0) onSelect?.(bars[i].ordinal);
    }

    // the strip is a one-dimensional pick over the ring, which is what a slider is. the role
    // buys arrow keys and a spoken ordinal without inventing a widget.
    function handleKeydown(e: KeyboardEvent): void {
        const step =
            e.key === 'ArrowLeft'
                ? -1
                : e.key === 'ArrowRight'
                  ? 1
                  : e.key === 'PageUp'
                    ? -10
                    : e.key === 'PageDown'
                      ? 10
                      : e.key === 'Home'
                        ? -bars.length
                        : e.key === 'End'
                          ? bars.length
                          : 0;
        if (step === 0) return;
        e.preventDefault();
        const next = stepOrdinal(bars, selectedOrdinal, step);
        if (next !== null) onSelect?.(next);
    }

    function handleMove(e: MouseEvent): void {
        const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
        hoverX = e.clientX - rect.left;
        hoverIndex = barIndexAt(hoverX, rect.width, bars.length, slots);
    }

    let hovered = $derived<StripBar | null>(bars[hoverIndex] ?? null);
    let current = $derived(bars.find((b) => b.ordinal === selectedOrdinal) ?? null);
    let tooltipLeft = $derived(clampTooltipX(hoverX, tooltipWidth, widthPx));
    let budgetMs = $derived((FRAME_BUDGET_US / 1000).toFixed(1));
</script>

<div class="strip" data-testid="frame-strip">
    <div class="head">
        <span class="dim">{t('strip.title')}</span>
        {#if hovered}
            <span class="read" data-testid="strip-hover">
                {hovered.ordinal} | {ns(hovered.durationUs * 1000)}
            </span>
        {:else}
            <span class="read dim">{t('strip.budget')} ({budgetMs} ms)</span>
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
            <canvas
                bind:this={canvasEl}
                style="width:100%;height:100%"
                onclick={handleClick}
                onkeydown={handleKeydown}
                onmousemove={handleMove}
                onpointerdown={handlePointerDown}
                onpointermove={handlePointerMove}
                onpointerup={handlePointerUp}
                onmouseleave={() => (hoverIndex = -1)}
                tabindex="0"
                role="slider"
                aria-label={t('strip.title')}
                aria-valuemin={bars[0]?.ordinal ?? 0}
                aria-valuemax={bars[bars.length - 1]?.ordinal ?? 0}
                aria-valuenow={current?.ordinal ?? undefined}
                aria-valuetext={current
                    ? `${current.ordinal} | ${ns(current.durationUs * 1000)}`
                    : undefined}
            ></canvas>
            {#if rangeIndices}
                <div
                    class="rangesel"
                    style="left: {(rangeIndices.lo * widthPx) /
                        slots}px; width: {((rangeIndices.hi - rangeIndices.lo + 1) * widthPx) /
                        slots}px"
                    data-testid="strip-range"
                ></div>
            {/if}
            {#if hovered}
                <div
                    class="hover-tip"
                    bind:this={tooltipEl}
                    bind:clientWidth={tooltipWidth}
                    style="left: {tooltipLeft}px"
                    data-testid="strip-tooltip"
                >
                    <b>#{hovered.ordinal}</b><span>{ns(hovered.durationUs * 1000)}</span>
                </div>
            {/if}
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
    /* labels only. the rules are painted on the canvas, under the bars. */
    .axis {
        position: relative;
    }
    .axis span {
        position: absolute;
        left: 0;
        width: 100%;
        height: 0;
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
        color: var(--text-ghost);
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
        outline: none;
    }
    canvas:focus-visible {
        box-shadow: var(--ring-focus);
    }
    .rangesel {
        position: absolute;
        top: 0;
        bottom: 0;
        z-index: 1;
        background: color-mix(in srgb, var(--cyan) 18%, transparent);
        border-inline: 1px solid var(--cyan);
        pointer-events: none;
    }
    .hover-tip {
        position: absolute;
        top: 6px;
        z-index: 2;
        display: flex;
        gap: var(--s-2);
        align-items: center;
        transform: translateX(-50%);
        padding: 2px 6px;
        background: var(--bg-elev);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        font-family: var(--font-mono);
        font-size: var(--f-small, 11.5px);
        white-space: nowrap;
        color: var(--text);
        pointer-events: none;
    }
    .hover-tip b {
        color: var(--text-dim);
        font-weight: 500;
    }
</style>
