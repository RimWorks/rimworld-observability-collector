const STORAGE_KEY = 'rimobs:userPrefs';

export interface PersistedPrefs {
    closeOnDisconnect: boolean;
    lang: string;
    mainThreadOnly: boolean;
}

export const DEFAULT_PREFS: PersistedPrefs = {
    closeOnDisconnect: true,
    lang: '',
    mainThreadOnly: true,
};

function load(): PersistedPrefs {
    if (typeof localStorage === 'undefined') return { ...DEFAULT_PREFS };
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (raw == null) return { ...DEFAULT_PREFS };
        const parsed = JSON.parse(raw) as Partial<PersistedPrefs>;
        return { ...DEFAULT_PREFS, ...parsed };
    } catch {
        return { ...DEFAULT_PREFS };
    }
}

function persist(prefs: PersistedPrefs): void {
    if (typeof localStorage === 'undefined') return;
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs));
    } catch {
        // ignore quota / privacy-mode errors
    }
}

export class UserPrefs {
    closeOnDisconnect = $state<boolean>(DEFAULT_PREFS.closeOnDisconnect);
    lang = $state<string>(DEFAULT_PREFS.lang);
    mainThreadOnly = $state<boolean>(DEFAULT_PREFS.mainThreadOnly);

    constructor() {
        const loaded = load();
        this.closeOnDisconnect = loaded.closeOnDisconnect;
        this.lang = loaded.lang;
        this.mainThreadOnly = loaded.mainThreadOnly;
    }

    private snapshot(): PersistedPrefs {
        return {
            closeOnDisconnect: this.closeOnDisconnect,
            lang: this.lang,
            mainThreadOnly: this.mainThreadOnly,
        };
    }

    setCloseOnDisconnect(value: boolean): void {
        this.closeOnDisconnect = value;
        persist(this.snapshot());
    }

    setLang(value: string): void {
        this.lang = value;
        persist(this.snapshot());
    }

    setMainThreadOnly(value: boolean): void {
        this.mainThreadOnly = value;
        persist(this.snapshot());
    }

    reset(): void {
        this.closeOnDisconnect = DEFAULT_PREFS.closeOnDisconnect;
        this.lang = DEFAULT_PREFS.lang;
        this.mainThreadOnly = DEFAULT_PREFS.mainThreadOnly;
        if (typeof localStorage !== 'undefined') {
            try {
                localStorage.removeItem(STORAGE_KEY);
            } catch {
                // ignore
            }
        }
    }
}

export const userPrefs = new UserPrefs();
