using System.Collections.Generic;
using RimWorks.RimObs.Collector.Security;

namespace RimWorks.RimObs.Collector.Config;

public sealed class SecurityOptions {
    public bool CsrfOriginCheckEnabled { get; set; } = true;
    public string CliBearerTokenEnvVar { get; set; } = CollectorToken.EnvVarName;

    // extra exact origins for names the interface scan cannot know, like tailscale magicdns.
    public List<string> AllowedOrigins { get; set; } = [];
}
