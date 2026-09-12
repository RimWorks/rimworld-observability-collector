import type { ThreadLane } from './api';
import type { FrameData, FrameSummariesResponse } from './frameTree';

type SectionNames = Map<number, { name: string; subsystem: string | null }>;

export interface DroppedCounts {
    pre_frame_samples: number;
    late_samples: number;
    /** Samples the library's ring threw away before they ever left the game. */
    library_ring_samples: number;
}

/** What was instrumented when the frames were captured. A wide filter is the usual reason for drops. */
export interface AutoInstrumentProvenance {
    enabled: boolean;
    filters: string;
    ignore: string;
    /** the scan cap in force; a wide filter past it means silent gaps, not loss. */
    max_targets?: number | null;
}

const NO_DROPS: DroppedCounts = { pre_frame_samples: 0, late_samples: 0, library_ring_samples: 0 };

/**
 * A shareable snapshot: frames with every referenced section name resolved inline, so the
 * file answers questions on its own with no live collector behind it.
 */
export interface FrameExport {
    schema_version: number;
    kind: 'frame' | 'ring';
    exported_utc: string;
    stopwatch_frequency: number;
    threads: ThreadLane[];
    sections: Record<number, { name: string; subsystem: string | null }>;
    frames: FrameData[];
    dropped: DroppedCounts;
    auto_instrument: AutoInstrumentProvenance | null;
}

export function buildFrameExport(
    kind: 'frame' | 'ring',
    frames: FrameData[],
    threads: ThreadLane[],
    names: SectionNames,
    stopwatchFrequency: number,
    dropped: DroppedCounts = NO_DROPS,
    autoInstrument: AutoInstrumentProvenance | null = null,
    now = new Date(),
): FrameExport {
    const sections: FrameExport['sections'] = {};
    for (const frame of frames) {
        for (const id of frame.nodes.section_ids) {
            if (sections[id]) continue;
            const named = names.get(id);
            sections[id] = named ?? { name: `#${id}`, subsystem: null };
        }
    }
    return {
        schema_version: 10,
        kind,
        exported_utc: now.toISOString(),
        stopwatch_frequency: stopwatchFrequency,
        threads,
        sections,
        frames,
        dropped,
        auto_instrument: autoInstrument,
    };
}

export function exportFileName(
    kind: 'frame' | 'ring' | 'timeline',
    ordinal: number | null,
): string {
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    if (kind === 'frame') return `rimobs-frame-${ordinal ?? 'live'}-${stamp}.json`;
    return kind === 'ring' ? `rimobs-ring-${stamp}.json` : `rimobs-timeline-${stamp}.json`;
}

/**
 * One summary row per ring frame. The node-carrying ring export caps at a few hundred frames
 * for size; this is the whole ring's shape in a few MB.
 */
export interface TimelineExport {
    schema_version: number;
    kind: 'timeline';
    exported_utc: string;
    stopwatch_frequency: number;
    frame_count: number;
    ordinals: number[];
    start_us: number[];
    duration_us: number[];
    node_counts: number[];
    sections: Record<string, { name: string; subsystem: string | null }>;
    section_durations: Record<string, number[]>;
    dropped: DroppedCounts;
    auto_instrument: AutoInstrumentProvenance | null;
}

export function buildTimelineExport(
    summaries: FrameSummariesResponse,
    names: SectionNames,
    autoInstrument: AutoInstrumentProvenance | null = null,
    now = new Date(),
): TimelineExport {
    const sections: TimelineExport['sections'] = {};
    for (const id of Object.keys(summaries.section_durations)) {
        const named = names.get(Number(id));
        sections[id] = named ?? { name: `#${id}`, subsystem: null };
    }
    return {
        schema_version: 10,
        kind: 'timeline',
        exported_utc: now.toISOString(),
        stopwatch_frequency: summaries.stopwatch_frequency,
        frame_count: summaries.frame_count,
        ordinals: summaries.ordinals,
        start_us: summaries.start_us,
        duration_us: summaries.duration_us,
        node_counts: summaries.node_counts,
        sections,
        section_durations: summaries.section_durations,
        dropped: summaries.dropped,
        auto_instrument: autoInstrument,
    };
}
