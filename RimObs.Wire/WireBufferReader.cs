using System;
using System.Text;

namespace RimWorks.RimObs.Wire;

// Hand-rolled MessagePack-format decoder, the read counterpart to WireBufferWriter. Accepts the
// canonical compact MessagePack layout so it round-trips this codec's own output and any standard
// MessagePack producer. Depends only on the BCL; see WireBufferWriter for why the net48 side cannot
// reference the MessagePack package.
internal sealed class WireBufferReader {
    private readonly byte[] _data;
    private int _pos;

    public WireBufferReader(byte[] data) {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _pos = 0;
    }

    public int BytesRemaining => _data.Length - _pos;

    public int ReadArrayHeader() {
        byte code = ReadRawByte();
        if ((code & 0xf0) == 0x90)
            return code & 0x0f;
        if (code == 0xdc)
            return ReadUInt16BE();
        if (code == 0xdd)
            return checked((int)ReadUInt32BE());
        throw new WireFormatException($"Expected array header, got 0x{code:x2}.");
    }

    public byte ReadUInt8() {
        return checked((byte)ReadUInt64());
    }

    public int ReadInt32() {
        return checked((int)ReadInt64());
    }

    public long ReadInt64() {
        byte code = ReadRawByte();
        if (code <= 0x7f)
            return code;
        if (code >= 0xe0)
            return (sbyte)code;
        switch (code) {
            case 0xcc:
                return ReadRawByte();
            case 0xcd:
                return ReadUInt16BE();
            case 0xce:
                return ReadUInt32BE();
            case 0xcf:
                return checked((long)ReadUInt64BE());
            case 0xd0:
                return (sbyte)ReadRawByte();
            case 0xd1:
                return (short)ReadUInt16BE();
            case 0xd2:
                return (int)ReadUInt32BE();
            case 0xd3:
                return (long)ReadUInt64BE();
            default:
                throw new WireFormatException($"Expected integer, got 0x{code:x2}.");
        }
    }

    public ulong ReadUInt64() {
        byte code = ReadRawByte();
        if (code <= 0x7f)
            return code;
        if (code >= 0xe0)
            return unchecked((ulong)(sbyte)code);
        switch (code) {
            case 0xcc:
                return ReadRawByte();
            case 0xcd:
                return ReadUInt16BE();
            case 0xce:
                return ReadUInt32BE();
            case 0xcf:
                return ReadUInt64BE();
            case 0xd0:
                return unchecked((ulong)(sbyte)ReadRawByte());
            case 0xd1:
                return unchecked((ulong)(short)ReadUInt16BE());
            case 0xd2:
                return unchecked((ulong)(int)ReadUInt32BE());
            case 0xd3:
                return unchecked((ulong)(long)ReadUInt64BE());
            default:
                throw new WireFormatException($"Expected integer, got 0x{code:x2}.");
        }
    }

    public double ReadDouble() {
        byte code = ReadRawByte();
        if (code == 0xcb)
            return BitConverter.Int64BitsToDouble((long)ReadUInt64BE());
        throw new WireFormatException($"Expected float64, got 0x{code:x2}.");
    }

    public string? ReadString() {
        byte code = ReadRawByte();
        if (code == 0xc0)
            return null;
        int len;
        if ((code & 0xe0) == 0xa0)
            len = code & 0x1f;
        else if (code == 0xd9)
            len = ReadRawByte();
        else if (code == 0xda)
            len = ReadUInt16BE();
        else if (code == 0xdb)
            len = checked((int)ReadUInt32BE());
        else
            throw new WireFormatException($"Expected string, got 0x{code:x2}.");
        EnsureAvailable(len);
        string result = Encoding.UTF8.GetString(_data, _pos, len);
        _pos += len;
        return result;
    }

    public byte[]? ReadBinary() {
        byte code = ReadRawByte();
        if (code == 0xc0)
            return null;
        int len;
        if (code == 0xc4)
            len = ReadRawByte();
        else if (code == 0xc5)
            len = ReadUInt16BE();
        else if (code == 0xc6)
            len = checked((int)ReadUInt32BE());
        else
            throw new WireFormatException($"Expected binary, got 0x{code:x2}.");
        EnsureAvailable(len);
        byte[] result = new byte[len];
        Array.Copy(_data, _pos, result, 0, len);
        _pos += len;
        return result;
    }

    private byte ReadRawByte() {
        if (_pos >= _data.Length)
            throw new WireFormatException("Unexpected end of data.");
        return _data[_pos++];
    }

    private ushort ReadUInt16BE() {
        EnsureAvailable(2);
        ushort value = (ushort)((_data[_pos] << 8) | _data[_pos + 1]);
        _pos += 2;
        return value;
    }

    private uint ReadUInt32BE() {
        EnsureAvailable(4);
        uint value = ((uint)_data[_pos] << 24)
            | ((uint)_data[_pos + 1] << 16)
            | ((uint)_data[_pos + 2] << 8)
            | _data[_pos + 3];
        _pos += 4;
        return value;
    }

    private ulong ReadUInt64BE() {
        EnsureAvailable(8);
        ulong value = ((ulong)_data[_pos] << 56)
            | ((ulong)_data[_pos + 1] << 48)
            | ((ulong)_data[_pos + 2] << 40)
            | ((ulong)_data[_pos + 3] << 32)
            | ((ulong)_data[_pos + 4] << 24)
            | ((ulong)_data[_pos + 5] << 16)
            | ((ulong)_data[_pos + 6] << 8)
            | _data[_pos + 7];
        _pos += 8;
        return value;
    }

    private void EnsureAvailable(int count) {
        if (_pos + count > _data.Length)
            throw new WireFormatException("Unexpected end of data.");
    }
}
