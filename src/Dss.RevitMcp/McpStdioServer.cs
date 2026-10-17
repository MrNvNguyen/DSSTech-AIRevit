using System.Text.Json;
using Dss.McpContracts;

namespace Dss.RevitMcp;

internal static class McpStdioServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task RunAsync(TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        string? line;
        while ((line = await stdin.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var response = HandleLine(line);
                if (response is not null)
                    await stdout.WriteLineAsync(response).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await stderr.WriteLineAsync($"sidecar error: {ex.Message}").ConfigureAwait(false);
            }
        }
    }

    private static string? HandleLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("method", out var methodEl))
            return ErrorResponse(default, -32600, "Invalid Request", "Missing method.");

        var method = methodEl.GetString() ?? "";
        var hasId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

        if (!hasId)
        {
            if (method.Equals("notifications/initialized", StringComparison.OrdinalIgnoreCase))
                return null;
            return null;
        }

        return method switch
        {
            _ when method.Equals("initialize", StringComparison.OrdinalIgnoreCase) => HandleInitialize(idEl),
            _ when method.Equals("ping", StringComparison.OrdinalIgnoreCase) => HandlePing(idEl),
            _ when method.Equals("tools/list", StringComparison.OrdinalIgnoreCase) => HandleToolsList(idEl),
            _ when method.Equals("tools/call", StringComparison.OrdinalIgnoreCase) => HandleToolsCall(idEl, root),
            _ => ErrorResponse(idEl, -32601, "Method not found", method)
        };
    }

    private static string HandleInitialize(JsonElement id)
    {
        var result = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { tools = new { } },
            serverInfo = new { name = "DSSTech Revit MCP", version = "0.1.0" }
        };
        return SuccessResponse(id, result);
    }

    private static string HandlePing(JsonElement id)
    {
        var pipeResponse = ForwardToHost(id, "dss.ping", default);
        if (pipeResponse is null)
            return McpOffResponse(id);
        return pipeResponse;
    }

    private static string HandleToolsList(JsonElement id)
    {
        if (!PipeClient.SessionReady(out var session))
            return McpOffResponse(id);

        using var client = new PipeClient();
        if (!client.TryConnect(session))
            return McpOffResponse(id);

        var requestJson = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = IdToObject(id), method = "tools/list" }, JsonOptions);
        var responseJson = client.SendRequest(requestJson);
        if (responseJson is null)
            return McpOffResponse(id);

        if (TryGetHostError(responseJson))
        {
            var pingJson = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "dss.ping" }, JsonOptions);
            var pingResponse = client.SendRequest(pingJson);
            if (pingResponse is null || TryGetHostError(pingResponse))
                return SurfaceHostResponse(id, responseJson);

            var fallback = new
            {
                tools = new[]
                {
                    new
                    {
                        name = "ping",
                        description = "Health check the DSSTech Host",
                        inputSchema = new { type = "object", properties = new { } }
                    }
                }
            };
            return SuccessResponse(id, fallback);
        }

        return SurfaceHostResponse(id, responseJson);
    }

    private static string HandleToolsCall(JsonElement id, JsonElement root)
    {
        if (!PipeClient.SessionReady(out var session))
            return McpOffResponse(id);

        using var client = new PipeClient();
        if (!client.TryConnect(session))
            return McpOffResponse(id);

        var forward = RebuildRequestJson(root);
        var responseJson = client.SendRequest(forward);
        if (responseJson is null)
            return McpOffResponse(id);

        return MapToolsCallResponse(id, responseJson);
    }

    private static string? ForwardToHost(JsonElement id, string hostMethod, JsonElement paramsEl)
    {
        if (!PipeClient.SessionReady(out var session))
            return null;

        using var client = new PipeClient();
        if (!client.TryConnect(session))
            return null;

        object? parameters = paramsEl.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : JsonSerializer.Deserialize<object>(paramsEl.GetRawText());

        var requestJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = IdToObject(id),
            method = hostMethod,
            @params = parameters
        }, JsonOptions);

        return client.SendRequest(requestJson);
    }

    private static string RebuildRequestJson(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            if (root.TryGetProperty("id", out var id))
            {
                writer.WritePropertyName("id");
                id.WriteTo(writer);
            }

            if (root.TryGetProperty("method", out var method))
            {
                writer.WriteString("method", method.GetString());
            }

            if (root.TryGetProperty("params", out var p) &&
                p.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            {
                writer.WritePropertyName("params");
                p.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string MapToolsCallResponse(JsonElement requestId, string hostResponseJson)
    {
        using var doc = JsonDocument.Parse(hostResponseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorEl))
            return SurfaceHostResponse(requestId, hostResponseJson);

        if (!root.TryGetProperty("result", out var resultEl))
            return ErrorResponse(requestId, -32603, "Internal error", "Host response missing result.");

        if (resultEl.ValueKind == JsonValueKind.Object &&
            resultEl.TryGetProperty("content", out _))
        {
            return SurfaceHostResponse(requestId, hostResponseJson);
        }

        var wrapped = new
        {
            content = new[]
            {
                new { type = "text", text = resultEl.GetRawText() }
            },
            isError = false
        };
        return SuccessResponse(requestId, wrapped);
    }

    private static string SurfaceHostResponse(JsonElement requestId, string hostResponseJson)
    {
        using var doc = JsonDocument.Parse(hostResponseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorEl))
        {
            var message = errorEl.TryGetProperty("message", out var msgEl) ? ReadScalar(msgEl, "error") : "error";
            var hasCode = errorEl.TryGetProperty("code", out var codeEl);
            var codeStr = hasCode ? ReadScalar(codeEl, "") : null;
            if (string.Equals(codeStr, "mcp_off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message, "mcp_off", StringComparison.OrdinalIgnoreCase))
                return McpOffResponse(requestId);

            var numeric = -32000;
            if (hasCode && codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var n))
                numeric = n;

            return ErrorResponse(requestId, numeric, message, message);
        }

        if (root.TryGetProperty("result", out var resultEl))
            return SuccessResponse(requestId, JsonSerializer.Deserialize<object>(resultEl.GetRawText())!);

        return ErrorResponse(requestId, -32603, "Internal error", "Invalid host response.");
    }

    private static bool TryGetHostError(string hostResponseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(hostResponseJson);
            return doc.RootElement.TryGetProperty("error", out _);
        }
        catch
        {
            return false;
        }
    }

    private static string ReadScalar(JsonElement element, string fallback) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? fallback,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
            _ => fallback
        };

    private static string McpOffResponse(JsonElement id) =>
        ErrorResponse(id, -32000, "mcp_off", "Revit DSSTech MCP is Off or Revit is not running.");

    private static string SuccessResponse(JsonElement id, object result) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id = IdToObject(id), result }, JsonOptions);

    private static string ErrorResponse(JsonElement id, int code, string message, string? data = null)
    {
        object? idObj = IdToObject(id);
        if (data is null)
            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idObj, error = new { code, message } }, JsonOptions);
        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idObj, error = new { code, message, data } }, JsonOptions);
    }

    private static object? IdToObject(JsonElement id) =>
        id.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.Number => id.TryGetInt64(out var n) ? n : id.GetDouble(),
            JsonValueKind.String => id.GetString(),
            _ => id.GetRawText()
        };
}
