import { describe, it, expect } from 'vitest';
import {
    buildSeries,
    entryIndexAt,
    entryOfNode,
    hitTestSeries,
    layoutSeries,
    pushFrame,
    resolveFocusIndex,
    visibleEntries,
    visibleGaps,
    EMPTY_SERIES,
    type SeriesCacheEntry,
} from './frameSeries';
import type { FrameData } from './frameTree';

// one root [start, start+100) with a child [start+10, start+30).
function frame(ordinal: number, startUs: number, durationUs = 100): FrameData {
    return {
        capture_ordinal: ordinal,
        start_us: startUs,
        end_us: startUs + durationUs,
        duration_us: durationUs,
        node_count: 2,
        nodes: {
            section_ids: [10, 20],
            parent_ids: [-1, 10],
            node_ids: [1, 2],
            parent_node_ids: [-1, 1],
            start_us: [startUs, startUs + 10],
            dur_us: [durationUs, 20],
        },
    };
}

describe('buildSeries', () => {
    it('places every frame on one absolute time axis', () => {
        const s = buildSeries([frame(1, 1000), frame(2, 1500)]);
        expect(s.entries.map((e) => e.ordinal)).toEqual([1, 2]);
        expect(s.startUs).toBe(1000);
        expect(s.endUs).toBe(1600);
        expect(s.nodes[0].startUs).toBe(1000);
        expect(s.nodes[2].startUs).toBe(1500);
    });

    it('rebases parent indices onto the concatenated node array', () => {
        const s = buildSeries([frame(1, 1000), frame(2, 1500)]);
        expect(s.nodes[1].parentIndex).toBe(0);
        expect(s.nodes[3].parentIndex).toBe(2);
    });

    it('gives each frame a half-open slice of the node array', () => {
        const s = buildSeries([frame(1, 1000), frame(2, 1500)]);
        expect(s.entries[0]).toMatchObject({ nodeStart: 0, nodeEnd: 2 });
        expect(s.entries[1]).toMatchObject({ nodeStart: 2, nodeEnd: 4 });
    });

    it('orders frames by ordinal even when handed them out of order', () => {
        const s = buildSeries([frame(2, 1500), frame(1, 1000)]);
        expect(s.entries.map((e) => e.ordinal)).toEqual([1, 2]);
    });

    it('sums orphan counts across the window', () => {
        const bad = frame(1, 1000);
        bad.nodes.parent_node_ids = [99, 1];
        expect(buildSeries([bad, frame(2, 1500)]).orphanCount).toBe(1);
    });

    it('is empty when every frame is', () => {
        expect(buildSeries([])).toBe(EMPTY_SERIES);
    });

    it('builds a frame once no matter how many windows it appears in', () => {
        const cache = new Map<number, SeriesCacheEntry>();
        buildSeries([frame(1, 1000)], cache);
        const first = cache.get(1);
        buildSeries([frame(1, 1000), frame(2, 1500)], cache);
        expect(cache.get(1)).toBe(first);
    });

    // a backfilled frame keeps its ordinal, so the cache must key on node count too or the
    // fuller frame draws with the stale truncated tree.
    it('rebuilds a cached frame when it returns with more nodes', () => {
        const cache = new Map<number, SeriesCacheEntry>();
        const partial = frame(1, 1000);
        partial.node_count = 1;
        partial.nodes = {
            section_ids: [10],
            parent_ids: [-1],
            node_ids: [1],
            parent_node_ids: [-1],
            start_us: [1000],
            dur_us: [100],
        };
        buildSeries([partial], cache);
        expect(cache.get(1)!.nodes).toHaveLength(1);

        const s = buildSeries([frame(1, 1000)], cache);
        expect(s.nodes).toHaveLength(2);
        expect(cache.get(1)!.nodes).toHaveLength(2);
    });
});

