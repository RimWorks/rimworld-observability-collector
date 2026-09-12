/**
 * One EventSource for the whole app. Every stream-backed resource subscribes here by event
 * name, so N resources never open N connections. EventSource reconnects on its own.
 */
export type StreamListener = (raw: string) => void;
export type EventSourceFactory = (url: string) => EventSource;

export class StreamHub {
    private es: EventSource | null = null;
    private readonly listeners = new Map<string, Set<StreamListener>>();
    private readonly errorListeners = new Set<() => void>();
    private readonly openListeners = new Set<() => void>();

    constructor(
        private readonly url: string,
        private readonly factory: EventSourceFactory = (u) => new EventSource(u),
    ) {}

    subscribe(event: string, cb: StreamListener): () => void {
        let set = this.listeners.get(event);
        if (!set) {
            set = new Set();
            this.listeners.set(event, set);
            // the server only sends requested lanes, so a new lane needs a fresh connection.
            this.close();
            this.ensureSource();
        }
        set.add(cb);
        return () => {
            set.delete(cb);
            if (set.size === 0) {
                this.listeners.delete(event);
                // drop the lane server-side too; a closed drawer must stop 15MB pushes.
                this.close();
                if (this.listeners.size > 0) this.ensureSource();
            }
            if (this.listeners.size === 0) this.close();
        };
    }

    onError(cb: () => void): () => void {
        this.errorListeners.add(cb);
        return () => this.errorListeners.delete(cb);
    }

    onOpen(cb: () => void): () => void {
        this.openListeners.add(cb);
        return () => this.openListeners.delete(cb);
    }

    private dispatchTo(event: string, ev: Event): void {
        const set = this.listeners.get(event);
        if (!set) return;
        for (const cb of set) cb((ev as MessageEvent).data as string);
    }

    private ensureSource(): void {
        if (this.es) return;
        const lanes = [...this.listeners.keys()].sort().join(',');
        const sep = this.url.includes('?') ? '&' : '?';
        this.es = this.factory(lanes ? `${this.url}${sep}lanes=${lanes}` : this.url);
        for (const event of this.listeners.keys())
            this.es.addEventListener(event, (ev) => this.dispatchTo(event, ev));
        this.es.onerror = () => {
            for (const cb of this.errorListeners) cb();
        };
        this.es.onopen = () => {
            for (const cb of this.openListeners) cb();
        };
    }

    private close(): void {
        this.es?.close();
        this.es = null;
    }
}
