<script lang="ts">
    import type { Snippet } from 'svelte';

    let {
        text,
        placement = 'top',
        tabindex = 0,
        align = 'start',
        children,
    }: {
        text: string;
        placement?: 'top' | 'bottom' | 'left' | 'right';
        tabindex?: number;
        align?: 'start' | 'end' | 'stretch';
        children: Snippet;
    } = $props();

    const id: string = `tt-${Math.random().toString(36).slice(2, 9)}`;
    let open = $state<boolean>(false);

    function show(): void {
        open = true;
    }
    function hide(): void {
        open = false;
    }
</script>

<!-- svelte-ignore a11y_no_noninteractive_tabindex -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<span
    class="tt-wrap"
    data-align={align}
    {tabindex}
    onmouseenter={show}
    onmouseleave={hide}
    onfocus={show}
    onblur={hide}
    aria-describedby={open ? id : undefined}
>
    {@render children()}
    {#if open}
        <span class="tt-bubble" data-placement={placement} role="tooltip" {id}>
            {text}
        </span>
    {/if}
</span>

<style>
    .tt-wrap {
        position: relative;
        display: inline-flex;
        align-items: center;
        outline: none;
        cursor: help;
    }
    .tt-wrap[data-align='end'] {
        justify-content: flex-end;
        width: 100%;
    }
    .tt-wrap[data-align='stretch'] {
        display: flex;
        width: 100%;
    }
    .tt-wrap:focus-visible {
        outline: 2px solid color-mix(in srgb, var(--cyan) 70%, transparent);
        outline-offset: 2px;
        border-radius: 3px;
    }
    .tt-bubble {
        position: absolute;
        z-index: 50;
        width: max-content;
        max-width: 260px;
        padding: var(--s-2) var(--s-3);
        background: var(--bg-elev);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        box-shadow:
            0 1px 0 rgba(255, 255, 255, 0.03) inset,
            0 10px 28px rgba(0, 0, 0, 0.45);
        font-family: var(--font-ui);
        font-size: 0.78rem;
        font-weight: 400;
        line-height: 1.45;
        letter-spacing: 0;
        text-transform: none;
        color: var(--text);
        white-space: normal;
        pointer-events: none;
        opacity: 0;
        animation: tt-in 120ms var(--ease-out) forwards;
    }
    .tt-bubble[data-placement='top'] {
        bottom: calc(100% + 8px);
        left: 50%;
        transform: translateX(-50%);
    }
    .tt-bubble[data-placement='bottom'] {
        top: calc(100% + 8px);
        left: 50%;
        transform: translateX(-50%);
    }
    .tt-bubble[data-placement='right'] {
        top: 50%;
        left: calc(100% + 8px);
        transform: translateY(-50%);
    }
    .tt-bubble[data-placement='left'] {
        top: 50%;
        right: calc(100% + 8px);
        transform: translateY(-50%);
    }
    @keyframes tt-in {
        from {
            opacity: 0;
        }
        to {
            opacity: 1;
        }
    }
    @media (prefers-reduced-motion: reduce) {
        .tt-bubble {
            animation: none;
            opacity: 1;
        }
    }
</style>
