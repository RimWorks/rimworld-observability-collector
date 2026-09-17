import { describe, it, expect, afterEach, vi } from 'vitest';
import { t, tn, getLang, LANGUAGES } from './i18n';
import { userPrefs } from './userPrefs.svelte';

function setSearch(search: string) {
    globalThis.history.replaceState({}, '', `/${search}`);
}

afterEach(() => {
    setSearch('');
    userPrefs.setLang('');
});

describe('t', () => {
    it('returns the value for a known key', () => {
        expect(t('app.title')).toBe('RimWorld Observability');
    });

    it('falls back to the provided fallback for an unknown key', () => {
        expect(t('does.not.exist', 'fallback text')).toBe('fallback text');
    });

    it('falls back to the key itself when no fallback is given', () => {
        expect(t('totally.missing')).toBe('totally.missing');
    });

    it('returns the translated value for the active language', () => {
        userPrefs.setLang('fr');
        expect(t('common.retry')).toBe('Réessayer');
    });

    it('falls back to English when a key is missing in the active language', () => {
        userPrefs.setLang('fr');
        expect(t('totally.missing', 'fallback')).toBe('fallback');
    });
});

describe('tn', () => {
    it('uses the singular form at n = 1', () => {
        expect(tn('flamegraph.overFrames', 1)).toBe('over 1 frame');
    });

    it('uses the plural form at n = 2', () => {
        expect(tn('flamegraph.overFrames', 2)).toBe('over 2 frames');
    });

    it('uses the plural form at n = 0', () => {
        expect(tn('flamegraph.overFrames', 0)).toBe('over 0 frames');
    });

    for (const lang of LANGUAGES) {
        it(`resolves both forms for ${lang.code}`, () => {
            userPrefs.setLang(lang.code);
            expect(tn('flamegraph.overFrames', 1)).not.toContain('overFrames');
            expect(tn('flamegraph.overFrames', 2)).not.toContain('overFrames');
        });
    }
});

describe('getLang', () => {
    it.each([
        ['no query param', ''],
        ['a valid ?lang= override', '?lang=en'],
        ['an unknown ?lang=', '?lang=zz'],
    ])('resolves to en with %s', (_label, search) => {
        setSearch(search);
        expect(getLang()).toBe('en');
    });

    it('honours a registered ?lang= for an added language', () => {
        setSearch('?lang=fr');
        expect(getLang()).toBe('fr');
    });

    it('prefers a persisted userPrefs language over the query param', () => {
        setSearch('?lang=fr');
        userPrefs.setLang('de');
        expect(getLang()).toBe('de');
    });

    it('ignores an unknown persisted language and falls back', () => {
        userPrefs.setLang('zz');
        expect(getLang()).toBe('en');
    });

    it('registers all four added languages plus English', () => {
        expect(LANGUAGES.map((l) => l.code)).toEqual(['en', 'zh', 'fr', 'es', 'de']);
    });
});

// t() runs per rendered string, so a URLSearchParams per call is a parse and an allocation on
// a hot path. the parse is cached on the search string it came from.
describe('query parsing', () => {
    it('parses the query once across many lookups', () => {
        setSearch('?lang=fr');
        const real = globalThis.URLSearchParams;
        let built = 0;
        class Counting extends real {
            constructor(init?: string) {
                super(init);
                built++;
            }
        }
        vi.stubGlobal('URLSearchParams', Counting);
        try {
            getLang();
            built = 0;
            for (let i = 0; i < 50; i++) expect(t('common.retry')).toBe('Réessayer');
            expect(built).toBe(0);
        } finally {
            vi.unstubAllGlobals();
        }
    });

    it('still sees a query that changed after the first lookup', () => {
        setSearch('?lang=fr');
        expect(getLang()).toBe('fr');
        setSearch('?lang=de');
        expect(getLang()).toBe('de');
    });
});

describe('by-mod tree keys', () => {
    const modKeys = [
        'tree.group.sections',
        'tree.group.mods',
        'tree.scope.mod',
        'tree.mod.unknown',
        'tree.mod.bundleHint',
    ];

    for (const lang of LANGUAGES) {
        it(`resolves all by-mod keys for ${lang.code}`, () => {
            userPrefs.setLang(lang.code);
            for (const key of modKeys) {
                expect(t(key)).not.toBe(key);
            }
        });
    }
});
