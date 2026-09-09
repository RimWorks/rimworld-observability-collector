import type { TreeNode } from './frameTree';

export interface LayoutOptions {
    viewStartUs: number;
    viewEndUs: number;
    widthPx: number;
    maxDepth: number;
    minWidthPx: number;
    minVisibleDurationUs: number;
    /** half-open node index bounds; defaults to the whole tree. */
    from?: number;
    to?: number;
}

export interface Quad {
    depth: number;
    startUs: number;
    endUs: number;
    totalUs: number;
    sectionId: number;
    count: number;
    firstIndex: number;
}

// a node under the threshold goes, and so does everything beneath it.
export function foldFrame(
    tree: TreeNode[],
    minVisibleDurationUs: number,
    from = 0,
    to = tree.length,
): Uint8Array {
    const folded = new Uint8Array(tree.length);
    // buildFrameTree emits a parent before its children, so one forward pass carries the flag down.
    for (let i = from; i < to; i++) {
        const n = tree[i];
        const parentFolded = n.parentIndex >= 0 && folded[n.parentIndex] === 1;
        folded[i] = parentFolded || n.durUs < minVisibleDurationUs ? 1 : 0;
    }
    return folded;
}

// pulled out of layoutFrame so the hot loop reads as three cases, not eight branches
function isHidden(n: TreeNode, i: number, folded: Uint8Array, opts: LayoutOptions): boolean {
    if (folded[i] === 1) return true;
    if (n.depth >= opts.maxDepth) return true;
    return n.endUs <= opts.viewStartUs || n.startUs >= opts.viewEndUs;
}

function quadOf(n: TreeNode, i: number): Quad {
    return {
        depth: n.depth,
        startUs: n.startUs,
        endUs: n.endUs,
        totalUs: n.durUs,
        sectionId: n.sectionId,
        count: 1,
        firstIndex: i,
    };
}

export function layoutFrame(tree: TreeNode[], opts: LayoutOptions): Quad[] {
    const span = opts.viewEndUs - opts.viewStartUs;
    if (span <= 0 || opts.widthPx <= 0) return [];

    const from = opts.from ?? 0;
    const to = opts.to ?? tree.length;
    const folded = foldFrame(tree, opts.minVisibleDurationUs, from, to);
    const minUs = (span / opts.widthPx) * opts.minWidthPx;
    const out: Quad[] = [];
    const open = new Map<number, Quad>();

    const flush = (depth: number): void => {
        const run = open.get(depth);
        if (run) {
            out.push(run);
            open.delete(depth);
        }
    };

    for (let i = from; i < to; i++) {
        const n = tree[i];
        if (isHidden(n, i, folded, opts)) continue;

        if (n.durUs >= minUs) {
            flush(n.depth);
            out.push(quadOf(n, i));
            continue;
        }

        const run = open.get(n.depth);
        if (run && n.startUs - run.endUs <= minUs) {
            run.endUs = Math.max(run.endUs, n.endUs);
            run.totalUs += n.durUs;
            run.count += 1;
            if (run.sectionId !== n.sectionId) run.sectionId = -1;
            continue;
        }

        flush(n.depth);
        open.set(n.depth, quadOf(n, i));
    }

    for (const run of open.values()) out.push(run);
    out.sort((a, b) => a.depth - b.depth || a.startUs - b.startUs);
    return out;
}

// depth plus time containment, not a lookup: a run's tree indices are not contiguous.
// the LAST candidate, not the first, stops a node on a run boundary hitting the prior run.
export function quadIndexForNode(quads: Quad[], node: TreeNode): number {
    let candidate = -1;
    for (let i = 0; i < quads.length; i++) {
        const q = quads[i];
        if (q.depth === node.depth && q.startUs <= node.startUs) candidate = i;
    }
    if (candidate === -1) return -1;
    return node.startUs < quads[candidate].endUs ? candidate : -1;
}
