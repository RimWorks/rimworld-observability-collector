using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RimWorks.RimObs.Collector.Exporters;

// Writes the Prometheus text exposition format, version 0.0.4.
// Reference: https://prometheus.io/docs/instrumenting/exposition_formats/
public sealed class PrometheusExposition {
    private readonly StringBuilder _sb = new();
    private readonly HashSet<string> _declared = new(System.StringComparer.Ordinal);

    public void WriteMetadata(string name, string type, string help) {
        if (!_declared.Add(name))
            return;
        _sb.Append("# HELP ").Append(name).Append(' ').Append(EscapeHelp(help)).Append('\n');
        _sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
    }

    public void WriteSample(string name, double value) {
        WriteSample(name, [], value);
    }

    public void WriteSample(string name, IReadOnlyList<PrometheusLabel> labels, double value) {
        _sb.Append(name);
        if (labels.Count > 0) {
            _sb.Append('{');
            for (int i = 0; i < labels.Count; i++) {
                if (i > 0)
                    _sb.Append(',');
                _sb.Append(labels[i].Name).Append("=\"").Append(EscapeLabelValue(labels[i].Value)).Append('"');
            }
            _sb.Append('}');
        }
        _sb.Append(' ').Append(FormatValue(value)).Append('\n');
    }

    public override string ToString() => _sb.ToString();

    internal static string FormatValue(double value) {
        if (double.IsNaN(value))
            return "NaN";
        if (double.IsPositiveInfinity(value))
            return "+Inf";
        if (double.IsNegativeInfinity(value))
            return "-Inf";
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    internal static string EscapeHelp(string help) {
        if (help.IndexOf('\\') < 0 && help.IndexOf('\n') < 0)
            return help;
        StringBuilder sb = new(help.Length + 4);
        foreach (char c in help) {
            switch (c) {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    internal static string EscapeLabelValue(string value) {
        if (value.IndexOf('\\') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0)
            return value;
        StringBuilder sb = new(value.Length + 4);
        foreach (char c in value) {
            switch (c) {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
