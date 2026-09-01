// Dev-only fixture server. Serves /api/* from canned RimWorld-shaped data and
// proxies everything else to vite, so the dashboard renders populated with no
// collector and no running game. Usage: node scripts/mock-api.mjs [port] [vitePort]

import { createServer, request as httpRequest } from 'node:http';

const PORT = Number(process.argv[2] ?? 17654);
const VITE_PORT = Number(process.argv[3] ?? 5174);

const SESSION = {
    id: '01JZQK4M8N7P2R5T9V3W6X',
    started_utc: '2026-08-31T14:02:11Z',
    library_version: '2.0.0',
    game_version: '1.6.4519 rev1043',
    is_current: true,
};

const PRIOR_SESSION = {
    id: '01JZQ8B2C4D6E8F0G2H4J6',
    started_utc: '2026-08-30T21:48:03Z',
    library_version: '1.9.2',
    game_version: '1.6.4519 rev1043',
    is_current: false,
};

const RECEIVE = {
    total_batches: 18412,
    total_samples: 2946719,
    total_bytes: 41983221,
    last_batch_utc: '2026-08-31T16:41:52Z',
    section_count: 34,
    total_gc_events: 1187,
    total_allocations: 88421,
    tps: 47.3,
    fps: 59.1,
    tps_fps_tick: 412088,
};

const SECTIONS = [
    ['Verse.MapDrawer.MapMeshDrawerUpdate_First', 11_842_000, 214_903],
    ['Verse.TickManager.DoSingleTick', 4_218_400, 412_088],
    ['Verse.PathFinder.FindPathNow', 2_104_900, 38_411],
    ['Verse.MapTemperature.MapTemperatureTick', 1_402_100, 412_088],
    ['vanillaexpanded.vfecore.HediffCompTick', 986_400, 209_331],
    ['Verse.Map.MapUpdate', 902_700, 214_903],
    ['RimWorld.WeatherManager.WeatherManagerUpdate', 604_800, 214_903],
    ['Verse.Pawn_HealthTracker.HealthTick', 511_200, 412_088],
    ['fluffy.worktab.PriorityRecache', 402_600, 8_142],
    ['Verse.ThingGrid.ThingsAt', 288_300, 1_204_811],
    ['RimWorld.StoreUtility.TryFindBestBetterStorageFor', 201_400, 14_882],
    ['Verse.Sound.SoundStarter.PlayOneShot', 96_800, 42_119],
];

const sectionRows = SECTIONS.map(([name, meanNs, samples], i) => ({
    id: i + 1,
    name,
    sample_count: samples,
    total_ns: meanNs * samples,
    mean_ns: meanNs,
    min_ns: Math.round(meanNs * 0.31),
    max_ns: Math.round(meanNs * 6.4),
    p50_ns: Math.round(meanNs * 0.88),
    p95_ns: Math.round(meanNs * 2.1),
    p99_ns: Math.round(meanNs * 3.7),
}));

const CALL_TREE = [
    {
        id: 1,
        name: 'Verse.Root.Update',
        call_count: 214_903,
        total_ns: 21_004_800_000,
        is_other: false,
        children: [
            {
                id: 2,
                name: 'Verse.MapDrawer.MapMeshDrawerUpdate_First',
                call_count: 214_903,
                total_ns: 12_218_400_000,
                is_other: false,
                children: [
                    {
                        id: 6,
                        name: 'Verse.SectionLayer_Things.Regenerate',
                        call_count: 88_412,
                        total_ns: 7_102_900_000,
                        is_other: false,
                        children: [],
                    },
                    {
                        id: 7,
                        name: 'Verse.SectionLayer_Terrain.Regenerate',
                        call_count: 88_412,
                        total_ns: 3_884_100_000,
                        is_other: false,
                        children: [],
                    },
                    {
                        id: 8,
                        name: 'other',
                        call_count: 38_079,
                        total_ns: 1_231_400_000,
                        is_other: true,
                        children: [],
                    },
                ],
            },
            {
                id: 3,
                name: 'Verse.TickManager.DoSingleTick',
                call_count: 412_088,
                total_ns: 6_418_200_000,
                is_other: false,
                children: [
                    {
                        id: 9,
                        name: 'Verse.Pawn.Tick',
                        call_count: 402_119,
                        total_ns: 3_902_400_000,
                        is_other: false,
                        children: [
                            {
                                id: 11,
                                name: 'Verse.PathFinder.FindPathNow',
                                call_count: 38_411,
                                total_ns: 2_104_900_000,
                                is_other: false,
                                children: [],
                            },
                        ],
                    },
                    {
                        id: 10,
                        name: 'vanillaexpanded.vfecore.HediffCompTick',
                        call_count: 209_331,
                        total_ns: 1_486_400_000,
                        is_other: false,
                        children: [],
                    },
                ],
            },
            {
                id: 4,
                name: 'fluffy.worktab.PriorityRecache',
                call_count: 8_142,
                total_ns: 1_402_600_000,
                is_other: false,
                children: [],
            },
            {
                id: 5,
                name: 'other',
                call_count: 214_903,
                total_ns: 965_600_000,
                is_other: true,
                children: [],
            },
        ],
    },
];

