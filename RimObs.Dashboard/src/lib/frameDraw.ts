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

const SUBSYSTEM_TOKENS = ['--sub-tick', '--sub-ai', '--sub-render', '--sub-ui'];
const SUBSYSTEMS = ['tick', 'ai', 'render', 'ui'];

export interface DrawTheme {
    background: string;
    collapsed: string;
    inkLight: string;
    inkDark: string;
    hue: Record<string, string>;
    hueNone: string;
    font: string;
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
    };
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

// deeper bars sit lighter, so nesting reads without a border on every quad.
function quadFill(q: Quad, opts: DrawOptions, lifted: boolean): string {
    const lift = Math.min(q.depth, 5) * 0.07 + 0.12 + (lifted ? HOVER_LIFT : 0);
    const base =
        q.count > 1 && q.sectionId < 0
            ? opts.theme.collapsed
            : (opts.theme.hue[opts.subsystem(q) ?? ''] ?? opts.theme.hueNone);
    return lighten(base, lift);
}

function inkOn(fill: string, theme: DrawTheme): string {
    return luminance(fill) > INK_THRESHOLD ? theme.inkDark : theme.inkLight;
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

    ctx.font = theme.font;
    ctx.textBaseline = 'middle';

    const pxPerUs = widthPx / span;
    // the flame font is monospace, so one measurement fits every label.
    const charPx = ctx.measureText('.').width;
    const minLabelPx = charPx * MIN_LABEL_CHARS;
    let labelRow = -1;
    let labelEnd = 0;

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
        const fill = quadFill(q, opts, i === opts.hoverIndex || isFocused);
        ctx.fillStyle = fill;
        ctx.fillRect(x, y, w, ROW_HEIGHT - 1);

        const ink = inkOn(fill, theme);

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

    ctx.restore();
}
