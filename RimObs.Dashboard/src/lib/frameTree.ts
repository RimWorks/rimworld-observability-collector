import type { ThreadLane } from './api';

export interface FrameNodes {
    section_ids: number[];
    parent_ids: number[];
    node_ids: number[];
    parent_node_ids: number[];
    start_us: number[];
    dur_us: number[];
    alloc_bytes?: number[];
    /** empty for a v8 bundle, which predates per-node thread ids. */
    thread_ids?: number[];
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

export interface FrameVitals {
    tps: number;
    fps: number;
    tick: number;
}

export interface FrameResponse {
    schema_version: number;
    stopwatch_frequency: number;
    frame: FrameData | null;
    strip?: FrameStripData;
    stats: FrameStats;
    vitals?: FrameVitals | null;
    threads?: ThreadLane[];
    dropped: { pre_frame_samples: number; late_samples: number; library_ring_samples: number };
}

export interface FrameRangeResponse {
    schema_version: number;
    /** the duration floor the frames were filtered at; 0 or absent means full detail. */
    lod_min_dur_us?: number;
    stopwatch_frequency: number;
    frames: FrameData[];
    strip?: FrameStripData;
    stats: FrameStats;
    dropped: { pre_frame_samples: number; late_samples: number; library_ring_samples: number };
}

export interface FrameSummariesResponse {
    schema_version: number;
    stopwatch_frequency: number;
    frame_count: number;
    ordinals: number[];
    start_us: number[];
    duration_us: number[];
    node_counts: number[];
    /** per requested section id, its summed duration per frame, aligned with ordinals. */
    section_durations: Record<string, number[]>;
    dropped: { pre_frame_samples: number; late_samples: number; library_ring_samples: number };
}

export interface BundleFramesResponse {
    schema_version: number;
    session_id: string;
    stopwatch_frequency: number;
    frames: FrameData[];
    stats: FrameStats;
    dropped: { pre_frame_samples: number; late_samples: number; library_ring_samples: number };
    /** The filter that was running when the bundle was captured. Absent in older bundles. */
    auto_instrument?: { enabled: boolean; filters: string; ignore: string } | null;
}

export interface TreeNode {
    sectionId: number;
    nodeId: number;
    parentIndex: number;
    depth: number;
    startUs: number;
    durUs: number;
    endUs: number;
    /** thread the node ran on. absent when the producer sent no thread ids. */
    laneId?: number;
    /** bytes allocated inside this scope. absent when the runtime has no allocation hook. */
    allocBytes?: number;
    /** aggregated nodes stand in for many calls; a live frame node is one call. */
    calls?: number;
}

export const NO_PARENT = -1;

/** lane id for a node the producer sent no thread id for. it rides the main thread's band. */
export const UNKNOWN_LANE = -1;

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

// an unresolved parent id falls back to the innermost open container on its OWN lane, walked
// in sorted order so the fallback reflects real containment, not wire arrival order.
function resolveParents(
    order: number[],
    parent_node_ids: number[],
    node_ids: number[],
    relStart: number[],
    relEnd: number[],
    lanes: number[],
): { parentWire: number[]; orphanCount: number } {
    // order holds exactly the n valid wire indices; node_ids can run longer when a shorter
    // sibling array (e.g. dur_us) truncated the frame, and an id past n must stay unresolved.
    const n = order.length;
    const idToWire = new Map<number, number>();
    for (let i = 0; i < n; i++) idToWire.set(node_ids[i], i);

    const parentWire = new Array<number>(n).fill(NO_PARENT);
    const openByLane = new Map<number, number[]>();
    let orphanCount = 0;

    for (const i of order) {
        const lane = lanes[i];
        let open = openByLane.get(lane);
        if (!open) openByLane.set(lane, (open = []));
        while (open.length > 0 && relEnd[open.at(-1)!] <= relStart[i]) {
            open.pop();
        }
        const parentNodeId = parent_node_ids[i];
        if (parentNodeId !== NO_PARENT) {
            const wire = idToWire.get(parentNodeId);
            if (wire === undefined) {
                orphanCount++;
                parentWire[i] = open.length > 0 ? open.at(-1)! : NO_PARENT;
            } else if (lanes[wire] === lane) {
                parentWire[i] = wire;
            } else {
                // the thread that queued a job opens its scope, so the wire does point across
                // lanes. that is not a dropped sample, so it does not count as an orphan.
                parentWire[i] = open.length > 0 ? open.at(-1)! : NO_PARENT;
            }
        }
        open.push(i);
    }

    return { parentWire, orphanCount };
}

// depth walks the parent chain iteratively; it must not assume the parent was emitted yet,
// and a forward wire parent whose fallback points back forms a cycle that must sever.
function computeDepths(parentWire: number[]): number[] {
    const depth = new Array<number>(parentWire.length).fill(-1);
    const onChain = new Array<boolean>(parentWire.length).fill(false);
    const chain: number[] = [];
    for (let i = 0; i < parentWire.length; i++) {
        let at = i;
        while (at !== NO_PARENT && depth[at] === -1 && !onChain[at]) {
            onChain[at] = true;
            chain.push(at);
            at = parentWire[at];
        }
        let d: number;
        if (at === NO_PARENT) {
            d = -1;
        } else if (depth[at] !== -1) {
            d = depth[at];
        } else {
            // the chain re-entered itself; root the node whose parent looped back.
            parentWire[chain.at(-1)!] = NO_PARENT;
            d = -1;
        }
        while (chain.length > 0) {
            const n = chain.pop()!;
            onChain[n] = false;
            d += 1;
            depth[n] = d;
        }
    }
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
    alloc_bytes: number[] | undefined;
    lanes: number[] | undefined;
}

function emitInDrawOrder(cols: DrawColumns): TreeNode[] {
    const { order, parentWire, depth, section_ids, node_ids, relStart, relEnd, dur_us } = cols;
    const alloc = cols.alloc_bytes;
    const lanes = cols.lanes;
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
                allocBytes: alloc?.[i] ?? 0,
                laneId: lanes?.[i],
            });
        }
        return wireToOutput[i];
    };

    for (const i of order) emit(i);
    return nodes;
}

