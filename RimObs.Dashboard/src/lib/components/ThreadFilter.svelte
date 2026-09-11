<script lang="ts">
    import type { ThreadLane } from '../api';
    import type { FrameNodes } from '../frameTree';
    import { laneBusyNs, laneLabel, orderLanes, ThreadRole } from '../threadLanes';
    import { t } from '../i18n';
    import { userPrefs } from '../userPrefs.svelte';
    import type { SvelteSet } from 'svelte/reactivity';

    let {
        threads,
        nodes,
        frameNs,
        selected,
    }: {
        threads: ThreadLane[];
        nodes: FrameNodes | null;
        frameNs: number;
        selected: SvelteSet<number>;
    } = $props();

    const WARM = 0.25;
    const HOT = 0.5;

    // while main-thread-only is on the rows mirror the gutter (main only drawn), but they
    // stay clickable: the first click leaves that mode, so this panel owns lane visibility.
    let locked = $derived(userPrefs.mainThreadOnly);

    let rows = $derived(
        orderLanes(threads).map((lane) => {
            const busy = nodes ? laneBusyNs(nodes, lane.id) : 0;
            const share = frameNs > 0 ? busy / frameNs : 0;
            const drawn = locked ? lane.role === ThreadRole.Main : selected.has(lane.id);
            return { lane, share, drawn, pct: Math.min(100, Math.round(share * 1000) / 10) };
        }),
    );

    function toggle(id: number): void {
        if (locked) {
            userPrefs.setMainThreadOnly(false);
            selected.clear();
            for (const row of rows) {
                if (row.lane.role === ThreadRole.Main) selected.add(row.lane.id);
            }
            selected.add(id);
            return;
        }
        if (selected.has(id)) selected.delete(id);
        else selected.add(id);
    }
</script>

<div class="threadfilter">
    <h3>{t('threads.title')}</h3>
    {#each rows as row (row.lane.id)}
        <button
            type="button"
            class="row"
            class:off={!row.drawn}
            aria-pressed={row.drawn}
            title={locked ? t('tip.threads.mainOnly') : undefined}
            onclick={() => toggle(row.lane.id)}
            data-testid="thread-row-{row.lane.id}"
        >
            <i class="dot" class:main={row.lane.role === ThreadRole.Main}></i>
            <span class="name">{laneLabel(row.lane)}</span>
            <span class="track" title={t('threads.busy')}>
                <span
                    class="busy"
                    class:warm={row.share > WARM && row.share <= HOT}
                    class:hot={row.share > HOT}
                    style:width="{Math.min(100, row.share * 100)}%"
                    data-testid="busy-{row.lane.id}"
                ></span>
            </span>
            <span class="pct mono">{row.pct}%</span>
        </button>
    {/each}
</div>

<style>
    .threadfilter {
        display: flex;
        flex-direction: column;
        gap: var(--s-1);
    }
    h3 {
        margin: 0 0 var(--s-1);
        font-size: 0.82rem;
        font-weight: 600;
        color: var(--text-dim);
    }
    .row {
        display: grid;
        grid-template-columns: 10px 9rem 1fr 3.5rem;
        align-items: center;
        gap: var(--s-2);
        width: 100%;
        padding: var(--s-1) var(--s-2);
        border: 0;
        border-radius: 4px;
        background: none;
        color: var(--text);
        font-size: 0.82rem;
        text-align: left;
        cursor: pointer;
    }
    .row:hover {
        background: var(--bg-surface);
    }
    /* ghosting the text keeps the bar readable; opacity would fade the track with it */
    .row.off {
        color: var(--text-ghost);
    }
    .row.off .busy,
    .row.off .dot {
        background: var(--text-ghost);
    }
    .dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: var(--warn);
    }
    .dot.main {
        background: var(--good);
    }
    .name {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .track {
        height: 8px;
        border-radius: 4px;
        background: var(--bg-surface);
        overflow: hidden;
    }
    .busy {
        display: block;
        height: 100%;
        background: var(--good);
    }
    .busy.warm {
        background: var(--warn);
    }
    .busy.hot {
        background: var(--bad);
    }
    .pct {
        text-align: right;
    }
    .mono {
        font-family: var(--font-mono);
    }
</style>
