/**
 * Config values a component other than the settings pane has to react to. The pane sits under
 * TopBar and the flamegraph is its sibling, so a callback would be three hops of prop drilling.
 */
class LiveConfig {
    ringCapacity = $state<number | null>(null);

    setRingCapacity(capacity: number | null | undefined): void {
        this.ringCapacity = capacity ?? null;
    }
}

export const liveConfig = new LiveConfig();
