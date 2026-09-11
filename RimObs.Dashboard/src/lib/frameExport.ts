import type { ThreadLane } from './api';
import type { FrameData } from './frameTree';

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

export function exportFileName(kind: 'frame' | 'ring', ordinal: number | null): string {
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    return kind === 'frame'
        ? `rimobs-frame-${ordinal ?? 'live'}-${stamp}.json`
        : `rimobs-ring-${stamp}.json`;
}
