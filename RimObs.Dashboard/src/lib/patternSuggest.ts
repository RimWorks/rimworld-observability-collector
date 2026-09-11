import type { MethodDescriptor } from './api';

export interface LineAt {
    start: number;
    end: number;
    line: string;
}

export type Stage =
    | { kind: 'assembly'; fragment: string; bang: string }
    | { kind: 'type'; assembly: string; fragment: string; bang: string }
    | { kind: 'method'; assembly: string; type: string; fragment: string; bang: string };

const MAX_SUGGESTIONS = 10;

/** the line the caret sits on, with its absolute bounds in the text. */
export function currentLine(text: string, caret: number): LineAt {
    const start = text.lastIndexOf('\n', caret - 1) + 1;
    const nextBreak = text.indexOf('\n', caret);
    const end = nextBreak < 0 ? text.length : nextBreak;
    return { start, end, line: text.slice(start, end) };
}

/** which part of Assembly!Type::Method the caret line is on, or null for blanks and comments. */
export function stageFor(line: string): Stage | null {
    let text = line.trim();
    if (text.length === 0 || text.startsWith('#')) return null;
    let bang = '';
    if (text.startsWith('!')) {
        bang = '!';
        text = text.slice(1).trim();
    }

    const bangAt = text.indexOf('!');
    if (bangAt < 0) return { kind: 'assembly', fragment: text, bang };
    const assembly = text.slice(0, bangAt);
    text = text.slice(bangAt + 1);

    const colons = text.indexOf('::');
    if (colons < 0) return { kind: 'type', assembly, fragment: text, bang };
    return {
        kind: 'method',
        assembly,
        type: text.slice(0, colons),
        fragment: text.slice(colons + 2),
        bang,
    };
}

function insensitiveIncludes(haystack: string, needle: string): boolean {
    return haystack.toLowerCase().includes(needle.toLowerCase());
}

function assemblyMatches(assembly: string, wanted: string): boolean {
    return wanted === '*' || wanted === '' || assembly.toLowerCase() === wanted.toLowerCase();
}

/** matching loaded assemblies, bang-terminated so accepting one lands on the type stage. */
export function assemblySuggestions(assemblies: string[], stage: Stage): string[] {
    if (stage.kind !== 'assembly') return [];
    return assemblies
        .filter((a) => insensitiveIncludes(a, stage.fragment))
        .slice(0, MAX_SUGGESTIONS)
        .map((a) => `${stage.bang}${a}!`);
}

/** namespaces first as Ns.* lines, then concrete classes as Type::* lines. */
export function typeSuggestions(results: MethodDescriptor[], stage: Stage): string[] {
    if (stage.kind !== 'type') return [];
    const scoped = results.filter((r) => assemblyMatches(r.assemblyName, stage.assembly));
    const out: string[] = [];
    const seen = new Set<string>();
    const push = (pattern: string) => {
        if (!seen.has(pattern) && out.length < MAX_SUGGESTIONS) {
            seen.add(pattern);
            out.push(pattern);
        }
    };
    for (const r of scoped) {
        const dot = r.typeFullName.lastIndexOf('.');
        if (dot > 0) push(`${stage.bang}${r.assemblyName}!${r.typeFullName.slice(0, dot)}.*`);
    }
    for (const r of scoped) push(`${stage.bang}${r.assemblyName}!${r.typeFullName}::*`);
    return out;
}

/** methods of the chosen type matching the fragment; an empty fragment lists them all. */
export function methodSuggestions(results: MethodDescriptor[], stage: Stage): string[] {
    if (stage.kind !== 'method') return [];
    const out: string[] = [];
    const seen = new Set<string>();
    for (const r of results) {
        if (!assemblyMatches(r.assemblyName, stage.assembly)) continue;
        if (r.typeFullName.toLowerCase() !== stage.type.toLowerCase()) continue;
        if (!insensitiveIncludes(r.methodName, stage.fragment)) continue;
        const pattern = `${stage.bang}${r.assemblyName}!${r.typeFullName}::${r.methodName}`;
        if (seen.has(pattern) || out.length >= MAX_SUGGESTIONS) continue;
        seen.add(pattern);
        out.push(pattern);
    }
    return out;
}

/** replaces the caret's line with the chosen pattern; the caret lands at its end. */
export function applySuggestion(
    text: string,
    at: LineAt,
    suggestion: string,
): { text: string; caret: number } {
    return {
        text: text.slice(0, at.start) + suggestion + text.slice(at.end),
        caret: at.start + suggestion.length,
    };
}
