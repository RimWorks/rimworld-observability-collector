import type { TreeNode } from './frameTree';

export interface SectionMatch {
    id: number;
    name: string;
    subsystem: string | null;
    /** false means the section is registered but has no node in the current frame. */
    sampled: boolean;
}

// case-insensitive substring over the WHOLE catalog, not just what is on screen, so a
// section that never fires still shows up as a distinct "registered, not sampled" result.
export function searchCatalog(
    query: string,
    names: Map<number, { name: string; subsystem: string | null }>,
    sampledIds: ReadonlySet<number>,
): SectionMatch[] {
    const q = query.trim().toLowerCase();
    if (!q) return [];
    const out: SectionMatch[] = [];
    for (const [id, info] of names) {
        if (info.name.toLowerCase().includes(q)) {
            out.push({
                id,
                name: info.name,
                subsystem: info.subsystem,
                sampled: sampledIds.has(id),
            });
        }
    }
    out.sort((a, b) => a.name.localeCompare(b.name));
    return out;
}

export interface MatchOccurrence {
    nodeIndex: number;
    sectionId: number;
    startUs: number;
}

// every node in the window whose section matches, time-ordered so next/prev reads left to right.
export function matchOccurrences(
    nodes: TreeNode[],
    matchIds: ReadonlySet<number>,
): MatchOccurrence[] {
    if (matchIds.size === 0) return [];
    const out: MatchOccurrence[] = [];
    for (let i = 0; i < nodes.length; i++) {
        const n = nodes[i];
        if (matchIds.has(n.sectionId))
            out.push({ nodeIndex: i, sectionId: n.sectionId, startUs: n.startUs });
    }
    out.sort((a, b) => a.startUs - b.startUs);
    return out;
}

export interface MatchCursor {
    sectionId: number;
    startUs: number;
}

// pinned by node identity, not array index: the live window slides under this on every
// poll, so an index would silently re-point at a different node four times a second.
export function stepCursor(
    occurrences: MatchOccurrence[],
    cursor: MatchCursor | null,
    delta: 1 | -1,
): MatchCursor | null {
    if (occurrences.length === 0) return null;
    if (!cursor) {
        const first = delta > 0 ? occurrences[0] : occurrences.at(-1)!;
        return { sectionId: first.sectionId, startUs: first.startUs };
    }
    let idx = occurrences.findIndex(
        (o) => o.sectionId === cursor.sectionId && o.startUs === cursor.startUs,
    );
    if (idx === -1) {
        // the cursor's node scrolled out of the window; land on whichever occurrence is
        // closest in time and step from there.
        idx = 0;
        let best = Infinity;
        for (let i = 0; i < occurrences.length; i++) {
            const d = Math.abs(occurrences[i].startUs - cursor.startUs);
            if (d < best) {
                best = d;
                idx = i;
            }
        }
    } else {
        idx += delta;
    }
    const wrapped = ((idx % occurrences.length) + occurrences.length) % occurrences.length;
    const next = occurrences[wrapped];
    return { sectionId: next.sectionId, startUs: next.startUs };
}

// resolves a stable cursor back to a live node index; -1 once its node has scrolled out.
export function resolveCursorIndex(nodes: TreeNode[], cursor: MatchCursor | null): number {
    if (!cursor) return -1;
    for (let i = 0; i < nodes.length; i++) {
        const n = nodes[i];
        if (n.sectionId === cursor.sectionId && n.startUs === cursor.startUs) return i;
    }
    return -1;
}

// a node is kept if it matches or something beneath it does. nodes emit parent before
// child, so one reverse pass carries a child's keep flag up to its parent.
export function hideMask(nodes: TreeNode[], matchIds: ReadonlySet<number>): Uint8Array {
    const keep = new Uint8Array(nodes.length);
    for (let i = nodes.length - 1; i >= 0; i--) {
        const n = nodes[i];
        if (matchIds.has(n.sectionId) || keep[i] === 1) {
            keep[i] = 1;
            if (n.parentIndex >= 0) keep[n.parentIndex] = 1;
        }
    }
    const hidden = new Uint8Array(nodes.length);
    for (let i = 0; i < nodes.length; i++) hidden[i] = keep[i] === 1 ? 0 : 1;
    return hidden;
}
