using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dss.McpContracts;

namespace Dss.McpHost.Tools;

/// <summary>
/// The lessons learnt fixing CAD → Revit modelling (Knowledge/LESSONS.md, embedded): what an agent must keep to before
/// it models anything, on every machine the add-in is installed on.
/// </summary>
internal static class LessonsTool
{
    private const string Resource = "Dss.McpHost.Knowledge.LESSONS.md";

    public static string Text { get; } = Load();

    private static string Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
        if (stream is null)
            return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Topic ids, as written in the headings: "## [mep-tray] …".</summary>
    public static IReadOnlyList<string> Topics() =>
        Regex.Matches(Text, @"^## \[([\w-]+)\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

    /// <summary>The sections whose id or heading holds <paramref name="topic"/> (all of them when empty), with the intro.</summary>
    public static McpResult Execute(JsonElement args)
    {
        if (Text.Length == 0)
            return McpResult.Fail("lessons_missing", "Knowledge/LESSONS.md is not embedded in this build.");
        var topic = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("topic", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()?.Trim() ?? ""
            : "";
        var parts = Regex.Split(Text, @"(?=^## )", RegexOptions.Multiline);
        var intro = parts[0];
        var sections = parts.Skip(1)
            .Where(s => topic.Length == 0 || s.Split('\n')[0].IndexOf(topic, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        if (sections.Count == 0)
            return McpResult.Fail("unknown_topic", $"No lessons for '{topic}'. Topics: {string.Join(", ", Topics())}.");
        var payload = new { topics = Topics(), lessons = intro + string.Concat(sections) };
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return McpResult.Success(doc.RootElement.Clone());
    }
}
