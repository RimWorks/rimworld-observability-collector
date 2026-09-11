import { cssVar } from './theme';
import type { Quad } from './frameLayout';
import type { ViewRange } from './frameView';

export const ROW_HEIGHT = 18;
const LABEL_PAD = 4;
const LABEL_GAP = LABEL_PAD * 2;
const MIN_QUAD_PX = 1;
const HOVER_LIFT = 0.15;
const INK_THRESHOLD = 0.16;
// below this a truncated name says nothing, so the bar reads better bare.
const MIN_LABEL_CHARS = 10;
const ELLIPSIS = '\u2026';
const FOCUS_RING_PX = 2;
const GAP_ALPHA = 0.55;
const MATCH_RING_PX = 1.5;
// non-matches read as background noise once a search is active, without losing their shape.
const SEARCH_DIM_ALPHA = 0.32;

const SUBSYSTEM_TOKENS = [
    '--sub-tick',
    '--sub-ai',
    '--sub-render',
    '--sub-ui',
    '--sub-engine',
    '--sub-idle',
];
const SUBSYSTEMS = ['tick', 'ai', 'render', 'ui', 'engine', 'idle'];

export interface DrawTheme {
    background: string;
    collapsed: string;
    inkLight: string;
    inkDark: string;
    hue: Record<string, string>;
    hueNone: string;
    font: string;
    match: string;
    /** alternate lane bands tint with this, Neo's zebra treatment. */
    zebra: string;
    laneLine: string;
}

export interface DrawOptions {
    view: ViewRange;
    widthPx: number;
    heightPx: number;
    dpr: number;
    theme: DrawTheme;
    label: (q: Quad) => string;
    subsystem: (q: Quad) => string | null;
    hoverIndex: number;
    focusIndex: number;
    /** only gaps the ordinals say are missing frames get a band; idle time reads as empty. */
    gaps?: { startUs: number; endUs: number; missing: number }[];
    /** section search: present (even empty) means a query is active and non-matches dim. */
    matchSectionIds?: ReadonlySet<number> | null;
    /** frame-scoped search: a quad outside this span is not a match even if its section is. */
    matchRange?: { startUs: number; endUs: number } | null;
    /** per-lane row counts in draw order; two or more bands get zebra fills and boundaries. */
    laneBands?: { rows: number }[];
    /** frame start times: each edge gets a full-height rule so frames never run together. */
    frameEdgesUs?: number[];
}

export function readTheme(el: Element): DrawTheme {
    const read = (token: string, fallback: string): string => cssVar(el, token) || fallback;
    const hue: Record<string, string> = {};
    for (let i = 0; i < SUBSYSTEMS.length; i++) {
        hue[SUBSYSTEMS[i]] = read(SUBSYSTEM_TOKENS[i], '#5c6b85');
    }
    return {
        background: read('--bg-surface', '#131925'),
        collapsed: read('--border', '#28344a'),
        inkLight: read('--text', '#d4dded'),
        inkDark: read('--bg-void', '#080a0f'),
        hue,
        hueNone: read('--sub-none', '#5c6b85'),
        font: `500 11px ${read('--font-mono', 'monospace')}`,
        match: read('--cyan', '#39c4d4'),
        zebra: withAlpha(read('--bg-elev', '#1a2231'), 0.45),
        laneLine: read('--border-soft', '#1c2434'),
    };
}

function withAlpha(hex: string, alpha: number): string {
    const [r, g, b] = parseHex(hex);
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function parseHex(hex: string): [number, number, number] {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
    return m
        ? [Number.parseInt(m[1], 16), Number.parseInt(m[2], 16), Number.parseInt(m[3], 16)]
        : [128, 128, 128];
}

function lighten(hex: string, lift: number): string {
    const [r, g, b] = parseHex(hex);
    const mix = (c: number): number => Math.round(c + (255 - c) * lift);
    return `rgb(${mix(r)}, ${mix(g)}, ${mix(b)})`;
}

function luminance(rgb: string): number {
    const nums = rgb.match(/\d+(?:\.\d+)?/g) ?? ['0', '0', '0'];
    const lin = nums.slice(0, 3).map((v) => {
        const c = Number(v) / 255;
        return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    });
    return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2];
}

