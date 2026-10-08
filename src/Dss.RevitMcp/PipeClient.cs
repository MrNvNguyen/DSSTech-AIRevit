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
            _stream = new NamedPipeClientStream(".", session.PipeName, PipeDirection.InOut, PipeOptions.None);
            _stream.Connect(timeoutMs);
            return _stream.IsConnected;
        }
        catch
        {
            DisposeStream();
            return false;
        }
    }

    public string? SendRequest(string requestJson, int readTimeoutMs = 5000)
    {
        if (_stream is null || !_stream.IsConnected)
            return null;

        try
        {
            var frame = McpFrame.Encode(requestJson);
            _stream.Write(frame, 0, frame.Length);
            _stream.Flush();

            var buffer = new FrameBuffer();
            var deadline = Environment.TickCount64 + readTimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (McpFrame.TryReadFrame(buffer, out var responseJson))
                    return responseJson;

                if (_stream.ReadTimeout != 500)
                    _stream.ReadTimeout = 500;

                var chunk = new byte[4096];
                try
                {
                    var read = _stream.Read(chunk, 0, chunk.Length);
                    if (read <= 0)
                        return null;
                    buffer.Append(chunk.AsSpan(0, read));
                }
                catch (IOException)
                {
                    return null;
                }
                catch (TimeoutException)
                {
                    // keep polling until deadline
                }
            }

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
