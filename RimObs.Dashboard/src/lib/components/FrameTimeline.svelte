<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { buildFrameTree, type FrameData, type TreeNode } from '../frameTree';
    import { layoutFrame, quadIndexForNode } from '../frameLayout';
    import {
        fitView,
        clampView,
        zoomAbout,
        panBy,
        hitTest,
        moveFocus,
        type ViewRange,
        type Focus,
        type FocusMove,
    } from '../frameView';
    import { readTheme, drawTimeline, ROW_HEIGHT } from '../frameDraw';
    import { shareOfFrame, shareOfBudget, percent } from '../frameCost';
    import { ns } from '../format';

    let {
        frame,
        names,
        orphanCount = $bindable(0),
        selectedNode = $bindable(-1),
    }: {
        frame: FrameData | null;
        names: Map<number, { name: string; subsystem: string | null }>;
        orphanCount?: number;
        selectedNode?: number;
    } = $props();

    const ANIM_MS = 180;
    const ZOOM_IN = 0.9;
    const ZOOM_OUT = 1 / 0.9;
    const MAX_DEPTH = 32;

    // matches --ease-out (theme.css) closely enough at 180ms.
    const EASE = (t: number): number => 1 - (1 - t) ** 5;

    let tree = $derived(frame ? buildFrameTree(frame) : { nodes: [], orphanCount: 0 });

    $effect(() => {
        orphanCount = tree.orphanCount;
    });

    $effect(() => {
        selectedNode = focusTreeIndex;
    });

    // the inbound direction is a call, not a second effect: binding both ways would make
    // focus and selectedNode write each other on every flush.
    export function focusNode(index: number): void {
        const node = tree.nodes[index];
        if (node) focus = { depth: node.depth, atUs: node.startUs };
    }

    let view = $state<ViewRange | null>(null);
    // clamped on read, not just on write: the frame changes under us on every poll and on
    // every scrub, and a view from a longer frame culls every node in a shorter one.
    let effectiveView = $derived(
        view ? clampView(view, frame?.duration_us ?? 0) : fitView(frame?.duration_us ?? 0),
    );

    // holds the in-flight interpolated range while a zoom animation runs, so the layout
    // (quads) tracks what is actually painted instead of jumping to the target at t=0.
    let animatedView = $state<ViewRange | null>(null);
    let layoutView = $derived(animatedView ?? effectiveView);
    let minVisibleDurationUs = $derived((layoutView.endUs - layoutView.startUs) / 2000);

    let widthPx = $state(600);
    let dpr = $state(1);

    let quads = $derived(
        layoutFrame(tree.nodes, {
            viewStartUs: layoutView.startUs,
            viewEndUs: layoutView.endUs,
            widthPx,
            maxDepth: MAX_DEPTH,
            minWidthPx: 2,
            minVisibleDurationUs,
        }),
    );
    // depth of the actual tree, capped like the layout, so height is stable per frame
    // instead of resizing every time a zoom step folds or reveals a row.
    let deepestDepth = $derived(
        tree.nodes.reduce((max, n) => Math.max(max, Math.min(n.depth, MAX_DEPTH - 1)), 0),
    );
    let heightPx = $derived(Math.max(1, deepestDepth + 1) * ROW_HEIGHT);

    // hitTest is half-open, so a zero-duration node (dur_us: 0 is routine at microsecond
    // resolution) never matches on atUs. fall back to the node that starts exactly there.
    function resolveFocus(nodes: TreeNode[], f: Focus): number {
        const hit = hitTest(nodes, f.depth, f.atUs);
        if (hit >= 0) return hit;
        for (let i = 0; i < nodes.length; i++) {
            if (nodes[i].depth === f.depth && nodes[i].startUs === f.atUs) return i;
        }
        return -1;
    }

    let focus = $state<Focus | null>(null);
    let hoverAt = $state<{ depth: number; atUs: number } | null>(null);

    let focusTreeIndex = $derived(focus ? resolveFocus(tree.nodes, focus) : -1);
    let hoverTreeIndex = $derived(hoverAt ? hitTest(tree.nodes, hoverAt.depth, hoverAt.atUs) : -1);
    let focusQuadIndex = $derived(
        focusTreeIndex >= 0 ? quadIndexForNode(quads, tree.nodes[focusTreeIndex]) : -1,
    );
    let hoverQuadIndex = $derived(
        hoverTreeIndex >= 0 ? quadIndexForNode(quads, tree.nodes[hoverTreeIndex]) : -1,
    );

    // stable on purpose: a per-frame label re-announces at 4Hz. numbers live in the StatCards.
    const ariaLabel = 'current frame';
    let rangeText = $derived(ns((effectiveView.endUs - effectiveView.startUs) * 1000));

    // five evenly spaced marks; the view is already clamped so these always sit in frame.
    let ticks = $derived(
        [0, 0.2, 0.4, 0.6, 0.8].map((f) => ({
            at: f,
            label: ns(
                (effectiveView.startUs + (effectiveView.endUs - effectiveView.startUs) * f) * 1000,
            ),
        })),
    );

    function nodeName(n: TreeNode): string {
        return names.get(n.sectionId)?.name ?? `section ${n.sectionId}`;
    }
    function nodeCost(n: TreeNode): string {
        const frameDurationUs = frame?.duration_us ?? 0;
        return `${percent(shareOfFrame(n.durUs, frameDurationUs))} of frame, ${percent(shareOfBudget(n.durUs))} of budget`;
    }
    let liveText = $derived(
        focusTreeIndex >= 0
            ? `${nodeName(tree.nodes[focusTreeIndex])}, ${ns(tree.nodes[focusTreeIndex].durUs * 1000)}, ${nodeCost(tree.nodes[focusTreeIndex])}`
            : '',
    );

    let canvasEl = $state<HTMLCanvasElement | null>(null);
    let hostEl = $state<HTMLDivElement | null>(null);
    let hoverClientX = $state(0);
    let hoverClientY = $state(0);
    let dirty = $state(true);
    let rafId = 0;
    let ro: ResizeObserver | null = null;
    let animFrom: ViewRange | null = null;
    let animTo: ViewRange | null = null;
    let animStart = 0;

    $effect(() => {
        void frame;
        void effectiveView;
        void hoverQuadIndex;
        void focusQuadIndex;
        void widthPx;
        void heightPx;
        void names;
        dirty = true;
    });

    function reducedMotion(): boolean {
        return (
            typeof matchMedia === 'function' &&
            matchMedia('(prefers-reduced-motion: reduce)').matches
        );
    }

    function setViewInstant(next: ViewRange): void {
        if (!frame) return;
        animFrom = null;
        animTo = null;
        view = clampView(next, frame.duration_us);
        dirty = true;
    }

    function zoomToNode(node: TreeNode): void {
        if (!frame) return;
        const target = clampView({ startUs: node.startUs, endUs: node.endUs }, frame.duration_us);
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
        if (tree.nodes.length === 0) return;
        const idx = focusTreeIndex >= 0 ? focusTreeIndex : 0;
        const next = moveFocus(tree.nodes, idx, move);
        const node = tree.nodes[next];
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
                if (focusTreeIndex >= 0) zoomToNode(tree.nodes[focusTreeIndex]);
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
        if (!frame || !canvasEl) return;
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
        if (!frame || !canvasEl) return;
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
        if (!frame || !canvasEl) return;
        const wasDrag = dragState?.moved ?? false;
        dragState = null;
        if (wasDrag) return;
        const { depth, atUs } = posToView(event.clientX, event.clientY);
        const idx = hitTest(tree.nodes, depth, atUs);
        if (idx >= 0) zoomToNode(tree.nodes[idx]);
    }

    function handlePointerLeave(): void {
        hoverAt = null;
    }

    function handleWheel(event: WheelEvent): void {
        if (!frame || !canvasEl) return;
        event.preventDefault();
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
        });
    }

    function tick(now: number): void {
        rafId = requestAnimationFrame(tick);
        const animating = animTo !== null && animFrom !== null;
        if (!dirty && !animating) return;

        let drawView = effectiveView;
        if (animating) {
            const t = Math.min((now - animStart) / ANIM_MS, 1);
            const eased = EASE(t);
            drawView = {
                startUs: animFrom!.startUs + (animTo!.startUs - animFrom!.startUs) * eased,
                endUs: animFrom!.endUs + (animTo!.endUs - animFrom!.endUs) * eased,
            };
            animatedView = t >= 1 ? null : drawView;
            if (t >= 1) {
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
        if (hostEl && typeof ResizeObserver !== 'undefined') {
            ro = new ResizeObserver((entries) => {
                const w = entries[0]?.contentRect.width;
                if (w) {
                    widthPx = Math.max(1, Math.round(w));
                    dirty = true;
                }
            });
            ro.observe(hostEl);
        }
        rafId = requestAnimationFrame(tick);
    });

    onDestroy(() => {
        if (rafId) cancelAnimationFrame(rafId);
        ro?.disconnect();
    });
</script>

{#if !frame}
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
            onwheel={handleWheel}
            onpointerdown={handlePointerDown}
            onpointermove={handlePointerMove}
            onpointerup={handlePointerUp}
            onpointerleave={handlePointerLeave}
        ></canvas>
        {#if hoverTreeIndex >= 0}
            {@const hoverNode = tree.nodes[hoverTreeIndex]}
            {@const hoverQuad = hoverQuadIndex >= 0 ? quads[hoverQuadIndex] : null}
            <div class="tip" style="left: {hoverClientX + 14}px; top: {hoverClientY + 14}px">
                <span class="tip-name mono">{nodeName(hoverNode)}</span>
                <dl>
                    <dt>subsystem</dt>
                    <dd>{names.get(hoverNode.sectionId)?.subsystem ?? 'untagged'}</dd>
                    <dt>duration</dt>
                    <dd class="mono">{ns(hoverNode.durUs * 1000)}</dd>
                    <dt>of frame</dt>
                    <dd class="mono">
                        {percent(shareOfFrame(hoverNode.durUs, frame.duration_us))}
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
    {#if focusTreeIndex >= 0}
        {@const focusNode = tree.nodes[focusTreeIndex]}
        <p class="meta mono" data-testid="frame-selected">
            {nodeName(focusNode)}, {ns(focusNode.durUs * 1000)}, {nodeCost(focusNode)}
        </p>
    {/if}
    <p class="meta">
        <span data-testid="frame-range" class="mono">{rangeText}</span>
    </p>
    <div class="sr-only" role="status" aria-live="polite">{liveText}</div>
{/if}

<style>
    .wrap {
        position: relative;
        width: 100%;
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
    .empty {
        padding: var(--s-7) var(--s-4);
        text-align: center;
        color: var(--text-faint);
        font-size: 0.85rem;
    }
    .meta {
        margin: var(--s-2) 0 0;
        font-size: 0.78rem;
        color: var(--text-dim);
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
