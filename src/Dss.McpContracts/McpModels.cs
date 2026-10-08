using System.Text.Json.Serialization;

namespace Dss.McpContracts;

public sealed class McpSessionInfo
{
    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("pipeName")]
    public string PipeName { get; set; } = "";

    [JsonPropertyName("revitYear")]
    public int RevitYear { get; set; }

    [JsonPropertyName("startedUtc")]
    public string StartedUtc { get; set; } = "";

    [JsonPropertyName("permission")]
    public string Permission { get; set; } = "ReadCreate";

    [JsonPropertyName("on")]
    public bool On { get; set; }

    [JsonPropertyName("httpUrl")]
    public string? HttpUrl { get; set; }
}

public sealed class PingResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("revitYear")]
    public int RevitYear { get; set; }

    [JsonPropertyName("documentTitle")]
    public string? DocumentTitle { get; set; }

    [JsonPropertyName("mcpOn")]
    public bool McpOn { get; set; }
}

public sealed class McpRequest
{
    [JsonPropertyName("json")]
    public string Json { get; set; } = "";
}

public sealed class McpResponse
{
    [JsonPropertyName("json")]
    public string Json { get; set; } = "";
}

public sealed class McpError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class McpErrorBody
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
