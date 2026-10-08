using System.IO;
using Autodesk.Revit.DB;
using QSNormEngine.Core;

namespace QSNormEngine.Revit;

internal static class NormWriteBack
{
    public static WriteBackResult WriteResults(Document doc, IEnumerable<NormSelectionResult> results, bool officialOnly)
    {
        var eligible = results
            .Where(r => (!officialOnly || r.CanIssueOfficially) && !string.IsNullOrWhiteSpace(r.NormCode))
            .ToList();
        if (eligible.Count == 0)
        {
            return new WriteBackResult
            {
                WrittenCount = 0,
                SkippedMissingBinding = 0,
                Message = officialOnly
                    ? "Không có dòng Official-ready để ghi. Bổ sung tham số thiếu hoặc chạy Setup Parameters trước."
                    : "Không có dòng nào có NormCode để ghi."
            };
        }

        var definitionPath = Path.Combine(PluginPaths.Root, "Config", "QS_SharedParameters.txt");
        var written = 0;
        var skippedBinding = 0;

        using var tx = new Transaction(doc, "DSSTech write-back");
        tx.Start();
        try
        {
            SharedParameterSetup.EnsureBound(doc, definitionPath);

            foreach (var result in eligible)
            {
                if (!TryGetElement(doc, result.ElementId, out var element)) continue;

                if (!TrySetParam(element, "QS_NormCode", result.NormCode)) skippedBinding++;
                if (!TrySetParam(element, "QS_NormStatus", result.Status.ToString())) skippedBinding++;
                if (!TrySetParam(element, "QS_NormStamp", DateTime.UtcNow.ToString("O"))) skippedBinding++;
                written++;
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            if (tx.HasStarted()) tx.RollBack();
            return new WriteBackResult
            {
                WrittenCount = 0,
                SkippedMissingBinding = 0,
                Message = $"Ghi tham số thất bại: {ex.Message}\n\nHãy chạy Setup Parameters một lần, rồi thử lại.",
                Failed = true
            };
        }

        var msg = written == 0
            ? "Không ghi được cấu kiện nào — tham số QS_* chưa bind. Chạy Setup Parameters."
            : $"Đã ghi {written} cấu kiện.";
        if (skippedBinding > 0)
            msg += $"\n({skippedBinding} lần ghi param bị bỏ qua do chưa bind/read-only.)";

        return new WriteBackResult { WrittenCount = written, SkippedMissingBinding = skippedBinding, Message = msg };
    }

    private static bool TryGetElement(Document doc, string elementId, out Element element)
    {
        element = null!;
        if (long.TryParse(elementId, out var idValue))
        {
            element = doc.GetElement(new ElementId(idValue));
            return element is not null;
        }
        if (ElementId.TryParse(elementId, out var eid))
        {
            element = doc.GetElement(eid);
            return element is not null;
        }
        return false;
    }

    private static bool TrySetParam(Element element, string name, string value)
    {
        var p = element.LookupParameter(name);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
        p.Set(value);
        return true;
    }
}

internal sealed class WriteBackResult
{
    public int WrittenCount { get; init; }
    public int SkippedMissingBinding { get; init; }
    public string Message { get; init; } = "";
    public bool Failed { get; init; }
}
