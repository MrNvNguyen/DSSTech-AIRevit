using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dss.McpContracts;

namespace Dss.McpHost.Http;

/// <summary>
/// Localhost-only MCP JSON-RPC over HTTP for ChatGPT connectors.
/// Bind 127.0.0.1 only. Bearer token rotated each Start.
/// </summary>
public sealed class McpHttpServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly Func<string, string> _dispatchJsonRpc;
    private readonly Func<bool> _isOn;

    public string? HttpUrl { get; private set; }
    public string? BearerToken { get; private set; }
    public int Port { get; private set; }

    public McpHttpServer(Func<string, string> dispatchJsonRpc, Func<bool> isOn)
    {
        _dispatchJsonRpc = dispatchJsonRpc;
        _isOn = isOn;
    }

    public bool TryStart(int preferredPort = 17832)
    {
        Stop();
        BearerToken = CreateToken();
        Port = preferredPort;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = preferredPort + attempt;
            var prefix = $"http://127.0.0.1:{port}/";
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                listener.Start();
                _listener = listener;
                Port = port;
                HttpUrl = $"{prefix}mcp";
                _cts = new CancellationTokenSource();
                _loop = Task.Run(() => Loop(_cts.Token));
                McpLog.Write($"http start url={HttpUrl} tokenIssued=true");
                return true;
            }
            catch
            {
                // try next port
            }
        }

        BearerToken = null;
        HttpUrl = null;
        McpLog.Write("http start failed: no free loopback port");
        return false;
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // ignore
        }

        _listener = null;
        _cts = null;
        _loop = null;
        BearerToken = null;
        HttpUrl = null;
        McpLog.Write("http stop");
    }

    public void Dispose() => Stop();

    private async Task Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => Handle(ctx), token);
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            if (!_isOn())
            {
                Write(ctx, 503, JsonRpcWriter.ErrorNullId("mcp_off", "MCP host is off."));
                return;
            }

            var auth = ctx.Request.Headers["Authorization"] ?? "";
            if (!BearerMatches(auth, BearerToken))
            {
                Write(ctx, 401, "{\"error\":\"unauthorized\"}");
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            if (ctx.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                Write(ctx, 200, JsonSerializer.Serialize(new
                {
                    ok = true,
                    service = "DSSTech Revit MCP",
                    transport = "http-jsonrpc",
                    note = "POST JSON-RPC to /mcp with Authorization Bearer"
                }));
                return;
            }

            if (!ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                Write(ctx, 405, "{\"error\":\"method_not_allowed\"}");
                return;
            }

            if (!TryReadBody(ctx.Request, out var body))
            {
                Write(ctx, 413, JsonRpcWriter.ErrorNullId("payload_too_large", "HTTP body exceeds 1 MiB."));
                return;
            }

            var response = _dispatchJsonRpc(body);
            Write(ctx, 200, response);
        }
        catch (Exception ex)
        {
            try
            {
                Write(ctx, 500, JsonRpcWriter.ErrorNullId("internal_error", ex.Message));
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void Write(HttpListenerContext ctx, int status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    private static bool TryReadBody(HttpListenerRequest request, out string body)
    {
        body = "";
        if (request.ContentLength64 > McpFrame.MaxPayloadBytes)
            return false;

        var encoding = request.ContentEncoding ?? Encoding.UTF8;
        using var reader = new StreamReader(request.InputStream, encoding);
        var buffer = new char[8192];
        var sb = new StringBuilder();
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (sb.Length + read > McpFrame.MaxPayloadBytes)
                return false;
            sb.Append(buffer, 0, read);
        }

        body = sb.ToString();
        return true;
    }

    private static bool BearerMatches(string header, string? expected)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(expected) || header.Length < prefix.Length)
            return false;
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var presented = header.Substring(prefix.Length).Trim();
        var actual = Encoding.UTF8.GetBytes(presented);
        var wanted = Encoding.UTF8.GetBytes(expected);
        return actual.Length == wanted.Length && CryptographicOperations.FixedTimeEquals(actual, wanted);
    }

    private static string CreateToken()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
