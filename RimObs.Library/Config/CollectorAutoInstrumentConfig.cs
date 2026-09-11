using System.Runtime.Serialization;

namespace RimWorks.RimObs.Config;

[DataContract]
internal sealed class CollectorAutoInstrumentConfig {
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; }

    [DataMember(Name = "filters")]
    public string? Filters { get; set; }

    [DataMember(Name = "ignore")]
    public string? Ignore { get; set; }

    [DataMember(Name = "mute_trivial")]
    public bool? MuteTrivial { get; set; }

    [DataMember(Name = "max_targets")]
    public int? MaxTargets { get; set; }
}
