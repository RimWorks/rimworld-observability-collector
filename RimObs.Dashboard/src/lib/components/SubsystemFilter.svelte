<script module lang="ts">
    export type SubsystemFilterValue = string | null | 'all';

    const NULL_SUBSYSTEM = '__unset__';

    export function readFilterFromUrl(): SubsystemFilterValue {
        const raw = new URLSearchParams(window.location.search).get('subsystem');
        if (raw === null) return 'all';
        if (raw === NULL_SUBSYSTEM) return null;
        return raw;
    }

    export function writeFilterToUrl(filter: SubsystemFilterValue): void {
        const params = new URLSearchParams(window.location.search);
        if (filter === 'all') {
            params.delete('subsystem');
        } else {
            params.set('subsystem', filter === null ? NULL_SUBSYSTEM : filter);
        }
        const qs = params.toString();
        window.history.replaceState(null, '', qs ? `?${qs}` : window.location.pathname);
    }

    export function matchesFilter(
        subsystem: string | null | undefined,
        filter: SubsystemFilterValue,
    ): boolean {
        if (filter === 'all') return true;
        return (subsystem ?? null) === filter;
    }
</script>

<script lang="ts">
    import { t } from '../i18n';

    interface Props {
        items: Array<{ subsystem?: string | null }>;
        filter: SubsystemFilterValue;
    }

    let { items, filter = $bindable() }: Props = $props();

    let subsystems = $derived.by(() => {
        const seen = new Set<string>();
        const result: Array<string | null> = [];
        for (const item of items) {
            const value = item.subsystem ?? null;
            const key = value ?? '\x00';
            if (!seen.has(key)) {
                seen.add(key);
                result.push(value);
            }
        }
        return result;
    });

    function chipLabel(sub: string | null): string {
        return sub === null ? t('sections.filter.unset') : sub;
    }

    function pick(sub: SubsystemFilterValue): void {
        filter = sub;
        writeFilterToUrl(sub);
    }
</script>

{#if items.length > 1}
    <div class="chips" role="group" aria-label={t('sections.filter.label')}>
        <button class="chip" class:active={filter === 'all'} onclick={() => pick('all')}>
            {t('sections.filter.all')}
        </button>
        {#each subsystems as sub (sub ?? '\x00')}
            <button class="chip" class:active={filter === sub} onclick={() => pick(sub)}>
                {chipLabel(sub)}
            </button>
        {/each}
    </div>
{/if}

<style>
    .chips {
        display: flex;
        flex-wrap: wrap;
        gap: var(--s-2);
    }
    .chip {
        padding: var(--s-1) var(--s-3);
        border-radius: 99px;
        border: 1px solid var(--border);
        background: var(--bg-surface);
        color: var(--text-dim);
        font-family: var(--font-ui);
        font-size: 0.78rem;
        cursor: pointer;
        transition:
            background var(--t-fast) var(--ease-out),
            color var(--t-fast) var(--ease-out),
            border-color var(--t-fast) var(--ease-out);
    }
    .chip:hover {
        background: var(--bg-elev);
        color: var(--text);
    }
    .chip.active {
        background: color-mix(in srgb, var(--cyan) 14%, var(--bg-surface));
        border-color: var(--border-strong);
        color: var(--text);
    }
</style>
