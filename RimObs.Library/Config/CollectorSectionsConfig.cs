using System.Runtime.Serialization;

namespace RimWorks.RimObs.Config;

[DataContract]
internal sealed class CollectorSectionsConfig {
    [DataMember(Name = "disabled")]
    public string[]? Disabled { get; set; }
}
