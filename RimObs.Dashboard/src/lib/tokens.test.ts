import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const tokensCss = readFileSync('src/lib/tokens.css', 'utf8');

function tokenValues(css: string): Map<string, string> {
    const out = new Map<string, string>();
    for (const [, name, value] of css.matchAll(/(--[a-z0-9-]+):\s*([^;]+);/g)) {
        out.set(name, value.trim());
    }
    return out;
}

function sourceFiles(dir: string): string[] {
    const out: string[] = [];
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const path = join(dir, entry.name);
        if (entry.isDirectory()) out.push(...sourceFiles(path));
        else if (/\.(ts|svelte|css)$/.test(entry.name) && !entry.name.includes('.test.'))
            out.push(path);
    }
    return out;
}

describe('token fallbacks', () => {
    const tokens = tokenValues(tokensCss);

    it('every read(token, fallback) fallback matches tokens.css', () => {
        const drifted: string[] = [];
        for (const file of sourceFiles('src')) {
            const text = readFileSync(file, 'utf8');
            for (const [, name, fallback] of text.matchAll(
                /read\('(--[a-z0-9-]+)',\s*'(#[0-9a-f]{6})'\)/g,
            )) {
                const real = tokens.get(name);
                if (real && real !== fallback)
                    drifted.push(`${file}: ${name} ${fallback} != ${real}`);
            }
        }
        expect(drifted).toEqual([]);
    });

    it('no css var() carries a literal font-scale fallback', () => {
        const offenders: string[] = [];
        for (const file of sourceFiles('src')) {
            const text = readFileSync(file, 'utf8');
            for (const [match] of text.matchAll(/var\(--f(-[a-z]+)?,[^)]*\)/g)) {
                offenders.push(`${file}: ${match}`);
            }
        }
        expect(offenders).toEqual([]);
    });
});

describe('font scale', () => {
    it('tokens.css is in the bundle', () => {
        expect(readFileSync('src/lib/theme.css', 'utf8')).toContain("@import './tokens.css'");
    });

    it('resolves the same inside and outside .profiler', () => {
        const style = document.createElement('style');
        style.textContent = tokensCss;
        document.head.appendChild(style);
        const profiler = document.createElement('div');
        profiler.className = 'profiler';
        document.body.appendChild(profiler);

        // jsdom does not resolve var() or inherit custom properties, so this asserts the two
        // scopes carry the same declaration and that only :root sets --f.
        const root = getComputedStyle(document.documentElement);
        expect(getComputedStyle(profiler).getPropertyValue('--f-small')).toBe(
            root.getPropertyValue('--f-small'),
        );
        expect(root.getPropertyValue('--f')).not.toBe('');
        expect(root.getPropertyValue('--f-small')).toContain('11.5px');
        expect(getComputedStyle(profiler).getPropertyValue('--f')).toBe('');

        profiler.remove();
        style.remove();
    });
});
