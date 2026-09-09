import { describe, it, expect } from 'vitest';
import type { GcEvent } from './api';
import { pauseLabel, genLabel, summarize, heapSeries } from './gc';

function ev(partial: Partial<GcEvent>): GcEvent {
    return {
        generation: 0,
        pause_type: 0,
        heap_before: 0,
        heap_after: 0,
        duration_micros: 0,
        ticks: 0,
        allocation_rate_bpm: 0,
        frame_ordinal: 0,
        ...partial,
    };
}

describe('pauseLabel', () => {
    it('decodes known pause types', () => {
        expect(pauseLabel(0)).toBe('Foreground');
        expect(pauseLabel(1)).toBe('Background');
    });

    it('falls back for unknown values', () => {
        expect(pauseLabel(7)).toBe('pause:7');
    });
});

describe('genLabel', () => {
    it('labels each generation', () => {
        expect(genLabel(0)).toBe('Gen 0');
        expect(genLabel(1)).toBe('Gen 1');
        expect(genLabel(2)).toBe('Gen 2');
    });

    it('clamps higher generations into Gen 2', () => {
        expect(genLabel(5)).toBe('Gen 2');
    });
});

describe('summarize', () => {
    it('returns zeroed summary for no events', () => {
        const s = summarize([]);
        expect(s.currentHeap).toBe(0);
        expect(s.peakHeap).toBe(0);
        expect(s.peakAllocRate).toBe(0);
        expect(s.totalCollections).toBe(0);
        expect(s.perGen).toEqual([0, 0, 0]);
    });

    it('takes current heap from the newest event', () => {
        const s = summarize([ev({ heap_after: 500 }), ev({ heap_after: 900 })]);
        expect(s.currentHeap).toBe(500);
    });

    it('tracks peak heap and peak alloc rate across events', () => {
        const s = summarize([
            ev({ heap_after: 100, allocation_rate_bpm: 10 }),
            ev({ heap_after: 800, allocation_rate_bpm: 50 }),
            ev({ heap_after: 300, allocation_rate_bpm: 20 }),
        ]);
        expect(s.peakHeap).toBe(800);
        expect(s.peakAllocRate).toBe(50);
    });

    it('counts collections per generation with gen>=2 clamped', () => {
        const s = summarize([
            ev({ generation: 0 }),
            ev({ generation: 0 }),
            ev({ generation: 1 }),
            ev({ generation: 3 }),
        ]);
        expect(s.totalCollections).toBe(4);
        expect(s.perGen).toEqual([2, 1, 1]);
    });
});

describe('heapSeries', () => {
    it('reverses newest-first events into chronological order', () => {
        const series = heapSeries([
            ev({ ticks: 30, heap_after: 300 }),
            ev({ ticks: 20, heap_after: 200 }),
            ev({ ticks: 10, heap_after: 100 }),
        ]);
        expect(series.ticks).toEqual([10, 20, 30]);
        expect(series.heap).toEqual([100, 200, 300]);
    });

    it('handles empty input', () => {
        const series = heapSeries([]);
        expect(series.ticks).toEqual([]);
        expect(series.heap).toEqual([]);
    });
});
