import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { viteSingleFile } from 'vite-plugin-singlefile';
import { resolve } from 'node:path';

export default defineConfig(({ mode }) => {
    const isReport = mode === 'report';
    return {
        plugins: [svelte(), ...(isReport ? [viteSingleFile()] : [])],
        base: '/',
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
            port: 5173,
            host: '0.0.0.0',
        },
    };
});