function timeseries(id) {
    const row = sectionRows[id - 1] ?? sectionRows[0];
    const points = [];
    for (let i = 0; i < 90; i++) {
        const wobble = 1 + Math.sin(i / 6) * 0.22 + (i === 61 ? 2.4 : 0);
        const mean = Math.round(row.mean_ns * wobble);
        const count = Math.round(row.sample_count / 90);
        points.push({ t: 1756654931 + i * 10, count, mean_ns: mean, total_ns: mean * count });
    }
    return { schema_version: 1, id: row.id, name: row.name, bucket_seconds: 10, points };
}

function gcEvents(limit) {
    const events = [];
    for (let i = 0; i < Math.min(limit, 180); i++) {
        const gen = i % 17 === 0 ? 2 : i % 5 === 0 ? 1 : 0;
        const before = 214_000_000 + (i % 23) * 3_100_000;
        events.push({
            generation: gen,
            pause_type: gen === 2 ? 1 : 0,
            heap_before: before,
            heap_after: before - (gen + 1) * 18_400_000,
            duration_micros: 420 + gen * 3100 + (i % 7) * 90,
            ticks: 412_088 - i * 214,
            allocation_rate_bpm: 88_400_000 + (i % 11) * 2_400_000,
        });
    }
    return { schema_version: 1, total_events: 1187, events };
}

const LOG_LINES = [
    ['Information', 'Collector listening on http://127.0.0.1:17654', null],
    ['Information', 'Session 01JZQK4M8N7P2R5T9V3W6X opened, library 2.0.0', null],
    ['Information', 'Registered 34 sections from 6 owners', null],
    ['Warning', 'Ring buffer high-water mark 91%, 412 samples dropped', null],
    ['Information', 'Prometheus exporter scraped, 214 samples', null],
    [
        'Error',
        'Patch apply failed for Verse.ThingGrid.ThingsAt',
        'HarmonyLib.HarmonyException: transpiler produced invalid IL\n   at HarmonyLib.PatchFunctions.UpdateWrapper(MethodBase original)',
    ],
    ['Debug', 'Flushed batch 18412 (2214 samples, 48.2 KB)', null],
    ['Information', 'GC gen2 pause 9.4 ms, heap 214 MB -> 158 MB', null],
];

function logs(limit, level) {
    const entries = [];
    for (let i = 0; i < Math.min(limit, 120); i++) {
        const [lvl, message, exception] = LOG_LINES[i % LOG_LINES.length];
        if (level && lvl.toLowerCase() !== level.toLowerCase()) continue;
        entries.push({
            timestamp: new Date(1788194512000 - i * 4200).toISOString(),
            level: lvl,
            message,
            exception,
        });
    }
    return { count: entries.length, entries };
}

const METRICS = {
    schema_version: 1,
    total_observations: 1_204_882,
    metrics: [
        {
            id: 1,
            name: 'rimobs.colonists.count',
            kind: 1,
            unit: 'pawns',
            labels: [
                { canonical: 'faction=player', latest_value: 14, total_sample_count: 412_088 },
            ],
        },
        {
            id: 2,
            name: 'rimobs.tick.duration',
            kind: 2,
            unit: 'ms',
            labels: [
                { canonical: 'map=0', latest_value: 18.4, total_sample_count: 412_088 },
                { canonical: 'map=1', latest_value: 4.1, total_sample_count: 118_402 },
            ],
        },
        {
            id: 3,
            name: 'rimobs.raid.spawned',
            kind: 0,
            unit: 'events',
            labels: [{ canonical: 'threat=big', latest_value: 7, total_sample_count: 7 }],
        },
        {
            id: 4,
            name: 'vfecore.hediff.ticked',
            kind: 0,
            unit: 'calls',
            labels: [
                {
                    canonical: 'owner=vanillaexpanded.vfecore',
                    latest_value: 209_331,
                    total_sample_count: 209_331,
                },
            ],
        },
    ],
};

