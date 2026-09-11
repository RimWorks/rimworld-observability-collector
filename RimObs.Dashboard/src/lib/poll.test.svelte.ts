import { describe, it, expect, vi, afterEach } from 'vitest';
import { Resource, isFrozen } from './poll.svelte';

afterEach(() => vi.useRealTimers());

describe('Resource.refresh', () => {
    it('stores data and marks ok on success', async () => {
        const res = new Resource(async () => 42, 0);
        await res.refresh();
        expect(res.data).toBe(42);
        expect(res.state).toBe('ok');
        expect(res.error).toBe('');
    });

    it('marks error and keeps null data on the first failure', async () => {
        const res = new Resource(async () => {
            throw new Error('boom');
        }, 0);
        await res.refresh();
        expect(res.data).toBeNull();
        expect(res.state).toBe('error');
        expect(res.error).toBe('boom');
    });

    it('retains last-good data and stays ok when a later refresh fails', async () => {
        let fail = false;
        const res = new Resource(async () => {
            if (fail) throw new Error('later');
            return 7;
        }, 0);
        await res.refresh();
        fail = true;
        await res.refresh();
        expect(res.data).toBe(7);
        expect(res.state).toBe('ok');
        expect(res.error).toBe('later');
    });

    it('tracks consecutive failures and resets the counter on success', async () => {
        let fail = false;
        const res = new Resource(async () => {
            if (fail) throw new Error('down');
            return 'up';
        }, 0);

        await res.refresh();
        expect(res.consecutiveFailures).toBe(0);

        fail = true;
        await res.refresh();
        await res.refresh();
        await res.refresh();
        expect(res.consecutiveFailures).toBe(3);

        fail = false;
        await res.refresh();
        expect(res.consecutiveFailures).toBe(0);
    });
});

describe('Resource start/stop', () => {
    it('refreshes immediately and on each interval, and stop() halts polling', async () => {
        vi.useFakeTimers();
        let calls = 0;
        const res = new Resource(async () => ++calls, 1000);
        res.start();
        await vi.advanceTimersByTimeAsync(0);
        expect(calls).toBe(1);
        await vi.advanceTimersByTimeAsync(2000);
        expect(calls).toBe(3);
        res.stop();
        await vi.advanceTimersByTimeAsync(5000);
        expect(calls).toBe(3);
    });

    it('does not schedule an interval when intervalMs is 0', async () => {
        vi.useFakeTimers();
        let calls = 0;
        const res = new Resource(async () => ++calls, 0);
        res.start();
        await vi.advanceTimersByTimeAsync(0);
        expect(calls).toBe(1);
        await vi.advanceTimersByTimeAsync(10000);
        expect(calls).toBe(1);
        res.stop();
    });

    it('skips a tick while a refresh is still in flight', async () => {
        vi.useFakeTimers();
        let started = 0;
        let release: (() => void) | null = null;
        const res = new Resource<number>(() => {
            started += 1;
            return new Promise<number>((resolve) => {
                release = () => resolve(started);
            });
        }, 10);

        res.start();
        expect(started).toBe(1);

        await vi.advanceTimersByTimeAsync(50);
        expect(started).toBe(1);

        release!();
        await vi.advanceTimersByTimeAsync(20);
        expect(started).toBe(2);

        res.stop();
    });
});

// a scanner or a screenshot tool waits for the network to go quiet, and a 3s poll never lets
// it. freeze keeps the first load, which is what makes the capture worth looking at.
// the flamegraph retunes its poll to frame size; rebuilding the Resource for that reset
// data to null and flashed the whole page through the loading state on every band change.
describe('Resource.setIntervalMs', () => {
    it('changes cadence in place without dropping data or state', async () => {
        vi.useFakeTimers();
        let calls = 0;
        const res = new Resource(async () => ++calls, 1000);
        res.start();
        await vi.advanceTimersByTimeAsync(0);
        expect(res.data).toBe(1);

        res.setIntervalMs(100);

        expect(res.data).toBe(1);
        expect(res.state).toBe('ok');
        await vi.advanceTimersByTimeAsync(350);
        expect(calls).toBe(4);
        res.stop();
    });

    it('does nothing when the cadence is unchanged', async () => {
        vi.useFakeTimers();
        let calls = 0;
        const res = new Resource(async () => ++calls, 1000);
        res.start();
        await vi.advanceTimersByTimeAsync(0);

        res.setIntervalMs(1000);

        await vi.advanceTimersByTimeAsync(1000);
        expect(calls).toBe(2);
        res.stop();
    });
});

describe('isFrozen', () => {
    it('is off with no query string', () => {
        expect(isFrozen('')).toBe(false);
    });

    it('is on for a bare freeze flag', () => {
        expect(isFrozen('?freeze')).toBe(true);
    });

    it('is on for freeze=1 alongside other params', () => {
        expect(isFrozen('?lang=de&freeze=1')).toBe(true);
    });

    it('ignores a param that merely starts with freeze', () => {
        expect(isFrozen('?freezer=1')).toBe(false);
    });
});

describe('Resource.start when frozen', () => {
    it('loads once and schedules no interval', async () => {
        const spy = vi.spyOn(globalThis, 'setInterval');
        vi.stubGlobal('location', { search: '?freeze=1' } as Location);
        let calls = 0;
        const res = new Resource(async () => ++calls, 50);

        res.start();
        await vi.waitFor(() => expect(calls).toBe(1));

        expect(spy).not.toHaveBeenCalled();
        res.stop();
        vi.unstubAllGlobals();
        spy.mockRestore();
    });
});
