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