describe('gaps', () => {
    it('records idle time between two consecutive frames as a zero-missing gap', () => {
        const s = buildSeries([frame(1, 1000), frame(2, 1500)]);
        expect(s.gaps).toEqual([{ startUs: 1100, endUs: 1500, missing: 0 }]);
    });

    it('counts the frames the ordinals say were dropped', () => {
        const s = buildSeries([frame(1, 1000), frame(5, 1500)]);
        expect(s.gaps[0].missing).toBe(3);
    });

    it('records nothing when two frames abut', () => {
        expect(buildSeries([frame(1, 1000), frame(2, 1100)]).gaps).toEqual([]);
    });

    it('returns only the gaps the view touches', () => {
        const s = buildSeries([frame(1, 1000), frame(5, 1500), frame(9, 2000)]);
        expect(visibleGaps(s.gaps, 1000, 1200)).toHaveLength(1);
        expect(visibleGaps(s.gaps, 1610, 1990)).toHaveLength(1);
        expect(visibleGaps(s.gaps, 1000, 1050)).toHaveLength(0);
    });
});

describe('entryIndexAt', () => {
    const s = buildSeries([frame(1, 1000), frame(2, 1500), frame(3, 2000)]);

    it('binary searches to the frame holding the time', () => {
        expect(entryIndexAt(s.entries, 1050)).toBe(0);
        expect(entryIndexAt(s.entries, 1550)).toBe(1);
        expect(entryIndexAt(s.entries, 2099)).toBe(2);
    });

    it('returns -1 inside a gap', () => {
        expect(entryIndexAt(s.entries, 1300)).toBe(-1);
    });

    it('returns -1 outside the window on either side', () => {
        expect(entryIndexAt(s.entries, 900)).toBe(-1);
        expect(entryIndexAt(s.entries, 9000)).toBe(-1);
    });

    it('treats a frame span as half open', () => {
        expect(entryIndexAt(s.entries, 1000)).toBe(0);
        expect(entryIndexAt(s.entries, 1100)).toBe(-1);
    });
});

describe('visibleEntries', () => {
    const s = buildSeries([frame(1, 1000), frame(2, 1500), frame(3, 2000)]);

    it('returns every frame the view overlaps', () => {
        expect(visibleEntries(s.entries, 1000, 2100)).toEqual({ lo: 0, hi: 2 });
    });

    it('excludes frames entirely before or after the view', () => {
        expect(visibleEntries(s.entries, 1450, 1700)).toEqual({ lo: 1, hi: 1 });
    });

    it('reports nothing visible with lo past hi', () => {
        const out = visibleEntries(s.entries, 1200, 1300);
        expect(out.lo).toBeGreaterThan(out.hi);
    });
});

describe('hitTestSeries', () => {
    const s = buildSeries([frame(1, 1000), frame(2, 1500)]);

    it('finds a node in the second frame by absolute time', () => {
        expect(hitTestSeries(s, 1, 1515)).toBe(3);
    });

    it('finds a node in the first frame at the same offset', () => {
        expect(hitTestSeries(s, 1, 1015)).toBe(1);
    });

    it('returns -1 in the gap between frames', () => {
        expect(hitTestSeries(s, 0, 1300)).toBe(-1);
    });

    it('returns -1 below the deepest row', () => {
        expect(hitTestSeries(s, 5, 1015)).toBe(-1);
    });

    it('treats a node span as half open', () => {
        expect(hitTestSeries(s, 1, 1030)).toBe(-1);
        expect(hitTestSeries(s, 1, 1010)).toBe(1);
    });
});

describe('resolveFocusIndex', () => {
    it('falls back to an exact start match for a zero duration node', () => {
        const f = frame(1, 1000);
        f.nodes.dur_us = [100, 0];
        const s = buildSeries([f]);
        expect(hitTestSeries(s, 1, 1010)).toBe(-1);
        expect(resolveFocusIndex(s, 1, 1010)).toBe(1);
    });

    it('prefers the containing node when there is one', () => {
        const s = buildSeries([frame(1, 1000)]);
        expect(resolveFocusIndex(s, 1, 1015)).toBe(1);
    });
});

