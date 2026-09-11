using System.Runtime.Serialization;

namespace RimWorks.RimObs.Config;

[DataContract]
internal sealed class CollectorSamplingConfig {
    [DataMember(Name = "max_capture_depth")]
    public int? MaxCaptureDepth { get; set; }

    [DataMember(Name = "ring_capacity")]
    public int? RingCapacity { get; set; }
}
