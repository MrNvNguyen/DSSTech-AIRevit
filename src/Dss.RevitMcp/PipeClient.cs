using System.IO.Pipes;
using System.Text.Json;
using Dss.McpContracts;

namespace Dss.RevitMcp;

internal sealed class PipeClient : IDisposable
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DSSTech",
        "mcp-session.json");

    private NamedPipeClientStream? _stream;

    public static bool TryReadSession(out McpSessionInfo? session)
    {
        session = null;
        try
        {
            if (!File.Exists(SessionPath))
                return false;

            var json = File.ReadAllText(SessionPath);
            session = JsonSerializer.Deserialize<McpSessionInfo>(json);
            return session is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool SessionReady(out McpSessionInfo session)
    {
        session = new McpSessionInfo();
        if (!TryReadSession(out var info) || info is null)
            return false;
        if (!info.On || string.IsNullOrWhiteSpace(info.PipeName))
            return false;
        session = info;
        return true;
    }

    public bool TryConnect(McpSessionInfo session, int timeoutMs = 2000)
    {
        DisposeStream();
        try
        {
            // Asynchronous: the read deadline is a cancellation token; PipeStream has no ReadTimeout.
            _stream = new NamedPipeClientStream(".", session.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _stream.Connect(timeoutMs);
            return _stream.IsConnected;
        }
        catch
        {
            DisposeStream();
            return false;
        }
    }

    /// <param name="readTimeoutMs">
    /// Must stay above the host wait (30s). A shorter read makes the sidecar report mcp_off
    /// while Revit is still executing the tool.
    /// </param>
    public string? SendRequest(string requestJson, int readTimeoutMs = 35000)
    {
        if (_stream is null || !_stream.IsConnected)
            return null;

        try
        {
            var frame = McpFrame.Encode(requestJson);
            _stream.Write(frame, 0, frame.Length);
            _stream.Flush();

            var buffer = new FrameBuffer();
            var chunk = new byte[4096];
            using var cts = new CancellationTokenSource(readTimeoutMs);
            while (true)
            {
                if (McpFrame.TryReadFrame(buffer, out var responseJson))
                    return responseJson;

                var read = _stream.ReadAsync(chunk.AsMemory(), cts.Token).AsTask().GetAwaiter().GetResult();
                if (read <= 0)
                    return null;
                buffer.Append(chunk.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException)
        {
            // A late frame would desync the next request on this pipe.
            DisposeStream();
            return null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        DisposeStream();
    }

    private void DisposeStream()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // ignore
        }

        _stream = null;
    }
}