describe('entryOfNode', () => {
    const s = buildSeries([frame(1, 1000), frame(2, 1500)]);

    it('maps a node index back to its frame', () => {
        expect(entryOfNode(s, 0)?.ordinal).toBe(1);
        expect(entryOfNode(s, 3)?.ordinal).toBe(2);
    });

    it('returns null for an index outside the window', () => {
        expect(entryOfNode(s, 99)).toBeNull();
    });
});

describe('layoutSeries', () => {
    const s = buildSeries([frame(1, 1000), frame(2, 1500), frame(3, 2000)]);
    const opts = {
        viewStartUs: 0,
        viewEndUs: 0,
        widthPx: 600,
        maxDepth: 32,
        minWidthPx: 2,
        minVisibleDurationUs: 0,
    };

    it('lays out every visible frame in one pass', () => {
        const quads = layoutSeries(s, { ...opts, viewStartUs: 1000, viewEndUs: 2100 });
        expect(quads.filter((q) => q.depth === 0)).toHaveLength(3);
    });

    it('returns nothing when the view sits entirely in a gap', () => {
        expect(layoutSeries(s, { ...opts, viewStartUs: 1200, viewEndUs: 1300 })).toEqual([]);
    });

    it('sorts quads by depth so the label suppression pass stays correct', () => {
        const quads = layoutSeries(s, { ...opts, viewStartUs: 1000, viewEndUs: 2100 });
        for (let i = 1; i < quads.length; i++) {
            expect(quads[i].depth).toBeGreaterThanOrEqual(quads[i - 1].depth);
        }
    });
});

describe('pushFrame', () => {
    it('appends a newer frame', () => {
        expect(pushFrame([frame(1, 1000)], frame(2, 1500)).map((f) => f.capture_ordinal)).toEqual([
            1, 2,
        ]);
    });

    it('returns the same array when the poll saw the same frame again', () => {
        const window = [frame(1, 1000)];
        expect(pushFrame(window, frame(1, 1000))).toBe(window);
    });

    // a hitch frame gets served mid-flight; freezing that first sight truncates the one
    // frame worth looking at.
    it('replaces the newest frame when the same ordinal returns with more nodes', () => {
        const partial = frame(2, 1500);
        const fuller = frame(2, 1500);
        fuller.node_count = 3;
        fuller.nodes = {
            ...fuller.nodes,
            section_ids: [10, 20, 30],
            parent_ids: [-1, 10, 10],
            node_ids: [1, 2, 3],
            parent_node_ids: [-1, 1, 1],
            start_us: [1500, 1510, 1540],
            dur_us: [100, 20, 40],
        };
        const window = pushFrame([frame(1, 1000), partial], fuller);
        expect(window.map((f) => f.capture_ordinal)).toEqual([1, 2]);
        expect(window.at(-1)!.node_count).toBe(3);
    });

    it('keeps the window when the same ordinal returns no fuller', () => {
        const window = [frame(1, 1000), frame(2, 1500)];
        expect(pushFrame(window, frame(2, 1500))).toBe(window);
    });

    it('returns the same array for an older frame', () => {
        const window = [frame(2, 1500)];
        expect(pushFrame(window, frame(1, 1000))).toBe(window);
    });

    it('returns the same array when there is nothing to push', () => {
        const window = [frame(1, 1000)];
        expect(pushFrame(window, null)).toBe(window);
    });

    it('drops the oldest frames once the frame cap is passed', () => {
        let window: FrameData[] = [];
        for (let i = 1; i <= 5; i++) window = pushFrame(window, frame(i, i * 1000), 3, 1_000_000);
        expect(window.map((f) => f.capture_ordinal)).toEqual([3, 4, 5]);
    });

    it('drops the oldest frames once the node budget is passed', () => {
        let window: FrameData[] = [];
        for (let i = 1; i <= 5; i++) window = pushFrame(window, frame(i, i * 1000), 100, 6);
        expect(window.map((f) => f.capture_ordinal)).toEqual([3, 4, 5]);
    });

    it('never drops the frame it was just handed', () => {
        expect(pushFrame([], frame(1, 1000), 100, 0)).toHaveLength(1);
    });
});
