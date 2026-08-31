using System.Collections.Generic;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Collector.Panels;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class PanelsEndpoints {
    public static IEndpointRouteBuilder MapPanelsEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/panels", GetPanels);
        endpoints.MapPost("/api/v1/panels/register", RegisterPanel);

        endpoints.MapGet("/api/v1/panels/refresh_requested", (PanelRegistry registry, ConfigStore config) =>
            RefreshResult(registry, config));

        endpoints.MapPost("/api/v1/panels/refresh_requested", (PanelRegistry registry, ConfigStore config) => {
            registry.RequestRefresh();
            return RefreshResult(registry, config);
        });

        return endpoints;
    }

    private static IResult GetPanels(PanelRegistry registry) {
        List<object> owners = new();
        foreach (KeyValuePair<string, PanelDefinition[]> owner in registry.Snapshot()) {
            List<object> panels = new(owner.Value.Length);
            foreach (PanelDefinition panel in owner.Value) {
                List<object> layout = new(panel.Layout.Count);
                foreach (PanelLayoutItem item in panel.Layout) {
                    layout.Add(new { metric = item.Metric, widget = item.Widget });
                }

                panels.Add(new { id = panel.Id, title = panel.Title, icon = panel.Icon, layout = layout.ToArray() });
            }

            owners.Add(new { owner_id = owner.Key, panels = panels.ToArray() });
        }

        return Results.Ok(new { schema_version = SchemaVersion.Current, owners = owners.ToArray() });
    }

    private static async Task<IResult> RegisterPanel(HttpContext context, PanelRegistry registry) {
        (PanelRegistration? registration, IResult? error) = await RequestBody.ReadValidated<PanelRegistration>(
            context, PanelRegistry.SchemaVersion, r => r.SchemaVersion, "panel registration");
        if (error is not null) {
            return error;
        }

        if (string.IsNullOrWhiteSpace(registration!.OwnerId)) {
            return Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = "owner_id is required" });
        }

        foreach (PanelDefinition panel in registration.Panels) {
            foreach (PanelLayoutItem item in panel.Layout) {
                if (!PanelWidgets.IsValid(item.Widget)) {
                    return Results.BadRequest(new {
                        schema_version = SchemaVersion.Current,
                        reason = $"unknown widget '{item.Widget}'",
                    });
                }
            }
        }

        registry.Replace(registration.OwnerId, registration.Panels.ToArray());
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            owner_id = registration.OwnerId,
            panel_count = registration.Panels.Count,
        });
    }

    private static IResult RefreshResult(PanelRegistry registry, ConfigStore config) {
        RefreshFlagState state = registry.RefreshState(config.Current.Panels.RefreshFlagTtlSeconds);
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            refresh_requested = state.Requested,
            ttl_seconds_remaining = state.RemainingSeconds,
        });
    }
}
