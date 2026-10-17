using System.Text.Json;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Dss.McpContracts;
using Dss.McpHost.Http;

namespace Dss.McpHost;

public sealed class McpHostService
{
    public static McpHostService Instance { get; } = new();

    private readonly object _gate = new();
    private readonly McpRequestQueue _queue = new();
    private readonly McpHostRuntimeState _runtime = new();
    private readonly McpToolRegistry _registry = new();

    private McpExternalEventHandler? _handler;
    private ExternalEvent? _externalEvent;
    private McpNamedPipeServer? _pipeServer;
    private McpHttpServer? _httpServer;
    private UIControlledApplication? _application;
    private string _pipeName = "";
    private DateTime _startedUtc;
    private int _revitThreadId;

    private McpHostService()
    {
        _registry.Register(new Tools.HostBuiltInProvider());
    }

    public McpToolRegistry Registry => _registry;

    public void RegisterProvider(Dss.McpContracts.IMcpToolProvider provider) =>
        _registry.Register(provider);

    public bool IsOn => _runtime.IsOn;
    public McpPermission Permission => _runtime.Permission;
    public string? HttpUrl => _httpServer?.HttpUrl;
    public string? HttpBearerToken => _httpServer?.BearerToken;

    public void Start(UIControlledApplication application)
    {
        // OnStartup runs on the Revit API thread. DispatchSync must not Wait there.
        _revitThreadId = Environment.CurrentManagedThreadId;
        lock (_gate)
        {
            if (_runtime.IsOn)
                return;

            _application = application;
            _runtime.RevitYear = ParseRevitYear(application.ControlledApplication.VersionNumber);
            _runtime.IsOn = true;
            _runtime.Permission = McpPermission.ReadCreate;
            _startedUtc = DateTime.UtcNow;
            _pipeName = $"DSSTech.Mcp.{Environment.ProcessId}";

            try
            {
                _handler = new McpExternalEventHandler(_queue, () => _runtime, _registry);
                _externalEvent = ExternalEvent.Create(_handler);
                // Raise() during Execute is ignored. Idling retries a non-empty queue.
                application.Idling += OnIdling;
                _pipeServer = new McpNamedPipeServer(_pipeName, _queue, () => _runtime, RaiseExternalEvent);
                _pipeServer.Start();

                _httpServer = new McpHttpServer(json => DispatchSync(json), () => _runtime.IsOn);
                _httpServer.TryStart();

                WriteSessionFile();
                McpLog.Write($"mcp start pid={Environment.ProcessId} pipe={_pipeName} year={_runtime.RevitYear}");
            }
            catch (Exception ex)
            {
                McpLog.Write($"mcp start failed: {ex.Message}");
                Stop();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_runtime.IsOn)
                return;

            _runtime.IsOn = false;
            if (_application is not null)
                _application.Idling -= OnIdling;
            _queue.CompleteAllWithError("mcp_off", "MCP host is off.");
            _httpServer?.Stop();
            _httpServer = null;
            _pipeServer?.Dispose();
            _pipeServer = null;
            _externalEvent?.Dispose();
            _externalEvent = null;
            _handler = null;

            WriteSessionFile();
            McpLog.Write("mcp stop");
        }
    }

    public void Toggle()
    {
        if (IsOn)
            Stop();
        else if (_application is not null)
            Start(_application);
    }

    public string DispatchSync(string requestJson, int timeoutSeconds = 30)
    {
        if (_revitThreadId != 0 && Environment.CurrentManagedThreadId == _revitThreadId)
            return JsonRpcWriter.ErrorNullId("revit_thread", "DispatchSync cannot wait on the Revit API thread.");

        if (!_runtime.IsOn)
            return JsonRpcWriter.ErrorNullId("mcp_off", "MCP host is off.");

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new McpWorkItem(requestJson, tcs);
        _queue.Enqueue(item, timeout);
        RaiseExternalEvent();
        if (!tcs.Task.Wait(timeout))
        {
            // Same lock as TryDequeue: a handler cannot claim this item after the flag is set.
            _queue.Abandon(item);
            return JsonRpcWriter.ErrorNullId("timeout", "MCP request timed out.");
        }
        return tcs.Task.Result;
    }

    private void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (!_runtime.IsOn || _queue.IsEmpty)
            return;
        try
        {
            _externalEvent?.Raise();
        }
        catch
        {
            // Stop may dispose the event between the empty check and Raise.
        }
    }

    private void RaiseExternalEvent() => _externalEvent?.Raise();

    private void WriteSessionFile()
    {
        try
        {
            var info = new McpSessionInfo
            {
                Pid = Environment.ProcessId,
                PipeName = _pipeName,
                RevitYear = _runtime.RevitYear,
                StartedUtc = _startedUtc == default ? DateTime.UtcNow.ToString("O") : _startedUtc.ToString("O"),
                Permission = _runtime.Permission.ToString(),
                On = _runtime.IsOn,
                HttpUrl = _httpServer?.HttpUrl
            };

            Directory.CreateDirectory(Path.GetDirectoryName(McpPaths.SessionFilePath)!);
            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(McpPaths.SessionFilePath, json);
        }
        catch (Exception ex)
        {
            McpLog.Write($"session write failed: {ex.Message}");
        }
    }

    private static int ParseRevitYear(string versionNumber)
    {
        if (string.IsNullOrWhiteSpace(versionNumber))
            return 0;

        var digits = new string(versionNumber.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var year) ? year : 0;
    }
}
