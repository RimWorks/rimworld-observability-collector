using RimWorks.RimObs.Collector.Security;

namespace RimWorks.RimObs.Collector.Config;

public sealed class SecurityOptions {
    public bool CsrfOriginCheckEnabled { get; set; } = true;
    public string CliBearerTokenEnvVar { get; set; } = CollectorToken.EnvVarName;
}
