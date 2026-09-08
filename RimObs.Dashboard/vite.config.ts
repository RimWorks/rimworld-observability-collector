import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { viteSingleFile } from 'vite-plugin-singlefile';
import { resolve } from 'node:path';

export default defineConfig(({ mode }) => {
    const isReport = mode === 'report';
    return {
        plugins: [svelte(), ...(isReport ? [viteSingleFile()] : [])],
        build: {
            outDir: isReport ? 'dist-report' : 'dist',
            target: 'es2022',
            sourcemap: !isReport,
            emptyOutDir: true,
            rollupOptions: isReport
                ? { input: resolve(__dirname, 'src/report/index.html') }
                : undefined,
        },
        server: {
            // the collector takes 25950, so the dev server takes the next port up
            port: 25951,
            host: '0.0.0.0',
            // point the dev server at whichever collector the game launched, so CSS work
            // hot-reloads instead of needing a rebuild and a relaunch.
            proxy: {
                '/api': {
                    target: process.env.RIMOBS_API ?? 'http://127.0.0.1:25950',
                    changeOrigin: true,
                },
            },
        },
    };
});
