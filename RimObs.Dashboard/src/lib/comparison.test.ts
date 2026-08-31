import { describe, it, expect } from 'vitest';
import { signedNs, signedPercent, deltaTone } from './comparison';

describe('signedNs', () => {
    it('renders zero without a sign', () => {
        expect(signedNs(0)).toBe('0');
    });

    it('prefixes positive deltas with +', () => {
        expect(signedNs(1_500)).toBe('+1.5 us');
        expect(signedNs(2_000_000)).toBe('+2.00 ms');
    });

    it('prefixes negative deltas with -', () => {
        expect(signedNs(-1_500)).toBe('-1.5 us');
        expect(signedNs(-500)).toBe('-500 ns');
    });
});

describe('signedPercent', () => {
    it('renders null as em dash placeholder', () => {
        expect(signedPercent(null)).toBe('-');
    });

    it('signs positive values', () => {
        expect(signedPercent(12.34)).toBe('+12.3%');
    });

    it('keeps the native minus for negative values', () => {
        expect(signedPercent(-8)).toBe('-8.0%');
    });
});

describe('deltaTone', () => {
    it('maps each status to a tone', () => {
        expect(deltaTone('added')).toBe('new');
        expect(deltaTone('removed')).toBe('gone');
        expect(deltaTone('regressed')).toBe('up');
        expect(deltaTone('improved')).toBe('down');
        expect(deltaTone('unchanged')).toBe('flat');
    });
});
