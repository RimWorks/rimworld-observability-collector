import type { StripBar } from './frameStrip';
import { budgetLine, barWidthPx } from './frameStrip';
import { FRAME_BUDGET_US } from './frameCost';

export interface StripTheme {
    background: string;
    bar: string;
    over: string;
    selected: string;
    line: string;
    cut: string;
}

export interface StripDrawOptions {
    widthPx: number;
    heightPx: number;
    dpr: number;
    selectedOrdinal: number | null;
    /** ordinals a pause cut the history at, drawn as a dashed rule */
    cutOrdinals?: readonly number[];
    theme: StripTheme;
}

export function drawStrip(
    ctx: CanvasRenderingContext2D,
    bars: readonly StripBar[],
    opts: StripDrawOptions,
): void {
    const { widthPx: w, heightPx: h, dpr, selectedOrdinal, theme } = opts;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = theme.background;
    ctx.fillRect(0, 0, w, h);

    const bw = barWidthPx(w, bars.length);
    // a sub-pixel gap would swallow the bar, so only inset once bars are wide enough to spare it.
    const inset = bw > 3 ? 1 : 0;
    for (let i = 0; i < bars.length; i++) {
        const bar = bars[i];
        if (bar.height <= 0) continue;
        const barH = Math.max(1, bar.height * h);
        ctx.fillStyle =
            bar.ordinal === selectedOrdinal
                ? theme.selected
                : bar.overBudget
                  ? theme.over
                  : theme.bar;
        ctx.fillRect(i * bw, h - barH, Math.max(1, bw - inset), barH);
    }

    // a pause leaves a hole in the history; mark where it was so the jump is not read as data.
    const cuts = opts.cutOrdinals ?? [];
    if (cuts.length > 0) {
        ctx.save();
        ctx.setLineDash([3, 3]);
        ctx.strokeStyle = theme.cut;
        ctx.lineWidth = 1;
        for (const ordinal of cuts) {
            const index = bars.findIndex((b) => b.ordinal === ordinal);
            if (index < 0) continue;
            const x = Math.min(Math.round(index * bw), w - 1) + 0.5;
            ctx.beginPath();
            ctx.moveTo(x, 0);
            ctx.lineTo(x, h);
            ctx.stroke();
        }
        ctx.restore();
    }

    // budget line last, so it reads on top of the bars it is judging.
    const lineY = h - budgetLine(FRAME_BUDGET_US) * h;
    ctx.strokeStyle = theme.line;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(0, lineY + 0.5);
    ctx.lineTo(w, lineY + 0.5);
    ctx.stroke();
}
