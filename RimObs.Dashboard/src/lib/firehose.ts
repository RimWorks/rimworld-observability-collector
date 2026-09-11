export const MIN_SAMPLE_RING = 1024;
export const MAX_SAMPLE_RING = 1048576;

export const MIN_MAX_TARGETS = 1;
export const MAX_MAX_TARGETS = 1000000;

/** Mirrors SamplingOptions.ClampRingCapacity: the library's rings must be a power of two. */
export function clampSampleRing(value: number): number {
    if (!Number.isFinite(value)) return MIN_SAMPLE_RING;
    const capped = Math.min(MAX_SAMPLE_RING, Math.max(MIN_SAMPLE_RING, Math.round(value)));
    let rounded = MIN_SAMPLE_RING;
    while (rounded < capped) rounded *= 2;
    return rounded;
}

export function clampMaxTargets(value: number): number {
    if (!Number.isFinite(value)) return MIN_MAX_TARGETS;
    return Math.min(MAX_MAX_TARGETS, Math.max(MIN_MAX_TARGETS, Math.round(value)));
}
