using FluentAssertions;
using RimWorks.RimObs.Collector.Push;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class SnappyBlockTests {
    [Fact]
    public void Compress_of_empty_input_is_a_lone_zero_length_preamble() {
        SnappyBlock.Compress([]).Should().Equal((byte)0x00);
    }

    [Fact]
    public void Compress_emits_preamble_then_a_single_literal_tag() {
        byte[] output = SnappyBlock.Compress("hi"u8);

        Convert.ToHexString(output).Should().Be("02046869");
    }

    [Fact]
    public void Compress_keeps_the_inline_tag_at_the_sixty_byte_boundary() {
        byte[] sixty = SnappyBlock.Compress(new byte[60]);
        sixty[0].Should().Be(60, "the preamble is the uncompressed length");
        sixty[1].Should().Be(59 << 2, "length-1 still fits in the tag byte");
        sixty.Length.Should().Be(62);

        byte[] sixtyOne = SnappyBlock.Compress(new byte[61]);
        sixtyOne[1].Should().Be(60 << 2, "length-1 spills into one trailing byte");
        sixtyOne[2].Should().Be(60);
        sixtyOne.Length.Should().Be(64);
    }

    [Fact]
    public void Compress_uses_a_three_byte_length_past_sixty_five_thousand() {
        byte[] output = SnappyBlock.Compress(new byte[70000]);

        Convert.ToHexString(output.AsSpan(0, 3).ToArray()).Should().Be("F0A204", "varint of 70000");
        output[3].Should().Be(62 << 2, "tag 62 means three little-endian length bytes");
        Convert.ToHexString(output.AsSpan(4, 3).ToArray()).Should().Be("6F1101", "69999 little-endian");
        output.Length.Should().Be(70007);
    }

    [Fact]
    public void Compress_roundtrips_through_a_decoder() {
        byte[] payload = new byte[100_003];
        new Random(1337).NextBytes(payload);

        SnappyTestDecoder.Decompress(SnappyBlock.Compress(payload)).Should().Equal(payload);
    }
}

// Literal-only snappy block reader, enough to prove the writer's framing decodes.
internal static class SnappyTestDecoder {
    public static byte[] Decompress(ReadOnlySpan<byte> input) {
        int at = 0;
        int expected = (int)ReadVarint(input, ref at);
        using MemoryStream output = new(expected);
        while (at < input.Length) {
            byte tag = input[at++];
            (tag & 0x03).Should().Be(0, "the writer only emits literals");
            int lengthMinusOne = tag >> 2;
            if (lengthMinusOne >= 60) {
                int extra = lengthMinusOne - 59;
                lengthMinusOne = 0;
                for (int i = 0; i < extra; i++)
                    lengthMinusOne |= input[at++] << (8 * i);
            }

            int length = lengthMinusOne + 1;
            output.Write(input.Slice(at, length));
            at += length;
        }

        byte[] bytes = output.ToArray();
        bytes.Length.Should().Be(expected, "the preamble must match the decoded length");
        return bytes;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> input, ref int at) {
        ulong value = 0;
        int shift = 0;
        while (true) {
            byte b = input[at++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
            shift += 7;
        }
    }
}
