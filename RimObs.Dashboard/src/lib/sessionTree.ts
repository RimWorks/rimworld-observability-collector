import type { CallNode } from './api';
import type { TreeNode } from './frameTree';

const NO_PARENT = -1;
const NS_PER_US = 1000;

/**
 * Flattens the collector's nested session call tree into the same flat array the frame tree
 * uses, so one table renders both scopes. Session nodes are aggregates, so the timeline
 * positions are laid out end to end rather than measured.
 */
export function flattenCallNodes(roots: readonly CallNode[]): TreeNode[] {
    const out: TreeNode[] = [];

    const walk = (node: CallNode, parentIndex: number, depth: number, startUs: number): number => {
        const durUs = node.total_ns / NS_PER_US;
        const index = out.length;
        out.push({
            sectionId: node.id,
            nodeId: index,
            parentIndex,
            depth,
            startUs,
            durUs,
            endUs: startUs + durUs,
            allocBytes: node.alloc_bytes ?? 0,
            calls: node.call_count,
        });

        let childStartUs = startUs;
        for (const child of node.children ?? []) {
            childStartUs = walk(child, index, depth + 1, childStartUs);
        }
        return startUs + durUs;
    };

    let cursorUs = 0;
    for (const root of roots) cursorUs = walk(root, NO_PARENT, 0, cursorUs);
    return out;
}

/** Wall time the whole session tree covers, for the share column. */
export function sessionTotalUs(roots: readonly CallNode[]): number {
    let total = 0;
    for (const root of roots) total += root.total_ns / NS_PER_US;
    return total;
}
