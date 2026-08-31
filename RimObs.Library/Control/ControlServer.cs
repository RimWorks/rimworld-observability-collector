using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Library.Control;

internal sealed class ControlServer {
    private readonly string _frameworkPackageId;
    private readonly Func<IEnumerable<Assembly>> _assemblies;
    private readonly HttpListener _listener;
    private Thread? _thread;
    private volatile bool _running;

    public ControlServer(string secret, string frameworkPackageId)
        : this(secret, frameworkPackageId, AssemblyIndex.Enumerate) { }

    public ControlServer(string secret, string frameworkPackageId, Func<IEnumerable<Assembly>> assemblies) {
        Secret = secret;
        _frameworkPackageId = frameworkPackageId;
        _assemblies = assemblies;
        Port = PickFreeLoopbackPort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public int Port { get; }

    public string Secret { get; }

    public void Start() {
        _listener.Start();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "RimObs-Control" };
        _thread.Start();
    }

    public void Stop() {
        _running = false;
        _listener.Stop();
        _thread?.Join(System.TimeSpan.FromSeconds(2));
    }

    private void Loop() {
        while (_running) {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); }
            catch { return; }
            try { Handle(ctx); }
            catch { /* swallow per-request errors; never crash the listener */ }
            finally { ctx.Response.Close(); }
        }
    }

    private void Handle(HttpListenerContext ctx) {
        string? presented = ctx.Request.Headers[ControlProtocol.SecretHeader];
        if (!ConstantTimeEquals(presented, Secret)) {
            ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        string path = ctx.Request.Url?.AbsolutePath ?? "/";
        string method = ctx.Request.HttpMethod;

        if (method == "POST" && path == "/search") { HandleSearch(ctx); return; }
        if (method == "POST" && path == "/patch") { HandlePatch(ctx); return; }
        if (method == "GET" && path == "/patches") { HandlePatchList(ctx); return; }
        if (method == "DELETE" && path.StartsWith("/patch/", StringComparison.Ordinal)) {
            HandleUnpatch(ctx, path); return;
        }

        ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
    }

    private void HandleSearch(HttpListenerContext ctx) {
        byte[] body = ReadBody(ctx);
        ControlSearchRequest req = WireCodec.Deserialize<ControlSearchRequest>(body);
        ControlSearchResponse res = ControlSearchService.Run(req, _assemblies());
        WriteResponse(ctx, WireCodec.Serialize(res));
    }

    private void HandlePatch(HttpListenerContext ctx) {
        byte[] body = ReadBody(ctx);
        ControlPatchRequest req = WireCodec.Deserialize<ControlPatchRequest>(body);

        MethodResolveResult resolved = MethodResolver.Resolve(req.TypeFullName, req.MethodName, req.ParamTypeFullNames, _assemblies());
        if (resolved.Refused) {
            WriteResponse(ctx, WireCodec.Serialize(new ControlPatchResponse {
                Status = PatchStatus.Refused,
                ErrorReason = resolved.Reason,
            }), HttpStatusCode.BadRequest);
            return;
        }

        ApplyResult? apply = null;
        ControlOp op = new ControlOp(ControlOpKind.Patch,
            () => apply = PatchRegistry.Apply(_frameworkPackageId, resolved.Method!, resolved.Signature));
        ControlServices.Queue.Enqueue(op);

        if (!op.Wait(TimeSpan.FromSeconds(2)) || apply is null) {
            WriteResponse(ctx, WireCodec.Serialize(new ControlPatchResponse {
                Status = PatchStatus.Refused,
                ErrorReason = "main-thread drain timed out",
            }), HttpStatusCode.GatewayTimeout);
            return;
        }

        WriteResponse(ctx, WireCodec.Serialize(new ControlPatchResponse {
            PatchId = apply.PatchId,
            SectionId = apply.SectionId,
            SectionName = apply.SectionName,
            Status = apply.Status,
            ErrorReason = apply.ErrorReason,
        }));
    }

    private static void HandlePatchList(HttpListenerContext ctx) {
        List<ControlPatchEntry> entries = new List<ControlPatchEntry>();
        foreach ((int id, string sig, int sec, PatchStatus status) in PatchRegistry.Snapshot())
            entries.Add(new ControlPatchEntry { PatchId = id, Signature = sig, SectionId = sec, Status = status });
        WriteResponse(ctx, WireCodec.Serialize(new ControlPatchListResponse { Patches = entries.ToArray() }));
    }

    private static void HandleUnpatch(HttpListenerContext ctx, string path) {
        string suffix = path.Substring("/patch/".Length);
        if (!int.TryParse(suffix, out int id)) {
            ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        bool? ok = null;
        ControlOp op = new ControlOp(ControlOpKind.Unpatch, () => ok = PatchRegistry.Remove(id));
        ControlServices.Queue.Enqueue(op);
        if (!op.Wait(TimeSpan.FromSeconds(2)) || ok is null) {
            ctx.Response.StatusCode = (int)HttpStatusCode.GatewayTimeout;
            return;
        }

        ctx.Response.StatusCode = ok == true ? (int)HttpStatusCode.OK : (int)HttpStatusCode.NotFound;
    }

    private static byte[] ReadBody(HttpListenerContext ctx) {
        using System.IO.MemoryStream ms = new System.IO.MemoryStream();
        ctx.Request.InputStream.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteResponse(HttpListenerContext ctx, byte[] body, HttpStatusCode status = HttpStatusCode.OK) {
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/x-msgpack";
        ctx.Response.ContentLength64 = body.Length;
        ctx.Response.OutputStream.Write(body, 0, body.Length);
    }

    private static bool ConstantTimeEquals(string? a, string b) {
        if (a is null) return false;
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static int PickFreeLoopbackPort() {
        TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
