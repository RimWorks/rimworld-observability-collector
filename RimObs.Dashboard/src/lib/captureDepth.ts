export const MIN_DEPTH = 1;
export const MAX_DEPTH = 64;
export const DEFAULT_DEPTH = 8;

/** 64 is the library's per-thread stack, so anything above it would be silently truncated. */
export function clampDepth(value: number): number {
    if (!Number.isFinite(value)) return DEFAULT_DEPTH;
    return Math.min(MAX_DEPTH, Math.max(MIN_DEPTH, Math.round(value)));
}
