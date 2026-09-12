import { describe, it, expect, beforeEach } from 'vitest';
import { UserPrefs } from './userPrefs.svelte';

const STORAGE_KEY = 'rimobs:userPrefs';

beforeEach(() => {
    localStorage.clear();
});

describe('UserPrefs', () => {
    it('defaults closeOnDisconnect to true when no prior storage exists', () => {
        const prefs = new UserPrefs();
        expect(prefs.closeOnDisconnect).toBe(true);
    });

    it('reads a persisted false value back from localStorage', () => {
        localStorage.setItem(STORAGE_KEY, JSON.stringify({ closeOnDisconnect: false }));
        const prefs = new UserPrefs();
        expect(prefs.closeOnDisconnect).toBe(false);
    });

    it('persists changes via setCloseOnDisconnect', () => {
        const prefs = new UserPrefs();
        prefs.setCloseOnDisconnect(false);
        expect(prefs.closeOnDisconnect).toBe(false);
        const raw = localStorage.getItem(STORAGE_KEY);
        expect(raw).not.toBeNull();
        expect(JSON.parse(raw!)).toEqual({
            closeOnDisconnect: false,
            lang: '',
            mainThreadOnly: true,
            flameRenderer: 'gl',
        });
    });

    it('defaults lang to empty when no prior storage exists', () => {
        const prefs = new UserPrefs();
        expect(prefs.lang).toBe('');
    });

    it('persists and reads back lang via setLang', () => {
        const prefs = new UserPrefs();
        prefs.setLang('de');
        expect(prefs.lang).toBe('de');
        expect(new UserPrefs().lang).toBe('de');
        expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!)).toEqual({
            closeOnDisconnect: true,
            lang: 'de',
            mainThreadOnly: true,
            flameRenderer: 'gl',
        });
    });

    it('defaults mainThreadOnly to true, and persists it off', () => {
        const prefs = new UserPrefs();
        expect(prefs.mainThreadOnly).toBe(true);
        prefs.setMainThreadOnly(false);
        expect(new UserPrefs().mainThreadOnly).toBe(false);
    });

    it('falls back to defaults when stored JSON is corrupt', () => {
        localStorage.setItem(STORAGE_KEY, '{not-json');
        const prefs = new UserPrefs();
        expect(prefs.closeOnDisconnect).toBe(true);
    });

    it('reset() returns to defaults and clears storage', () => {
        const prefs = new UserPrefs();
        prefs.setCloseOnDisconnect(false);
        prefs.reset();
        expect(prefs.closeOnDisconnect).toBe(true);
        expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    });
});

describe('flame renderer pref', () => {
    it('defaults to gl and persists a cpu switch', () => {
        const prefs = new UserPrefs();
        expect(prefs.flameRenderer).toBe('gl');
        prefs.setFlameRenderer('cpu');
        expect(new UserPrefs().flameRenderer).toBe('cpu');
    });
});
