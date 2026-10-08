using System.IO;
using System.Globalization;
using Autodesk.Revit.DB;
using QSNormEngine.Core;

namespace QSNormEngine.Revit;

/// <summary>Fill QS_* instance parameters on elements (explicit user action only).</summary>
internal static class QsParameterFiller
{
    public static FillResult FillValues(
        Document doc,
        IEnumerable<NormSelectionResult> results,
        IReadOnlyDictionary<string, string> factValues,
        bool onlyMissingOrUnmatched)
    {
        if (factValues.Count == 0)
            return new FillResult { Message = "Chưa chọn giá trị nào để gán." };

        var definitionPath = Path.Combine(PluginPaths.Root, "Config", "QS_SharedParameters.txt");
        var targets = results
            .Where(r => !onlyMissingOrUnmatched
                        || r.Status is SelectionStatus.MissingParameters or SelectionStatus.NoNormMatched or SelectionStatus.Ambiguous
                        || r.MissingParameters.Count > 0)
            .Select(r => r.ElementId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
            return new FillResult { Message = "Không có cấu kiện phù hợp phạm vi gán (Missing / NoNormMatched / Ambiguous)." };

        var written = 0;
        var paramHits = 0;
        using var tx = new Transaction(doc, "DSSTech assign QS values");
        tx.Start();
        try
        {
            SharedParameterSetup.EnsureBound(doc, definitionPath);
            foreach (var elementId in targets)
            {
                if (!TryGetElement(doc, elementId, out var element)) continue;
                var any = false;
                foreach (var (factKey, value) in factValues)
                {
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var qsName = QsValueCatalog.ToQsParam(factKey);
                    if (qsName is null) continue;
                    if (SetParam(element, qsName, value))
                    {
                        paramHits++;
                        any = true;
                    }
                }
                if (any) written++;
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            if (tx.HasStarted()) tx.RollBack();
            return new FillResult { Failed = true, Message = $"Không gán được tham số: {ex.Message}" };
        }

        var assigned = string.Join(", ", factValues.Select(kv => $"{kv.Key}={kv.Value}"));
        return new FillResult
        {
            WrittenCount = written,
            Message = $"Đã gán [{assigned}] cho {written}/{targets.Count} cấu kiện ({paramHits} lần set param).\nChạy lại chọn mã để cập nhật."
        };
    }

    public static FillResult FillPlacementMethod(
        Document doc,
        IEnumerable<NormSelectionResult> results,
        string placementMethod,
        string? concreteSupply,
        string? pumpType)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PlacementMethod"] = placementMethod
        };
        if (!string.IsNullOrWhiteSpace(concreteSupply)) map["ConcreteSupply"] = concreteSupply!;
        if (!string.IsNullOrWhiteSpace(pumpType)) map["PumpType"] = pumpType!;
        return FillValues(doc, results, map, onlyMissingOrUnmatched: true);
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

    private static bool SetParam(Element element, string name, string value)
    {
        var p = element.LookupParameter(name);
        if (p is null || p.IsReadOnly) return false;
        if (p.StorageType == StorageType.String)
        {
            p.Set(value);
            return true;
        }
        if (p.StorageType == StorageType.Integer)
        {
            var on = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
            if (name.Contains("PrePutty", StringComparison.OrdinalIgnoreCase) || on || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                p.Set(on ? 1 : 0);
                return true;
            }
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                p.Set(i);
                return true;
            }
        }
        if (p.StorageType == StorageType.Double
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            // Shared NUMBER without length/area forge type: store raw engineering value.
            p.Set(d);
            return true;
        }
        return false;
    }
}

internal sealed class FillResult
{
    public int WrittenCount { get; init; }
    public string Message { get; init; } = "";
    public bool Failed { get; init; }
}
