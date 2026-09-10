using System.Runtime.Serialization;

namespace RimWorks.RimObs.Config;

[DataContract]
internal sealed class CollectorSamplingConfig {
    [DataMember(Name = "max_capture_depth")]
    public int? MaxCaptureDepth { get; set; }
}
