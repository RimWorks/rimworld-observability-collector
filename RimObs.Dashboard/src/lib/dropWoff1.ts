// fontsource ships a woff1 fallback next to every woff2, and dist gets embedded in the
// collector once per RID. Strip the fallback from the bundled css and drop the files.
// vite inlines the @import chain inside its own css plugin, so a transform hook never
// sees the fontsource files; generateBundle is the first point where they exist.
const WOFF1_SRC = /,\s*url\([^)]*\.woff\)(\s*format\((['"])woff\2\))?/g;

export function stripWoff1(css: string): string {
    return css.replace(WOFF1_SRC, '');
}

export const dropWoff1 = {
    name: 'rimobs-drop-woff1',
    generateBundle(_options: unknown, bundle: Record<string, { type: string; source?: unknown }>) {
        for (const [name, asset] of Object.entries(bundle)) {
            if (asset.type !== 'asset') continue;
            if (name.endsWith('.woff')) delete bundle[name];
            else if (name.endsWith('.css') && typeof asset.source === 'string') {
                asset.source = stripWoff1(asset.source);
            }
        }
    },
};
