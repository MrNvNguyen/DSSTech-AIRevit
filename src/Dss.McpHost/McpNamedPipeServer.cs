using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Dss.McpContracts;

namespace Dss.McpHost;

internal sealed class McpNamedPipeServer : IDisposable
{
    private readonly McpRequestQueue _queue;
    private readonly Func<McpHostRuntimeState> _state;
    private readonly Action _raiseExternalEvent;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private readonly string _pipeName;

    public McpNamedPipeServer(string pipeName, McpRequestQueue queue, Func<McpHostRuntimeState> state, Action raiseExternalEvent)
    {
        _pipeName = pipeName;
        _queue = queue;
        _state = state;
        _raiseExternalEvent = raiseExternalEvent;
    }

    public void Start() =>
        _loopTask = Task.Run(() => PipeLoop(_cts.Token), _cts.Token);

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore shutdown race
        }

        _cts.Dispose();
    }

    private void PipeLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _state().IsOn)
        {
            try
            {
                using var server = CreateServerStream();
                server.WaitForConnection();

                while (!token.IsCancellationRequested && _state().IsOn && server.IsConnected)
                {
                    if (!TryHandleOneMessage(server, token))
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                if (token.IsCancellationRequested || !_state().IsOn)
                    break;
            }
            catch (Exception ex)
            {
                McpLog.Write($"pipe error: {ex.Message}");
                if (token.IsCancellationRequested || !_state().IsOn)
                    break;
            }
        }
    }

    private bool TryHandleOneMessage(NamedPipeServerStream server, CancellationToken token)
    {
        var buffer = new FrameBuffer();
        var header = new byte[4];
        if (!ReadExact(server, header, token))
            return false;

        buffer.Append(header);
        if (!McpFrame.TryReadFrame(buffer, out var json) || json is null)
        {
            var length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > McpFrame.MaxPayloadBytes)
                return false;

            var rest = new byte[length];
            if (!ReadExact(server, rest, token))
                return false;
            buffer.Append(rest);
            if (!McpFrame.TryReadFrame(buffer, out json) || json is null)
                return false;
        }

        if (!_state().IsOn)
        {
            WriteResponse(server, JsonRpcWriter.ErrorNullId("mcp_off", "MCP host is off."));
            return server.IsConnected;
        }

        var timeout = TimeSpan.FromSeconds(30);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new McpWorkItem(json, tcs);
        _queue.Enqueue(item, timeout);
        _raiseExternalEvent();

        var completed = tcs.Task.Wait(timeout);
        if (!completed)
            _queue.Abandon(item);
        var responseJson = completed
            ? tcs.Task.Result
            : JsonRpcWriter.ErrorNullId("timeout", "MCP request timed out after 30s.");

        WriteResponse(server, responseJson);
        return server.IsConnected;
    }

    private static void WriteResponse(NamedPipeServerStream server, string responseJson)
    {
        var frame = McpFrame.Encode(responseJson);
        server.Write(frame, 0, frame.Length);
        server.Flush();
    }

    private static bool ReadExact(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            if (token.IsCancellationRequested)
                return false;
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
                return false;
            offset += read;
        }

        return true;
    }

    private NamedPipeServerStream CreateServerStream()
    {
        var security = BuildCurrentUserPipeSecurity();

#if NET8_0_OR_GREATER
        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
#else
        return new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
#endif
    }

    private static PipeSecurity BuildCurrentUserPipeSecurity()
    {
        var ps = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
        {
            ps.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            ps.SetAccessRuleProtection(true, false);
        }

        return ps;
    }
}
