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
    p75_us: number;
    p90_us: number;
    p99_us: number;
    min_us: number;
    max_us: number;
}

export interface FrameStripData {
    ordinals: number[];
    durations_us: number[];
}

export interface FrameResponse {
    schema_version: number;
    stopwatch_frequency: number;
    frame: FrameData | null;
    strip?: FrameStripData;
    stats: FrameStats;
    dropped: { pre_frame_samples: number; late_samples: number };
}

export interface BundleFramesResponse {
    schema_version: number;
    session_id: string;
    stopwatch_frequency: number;
    frames: FrameData[];
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
    /** aggregated nodes stand in for many calls; a live frame node is one call. */
    calls?: number;
}

export const NO_PARENT = -1;

export interface FrameTree {
    nodes: TreeNode[];
    orphanCount: number;
}

// total order: start ascending, then duration descending, then wire index ascending.
// transitive, so an exact-interval ancestry chain sorts the same way every time.
function sortByInterval(n: number, relStart: number[], dur_us: number[]): number[] {
    const order: number[] = [];
    for (let i = 0; i < n; i++) order.push(i);
    order.sort((a, b) => {
        if (relStart[a] !== relStart[b]) return relStart[a] - relStart[b];
        if (dur_us[a] !== dur_us[b]) return dur_us[b] - dur_us[a];
        return a - b;
    });
    return order;
}

// an unresolved parent id falls back to the innermost still-open container, walked in
// sorted order so an orphan's fallback reflects real containment, not wire arrival order.
function resolveParents(
    order: number[],
    parent_node_ids: number[],
    node_ids: number[],
    relStart: number[],
    relEnd: number[],
): { parentWire: number[]; orphanCount: number } {
    // order holds exactly the n valid wire indices; node_ids can run longer when a shorter
    // sibling array (e.g. dur_us) truncated the frame, and an id past n must stay unresolved.
    const n = order.length;
    const idToWire = new Map<number, number>();
    for (let i = 0; i < n; i++) idToWire.set(node_ids[i], i);

    const parentWire = new Array<number>(n).fill(NO_PARENT);
    const openStack: number[] = [];
    let orphanCount = 0;

    for (const i of order) {
        while (openStack.length > 0 && relEnd[openStack.at(-1)!] <= relStart[i]) {
            openStack.pop();
        }
        const parentNodeId = parent_node_ids[i];
        if (parentNodeId !== NO_PARENT) {
            const wire = idToWire.get(parentNodeId);
            if (wire === undefined) {
                orphanCount++;
                parentWire[i] = openStack.length > 0 ? openStack.at(-1)! : NO_PARENT;
            } else {
                parentWire[i] = wire;
            }
        }
        openStack.push(i);
    }

    return { parentWire, orphanCount };
}

// depth walks the parent chain directly; it must not assume the parent was emitted yet.
function computeDepths(parentWire: number[]): number[] {
    const depth = new Array<number>(parentWire.length).fill(-1);
    const depthOf = (i: number): number => {
        if (depth[i] === -1) {
            const p = parentWire[i];
            depth[i] = p === NO_PARENT ? 0 : depthOf(p) + 1;
        }
        return depth[i];
    };
    for (let i = 0; i < parentWire.length; i++) depthOf(i);
    return depth;
}

// emit in sorted order, pulling an unemitted parent forward first. only an exact
// interval tie needs this; real containment already sorts a parent before its child.
// the wire format is parallel arrays, so they travel together rather than as eight arguments
interface DrawColumns {
    order: number[];
    parentWire: number[];
    depth: number[];
    section_ids: number[];
    node_ids: number[];
    relStart: number[];
    relEnd: number[];
    dur_us: number[];
}

function emitInDrawOrder(cols: DrawColumns): TreeNode[] {
    const { order, parentWire, depth, section_ids, node_ids, relStart, relEnd, dur_us } = cols;
    const nodes: TreeNode[] = [];
    const wireToOutput = new Array<number>(parentWire.length).fill(-1);

    const emit = (i: number): number => {
        if (wireToOutput[i] === -1) {
            const p = parentWire[i];
            const parentIndex = p === NO_PARENT ? NO_PARENT : emit(p);
            wireToOutput[i] = nodes.length;
            nodes.push({
                sectionId: section_ids[i],
                nodeId: node_ids[i],
                parentIndex,
                depth: depth[i],
                startUs: relStart[i],
                durUs: dur_us[i],
                endUs: relEnd[i],
            });
        }
        return wireToOutput[i];
    };

    for (const i of order) emit(i);
    return nodes;
}

// parent_node_ids address nodes exactly; parent_ids hold SECTION ids and cannot, because
// DoSingleTick emits three TickList.Tick nodes per tick with one parent id between them.
export function buildFrameTree(frame: FrameData): FrameTree {
    // an imported bundle is a user-supplied zip, so nodes can be missing entirely.
    if (!frame.nodes) return { nodes: [], orphanCount: 0 };
    const { section_ids, node_ids, parent_node_ids, start_us, dur_us } = frame.nodes;
    const n = Math.min(
        section_ids.length,
        node_ids.length,
        parent_node_ids.length,
        start_us.length,
        dur_us.length,
    );
    if (n === 0) return { nodes: [], orphanCount: 0 };

    const origin = frame.start_us;
    const relStart = new Array<number>(n);
    const relEnd = new Array<number>(n);
    for (let i = 0; i < n; i++) {
        relStart[i] = start_us[i] - origin;
        relEnd[i] = relStart[i] + dur_us[i];
    }

    const order = sortByInterval(n, relStart, dur_us);
    const { parentWire, orphanCount } = resolveParents(
        order,
        parent_node_ids,
        node_ids,
        relStart,
        relEnd,
    );
    const depth = computeDepths(parentWire);
    const nodes = emitInDrawOrder({
        order,
        parentWire,
        depth,
        section_ids,
        node_ids,
        relStart,
        relEnd,
        dur_us,
    });

    return { nodes, orphanCount };
}
