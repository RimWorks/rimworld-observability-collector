import { describe, it, expect, afterEach } from 'vitest';
import { t, getLang, LANGUAGES } from './i18n';
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

describe('patch conflict keys', () => {
    const patchKeys = ['patches.title', 'patches.empty', 'patches.unknown', 'patches.unknown.hint'];

    for (const lang of LANGUAGES) {
        it(`resolves all patch conflict keys for ${lang.code}`, () => {
            userPrefs.setLang(lang.code);
            for (const key of patchKeys) {
                expect(t(key)).not.toBe(key);
            }
        });
    }

    it('no longer names Harmony in the panel title', () => {
        for (const lang of LANGUAGES) {
            userPrefs.setLang(lang.code);
            expect(t('patches.title')).not.toContain('Harmony');
        }
    });
});

describe('exporter settings keys', () => {
    const exporterKeys = [
        'settings.exporters',
        'settings.prometheus',
        'settings.exporter.enabled',
        'settings.exporter.disabled',
        'settings.exporter.endpoint',
        'settings.exporter.last_scrape',
        'settings.exporter.sample_count',
        'settings.exporter.errors',
        'settings.exporter.unavailable',
        'tip.settings.prometheus',
    ];

    for (const lang of LANGUAGES) {
        it(`resolves all exporter keys for ${lang.code}`, () => {
            userPrefs.setLang(lang.code);
            for (const key of exporterKeys) {
                expect(t(key)).not.toBe(key);
            }
        });
    }
});
