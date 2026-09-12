using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class FramesEndpoints {
    public static IEndpointRouteBuilder MapFramesEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/frames/latest", (SessionAggregator aggregator) =>
            Results.Ok(BuildLatestPayload(aggregator)));

        // clipped, not padded: an evicted `from` starts at the oldest frame held, and holes
        // inside the run stay missing so the client can draw them.
        endpoints.MapGet("/api/v1/frames", (SessionAggregator aggregator, int? from, int? count, double? min_dur_us) => {
            SessionMeta? meta = aggregator.Meta;
            double usPerTick = TickConverter.NsPerTick(meta) / 1000.0;
            long anchor = meta?.AnchorTimestamp ?? 0L;
            // duration floor for wide selections: nodes a zoomed-out view cannot draw are
            // most of the payload, and serializing them is what made a range take seconds.
            long minDurTicks = min_dur_us is > 0 && usPerTick > 0 ? (long)(min_dur_us.Value / usPerTick) : 0L;
            FrameSnapshot[] frames = aggregator.Frames.Range(from ?? -1, QueryLimit.Clamp(count, 64, 256));
            object[] mapped = new object[frames.Length];
            for (int i = 0; i < frames.Length; i++)
                mapped[i] = FramePayload.Map(FrameLod.Filter(frames[i], minDurTicks), anchor, usPerTick);
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                lod_min_dur_us = min_dur_us is > 0 ? min_dur_us.Value : 0.0,
                stopwatch_frequency = meta?.StopwatchFrequency ?? 0L,
                frames = mapped,
                strip = MapStrip(aggregator, usPerTick, 0),
                stats = FramePayload.MapStats(aggregator.Frames.ComputeStats(), usPerTick),
                vitals = MapVitals(aggregator),
                dropped = new {
                    pre_frame_samples = aggregator.Frames.PreFrameSamples,
                    late_samples = aggregator.Frames.LateSamples,
                    library_ring_samples = aggregator.Meta?.SamplesDropped ?? 0L,
                },
            });
        });

        // push instead of poll: one `frame` event per coalesce window, same payload as
        // /frames/latest, comment keepalives while the game is idle.
        endpoints.MapGet("/api/v1/stream", async (HttpContext context, FrameStreamBroadcaster broadcaster) => {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            System.Threading.Channels.ChannelReader<string> reader = broadcaster.Subscribe();
            try {
                await WriteEvent(context, broadcaster.BuildEventJson());
                while (!context.RequestAborted.IsCancellationRequested) {
                    Task<string> next = reader.ReadAsync(context.RequestAborted).AsTask();
                    Task winner = await Task.WhenAny(next, Task.Delay(10_000, context.RequestAborted));
                    if (winner == next) {
                        await WriteEvent(context, await next);
                    }
                    else {
                        await context.Response.WriteAsync(": keepalive\n\n", context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                }
            }
            catch (OperationCanceledException) {
                // the client hung up; nothing to clean but the subscription.
            }
            finally {
                broadcaster.Unsubscribe(reader);
            }
        });

        // one summary row per ring frame, flat arrays. the node-carrying export caps at 256
        // frames for size; this is how a 20k ring answers convergence and tick questions.
        endpoints.MapGet("/api/v1/frames/summaries", (SessionAggregator aggregator, string? sections) => {
            SessionMeta? meta = aggregator.Meta;
            double usPerTick = TickConverter.NsPerTick(meta) / 1000.0;
            long anchor = meta?.AnchorTimestamp ?? 0L;
            FrameSnapshot[] frames = aggregator.Frames.Range(-1, 0);

            int[] wanted = ParseSectionIds(sections);
            int[] ordinals = new int[frames.Length];
            double[] startUs = new double[frames.Length];
            double[] durationUs = new double[frames.Length];
            int[] nodeCounts = new int[frames.Length];
            var sectionDurations = new Dictionary<string, double[]>(wanted.Length);
            foreach (int id in wanted)
                sectionDurations[id.ToString()] = new double[frames.Length];

            for (int f = 0; f < frames.Length; f++) {
                FrameSnapshot frame = frames[f];
                ordinals[f] = frame.CaptureOrdinal;
                startUs[f] = (frame.StartTicks - anchor) * usPerTick;
                durationUs[f] = frame.DurationTicks * usPerTick;
                nodeCounts[f] = frame.NodeCount;
                for (int w = 0; w < wanted.Length; w++) {
                    double[] sums = sectionDurations[wanted[w].ToString()];
                    for (int i = 0; i < frame.NodeCount; i++) {
                        if (frame.SectionIds[i] == wanted[w])
                            sums[f] += frame.NodeElapsedTicks[i] * usPerTick;
                    }
                }
            }

            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                stopwatch_frequency = meta?.StopwatchFrequency ?? 0L,
                frame_count = frames.Length,
                ordinals,
                start_us = startUs,
                duration_us = durationUs,
                node_counts = nodeCounts,
                section_durations = sectionDurations,
                dropped = new {
                    pre_frame_samples = aggregator.Frames.PreFrameSamples,
                    late_samples = aggregator.Frames.LateSamples,
                    library_ring_samples = aggregator.Meta?.SamplesDropped ?? 0L,
                },
            });
        });

        endpoints.MapGet("/api/v1/frames/{ordinal:int}", (SessionAggregator aggregator, int ordinal) => {
            SessionMeta? meta = aggregator.Meta;
            double usPerTick = TickConverter.NsPerTick(meta) / 1000.0;
            long anchor = meta?.AnchorTimestamp ?? 0L;
            FrameSnapshot? frame = aggregator.Frames.FindByOrdinal(ordinal);
            if (frame is null)
                return Results.NotFound(new { error = "no frame with that ordinal is in the ring" });
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                stopwatch_frequency = meta?.StopwatchFrequency ?? 0L,
                frame = FramePayload.Map(frame, anchor, usPerTick),
                strip = MapStrip(aggregator, usPerTick, 0),
                stats = FramePayload.MapStats(aggregator.Frames.ComputeStats(), usPerTick),
                vitals = MapVitals(aggregator),
                dropped = new {
                    pre_frame_samples = aggregator.Frames.PreFrameSamples,
                    late_samples = aggregator.Frames.LateSamples,
                    library_ring_samples = aggregator.Meta?.SamplesDropped ?? 0L,
                },
            });
        });

        // POST so the Origin check gates it: it throws away capture history the user cannot
        // get back. the session and its section registry are untouched.
        endpoints.MapPost("/api/v1/frames/clear", (SessionAggregator aggregator) => {
            aggregator.Frames.Clear();
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                frame_count = aggregator.Frames.Count,
            });
        });

        // its own endpoint on a slow poll: 128 frames of nodes is far too much work to put
        // on /frames/latest at 10/s.
        endpoints.MapGet("/api/v1/frames/baseline", (SessionAggregator aggregator, int? frames) => {
            SessionMeta? meta = aggregator.Meta;
            double usPerTick = TickConverter.NsPerTick(meta) / 1000.0;
            Dictionary<int, long> medians = aggregator.Frames.BaselineMedians(frames ?? 128);
            Dictionary<string, double> mapped = new(medians.Count);
            foreach (KeyValuePair<int, long> entry in medians)
                mapped[entry.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = entry.Value * usPerTick;
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                frames = frames ?? 128,
                median_us = mapped,
            });
        });

        return endpoints;
    }

    // tps and fps ride along on the frame poll so the header updates every frame instead of
    // waiting on the 2s /status poll. the aggregator already holds the newest values.
    private static object? MapVitals(SessionAggregator aggregator) {
        if (!aggregator.HasTpsFps)
            return null;
        return new {
            tps = aggregator.LatestTps,
            fps = aggregator.LatestFps,
            tick = aggregator.LatestTpsFpsTick,
        };
    }

    // spelled out here rather than serialized off ThreadInfo, which is PascalCase.
    private static object[] MapThreads(SessionAggregator aggregator, double nsPerTick) {
        IReadOnlyList<ThreadInfo> threads = aggregator.Threads.Snapshot();
        object[] mapped = new object[threads.Count];
        for (int i = 0; i < threads.Count; i++) {
            ThreadInfo thread = threads[i];
            mapped[i] = new {
                id = thread.Id,
                name = thread.Name,
                role = thread.Role,
                busy_ns = (long)(thread.BusyTicks * nsPerTick),
            };
        }
        return mapped;
    }

    private static async Task WriteEvent(HttpContext context, string json) {
        await context.Response.WriteAsync($"event: frame\ndata: {json}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    /// <summary>The /frames/latest shape. The SSE stream sends the same payload per event.</summary>
    public static object BuildLatestPayload(SessionAggregator aggregator) {
        SessionMeta? meta = aggregator.Meta;
        double usPerTick = TickConverter.NsPerTick(meta) / 1000.0;
        long anchor = meta?.AnchorTimestamp ?? 0L;
        FrameSnapshot? frame = aggregator.Frames.Latest();
        FrameRingStats stats = aggregator.Frames.ComputeStats();
        return new {
            schema_version = SchemaVersion.Current,
            stopwatch_frequency = meta?.StopwatchFrequency ?? 0L,
            frame = frame is null ? null : FramePayload.Map(frame, anchor, usPerTick),
            strip = MapStrip(aggregator, usPerTick, 0),
            stats = FramePayload.MapStats(stats, usPerTick),
            threads = MapThreads(aggregator, TickConverter.NsPerTick(meta)),
            vitals = MapVitals(aggregator),
            dropped = new {
                pre_frame_samples = aggregator.Frames.PreFrameSamples,
                late_samples = aggregator.Frames.LateSamples,
                library_ring_samples = aggregator.Meta?.SamplesDropped ?? 0L,
            },
        };
    }

    private static int[] ParseSectionIds(string? csv) {
        if (string.IsNullOrEmpty(csv))
            return [];
        string[] parts = csv.Split(',');
        List<int> ids = new(parts.Length);
        foreach (string part in parts) {
            if (int.TryParse(part, out int id))
                ids.Add(id);
        }
        return ids.ToArray();
    }

    private static object MapStrip(SessionAggregator aggregator, double usPerTick, int count) {
        (int Ordinal, long DurationTicks)[] strip = aggregator.Frames.SnapshotStrip(count);
        int[] ordinals = new int[strip.Length];
        double[] durations = new double[strip.Length];
        for (int i = 0; i < strip.Length; i++) {
            ordinals[i] = strip[i].Ordinal;
            durations[i] = strip[i].DurationTicks * usPerTick;
        }
        return new { ordinals, durations_us = durations };
    }
}
