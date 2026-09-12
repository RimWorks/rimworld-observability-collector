<script lang="ts">
    import { onMount, onDestroy } from 'svelte';
    import { api, type StatusResponse, type InstrumentationAutoResponse } from './lib/api';
    import { StreamResource } from './lib/stream.svelte';
    import { Resource } from './lib/poll.svelte';
    import { userPrefs } from './lib/userPrefs.svelte';
    import TopBar from './lib/components/TopBar.svelte';
    import Flamegraph from './routes/Flamegraph.svelte';
    import NameSessionPrompt from './lib/components/NameSessionPrompt.svelte';
    import { sessionsStore } from './lib/sessions.svelte';

    const status = new StreamResource<StatusResponse>(
        '/api/v1/stream',
        () => api.status(),
        4000,
        undefined,
        0,
        'status',
    );
    const autoRes = new StreamResource<InstrumentationAutoResponse>(
        '/api/v1/stream',
        () => api.instrumentationAuto(),
        8000,
        undefined,
        0,
        'auto',
    );
    const DISCONNECT_THRESHOLD = 3;
    let hasBeenConnected = $state(false);
    let closeRequested = $state(false);

    onMount(() => {
        status.start();
        autoRes.start();
    });
    onDestroy(() => {
        status.stop();
        autoRes.stop();
    });

    $effect(() => {
        if (status.data != null && !hasBeenConnected) hasBeenConnected = true;
    });

    // a session is one launch of the game, so an unnamed one that just appeared is the moment
    // the user knows what they are about to test. asked once per session, and never again once
    // they turn it off.
    let promptEnabled = $state(true);
    let skipped = $state(new Set<string>());
    let session = $derived(status.data?.session ?? null);
    let needsName = $derived(
        promptEnabled && session != null && (session.name ?? '') === '' && !skipped.has(session.id),
    );

    onMount(async () => {
        try {
            promptEnabled = (await api.config()).session.prompt_for_name;
        } catch {
            promptEnabled = false;
        }
    });

    async function nameSession(name: string): Promise<void> {
        const id = session?.id;
        if (!id) return;
        skipped = new Set(skipped).add(id);
        await sessionsStore.rename(id, name);
        void status.refresh();
    }

    function skipNaming(): void {
        if (session) skipped = new Set(skipped).add(session.id);
    }

    async function disablePrompt(): Promise<void> {
        promptEnabled = false;
        try {
            const config = await api.config();
            config.session.prompt_for_name = false;
            await api.saveConfig(config);
        } catch {
            // the prompt is already off for this page; a failed save only means it returns later.
        }
    }

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
    <TopBar status={status.data} auto={autoRes.data?.auto ?? null} />
    <main class="main" id="main">
        <Flamegraph />
    </main>
</div>

{#if needsName && session}
    <NameSessionPrompt
        sessionId={session.id}
        onName={nameSession}
        onSkip={skipNaming}
        onDisable={disablePrompt}
    />
{/if}

<style>
    .shell {
        display: grid;
        grid-template-rows: var(--topbar-h) 1fr;
        grid-template-areas:
            'topbar'
            'main';
        height: 100vh;
        overflow: hidden;
    }
    .main {
        grid-area: main;
        overflow-y: auto;
    }
</style>
