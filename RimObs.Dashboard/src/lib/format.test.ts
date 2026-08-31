import { describe, it, expect, vi, afterEach } from 'vitest';
import {
    ns,
    bytes,
    count,
    rate,
    metricKind,
    patchType,
    relativeTime,
    gradeFromShare,
} from './format';

describe('ns', () => {
    it('renders zero and negatives as 0', () => {
        expect(ns(0)).toBe('0');
        expect(ns(-5)).toBe('0');
    });

    it('keeps sub-microsecond values in ns', () => {
        expect(ns(1)).toBe('1 ns');
        expect(ns(999)).toBe('999 ns');
    });

    it('scales to us, ms, and s at the boundaries', () => {
        expect(ns(1_000)).toBe('1.0 us');
        expect(ns(1_500)).toBe('1.5 us');
        expect(ns(1_000_000)).toBe('1.00 ms');
        expect(ns(2_500_000)).toBe('2.50 ms');
        expect(ns(1_000_000_000)).toBe('1.00 s');
        expect(ns(90_000_000_000)).toBe('90.00 s');
    });
});

describe('bytes', () => {
    it('keeps small values in bytes', () => {
        expect(bytes(0)).toBe('0 B');
        expect(bytes(1023)).toBe('1023 B');
    });

    it('scales through KB, MB, GB', () => {
        expect(bytes(1024)).toBe('1.0 KB');
        expect(bytes(1024 ** 2)).toBe('1.0 MB');
        expect(bytes(1024 ** 3)).toBe('1.00 GB');
    });

    it('handles negative values by magnitude', () => {
        expect(bytes(-2048)).toBe('-2.0 KB');
    });
});

describe('count', () => {
    it('groups thousands', () => {
        expect(count(1000)).toBe('1,000');
        expect(count(1234567)).toBe('1,234,567');
        expect(count(0)).toBe('0');
    });
});

describe('rate', () => {
    it('formats numbers to one decimal', () => {
        expect(rate(0)).toBe('0.0');
        expect(rate(60)).toBe('60.0');
        expect(rate(59.94)).toBe('59.9');
        expect(rate(60.16)).toBe('60.2');
    });

    it('renders null and NaN as dash', () => {
        expect(rate(null)).toBe('-');
        expect(rate(Number.NaN)).toBe('-');
    });

    it('renders infinities with symbols', () => {
        expect(rate(Number.POSITIVE_INFINITY)).toBe('∞');
        expect(rate(Number.NEGATIVE_INFINITY)).toBe('-∞');
    });
});

describe('metricKind', () => {
    it('maps known kinds', () => {
        expect(metricKind(0)).toBe('counter');
        expect(metricKind(1)).toBe('gauge');
        expect(metricKind(2)).toBe('histogram');
    });

    it('falls back for unknown kinds', () => {
        expect(metricKind(7)).toBe('kind:7');
    });
});

describe('patchType', () => {
    it('maps known harmony patch types', () => {
        expect(patchType(0)).toBe('all');
        expect(patchType(1)).toBe('prefix');
        expect(patchType(2)).toBe('postfix');
        expect(patchType(3)).toBe('transpiler');
        expect(patchType(4)).toBe('finalizer');
        expect(patchType(5)).toBe('reverse');
    });

    it('falls back for unknown types', () => {
        expect(patchType(9)).toBe('type:9');
    });
});

describe('relativeTime', () => {
    afterEach(() => vi.useRealTimers());

    it('handles null and invalid input', () => {
        expect(relativeTime(null)).toBe('never');
        expect(relativeTime('not-a-date')).toBe('unknown');
    });

    it('buckets deltas into human units', () => {
        vi.useFakeTimers();
        const now = new Date('2026-05-21T12:00:00Z');
        vi.setSystemTime(now);
        expect(relativeTime('2026-05-21T11:59:59.5Z')).toBe('just now');
        expect(relativeTime('2026-05-21T11:59:30Z')).toBe('30s ago');
        expect(relativeTime('2026-05-21T11:55:00Z')).toBe('5m ago');
        expect(relativeTime('2026-05-21T09:00:00Z')).toBe('3h ago');
        expect(relativeTime('2026-05-19T12:00:00Z')).toBe('2d ago');
    });
});

describe('gradeFromShare', () => {
    it('grades by share thresholds', () => {
        expect(gradeFromShare(0.5)).toBe(4);
        expect(gradeFromShare(0.4)).toBe(4);
        expect(gradeFromShare(0.25)).toBe(3);
        expect(gradeFromShare(0.15)).toBe(2);
        expect(gradeFromShare(0.05)).toBe(1);
        expect(gradeFromShare(0.01)).toBe(0);
    });
});

describe('non-finite input', () => {
    it('renders a dash instead of leaking NaN or Infinity to the UI', () => {
        for (const bad of [NaN, Infinity, -Infinity]) {
            expect(ns(bad)).toBe('-');
            expect(bytes(bad)).toBe('-');
            expect(count(bad)).toBe('-');
        }
    });
});