interface ResolvedFrame {
    n: number;
    order: number[];
    relStart: number[];
    relEnd: number[];
    parentWire: number[];
    orphanCount: number;
    /** undefined when the producer sent no thread ids, so every node shares one lane. */
    lanes: number[] | undefined;
}

/**
 * Wire-index parent for every node, orphans re-parented onto the innermost open container.
 * Anything reading containment has to go through this or it disagrees with the flame.
 */
export function resolveFrameParents(nodes: FrameNodes, origin = 0): ResolvedFrame {
    const { section_ids, node_ids, parent_node_ids, start_us, dur_us } = nodes;
    const n = Math.min(
        section_ids.length,
        node_ids.length,
        parent_node_ids.length,
        start_us.length,
        dur_us.length,
    );
    const relStart = new Array<number>(n);
    const relEnd = new Array<number>(n);
    for (let i = 0; i < n; i++) {
        relStart[i] = start_us[i] - origin;
        relEnd[i] = relStart[i] + dur_us[i];
    }

    const lanes = laneColumn(nodes.thread_ids, n);
    const order = sortByInterval(n, relStart, dur_us);
    const { parentWire, orphanCount } = resolveParents(
        order,
        parent_node_ids,
        node_ids,
        relStart,
        relEnd,
        lanes ?? new Array<number>(n).fill(UNKNOWN_LANE),
    );
    return { n, order, relStart, relEnd, parentWire, orphanCount, lanes };
}

// a short thread_ids array leaves its tail unknown rather than truncating the frame.
function laneColumn(thread_ids: number[] | undefined, n: number): number[] | undefined {
    if (!thread_ids?.length) return undefined;
    const lanes = new Array<number>(n);
    for (let i = 0; i < n; i++) lanes[i] = thread_ids[i] ?? UNKNOWN_LANE;
    return lanes;
}

// parent_node_ids address nodes exactly; parent_ids hold SECTION ids and cannot, because
// DoSingleTick emits three TickList.Tick nodes per tick with one parent id between them.
export function buildFrameTree(frame: FrameData): FrameTree {
    // an imported bundle is a user-supplied zip, so nodes can be missing entirely.
    if (!frame.nodes) return { nodes: [], orphanCount: 0 };
    const { section_ids, node_ids, dur_us, alloc_bytes } = frame.nodes;
    const { n, order, relStart, relEnd, parentWire, orphanCount, lanes } = resolveFrameParents(
        frame.nodes,
        frame.start_us,
    );
    if (n === 0) return { nodes: [], orphanCount: 0 };

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
        alloc_bytes,
        lanes,
    });

    return { nodes, orphanCount };
}