// Neo's ProfilerPalette: 7 baked colors, then 6 generated at HSV(i/6, 0.35, 0.5), and a
// section lands on one by LCG-hashing its id. keeps a section's color stable per session.
const HASHED_COLORS = [
    '#5a78c8',
    '#60a060',
    '#9664be',
    '#54a0b0',
    '#468c96',
    '#bea050',
    '#606068',
    ...[0, 1, 2, 3, 4, 5].map((i) => hsv(i / 6, 0.35, 0.5)),
];

function hsv(h: number, s: number, v: number): string {
    const f = (n: number): number => {
        const k = (n + h * 6) % 6;
        return v - v * s * Math.max(0, Math.min(k, 4 - k, 1));
    };
    const hex = (x: number): string =>
        Math.round(x * 255)
            .toString(16)
            .padStart(2, '0');
    return `#${hex(f(5))}${hex(f(3))}${hex(f(1))}`;
}

export function hashedSectionColor(sectionId: number): string {
    const index = ((sectionId * 1103515245 + 12345) >>> 0) % HASHED_COLORS.length;
    return HASHED_COLORS[index];
}

// deeper bars sit lighter, so nesting reads without a border on every quad. nest, not the
// row: a second band starts over at its own depth 0.
function quadFill(q: Quad, opts: DrawOptions, lifted: boolean): string {
    const lift = Math.min(q.nest ?? q.depth, 5) * 0.07 + 0.12 + (lifted ? HOVER_LIFT : 0);
    const base =
        q.count > 1 && q.sectionId < 0
            ? opts.theme.collapsed
            : (opts.theme.hue[opts.subsystem(q) ?? ''] ?? hashedSectionColor(q.sectionId));
    return lighten(base, lift);
}

function inkOn(fill: string, theme: DrawTheme): string {
    return luminance(fill) > INK_THRESHOLD ? theme.inkDark : theme.inkLight;
}

function drawGapBands(ctx: CanvasRenderingContext2D, opts: DrawOptions, pxPerUs: number): void {
    if (!opts.gaps?.length) return;
    ctx.save();
    ctx.fillStyle = opts.theme.collapsed;
    ctx.globalAlpha = GAP_ALPHA;
    for (const g of opts.gaps) {
        if (g.missing <= 0) continue;
        const x = Math.max((g.startUs - opts.view.startUs) * pxPerUs, 0);
        const right = Math.min((g.endUs - opts.view.startUs) * pxPerUs, opts.widthPx);
        if (right > x) ctx.fillRect(x, 0, Math.max(right - x, MIN_QUAD_PX), opts.heightPx);
    }
    ctx.restore();
}

// zebra fills on alternate bands plus a 1px boundary at each band top, so the eye can tell
// where one thread ends and the next begins without counting rows.
function drawLaneBands(ctx: CanvasRenderingContext2D, opts: DrawOptions): void {
    const bands = opts.laneBands;
    if (!bands || bands.length < 2) return;
    let row = 0;
    for (let i = 0; i < bands.length; i++) {
        const y = row * ROW_HEIGHT;
        if (i % 2 === 1) {
            ctx.fillStyle = opts.theme.zebra;
            ctx.fillRect(0, y, opts.widthPx, bands[i].rows * ROW_HEIGHT);
        }
        if (i > 0) {
            ctx.fillStyle = opts.theme.laneLine;
            ctx.fillRect(0, y, opts.widthPx, 1);
        }
        row += bands[i].rows;
    }
}

