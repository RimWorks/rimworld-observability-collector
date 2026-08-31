using System.Threading;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Instrumentation;

public sealed class SessionMetaRegistry {
    private int _controlPort;
    private string _controlSecret = string.Empty;
    private string _sessionId = string.Empty;

    public int ControlPort => Volatile.Read(ref _controlPort);
    public string ControlSecret => Volatile.Read(ref _controlSecret);
    public string SessionId => Volatile.Read(ref _sessionId);

    public bool IsAvailable => ControlPort > 0;

    public void OnSessionMeta(SessionMeta meta) {
        Volatile.Write(ref _controlPort, meta.ControlPort);
        Volatile.Write(ref _controlSecret, meta.ControlSecret);
        Volatile.Write(ref _sessionId, meta.SessionId);
    }
}
