<script lang="ts">
    import type { Snippet } from 'svelte';
    import { computePosition, autoUpdate, flip, shift, offset } from '@floating-ui/dom';

    let {
        text = '',
        content,
        placement = 'top',
        tabindex,
        align = 'start',
        childFocusable,
        children,
    }: {
        text?: string;
        content?: Snippet;
        placement?: 'top' | 'bottom' | 'left' | 'right';
        tabindex?: number;
        align?: 'start' | 'end' | 'stretch';
        childFocusable?: boolean;
        children: Snippet;
    } = $props();

    const id: string = `tt-${Math.random().toString(36).slice(2, 9)}`;
    const GAP_PX = 8;
    const EDGE_PX = 8;

    let open = $state<boolean>(false);
    let wrapEl = $state<HTMLElement | null>(null);
    let bubbleEl = $state<HTMLElement | null>(null);
    let side = $state<string>('');
    let domFocusable = $state<boolean>(false);
    let stop: (() => void) | null = null;

    const FOCUSABLE =
        'a[href],button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled),summary,[tabindex]:not([tabindex="-1"])';

    function findFocusChild(): Element | null {
        const hit = wrapEl?.querySelector(FOCUSABLE) ?? null;
        return hit != null && hit.closest('.tt-bubble') == null ? hit : null;
    }

    // a wrapped button is already a tab stop; a second one is a keyboard stop that does nothing.
    // A DOM read is not reactive, so a child that flips disabled passes childFocusable instead.
    $effect(() => {
        if (!wrapEl) return;
        domFocusable = findFocusChild() != null;
    });

    const hasFocusChild = $derived(childFocusable ?? domFocusable);
    const wrapTabindex = $derived(tabindex ?? (hasFocusChild ? undefined : 0));

    // ARIA relations do not inherit, so the id has to land on the element that actually takes
    // focus, not on the wrapper around it.
    $effect(() => {
        if (!open || !hasFocusChild) return;
        const el = findFocusChild();
        if (!el) return;
        el.setAttribute('aria-describedby', id);
        return () => el.removeAttribute('aria-describedby');
    });

    // The bubble is fixed so `.main`'s scroll box cannot clip it, and Floating UI flips it to
    // the other side and slides it along the edge when the viewport has no room.
    $effect(() => {
        const anchor = wrapEl;
        const bubble = bubbleEl;
        if (!open || !anchor || !bubble) return;

        stop = autoUpdate(anchor, bubble, () => {
            void computePosition(anchor, bubble, {
                strategy: 'fixed',
                placement,
                middleware: [offset(GAP_PX), flip(), shift({ padding: EDGE_PX })],
            }).then((pos) => {
                bubble.style.left = `${pos.x}px`;
                bubble.style.top = `${pos.y}px`;
                side = pos.placement.split('-')[0];
            });
        });

        return () => {
            stop?.();
            stop = null;
        };
    });

    function show(): void {
        open = true;
    }
    function hide(): void {
        open = false;
    }

    // WCAG 1.4.13: Escape dismisses the tooltip and the event keeps going, so the same press
    // still refits the timeline, clears search, or closes the popover behind it.
    $effect(() => {
        if (!open) return;
        function onEscape(e: KeyboardEvent): void {
            if (e.key === 'Escape') hide();
        }
        globalThis.addEventListener('keydown', onEscape);
        return () => globalThis.removeEventListener('keydown', onEscape);
    });
</script>

<!-- prettier-ignore -->
<!-- svelte-ignore a11y_no_noninteractive_tabindex -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<span
    class="tt-wrap"
    bind:this={wrapEl}
    data-align={align}
    tabindex={wrapTabindex}
    onmouseenter={show}
    onmouseleave={hide}
    onfocus={show}
    onblur={hide}
    onfocusin={show}
    onfocusout={hide}
    aria-describedby={open && !hasFocusChild ? id : undefined}
    >{@render children()}{#if open}<span
            class="tt-bubble"
            bind:this={bubbleEl}
            role="tooltip"
            {id}
            data-side={side || placement}
            >{#if content}{@render content()}{:else}{text}{/if}</span
        >{/if}</span
>

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
        position: fixed;
        top: 0;
        left: 0;
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
        /* fixed at z-50, so any pointer surface here covers the toolbar and eats its clicks */
        pointer-events: none;
        opacity: 0;
        animation: tt-in 120ms var(--ease-out) forwards;
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
