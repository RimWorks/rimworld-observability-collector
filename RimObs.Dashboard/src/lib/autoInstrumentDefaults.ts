/**
 * Starting patterns for the auto-instrumentation box. Real defaults, not placeholder text:
 * the first run lands with something that works, and anything you type afterwards wins.
 * A leading ! on a line means "never instrument this", and the standing exclusions cover
 * log spam plus the sub-microsecond leaves whose scopes cost more than they measure.
 */
export const DEFAULT_FILTERS = [
    'Assembly-CSharp!Verse.Map::*',
    'Assembly-CSharp!RimWorld.*::*Tick*',
    '!Assembly-CSharp!Verse.Log::*',
    '!*::get_*',
    '!*::set_*',
    '!*::GetHashCode',
    '!*::Equals',
    '!*::ToString',
].join('\n');

const FILTERS_KEY = 'rimobs:autoInstrument.filters';

function read(key: string, fallback: string): string {
    try {
        const stored = localStorage.getItem(key);
        // an empty string is a deliberate "instrument nothing", so it must survive a reload.
        return stored === null ? fallback : stored;
    } catch {
        return fallback;
    }
}

function write(key: string, value: string): void {
    try {
        localStorage.setItem(key, value);
    } catch {
        // private mode or a full quota. the collector still has the value we just sent it.
    }
}

/** folds a legacy ignore list into one filter text, each real line negated with !. */
export function mergePatterns(filters: string, ignore: string): string {
    const negated = ignore
        .split('\n')
        .map((line) => line.trim())
        .filter((line) => line.length > 0 && !line.startsWith('#'))
        .map((line) => (line.startsWith('!') ? line : `!${line}`));
    if (negated.length === 0) return filters;
    return filters.length > 0 ? `${filters}\n${negated.join('\n')}` : negated.join('\n');
}

/** The value to seed the box with: what the collector holds, else yours, else the example. */
export function initialFilters(fromCollector: string, fromCollectorIgnore = ''): string {
    const merged = mergePatterns(fromCollector.trim(), fromCollectorIgnore.trim());
    return merged.trim() !== '' ? merged : read(FILTERS_KEY, DEFAULT_FILTERS);
}

export function rememberFilters(value: string): void {
    write(FILTERS_KEY, value);
}
