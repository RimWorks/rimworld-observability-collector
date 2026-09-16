import '../lib/tokens.css';
import { mount } from 'svelte';
import ReportApp from './ReportApp.svelte';

declare global {
    // eslint-disable-next-line no-var
    var __BUNDLE__: unknown;
}

mount(ReportApp, {
    target: document.getElementById('app')!,
    props: { raw: globalThis.__BUNDLE__ ?? null },
});
