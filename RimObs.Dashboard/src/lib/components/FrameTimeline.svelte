<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import type { TreeNode } from '../frameTree';
    import { quadIndexForNode } from '../frameLayout';
    import {
        EMPTY_SERIES,
        layoutSeries,
        hitTestSeries,
        resolveFocusIndex,
        entryOfNode,
        visibleEntries,
        visibleGaps,
        type FrameSeries,
    } from '../frameSeries';
    import {
        fitView,
        clampView,
        zoomAbout,
        panBy,
        moveFocus,
        scrollContentPx,
        scrollLeftPx,
        viewFromScrollLeft,
        type ViewRange,
        type Focus,
        type FocusMove,
    } from '../frameView';
    import { readTheme, drawTimeline, ROW_HEIGHT } from '../frameDraw';
    import { shareOfFrame, shareOfBudget, percent } from '../frameCost';
    import { ns } from '../format';
    import { t } from '../i18n';

    let {
        series = EMPTY_SERIES,
        names,
        selectedNode = $bindable(-1),
        selectedOrdinal = null,
        onContext,
    }: {
        series?: FrameSeries;
        selectedOrdinal?: number | null;
        names: Map<number, { name: string; subsystem: string | null }>;
        selectedNode?: number;
        onContext?: (p: { sectionId: number; x: number; y: number }) => void;
    } = $props();

    const ANIM_MS = 180;
    const ZOOM_IN = 0.9;
    const ZOOM_OUT = 1 / 0.9;
    // deeper than the stage is tall on purpose; .stage takes over with a native scrollbar.
    const MAX_DEPTH = 128;

    // matches --ease-out (theme.css) closely enough at 180ms.
    const EASE = (x: number): number => 1 - (1 - x) ** 5;

    let empty = $derived(series.entries.length === 0);
    let bounds = $derived<ViewRange>({ startUs: series.startUs, endUs: series.endUs });

    $effect(() => {
        selectedNode = focusIndex;
    });

    // the inbound direction is a call, not a second effect: binding both ways would make
    // focus and selectedNode write each other on every flush.
    export function focusNode(index: number): void {
        const node = series.nodes[index];
        if (node) focus = { depth: node.depth, atUs: node.startUs };
    }

    // the window holds many frames so a user can zoom out to them, but landing on the whole
    // window would bury the frame they picked. default to that frame alone.
    let selectedBounds = $derived.by<ViewRange>(() => {
        const entry = series.entries.find((e) => e.ordinal === selectedOrdinal);
        return entry ? { startUs: entry.startUs, endUs: entry.endUs } : bounds;
    });

    let view = $state<ViewRange | null>(null);
    // clamped on read, not just on write: the window grows under us on every poll, and a
    // view from a longer series culls every node in a shorter one.
    let effectiveView = $derived(view ? clampView(view, bounds) : fitView(selectedBounds));

    // holds the in-flight interpolated range while a zoom animation runs, so the layout
    // (quads) tracks what is actually painted instead of jumping to the target at t=0.
    let animatedView = $state<ViewRange | null>(null);
    let layoutView = $derived(animatedView ?? effectiveView);
    let minVisibleDurationUs = $derived((layoutView.endUs - layoutView.startUs) / 2000);

    let widthPx = $state(600);
    let dpr = $state(1);

    let quads = $derived(
        layoutSeries(series, {
            viewStartUs: layoutView.startUs,
            viewEndUs: layoutView.endUs,
            widthPx,
            maxDepth: MAX_DEPTH,
            minWidthPx: 2,
            minVisibleDurationUs,
        }),
    );
    let gaps = $derived(visibleGaps(series.gaps, layoutView.startUs, layoutView.endUs));
    let shown = $derived(
        visibleEntries(series.entries, effectiveView.startUs, effectiveView.endUs),
    );
    let shownFrames = $derived(Math.max(0, shown.hi - shown.lo + 1));

    // depth of the whole window, capped like the layout, so height is stable instead of
    // resizing every time a zoom step folds or reveals a row.
    let deepestDepth = $derived(
        series.nodes.reduce((max, n) => Math.max(max, Math.min(n.depth, MAX_DEPTH - 1)), 0),
    );
    let heightPx = $derived(Math.max(1, deepestDepth + 1) * ROW_HEIGHT);

    let focus = $state<Focus | null>(null);
    let hoverAt = $state<{ depth: number; atUs: number } | null>(null);

    let focusIndex = $derived(focus ? resolveFocusIndex(series, focus.depth, focus.atUs) : -1);
    let hoverIndex = $derived(hoverAt ? hitTestSeries(series, hoverAt.depth, hoverAt.atUs) : -1);
    let focusQuadIndex = $derived(
        focusIndex >= 0 ? quadIndexForNode(quads, series.nodes[focusIndex]) : -1,
    );
    let hoverQuadIndex = $derived(
        hoverIndex >= 0 ? quadIndexForNode(quads, series.nodes[hoverIndex]) : -1,
    );

    // stable on purpose: a per-frame label re-announces at 4Hz. numbers live in the StatCards.
    const ariaLabel = 'frame timeline';
    let rangeText = $derived(ns((effectiveView.endUs - effectiveView.startUs) * 1000));

    // five evenly spaced marks, labelled from the start of the window rather than the
    // session anchor so the numbers stay short.
    let ticks = $derived(
        [0, 0.2, 0.4, 0.6, 0.8].map((f) => ({
            at: f,
            label: ns(
                (effectiveView.startUs -
                    series.startUs +
                    (effectiveView.endUs - effectiveView.startUs) * f) *
                    1000,
            ),
        })),
    );

    function nodeName(n: TreeNode): string {
        return names.get(n.sectionId)?.name ?? `section ${n.sectionId}`;
    }
    function frameDurationOf(index: number): number {
        return entryOfNode(series, index)?.durationUs ?? 0;
    }
    function nodeCost(index: number): string {
        const n = series.nodes[index];
        return `${percent(shareOfFrame(n.durUs, frameDurationOf(index)))} of frame, ${percent(shareOfBudget(n.durUs))} of budget`;
    }
    let liveText = $derived(
        focusIndex >= 0
            ? `${nodeName(series.nodes[focusIndex])}, ${ns(series.nodes[focusIndex].durUs * 1000)}, ${nodeCost(focusIndex)}`
            : '',
    );

    let canvasEl = $state<HTMLCanvasElement | null>(null);
    let hostEl = $state<HTMLDivElement | null>(null);
    let scrollEl = $state<HTMLDivElement | null>(null);
    let hoverClientX = $state(0);
    let hoverClientY = $state(0);
    let dirty = $state(true);
    let rafId = 0;
    let ro: ResizeObserver | null = null;
    let animFrom: ViewRange | null = null;
    let animTo: ViewRange | null = null;
    let animStart = 0;

    $effect(() => {
        void series;
        void effectiveView;
        void hoverQuadIndex;
        void focusQuadIndex;
        void widthPx;
        void heightPx;
        void names;
        dirty = true;
    });

    let contentPx = $derived(scrollContentPx(effectiveView, bounds, widthPx));
    let wantScrollLeft = $derived(scrollLeftPx(effectiveView, bounds, widthPx, contentPx));

    $effect(() => {
        const el = scrollEl;
        const want = wantScrollLeft;
        if (el && Math.abs(el.scrollLeft - want) > 1) el.scrollLeft = want;
    });

    function handleScroll(): void {
        const el = scrollEl;
        if (!el || empty) return;
        const next = viewFromScrollLeft(el.scrollLeft, effectiveView, bounds, widthPx, contentPx);
        const pxUs = (effectiveView.endUs - effectiveView.startUs) / Math.max(widthPx, 1);
        // sub-pixel moves are the echo of our own write; acting on them oscillates.
        if (Math.abs(next.startUs - effectiveView.startUs) < pxUs) return;
        setViewInstant(next);
    }

    function reducedMotion(): boolean {
        return (
            typeof matchMedia === 'function' &&
            matchMedia('(prefers-reduced-motion: reduce)').matches
        );
    }

    function setViewInstant(next: ViewRange): void {
        if (empty) return;
        animFrom = null;
        animTo = null;
        view = clampView(next, bounds);
        dirty = true;
    }

    function zoomToNode(node: TreeNode): void {
        if (empty) return;
        const target = clampView({ startUs: node.startUs, endUs: node.endUs }, bounds);
        if (reducedMotion()) {
            animFrom = null;
            animTo = null;
        } else {
            animFrom = effectiveView;
            animTo = target;
            animStart = performance.now();
        }
        view = target;
        dirty = true;
    }

    function resetView(): void {
        animFrom = null;
        animTo = null;
        view = null;
        dirty = true;
    }

    function handleMove(move: FocusMove): void {
        if (series.nodes.length === 0) return;
        const idx = focusIndex >= 0 ? focusIndex : 0;
        const next = moveFocus(series.nodes, idx, move);
        const node = series.nodes[next];
        if (node) focus = { depth: node.depth, atUs: node.startUs };
    }

    function handleKeydown(event: KeyboardEvent): void {
        switch (event.key) {
            case 'ArrowLeft':
                event.preventDefault();
                handleMove('left');
                break;
            case 'ArrowRight':
                event.preventDefault();
                handleMove('right');
                break;
            case 'ArrowUp':
                event.preventDefault();
                handleMove('up');
                break;
            case 'ArrowDown':
                event.preventDefault();
                handleMove('down');
                break;
            case 'Enter':
                event.preventDefault();
                if (focusIndex >= 0) zoomToNode(series.nodes[focusIndex]);
                break;
            case 'Escape':
            case 'Home':
                event.preventDefault();
                resetView();
                break;
            case '+':
                event.preventDefault();
                setViewInstant(
                    zoomAbout(
                        effectiveView,
                        (effectiveView.startUs + effectiveView.endUs) / 2,
                        ZOOM_IN,
                    ),
                );
                break;
            case '-':
                event.preventDefault();
                setViewInstant(
                    zoomAbout(
                        effectiveView,
                        (effectiveView.startUs + effectiveView.endUs) / 2,
                        ZOOM_OUT,
                    ),
                );
                break;
        }
    }

    function posToView(clientX: number, clientY: number): { depth: number; atUs: number } {
        const rect = canvasEl!.getBoundingClientRect();
        const x = clientX - rect.left;
        const y = clientY - rect.top;
        const atUs =
            effectiveView.startUs +
            (x / Math.max(rect.width, 1)) * (effectiveView.endUs - effectiveView.startUs);
        return { depth: Math.floor(y / ROW_HEIGHT), atUs };
    }

    let dragState: {
        startClientX: number;
        startClientY: number;
        startView: ViewRange;
        moved: boolean;
    } | null = null;

    function handlePointerDown(event: PointerEvent): void {
        if (empty || !canvasEl) return;
        canvasEl.focus();
        canvasEl.setPointerCapture(event.pointerId);
        dragState = {
            startClientX: event.clientX,
            startClientY: event.clientY,
            startView: effectiveView,
            moved: false,
        };
    }

    function handlePointerMove(event: PointerEvent): void {
        if (empty || !canvasEl) return;
        if (dragState) {
            const dx = event.clientX - dragState.startClientX;
            if (Math.abs(dx) > 2 || Math.abs(event.clientY - dragState.startClientY) > 2) {
                dragState.moved = true;
            }
            const span = dragState.startView.endUs - dragState.startView.startUs;
            const pxPerUs = widthPx / span;
            setViewInstant(panBy(dragState.startView, -dx / pxPerUs));
        } else {
            hoverAt = posToView(event.clientX, event.clientY);
            hoverClientX = event.clientX;
            hoverClientY = event.clientY;
        }
    }

    function handlePointerUp(event: PointerEvent): void {
        if (empty || !canvasEl) return;
        const wasDrag = dragState?.moved ?? false;
        dragState = null;
        if (wasDrag) return;
        const { depth, atUs } = posToView(event.clientX, event.clientY);
        const idx = hitTestSeries(series, depth, atUs);
        if (idx >= 0) zoomToNode(series.nodes[idx]);
    }

    function handleContextMenu(event: MouseEvent): void {
        if (empty || !canvasEl || !onContext) return;
        const { depth, atUs } = posToView(event.clientX, event.clientY);
        const idx = hitTestSeries(series, depth, atUs);
        if (idx < 0) return;
        event.preventDefault();
        onContext({ sectionId: series.nodes[idx].sectionId, x: event.clientX, y: event.clientY });
    }

    function handlePointerLeave(): void {
        hoverAt = null;
    }

    function handleWheel(event: WheelEvent): void {
        if (empty || !canvasEl) return;
        event.preventDefault();
        // a trackpad's horizontal axis pans; the vertical one zooms at the cursor.
        if (Math.abs(event.deltaX) > Math.abs(event.deltaY)) {
            const span = effectiveView.endUs - effectiveView.startUs;
            setViewInstant(panBy(effectiveView, (event.deltaX / Math.max(widthPx, 1)) * span));
            return;
        }
        const { atUs } = posToView(event.clientX, event.clientY);
        setViewInstant(zoomAbout(effectiveView, atUs, event.deltaY < 0 ? ZOOM_IN : ZOOM_OUT));
    }

    function draw(drawView: ViewRange): void {
        if (!canvasEl) return;
        const ctx = canvasEl.getContext('2d');
        if (!ctx) return;
        drawTimeline(ctx, quads, {
            view: drawView,
            widthPx,
            heightPx,
            dpr,
            theme: readTheme(canvasEl),
            label: (q) =>
                names.get(q.sectionId)?.name ?? (q.sectionId < 0 ? 'mixed' : `#${q.sectionId}`),
            subsystem: (q) => names.get(q.sectionId)?.subsystem ?? null,
            hoverIndex: hoverQuadIndex,
            focusIndex: focusQuadIndex,
            gaps,
        });
    }

    function tick(now: number): void {
        rafId = requestAnimationFrame(tick);
        const animating = animTo !== null && animFrom !== null;
        if (!dirty && !animating) return;

        let drawView = effectiveView;
        if (animating) {
            const x = Math.min((now - animStart) / ANIM_MS, 1);
            const eased = EASE(x);
            drawView = {
                startUs: animFrom!.startUs + (animTo!.startUs - animFrom!.startUs) * eased,
                endUs: animFrom!.endUs + (animTo!.endUs - animFrom!.endUs) * eased,
            };
            animatedView = x >= 1 ? null : drawView;
            if (x >= 1) {
                animFrom = null;
                animTo = null;
            }
        } else {
            animatedView = null;
        }
        draw(drawView);
        dirty = false;
    }

    onMount(() => {
        dpr = typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1;
        rafId = requestAnimationFrame(tick);
    });

    // hostEl only exists once a frame has arrived, so this cannot be done on mount: the
    // element is null then and the width stays at its placeholder forever.
    $effect(() => {
        const host = hostEl;
        if (!host || typeof ResizeObserver === 'undefined') return;

        const observer = new ResizeObserver((entries) => {
            const w = entries[0]?.contentRect.width;
            if (w) {
                widthPx = Math.max(1, Math.round(w));
                dirty = true;
            }
        });
        observer.observe(host);
        widthPx = Math.max(1, Math.round(host.getBoundingClientRect().width) || widthPx);
        ro = observer;

        return () => {
            observer.disconnect();
            ro = null;
        };
    });

    onDestroy(() => {
        if (rafId) cancelAnimationFrame(rafId);
        ro?.disconnect();
    });
