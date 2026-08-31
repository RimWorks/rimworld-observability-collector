namespace RimWorks.RimObs.Collector.Security;

internal static class BearerHeader {
    private const string Scheme = "Bearer ";

    public static string? ExtractToken(string? authorizationHeader) {
        if (string.IsNullOrEmpty(authorizationHeader))
            return null;
        if (!authorizationHeader.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            return null;
        string remainder = authorizationHeader.Substring(Scheme.Length).Trim();
        return remainder.Length == 0 ? null : remainder;
    }
}
