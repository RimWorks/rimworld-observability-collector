using System.Net;
using System.Net.Sockets;
using System.Threading;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Collector.Tests.Stubs;

public sealed class StubControlServer : System.IDisposable {
    private readonly HttpListener _listener;
    private readonly string _secret;
    private Thread? _thread;
    private volatile bool _running;

    public System.Func<ControlSearchRequest, ControlSearchResponse>? OnSearch { get; set; }
    public System.Func<ControlPatchRequest, ControlPatchResponse>? OnPatch { get; set; }
    public System.Func<ControlPatchListResponse>? OnList { get; set; }
    public System.Func<int, bool>? OnUnpatch { get; set; }

    public int Port { get; }

    public StubControlServer(string secret) {
        _secret = secret;
        TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public void Start() {
        _listener.Start();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true };
        _thread.Start();
    }

    public void Dispose() {
        _running = false;
        _listener.Stop();
        _thread?.Join(System.TimeSpan.FromSeconds(1));
    }

    private void Loop() {
        while (_running) {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); }
            catch { return; }
            try { Handle(ctx); }
            catch { /* swallow */ }
            finally { ctx.Response.Close(); }
        }
    }

    private void Handle(HttpListenerContext ctx) {
        if (ctx.Request.Headers["X-RimObs-Control"] != _secret) {
            ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }
        string path = ctx.Request.Url?.AbsolutePath ?? "/";
        string method = ctx.Request.HttpMethod;
        byte[] body;
        using (System.IO.MemoryStream ms = new()) {
            ctx.Request.InputStream.CopyTo(ms);
            body = ms.ToArray();
        }

        if (method == "POST" && path == "/search") {
            ControlSearchRequest req = WireCodec.Deserialize<ControlSearchRequest>(body);
            Write(ctx, WireCodec.Serialize(OnSearch?.Invoke(req) ?? new ControlSearchResponse()));
            return;
        }
        if (method == "POST" && path == "/patch") {
            ControlPatchRequest req = WireCodec.Deserialize<ControlPatchRequest>(body);
            Write(ctx, WireCodec.Serialize(OnPatch?.Invoke(req) ?? new ControlPatchResponse { Status = PatchStatus.Active, PatchId = 1 }));
            return;
        }
        if (method == "GET" && path == "/patches") {
            Write(ctx, WireCodec.Serialize(OnList?.Invoke() ?? new ControlPatchListResponse()));
            return;
        }
        if (method == "DELETE" && path.StartsWith("/patch/", System.StringComparison.Ordinal)) {
            int id = int.Parse(path.Substring("/patch/".Length));
            bool ok = OnUnpatch?.Invoke(id) ?? true;
            ctx.Response.StatusCode = ok ? 200 : 404;
            return;
        }
        ctx.Response.StatusCode = 404;
    }

    private static void Write(HttpListenerContext ctx, byte[] body) {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentLength64 = body.Length;
        ctx.Response.OutputStream.Write(body, 0, body.Length);
    }
}
