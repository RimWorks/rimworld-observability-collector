/** js transitions ignore the css reduced-motion block, so they gate here instead. */
export function motionMs(base: number): number {
    return typeof matchMedia === 'function' &&
        matchMedia('(prefers-reduced-motion: reduce)').matches
        ? 0
        : base;
}
