using System.Globalization;
using System.IO;
using Autodesk.Revit.DB;
using QSNormEngine.Core;

namespace QSNormEngine.Revit;

/// <summary>
/// Confirm-fill QS_InOutBuilding + QS_InOutSource. Never overwrites existing InOut. Never touches SurfaceLocation.
/// </summary>
internal static class InOutWriteBack
{
    public static FillResult WriteProposed(Document doc, IEnumerable<string> elementIds)
    {
        var definitionPath = Path.Combine(PluginPaths.Root, "Config", "QS_SharedParameters.txt");
        var factory = new RevitElementFactsFactory(doc);
        var written = 0;
        var skippedExisting = 0;
        var skippedBind = 0;

        using var tx = new Transaction(doc, "DSSTech assign InOut");
        tx.Start();
        try
        {
            SharedParameterSetup.EnsureBound(doc, definitionPath);
            foreach (var id in elementIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
                    continue;
                var element = doc.GetElement(new ElementId(idValue));
                if (element?.Category is null) continue;

                var existing = element.LookupParameter("QS_InOutBuilding");
                if (existing is not null && existing.HasValue && !string.IsNullOrWhiteSpace(existing.AsString()))
                {
                    skippedExisting++;
                    continue;
                }

                var facts = factory.Create(element);
                if (!facts.Properties.TryGetValue("InOutBuilding", out var proposedObj) ||
                    proposedObj is null ||
                    string.IsNullOrWhiteSpace(proposedObj.ToString()))
                    continue;

                var value = proposedObj.ToString()!;
                var source = facts.Properties.TryGetValue("InOutSource", out var srcObj) && srcObj is not null
                    ? srcObj.ToString()!
                    : "CategoryDefault";
                if (source.Equals("Existing", StringComparison.OrdinalIgnoreCase))
                {
                    skippedExisting++;
                    continue;
                }

                var okValue = TrySetText(element, "QS_InOutBuilding", value);
                var okSrc = TrySetText(element, "QS_InOutSource", source);
                if (okValue) written++;
                else skippedBind++;
                _ = okSrc;
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            if (tx.HasStarted()) tx.RollBack();
            return new FillResult { Failed = true, Message = $"Gán InOut thất bại: {ex.Message}" };
        }

        return new FillResult
        {
            WrittenCount = written,
            Message =
                $"Đã gán InOut cho {written} cấu kiện (bỏ qua đã có: {skippedExisting}; thiếu bind: {skippedBind}).\n" +
                "Không ghi đè giá trị có sẵn. Không đụng QS_SurfaceLocation."
        };
    }

    private static bool TrySetText(Element element, string name, string value)
    {
        var p = element.LookupParameter(name);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
        p.Set(value);
        return true;
    }
}
