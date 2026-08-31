<script lang="ts">
    let { size = 18 }: { size?: number } = $props();
</script>

<svg width={size} height={size} viewBox="0 0 100 100" fill="none" aria-hidden="true">
    <path
        fill="var(--logo-ring, #4a7d6c)"
        fill-rule="evenodd"
        d="M50 2 98 26 98 74 50 98 2 74 2 26Z
           M50 12.56 87.44 31.28 87.44 68.72 50 87.44 12.56 68.72 12.56 31.28Z"
    />
    <polyline
        points="17.36,51.92 32.72,51.92 40.4,30.8 50,73.04 57.68,44.24 65.36,51.92 82.64,51.92"
        stroke="var(--logo-pulse, #9fd4bd)"
        stroke-width="6.72"
        stroke-linecap="round"
        stroke-linejoin="round"
    />
</svg>
