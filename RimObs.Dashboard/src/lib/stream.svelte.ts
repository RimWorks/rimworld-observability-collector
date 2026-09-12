import { Resource, isFrozen } from './poll.svelte';
import { StreamHub, type EventSourceFactory } from './streamHub';

const hubs = new Map<string, StreamHub>();

function hubFor(url: string, factory?: EventSourceFactory): StreamHub {
    let hub = hubs.get(url);
    if (!hub) {
        hub = new StreamHub(url, factory);
        hubs.set(url, hub);
    }
    return hub;
}

/** test-only: forget cached hubs so a fake EventSource factory takes effect. */
export function resetStreamHubs(): void {
    hubs.clear();
}

/**
 * A Resource fed by a named event on the shared SSE stream. While the stream is down it
 * polls at the fallback cadence; EventSource reconnects on its own.
 */
export class StreamResource<T> extends Resource<T> {
    private polling = false;
    private pending: string | null = null;
    private lastApplied = 0;
    private flushTimer: ReturnType<typeof setTimeout> | null = null;
    private readonly cleanups: (() => void)[] = [];
    private readonly onVisible = () => this.flush();

    constructor(
        private readonly url: string,
        loader: () => Promise<T>,
        fallbackIntervalMs = 1000,
        private readonly eventSourceFactory?: EventSourceFactory,
        /** floor between applied stream events; 0 applies every event. */
        private readonly minApplyIntervalMs = 0,
        private readonly eventName = 'frame',
    ) {
        super(loader, fallbackIntervalMs);
    }

    override start(): void {
        if (isFrozen() || typeof EventSource === 'undefined') {
            super.start();
            return;
        }
        void this.refresh();
        const hub = hubFor(this.url, this.eventSourceFactory);
        this.cleanups.push(
            hub.subscribe(this.eventName, (raw) => {
                this.setPolling(false);
                this.accept(raw);
            }),
            hub.onError(() => this.setPolling(true)),
        );
        if (typeof document !== 'undefined')
            document.addEventListener('visibilitychange', this.onVisible);
    }

    override stop(): void {
        for (const cleanup of this.cleanups.splice(0)) cleanup();
        this.polling = false;
        this.pending = null;
        if (this.flushTimer !== null) clearTimeout(this.flushTimer);
        this.flushTimer = null;
        if (typeof document !== 'undefined')
            document.removeEventListener('visibilitychange', this.onVisible);
        super.stop();
    }

    // applies are coalesced to the floor and dropped entirely while the tab is hidden.
    private accept(raw: string): void {
        if (typeof document !== 'undefined' && document.hidden) {
            this.pending = raw;
            return;
        }
        const now = Date.now();
        const wait = this.lastApplied + this.minApplyIntervalMs - now;
        if (wait <= 0) {
            this.apply(raw, now);
            return;
        }
        this.pending = raw;
        this.flushTimer ??= setTimeout(() => this.flush(), wait);
    }

    private flush(): void {
        this.flushTimer = null;
        if (this.pending === null || (typeof document !== 'undefined' && document.hidden)) return;
        this.apply(this.pending, Date.now());
    }

    private apply(raw: string, now: number): void {
        this.pending = null;
        this.lastApplied = now;
        this.data = JSON.parse(raw) as T;
        this.state = 'ok';
        this.error = '';
        this.consecutiveFailures = 0;
    }

    private setPolling(on: boolean): void {
        if (on === this.polling) return;
        this.polling = on;
        if (on) super.start();
        else super.stop();
    }
}
