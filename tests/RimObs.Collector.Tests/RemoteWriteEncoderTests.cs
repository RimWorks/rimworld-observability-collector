using System.Text;
using FluentAssertions;
using RimWorks.RimObs.Collector.Push;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class RemoteWriteEncoderTests {
    [Fact]
    public void EncodeWriteRequest_matches_the_golden_protobuf() {
        RemoteWriteSample sample = new("up", [new("job", "rimobs")], 1.0, 1_700_000_000_000);

        Convert.ToHexString(RemoteWriteEncoder.EncodeWriteRequest([sample]))
            .Should()
            .Be("0A310A0E0A085F5F6E616D655F5F120275700A0D0A036A6F62120672696D6F6273121009000000000000F03F1080D095FFBC31");
    }

    [Fact]
    public void Encode_wraps_the_protobuf_in_a_snappy_block() {
        RemoteWriteSample sample = new("up", [new("job", "rimobs")], 1.0, 1_700_000_000_000);
        byte[] expected = RemoteWriteEncoder.EncodeWriteRequest([sample]);

        byte[] compressed = RemoteWriteEncoder.Encode([sample]);

        compressed[0].Should().Be((byte)expected.Length, "the snappy preamble is the uncompressed length");
        SnappyTestDecoder.Decompress(compressed).Should().Equal(expected);
    }

    [Fact]
    public void EncodeWriteRequest_sorts_labels_by_name() {
        RemoteWriteSample sample = new("rimobs_tps", [new("zone", "a"), new("alpha", "b")], 60, 1);

        string hex = Convert.ToHexString(RemoteWriteEncoder.EncodeWriteRequest([sample]));

        hex.IndexOf(Hex("__name__"), StringComparison.Ordinal).Should().BeLessThan(hex.IndexOf(Hex("alpha"), StringComparison.Ordinal));
        hex.IndexOf(Hex("alpha"), StringComparison.Ordinal).Should().BeLessThan(hex.IndexOf(Hex("zone"), StringComparison.Ordinal));
    }

    [Fact]
    public void EncodeWriteRequest_injects_the_metric_name_label() {
        RemoteWriteSample sample = new("rimobs_tps", [], 60, 1);

        string hex = Convert.ToHexString(RemoteWriteEncoder.EncodeWriteRequest([sample]));

        hex.Should().Contain("0A08" + Hex(RemoteWriteEncoder.NameLabel) + "120A" + Hex("rimobs_tps"));
    }

    [Fact]
    public void EncodeWriteRequest_drops_a_caller_supplied_name_label() {
        RemoteWriteSample sample = new("rimobs_tps", [new(RemoteWriteEncoder.NameLabel, "hijacked")], 60, 1);

        string hex = Convert.ToHexString(RemoteWriteEncoder.EncodeWriteRequest([sample]));

        Occurrences(hex, Hex(RemoteWriteEncoder.NameLabel)).Should().Be(1);
        hex.Should().NotContain(Hex("hijacked"));
        hex.Should().Contain(Hex("rimobs_tps"));
    }

    [Fact]
    public void EncodeWriteRequest_of_no_samples_is_an_empty_request() {
        RemoteWriteEncoder.EncodeWriteRequest([]).Should().BeEmpty();
        RemoteWriteEncoder.Encode([]).Should().Equal((byte)0x00);
    }

    [Fact]
    public void EncodeWriteRequest_writes_negative_timestamps_as_two_s_complement() {
        RemoteWriteSample sample = new("up", [], 0, -1);

        string hex = Convert.ToHexString(RemoteWriteEncoder.EncodeWriteRequest([sample]));

        hex.Should().Contain("10FFFFFFFFFFFFFFFFFF01", "a negative int64 is a ten-byte varint, not zigzag");
    }

    [Theory]
    [InlineData(unchecked((long)0xFFF8000000000001), "09010000000000F8FF")]
    [InlineData(0x7FF0000000000000, "09000000000000F07F")]
    [InlineData(unchecked((long)0x8000000000000000), "090000000000000080")]
    public void EncodeWriteRequest_keeps_special_doubles_bit_exact(long bits, string expected) {
        RemoteWriteSample sample = new("up", [], BitConverter.Int64BitsToDouble(bits), 0);

        Convert.ToHexString(RemoteWriteEncoder.EncodeWriteRequest([sample])).Should().Contain(expected);
    }

    private static string Hex(string text) => Convert.ToHexString(Encoding.UTF8.GetBytes(text));

    private static int Occurrences(string haystack, string needle) {
        int count = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
