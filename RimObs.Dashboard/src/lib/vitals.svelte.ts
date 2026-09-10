import type { FrameVitals } from './frameTree';

/**
 * TPS and FPS ride along on the frame poll, which runs about two orders of magnitude faster
 * than the status poll. The header reads them from here so they move every frame.
 */
class LiveVitals {
    tps = $state<number | null>(null);
    fps = $state<number | null>(null);

    set(vitals: FrameVitals | null | undefined): void {
        this.tps = vitals?.tps ?? null;
        this.fps = vitals?.fps ?? null;
    }

    clear(): void {
        this.tps = null;
        this.fps = null;
    }
}

export const liveVitals = new LiveVitals();
