import type { AutoPreviewCounts } from './api';

/** How loud the match count should read. The bands come from what the game actually does. */
export type PreviewBand = 'ok' | 'warn' | 'bad';

/**
 * Under 500 the patch pass finishes inside a frame or two. Past 5,000 it stalls loading for
 * seconds and buries the real hotspots, which is the failure the preview exists to prevent.
 */
export const WARN_AT = 500;
export const BAD_AT = 5000;

export function previewBand(eligible: number): PreviewBand {
    if (eligible >= BAD_AT) return 'bad';
    if (eligible >= WARN_AT) return 'warn';
    return 'ok';
}

/** True when the filters changed since the last apply, so Apply has something to do. */
export function isDirty(
    filters: string,
    ignore: string,
    appliedFilters: string,
    appliedIgnore: string,
): boolean {
    return filters !== appliedFilters || ignore !== appliedIgnore;
}

/** The counts that explain the gap between Matched and Eligible, largest first, zeros dropped. */
export function dropReasons(p: AutoPreviewCounts): { key: string; count: number }[] {
    return (
        [
            { key: 'trivial', count: p.skippedTrivial },
            { key: 'ignored', count: p.skippedIgnored },
            { key: 'blocked', count: p.skippedBlocklisted },
            { key: 'already', count: p.skippedAlreadyInstrumented },
            { key: 'overCap', count: p.skippedOverCap },
        ] as const
    )
        .filter((r) => r.count > 0)
        .map((r) => ({ key: r.key, count: r.count }))
        .sort((a, b) => b.count - a.count);
}
