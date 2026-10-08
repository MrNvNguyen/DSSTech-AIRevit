using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dss.McpContracts;

public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public JsonElement Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }
}

public sealed class ToolsCallParams
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}

public static class JsonRpcWriter
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Success(JsonElement id, object result) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id = IdToObject(id), result }, Options);

    public static string Error(JsonElement id, string code, string message) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = IdToObject(id),
            error = new McpErrorBody { Code = code, Message = message }
        }, Options);

    public static string ErrorNullId(string code, string message) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = (object?)null,
            error = new McpErrorBody { Code = code, Message = message }
        }, Options);

    private static object? IdToObject(JsonElement id) =>
        id.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.Number => id.TryGetInt64(out var n) ? n : id.GetDouble(),
            JsonValueKind.String => id.GetString(),
            _ => id.GetRawText()
        };
}
