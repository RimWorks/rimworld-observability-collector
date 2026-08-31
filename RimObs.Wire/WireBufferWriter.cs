using System;
using System.Text;

namespace RimWorks.RimObs.Wire;

// Hand-rolled MessagePack-format encoder with zero external dependencies. RimWorld's Unity Mono
// cannot load the MessagePack package (its dynamic-codegen types reference System.Reflection.Emit
// split-facade assemblies that fail to bind, which poisons the mod's assembly load and segfaults),
// so the net48 side cannot ship MessagePack.dll at all. This writer emits the canonical compact
// MessagePack byte layout (positive/negative fixint, str/bin, fixarray etc.) so output stays valid
// standard MessagePack while depending only on the BCL.
internal sealed class WireBufferWriter {
    private byte[] _buffer;
    private int _written;

    public WireBufferWriter(int initialCapacity = 256) {
        if (initialCapacity < 1)
            initialCapacity = 1;
        _buffer = new byte[initialCapacity];
        _written = 0;
    }

    public byte[] ToArray() {
        byte[] result = new byte[_written];
        Array.Copy(_buffer, 0, result, 0, _written);
        return result;
    }

    public void WriteArrayHeader(int count) {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count <= 15) {
            WriteRawByte((byte)(0x90 | count));
        }
        else if (count <= ushort.MaxValue) {
            WriteRawByte(0xdc);
            WriteUInt16BE((ushort)count);
        }
        else {
            WriteRawByte(0xdd);
            WriteUInt32BE((uint)count);
        }
    }

    public void WriteInt32(int value) {
        WriteInt64(value);
    }

    public void WriteInt64(long value) {
        if (value >= 0) {
            WriteUInt64((ulong)value);
            return;
        }
        if (value >= -32) {
            WriteRawByte((byte)value);
        }
        else if (value >= sbyte.MinValue) {
            WriteRawByte(0xd0);
            WriteRawByte((byte)value);
        }
        else if (value >= short.MinValue) {
            WriteRawByte(0xd1);
            WriteUInt16BE((ushort)(short)value);
        }
        else if (value >= int.MinValue) {
            WriteRawByte(0xd2);
            WriteUInt32BE((uint)(int)value);
        }
        else {
            WriteRawByte(0xd3);
            WriteUInt64BE((ulong)value);
        }
    }

    public void WriteUInt64(ulong value) {
        if (value <= 127) {
            WriteRawByte((byte)value);
        }
        else if (value <= byte.MaxValue) {
            WriteRawByte(0xcc);
            WriteRawByte((byte)value);
        }
        else if (value <= ushort.MaxValue) {
            WriteRawByte(0xcd);
            WriteUInt16BE((ushort)value);
        }
        else if (value <= uint.MaxValue) {
            WriteRawByte(0xce);
            WriteUInt32BE((uint)value);
        }
        else {
            WriteRawByte(0xcf);
            WriteUInt64BE(value);
        }
    }

    public void WriteUInt8(byte value) {
        WriteUInt64(value);
    }

    public void WriteDouble(double value) {
        WriteRawByte(0xcb);
        WriteUInt64BE((ulong)BitConverter.DoubleToInt64Bits(value));
    }

    public void WriteString(string? value) {
        if (value == null) {
            WriteRawByte(0xc0);
            return;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int len = bytes.Length;
        if (len <= 31) {
            WriteRawByte((byte)(0xa0 | len));
        }
        else if (len <= byte.MaxValue) {
            WriteRawByte(0xd9);
            WriteRawByte((byte)len);
        }
        else if (len <= ushort.MaxValue) {
            WriteRawByte(0xda);
            WriteUInt16BE((ushort)len);
        }
        else {
            WriteRawByte(0xdb);
            WriteUInt32BE((uint)len);
        }
        WriteRawBytes(bytes, 0, len);
    }

    public void WriteBinary(byte[]? value) {
        if (value == null) {
            WriteRawByte(0xc0);
            return;
        }
        int len = value.Length;
        if (len <= byte.MaxValue) {
            WriteRawByte(0xc4);
            WriteRawByte((byte)len);
        }
        else if (len <= ushort.MaxValue) {
            WriteRawByte(0xc5);
            WriteUInt16BE((ushort)len);
        }
        else {
            WriteRawByte(0xc6);
            WriteUInt32BE((uint)len);
        }
        WriteRawBytes(value, 0, len);
    }

    private void WriteUInt16BE(ushort value) {
        EnsureCapacity(2);
        _buffer[_written++] = (byte)(value >> 8);
        _buffer[_written++] = (byte)value;
    }

    private void WriteUInt32BE(uint value) {
        EnsureCapacity(4);
        _buffer[_written++] = (byte)(value >> 24);
        _buffer[_written++] = (byte)(value >> 16);
        _buffer[_written++] = (byte)(value >> 8);
        _buffer[_written++] = (byte)value;
    }

    private void WriteUInt64BE(ulong value) {
        EnsureCapacity(8);
        _buffer[_written++] = (byte)(value >> 56);
        _buffer[_written++] = (byte)(value >> 48);
        _buffer[_written++] = (byte)(value >> 40);
        _buffer[_written++] = (byte)(value >> 32);
        _buffer[_written++] = (byte)(value >> 24);
        _buffer[_written++] = (byte)(value >> 16);
        _buffer[_written++] = (byte)(value >> 8);
        _buffer[_written++] = (byte)value;
    }

    private void WriteRawByte(byte value) {
        EnsureCapacity(1);
        _buffer[_written++] = value;
    }

    private void WriteRawBytes(byte[] source, int offset, int count) {
        EnsureCapacity(count);
        Array.Copy(source, offset, _buffer, _written, count);
        _written += count;
    }

    private void EnsureCapacity(int additional) {
        int required = _written + additional;
        if (required <= _buffer.Length)
            return;
        int newSize = _buffer.Length * 2;
        if (newSize < required)
            newSize = required;
        Array.Resize(ref _buffer, newSize);
    }
}
