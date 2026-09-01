import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { svelteTesting } from '@testing-library/svelte/vite';

export default defineConfig({
    plugins: [svelte({ hot: false }), svelteTesting()],
    test: {
        environment: 'jsdom',
        include: ['src/**/*.{test,spec}.{ts,svelte.ts}'],
        setupFiles: ['./vitest-setup.ts'],
        coverage: {
            provider: 'v8',
            reporter: ['text-summary', 'lcov'],
            include: ['src/**/*.{ts,svelte}'],
            exclude: ['src/**/*.{test,spec}.*', 'src/report/**'],
        },
    },
});
