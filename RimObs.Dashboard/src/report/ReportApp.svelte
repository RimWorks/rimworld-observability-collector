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
    const data = parseBundle(raw);
    let active = $state('summary');
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
                · collector <code>{data.manifest.collectorVersion}</code>
                · {data.manifest.createdUtc}
            </p>
        </header>
        <nav class="tabs">
            <button class:active={active === 'summary'} onclick={() => (active = 'summary')}
                >Summary</button
            >
            <button class:active={active === 'hotspots'} onclick={() => (active = 'hotspots')}
                >Hotspots</button
            >
            <button class:active={active === 'metrics'} onclick={() => (active = 'metrics')}
                >Custom metrics</button
            >
            <button class:active={active === 'loadorder'} onclick={() => (active = 'loadorder')}
                >Load order</button
            >
            <button class:active={active === 'health'} onclick={() => (active = 'health')}
                >Health</button
            >
            {#if data.hasAllocations}
                <button class:active={active === 'alloc'} onclick={() => (active = 'alloc')}
                    >Allocations</button
                >
            {/if}
            {#if data.hasGcEvents}
                <button class:active={active === 'gc'} onclick={() => (active = 'gc')}>GC</button>
            {/if}
            {#if data.hasPatches}
                <button class:active={active === 'patches'} onclick={() => (active = 'patches')}
                    >Patches</button
                >
            {/if}
            {#if data.hasCallHierarchy}
                <button class:active={active === 'calls'} onclick={() => (active = 'calls')}
                    >Call hierarchy</button
                >
            {/if}
        </nav>
        {#if active === 'summary'}<SummaryTab data={data.sessionSummary} />{/if}
        {#if active === 'hotspots'}<HotspotsTab data={data.hotspots} />{/if}
        {#if active === 'metrics'}<CustomMetricsTab data={data.customMetrics} />{/if}
        {#if active === 'loadorder'}<LoadOrderTab data={data.loadOrder} />{/if}
        {#if active === 'health'}<HealthTab data={data.collectorHealth} />{/if}
        {#if active === 'alloc' && data.allocations}<AllocationsTab data={data.allocations} />{/if}
        {#if active === 'gc' && data.gcEvents}<GcTab data={data.gcEvents} />{/if}
        {#if active === 'patches' && data.patches}<PatchesTab data={data.patches} />{/if}
        {#if active === 'calls' && data.callHierarchy}<CallHierarchyTab
                data={data.callHierarchy}
            />{/if}
    </main>
{/if}

<style>
    .report-root,
    .empty-root {
        font-family: 'Hanken Grotesk', sans-serif;
        max-width: 1280px;
        margin: 0 auto;
        padding: 2rem;
    }
    header {
        margin-bottom: 1.5rem;
    }
    .meta {
        color: #666;
        font-size: 0.9rem;
    }
    .tabs {
        display: flex;
        gap: 0.25rem;
        border-bottom: 1px solid #ddd;
        margin-bottom: 1rem;
        flex-wrap: wrap;
    }
    .tabs button {
        background: none;
        border: none;
        padding: 0.5rem 1rem;
        cursor: pointer;
        border-bottom: 2px solid transparent;
        font: inherit;
        color: #444;
    }
    .tabs button.active {
        border-bottom-color: #2563eb;
        color: #2563eb;
    }
</style>