const PATCHES = {
    schema_version: 1,
    conflicts: [
        {
            section: 'Verse.ThingGrid.ThingsAt',
            target_method: 'Verse.ThingGrid.ThingsAt(IntVec3)',
            other_owner: 'fluffy.worktab',
            patch_type: 1,
            priority: 400,
            patch_method: 'WorkTab.ThingGrid_ThingsAt_Prefix',
        },
        {
            section: 'Verse.TickManager.DoSingleTick',
            target_method: 'Verse.TickManager.DoSingleTick()',
            other_owner: 'rocketman',
            patch_type: 2,
            priority: 800,
            patch_method: 'RocketMan.TickManager_DoSingleTick_Transpiler',
        },
    ],
};

const INSTRUMENTATION_PATCHES = {
    schema_version: 1,
    persisted: [
        {
            id: 1,
            typeFullName: 'Verse.PathFinder',
            methodName: 'FindPathNow',
            paramTypesJoined:
                'IntVec3, LocalTargetInfo, TraverseParms, PathFinderCostTuning?, PathEndMode, IPathGridCustomizer',
            createdUtc: '2026-08-31T14:08:44Z',
            lastStatus: 'active',
            lastError: null,
        },
        {
            id: 2,
            typeFullName: 'Verse.ThingGrid',
            methodName: 'ThingsAt',
            paramTypesJoined: 'IntVec3',
            createdUtc: '2026-08-31T15:22:10Z',
            lastStatus: 'stale',
            lastError: 'transpiler produced invalid IL',
        },
    ],
    live: [
        { patchId: 1, signature: 'Verse.PathFinder.FindPathNow', sectionId: 3, status: 'active' },
    ],
};

const SEARCH_RESULTS = [
    {
        typeFullName: 'Verse.PathFinder',
        methodName: 'FindPathNow',
        signature:
            'PawnPath FindPathNow(IntVec3 start, LocalTargetInfo target, Pawn pawn, PathFinderCostTuning? tuning, PathEndMode peMode)',
        paramTypeFullNames: [
            'Verse.IntVec3',
            'Verse.LocalTargetInfo',
            'Verse.Pawn',
            'Verse.PathFinderCostTuning',
            'Verse.AI.PathEndMode',
        ],
        assemblyName: 'Assembly-CSharp',
    },
    {
        typeFullName: 'Verse.PathFinder',
        methodName: 'FindPathNow',
        signature:
            'PawnPath FindPathNow(IntVec3 start, LocalTargetInfo target, TraverseParms traverseParms, PathFinderCostTuning? tuning, PathEndMode peMode, IPathGridCustomizer customizer)',
        paramTypeFullNames: [
            'Verse.IntVec3',
            'Verse.LocalTargetInfo',
            'Verse.TraverseParms',
            'Verse.PathFinderCostTuning',
            'Verse.AI.PathEndMode',
            'Verse.PathRequest.IPathGridCustomizer',
        ],
        assemblyName: 'Assembly-CSharp',
    },
];

const CAPTURES = {
    schema_version: 1,
    active_capture_id: null,
    captures: [
        {
            id: 'cap_01JZQM2X',
            session_id: SESSION.id,
            trigger: 'slow_tick',
            status: 'finalized',
            started_utc: '2026-08-31T16:12:04Z',
            stopped_utc: '2026-08-31T16:12:34Z',
            finalize_reason: 'time_cap',
            edge_count: 1842,
            estimated_bytes: 2_884_112,
            dropped_samples: 0,
            warning: null,
            roots: CALL_TREE,
        },
        {
            id: 'cap_01JZQJ8B',
            session_id: SESSION.id,
            trigger: 'manual',
            status: 'finalized',
            started_utc: '2026-08-31T15:40:19Z',
            stopped_utc: '2026-08-31T15:41:02Z',
            finalize_reason: 'user_stopped',
            edge_count: 3104,
            estimated_bytes: 5_102_884,
            dropped_samples: 214,
            warning: 'ring buffer saturated for 1.2 s, some edges missing',
            roots: CALL_TREE,
        },
    ],
};

const delta = (base, head) => ({
    delta_ns: head - base,
    delta_percent: base === 0 ? null : ((head - base) / base) * 100,
});

