using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Captures;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class CapturesEndpoints {
    public static IEndpointRouteBuilder MapCapturesEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapPost("/api/v1/captures/start", (CaptureManager captures) => {
            CaptureSession capture = captures.Start(CaptureTrigger.Manual);
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                capture = MapCapture(capture, includeTree: false, ConfigDefaults(), NsPerTickFallback()),
            });
        });

        endpoints.MapPost("/api/v1/captures/stop", (CaptureManager captures) => {
            CaptureSession? capture = captures.Stop();
            if (capture is null)
                return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "no active capture" });

            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                capture = MapCapture(capture, includeTree: false, ConfigDefaults(), NsPerTickFallback()),
            });
        });

        endpoints.MapGet("/api/v1/sessions/current/captures", (
            CaptureManager captures,
            SessionAggregator aggregator,
            ConfigStore config,
            string? id,
            bool? tree) => {
                double nsPerTick = NsPerTick(aggregator.Meta);
                CaptureOptions options = config.Current.Capture;
                IReadOnlyList<CaptureSession> snapshot = captures.Snapshot();

                if (!string.IsNullOrEmpty(id)) {
                    CaptureSession? match = snapshot.FirstOrDefault(c => c.CaptureId == id);
                    if (match is null)
                        return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "unknown capture" });

                    return Results.Ok(new {
                        schema_version = SchemaVersion.Current,
                        capture = MapCapture(match, includeTree: true, options, nsPerTick),
                    });
                }

                bool includeTree = tree == true;
                return Results.Ok(new {
                    schema_version = SchemaVersion.Current,
                    active_capture_id = captures.Active?.CaptureId,
                    captures = snapshot.Select(c => MapCapture(c, includeTree, options, nsPerTick)).ToArray(),
                });
            });

        return endpoints;
    }

    private static object MapCapture(CaptureSession capture, bool includeTree, CaptureOptions options, double nsPerTick) {
        object[] roots = [];
        if (includeTree) {
            IReadOnlyList<CallTreeNode> tree = CallTreeBuilder.Build(
                capture.SnapshotEdges(),
                capture.Names,
                nsPerTick,
                options.NestedSectionDepthCap,
                options.NestedSectionTopN);
            roots = tree.Select(MapCallNode).ToArray();
        }

        return new {
            id = capture.CaptureId,
            session_id = capture.SessionId,
            trigger = TriggerName(capture.Trigger),
            status = capture.Status == CaptureStatus.Running ? "running" : "finalized",
            started_utc = capture.StartedUtc,
            stopped_utc = capture.StoppedUtc,
            finalize_reason = FinalizeReasonName(capture.FinalizeReason),
            edge_count = capture.EdgeCount,
            estimated_bytes = capture.EstimatedBytes,
            dropped_samples = capture.DroppedSamples,
            warning = WarningFor(capture),
            roots,
        };
    }

    private static string? WarningFor(CaptureSession capture) {
        return capture.FinalizeReason switch {
            CaptureFinalizeReason.TimeCap => "capture auto-finalized: reached duration cap",
            CaptureFinalizeReason.SizeCap => "capture auto-finalized: reached size cap",
            CaptureFinalizeReason.DashboardClosed => "capture finalized: dashboard closed",
            _ => null,
        };
    }

    private static object MapCallNode(CallTreeNode node) {
        return new {
            id = node.SectionId,
            name = node.Name,
            call_count = node.CallCount,
            total_ns = node.TotalNs,
            is_other = node.IsOther,
            children = node.Children.Select(MapCallNode).ToArray(),
        };
    }

    private static string TriggerName(CaptureTrigger trigger) {
        return trigger == CaptureTrigger.SlowTick ? "slow_tick" : "manual";
    }

    private static string FinalizeReasonName(CaptureFinalizeReason reason) {
        return reason switch {
            CaptureFinalizeReason.UserStopped => "user_stopped",
            CaptureFinalizeReason.TimeCap => "time_cap",
            CaptureFinalizeReason.SizeCap => "size_cap",
            CaptureFinalizeReason.DashboardClosed => "dashboard_closed",
            _ => "none",
        };
    }

    private static double NsPerTick(SessionMeta? meta) => TickConverter.NsPerTick(meta);

    private static double NsPerTickFallback() => TickConverter.NsPerTick(0L);

    private static CaptureOptions ConfigDefaults() => new();
}
