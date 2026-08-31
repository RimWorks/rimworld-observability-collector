using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Profile;

namespace RimWorks.RimObs.Config;

internal sealed class CollectorConfigClient {
    public const int DefaultIntervalMs = 30000;
    public const int DefaultTimeoutMs = 5000;

    private readonly string _configUrl;
    private readonly int _timeoutMs;
    private readonly PollerThread _poller;

    public CollectorConfigClient(string baseAddress, int intervalMs = DefaultIntervalMs, int timeoutMs = DefaultTimeoutMs) {
        if (string.IsNullOrEmpty(baseAddress))
            throw new ArgumentException("Base address must not be empty.", nameof(baseAddress));
        if (timeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "Timeout must be positive.");

        _configUrl = baseAddress.TrimEnd('/') + "/api/v1/config";
        _timeoutMs = timeoutMs;
        _poller = new PollerThread("RimObs.ConfigPoll", PollOnce, intervalMs);
    }

    public bool IsRunning => _poller.IsRunning;

    public void Start() => _poller.Start();

    public void Stop(int joinTimeoutMs = 2000) => _poller.Stop(joinTimeoutMs);

    internal void PollOnce() {
        string? json = Fetch();
        if (json == null)
            return;
        CollectorConfigDocument? document = CollectorConfigDocument.TryParse(json);
        if (document == null)
            return;
        ApplyToRegistry(document);
    }

    internal static void ApplyToRegistry(CollectorConfigDocument document) {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        HashSet<string> disabled = new HashSet<string>(StringComparer.Ordinal);
        string[]? names = document.Sections?.Disabled;
        if (names != null) {
            for (int i = 0; i < names.Length; i++) {
                string name = names[i];
                if (!string.IsNullOrEmpty(name))
                    disabled.Add(name);
            }
        }

        SectionRegistry.ApplyDisabledSet(disabled);
    }

    private string? Fetch() {
        try {
#pragma warning disable SYSLIB0014 // WebRequest is the robust choice under net48/Mono; obsolete only in the net10 test host.
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_configUrl);
#pragma warning restore SYSLIB0014
            request.Method = "GET";
            request.Timeout = _timeoutMs;
            request.ReadWriteTimeout = _timeoutMs;
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) {
                return reader.ReadToEnd();
            }
        }
        catch (WebException) {
            return null;
        }
        catch (IOException) {
            return null;
        }
    }
}
