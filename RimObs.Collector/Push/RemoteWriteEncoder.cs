using System.Buffers.Binary;
using System.Text;

namespace RimWorks.RimObs.Collector.Push;

public readonly record struct RemoteWriteSample(
    string MetricName,
    IReadOnlyList<KeyValuePair<string, string>> Labels,
    double Value,
    long TimestampMs
);

// Hand-rolled prometheus.WriteRequest protobuf, three tiny message shapes.
// Spec: https://prometheus.io/docs/specs/remote_write_spec/
public static class RemoteWriteEncoder {
    public const string NameLabel = "__name__";

    private const int WireVarint = 0;
    private const int WireFixed64 = 1;
    private const int WireLengthDelimited = 2;

    public static byte[] Encode(IReadOnlyList<RemoteWriteSample> samples) => SnappyBlock.Compress(EncodeWriteRequest(samples));

    internal static byte[] EncodeWriteRequest(IReadOnlyList<RemoteWriteSample> samples) {
        MemoryStream request = new();
        for (int i = 0; i < samples.Count; i++)
            WriteLengthDelimited(request, 1, EncodeTimeSeries(samples[i]));

        return request.ToArray();
    }

    private static byte[] EncodeTimeSeries(in RemoteWriteSample sample) {
        List<KeyValuePair<string, string>> labels = [new(NameLabel, sample.MetricName)];
        // a config-supplied __name__ would 400 every push, so MetricName wins.
        for (int i = 0; i < sample.Labels.Count; i++)
            if (sample.Labels[i].Key != NameLabel)
                labels.Add(sample.Labels[i]);

        labels.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        MemoryStream series = new();
        for (int i = 0; i < labels.Count; i++)
            WriteLengthDelimited(series, 1, EncodeLabel(labels[i].Key, labels[i].Value));

        WriteLengthDelimited(series, 2, EncodeSample(sample.Value, sample.TimestampMs));
        return series.ToArray();
    }

    private static byte[] EncodeLabel(string name, string value) {
        MemoryStream label = new();
        WriteLengthDelimited(label, 1, Encoding.UTF8.GetBytes(name));
        WriteLengthDelimited(label, 2, Encoding.UTF8.GetBytes(value));
        return label.ToArray();
    }

    private static byte[] EncodeSample(double value, long timestampMs) {
        MemoryStream sample = new();
        WriteTag(sample, 1, WireFixed64);
        Span<byte> bits = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bits, BitConverter.DoubleToInt64Bits(value));
        sample.Write(bits);

        WriteTag(sample, 2, WireVarint);
        WriteVarint(sample, (ulong)timestampMs);
        return sample.ToArray();
    }

    private static void WriteLengthDelimited(Stream output, int fieldNumber, ReadOnlySpan<byte> payload) {
        WriteTag(output, fieldNumber, WireLengthDelimited);
        WriteVarint(output, (ulong)payload.Length);
        output.Write(payload);
    }

    private static void WriteTag(Stream output, int fieldNumber, int wireType) => WriteVarint(output, (ulong)((fieldNumber << 3) | wireType));

    private static void WriteVarint(Stream output, ulong value) {
        while (value >= 0x80) {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        output.WriteByte((byte)value);
    }
}
