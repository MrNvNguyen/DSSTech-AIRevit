using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dss.McpContracts;

public sealed class CreateOperation
{
    [JsonPropertyName("opId")]
    public string? OpId { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("fields")]
    public JsonElement Fields { get; set; }

    [JsonPropertyName("skipIfExists")]
    public bool? SkipIfExists { get; set; }

    [JsonPropertyName("dryRun")]
    public bool? DryRun { get; set; }

    [JsonPropertyName("units")]
    public string? Units { get; set; }
}

public sealed class CreateItemResult
{
    [JsonPropertyName("opId")]
    public string? OpId { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = ""; // created | skipped | failed

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
