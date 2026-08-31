<script lang="ts">
    import { api, type LogsResponse } from '../lib/api';
    import { Resource } from '../lib/poll.svelte';
    import DataState from '../lib/components/DataState.svelte';
    import { relativeTime } from '../lib/format';
    import { t } from '../lib/i18n';
    import { onMount, onDestroy } from 'svelte';

    const levels = ['', 'Information', 'Warning', 'Error'];
    let level = $state('');

    function levelLabel(l: string): string {
        return l === '' ? t('logs.filter.all') : t(`logs.level.${l.toLowerCase()}`, l);
    }

    const res = new Resource<LogsResponse>(() => api.logs(200, level || undefined), 3000);
    onMount(() => res.start());
    onDestroy(() => res.stop());

    function selectLevel(next: string) {
        level = next;
        res.refresh();
    }

    let entries = $derived(res.data?.entries ?? []);
    function tone(l: string): string {
        const v = l.toLowerCase();
        if (v.startsWith('err') || v.startsWith('fat')) return 'bad';
        if (v.startsWith('warn')) return 'warn';
        if (v.startsWith('deb') || v.startsWith('trace')) return 'faint';
        return 'info';
    }
</script>

<div class="filters">
    {#each levels as l (l)}
        <button class="chip" class:active={level === l} onclick={() => selectLevel(l)}
            >{levelLabel(l)}</button
        >
    {/each}
</div>

<DataState
    state={res.state}
    error={res.error}
    empty={entries.length === 0}
    onretry={() => res.refresh()}
>
    <div class="stream">
        <!-- log entries have no stable id and rows are display-only, so index keying is safe -->
        {#each entries as e, i (i)}
            <div class="line t-{tone(e.level)}">
                <span class="time mono">{relativeTime(e.timestamp)}</span>
                <span class="lvl">{levelLabel(e.level)}</span>
                <span class="msg mono">
                    {e.message}
                    {#if e.exception}<span class="exc">{e.exception}</span>{/if}
                </span>
            </div>
        {/each}
    </div>
</DataState>

<style>
    .filters {
        display: flex;
        gap: var(--s-2);
        margin-bottom: var(--s-3);
    }
    .chip {
        background: var(--bg-surface);
        border: 1px solid var(--border-soft);
        color: var(--text-dim);
        padding: var(--s-1) var(--s-3);
        border-radius: 99px;
        font-size: 0.76rem;
        cursor: pointer;
        transition:
            border-color var(--t-fast) var(--ease-out),
            background var(--t-fast) var(--ease-out),
            color var(--t-fast) var(--ease-out);
    }
    .chip:hover {
        border-color: var(--border);
    }
    .chip.active {
        background: color-mix(in srgb, var(--cyan) 16%, transparent);
        border-color: var(--cyan);
        color: var(--cyan-soft);
    }
    .stream {
        display: flex;
        flex-direction: column;
    }
    .line {
        display: grid;
        grid-template-columns: 72px 90px 1fr;
        gap: var(--s-3);
        padding: var(--s-2) var(--s-2);
        border-bottom: 1px solid var(--border-soft);
        align-items: baseline;
    }
    .line:hover {
        background: var(--bg-surface);
    }
    .time {
        font-size: 0.72rem;
        color: var(--text-faint);
        text-align: right;
        white-space: nowrap;
    }
    .lvl {
        font-size: 0.72rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
    }
    .msg {
        font-size: 0.8rem;
        color: var(--text-dim);
        word-break: break-word;
    }
    .exc {
        display: block;
        margin-top: var(--s-1);
        color: var(--bad);
        white-space: pre-wrap;
        font-size: 0.74rem;
    }
    .t-bad .lvl {
        color: var(--bad);
    }
    .t-warn .lvl {
        color: var(--warn);
    }
    .t-info .lvl {
        color: var(--cyan-soft);
    }
    .t-faint .lvl {
        color: var(--text-faint);
    }
    .t-bad .msg {
        color: var(--text);
    }
</style>
