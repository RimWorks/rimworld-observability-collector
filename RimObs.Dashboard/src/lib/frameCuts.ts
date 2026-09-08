/**
 * A pause leaves a hole in the frame history. The ordinal the pause ended on marks that hole,
 * and stays marked until it scrolls out of the strip's window.
 */
export function recordCut(cuts: readonly number[], frozenOrdinals: readonly number[]): number[] {
    const last = frozenOrdinals.at(-1);
    if (last === undefined || cuts.includes(last)) return [...cuts];
    return [...cuts, last];
}

/** Cuts still inside the window the strip is showing. */
export function visibleCuts(cuts: readonly number[], ordinals: readonly number[]): number[] {
    if (ordinals.length === 0) return [];
    const oldest = ordinals[0];
    const newest = ordinals[ordinals.length - 1];
    return cuts.filter((o) => o >= oldest && o <= newest);
}
