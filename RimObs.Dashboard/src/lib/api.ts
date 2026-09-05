export interface StatusResponse {
    schema_version: number;
    status: string;
    version: string;
    session: SessionInfo | null;
    receive: {
        total_batches: number;
        total_samples: number;
        total_bytes: number;
        last_batch_utc: string | null;
        section_count: number;
        total_gc_events: number;
        total_allocations: number;
        tps: number | null;
        fps: number | null;
        tps_fps_tick: number | null;
    };
    update: {
        available: boolean;
        latest_version: string | null;
        url: string | null;
    };
    exporters: {
        prometheus_enabled: boolean;
        prometheus_port: number;
        otlp_enabled: boolean;
        prometheus_health: {
            total_scrapes: number;
            last_scrape_utc: string | null;
            last_sample_count: number;
            total_errors: number;
            last_error: string | null;
        };
    };
}

export interface Hotspot {
    id: number;
    name: string;
    subsystem: string | null;
    sample_count: number;
    total_ns: number;
    mean_ns: number;
    min_ns: number;
    max_ns: number;
    p50_ns: number;
    p95_ns: number;
    p99_ns: number;
}

export interface HotspotsResponse {
    schema_version: number;
    hotspots: Hotspot[];
}

export interface Section {
    id: number;
    name: string;
    sample_count: number;
    total_ns: number;
    min_ns: number;
    max_ns: number;
    p50_ns: number;
    p95_ns: number;
    p99_ns: number;
}

export interface SectionsResponse {
    schema_version: number;
    sections: Section[];
}

export interface SectionTimeseriesPoint {
    t: number;
    count: number;
    mean_ns: number;
    total_ns: number;
}

export interface SectionTimeseriesResponse {
    schema_version: number;
    id: number;
    name: string;
    bucket_seconds: number;
    points: SectionTimeseriesPoint[];
}

export interface RegistrySection {
    id: number;
    name: string;
    subsystem: string | null;
}

export interface RegistrySectionsResponse {
    schema_version: number;
    sections: RegistrySection[];
}

export interface MetricLabel {
    canonical: string;
    latest_value: number;
    total_sample_count: number;
}

export type MetricKind = 0 | 1 | 2;

export interface Metric {
    id: number;
    name: string;
    kind: MetricKind;
    unit: string;
    labels: MetricLabel[];
}

export interface MetricsResponse {
    schema_version: number;
    total_observations: number;
    metrics: Metric[];
}

export interface GcEvent {
    generation: number;
    pause_type: number;
    heap_before: number;
    heap_after: number;
    duration_micros: number;
    ticks: number;
    allocation_rate_bpm: number;
}

export interface GcResponse {
    schema_version: number;
    total_events: number;
    events: GcEvent[];
}

export interface CallNode {
    id: number;
    name: string;
    call_count: number;
    total_ns: number;
    is_other: boolean;
    children: CallNode[];
}

export interface CallTreeResponse {
    schema_version: number;
    depth_cap: number;
    top_n: number;
    roots: CallNode[];
}

export interface CaptureSummary {
    id: string;
    session_id: string;
    trigger: 'manual' | 'slow_tick';
    status: 'running' | 'finalized';
    started_utc: string;
    stopped_utc: string | null;
    finalize_reason: 'none' | 'user_stopped' | 'time_cap' | 'size_cap' | 'dashboard_closed';
    edge_count: number;
    estimated_bytes: number;
    dropped_samples: number;
    warning: string | null;
    roots: CallNode[];
}

export interface CapturesResponse {
    schema_version: number;
    active_capture_id: string | null;
    captures: CaptureSummary[];
}

export interface CaptureResponse {
    schema_version: number;
    capture: CaptureSummary;
}

export interface LogEntry {
    timestamp: string;
    level: string;
    message: string;
    exception: string | null;
}

export interface LogsResponse {
    count: number;
    entries: LogEntry[];
}

export interface PatchConflict {
    section: string;
    target_method: string;
    other_owner: string;
    patch_type: number;
    priority: number;
    patch_method: string;
}

export interface PatchesResponse {
    schema_version: number;
    conflicts_known: boolean;
    conflicts: PatchConflict[];
}

export interface SessionInfo {
    id: string;
    started_utc: string;
    library_version: string;
    game_version: string;
    is_current: boolean;
}

export interface SessionsResponse {
    schema_version: number;
    sessions: SessionInfo[];
}

export interface CurrentSessionResponse {
    schema_version: number;
    session: SessionInfo;
    receive: StatusResponse['receive'];
}

export interface SessionSummaryResponse {
    schema_version: number;
    session: SessionInfo;
    section_count: number;
    metric_count: number;
    total_batches: number;
    total_samples: number;
    total_bytes: number;
    total_gc_events: number;
    total_allocations: number;
    total_metric_observations: number;
    total_section_ns: number;
    last_batch_utc: string | null;
}

export interface MethodDescriptor {
    typeFullName: string;
    methodName: string;
    signature: string;
    paramTypeFullNames: string[];
    assemblyName: string;
}

