import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { dropWoff1, stripWoff1 } from './dropWoff1';

describe('dashboard build output', () => {
    it('ships no sourcemap', () => {
        const config = readFileSync('vite.config.ts', 'utf8');
        expect(config).toMatch(/sourcemap:\s*false/);
        expect(config).not.toMatch(/sourcemap:\s*(true|!)/);
    });

    it('strips the woff1 fallback from the bundled css', () => {
        expect(
            stripWoff1(
                "src: url(/assets/x.woff2) format('woff2'), url(/assets/x.woff) format('woff');",
            ),
        ).toBe("src: url(/assets/x.woff2) format('woff2');");
        // vite's minifier drops the redundant format() descriptor before we see the css
        expect(stripWoff1('src:url(/assets/x.woff2) format("woff2"),url(/assets/x.woff)')).toBe(
            'src:url(/assets/x.woff2) format("woff2")',
        );
    });

    it('drops woff assets from the bundle', () => {
        const bundle: Record<string, { type: string; source?: unknown }> = {
            'assets/x.woff': { type: 'asset', source: 'bin' },
            'assets/x.woff2': { type: 'asset', source: 'bin' },
            'assets/i.css': { type: 'asset', source: 'src:url(/a/x.woff2),url(/a/x.woff)' },
        };
        dropWoff1.generateBundle(null, bundle);
        expect(Object.keys(bundle)).toEqual(['assets/x.woff2', 'assets/i.css']);
        expect(bundle['assets/i.css'].source).toBe('src:url(/a/x.woff2)');
    });
});
