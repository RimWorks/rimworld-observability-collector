import type { TreeNode } from './frameTree';
import { laneRow, type LaneBands } from './threadLanes';

// deeper than the stage is tall on purpose; .stage takes over with a native scrollbar.
export const MAX_DEPTH = 128;

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
    /** 1 = force-hidden regardless of duration, e.g. the search filter. */
    hidden?: Uint8Array;
    /** absent means one band: every node draws at its own depth. */
    bands?: LaneBands;
}

export interface Quad {
    /** canvas row, which is the node's depth plus its lane's band offset. */
    depth: number;
    startUs: number;
    endUs: number;
    totalUs: number;
    sectionId: number;
    count: number;
    firstIndex: number;
    /** depth inside the lane, for shading. defaults to `depth`. */
    nest?: number;
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
    if (opts.hidden?.[i] === 1) return true;
    if (n.depth >= opts.maxDepth) return true;
    return n.endUs <= opts.viewStartUs || n.startUs >= opts.viewEndUs;
}

function quadOf(n: TreeNode, i: number, row: number): Quad {
    return {
        depth: row,
        startUs: n.startUs,
        endUs: n.endUs,
        totalUs: n.durUs,
        sectionId: n.sectionId,
        count: 1,
        firstIndex: i,
        nest: n.depth,
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
        const row = laneRow(opts.bands, n);
        // a row below zero means the node's lane is filtered out of the canvas entirely.
        if (row < 0 || isHidden(n, i, folded, opts)) continue;

        if (n.durUs >= minUs) {
            flush(row);
            out.push(quadOf(n, i, row));
            continue;
        }

        const run = open.get(row);
        if (run && n.startUs - run.endUs <= minUs) {
            run.endUs = Math.max(run.endUs, n.endUs);
            run.totalUs += n.durUs;
            run.count += 1;
            if (run.sectionId !== n.sectionId) run.sectionId = -1;
            continue;
        }

        flush(row);
        open.set(row, quadOf(n, i, row));
    }

    for (const run of open.values()) out.push(run);
    out.sort((a, b) => a.depth - b.depth || a.startUs - b.startUs);
    return out;
}

// depth plus time containment, not a lookup: a run's tree indices are not contiguous.
// the LAST candidate, not the first, stops a node on a run boundary hitting the prior run.
export function quadIndexForNode(quads: Quad[], node: TreeNode, row = node.depth): number {
    let candidate = -1;
    for (let i = 0; i < quads.length; i++) {
        const q = quads[i];
        if (q.depth === row && q.startUs <= node.startUs) candidate = i;
    }
    if (candidate === -1) return -1;
    return node.startUs < quads[candidate].endUs ? candidate : -1;
}
