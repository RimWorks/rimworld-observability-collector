<script lang="ts">
    import { api } from '../api';
    import {
        currentLine,
        stageFor,
        assemblySuggestions,
        typeSuggestions,
        methodSuggestions,
        applySuggestion,
        type LineAt,
    } from '../patternSuggest';

    let {
        value,
        label,
        testid,
        rows = 3,
        placeholder = '',
        disabled = false,
        debounceMs = 150,
        oninput,
        onblur,
    }: {
        value: string;
        label: string;
        testid: string;
        rows?: number;
        placeholder?: string;
        disabled?: boolean;
        debounceMs?: number;
        oninput: (value: string) => void;
        onblur?: () => void;
    } = $props();

    let el = $state<HTMLTextAreaElement | null>(null);
    let suggestions = $state<string[]>([]);
    let active = $state(-1);
    let at: LineAt | null = null;
    let timer = 0;
    let generation = 0;

    function close(): void {
        suggestions = [];
        active = -1;
        generation++;
    }

    // loaded once per pane: the assembly list only changes when the game restarts.
    let assemblies: string[] | null = null;
    async function loadAssemblies(): Promise<string[]> {
        if (assemblies === null) {
            try {
                assemblies = (await api.instrumentationAssemblies()).assemblies;
            } catch {
                assemblies = [];
            }
        }
        return assemblies;
    }

    function handleInput(e: Event): void {
        const target = e.currentTarget as HTMLTextAreaElement;
        oninput(target.value);
        at = currentLine(target.value, target.selectionStart);
        const stage = stageFor(at.line);
        clearTimeout(timer);
        if (stage === null) {
            close();
            return;
        }
        const mine = ++generation;
        if (stage.kind === 'assembly') {
            void loadAssemblies().then((list) => {
                if (mine !== generation) return;
                suggestions = assemblySuggestions(list, stage);
                active = -1;
            });
            return;
        }
        const q = stage.kind === 'type' ? stage.fragment : stage.type;
        if (q.trim().length < 2) {
            close();
            return;
        }
        timer = window.setTimeout(async () => {
            try {
                const res = await api.instrumentationSearch(q, 30);
                if (mine !== generation) return;
                suggestions =
                    stage.kind === 'type'
                        ? typeSuggestions(res.results, stage)
                        : methodSuggestions(res.results, stage);
                active = -1;
            } catch {
                close();
            }
        }, debounceMs);
    }

    function accept(suggestion: string): void {
        if (!el || !at) return;
        const applied = applySuggestion(el.value, at, suggestion);
        oninput(applied.text);
        el.value = applied.text;
        el.setSelectionRange(applied.caret, applied.caret);
        el.focus();
        close();
    }

    function handleKeydown(e: KeyboardEvent): void {
        if (suggestions.length === 0) return;
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            active = (active + 1) % suggestions.length;
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            active = active <= 0 ? suggestions.length - 1 : active - 1;
        } else if (e.key === 'Enter' && active >= 0) {
            e.preventDefault();
            accept(suggestions[active]);
        } else if (e.key === 'Escape') {
            e.preventDefault();
            close();
        }
    }

    function handleBlur(): void {
        close();
        onblur?.();
    }
</script>

<span class="host">
    <textarea
        bind:this={el}
        class="text mono"
        {rows}
        spellcheck="false"
        {placeholder}
        {disabled}
        {value}
        oninput={handleInput}
        onkeydown={handleKeydown}
        onblur={handleBlur}
        aria-label={label}
        role="combobox"
        aria-expanded={suggestions.length > 0}
        aria-controls="{testid}-suggestions"
        data-testid={testid}></textarea>
    {#if suggestions.length > 0}
        <ul
            class="drop"
            id="{testid}-suggestions"
            role="listbox"
            aria-label={label}
            data-testid="pattern-suggestions"
        >
            {#each suggestions as s, i (s)}
                <li role="option" aria-selected={i === active}>
                    <button
                        type="button"
                        class="mono"
                        class:active={i === active}
                        tabindex="-1"
                        onpointerdown={(e) => {
                            e.preventDefault();
                            accept(s);
                        }}>{s}</button
                    >
                </li>
            {/each}
        </ul>
    {/if}
</span>

<style>
    .host {
        position: relative;
        display: block;
    }
    /* matches SettingsPopover's .text inputs, which cannot style across the component line */
    textarea {
        width: 100%;
        font: inherit;
        font-size: 0.82rem;
        font-family: var(--font-mono);
        color: var(--text);
        background: var(--bg-surface);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        padding: var(--s-1) var(--s-2);
        caret-color: var(--cyan);
        resize: vertical;
        line-height: 1.5;
    }
    textarea:hover:not(:disabled) {
        border-color: var(--border-strong);
    }
    textarea:disabled {
        color: var(--text-ghost);
        cursor: not-allowed;
    }
    textarea::placeholder {
        color: var(--text-faint);
        opacity: 0.75;
    }
    .drop {
        position: absolute;
        top: 100%;
        left: 0;
        right: 0;
        z-index: 10;
        margin: 0;
        padding: 2px;
        list-style: none;
        max-height: 220px;
        overflow-y: auto;
        background: var(--bg-elev);
        border: 1px solid var(--border);
        border-radius: var(--r-sm);
        box-shadow: var(--shadow-2, 0 4px 12px rgb(0 0 0 / 0.4));
    }
    .drop button {
        display: block;
        width: 100%;
        padding: 3px 8px;
        border: 0;
        border-radius: 3px;
        background: none;
        text-align: left;
        font-size: var(--f-small, 11.5px);
        color: var(--text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        cursor: pointer;
    }
    .drop button:hover,
    .drop button.active {
        background: color-mix(in srgb, var(--cyan) 16%, transparent);
        color: var(--cyan-soft, var(--text));
    }
</style>
