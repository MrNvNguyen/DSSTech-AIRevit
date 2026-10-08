using System.Text.Json;
using Autodesk.Revit.UI;
using Dss.McpContracts;
using Dss.McpHost.Tools;

namespace Dss.McpHost;

internal sealed class McpExternalEventHandler : IExternalEventHandler
{
    private readonly McpRequestQueue _queue;
    private readonly Func<McpHostRuntimeState> _state;
    private readonly McpToolRegistry _registry;

    public McpExternalEventHandler(McpRequestQueue queue, Func<McpHostRuntimeState> state, McpToolRegistry registry)
    {
        _queue = queue;
        _state = state;
        _registry = registry;
    }

    public string GetName() => "DSSTech MCP Host";

    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var item))
        {
            try
            {
                if (!_state().IsOn)
                {
                    item.ResponseTcs.TrySetResult(JsonRpcWriter.Error(ParseIdElement(item.RequestJson), "mcp_off", "MCP host is off."));
                    continue;
                }

                var response = Dispatch(app, item.RequestJson);
                item.ResponseTcs.TrySetResult(response);
            }
            catch (Exception ex)
            {
                item.ResponseTcs.TrySetResult(JsonRpcWriter.Error(ParseIdElement(item.RequestJson), "internal_error", ex.Message));
            }
        }
    }

    private string Dispatch(UIApplication app, string requestJson)
    {
        var request = JsonSerializer.Deserialize<JsonRpcRequest>(requestJson);
        if (request?.Method is null)
            return JsonRpcWriter.ErrorNullId("invalid_request", "Missing method.");

        var id = request.Id;

        if (request.Method.Equals("tools/list", StringComparison.OrdinalIgnoreCase))
            return JsonRpcWriter.Success(id, ListTools());

        var runtime = _state();
        string? docTitle = null;
        string? docPath = null;
        var uidoc = app.ActiveUIDocument;
        if (uidoc?.Document is not null)
        {
            docTitle = uidoc.Document.Title;
            docPath = uidoc.Document.PathName;
        }

        if (request.Method.Equals("dss.ping", StringComparison.OrdinalIgnoreCase))
        {
            var ping = Tools.PingTool.Execute(Environment.ProcessId, runtime.RevitYear, docTitle, runtime.IsOn);
            return JsonRpcWriter.Success(id, ping);
        }

        if (!request.Method.Equals("tools/call", StringComparison.OrdinalIgnoreCase))
            return JsonRpcWriter.Error(id, "method_not_found", $"Unknown method: {request.Method}");

        ToolsCallParams? toolsParams = null;
        if (request.Params.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            toolsParams = JsonSerializer.Deserialize<ToolsCallParams>(request.Params.GetRawText());

        var toolName = toolsParams?.Name;
        if (string.IsNullOrWhiteSpace(toolName))
            return JsonRpcWriter.Error(id, "invalid_params", "tools/call requires params.name.");

        if (!_registry.TryGet(toolName, out var provider))
            return JsonRpcWriter.Error(id, "method_not_found", $"Unknown tool '{toolName}'.");

        var ctx = new McpCallContext
        {
            DocumentTitle = docTitle,
            DocumentPath = docPath,
            Permission = runtime.Permission,
            RevitYear = runtime.RevitYear,
            HostScope = new RevitCallScope { App = app, Runtime = runtime }
        };

        var args = toolsParams!.Arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? default
            : toolsParams.Arguments;
        var result = provider.Invoke(toolName, args, ctx);
        if (!result.Ok)
            return JsonRpcWriter.Error(id, result.ErrorCode ?? "tool_error", result.Message ?? "Tool failed.");
        return JsonRpcWriter.Success(id, result.Data);
    }

    private object ListTools()
    {
        var tools = new List<object>();
        foreach (var tool in _registry.List())
        {
            JsonElement schema;
            try
            {
                using var schemaDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(tool.InputSchemaJson)
                    ? "{\"type\":\"object\",\"properties\":{}}"
                    : tool.InputSchemaJson);
                schema = schemaDoc.RootElement.Clone();
            }
            catch
            {
                using var schemaDoc = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                schema = schemaDoc.RootElement.Clone();
            }

            tools.Add(new { name = tool.Name, description = tool.Description, inputSchema = schema });
        }

        return new { tools };
    }

    private static JsonElement ParseIdElement(string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            if (doc.RootElement.TryGetProperty("id", out var id))
                return id.Clone();
        }
        catch
        {
            // ignore
        }

        return default;
    }
}

internal sealed class McpHostRuntimeState
{
    public bool IsOn { get; set; }
    public int RevitYear { get; set; }
    public McpPermission Permission { get; set; } = McpPermission.ReadCreate;
}
