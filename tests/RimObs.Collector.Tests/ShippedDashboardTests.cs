using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class ShippedDashboardTests {
    // Every series MetricsPushService.Add()s. A panel naming anything else queries nothing.
    private static readonly string[] PushedMetrics = [
        "rimobs_tps",
        "rimobs_fps",
        "rimobs_tick_ms_p50_session",
        "rimobs_tick_ms_p99_session",
        "rimobs_frame_ms_p50",
        "rimobs_frame_ms_p99",
        "rimobs_alloc_bytes_per_min",
        "rimobs_gc_pause_ms_total",
        "rimobs_gc_events_total",
        "rimobs_vram_tracked_bytes",
        "rimobs_samples_total",
        "rimobs_transport_lost_total",
        "rimobs_session_info",
    ];

    private static JsonElement Dashboard() {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "RimObs.slnx"))) {
            at = at.Parent;
        }

        at.Should().NotBeNull("the tests run from inside the repo");
        string path = Path.Combine(at!.FullName, "contrib", "grafana", "rimobs-dashboard.json");
        File.Exists(path).Should().BeTrue("the shipped dashboard lives at contrib/grafana/rimobs-dashboard.json");

        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Panels(JsonElement dashboard) {
        foreach (JsonElement panel in dashboard.GetProperty("panels").EnumerateArray()) {
            yield return panel;
            if (panel.TryGetProperty("panels", out JsonElement nested) && nested.ValueKind == JsonValueKind.Array) {
                foreach (JsonElement child in nested.EnumerateArray()) {
                    yield return child;
                }
            }
        }
    }

    private static List<string> Expressions(JsonElement dashboard) {
        List<string> exprs = [];
        foreach (JsonElement panel in Panels(dashboard)) {
            if (!panel.TryGetProperty("targets", out JsonElement targets)) {
                continue;
            }

            foreach (JsonElement target in targets.EnumerateArray()) {
                exprs.Add(target.GetProperty("expr").GetString()!);
            }
        }

        return exprs;
    }

    // tertius: the push moved the name onto rimobs_session_info, so the shipped panels have
    // to join on it or a named session shows as a raw id in grafana.
    [Fact]
    public void Every_measurement_panel_joins_the_session_info_series() {
        int checked_ = 0;
        foreach (JsonElement panel in Panels(Dashboard())) {
            if (!panel.TryGetProperty("targets", out JsonElement targets))
                continue;
            foreach (JsonElement target in targets.EnumerateArray()) {
                string expr = target.GetProperty("expr").GetString() ?? string.Empty;
                expr.Should().Contain(
                    "on(session_id) group_left(session_name) rimobs_session_info",
                    "a panel without the join can only legend a raw session id");
                target.GetProperty("legendFormat").GetString().Should().Contain("{{session_name}}");
                checked_++;
            }
        }

        checked_.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Dashboard_is_json_with_the_rimobs_uid_and_title() {
        JsonElement dashboard = Dashboard();

        dashboard.GetProperty("uid").GetString().Should().Be("rimobs");
        dashboard.GetProperty("title").GetString().Should().Be("RimObs");
        dashboard.GetProperty("schemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(39);
    }

    [Fact]
    public void Every_promql_target_names_a_metric_the_collector_actually_pushes() {
        List<string> exprs = Expressions(Dashboard());
        exprs.Should().NotBeEmpty();

        foreach (string expr in exprs) {
            foreach (Match match in Regex.Matches(expr, "rimobs_[a-z0-9_]+")) {
                PushedMetrics.Should().Contain(match.Value, "the collector pushes no series called '{0}'", match.Value);
            }
        }
    }

    [Fact]
    public void Every_promql_target_filters_by_the_session_variable() {
        foreach (string expr in Expressions(Dashboard())) {
            expr.Should().Contain("session_id=~\"$session\"", "panels have to honour the session picker");
        }
    }

    [Fact]
    public void Panels_filter_on_the_selected_session() {
        foreach (JsonElement panel in Panels(Dashboard())) {
            if (!panel.TryGetProperty("targets", out JsonElement targets)) {
                continue;
            }

            foreach (JsonElement target in targets.EnumerateArray()) {
                target.GetProperty("expr").GetString().Should()
                    .Contain("session_id=~\"$session\"", "a panel that ignores $session shows every session at once");
            }
        }
    }

    [Fact]
    public void Session_regions_come_from_an_annotation_query_tagged_rimobs() {
        JsonElement annotations = Dashboard().GetProperty("annotations").GetProperty("list");

        JsonElement sessions = annotations
            .EnumerateArray()
            .Single(a => a.TryGetProperty("target", out JsonElement t) && t.TryGetProperty("tags", out _));

        sessions.GetProperty("enable").GetBoolean().Should().BeTrue();
        sessions.GetProperty("target").GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("rimobs");
    }

    [Fact]
    public void A_datasource_variable_lets_the_importer_pick_their_own_store() {
        JsonElement variables = Dashboard().GetProperty("templating").GetProperty("list");

        JsonElement datasource = variables.EnumerateArray().Single(v => v.GetProperty("name").GetString() == "DS_PROMETHEUS");
        datasource.GetProperty("type").GetString().Should().Be("datasource");
        datasource.GetProperty("query").GetString().Should().Be("prometheus");

        JsonElement session = variables.EnumerateArray().Single(v => v.GetProperty("name").GetString() == "session");
        session.GetProperty("type").GetString().Should().Be("query");
        session.GetProperty("multi").GetBoolean().Should().BeTrue();
        session.GetProperty("includeAll").GetBoolean().Should().BeTrue();
        session.GetProperty("definition").GetString().Should().Be("label_values(rimobs_samples_total, session_id)");
    }
}
