import en from '../i18n/en.json';
import zh from '../i18n/zh.json';
import fr from '../i18n/fr.json';
import es from '../i18n/es.json';
import de from '../i18n/de.json';
import { userPrefs } from './userPrefs.svelte';

type Dict = Record<string, string>;

const dictionaries: Record<string, Dict> = { en, zh, fr, es, de };

export interface Language {
    code: string;
    label: string;
}

export const LANGUAGES: Language[] = [
    { code: 'en', label: 'English' },
    { code: 'zh', label: '中文' },
    { code: 'fr', label: 'Français' },
    { code: 'es', label: 'Español' },
    { code: 'de', label: 'Deutsch' },
];

export function getLang(): string {
    const pref = userPrefs.lang;
    if (pref && dictionaries[pref]) return pref;
    const param = new URLSearchParams(globalThis.location.search).get('lang');
    return param && dictionaries[param] ? param : 'en';
}

export function t(key: string, fallback?: string): string {
    const lang = getLang();
    return dictionaries[lang]?.[key] ?? dictionaries.en[key] ?? fallback ?? key;
}

/**
 * count-aware lookup. n === 1 takes the `<key>.one` form, everything else takes `key`.
 * {n} in the string is replaced with the count.
 */
export function tn(key: string, n: number): string {
    return t(n === 1 ? `${key}.one` : key).replace('{n}', String(n));
}
