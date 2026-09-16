import { readFileSync } from 'node:fs';
import { describe, it, expect } from 'vitest';

const read = (p: string) => readFileSync(new URL(p, import.meta.url), 'utf8');
const tokens = read('./tokens.css');
const theme = read('./theme.css');
const threadFilter = read('./components/ThreadFilter.svelte');

function token(name: string): string {
    const hex = new RegExp(`${name}:\\s*(#[0-9a-f]{6})`).exec(tokens);
    if (!hex) throw new Error(`${name} is not defined in tokens.css`);
    return hex[1];
}

function luminance(hex: string): number {
    const c = [1, 3, 5]
        .map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
        .map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
}

function ratio(a: string, b: string): number {
    const [x, y] = [luminance(a), luminance(b)];
    return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
}

describe('contrast tokens', () => {
    it('matches a known wcag ratio', () => {
        expect(ratio('#ffffff', '#000000')).toBeCloseTo(21, 5);
    });

    it('clears 3:1 for --border-strong on every surface', () => {
        const border = token('--border-strong');
        for (const surface of ['--bg-base', '--bg-surface', '--bg-surface-2', '--bg-elev']) {
            expect(ratio(border, token(surface))).toBeGreaterThanOrEqual(3);
        }
    });

    it('clears 4.5:1 for --text-faint on every surface', () => {
        const faint = token('--text-faint');
        for (const surface of ['--bg-base', '--bg-surface', '--bg-surface-2', '--bg-elev']) {
            expect(ratio(faint, token(surface))).toBeGreaterThanOrEqual(4.5);
        }
    });

    // controls sit on quiet borders by design (ka, 2026-09-16); the checkbox is the
    // exception because a 20px box with a quiet border disappears into the page.
    it('draws button borders quiet and the checkbox strong', () => {
        expect(theme).toMatch(/button \{[^}]*border: 1px solid var\(--border\)/s);
        expect(theme).toMatch(/input\[type='checkbox'\] \{[^}]*var\(--border-strong\)/s);
    });

    it('keeps the soft divider token untouched', () => {
        expect(token('--border-soft')).toBe('#1e232c');
        expect(token('--border')).toBe('#2a303c');
    });

    // both sit on a tooltip bubble, where --text-ghost reads 3.86:1 against --bg-elev
    it('writes the flamegraph tooltip footnotes in --text-faint', () => {
        const flame = read('../routes/Flamegraph.svelte');
        for (const rule of ['.tiphint', '.spread-foot']) {
            const body = new RegExp(`\\${rule} \\{([^}]*)\\}`).exec(flame)?.[1] ?? '';
            expect(body).toContain('color: var(--text-faint)');
        }
    });

    it('dims a toggled-off thread row with --text-faint, not --text-ghost', () => {
        expect(threadFilter).toMatch(/\.row\.off \{\s*color: var\(--text-faint\);/);
        expect(threadFilter).not.toMatch(/background: var\(--text-ghost\)/);
    });
});
