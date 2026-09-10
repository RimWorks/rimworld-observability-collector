/**
 * Starting patterns for the auto-instrumentation fields. These used to be placeholder text, which
 * showed you the syntax but left you to retype it. They are real defaults now: the first run
 * lands with something that works, and anything you type afterwards wins forever.
 */
export const DEFAULT_FILTERS = 'Assembly-CSharp!Verse.Map::*\nAssembly-CSharp!RimWorld.*::*Tick*';
export const DEFAULT_IGNORE = 'Assembly-CSharp!Verse.Log::*';

const FILTERS_KEY = 'rimobs:autoInstrument.filters';
const IGNORE_KEY = 'rimobs:autoInstrument.ignore';

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

/** The value to seed the field with: what the collector holds, else yours, else the example. */
export function initialFilters(fromCollector: string): string {
    return fromCollector.trim() !== '' ? fromCollector : read(FILTERS_KEY, DEFAULT_FILTERS);
}

export function initialIgnore(fromCollector: string): string {
    return fromCollector.trim() !== '' ? fromCollector : read(IGNORE_KEY, DEFAULT_IGNORE);
}

export function rememberFilters(value: string): void {
    write(FILTERS_KEY, value);
}

export function rememberIgnore(value: string): void {
    write(IGNORE_KEY, value);
}
