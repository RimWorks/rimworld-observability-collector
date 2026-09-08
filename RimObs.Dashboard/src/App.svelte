<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api, type StatusResponse } from './lib/api';
    import { Resource } from './lib/poll.svelte';
    import { router } from './lib/router.svelte';
    import { userPrefs } from './lib/userPrefs.svelte';
    import Sidebar from './lib/components/Sidebar.svelte';
    import TopBar from './lib/components/TopBar.svelte';
    import Overview from './routes/Overview.svelte';
    import Flamegraph from './routes/Flamegraph.svelte';
    import Comparison from './routes/Comparison.svelte';

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
                {:else if route.id === 'flamegraph'}
                    <Flamegraph />
                {:else if route.id === 'comparison'}
                    <Comparison />
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
    }
</style>
