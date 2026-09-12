import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { StreamResource } from './stream.svelte';

class FakeEventSource {
    static latest: FakeEventSource | null = null;
    onerror: (() => void) | null = null;
    closed = false;
    private listeners = new Map<string, (ev: MessageEvent) => void>();

    constructor(public url: string) {
        FakeEventSource.latest = this;
    }

    addEventListener(type: string, handler: (ev: MessageEvent) => void): void {
        this.listeners.set(type, handler);
    }

    emit(type: string, data: unknown): void {
        this.listeners.get(type)?.({ data: JSON.stringify(data) } as MessageEvent);
    }

    close(): void {
        this.closed = true;
    }
}

describe('StreamResource', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.stubGlobal('EventSource', FakeEventSource);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        vi.useRealTimers();
    });

    function make(loader = vi.fn().mockResolvedValue({ n: 0 })) {
        const res = new StreamResource<{ n: number }>(
            '/api/v1/stream',
            loader,
            1000,
            (url) => new FakeEventSource(url) as unknown as EventSource,
        );
        return { res, loader };
    }

    it('feeds data from frame events without any timer', async () => {
        const { res, loader } = make();
        res.start();
        await vi.runOnlyPendingTimersAsync();
        loader.mockClear();

        FakeEventSource.latest!.emit('frame', { n: 42 });

        expect(res.data).toEqual({ n: 42 });
        expect(res.state).toBe('ok');
        await vi.advanceTimersByTimeAsync(5000);
        expect(loader).not.toHaveBeenCalled();
    });

    it('falls back to polling while the stream errors and stops when it recovers', async () => {
        const { res, loader } = make();
        res.start();
        await vi.runOnlyPendingTimersAsync();
        loader.mockClear();

        FakeEventSource.latest!.onerror?.();
        await vi.advanceTimersByTimeAsync(3000);
        expect(loader.mock.calls.length).toBeGreaterThan(0);

        FakeEventSource.latest!.emit('frame', { n: 7 });
        loader.mockClear();
        await vi.advanceTimersByTimeAsync(5000);
        expect(loader).not.toHaveBeenCalled();
        expect(res.data).toEqual({ n: 7 });
    });

    it('closes the stream on stop', async () => {
        const { res } = make();
        res.start();
        await vi.runOnlyPendingTimersAsync();

        res.stop();

        expect(FakeEventSource.latest!.closed).toBe(true);
    });
});
