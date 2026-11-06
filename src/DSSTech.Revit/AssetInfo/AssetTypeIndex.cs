using System.IO;
using Autodesk.Revit.DB;
using Dss.AssetInfo.Core;

namespace DSSTech.Revit.AssetInfo;

internal static class AssetInfoPaths
{
    public static string Rules => Path.Combine(PluginPaths.Root, "Config", "asset-spec-rules.json");
    public static string SharedParameters => Path.Combine(PluginPaths.Root, "Config", "TS_SharedParameters_FM.txt");
}

/// <summary>Snapshot of one Revit ElementType (read in the command context, no transaction).</summary>
internal sealed class RevitTypeInfo
{
    public ElementId Id { get; init; } = ElementId.InvalidElementId;
    public string TypeName { get; init; } = "";
    public string FamilyName { get; init; } = "";
    public string CategoryName { get; init; } = "";
    public string BicName { get; init; } = "";
    public int InstanceCount { get; init; }
    public string TsId { get; init; } = "";
    public string TsTypeName { get; init; } = "";
    public string TsModel { get; init; } = "";
    public string NormName { get; init; } = "";
    public string NormFamilyAndType { get; init; } = "";

    public string Display => $"{CategoryName} │ {FamilyName} : {TypeName}  ({InstanceCount})";
    public override string ToString() => Display;
}

internal static class AssetTypeIndex
{
    /// <summary>Categories scanned = keys of familyByRevitCategory in asset-spec-rules.json.</summary>
    public static IReadOnlyList<BuiltInCategory> TargetCategories(SpecRuleSet rules) =>
        rules.FamilyByRevitCategory.Keys
            .Select(k => Enum.TryParse<BuiltInCategory>(k, out var b) ? b : BuiltInCategory.INVALID)
            .Where(b => b != BuiltInCategory.INVALID)
            .Distinct()
            .ToList();

    public static List<RevitTypeInfo> Build(Document doc, SpecRuleSet rules)
    {
        var cats = TargetCategories(rules);
        if (cats.Count == 0) return new List<RevitTypeInfo>();
        var filter = new ElementMulticategoryFilter(cats.ToList());

        var counts = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(filter)
            .Select(e => e.GetTypeId())
            .Where(id => id != ElementId.InvalidElementId)
            .GroupBy(id => id.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .WherePasses(filter)
            .Cast<ElementType>()
            .Select(t => new RevitTypeInfo
            {
                Id = t.Id,
                TypeName = t.Name,
                FamilyName = t.FamilyName ?? "",
                CategoryName = t.Category?.Name ?? "",
                BicName = t.Category is null ? "" : t.Category.BuiltInCategory.ToString(),
                InstanceCount = counts.TryGetValue(t.Id.Value, out var n) ? n : 0,
                TsId = Text(t, AssetParams.Id),
                TsTypeName = Text(t, AssetParams.TypeName),
                TsModel = Text(t, AssetParams.Model),
                NormName = ModelTokens.Normalize(t.Name),
                NormFamilyAndType = ModelTokens.Normalize((t.FamilyName ?? "") + " " + t.Name)
            })
            .OrderBy(t => t.CategoryName).ThenBy(t => t.FamilyName).ThenBy(t => t.TypeName)
            .ToList();
    }

    /// <summary>Records built from the model itself — used when only PDFs are imported (no Excel).</summary>
    public static List<AssetRecord> ToRecords(IEnumerable<RevitTypeInfo> types, SpecRuleSet rules) =>
        types.Select(t =>
        {
            var r = new AssetRecord { SourceFile = "Revit", Sheet = t.CategoryName, Row = (int)Math.Min(int.MaxValue, t.Id.Value), Family = rules.FamilyForCategory(t.BicName) };
            r.Set(AssetParams.Id, string.IsNullOrWhiteSpace(t.TsId) ? $"REVIT-{t.Id.Value}" : t.TsId, "Revit");
            r.Set(AssetParams.TypeName, string.IsNullOrWhiteSpace(t.TsTypeName) ? t.TypeName : t.TsTypeName, "Revit");
            r.Set(AssetParams.Model, t.TsModel, "Revit");
            return r;
        }).ToList();

    private static string Text(Element e, string name)
    {
        var p = e.LookupParameter(name);
        return p is { HasValue: true } ? (p.AsString() ?? p.AsValueString() ?? "") : "";
    }
}