</script>

{#if empty}
    <div class="empty" data-testid="frame-empty">no frame captured yet</div>
{:else}
    <div class="wrap" bind:this={hostEl}>
        <div class="ruler" aria-hidden="true" data-testid="frame-ruler">
            {#each ticks as tick (tick.at)}
                <span style="left:{tick.at * 100}%">{tick.label}</span>
            {/each}
        </div>
        <!-- svelte-ignore a11y_no_interactive_element_to_noninteractive_role -->
        <canvas
            bind:this={canvasEl}
            tabindex="0"
            role="application"
            aria-roledescription="frame timeline"
            aria-label={ariaLabel}
            width={widthPx * dpr}
            height={heightPx * dpr}
            style="width: {widthPx}px; height: {heightPx}px;"
            onkeydown={handleKeydown}
            oncontextmenu={handleContextMenu}
            onwheel={handleWheel}
            onpointerdown={handlePointerDown}
            onpointermove={handlePointerMove}
            onpointerup={handlePointerUp}
            onpointerleave={handlePointerLeave}
        ></canvas>
        {#if hoverIndex >= 0}
            {@const hoverNode = series.nodes[hoverIndex]}
            {@const hoverQuad = hoverQuadIndex >= 0 ? quads[hoverQuadIndex] : null}
            <div class="tip" style="left: {hoverClientX + 14}px; top: {hoverClientY + 14}px">
                <span class="tip-name mono">{nodeName(hoverNode)}</span>
                <dl>
                    <dt>subsystem</dt>
                    <dd>{names.get(hoverNode.sectionId)?.subsystem ?? 'untagged'}</dd>
                    <dt>frame</dt>
                    <dd class="mono">{entryOfNode(series, hoverIndex)?.ordinal ?? '--'}</dd>
                    <dt>duration</dt>
                    <dd class="mono">{ns(hoverNode.durUs * 1000)}</dd>
                    <dt>of frame</dt>
                    <dd class="mono">
                        {percent(shareOfFrame(hoverNode.durUs, frameDurationOf(hoverIndex)))}
                    </dd>
                    <dt>of budget</dt>
                    <dd class="mono">{percent(shareOfBudget(hoverNode.durUs))}</dd>
                    {#if hoverQuad && hoverQuad.count > 1}
                        <dt>calls</dt>
                        <dd class="mono">{hoverQuad.count}</dd>
                    {/if}
                </dl>
            </div>
        {/if}
    </div>
    <div
        class="hscroll"
        bind:this={scrollEl}
        onscroll={handleScroll}
        aria-label={t('flamegraph.timeScroll')}
        data-testid="frame-hscroll"
    >
        <div class="hscroll-inner" style="width: {contentPx}px"></div>
    </div>
    {#if focusIndex >= 0}
        <p class="meta mono" data-testid="frame-selected">
            {nodeName(series.nodes[focusIndex])}, {ns(series.nodes[focusIndex].durUs * 1000)}, {nodeCost(
                focusIndex,
            )}
        </p>
    {/if}
    <p class="meta">
        <span data-testid="frame-range" class="mono">{rangeText}</span>
        <span data-testid="frame-span" class="mono"
            >{t('flamegraph.overFrames').replace('{n}', String(shownFrames))}</span
        >
        <button
            type="button"
            class="reset"
            onclick={resetView}
            disabled={view === null}
            data-testid="reset-view">{t('flamegraph.resetView')}</button
        >
    </p>
    <div class="sr-only" role="status" aria-live="polite">{liveText}</div>
{/if}

<style>
    .wrap {
        position: relative;
        width: 100%;
    }
    .reset {
        margin-left: auto;
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        color: var(--text-dim);
        font: inherit;
        font-size: 0.74rem;
        padding: 1px 8px;
        cursor: pointer;
    }
    .reset:hover:not(:disabled) {
        border-color: var(--cyan);
        color: var(--cyan);
    }
    .reset:disabled {
        opacity: 0.4;
        cursor: default;
    }
    canvas {
        display: block;
        max-width: 100%;
        border-radius: var(--r-sm);
        background: var(--bg-surface);
        outline: none;
    }
    .ruler {
        position: relative;
        height: 18px;
        border-bottom: 1px solid var(--border-soft);
    }
    .ruler span {
        position: absolute;
        top: 3px;
        font: 400 10.5px/1 var(--font-mono);
        color: var(--text-faint);
        padding-left: 4px;
        border-left: 1px solid var(--border);
    }
    canvas:focus {
        box-shadow: var(--ring-focus);
    }
    .hscroll {
        overflow-x: auto;
        overflow-y: hidden;
        height: 14px;
        border-top: 1px solid var(--border-soft);
    }
    .hscroll-inner {
        height: 1px;
    }
    .empty {
        padding: var(--s-7) var(--s-4);
        text-align: center;
        color: var(--text-faint);
        font-size: 0.85rem;
    }
    .meta {
        display: flex;
        align-items: center;
        gap: var(--s-2);
        margin: var(--s-2) 0 0;
        font-size: 0.78rem;
        color: var(--text-dim);
    }
    .meta span + span::before {
        content: ' · ';
        color: var(--border-strong);
    }
    .tip {
        position: fixed;
        z-index: 40;
        pointer-events: none;
        min-width: 190px;
        padding: var(--s-2) var(--s-3);
        border: 1px solid var(--border-strong);
        border-radius: var(--r-sm);
        background: var(--bg-elev);
        box-shadow: 0 6px 20px rgb(0 0 0 / 45%);
    }
    .tip-name {
        display: block;
        margin-bottom: var(--s-2);
        font-size: 0.74rem;
        color: var(--text);
        word-break: break-all;
    }
    .tip dl {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: 2px var(--s-3);
        margin: 0;
        font-size: 0.72rem;
    }
    .tip dt {
        color: var(--text-faint);
    }
    .tip dd {
        margin: 0;
        text-align: right;
        color: var(--text);
    }
    .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
    }
</style>