export function drawTimeline(
    ctx: CanvasRenderingContext2D,
    quads: Quad[],
    opts: DrawOptions,
): void {
    const { view, widthPx, heightPx, dpr, theme } = opts;
    const span = view.endUs - view.startUs;

    ctx.save();
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, widthPx, heightPx);

    if (span <= 0 || widthPx <= 0 || heightPx <= 0) {
        ctx.restore();
        return;
    }

    const pxPerUs = widthPx / span;
    drawLaneBands(ctx, opts);
    drawGapBands(ctx, opts, pxPerUs);

    ctx.font = theme.font;
    ctx.textBaseline = 'middle';

    // the flame font is monospace, so one measurement fits every label.
    const charPx = ctx.measureText('.').width;
    const minLabelPx = charPx * MIN_LABEL_CHARS;
    let labelRow = -1;
    let labelEnd = 0;
    const matchIds = opts.matchSectionIds;
    const searchActive = matchIds != null;

    for (let i = 0; i < quads.length; i++) {
        const q = quads[i];
        const y = q.depth * ROW_HEIGHT;
        if (y >= heightPx) continue;

        // layoutFrame keeps straddlers, so clip them to the canvas here.
        const x = Math.max((q.startUs - view.startUs) * pxPerUs, 0);
        const right = Math.min((q.endUs - view.startUs) * pxPerUs, widthPx);
        if (right < x) continue;
        const w = Math.max(right - x, MIN_QUAD_PX);

        const isFocused = i === opts.focusIndex;
        const inRange =
            !opts.matchRange ||
            (q.endUs > opts.matchRange.startUs && q.startUs < opts.matchRange.endUs);
        const isMatch = searchActive && inRange && matchIds.has(q.sectionId);
        ctx.globalAlpha = searchActive && !isMatch ? SEARCH_DIM_ALPHA : 1;
        const fill = quadFill(q, opts, i === opts.hoverIndex || isFocused);
        ctx.fillStyle = fill;
        ctx.fillRect(x, y, w, ROW_HEIGHT - 1);

        const ink = inkOn(fill, theme);

        if (isMatch) {
            ctx.strokeStyle = theme.match;
            ctx.lineWidth = MATCH_RING_PX;
            ctx.strokeRect(
                x + MATCH_RING_PX / 2,
                y + MATCH_RING_PX / 2,
                Math.max(w - MATCH_RING_PX, 0),
                ROW_HEIGHT - 1 - MATCH_RING_PX,
            );
        }

        if (isFocused) {
            ctx.strokeStyle = ink;
            ctx.lineWidth = FOCUS_RING_PX;
            ctx.strokeRect(
                x + FOCUS_RING_PX / 2,
                y + FOCUS_RING_PX / 2,
                Math.max(w - FOCUS_RING_PX, 0),
                ROW_HEIGHT - 1 - FOCUS_RING_PX,
            );
        }

        const full = q.count > 1 ? `${opts.label(q)} (${q.count})` : opts.label(q);
        const availPx = w - LABEL_PAD * 2;
        if (availPx < minLabelPx) continue;

        const maxChars = Math.floor(availPx / charPx);
        const text =
            full.length > maxChars ? full.slice(0, Math.max(maxChars - 1, 1)) + ELLIPSIS : full;
        const textWidth = ctx.measureText(text).width;
        if (q.depth === labelRow && x < labelEnd) continue;

        ctx.fillStyle = ink;
        ctx.fillText(text, x + LABEL_PAD, y + ROW_HEIGHT / 2);
        labelRow = q.depth;
        labelEnd = x + LABEL_PAD + textWidth + LABEL_GAP;
    }

    ctx.globalAlpha = 1;
    drawFrameEdges(ctx, opts, pxPerUs);
    ctx.restore();
}

// over the quads on purpose: an under-the-bars rule vanishes wherever a root spans the cut.
function drawFrameEdges(ctx: CanvasRenderingContext2D, opts: DrawOptions, pxPerUs: number): void {
    if (!opts.frameEdgesUs?.length) return;
    ctx.fillStyle = opts.theme.background;
    for (const atUs of opts.frameEdgesUs) {
        const x = (atUs - opts.view.startUs) * pxPerUs;
        if (x <= 0 || x >= opts.widthPx) continue;
        ctx.fillRect(x - 1, 0, 2, opts.heightPx);
    }
}
