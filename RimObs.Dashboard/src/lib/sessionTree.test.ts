import { describe, it, expect } from 'vitest';
import type { CallNode } from './api';
import { flattenCallNodes, sessionTotalUs } from './sessionTree';
import { buildTreeRows } from './frameTable';

function node(id: number, totalNs: number, calls: number, children: CallNode[] = []): CallNode {
    return {
        id,
        name: `s${id}`,
        call_count: calls,
        total_ns: totalNs,
        is_other: false,
        children,
    } as CallNode;
}

const NAMES = new Map([
    [1, { name: 'root', subsystem: 'tick' }],
    [2, { name: 'child', subsystem: 'tick' }],
]);

describe('flattenCallNodes', () => {
    it('flattens depth first with parent indices', () => {
        const flat = flattenCallNodes([node(1, 3_000_000, 5, [node(2, 1_000_000, 9)])]);
        expect(flat).toHaveLength(2);
        expect(flat[0].sectionId).toBe(1);
        expect(flat[0].parentIndex).toBe(-1);
        expect(flat[1].sectionId).toBe(2);
        expect(flat[1].parentIndex).toBe(0);
        expect(flat[1].depth).toBe(1);
    });

    it('converts nanoseconds to microseconds', () => {
        const flat = flattenCallNodes([node(1, 3_000_000, 1)]);
        expect(flat[0].durUs).toBe(3000);
        expect(flat[0].endUs).toBe(3000);
    });

    it('lays siblings end to end so the rows never overlap', () => {
        const flat = flattenCallNodes([node(1, 1_000_000, 1), node(2, 2_000_000, 1)]);
        expect(flat[0].startUs).toBe(0);
        expect(flat[1].startUs).toBe(1000);
        expect(flat[1].endUs).toBe(3000);
    });

    it('carries the aggregate call count into the table instead of counting one', () => {
        const flat = flattenCallNodes([node(1, 3_000_000, 72_234)]);
        const rows = buildTreeRows(flat, { names: NAMES, expanded: new Set<string>() });
        expect(rows[0].calls).toBe(72_234);
    });

    it('returns nothing for an empty tree', () => {
        expect(flattenCallNodes([])).toEqual([]);
        expect(sessionTotalUs([])).toBe(0);
    });

    it('sums the roots for the share denominator', () => {
        expect(sessionTotalUs([node(1, 1_000_000, 1), node(2, 3_000_000, 1)])).toBe(4000);
    });
});
