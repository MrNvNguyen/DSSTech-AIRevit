using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DSSTech.Core;

public static class ResultExporter
{
    public static void ExportJson(IEnumerable<NormSelectionResult> results, string path)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(results, options), new UTF8Encoding(true));
    }

    public static void ExportCsv(IEnumerable<NormSelectionResult> results, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ElementId,Category,WorkItem,NormCode,NormName,Status,Quantity,QuantityUnit,NormUnit,Confidence,CanIssue,Missing,Candidates,Warnings");
        foreach (var r in results)
        {
            var row = new[]
            {
                r.ElementId,r.RevitCategory,r.WorkItemId,r.NormCode,r.NormName,r.Status.ToString(),
                r.Quantity.ToString("0.########",CultureInfo.InvariantCulture),r.QuantityUnit,r.NormUnit,
                r.Confidence.ToString("0.000",CultureInfo.InvariantCulture),r.CanIssueOfficially.ToString(),
                string.Join("|",r.MissingParameters),string.Join("|",r.CandidateCodes),string.Join("|",r.Warnings)
            };
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static void ExportResourceCsv(IEnumerable<NormSelectionResult> results, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ElementId,NormCode,ResourceId,ResourceType,ResourceName,Unit,NormQuantity,TotalQuantity,IsPercentage,Note");
        foreach (var r in results)
        foreach (var d in r.Resources)
        {
            var row = new[]
            {
                r.ElementId,r.NormCode,d.ResourceId,d.ResourceType,d.ResourceName,d.Unit,
                d.NormQuantity.ToString("0.########",CultureInfo.InvariantCulture),
                d.TotalQuantity?.ToString("0.########",CultureInfo.InvariantCulture) ?? "",
                d.IsPercentage.ToString(),d.Note
            };
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Escape(string? value)
    {
        var s = value ?? "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
