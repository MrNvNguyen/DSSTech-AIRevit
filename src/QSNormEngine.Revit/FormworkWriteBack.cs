using System.Globalization;
using System.IO;
using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit;

/// <summary>
/// Confirm write of QS_Formwork* only. Never touches NormCode / NormStatus / WorkItemId.
/// </summary>
internal static class FormworkWriteBack
{
    public static WriteBackResult WriteResults(Document doc, IEnumerable<FormworkTakeoffRow> rows)
    {
        var eligible = rows
            .Where(r => !string.Equals(r.Status, "UNSUPPORTED", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (eligible.Count == 0)
        {
            return new WriteBackResult
            {
                Message = "Không có dòng formwork hợp lệ để ghi."
            };
        }

        var definitionPath = Path.Combine(PluginPaths.Root, "Config", "QS_SharedParameters.txt");
        var written = 0;
        var skippedBinding = 0;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        using var tx = new Transaction(doc, "DSSTech formwork write");
        tx.Start();
        try
        {
            SharedParameterSetup.EnsureBound(doc, definitionPath);

            foreach (var row in eligible)
            {
                if (!TryGetElement(doc, row.ElementId, out var element)) continue;

                var ok = true;
                ok &= TrySetDouble(element, "QS_FormworkArea", RoundM2(row.TotalAreaM2));
                ok &= TrySetDouble(element, "QS_FormworkSide", RoundM2(row.SideAreaM2));
                ok &= TrySetDouble(element, "QS_FormworkBottom", RoundM2(row.BottomAreaM2));
                // Audit fields (may be hidden on Properties) — best-effort.
                TrySetDouble(element, "QS_FormworkDeduct", RoundM2(row.DeductAreaM2));
                TrySetDouble(element, "QS_FormworkWaitPour", RoundM2(row.WaitPourAreaM2));
                TrySetYesNo(element, "QS_FormworkGroundContact", row.GroundContactDropBottom);
                TrySetText(element, "QS_FormworkConfidence", row.Confidence);
                TrySetText(element, "QS_FormworkStamp", stamp);

                if (!ok) skippedBinding++;
                else written++;
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            if (tx.HasStarted()) tx.RollBack();
            return new WriteBackResult
            {
                Failed = true,
                Message = $"Ghi formwork thất bại: {ex.Message}\n\nHãy chạy Setup Parameters một lần, rồi thử lại."
            };
        }

        var msg = written == 0
            ? "Không ghi được cấu kiện nào — tham số QS_Formwork* chưa bind. Chạy Setup Parameters."
            : $"Đã ghi QS_Formwork* cho {written}/{eligible.Count} cấu kiện (Area/Side/Bottom + audit).";
        if (skippedBinding > 0)
            msg += $"\n({skippedBinding} cấu kiện thiếu bind / read-only trên Area/Side/Bottom.)";
        msg += "\nKhông ghi QS_NormCode / QS_NormStatus / QS_WorkItemId.";

        return new WriteBackResult { WrittenCount = written, SkippedMissingBinding = skippedBinding, Message = msg };
    }

    private static bool TryGetElement(Document doc, string elementId, out Element element)
    {
        element = null!;
        if (long.TryParse(elementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
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

    private static bool TrySetDouble(Element element, string name, double value)
    {
        var p = element.LookupParameter(name);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
        p.Set(value);
        return true;
    }

    private static bool TrySetText(Element element, string name, string value)
    {
        var p = element.LookupParameter(name);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
        p.Set(value ?? "");
        return true;
    }

    private static bool TrySetYesNo(Element element, string name, bool value)
    {
        var p = element.LookupParameter(name);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.Integer) return false;
        p.Set(value ? 1 : 0);
        return true;
    }

    private static double RoundM2(double m2) =>
        Math.Round(m2, 3, MidpointRounding.AwayFromZero);
}
