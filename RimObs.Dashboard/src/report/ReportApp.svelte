<script lang="ts">
    import { parseBundle } from './lib/parsers';
    import SummaryTab from './lib/sections/SummaryTab.svelte';
    import HotspotsTab from './lib/sections/HotspotsTab.svelte';
    import CustomMetricsTab from './lib/sections/CustomMetricsTab.svelte';
    import LoadOrderTab from './lib/sections/LoadOrderTab.svelte';
    import HealthTab from './lib/sections/HealthTab.svelte';
    import AllocationsTab from './lib/sections/AllocationsTab.svelte';
    import GcTab from './lib/sections/GcTab.svelte';
    import PatchesTab from './lib/sections/PatchesTab.svelte';
    import CallHierarchyTab from './lib/sections/CallHierarchyTab.svelte';

    let { raw }: { raw: unknown } = $props();
    let data = $derived(parseBundle(raw));
    let active = $state('summary');

    let tabs = $derived(
        data
            ? [
                  { id: 'summary', label: 'Summary', show: true },
                  { id: 'hotspots', label: 'Hotspots', show: true },
                  { id: 'metrics', label: 'Custom metrics', show: true },
                  { id: 'loadorder', label: 'Load order', show: true },
                  { id: 'health', label: 'Health', show: true },
                  { id: 'alloc', label: 'Allocations', show: data.hasAllocations },
                  { id: 'gc', label: 'GC', show: data.hasGcEvents },
                  { id: 'patches', label: 'Patches', show: data.hasPatches },
                  { id: 'calls', label: 'Call hierarchy', show: data.hasCallHierarchy },
              ].filter((t) => t.show)
            : [],
    );

    function onTabKey(e: KeyboardEvent) {
        const from = tabs.findIndex((t) => t.id === active);
        const to = { ArrowLeft: from - 1, ArrowRight: from + 1, Home: 0, End: tabs.length - 1 }[
            e.key
        ];
        if (to === undefined) return;

        e.preventDefault();
        active = tabs[(to + tabs.length) % tabs.length].id;
        document.getElementById(`tab-${active}`)?.focus();
    }
</script>

{#if data === null}
    <main class="empty-root">
        <h1>RimObs Diagnostic Report</h1>
        <p>No bundle data found. This file expects <code>window.__BUNDLE__</code> to be set.</p>
    </main>
{:else}
    <main class="report-root">
        <header>
            <h1>RimObs Diagnostic Report</h1>
            <p class="meta">
                session <code>{data.manifest.sessionId}</code>
                | collector <code>{data.manifest.collectorVersion}</code>
                | {data.manifest.createdUtc}
            </p>
        </header>
        <div class="tabs" role="tablist" aria-label="Report sections">
            {#each tabs as tab}
                <button
                    role="tab"
                    id="tab-{tab.id}"
                    aria-controls="panel-{tab.id}"
                    aria-selected={active === tab.id}
                    tabindex={active === tab.id ? 0 : -1}
                    class:active={active === tab.id}
                    onkeydown={onTabKey}
                    onclick={() => (active = tab.id)}>{tab.label}</button
                >
            {/each}
        </div>
        {#each tabs as tab}
            {#if active === tab.id}
                <div
                    role="tabpanel"
                    id="panel-{tab.id}"
                    aria-labelledby="tab-{tab.id}"
                    tabindex="0"
                >
                    {#if tab.id === 'summary'}<SummaryTab data={data.sessionSummary} />{/if}
                    {#if tab.id === 'hotspots'}<HotspotsTab data={data.hotspots} />{/if}
                    {#if tab.id === 'metrics'}<CustomMetricsTab data={data.customMetrics} />{/if}
                    {#if tab.id === 'loadorder'}<LoadOrderTab data={data.loadOrder} />{/if}
                    {#if tab.id === 'health'}<HealthTab data={data.collectorHealth} />{/if}
                    {#if tab.id === 'alloc' && data.allocations}<AllocationsTab
                            data={data.allocations}
                        />{/if}
                    {#if tab.id === 'gc' && data.gcEvents}<GcTab data={data.gcEvents} />{/if}
                    {#if tab.id === 'patches' && data.patches}<PatchesTab
                            data={data.patches}
                        />{/if}
                    {#if tab.id === 'calls' && data.callHierarchy}<CallHierarchyTab
                            data={data.callHierarchy}
                        />{/if}
                </div>
            {/if}
        {/each}
    </main>
{/if}

<style>
    /* the report is one static file with no @fontsource payload, so every family here
       resolves through the token's fallback chain rather than an embedded webfont. */
    :global(body) {
        margin: 0;
        background: var(--bg-base);
        color: var(--text);
        font-family: var(--font-ui);
        font-size: 14px;
        line-height: 1.5;
    }
    .report-root,
    .empty-root {
        font-family: var(--font-ui);
        max-width: 1280px;
        margin: 0 auto;
        padding: var(--s-6);
    }
    h1 {
        font-family: var(--font-display);
        font-weight: 600;
    }
    code {
        font-family: var(--font-mono);
    }
    header {
        margin-bottom: var(--s-5);
    }
    .meta {
        color: var(--text-dim);
        font-size: 0.9rem;
    }
    .tabs {
        display: flex;
        gap: var(--s-1);
        border-bottom: 1px solid var(--border);
        margin-bottom: var(--s-4);
        flex-wrap: wrap;
    }
    .tabs button {
        background: none;
        border: none;
        border-radius: var(--r-sm) var(--r-sm) 0 0;
        padding: var(--s-2) var(--s-4);
        cursor: pointer;
        border-bottom: 2px solid transparent;
        font: inherit;
        color: var(--text-dim);
    }
    .tabs button:hover {
        color: var(--text);
    }
    .tabs button.active {
        background: var(--bg-surface);
        border-bottom-color: var(--cyan);
        color: var(--cyan);
        font-weight: 600;
    }
    .tabs button:focus-visible {
        outline: 2px solid var(--cyan);
        outline-offset: -2px;
    }
</style>
