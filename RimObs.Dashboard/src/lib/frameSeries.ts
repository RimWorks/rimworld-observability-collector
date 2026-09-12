import { buildFrameTree, UNKNOWN_LANE, type FrameData, type TreeNode } from './frameTree';
import { layoutFrame, type LayoutOptions, type Quad } from './frameLayout';
import { laneRow, type LaneBands, type WindowLaneStats } from './threadLanes';

/**
 * Per-frame rollups, built once with the tree and folded over the window instead of
 * rescanning every node on every apply.
 */
export interface FrameAggregates {
    /** deepest node in the frame, uncapped. */
    maxDepth: number;
    /** lane to its deepest node. a node with no thread id keys on UNKNOWN_LANE. */
    laneDepth: Map<number, number>;
    /** lane to its call count and outermost-scope busy time. same keying. */
    laneStats: Map<number, WindowLaneStats>;
}

export function aggregateNodes(nodes: TreeNode[]): FrameAggregates {
    const laneDepth = new Map<number, number>();
    const laneStats = new Map<number, WindowLaneStats>();
    let maxDepth = 0;
    for (let i = 0; i < nodes.length; i++) {
        const n = nodes[i];
        const lane = n.laneId ?? UNKNOWN_LANE;
        if (n.depth > maxDepth) maxDepth = n.depth;
        if (n.depth > (laneDepth.get(lane) ?? 0)) laneDepth.set(lane, n.depth);
        let s = laneStats.get(lane);
        if (s === undefined) laneStats.set(lane, (s = { calls: 0, busyNs: 0 }));
        s.calls++;
        if (n.parentIndex < 0) s.busyNs += (n.endUs - n.startUs) * 1000;
    }
    return { maxDepth, laneDepth, laneStats };
}

export interface FrameEntry {
    ordinal: number;
    startUs: number;
    endUs: number;
    durationUs: number;
    /** half-open slice of FrameSeries.nodes belonging to this frame. */
    nodeStart: number;
    nodeEnd: number;
    orphanCount: number;
    agg: FrameAggregates;
}

export interface FrameGap {
    startUs: number;
    endUs: number;
    /** frames the ordinals say are missing. 0 means the game was just idle here. */
    missing: number;
}

export interface FrameSeries {
    nodes: TreeNode[];
    entries: FrameEntry[];
    gaps: FrameGap[];
    startUs: number;
    endUs: number;
    orphanCount: number;
}

export interface SeriesCacheEntry {
    /** already shifted to absolute time; parentIndex is rewritten in place per rebuild. */
    nodes: TreeNode[];
    /** frame-local parent index per node, the source the in-place rewrite reads from. */
    parentLocal: Int32Array;
    orphanCount: number;
    /** revalidation key: a backfilled frame returns the same ordinal with more nodes. */
    nodeCount: number;
    agg: FrameAggregates;
    /** base the parentIndex values already carry. an unmoved frame skips the rewrite. */
    appliedBase: number;
}

export const EMPTY_SERIES: FrameSeries = {
    nodes: [],
    entries: [],
    gaps: [],
    startUs: 0,
    endUs: 0,
    orphanCount: 0,
};

// coordinates are absolute session microseconds, not series-relative, so a cached frame
// stays valid when the window slides past it. built once per frame, ever.
function shifted(nodes: TreeNode[], offsetUs: number): SeriesCacheEntry['nodes'] {
    const out = new Array<TreeNode>(nodes.length);
    for (let i = 0; i < nodes.length; i++) {
        const n = nodes[i];
        out[i] = { ...n, startUs: n.startUs + offsetUs, endUs: n.endUs + offsetUs };
    }
    return out;
}

function treeFor(frame: FrameData, cache?: Map<number, SeriesCacheEntry>): SeriesCacheEntry {
    const hit = cache?.get(frame.capture_ordinal);
    if (hit && hit.nodeCount === frame.node_count) return hit;
    const built = buildFrameTree(frame);
    const parentLocal = new Int32Array(built.nodes.length);
    for (let i = 0; i < built.nodes.length; i++) parentLocal[i] = built.nodes[i].parentIndex;
    const entry: SeriesCacheEntry = {
        nodes: shifted(built.nodes, frame.start_us),
        parentLocal,
        orphanCount: built.orphanCount,
        nodeCount: frame.node_count,
        agg: aggregateNodes(built.nodes),
        appliedBase: -1,
    };
    cache?.set(frame.capture_ordinal, entry);
    return entry;
}

