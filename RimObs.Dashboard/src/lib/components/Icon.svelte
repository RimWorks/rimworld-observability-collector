<script lang="ts" module>
    const PATHS = {
        gauge: 'M12 14a2 2 0 0 0 1.6-3.2L17 7M4.6 17a9 9 0 1 1 14.8 0',
        flame: 'M12 3c1 3.5 4 4.8 4 8a4 4 0 0 1-8 0c0-1.3.4-2.2 1-3 .2 1 .8 1.6 1.5 1.8C13 8.5 11.5 6 12 3Z',
        tree: 'M12 4v5m0 0H7a2 2 0 0 0-2 2v2m7-4h5a2 2 0 0 1 2 2v2M5 17h2v2H5zM11 17h2v2h-2zM17 17h2v2h-2z',
        memory: 'M5 7h14v10H5zM9 7V5m6 2V5M9 19v-2m6 2v-2M3 10h2m-2 4h2m14-4h2m-2 4h2',
        metric: 'M4 19V5m0 14h16M8 16l3-4 3 2 4-6',
        stack: 'M12 3 3 8l9 5 9-5-9-5ZM3 13l9 5 9-5M3 17.5l9 5 9-5',
        logs: 'M5 4h14v16H5zM8 8h8M8 12h8M8 16h5',
        alert: 'M12 4 2 20h20L12 4Zm0 6v4m0 3v.5',
        compare: 'M12 4v16M7 8l-3 3 3 3M17 8l3 3-3 3',
        cog: 'M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6Zm0-5 1.2 2.3 2.6-.5.4 2.6L19 12l-1.8 1.1.5 2.6-2.6-.5L12 18l-1.1-1.8-2.6.5.5-2.6L5 12l2.3-1.1-.5-2.6 2.6.5L12 4Z',
        dot: 'M12 12h.01',
        chevron: 'm9 6 6 6-6 6',
        external: 'M14 5h5v5m0-5L11 13M9 7H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-3',
        probe: 'M9 3h6v4l2 2v8a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V9l2-2V3Zm3 11v.01M12 10v3',
        download: 'M12 4v11m0 0 4-4m-4 4-4-4M5 19h14',
        upload: 'M12 20V9m0 0 4 4m-4-4-4 4M5 5h14',
    } satisfies Record<string, string>;
    export type IconName = keyof typeof PATHS;
    export { PATHS };
</script>

<script lang="ts">
    let { name, size = 18 }: { name: IconName; size?: number } = $props();
</script>

<svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.6"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
>
    <path d={PATHS[name]} />
</svg>
