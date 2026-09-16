namespace RimWorks.RimObs.Wire;

public sealed class VramBatch {
    public long DriverBytes { get; set; }

    public long TextureBytes { get; set; }

    public long MeshBytes { get; set; }

    public long RenderTargetBytes { get; set; }

    public int TextureCount { get; set; }

    public int MeshCount { get; set; }

    public int RenderTargetCount { get; set; }

    public string[] TopNames { get; set; } = [];

    /// <summary>Kind per top entry: 0 texture, 1 mesh, 2 render target.</summary>
    public byte[] TopKinds { get; set; } = [];

    public long[] TopBytes { get; set; } = [];
}
