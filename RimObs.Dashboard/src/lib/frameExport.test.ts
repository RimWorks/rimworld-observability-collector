import { describe, it, expect } from 'vitest';
import { buildFrameExport, exportFileName } from './frameExport';
import type { FrameData } from './frameTree';

const FRAME: FrameData = {
    capture_ordinal: 7,
    start_us: 0,
    end_us: 100,
    duration_us: 100,
    node_count: 2,
    nodes: {
        section_ids: [10, 20],
        parent_ids: [-1, 10],
        node_ids: [1, 2],
        parent_node_ids: [-1, 1],
        start_us: [0, 10],
        dur_us: [100, 20],
        thread_ids: [1, 1],
    },
};

describe('buildFrameExport', () => {
    it('inlines every referenced section name so the file stands alone', () => {
        const names = new Map([[10, { name: 'Verse.Root_Play.Update', subsystem: 'render' }]]);
        const out = buildFrameExport('frame', [FRAME], [], names, 10_000_000);

        expect(out.sections[10].name).toBe('Verse.Root_Play.Update');
        // an unnamed id still exports, as its number, rather than dropping the node's label.
        expect(out.sections[20].name).toBe('#20');
        expect(out.frames).toHaveLength(1);
        expect(out.stopwatch_frequency).toBe(10_000_000);
        expect(out.kind).toBe('frame');
    });

    it('records the drop counters and the filter that was running', () => {
        const out = buildFrameExport(
            'ring',
            [FRAME],
            [],
            new Map(),
            10_000_000,
            { pre_frame_samples: 12, late_samples: 7 },
            { enabled: true, filters: '*', ignore: 'System.*' },
        );

        expect(out.dropped).toEqual({ pre_frame_samples: 12, late_samples: 7 });
        expect(out.auto_instrument).toEqual({ enabled: true, filters: '*', ignore: 'System.*' });
    });

    // an imported bundle has no live config behind it, so the fields still have to be there.
    it('defaults to zero drops and no filter when neither is known', () => {
        const out = buildFrameExport('frame', [FRAME], [], new Map(), 10_000_000);

        expect(out.dropped).toEqual({ pre_frame_samples: 0, late_samples: 0 });
        expect(out.auto_instrument).toBeNull();
    });
});

describe('exportFileName', () => {
    it('names a frame export by its ordinal and a ring export by kind', () => {
        expect(exportFileName('frame', 4321)).toMatch(/^rimobs-frame-4321-/);
        expect(exportFileName('ring', null)).toMatch(/^rimobs-ring-/);
    });
});
