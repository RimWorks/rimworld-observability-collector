export interface ViewSpan {
    startUs: number;
    endUs: number;
}

export interface FrameSpan {
    ordinal: number;
    startUs: number;
    endUs: number;
}

export interface Refinement {
    from: number;
    to: number;
    floorUs: number;
}

/** spans under this load fully; a coarse first fetch is not worth its second request. */
const FULL_DETAIL_SPAN_US = 250_000;
/** drawable slots a view is treated as having, matching the canvas cull of span/2000. */
const SLOTS = 4000;

/** the duration floor a span can afford: nodes shorter than this cannot be drawn anyway. */
export function lodFloorUs(spanUs: number): number {
    return spanUs > FULL_DETAIL_SPAN_US ? spanUs / SLOTS : 0;
}

/**
 * The refetch a zoomed view needs, or null when the loaded detail already covers it.
 * `lodByOrdinal` holds the floor each pinned frame was fetched at.
 */
export function refinementFor(
    view: ViewSpan,
    frames: FrameSpan[],
    lodByOrdinal: Map<number, number>,
): Refinement | null {
    const needed = lodFloorUs(view.endUs - view.startUs);
    let from = -1;
    let to = -1;
    for (const f of frames) {
        if (f.endUs <= view.startUs || f.startUs >= view.endUs) continue;
        const loaded = lodByOrdinal.get(f.ordinal) ?? 0;
        // hysteresis: a refetch only pays off when the loaded floor is meaningfully coarser.
        if (loaded <= needed || (needed > 0 && loaded < needed * 2)) continue;
        if (from < 0) from = f.ordinal;
        to = f.ordinal;
    }
    return from < 0 ? null : { from, to, floorUs: needed };
}
