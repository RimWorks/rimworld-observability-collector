import type { GcEvent } from './api';

const PAUSE_LABELS = ['Foreground', 'Background'];
export function pauseLabel(pauseType: number): string {
    return PAUSE_LABELS[pauseType] ?? `pause:${pauseType}`;
}

export function genLabel(generation: number): string {
    if (generation >= 2) return 'Gen 2';
    return `Gen ${generation}`;
}

export interface GcSummary {
    currentHeap: number;
    peakHeap: number;
    peakAllocRate: number;
    totalCollections: number;
    perGen: [number, number, number];
}

export function summarize(events: readonly GcEvent[]): GcSummary {
    const summary: GcSummary = {
        currentHeap: 0,
        peakHeap: 0,
        peakAllocRate: 0,
        totalCollections: events.length,
        perGen: [0, 0, 0],
    };
    if (events.length === 0) return summary;

    // Events arrive newest-first; the freshest heap reading is index 0.
    summary.currentHeap = events[0].heap_after;
    for (const e of events) {
        if (e.heap_after > summary.peakHeap) summary.peakHeap = e.heap_after;
        if (e.allocation_rate_bpm > summary.peakAllocRate)
            summary.peakAllocRate = e.allocation_rate_bpm;
        const bucket = Math.min(e.generation, 2);
        summary.perGen[bucket]++;
    }
    return summary;
}
