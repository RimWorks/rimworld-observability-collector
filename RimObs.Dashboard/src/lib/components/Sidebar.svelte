<script lang="ts">
    import { routes, router } from '../router.svelte';
    import Icon from './Icon.svelte';
    import Logo from './Logo.svelte';
    import { t } from '../i18n';
</script>

<aside>
    <div class="brand">
        <div class="glyph"><Logo size={28} /></div>
        <div class="title">
            <strong>RimObs</strong>
            <span>{t('app.subtitle')}</span>
        </div>
    </div>

    <nav>
        {#each routes as r (r.id)}
            <a
                href="#/{r.id}"
                class="item"
                class:active={router.current === r.id}
                aria-current={router.current === r.id ? 'page' : undefined}
                aria-label={t(`nav.${r.id}`, r.title)}
                title={t(`nav.${r.id}`, r.title)}
            >
                <Icon name={r.icon} size={17} />
                <span>{t(`nav.${r.id}`, r.title)}</span>
            </a>
        {/each}
    </nav>
</aside>

<style>
    aside {
        grid-area: sidebar;
        width: var(--sb-w);
        background: var(--bg-surface);
        border-right: 1px solid var(--border-soft);
        display: flex;
        flex-direction: column;
        overflow-y: auto;
    }
    .brand {
        display: flex;
        align-items: center;
        gap: var(--s-3);
        padding: var(--s-4) var(--s-4);
        height: var(--topbar-h);
        border-bottom: 1px solid var(--border-soft);
    }
    .glyph {
        display: grid;
        place-items: center;
        width: 28px;
        height: 28px;
        flex: none;
    }
    .title {
        display: flex;
        flex-direction: column;
        line-height: 1.1;
    }
    .title strong {
        font-family: var(--font-display);
        font-size: 1.05rem;
        letter-spacing: 0.04em;
    }
    .title span {
        font-size: 0.68rem;
        text-transform: uppercase;
        letter-spacing: 0.16em;
        color: var(--text-faint);
    }
    nav {
        padding: var(--s-3) var(--s-2);
        display: flex;
        flex-direction: column;
        gap: 2px;
    }
    .item {
        display: flex;
        align-items: center;
        gap: var(--s-3);
        padding: var(--s-2) var(--s-3);
        border-radius: var(--r-md);
        color: var(--text-dim);
        font-size: 0.88rem;
        font-weight: 500;
        position: relative;
        transition:
            background var(--t-fast) var(--ease-out),
            color var(--t-fast) var(--ease-out);
    }
    .item:hover {
        background: var(--bg-surface);
        color: var(--text);
    }
    .item.active {
        background: var(--bg-elev);
        color: var(--text);
    }
    @media (max-width: 900px) {
        .brand {
            justify-content: center;
            padding: 0;
            gap: 0;
        }
        .title {
            display: none;
        }
        nav {
            padding: var(--s-3) 0;
            align-items: center;
        }
        .item {
            justify-content: center;
            gap: 0;
            padding: var(--s-2);
            width: 40px;
        }
        .item span {
            display: none;
        }
    }
</style>
