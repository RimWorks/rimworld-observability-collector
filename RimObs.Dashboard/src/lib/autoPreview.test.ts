import { describe, it, expect } from 'vitest';
import { previewBand, isDirty, dropReasons, WARN_AT, BAD_AT } from './autoPreview';
import type { AutoPreviewCounts } from './api';

const EMPTY: AutoPreviewCounts = {
    matched: 0,
    eligible: 0,
    skippedTrivial: 0,
    skippedIgnored: 0,
    skippedBlocklisted: 0,
    skippedAlreadyInstrumented: 0,
    skippedOverCap: 0,
};

describe('previewBand', () => {
    it('reads ok for a narrow filter', () => {
        expect(previewBand(0)).toBe('ok');
        expect(previewBand(WARN_AT - 1)).toBe('ok');
    });

    it('reads warn from the warn threshold up', () => {
        expect(previewBand(WARN_AT)).toBe('warn');
        expect(previewBand(BAD_AT - 1)).toBe('warn');
    });

    // Assembly-CSharp!* matches over 22,000 methods and stalls loading for seconds.
    it('reads bad from the bad threshold up', () => {
        expect(previewBand(BAD_AT)).toBe('bad');
        expect(previewBand(22411)).toBe('bad');
    });
});

describe('isDirty', () => {
    it('is clean when both lists match what was applied', () => {
        expect(isDirty('a', 'b', 'a', 'b')).toBe(false);
    });

    it('is dirty when either list differs', () => {
        expect(isDirty('a2', 'b', 'a', 'b')).toBe(true);
        expect(isDirty('a', 'b2', 'a', 'b')).toBe(true);
    });

    it('treats whitespace as a real edit, because the game will too', () => {
        expect(isDirty('a ', 'b', 'a', 'b')).toBe(true);
    });
});

describe('dropReasons', () => {
    it('is empty when nothing was dropped', () => {
        expect(dropReasons(EMPTY)).toEqual([]);
    });

    it('drops zero counts and orders the rest largest first', () => {
        const p = { ...EMPTY, skippedTrivial: 9001, skippedIgnored: 700, skippedOverCap: 107 };
        expect(dropReasons(p)).toEqual([
            { key: 'trivial', count: 9001 },
            { key: 'ignored', count: 700 },
            { key: 'overCap', count: 107 },
        ]);
    });
});
