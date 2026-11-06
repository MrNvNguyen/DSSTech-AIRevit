using System.Globalization;
using Autodesk.Revit.DB;
using Dss.AssetInfo.Core;

namespace DSSTech.Revit.AssetInfo;

internal sealed class WritePlanItem
{
    public AssetRecord Record { get; init; } = new();
    public IReadOnlyList<ElementId> TypeIds { get; init; } = Array.Empty<ElementId>();
}

internal sealed class WriteReport
{
    public int TypesWritten { get; set; }
    public int ValuesSet { get; set; }
    public int Unchanged { get; set; }
    public List<string> KeptExisting { get; } = new();
    public HashSet<string> MissingParams { get; } = new(StringComparer.OrdinalIgnoreCase);
    public BindReport? Bind { get; set; }
    public string Error { get; set; } = "";

    public string Text
    {
        get
        {
            if (!string.IsNullOrEmpty(Error)) return Error;
            var lines = new List<string>
            {
                $"Đã ghi {ValuesSet} giá trị lên {TypesWritten} Type ({Unchanged} giá trị đã đúng, không đổi)."
            };
            if (Bind is not null) lines.Add(Bind.Text);
            if (KeptExisting.Count > 0)
                lines.Add($"Giữ nguyên {KeptExisting.Count} giá trị đang có khác file (chưa bật 'Ghi đè'). Ví dụ:\n- " +
                          string.Join("\n- ", KeptExisting.Take(8)));
            if (MissingParams.Count > 0)
                lines.Add("Type không có tham số (chưa bind cho category này): " + string.Join(", ", MissingParams.Take(15)));
            return string.Join("\n\n", lines);
        }
    }
}

/// <summary>Writes TS_* values onto Revit types. Called only after the user confirms in the window.</summary>
internal static class AssetTypeWriter
{
    public static WriteReport Write(Document doc, IReadOnlyList<WritePlanItem> plan, bool overwrite, SpecRuleSet rules)
    {
        var report = new WriteReport();
        using var tx = new Transaction(doc, "DSSTech – Ghi thông tin tài sản (TS_*)");
        tx.Start();
        try
        {
            report.Bind = AssetParameterBinder.EnsureBound(doc, AssetInfoPaths.SharedParameters, AssetTypeIndex.TargetCategories(rules));
            doc.Regenerate(); // make freshly bound TS_* visible to LookupParameter in this transaction

            foreach (var item in plan)
            foreach (var typeId in item.TypeIds)
            {
                if (doc.GetElement(typeId) is not ElementType type) continue;
                var touched = false;
                foreach (var (param, value) in item.Record.Values)
                {
                    if (!AssetParams.IsAssetParam(param) || string.IsNullOrWhiteSpace(value)) continue;
                    switch (SetValue(type, param, value, overwrite, out var old))
                    {
                        case SetOutcome.Written: report.ValuesSet++; touched = true; break;
                        case SetOutcome.Same: report.Unchanged++; break;
                        case SetOutcome.KeptExisting: report.KeptExisting.Add($"{type.Name} › {param}: '{old}' (file: '{value}')"); break;
                        case SetOutcome.Missing: report.MissingParams.Add(param); break;
                    }
                }
                if (touched) report.TypesWritten++;
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
            report.Error = $"Không ghi được, đã hoàn tác toàn bộ:\n{ex.Message}";
        }
        return report;
    }

    private enum SetOutcome { Written, Same, KeptExisting, Missing }

    private static SetOutcome SetValue(Element type, string name, string value, bool overwrite, out string old)
    {
        old = "";
        var p = type.LookupParameter(name);
        if (p is null || p.IsReadOnly) return SetOutcome.Missing;

        switch (p.StorageType)
        {
            case StorageType.String:
                old = p.AsString() ?? "";
                if (old == value) return SetOutcome.Same;
                if (old.Length > 0 && !overwrite) return SetOutcome.KeptExisting;
                return p.Set(value) ? SetOutcome.Written : SetOutcome.Missing;

            case StorageType.Double when double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d):
                // Only if someone bound a TS_* as Number: raw value, no unit conversion (the shipped file is all Text).
                old = p.AsDouble().ToString(CultureInfo.InvariantCulture);
                if (Math.Abs(p.AsDouble() - d) < 1e-9) return SetOutcome.Same;
                if (p.HasValue && !overwrite) return SetOutcome.KeptExisting;
                return p.Set(d) ? SetOutcome.Written : SetOutcome.Missing;

            case StorageType.Integer when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i):
                old = p.AsInteger().ToString(CultureInfo.InvariantCulture);
                if (p.AsInteger() == i) return SetOutcome.Same;
                if (p.HasValue && !overwrite) return SetOutcome.KeptExisting;
                return p.Set(i) ? SetOutcome.Written : SetOutcome.Missing;

            default:
                return SetOutcome.Missing;
        }
    }
}
