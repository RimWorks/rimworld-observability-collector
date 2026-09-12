import { Resource, isFrozen } from './poll.svelte';

/**
 * A Resource fed by the collector's SSE stream instead of a timer. While the stream is down
 * it polls at the fallback cadence; EventSource reconnects on its own.
 */
export class StreamResource<T> extends Resource<T> {
    private es: EventSource | null = null;
    private polling = false;

    constructor(
        private readonly url: string,
        loader: () => Promise<T>,
        fallbackIntervalMs = 1000,
        private readonly eventSourceFactory: (url: string) => EventSource = (u) =>
            new EventSource(u),
    ) {
        super(loader, fallbackIntervalMs);
    }

    override start(): void {
        if (isFrozen() || typeof EventSource === 'undefined') {
            super.start();
            return;
        }
        void this.refresh();
        this.es = this.eventSourceFactory(this.url);
        this.es.addEventListener('frame', (ev) => {
            this.setPolling(false);
            this.accept(JSON.parse((ev as MessageEvent).data) as T);
        });
        this.es.onerror = () => this.setPolling(true);
    }

    override stop(): void {
        this.es?.close();
        this.es = null;
        this.polling = false;
        super.stop();
    }

    private accept(value: T): void {
        this.data = value;
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
