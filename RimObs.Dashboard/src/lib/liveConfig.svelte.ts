import type { AutoInstrumentProvenance } from './frameExport';

/**
 * Config values a component other than the settings pane has to react to. The pane sits under
 * TopBar and the flamegraph is its sibling, so a callback would be three hops of prop drilling.
 */
class LiveConfig {
    ringCapacity = $state<number | null>(null);
    autoInstrument = $state<AutoInstrumentProvenance | null>(null);

    setRingCapacity(capacity: number | null | undefined): void {
        this.ringCapacity = capacity ?? null;
    }

    setAutoInstrument(auto: Partial<AutoInstrumentProvenance> | null | undefined): void {
        this.autoInstrument = auto
            ? {
                  enabled: auto.enabled ?? false,
                  filters: auto.filters ?? '',
                  ignore: auto.ignore ?? '',
              }
            : null;
    }
}

export const liveConfig = new LiveConfig();
