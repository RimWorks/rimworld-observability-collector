<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { buildBars, barIndexAt, type StripBar } from '../frameStrip';
    import { drawStrip } from '../stripDraw';
    import { ns } from '../format';
    import { t } from '../i18n';

    let {
        ordinals,
        durationsUs,
        selectedOrdinal = null,
        onSelect,
    }: {
        ordinals: readonly number[];
        durationsUs: readonly number[];
        selectedOrdinal?: number | null;
        onSelect?: (ordinal: number) => void;
    } = $props();

    const HEIGHT_PX = 44;

    let bars = $derived(buildBars(ordinals, durationsUs));
    let canvasEl = $state<HTMLCanvasElement | null>(null);
    let hostEl = $state<HTMLDivElement | null>(null);
    let widthPx = $state(600);
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
        canvas.height = Math.round(HEIGHT_PX * dpr);
        drawStrip(ctx, bars, {
            widthPx,
            heightPx: HEIGHT_PX,
            dpr,
            selectedOrdinal,
            theme: {
                background: read('--bg-surface', '#131925'),
                bar: read('--sub-none', '#5c6b85'),
                over: read('--warn', '#d9a441'),
                selected: read('--text', '#d4dded'),
                line: read('--border', '#28344a'),
            },
        });
    }

    $effect(() => {
        void bars;
        void selectedOrdinal;
        void widthPx;
        void dpr;
        paint();
    });

    function measure(): void {
        if (!hostEl) return;
        widthPx = Math.max(1, Math.round(hostEl.getBoundingClientRect().width));
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

<div class="strip" bind:this={hostEl} data-testid="frame-strip">
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
    <!-- svelte-ignore a11y_no_interactive_element_to_noninteractive_role -->
    <canvas
        bind:this={canvasEl}
        style="width:100%;height:{HEIGHT_PX}px"
        onclick={handleClick}
        onmousemove={handleMove}
        onmouseleave={() => (hoverIndex = -1)}
        role="img"
        aria-label={t('strip.title')}
    ></canvas>
</div>

<style>
    .strip {
        margin-bottom: 0.75rem;
    }
    .head {
        display: flex;
        justify-content: space-between;
        font-size: 0.75rem;
        margin-bottom: 0.25rem;
    }
    .dim {
        color: var(--text-dim);
    }
    .read {
        font-family: var(--font-mono);
    }
    canvas {
        display: block;
        border: 1px solid var(--border);
        border-radius: 3px;
        cursor: pointer;
    }
</style>