export function buildSeries(
    frames: FrameData[],
    cache?: Map<number, SeriesCacheEntry>,
): FrameSeries {
    const usable = frames.filter((f) => f && f.node_count > 0);
    if (usable.length === 0) return EMPTY_SERIES;
    const ordered = [...usable].sort((a, b) => a.capture_ordinal - b.capture_ordinal);

    const builts = new Array<SeriesCacheEntry>(ordered.length);
    let total = 0;
    for (let f = 0; f < ordered.length; f++) {
        builts[f] = treeFor(ordered[f], cache);
        total += builts[f].nodes.length;
    }

    const nodes = new Array<TreeNode>(total);
    const entries: FrameEntry[] = [];
    const gaps: FrameGap[] = [];
    let orphanCount = 0;
    let at = 0;

    for (let f = 0; f < ordered.length; f++) {
        const frame = ordered[f];
        const built = builts[f];
        const nodeStart = at;
        // the cached objects are copied by reference, and parentIndex is only rewritten when
        // this frame actually moved in the window. an append-only poll rewrites nothing.
        const rebase = built.appliedBase !== nodeStart;
        for (let i = 0; i < built.nodes.length; i++) {
            const node = built.nodes[i];
            if (rebase) {
                const local = built.parentLocal[i];
                node.parentIndex = local < 0 ? local : local + nodeStart;
            }
            nodes[at++] = node;
        }
        built.appliedBase = nodeStart;
        orphanCount += built.orphanCount;

        const previous = entries.at(-1);
        entries.push({
            ordinal: frame.capture_ordinal,
            startUs: frame.start_us,
            endUs: frame.end_us,
            durationUs: frame.duration_us,
            nodeStart,
            nodeEnd: at,
            orphanCount: built.orphanCount,
            agg: built.agg,
        });
        if (previous && frame.start_us > previous.endUs) {
            gaps.push({
                startUs: previous.endUs,
                endUs: frame.start_us,
                missing: Math.max(0, frame.capture_ordinal - previous.ordinal - 1),
            });
        }
    }

    return {
        nodes,
        entries,
        gaps,
        startUs: entries[0].startUs,
        endUs: entries.at(-1)!.endUs,
        orphanCount,
    };
}

/** index of the frame containing atUs, or -1 when it lands in a gap or outside the series. */
export function entryIndexAt(entries: FrameEntry[], atUs: number): number {
    let lo = 0;
    let hi = entries.length - 1;
    while (lo <= hi) {
        const mid = lo + ((hi - lo) >> 1);
        const e = entries[mid];
        if (atUs < e.startUs) hi = mid - 1;
        else if (atUs >= e.endUs) lo = mid + 1;
        else return mid;
    }
    return -1;
}

/** entry index range that overlaps the view, inclusive. lo > hi means nothing is visible. */
export function visibleEntries(
    entries: FrameEntry[],
    viewStartUs: number,
    viewEndUs: number,
): { lo: number; hi: number } {
    let lo = 0;
    let hi = entries.length - 1;
    let first = entries.length;
    while (lo <= hi) {
        const mid = lo + ((hi - lo) >> 1);
        if (entries[mid].endUs > viewStartUs) {
            first = mid;
            hi = mid - 1;
        } else lo = mid + 1;
    }
    lo = 0;
    hi = entries.length - 1;
    let last = -1;
    while (lo <= hi) {
        const mid = lo + ((hi - lo) >> 1);
        if (entries[mid].startUs < viewEndUs) {
            last = mid;
            lo = mid + 1;
        } else hi = mid - 1;
    }
    return { lo: first, hi: last };
}

/** entry owning a node index, by binary search on the slice bounds. */
export function entryOfNode(series: FrameSeries, nodeIndex: number): FrameEntry | null {
    const { entries } = series;
    let lo = 0;
    let hi = entries.length - 1;
    while (lo <= hi) {
        const mid = lo + ((hi - lo) >> 1);
        const e = entries[mid];
        if (nodeIndex < e.nodeStart) hi = mid - 1;
        else if (nodeIndex >= e.nodeEnd) lo = mid + 1;
        else return e;
    }
    return null;
}

