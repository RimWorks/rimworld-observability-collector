import type { TreeNode } from './frameTree';

export interface ViewRange {
    startUs: number;
    endUs: number;
}

export interface Focus {
    depth: number;
    atUs: number;
}

export type FocusMove = 'left' | 'right' | 'up' | 'down';

const MIN_SPAN_US = 0.5;

export function fitView(bounds: ViewRange): ViewRange {
    return { startUs: bounds.startUs, endUs: bounds.startUs + boundsSpan(bounds) };
}

function boundsSpan(bounds: ViewRange): number {
    return Math.max(bounds.endUs - bounds.startUs, MIN_SPAN_US);
}

export function clampView(view: ViewRange, bounds: ViewRange): ViewRange {
    const total = boundsSpan(bounds);
    const span = Math.min(Math.max(view.endUs - view.startUs, MIN_SPAN_US), total);
    const start = Math.min(Math.max(view.startUs, bounds.startUs), bounds.startUs + total - span);
    return { startUs: start, endUs: start + span };
}

// the horizontal scrollbar is a native overflow proxy: an inner spacer this wide inside a
// track this wide gives the browser a correctly sized thumb for free.
const MAX_CONTENT_PX = 1_000_000;

export function scrollContentPx(view: ViewRange, bounds: ViewRange, trackPx: number): number {
    const span = Math.max(view.endUs - view.startUs, MIN_SPAN_US);
    const want = (trackPx * boundsSpan(bounds)) / span;
    return Math.round(Math.min(Math.max(want, trackPx), MAX_CONTENT_PX));
}

export function scrollLeftPx(
    view: ViewRange,
    bounds: ViewRange,
    trackPx: number,
    contentPx: number,
): number {
    const slack = boundsSpan(bounds) - (view.endUs - view.startUs);
    if (slack <= 0) return 0;
    return Math.round(((view.startUs - bounds.startUs) / slack) * (contentPx - trackPx));
}

export function viewFromScrollLeft(
    scrollLeft: number,
    view: ViewRange,
    bounds: ViewRange,
    trackPx: number,
    contentPx: number,
): ViewRange {
    const span = Math.max(view.endUs - view.startUs, MIN_SPAN_US);
    const travel = contentPx - trackPx;
    const slack = boundsSpan(bounds) - span;
    const start = travel <= 0 ? bounds.startUs : bounds.startUs + (scrollLeft / travel) * slack;
    return clampView({ startUs: start, endUs: start + span }, bounds);
}

export function zoomAbout(view: ViewRange, anchorUs: number, factor: number): ViewRange {
    const span = view.endUs - view.startUs;
    const share = span === 0 ? 0.5 : (anchorUs - view.startUs) / span;
    const next = span * factor;
    const start = anchorUs - next * share;
    return { startUs: start, endUs: start + next };
}

export function panBy(view: ViewRange, deltaUs: number): ViewRange {
    return { startUs: view.startUs + deltaUs, endUs: view.endUs + deltaUs };
}

// buildFrameTree emits a parent before its children, so a child is always forward of its
// parent and a sibling is the next node at the same depth under the same parent.
function firstChild(tree: TreeNode[], index: number, from: TreeNode): number {
    for (let i = index + 1; i < tree.length; i++) {
        if (tree[i].parentIndex === index) return i;
        if (tree[i].depth <= from.depth) break;
    }
    return index;
}

function nextSibling(tree: TreeNode[], index: number, from: TreeNode, step: number): number {
    for (let i = index + step; i >= 0 && i < tree.length; i += step) {
        if (tree[i].depth === from.depth && tree[i].parentIndex === from.parentIndex) return i;
        if (tree[i].depth < from.depth) break;
    }
    return index;
}

export function moveFocus(tree: TreeNode[], index: number, move: FocusMove): number {
    const from = tree[index];
    if (!from) return index;

    if (move === 'up') return from.parentIndex >= 0 ? from.parentIndex : index;
    if (move === 'down') return firstChild(tree, index, from);
    return nextSibling(tree, index, from, move === 'right' ? 1 : -1);
}
