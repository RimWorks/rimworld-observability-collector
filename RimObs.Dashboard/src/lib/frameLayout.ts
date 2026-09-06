import type { TreeNode } from './frameTree';

export interface LayoutOptions {
    viewStartUs: number;
    viewEndUs: number;
    widthPx: number;
    maxDepth: number;
    minWidthPx: number;
    minVisibleDurationUs: number;
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
export function foldFrame(tree: TreeNode[], minVisibleDurationUs: number): Uint8Array {
    const folded = new Uint8Array(tree.length);
    // buildFrameTree emits a parent before its children, so one forward pass carries the flag down.
    for (let i = 0; i < tree.length; i++) {
        const n = tree[i];
        const parentFolded = n.parentIndex >= 0 && folded[n.parentIndex] === 1;
        folded[i] = parentFolded || n.durUs < minVisibleDurationUs ? 1 : 0;
    }
    return folded;
}

export function layoutFrame(tree: TreeNode[], opts: LayoutOptions): Quad[] {
    const span = opts.viewEndUs - opts.viewStartUs;
    if (span <= 0 || opts.widthPx <= 0) return [];

    const folded = foldFrame(tree, opts.minVisibleDurationUs);
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

    for (let i = 0; i < tree.length; i++) {
        const n = tree[i];
        if (folded[i] === 1) continue;
        if (n.depth >= opts.maxDepth) continue;
        if (n.endUs <= opts.viewStartUs || n.startUs >= opts.viewEndUs) continue;

        if (n.durUs >= minUs) {
            flush(n.depth);
            out.push({
                depth: n.depth,
                startUs: n.startUs,
                endUs: n.endUs,
                totalUs: n.durUs,
                sectionId: n.sectionId,
                count: 1,
                firstIndex: i,
            });
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
        open.set(n.depth, {
            depth: n.depth,
            startUs: n.startUs,
            endUs: n.endUs,
            totalUs: n.durUs,
            sectionId: n.sectionId,
            count: 1,
            firstIndex: i,
        });
    }

    for (const run of open.values()) out.push(run);
    out.sort((a, b) => a.depth - b.depth || a.startUs - b.startUs);
    return out;
}