/** deepest node across the window, folded from the per-frame maxima. cap is exclusive. */
export function seriesMaxDepth(series: FrameSeries, cap = Infinity): number {
    let max = 0;
    for (const e of series.entries) if (e.agg.maxDepth > max) max = e.agg.maxDepth;
    return Math.min(max, cap - 1);
}

/**
 * The frame's own tree back out of the series cache, with frame-local times and parent
 * indices. Falls back to a real build when the frame is not in the window.
 */
export function frameTreeNodes(
    frame: FrameData,
    cache?: Map<number, SeriesCacheEntry>,
): TreeNode[] {
    const hit = cache?.get(frame.capture_ordinal);
    if (!hit || hit.nodeCount !== frame.node_count) return buildFrameTree(frame).nodes;
    const out = new Array<TreeNode>(hit.nodes.length);
    for (let i = 0; i < hit.nodes.length; i++) {
        const n = hit.nodes[i];
        out[i] = {
            ...n,
            parentIndex: hit.parentLocal[i],
            startUs: n.startUs - frame.start_us,
            endUs: n.endUs - frame.start_us,
        };
    }
    return out;
}

export function hitTestSeries(
    series: FrameSeries,
    row: number,
    atUs: number,
    bands?: LaneBands,
): number {
    const ei = entryIndexAt(series.entries, atUs);
    if (ei < 0) return -1;
    const e = series.entries[ei];
    for (let i = e.nodeStart; i < e.nodeEnd; i++) {
        const n = series.nodes[i];
        if (laneRow(bands, n) === row && atUs >= n.startUs && atUs < n.endUs) return i;
    }
    return -1;
}

// dur_us of 0 is routine at microsecond resolution, and a half-open hit test never matches
// one, so a focus parked on such a node falls back to an exact start match.
export function resolveFocusIndex(
    series: FrameSeries,
    row: number,
    atUs: number,
    bands?: LaneBands,
): number {
    const hit = hitTestSeries(series, row, atUs, bands);
    if (hit >= 0) return hit;
    const ei = entryIndexAt(series.entries, atUs);
    const from = ei < 0 ? 0 : series.entries[ei].nodeStart;
    const to = ei < 0 ? series.nodes.length : series.entries[ei].nodeEnd;
    for (let i = from; i < to; i++) {
        const n = series.nodes[i];
        if (laneRow(bands, n) === row && n.startUs === atUs) return i;
    }
    return -1;
}

/** lays out only the frames the view touches, so an off-screen frame costs one comparison. */
export function layoutSeries(series: FrameSeries, opts: LayoutOptions): Quad[] {
    const { lo, hi } = visibleEntries(series.entries, opts.viewStartUs, opts.viewEndUs);
    if (lo > hi) return [];
    return layoutFrame(series.nodes, {
        ...opts,
        from: series.entries[lo].nodeStart,
        to: series.entries[hi].nodeEnd,
    });
}

/** gaps overlapping the view, for the hatch that marks missing frames. */
export function visibleGaps(gaps: FrameGap[], viewStartUs: number, viewEndUs: number): FrameGap[] {
    return gaps.filter((g) => g.endUs > viewStartUs && g.startUs < viewEndUs);
}

export const MAX_WINDOW_FRAMES = 64;
export const MAX_WINDOW_NODES = 60_000;

// same array back when nothing changed, so a poll that saw the same frame does not
// invalidate every derived value downstream.
export function pushFrame(
    window: FrameData[],
    frame: FrameData | null,
    maxFrames = MAX_WINDOW_FRAMES,
    maxNodes = MAX_WINDOW_NODES,
): FrameData[] {
    if (!frame) return window;
    const last = window.at(-1);
    if (
        last &&
        frame.capture_ordinal === last.capture_ordinal &&
        frame.node_count > last.node_count
    ) {
        // a frame served mid-hitch came back fuller; take the fuller version so a stall
        // never freezes a truncated flame in the window.
        return [...window.slice(0, -1), frame];
    }
    if (last && frame.capture_ordinal <= last.capture_ordinal) return window;

    const next = [...window, frame];
    let nodes = 0;
    for (const f of next) nodes += f.node_count;
    let drop = Math.max(0, next.length - maxFrames);
    for (let i = 0; i < drop; i++) nodes -= next[i].node_count;
    while (drop < next.length - 1 && nodes > maxNodes) nodes -= next[drop++].node_count;
    return drop === 0 ? next : next.slice(drop);
}
