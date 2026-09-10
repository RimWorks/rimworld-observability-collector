import type { DeltaStatus } from './api';

export function signedNs(value: number): string {
    let sign = '';
    if (value > 0) sign = '+';
    else if (value < 0) sign = '-';
    const abs = Math.abs(value);
    if (abs === 0) return '0';
    if (abs < 1_000) return `${sign}${abs} ns`;
    if (abs < 1_000_000) return `${sign}${(abs / 1_000).toFixed(1)} us`;
    if (abs < 1_000_000_000) return `${sign}${(abs / 1_000_000).toFixed(2)} ms`;
    return `${sign}${(abs / 1_000_000_000).toFixed(2)} s`;
}

export function signedPercent(value: number | null): string {
    if (value === null || Number.isNaN(value)) return '-';
    const sign = value > 0 ? '+' : '';
    return `${sign}${value.toFixed(1)}%`;
}

/**
 * Turns a comparison's baseline totals into the section-keyed map the call tree reads.
 * Session totals only, so it belongs to session scope and never to a single frame.
 */
export function comparisonBaselineUs(
    hotspots: readonly { name: string; base_total_ns: number }[],
    names: ReadonlyMap<number, { name: string }>,
): Map<number, number> {
    // the collector pairs sections by name, so its section id can come from either session.
    const idByName = new Map<string, number>();
    for (const [id, meta] of names) idByName.set(meta.name, id);

    const out = new Map<number, number>();
    for (const h of hotspots) {
        const id = idByName.get(h.name);
        if (id !== undefined) out.set(id, h.base_total_ns / 1000);
    }
    return out;
}

export type DeltaTone = 'up' | 'down' | 'flat' | 'new' | 'gone';

export function deltaTone(status: DeltaStatus): DeltaTone {
    switch (status) {
        case 'added':
            return 'new';
        case 'removed':
            return 'gone';
        case 'regressed':
            return 'up';
        case 'improved':
            return 'down';
        default:
            return 'flat';
    }
}