export interface InstrumentationSearchResponse {
    schema_version: number;
    results: MethodDescriptor[];
}

export interface InstrumentationPatchEntry {
    id: number;
    typeFullName: string;
    methodName: string;
    paramTypesJoined: string;
    createdUtc: string;
    lastStatus: 'pending' | 'active' | 'stale';
    lastError: string | null;
}

export interface InstrumentationPatchesResponse {
    schema_version: number;
    persisted: InstrumentationPatchEntry[];
    live?: { patchId: number; signature: string; sectionId: number; status: string }[];
}

export interface InstrumentationPatchResult {
    schema_version: number;
    patch: {
        patchId: number;
        sectionId: number;
        sectionName: string;
        status: string;
        errorReason: string | null;
    };
}

export type ExportBundleResult =
    | { kind: 'ok'; blob: Blob }
    | { kind: 'over_cap'; estimatedBytes: number; capBytes: number }
    | { kind: 'error'; message: string };

export interface ExportBundleParams {
    sessionId: string;
    includes: string[];
    force: boolean;
}

export type EstimateBundleResult =
    | { kind: 'ok'; estimatedBytes: number; capBytes: number; exceedsSoftCap: boolean }
    | { kind: 'error'; message: string };

export interface ImportBundleResponse {
    token: string;
    manifest: Record<string, unknown>;
    contents: string[];
}

export class ApiError extends Error {
    constructor(
        public readonly status: number,
        message: string,
    ) {
        super(message);
        this.name = 'ApiError';
    }
}

export interface ComparisonSessionRef {
    id: string;
    library_version: string;
    game_version: string;
    started_utc: string;
}

export interface ComparisonTiming {
    base_total_ns: number;
    head_total_ns: number;
    delta_ns: number;
    delta_percent: number | null;
    base_sample_count: number;
    head_sample_count: number;
    base_mean_ns: number;
    head_mean_ns: number;
    delta_mean_ns: number;
}

export type DeltaStatus = 'added' | 'removed' | 'regressed' | 'improved' | 'unchanged';

export interface SectionDelta {
    id: number;
    name: string;
    owner: string;
    status: DeltaStatus;
    base_total_ns: number;
    head_total_ns: number;
    delta_ns: number;
    delta_percent: number | null;
    base_mean_ns: number;
    head_mean_ns: number;
    likely_regression_candidate: boolean;
}

export interface OwnerDelta {
    owner: string;
    status: DeltaStatus;
    base_total_ns: number;
    head_total_ns: number;
    delta_ns: number;
    delta_percent: number | null;
    likely_regression_candidate: boolean;
}

export interface MetricDelta {
    name: string;
    owner: string;
    kind: number;
    unit: string;
    status: DeltaStatus;
    base_value: number;
    head_value: number;
    delta_value: number;
    delta_percent: number | null;
}

export interface LoadOrderDiff {
    added: string[];
    removed: string[];
    common: string[];
}

export interface ComparisonResponse {
    schema_version: number;
    unit: string;
    disclaimer: string;
    base: ComparisonSessionRef;
    head: ComparisonSessionRef;
    timing: ComparisonTiming;
    hotspots: SectionDelta[];
    mod_costs: OwnerDelta[];
    metrics: MetricDelta[];
    load_order: LoadOrderDiff;
    warnings: string[];
}

async function get<T>(path: string): Promise<T> {
    const res = await fetch(path, { headers: { accept: 'application/json' } });
    if (!res.ok) {
        throw new ApiError(res.status, `${res.status} ${res.statusText}`);
    }
    return (await res.json()) as T;
}

function authHeaders(): Record<string, string> {
    const token = (globalThis as { __RIMOBS_TOKEN__?: string }).__RIMOBS_TOKEN__;
    return token ? { authorization: `Bearer ${token}` } : {};
}

