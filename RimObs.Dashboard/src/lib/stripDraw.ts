import type { StripBar } from './frameStrip';
import { budgetLine, barWidthPx } from './frameStrip';
import { TICK_BUDGET_US } from './frameCost';

export interface StripTheme {
    background: string;
    bar: string;
    over: string;
    selected: string;
    line: string;
}

export interface StripDrawOptions {
    widthPx: number;
    heightPx: number;
    dpr: number;
    selectedOrdinal: number | null;
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

    // budget line last, so it reads on top of the bars it is judging.
    const lineY = h - budgetLine(TICK_BUDGET_US) * h;
    ctx.strokeStyle = theme.line;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(0, lineY + 0.5);
    ctx.lineTo(w, lineY + 0.5);
    ctx.stroke();
}
