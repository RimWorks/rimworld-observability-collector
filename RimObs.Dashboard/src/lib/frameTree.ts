export interface FrameNodes {
    section_ids: number[];
    parent_ids: number[];
    node_ids: number[];
    parent_node_ids: number[];
    start_us: number[];
    dur_us: number[];
}

export interface FrameData {
    capture_ordinal: number;
    start_us: number;
    end_us: number;
    duration_us: number;
    node_count: number;
    nodes: FrameNodes;
}

export interface FrameStats {
    frame_count: number;
    newest_ordinal: number;
    oldest_ordinal: number;
    median_us: number;
    p99_us: number;
    min_us: number;
    max_us: number;
}

export interface FrameResponse {
    schema_version: number;
    frame: FrameData | null;
    stats: FrameStats;
    dropped: { pre_frame_samples: number; late_samples: number };
}

export interface TreeNode {
    sectionId: number;
    nodeId: number;
    parentIndex: number;
    depth: number;
    startUs: number;
    durUs: number;
    endUs: number;
}

export const NO_PARENT = -1;

export interface FrameTree {
    nodes: TreeNode[];
    orphanCount: number;
}

// parent_node_ids address nodes exactly; parent_ids hold SECTION ids and cannot, because
// DoSingleTick emits three TickList.Tick nodes per tick with one parent id between them.
export function buildFrameTree(frame: FrameData): FrameTree {
    const { section_ids, node_ids, parent_node_ids, start_us, dur_us } = frame.nodes;
    const n = Math.min(
        section_ids.length,
        node_ids.length,
        parent_node_ids.length,
        start_us.length,
        dur_us.length,
    );
    if (n === 0) return { nodes: [], orphanCount: 0 };

    const origin = frame.start_us; // node starts are session absolute, the view is frame relative
    const order = new Array<number>(n);
    for (let i = 0; i < n; i++) order[i] = i;

    order.sort((a, b) => {
        if (start_us[a] !== start_us[b]) return start_us[a] - start_us[b];
        if (dur_us[a] !== dur_us[b]) return dur_us[b] - dur_us[a];
        // identical interval: the parent must be drawn, and pushed, before its child
        if (parent_node_ids[a] === node_ids[b]) return 1;
        if (parent_node_ids[b] === node_ids[a]) return -1;
        return a - b;
    });

    const outOf = new Int32Array(n);
    for (let k = 0; k < n; k++) outOf[order[k]] = k;

    const idToWire = new Map<number, number>();
    for (let i = 0; i < n; i++) idToWire.set(node_ids[i], i);

    const nodes: TreeNode[] = [];
    const stack: number[] = [];
    let orphanCount = 0;

    for (let k = 0; k < n; k++) {
        const i = order[k];
        const startUs = start_us[i] - origin;
        const endUs = startUs + dur_us[i];

        while (stack.length > 0 && nodes[stack[stack.length - 1]].endUs <= startUs) {
            stack.pop();
        }

        let parentIndex = NO_PARENT;
        const parentNodeId = parent_node_ids[i];
        if (parentNodeId !== NO_PARENT) {
            const wire = idToWire.get(parentNodeId);
            if (wire === undefined) {
                // the parent's datagram never arrived. re-parent by containment so the frame
                // still draws, and count it so the page can admit the loss.
                orphanCount++;
                parentIndex = stack.length > 0 ? stack[stack.length - 1] : NO_PARENT;
            } else {
                parentIndex = outOf[wire];
            }
        }

        nodes.push({
            sectionId: section_ids[i],
            nodeId: node_ids[i],
            parentIndex,
            depth: parentIndex >= 0 ? nodes[parentIndex].depth + 1 : 0,
            startUs,
            durUs: dur_us[i],
            endUs,
        });
        stack.push(nodes.length - 1);
    }

    return { nodes, orphanCount };
}
