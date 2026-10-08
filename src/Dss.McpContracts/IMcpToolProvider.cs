using System.Collections.Generic;
using System.Text.Json;

namespace Dss.McpContracts;

public sealed class McpTool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string InputSchemaJson { get; set; } = "{\"type\":\"object\",\"properties\":{}}";
}

public sealed class McpCallContext
{
    public string? DocumentTitle { get; set; }
    public string? DocumentPath { get; set; }
    public McpPermission Permission { get; set; }
    public int RevitYear { get; set; }

    /// <summary>
    /// Host-only session handle. Contracts do not reference RevitAPI.
    /// External modules must not depend on this object.
    /// </summary>
    public object? HostScope { get; set; }
}

public sealed class McpResult
{
    public bool Ok { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public JsonElement Data { get; set; }

    public static McpResult Success(JsonElement data) => new() { Ok = true, Data = data };

    public static McpResult Fail(string code, string message) =>
        new() { Ok = false, ErrorCode = code, Message = message };
}

public interface IMcpToolProvider
{
    string ModuleId { get; }
    IReadOnlyList<McpTool> Tools { get; }

    /// <summary>
    /// Synchronous on purpose: Revit ExternalEvent.Execute cannot await the API thread.
    /// </summary>
    McpResult Invoke(string tool, JsonElement args, McpCallContext ctx);
}
