<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api, type StatusResponse } from './lib/api';
    import { Resource } from './lib/poll.svelte';
    import { router } from './lib/router.svelte';
    import { userPrefs } from './lib/userPrefs.svelte';
    import Sidebar from './lib/components/Sidebar.svelte';
    import TopBar from './lib/components/TopBar.svelte';
    import Overview from './routes/Overview.svelte';
    import Hotspots from './routes/Hotspots.svelte';
    import Instrumentation from './routes/Instrumentation.svelte';
    import Sections from './routes/Sections.svelte';
    import CallTree from './routes/CallTree.svelte';
    import Captures from './routes/Captures.svelte';
    import Memory from './routes/Memory.svelte';
    import Metrics from './routes/Metrics.svelte';
    import Patches from './routes/Patches.svelte';
    import Sessions from './routes/Sessions.svelte';
    import Bundle from './routes/Bundle.svelte';
    import Comparison from './routes/Comparison.svelte';
    import Logs from './routes/Logs.svelte';
    import Settings from './routes/Settings.svelte';

    const status = new Resource<StatusResponse>(() => api.status(), 2000);
    const DISCONNECT_THRESHOLD = 3;
    let hasBeenConnected = $state(false);
    let closeRequested = $state(false);

    onMount(() => {
        router.start();
        status.start();
    });
    onDestroy(() => status.stop());

    let route = $derived(router.route);

    $effect(() => {
        if (status.data != null && !hasBeenConnected) hasBeenConnected = true;
    });

    $effect(() => {
        if (
            hasBeenConnected &&
            userPrefs.closeOnDisconnect &&
            status.consecutiveFailures >= DISCONNECT_THRESHOLD &&
            !closeRequested
        ) {
            closeRequested = true;
            window.close();
        }
    });
</script>

<div class="shell">
    <Sidebar />
    <TopBar status={status.data} />
    <main class="main" id="main">
        {#key route.id}
            <div class="view">
                {#if route.id === 'overview'}
                    <Overview status={status.data} />
                {:else if route.id === 'hotspots'}
                    <Hotspots />
                {:else if route.id === 'instrumentation'}
                    <Instrumentation />
                {:else if route.id === 'sections'}
                    <Sections />
                {:else if route.id === 'calltree'}
                    <CallTree />
                {:else if route.id === 'captures'}
                    <Captures />
                {:else if route.id === 'memory'}
                    <Memory />
                {:else if route.id === 'metrics'}
                    <Metrics />
                {:else if route.id === 'patches'}
                    <Patches />
                {:else if route.id === 'sessions'}
                    <Sessions />
                {:else if route.id === 'bundle'}
                    <Bundle />
                {:else if route.id === 'comparison'}
                    <Comparison />
                {:else if route.id === 'logs'}
                    <Logs />
                {:else if route.id === 'settings'}
                    <Settings />
                {/if}
            </div>
        {/key}
    </main>
</div>

<style>
    .shell {
        display: grid;
        grid-template-columns: var(--sb-w) 1fr;
        grid-template-rows: var(--topbar-h) 1fr;
        grid-template-areas:
            'sidebar topbar'
            'sidebar main';
        height: 100vh;
        overflow: hidden;
    }
    .main {
        grid-area: main;
        overflow-y: auto;
        padding: var(--s-5) var(--s-6) var(--s-7);
    }
    .view {
        max-width: 1320px;
        margin: 0 auto;
    }

    @media (max-width: 820px) {
        .main {
            padding: var(--s-4) var(--s-3) var(--s-6);
        }
    }
</style>
