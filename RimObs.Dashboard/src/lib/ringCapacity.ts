export const MIN_RING = 100;
export const MAX_RING = 20000;

/** The whole ring ships on every frame poll, so the ceiling is a payload budget, not a memory one. */
export function clampRing(value: number): number {
    if (!Number.isFinite(value)) return MIN_RING;
    return Math.min(MAX_RING, Math.max(MIN_RING, Math.round(value)));
}
