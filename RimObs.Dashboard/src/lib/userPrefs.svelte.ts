const STORAGE_KEY = 'rimobs:userPrefs';

export interface PersistedPrefs {
    closeOnDisconnect: boolean;
    lang: string;
    mainThreadOnly: boolean;
    /** flame quad fills: 'gl' renders on the gpu, 'cpu' uses the 2d canvas alone. */
    flameRenderer: 'gl' | 'cpu';
    /** call tree grouping: 'sections' is the flat section tree, 'mods' groups by owning mod. */
    treeGroupMode: 'sections' | 'mods';
}

export const DEFAULT_PREFS: PersistedPrefs = {
    closeOnDisconnect: true,
    lang: '',
    mainThreadOnly: true,
    flameRenderer: 'gl',
    treeGroupMode: 'sections',
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
    flameRenderer = $state<'gl' | 'cpu'>(DEFAULT_PREFS.flameRenderer);
    treeGroupMode = $state<'sections' | 'mods'>(DEFAULT_PREFS.treeGroupMode);

    constructor() {
        const loaded = load();
        this.closeOnDisconnect = loaded.closeOnDisconnect;
        this.lang = loaded.lang;
        this.mainThreadOnly = loaded.mainThreadOnly;
        this.flameRenderer = loaded.flameRenderer;
        this.treeGroupMode = loaded.treeGroupMode;
    }

    private snapshot(): PersistedPrefs {
        return {
            closeOnDisconnect: this.closeOnDisconnect,
            lang: this.lang,
            mainThreadOnly: this.mainThreadOnly,
            flameRenderer: this.flameRenderer,
            treeGroupMode: this.treeGroupMode,
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

    setFlameRenderer(value: 'gl' | 'cpu'): void {
        this.flameRenderer = value;
        persist(this.snapshot());
    }

    setTreeGroupMode(value: 'sections' | 'mods'): void {
        this.treeGroupMode = value;
        persist(this.snapshot());
    }

    reset(): void {
        this.closeOnDisconnect = DEFAULT_PREFS.closeOnDisconnect;
        this.lang = DEFAULT_PREFS.lang;
        this.mainThreadOnly = DEFAULT_PREFS.mainThreadOnly;
        this.flameRenderer = DEFAULT_PREFS.flameRenderer;
        this.treeGroupMode = DEFAULT_PREFS.treeGroupMode;
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