const COMPARISON = {
    schema_version: 1,
    unit: 'ns',
    disclaimer: 'Sessions ran on different load orders. Treat deltas as directional, not exact.',
    base: PRIOR_SESSION,
    head: SESSION,
    timing: {
        base_total_ns: 18_402_100_000,
        head_total_ns: 21_004_800_000,
        ...delta(18_402_100_000, 21_004_800_000),
        base_sample_count: 2_418_402,
        head_sample_count: 2_946_719,
        base_mean_ns: 7_609,
        head_mean_ns: 7_128,
        delta_mean_ns: -481,
    },
    hotspots: [
        {
            id: 1,
            name: 'Verse.MapDrawer.MapMeshDrawerUpdate_First',
            owner: 'core',
            status: 'regressed',
            base_total_ns: 9_882_400_000,
            head_total_ns: 12_218_400_000,
            ...delta(9_882_400_000, 12_218_400_000),
            base_mean_ns: 9_904,
            head_mean_ns: 11_842,
            likely_regression_candidate: true,
        },
        {
            id: 5,
            name: 'vanillaexpanded.vfecore.HediffCompTick',
            owner: 'vanillaexpanded.vfecore',
            status: 'added',
            base_total_ns: 0,
            head_total_ns: 1_486_400_000,
            delta_ns: 1_486_400_000,
            delta_percent: null,
            base_mean_ns: 0,
            head_mean_ns: 986_400,
            likely_regression_candidate: true,
        },
        {
            id: 3,
            name: 'Verse.PathFinder.FindPathNow',
            owner: 'core',
            status: 'improved',
            base_total_ns: 3_104_800_000,
            head_total_ns: 2_104_900_000,
            ...delta(3_104_800_000, 2_104_900_000),
            base_mean_ns: 3_402_100,
            head_mean_ns: 2_104_900,
            likely_regression_candidate: false,
        },
        {
            id: 9,
            name: 'fluffy.worktab.PriorityRecache',
            owner: 'fluffy.worktab',
            status: 'unchanged',
            base_total_ns: 1_398_200_000,
            head_total_ns: 1_402_600_000,
            ...delta(1_398_200_000, 1_402_600_000),
            base_mean_ns: 401_200,
            head_mean_ns: 402_600,
            likely_regression_candidate: false,
        },
        {
            id: 12,
            name: 'rocketman.CacheDirtier.Tick',
            owner: 'rocketman',
            status: 'removed',
            base_total_ns: 884_200_000,
            head_total_ns: 0,
            delta_ns: -884_200_000,
            delta_percent: -100,
            base_mean_ns: 214_800,
            head_mean_ns: 0,
            likely_regression_candidate: false,
        },
    ],
    mod_costs: [
        {
            owner: 'vanillaexpanded.vfecore',
            status: 'added',
            base_total_ns: 0,
            head_total_ns: 1_486_400_000,
            delta_ns: 1_486_400_000,
            delta_percent: null,
            likely_regression_candidate: true,
        },
        {
            owner: 'core',
            status: 'regressed',
            base_total_ns: 14_882_100_000,
            head_total_ns: 17_218_400_000,
            ...delta(14_882_100_000, 17_218_400_000),
            likely_regression_candidate: true,
        },
        {
            owner: 'fluffy.worktab',
            status: 'unchanged',
            base_total_ns: 1_398_200_000,
            head_total_ns: 1_402_600_000,
            ...delta(1_398_200_000, 1_402_600_000),
            likely_regression_candidate: false,
        },
    ],
    metrics: [
        {
            name: 'rimobs.tick.duration',
            owner: 'core',
            kind: 2,
            unit: 'ms',
            status: 'regressed',
            base_value: 14.2,
            head_value: 18.4,
            delta_value: 4.2,
            delta_percent: 29.58,
        },
        {
            name: 'rimobs.colonists.count',
            owner: 'core',
            kind: 1,
            unit: 'pawns',
            status: 'unchanged',
            base_value: 14,
            head_value: 14,
            delta_value: 0,
            delta_percent: 0,
        },
    ],
    load_order: {
        added: ['vanillaexpanded.vfecore', 'vanillaexpanded.furniture'],
        removed: ['rocketman'],
        common: ['brrainz.harmony', 'RimWorks.RimObs', 'fluffy.worktab', 'ludeon.rimworld'],
    },
    warnings: [
        'Load order changed between sessions, 2 mods added and 1 removed.',
        'Head session ran 34 minutes, base ran 2 hours 11 minutes.',
    ],
};

