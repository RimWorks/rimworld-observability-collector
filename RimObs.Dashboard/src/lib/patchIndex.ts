import type { PatchConflict } from './api';

/**
 * Which other mods patch the method behind each section, keyed by section name. The collector
 * reports one conflict row per owner per target, so the same owner can appear several times.
 */
export function buildPatchIndex(conflicts: readonly PatchConflict[]): Map<string, string[]> {
    const owners = new Map<string, Set<string>>();
    for (const c of conflicts) {
        if (!c.section || !c.other_owner) continue;
        let set = owners.get(c.section);
        if (set === undefined) {
            set = new Set<string>();
            owners.set(c.section, set);
        }
        set.add(c.other_owner);
    }

    const index = new Map<string, string[]>();
    for (const [section, set] of owners) index.set(section, [...set].sort());
    return index;
}
