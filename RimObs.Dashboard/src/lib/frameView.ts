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

export function fitView(frameDurationUs: number): ViewRange {
    return { startUs: 0, endUs: Math.max(frameDurationUs, MIN_SPAN_US) };
}

export function clampView(view: ViewRange, frameDurationUs: number): ViewRange {
    const frame = Math.max(frameDurationUs, MIN_SPAN_US);
    const span = Math.min(Math.max(view.endUs - view.startUs, MIN_SPAN_US), frame);
    const start = Math.min(Math.max(view.startUs, 0), frame - span);
    return { startUs: start, endUs: start + span };
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

export function hitTest(tree: TreeNode[], depth: number, atUs: number): number {
    for (let i = 0; i < tree.length; i++) {
        const n = tree[i];
        if (n.depth === depth && atUs >= n.startUs && atUs < n.endUs) return i;
    }
    return -1;
}

export function moveFocus(tree: TreeNode[], index: number, move: FocusMove): number {
    const from = tree[index];
    if (!from) return index;

    if (move === 'up') return from.parentIndex >= 0 ? from.parentIndex : index;

    if (move === 'down') {
        for (let i = index + 1; i < tree.length; i++) {
            if (tree[i].parentIndex === index) return i;
            if (tree[i].depth <= from.depth) break;
        }
        return index;
    }

    const step = move === 'right' ? 1 : -1;
    for (let i = index + step; i >= 0 && i < tree.length; i += step) {
        if (tree[i].depth === from.depth && tree[i].parentIndex === from.parentIndex) return i;
        if (tree[i].depth < from.depth) break;
    }
    return index;
}
