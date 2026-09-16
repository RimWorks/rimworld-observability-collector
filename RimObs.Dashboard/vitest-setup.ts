import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/svelte';

// jsdom has no matchMedia; uplot calls it at module load to track device pixel ratio.
if (globalThis.window !== undefined && !globalThis.matchMedia) {
    globalThis.matchMedia = (query: string) =>
        ({
            matches: false,
            media: query,
            onchange: null,
            addListener: () => {},
            removeListener: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => false,
        }) as MediaQueryList;
}

// jsdom has no element.animate; svelte transitions need one that finishes at once.
if (globalThis.Element !== undefined && !Element.prototype.animate) {
    Element.prototype.animate = function () {
        const anim = {
            onfinish: null as (() => void) | null,
            oncancel: null as (() => void) | null,
            cancel() {},
            finished: Promise.resolve(),
        };
        queueMicrotask(() => anim.onfinish?.());
        return anim as unknown as Animation;
    } as unknown as typeof Element.prototype.animate;
}

// jsdom has no ResizeObserver, and svelte's clientHeight bindings construct one on mount.
globalThis.ResizeObserver ??= class {
    observe() {}
    unobserve() {}
    disconnect() {}
} as unknown as typeof ResizeObserver;

afterEach(() => cleanup());
