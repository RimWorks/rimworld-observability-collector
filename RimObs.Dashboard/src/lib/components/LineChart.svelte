<script lang="ts">
    import uPlot from 'uplot';
    import 'uplot/dist/uPlot.min.css';
    import { onMount, onDestroy } from 'svelte';
    import { cssVar as readVar, resolveColor as readColor } from '../theme';
    import { t } from '../i18n';

    interface SeriesSpec {
        label: string;
        values: number[];
        stroke: string;
        fill?: string;
    }

    let {
        x,
        series,
        height = 240,
        format = (n: number) => `${n}`,
        xFormat = (n: number) => `${n}`,
    }: {
        x: number[];
        series: SeriesSpec[];
        height?: number;
        format?: (n: number) => string;
        xFormat?: (n: number) => string;
    } = $props();

    let wrap: HTMLDivElement;
    let host: HTMLDivElement;
    let tip: HTMLDivElement;
    let chart: uPlot | null = null;
    let ro: ResizeObserver | null = null;

    function cssVar(name: string): string {
        return readVar(host, name);
    }

    function resolveColor(color: string | undefined): string | undefined {
        return readColor(host, color);
    }

    function data(): uPlot.AlignedData {
        return [x, ...series.map((s) => s.values)] as uPlot.AlignedData;
    }

    function renderTip(u: uPlot, idx: number): void {
        while (tip.firstChild) tip.removeChild(tip.firstChild);

        const xVal: number | null = u.data[0][idx] as number | null;
        if (xVal == null) return;

        const xRow: HTMLDivElement = document.createElement('div');
        xRow.className = 'tt-x';
        xRow.textContent = xFormat(xVal);
        tip.appendChild(xRow);

        for (let i = 1; i < u.data.length; i++) {
            const v: number | null = u.data[i][idx] as number | null;
            if (v == null) continue;
            const spec: SeriesSpec = series[i - 1];
            const dotColor: string = resolveColor(spec.stroke) ?? 'currentColor';

            const row: HTMLDivElement = document.createElement('div');
            row.className = 'tt-row';

            const dot: HTMLSpanElement = document.createElement('span');
            dot.className = 'tt-dot';
            dot.style.background = dotColor;

            const label: HTMLSpanElement = document.createElement('span');
            label.className = 'tt-label';
            label.textContent = spec.label;

            const val: HTMLSpanElement = document.createElement('span');
            val.className = 'tt-val';
            val.textContent = format(v);

            row.appendChild(dot);
            row.appendChild(label);
            row.appendChild(val);
            tip.appendChild(row);
        }
    }

    // the cursor hook fires per pointer move, and every offset* read off the plot forces
    // layout. offsets are relative to the offset parent, so only a resize can stale them.
    let over = { left: 0, top: 0, w: 0, h: 0 };

    function measureOver(u: uPlot): void {
        over = {
            left: u.over.offsetLeft,
            top: u.over.offsetTop,
            w: u.over.offsetWidth,
            h: u.over.offsetHeight,
        };
    }

    // the tooltip DOM and its measured size only change when the hovered index does, so a
    // same-index move is pure arithmetic with no rebuild and no offsetWidth reflow.
    let tipIdx = -1;
    let tipW = 0;
    let tipH = 0;

    function updateTip(u: uPlot): void {
        if (!tip) return;
        const idx: number | null | undefined = u.cursor.idx;
        if (idx == null || idx < 0) {
            tipIdx = -1;
            tip.style.opacity = '0';
            return;
        }
        if (idx !== tipIdx) {
            renderTip(u, idx);
            tipIdx = idx;
            tipW = tip.offsetWidth;
            tipH = tip.offsetHeight;
        }

        if (over.w <= 0) measureOver(u);
        const { left: overLeft, top: overTop, w, h } = over;
        const cx: number = u.cursor.left ?? 0;
        const cy: number = u.cursor.top ?? 0;
        const ttW: number = tipW;
        const ttH: number = tipH;
        const offset: number = 12;
        const flipX: boolean = cx + ttW + offset + 4 > w;
        const flipY: boolean = cy + ttH + offset + 4 > h;
        const left: number = overLeft + (flipX ? cx - ttW - offset : cx + offset);
        const top: number = overTop + (flipY ? cy - ttH - offset : cy + offset);
        tip.style.left = `${left}px`;
        tip.style.top = `${top}px`;
        tip.style.opacity = '1';
    }

    function build(): uPlot {
        const grid = cssVar('--border-soft');
        const axisColor = cssVar('--text-faint');
        const font = `11px ${cssVar('--font-mono') || 'monospace'}`;
        const axis = {
            stroke: axisColor,
            grid: { stroke: grid, width: 1 },
            ticks: { stroke: grid, width: 1 },
            font,
        };
        const opts: uPlot.Options = {
            width: host.clientWidth || 600,
            height,
            scales: { x: { time: false } },
            legend: { show: false },
            cursor: { y: false, points: { size: 6 } },
            axes: [
                { ...axis, values: (_u, vals) => vals.map((v) => xFormat(v)) },
                { ...axis, size: 88, values: (_u, vals) => vals.map((v) => format(v)) },
            ],
            series: [
                { label: 'x', value: (_u, v) => (v == null ? '' : xFormat(v)) },
                ...series.map((s) => ({
                    label: s.label,
                    stroke: resolveColor(s.stroke),
                    fill: resolveColor(s.fill),
                    width: 2,
                    points: { show: false },
                    value: (_u: uPlot, v: number | null) => (v == null ? '' : format(v)),
                })),
            ],
            hooks: {
                setCursor: [(u) => updateTip(u)],
                ready: [(u) => measureOver(u)],
                setSize: [(u) => measureOver(u)],
            },
        };
        return new uPlot(opts, data(), host);
    }

    function hideTip(): void {
        if (tip) tip.style.opacity = '0';
    }

    onMount(() => {
        chart = build();
        ro = new ResizeObserver(() => {
            if (chart) chart.setSize({ width: host.clientWidth, height });
        });
        ro.observe(host);
        host.addEventListener('mouseleave', hideTip);
    });

    onDestroy(() => {
        host?.removeEventListener('mouseleave', hideTip);
        ro?.disconnect();
        chart?.destroy();
        chart = null;
    });

    $effect(() => {
        if (chart) {
            chart.setData(data());
            // fresh data invalidates the tooltip cache or a parked cursor shows stale values.
            tipIdx = -1;
        }
    });

    function describe(s: SeriesSpec): string {
        let min: number = Infinity;
        let max: number = -Infinity;
        let last: number | null = null;
        for (const v of s.values) {
            if (v == null || !Number.isFinite(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
            last = v;
        }
        if (last == null) return t('chart.aria.empty').replace('{label}', s.label);
        return t('chart.aria.series')
            .replace('{label}', s.label)
            .replace('{min}', format(min))
            .replace('{max}', format(max))
            .replace('{last}', format(last));
    }

    let ariaLabel: string = $derived(
        [t('chart.aria'), ...series.map(describe)].join('. ').concat('.'),
    );
</script>

<div class="wrap" bind:this={wrap}>
    <div class="chart" bind:this={host} role="img" aria-label={ariaLabel}></div>
    <div class="tt" bind:this={tip} role="tooltip" aria-hidden="true"></div>
</div>

<style>
    .wrap {
        position: relative;
        width: 100%;
    }
    .chart {
        width: 100%;
    }
    .chart :global(.u-axis) {
        color: var(--text-faint);
    }
    .tt {
        position: absolute;
        top: 0;
        left: 0;
        pointer-events: none;
        opacity: 0;
        transition: opacity 90ms var(--ease-out);
        background: var(--bg-elev);
        border: 1px solid var(--border);
        border-radius: var(--r-md);
        box-shadow:
            0 1px 0 rgba(255, 255, 255, 0.03) inset,
            0 10px 28px rgba(0, 0, 0, 0.45);
        padding: var(--s-2) var(--s-2);
        font-family: var(--font-ui);
        font-size: 0.74rem;
        line-height: 1.4;
        color: var(--text);
        white-space: nowrap;
        z-index: 5;
        min-width: 0;
    }
    .tt :global(.tt-x) {
        font-size: 0.68rem;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--text-faint);
        margin-bottom: var(--s-1);
        font-family: var(--font-mono);
    }
    .tt :global(.tt-row) {
        display: flex;
        align-items: center;
        gap: var(--s-2);
    }
    .tt :global(.tt-dot) {
        width: 8px;
        height: 8px;
        border-radius: 99px;
        flex: none;
    }
    .tt :global(.tt-label) {
        color: var(--text-dim);
        font-size: 0.74rem;
    }
    .tt :global(.tt-val) {
        margin-left: auto;
        color: var(--text);
        font-family: var(--font-mono);
        font-size: 0.78rem;
        font-weight: 500;
    }
    @media (prefers-reduced-motion: reduce) {
        .tt {
            transition: none;
        }
    }
</style>
