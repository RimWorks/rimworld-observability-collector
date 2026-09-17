namespace RimWorks.RimObs.Collector.Push;

// Snappy block format, literals only. A literal run is a valid snappy element, so the output
// decodes anywhere; it just does not shrink. Remote-write batches are small and the receiver
// pays the same decode either way.
// Spec: https://github.com/google/snappy/blob/main/format_description.txt
public static class SnappyBlock {
    public static byte[] Compress(ReadOnlySpan<byte> data) {
        byte[] output = new byte[10 + data.Length];
        int at = WriteVarint(output, 0, (uint)data.Length);
        if (data.Length > 0) {
            at = WriteLiteralTag(output, at, data.Length);
            data.CopyTo(output.AsSpan(at));
            at += data.Length;
        }

        return output.AsSpan(0, at).ToArray();
    }

    private static int WriteVarint(byte[] output, int at, uint value) {
        while (value >= 0x80) {
            output[at++] = (byte)(value | 0x80);
            value >>= 7;
        }

        output[at++] = (byte)value;
        return at;
    }

    // Tag low bits 00 mark a literal. Top 6 bits hold length-1, or 60..63 meaning length-1
    // follows in 1..4 little-endian bytes.
    private static int WriteLiteralTag(byte[] output, int at, int length) {
        uint lengthMinusOne = (uint)(length - 1);
        if (lengthMinusOne < 60) {
            output[at++] = (byte)(lengthMinusOne << 2);
            return at;
        }

        int extra = lengthMinusOne switch {
            < 1u << 8 => 1,
            < 1u << 16 => 2,
            < 1u << 24 => 3,
            _ => 4,
        };
        output[at++] = (byte)((59 + extra) << 2);
        for (int i = 0; i < extra; i++)
            output[at++] = (byte)(lengthMinusOne >> (8 * i));

        return at;
    }
}