const ROUTES = [
    [
        /^\/api\/v1\/status$/,
        () => ({
            schema_version: 1,
            status: 'receiving',
            version: '2.0.0',
            session: SESSION,
            receive: RECEIVE,
            update: {
                available: true,
                latest_version: '2.1.0',
                url: 'https://github.com/RimWorks/rimworld-observability-collector/releases',
            },
            exporters: {
                prometheus_enabled: true,
                prometheus_port: 9464,
                otlp_enabled: false,
                prometheus_health: {
                    total_scrapes: 412,
                    last_scrape_utc: '2026-08-31T16:41:30Z',
                    last_sample_count: 214,
                    total_errors: 2,
                    last_error: 'connection reset by peer',
                },
            },
        }),
    ],
    [
        /^\/api\/v1\/sessions\/current\/hotspots$/,
        () => ({ schema_version: 1, hotspots: sectionRows }),
    ],
    [
        /^\/api\/v1\/sessions\/current\/sections$/,
        () => ({ schema_version: 1, sections: sectionRows }),
    ],
    [
        /^\/api\/v1\/sections$/,
        () => ({
            schema_version: 1,
            sections: sectionRows.map((s) => ({
                id: s.id,
                name: s.name,
                subsystem:
                    s.name.startsWith('Verse') || s.name.startsWith('RimWorld')
                        ? 'core'
                        : s.name.split('.')[0],
            })),
        }),
    ],
    [
        /^\/api\/v1\/sessions\/current\/sections\/(\d+)\/timeseries$/,
        (m) => timeseries(Number(m[1])),
    ],
    [/^\/api\/v1\/sessions\/current\/metrics$/, () => METRICS],
    [/^\/api\/v1\/sessions\/current\/gc$/, (m, q) => gcEvents(Number(q.get('limit') ?? 200))],
    [
        /^\/api\/v1\/sessions\/current\/call_tree$/,
        () => ({ schema_version: 1, depth_cap: 6, top_n: 12, roots: CALL_TREE }),
    ],
    [/^\/api\/v1\/logs$/, (m, q) => logs(Number(q.get('limit') ?? 200), q.get('level'))],
    [/^\/api\/v1\/sessions$/, () => ({ schema_version: 1, sessions: [SESSION, PRIOR_SESSION] })],
    [/^\/api\/v1\/sessions\/compare$/, () => COMPARISON],
    [
        /^\/api\/v1\/sessions\/current$/,
        () => ({ schema_version: 1, session: SESSION, receive: RECEIVE }),
    ],
    [
        /^\/api\/v1\/sessions\/current\/summary$/,
        () => ({
            schema_version: 1,
            session: SESSION,
            section_count: 34,
            metric_count: 4,
            total_batches: RECEIVE.total_batches,
            total_samples: RECEIVE.total_samples,
            total_bytes: RECEIVE.total_bytes,
            total_gc_events: RECEIVE.total_gc_events,
            total_allocations: RECEIVE.total_allocations,
            total_metric_observations: METRICS.total_observations,
            total_section_ns: 21_004_800_000,
            last_batch_utc: RECEIVE.last_batch_utc,
        }),
    ],
    [/^\/api\/v1\/sessions\/current\/patches$/, () => PATCHES],
    [
        /^\/api\/v1\/sessions\/current\/captures$/,
        (m, q) =>
            q.get('id')
                ? {
                      schema_version: 1,
                      capture:
                          CAPTURES.captures.find((c) => c.id === q.get('id')) ??
                          CAPTURES.captures[0],
                  }
                : CAPTURES,
    ],
    [
        /^\/api\/v1\/instrumentation\/search$/,
        () => ({ schema_version: 1, results: SEARCH_RESULTS }),
    ],
    [/^\/api\/v1\/instrumentation\/patches$/, () => INSTRUMENTATION_PATCHES],
    [
        /^\/api\/v1\/export\/bundle\/estimate$/,
        () => ({ estimated_bytes: 41_983_221, cap_bytes: 104_857_600, exceeds_soft_cap: false }),
    ],
];

function proxyToVite(req, res) {
    const upstream = httpRequest(
        {
            host: '127.0.0.1',
            port: VITE_PORT,
            path: req.url,
            method: req.method,
            headers: req.headers,
        },
        (up) => {
            res.writeHead(up.statusCode ?? 502, up.headers);
            up.pipe(res);
        },
    );
    upstream.on('error', () => {
        res.writeHead(502, { 'content-type': 'text/plain' });
        res.end(`mock-api: vite dev server not reachable on ${VITE_PORT}`);
    });
    req.pipe(upstream);
}

createServer((req, res) => {
    const url = new URL(req.url ?? '/', `http://${req.headers.host ?? 'localhost'}`);
    if (!url.pathname.startsWith('/api/')) return proxyToVite(req, res);

    for (const [pattern, handler] of ROUTES) {
        const match = pattern.exec(url.pathname);
        if (match) {
            res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
            return res.end(JSON.stringify(handler(match, url.searchParams)));
        }
    }

    res.writeHead(404, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ error: `no mock for ${url.pathname}` }));
}).listen(PORT, '0.0.0.0', () => {
    console.log(`mock-api on http://0.0.0.0:${PORT} -> vite ${VITE_PORT}`);
});
