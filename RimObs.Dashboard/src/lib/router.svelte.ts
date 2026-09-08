import type { IconName } from './components/Icon.svelte';

export interface RouteDef {
    id: string;
    title: string;
    icon: IconName;
}

export const routes: RouteDef[] = [
    { id: 'overview', title: 'Overview', icon: 'gauge' },
    { id: 'flamegraph', title: 'Flamegraph', icon: 'flame' },
    { id: 'instrumentation', title: 'Instrumentation', icon: 'probe' },
    { id: 'memory', title: 'Memory', icon: 'memory' },
    { id: 'comparison', title: 'Comparison', icon: 'compare' },
    { id: 'settings', title: 'Settings', icon: 'cog' },
];

const DEFAULT_ROUTE = 'overview';

function parseHash(): string {
    const raw = globalThis.location.hash.replace(/^#\/?/, '').split('?')[0];
    return routes.some((r) => r.id === raw) ? raw : DEFAULT_ROUTE;
}

class Router {
    current = $state<string>(DEFAULT_ROUTE);

    start() {
        this.current = parseHash();
        globalThis.addEventListener('hashchange', () => {
            this.current = parseHash();
        });
        if (!globalThis.location.hash) {
            globalThis.location.hash = `#/${DEFAULT_ROUTE}`;
        }
    }

    go(id: string) {
        globalThis.location.hash = `#/${id}`;
    }

    get route(): RouteDef {
        return routes.find((r) => r.id === this.current) ?? routes[0];
    }
}

export const router = new Router();
