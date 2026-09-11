import type { ThreadLane } from './api';
import type { FrameData } from './frameTree';

type SectionNames = Map<number, { name: string; subsystem: string | null }>;

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
}

export function buildFrameExport(
    kind: 'frame' | 'ring',
    frames: FrameData[],
    threads: ThreadLane[],
    names: SectionNames,
    stopwatchFrequency: number,
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
        schema_version: 9,
        kind,
        exported_utc: now.toISOString(),
        stopwatch_frequency: stopwatchFrequency,
        threads,
        sections,
        frames,
    };
}

export function exportFileName(kind: 'frame' | 'ring', ordinal: number | null): string {
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    return kind === 'frame'
        ? `rimobs-frame-${ordinal ?? 'live'}-${stamp}.json`
        : `rimobs-ring-${stamp}.json`;
}
