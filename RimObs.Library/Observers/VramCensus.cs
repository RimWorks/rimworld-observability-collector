using System;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Observers;

/// <summary>Pure half of the VRAM census, so totals and the top table are testable off Unity.</summary>
internal sealed class VramCensus {
    public const byte KindTexture = 0;
    public const byte KindMesh = 1;
    public const byte KindRenderTarget = 2;

    private readonly long[] _topBytes;
    private readonly byte[] _topKinds;
    private readonly string[] _topNames;
    private int _topUsed;

    private long _textureBytes;
    private long _meshBytes;
    private long _rtBytes;
    private int _textureCount;
    private int _meshCount;
    private int _rtCount;

    public VramCensus(int topCount = 24) {
        _topBytes = new long[topCount];
        _topKinds = new byte[topCount];
        _topNames = new string[topCount];
    }

    /// <summary>True when this size would enter the top table, so callers resolve names only then.</summary>
    public bool Qualifies(long bytes) {
        if (_topUsed < _topBytes.Length)
            return true;
        for (int i = 0; i < _topBytes.Length; i++) {
            if (bytes > _topBytes[i])
                return true;
        }
        return false;
    }

    public void Add(byte kind, long bytes, string? name) {
        switch (kind) {
            case KindMesh:
                _meshBytes += bytes;
                _meshCount++;
                break;
            case KindRenderTarget:
                _rtBytes += bytes;
                _rtCount++;
                break;
            default:
                _textureBytes += bytes;
                _textureCount++;
                break;
        }

        if (name is null)
            return;
        if (_topUsed < _topBytes.Length) {
            _topBytes[_topUsed] = bytes;
            _topKinds[_topUsed] = kind;
            _topNames[_topUsed] = name;
            _topUsed++;
            return;
        }

        int min = 0;
        for (int i = 1; i < _topBytes.Length; i++) {
            if (_topBytes[i] < _topBytes[min])
                min = i;
        }
        if (bytes <= _topBytes[min])
            return;
        _topBytes[min] = bytes;
        _topKinds[min] = kind;
        _topNames[min] = name;
    }

    public VramBatch ToBatch(long driverBytes) {
        int[] order = new int[_topUsed];
        for (int i = 0; i < _topUsed; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => _topBytes[b].CompareTo(_topBytes[a]));

        string[] names = new string[_topUsed];
        byte[] kinds = new byte[_topUsed];
        long[] bytes = new long[_topUsed];
        for (int i = 0; i < _topUsed; i++) {
            names[i] = _topNames[order[i]];
            kinds[i] = _topKinds[order[i]];
            bytes[i] = _topBytes[order[i]];
        }

        return new VramBatch {
            DriverBytes = driverBytes,
            TextureBytes = _textureBytes,
            MeshBytes = _meshBytes,
            RenderTargetBytes = _rtBytes,
            TextureCount = _textureCount,
            MeshCount = _meshCount,
            RenderTargetCount = _rtCount,
            TopNames = names,
            TopKinds = kinds,
            TopBytes = bytes,
        };
    }
}
