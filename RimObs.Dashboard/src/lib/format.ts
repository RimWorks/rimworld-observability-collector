export function ns(value: number): string {
    if (!Number.isFinite(value)) return '-';
    if (value <= 0) return '0';
    if (value < 1_000) return `${value} ns`;
    if (value < 1_000_000) return `${(value / 1_000).toFixed(1)} us`;
    if (value < 1_000_000_000) return `${(value / 1_000_000).toFixed(2)} ms`;
    return `${(value / 1_000_000_000).toFixed(2)} s`;
}

export function bytes(value: number): string {
    if (!Number.isFinite(value)) return '-';
    const abs = Math.abs(value);
    if (abs < 1024) return `${value} B`;
    if (abs < 1024 ** 2) return `${(value / 1024).toFixed(1)} KB`;
    if (abs < 1024 ** 3) return `${(value / 1024 ** 2).toFixed(1)} MB`;
    return `${(value / 1024 ** 3).toFixed(2)} GB`;
}

export function count(value: number): string {
    if (!Number.isFinite(value)) return '-';
    return value.toLocaleString('en-US');
}

export function rate(value: number | null): string {
    if (value === null || Number.isNaN(value)) return '-';
    if (!Number.isFinite(value)) return value > 0 ? '∞' : '-∞';
    return value.toFixed(1);
}

const METRIC_KINDS = ['counter', 'gauge', 'histogram'];
export function metricKind(kind: number): string {
    return METRIC_KINDS[kind] ?? `kind:${kind}`;
}

const PATCH_TYPES = ['all', 'prefix', 'postfix', 'transpiler', 'finalizer', 'reverse'];
export function patchType(kind: number): string {
    return PATCH_TYPES[kind] ?? `type:${kind}`;
}

export function relativeTime(iso: string | null): string {
    if (!iso) return 'never';
    const then = new Date(iso).getTime();
    if (Number.isNaN(then)) return 'unknown';
    const delta = Date.now() - then;
    if (delta < 1_000) return 'just now';
    if (delta < 60_000) return `${Math.floor(delta / 1_000)}s ago`;
    if (delta < 3_600_000) return `${Math.floor(delta / 60_000)}m ago`;
    if (delta < 86_400_000) return `${Math.floor(delta / 3_600_000)}h ago`;
    return `${Math.floor(delta / 86_400_000)}d ago`;
}

const HEAT_GRADES: ReadonlyArray<readonly [minShare: number, grade: number]> = [
    [0.4, 4],
    [0.2, 3],
    [0.1, 2],
    [0.04, 1],
];

export function gradeFromShare(share: number): number {
    for (const [minShare, grade] of HEAT_GRADES) {
        if (share >= minShare) return grade;
    }
    return 0;
}
