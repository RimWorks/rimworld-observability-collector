using System.Runtime.Serialization;

namespace RimWorks.RimObs.Transport;

[DataContract]
public sealed class CollectorSchemaCompat {
    [DataMember(Name = "min")]
    public int Min { get; set; }

    [DataMember(Name = "max")]
    public int Max { get; set; }
}
