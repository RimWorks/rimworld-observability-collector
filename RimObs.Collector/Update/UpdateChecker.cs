using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RimWorks.RimObs.Collector.Update;

public static class UpdateChecker {
    private static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static ReleaseInfo? SelectUpdate(string currentVersion, IEnumerable<ReleaseInfo> releases) {
        if (releases is null)
            return null;
        if (!SemVer.TryParse(currentVersion, out SemVer? current) || current is null)
            return null;

        ReleaseInfo? best = null;
        SemVer? bestVer = null;
        foreach (ReleaseInfo r in releases) {
            if (!IsEligibleUpgrade(r, current, out SemVer? v) || v is null)
                continue;
            if (bestVer is null || SemVer.Compare(v, bestVer) > 0) {
                best = r;
                bestVer = v;
            }
        }

        return best;
    }

    private static bool IsEligibleUpgrade(ReleaseInfo r, SemVer current, out SemVer? version) {
        version = null;
        if (r is null)
            return false;
        if (r.Draft || r.Prerelease)
            return false;
        if (!SemVer.TryParse(r.TagName, out SemVer? v) || v is null)
            return false;
        if (v.IsPrerelease)
            return false;
        if (SemVer.Compare(v, current) <= 0)
            return false;
        version = v;
        return true;
    }

    public static async Task<ReleaseInfo?> CheckAsync(
        HttpClient client,
        string currentVersion,
        string owner,
        string repo,
        CancellationToken ct) {
        string url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=30";
        using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!req.Headers.UserAgent.TryParseAdd("RimObs-Collector"))
            req.Headers.UserAgent.ParseAdd("RimObs-Collector/1.0");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using System.IO.Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        List<ReleaseInfo>? releases = await JsonSerializer
            .DeserializeAsync<List<ReleaseInfo>>(stream, JsonOpts, ct)
            .ConfigureAwait(false);
        return releases is null ? null : SelectUpdate(currentVersion, releases);
    }
}
