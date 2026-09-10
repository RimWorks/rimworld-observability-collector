import { describe, it, expect } from 'vitest';
import { signedNs, signedPercent, deltaTone, comparisonBaselineUs } from './comparison';

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

describe('comparisonBaselineUs', () => {
    const names = new Map([
        [7, { name: 'Verse.TickManager.DoSingleTick', subsystem: null }],
        [9, { name: 'RimWorld.MapDrawer.MapUpdate', subsystem: null }],
    ]);

    function delta(name: string, id: number, baseTotalNs: number) {
        return { name, id, base_total_ns: baseTotalNs };
    }

    // the collector pairs sections by name and reports whichever session's id it saw first,
    // so an id from the compared session must never key the live tree.
    it('keys by the live section id, not the id the comparison reported', () => {
        const map = comparisonBaselineUs(
            [delta('Verse.TickManager.DoSingleTick', 412, 3_000_000)],
            names,
        );

        expect(map.get(7)).toBe(3000);
        expect(map.has(412)).toBe(false);
    });

    it('converts base totals from ns to us', () => {
        const map = comparisonBaselineUs([delta('RimWorld.MapDrawer.MapUpdate', 9, 1_500)], names);

        expect(map.get(9)).toBe(1.5);
    });

    it('drops sections the live registry does not know', () => {
        const map = comparisonBaselineUs([delta('SomeMod.GoneSection', 3, 900)], names);

        expect(map.size).toBe(0);
    });
});
