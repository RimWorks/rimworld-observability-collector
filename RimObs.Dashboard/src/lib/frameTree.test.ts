import { describe, it, expect } from 'vitest';
import { buildFrameTree, type FrameData } from './frameTree';

// each tuple is [sectionId, parentNodeId, startUs, durUs]. node ids are auto-assigned as
// index + 1, so parentNodeId refers to those. -1 means root. samples arrive in Stop order,
// so a child is listed before its parent, exactly as the wire delivers them.
function frame(
    nodes: Array<[section: number, parentNodeId: number, start: number, dur: number]>,
    startUs = 0,
): FrameData {
    return {
        capture_ordinal: 1,
        start_us: startUs,
        end_us: startUs + 100,
        duration_us: 100,
        node_count: nodes.length,
        nodes: {
            section_ids: nodes.map((n) => n[0]),
            parent_ids: nodes.map((n) => n[1]),
            node_ids: nodes.map((_, i) => i + 1),
            parent_node_ids: nodes.map((n) => n[1]),
            start_us: nodes.map((n) => n[2]),
            dur_us: nodes.map((n) => n[3]),
        },
    };
}

describe('buildFrameTree', () => {
    it('returns nothing for an empty frame', () => {
        expect(buildFrameTree(frame([]))).toEqual({ nodes: [], orphanCount: 0 });
    });

    it('puts a lone node at depth 0 with no parent', () => {
        const { nodes } = buildFrameTree(frame([[7, -1, 0, 50]]));
        expect(nodes).toHaveLength(1);
        expect(nodes[0]).toMatchObject({ sectionId: 7, depth: 0, parentIndex: -1, nodeId: 1 });
    });

    it('nests a child under the node its parent id names', () => {
        const { nodes } = buildFrameTree(
            frame([
                [20, 2, 5, 10],
                [10, -1, 0, 50],
            ]),
        );
        expect(nodes.map((n) => n.sectionId)).toEqual([10, 20]);
        expect(nodes.map((n) => n.depth)).toEqual([0, 1]);
        expect(nodes[1].parentIndex).toBe(0);
    });

    // DoSingleTick runs tickListNormal/Rare/Long.Tick() back to back, so one frame holds three
    // TickList.Tick nodes with one parent SECTION id between them. node ids tell them apart.
    it('keeps three identical siblings apart under one parent', () => {
        const { nodes } = buildFrameTree(
            frame([
                [30, 4, 1, 2],
                [30, 4, 4, 2],
                [30, 4, 7, 2],
                [10, -1, 0, 50],
            ]),
        );
        expect(nodes.map((n) => n.sectionId)).toEqual([10, 30, 30, 30]);
        expect(nodes.map((n) => n.depth)).toEqual([0, 1, 1, 1]);
        expect(nodes.slice(1).map((n) => n.parentIndex)).toEqual([0, 0, 0]);
        expect(nodes.slice(1).map((n) => n.nodeId)).toEqual([1, 2, 3]);
    });

    // the case the section-id array provably cannot answer.
    it('parents each child to its own repeated sibling, not the first one', () => {
        const { nodes } = buildFrameTree(
            frame([
                [20, 2, 1, 2],
                [10, -1, 0, 10],
                [20, 4, 21, 2],
                [10, -1, 20, 10],
            ]),
        );
        expect(nodes.map((n) => n.startUs)).toEqual([0, 1, 20, 21]);
        expect(nodes.map((n) => n.depth)).toEqual([0, 1, 0, 1]);
        expect(nodes[1].parentIndex).toBe(0);
        expect(nodes[3].parentIndex).toBe(2);
    });

    it('handles a section nested inside itself', () => {
        const { nodes } = buildFrameTree(
            frame([
                [42, 2, 2, 4],
                [42, -1, 0, 20],
            ]),
        );
        expect(nodes.map((n) => n.depth)).toEqual([0, 1]);
        expect(nodes[1].parentIndex).toBe(0);
    });

    it('orders a child that shares its parent interval exactly', () => {
        const { nodes } = buildFrameTree(
            frame([
                [20, 2, 5, 0],
                [10, -1, 5, 0],
            ]),
        );
        expect(nodes.map((n) => n.sectionId)).toEqual([10, 20]);
        expect(nodes.map((n) => n.depth)).toEqual([0, 1]);
    });

    it('builds the same tree from shuffled input', () => {
        const ordered = buildFrameTree(
            frame([
                [30, 2, 6, 1],
                [20, 3, 5, 10],
                [10, -1, 0, 50],
            ]),
        );
        expect(ordered.nodes.map((n) => n.depth)).toEqual([0, 1, 2]);
        const shuffled = buildFrameTree(
            frame([
                [10, -1, 0, 50],
                [30, 3, 6, 1],
                [20, 1, 5, 10],
            ]),
        );
        expect(shuffled.nodes.map((n) => n.depth)).toEqual([0, 1, 2]);
        expect(shuffled.orphanCount).toBe(0);
    });

    // MapFrame emits node starts on the session clock, so a frame that began 1100us into the
    // session reports its first node at 1100, not 0. the view range is frame relative.
    it('rebases node starts onto the frame origin', () => {
        const { nodes } = buildFrameTree(
            frame(
                [
                    [20, 2, 1120, 10],
                    [10, -1, 1100, 50],
                ],
                1100,
            ),
        );
        expect(nodes.map((n) => n.startUs)).toEqual([0, 20]);
        expect(nodes.map((n) => n.endUs)).toEqual([50, 30]);
        expect(nodes.map((n) => n.depth)).toEqual([0, 1]);
    });

    it('starts a new root when a node begins after everything open has ended', () => {
        const { nodes } = buildFrameTree(
            frame([
                [10, -1, 0, 5],
                [11, -1, 10, 5],
            ]),
        );
        expect(nodes.map((n) => n.depth)).toEqual([0, 0]);
        expect(nodes.map((n) => n.parentIndex)).toEqual([-1, -1]);
    });

    // UDP is lossy. a dropped datagram takes a whole SectionBatch, so a surviving child can
    // name a parent that never arrived. the frame must still draw, and must admit the loss.
    describe('when a parent node never arrived', () => {
        it('re-parents the orphan to the innermost surviving container', () => {
            const { nodes, orphanCount } = buildFrameTree(
                frame([
                    [30, 99, 6, 1],
                    [10, -1, 0, 50],
                ]),
            );
            expect(orphanCount).toBe(1);
            expect(nodes.map((n) => n.depth)).toEqual([0, 1]);
            expect(nodes[1].parentIndex).toBe(0);
        });

        it('makes the orphan a root when nothing contains it', () => {
            const { nodes, orphanCount } = buildFrameTree(frame([[30, 99, 6, 1]]));
            expect(orphanCount).toBe(1);
            expect(nodes[0]).toMatchObject({ depth: 0, parentIndex: -1 });
        });

        it('counts every orphan, not just the first', () => {
            const { orphanCount } = buildFrameTree(
                frame([
                    [30, 98, 6, 1],
                    [31, 99, 8, 1],
                    [10, -1, 0, 50],
                ]),
            );
            expect(orphanCount).toBe(2);
        });

        it('reports zero orphans when every parent resolves', () => {
            const { orphanCount } = buildFrameTree(
                frame([
                    [20, 2, 5, 10],
                    [10, -1, 0, 50],
                ]),
            );
            expect(orphanCount).toBe(0);
        });
    });
});