export const api = {
    status: () => get<StatusResponse>('/api/v1/status'),
    hotspots: (limit = 50) =>
        get<HotspotsResponse>(`/api/v1/sessions/current/hotspots?limit=${limit}`),
    sections: () => get<SectionsResponse>('/api/v1/sessions/current/sections'),
    allSections: () => get<RegistrySectionsResponse>('/api/v1/sections'),
    sectionTimeseries: (id: number) =>
        get<SectionTimeseriesResponse>(`/api/v1/sessions/current/sections/${id}/timeseries`),
    metrics: () => get<MetricsResponse>('/api/v1/sessions/current/metrics'),
    gc: (limit = 200) => get<GcResponse>(`/api/v1/sessions/current/gc?limit=${limit}`),
    callTree: (depth = 10, top = 16) =>
        get<CallTreeResponse>(`/api/v1/sessions/current/call_tree?depth=${depth}&top=${top}`),
    logs: (limit = 200, level?: string) => {
        const levelParam = level ? `&level=${encodeURIComponent(level)}` : '';
        return get<LogsResponse>(`/api/v1/logs?limit=${limit}${levelParam}`);
    },
    sessions: () => get<SessionsResponse>('/api/v1/sessions'),
    compareSessions: (base: string, head: string) =>
        get<ComparisonResponse>(
            `/api/v1/sessions/compare?base=${encodeURIComponent(base)}&head=${encodeURIComponent(head)}`,
        ),
    currentSession: () => get<CurrentSessionResponse>('/api/v1/sessions/current'),
    sessionSummary: () => get<SessionSummaryResponse>('/api/v1/sessions/current/summary'),
    patches: () => get<PatchesResponse>('/api/v1/sessions/current/patches'),
    instrumentationSearch: (q: string, limit = 50) =>
        get<InstrumentationSearchResponse>(
            `/api/v1/instrumentation/search?q=${encodeURIComponent(q)}&limit=${limit}`,
        ),
    instrumentationPatches: () =>
        get<InstrumentationPatchesResponse>('/api/v1/instrumentation/patches'),
    instrumentationPatch: async (req: {
        typeFullName: string;
        methodName: string;
        paramTypeFullNames: string[];
    }): Promise<InstrumentationPatchResult> => {
        const res = await fetch('/api/v1/instrumentation/patch', {
            method: 'POST',
            headers: {
                'content-type': 'application/json',
                accept: 'application/json',
                ...authHeaders(),
            },
            body: JSON.stringify(req),
        });
        if (!res.ok) throw new ApiError(res.status, `${res.status} ${res.statusText}`);
        return res.json() as Promise<InstrumentationPatchResult>;
    },
    instrumentationUnpatch: async (id: number) => {
        const res = await fetch(`/api/v1/instrumentation/patches/${id}`, {
            method: 'DELETE',
            headers: authHeaders(),
        });
        if (!res.ok) throw new ApiError(res.status, `${res.status} ${res.statusText}`);
    },
    exportBundle: async (params: ExportBundleParams): Promise<ExportBundleResult> => {
        const res = await fetch('/api/v1/export/bundle', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', ...authHeaders() },
            body: JSON.stringify({
                session_id: params.sessionId,
                include: params.includes,
                force: params.force,
            }),
        });
        if (res.status === 413) {
            const body = await res.json();
            return {
                kind: 'over_cap',
                estimatedBytes: body.estimated_bytes,
                capBytes: body.cap_bytes,
            };
        }
        if (!res.ok) return { kind: 'error', message: `server returned ${res.status}` };
        return { kind: 'ok', blob: await res.blob() };
    },
    estimateBundle: async (
        sessionId: string,
        includes: string[],
    ): Promise<EstimateBundleResult> => {
        const res = await fetch('/api/v1/export/bundle/estimate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', ...authHeaders() },
            body: JSON.stringify({ session_id: sessionId, include: includes, force: false }),
        });
        if (!res.ok) return { kind: 'error', message: `server returned ${res.status}` };
        const body = await res.json();
        return {
            kind: 'ok',
            estimatedBytes: body.estimated_bytes,
            capBytes: body.cap_bytes,
            exceedsSoftCap: body.exceeds_soft_cap,
        };
    },
    importBundle: async (file: File): Promise<ImportBundleResponse> => {
        const form = new FormData();
        form.append('bundle', file);
        const res = await fetch('/api/v1/import/bundle', {
            method: 'POST',
            headers: authHeaders(),
            body: form,
        });
        if (!res.ok) throw new ApiError(res.status, `import failed: ${res.status}`);
        return (await res.json()) as ImportBundleResponse;
    },
    getImportFileUrl: (token: string, name: string): string =>
        `/api/v1/import/bundle/${encodeURIComponent(token)}/file/${encodeURIComponent(name)}`,
    deleteImport: async (token: string): Promise<void> => {
        const res = await fetch(`/api/v1/import/bundle/${encodeURIComponent(token)}`, {
            method: 'DELETE',
            headers: authHeaders(),
        });
        if (!res.ok && res.status !== 404)
            throw new ApiError(res.status, `delete failed: ${res.status}`);
    },
    captures: (tree = false) =>
        get<CapturesResponse>(`/api/v1/sessions/current/captures${tree ? '?tree=true' : ''}`),
    capture: (id: string) =>
        get<CaptureResponse>(`/api/v1/sessions/current/captures?id=${encodeURIComponent(id)}`),
    startCapture: async (): Promise<CaptureResponse> => {
        const res = await fetch('/api/v1/captures/start', {
            method: 'POST',
            headers: { accept: 'application/json', ...authHeaders() },
        });
        if (!res.ok) throw new ApiError(res.status, `${res.status} ${res.statusText}`);
        return res.json() as Promise<CaptureResponse>;
    },
    stopCapture: async (): Promise<CaptureResponse> => {
        const res = await fetch('/api/v1/captures/stop', {
            method: 'POST',
            headers: { accept: 'application/json', ...authHeaders() },
        });
        if (!res.ok) throw new ApiError(res.status, `${res.status} ${res.statusText}`);
        return res.json() as Promise<CaptureResponse>;
    },
};
