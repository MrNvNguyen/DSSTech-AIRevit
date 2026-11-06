using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Dss.AssetInfo.Core;

/// <summary>
/// Extraction rules from Config/asset-spec-rules.json.
/// Same file is used by tools/validate_rules.py (reference implementation) — keep both in sync.
/// </summary>
public sealed class SpecRuleSet
{
    public int Version { get; set; }
    public string Separator { get; set; } = "";
    public List<string> Prefixes { get; set; } = new();
    public string ValueTail { get; set; } = "";
    public int MaxValueLength { get; set; } = 70;
    /// <summary>Characters trimmed from both ends of a value.</summary>
    public string ValueTrim { get; set; } = " .,;-/";
    public List<FieldRule> CommonFields { get; set; } = new();
    public List<FieldRule> TailFields { get; set; } = new();
    public Dictionary<string, FamilyRules> Families { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DefaultFamily { get; set; } = "";
    public List<FamilyPattern> FamilyBySheet { get; set; } = new();
    public Dictionary<string, string> FamilyByRevitCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PdfOptions Pdf { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static SpecRuleSet Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy file quy tắc tách thông số.", path);
        var rules = JsonSerializer.Deserialize<SpecRuleSet>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException($"File quy tắc rỗng: {path}");
        // System.Text.Json replaces the dictionaries → restore case-insensitive lookups.
        rules.Families = new Dictionary<string, FamilyRules>(rules.Families, StringComparer.OrdinalIgnoreCase);
        foreach (var fam in rules.Families.Values)
            fam.Overrides = new Dictionary<string, FieldOverride>(fam.Overrides, StringComparer.OrdinalIgnoreCase);
        rules.FamilyByRevitCategory = new Dictionary<string, string>(rules.FamilyByRevitCategory, StringComparer.OrdinalIgnoreCase);
        rules.Validate(path);
        return rules;
    }

    /// <summary>Fields to extract for a family, in priority order (common → family extras → tail).</summary>
    public IReadOnlyList<FieldRule> FieldsFor(string? family, bool pdf)
    {
        Families.TryGetValue(family ?? "", out var fam);
        var list = new List<FieldRule>();
        foreach (var f in CommonFields)
        {
            if (fam is not null && fam.Overrides.TryGetValue(f.Param, out var o))
                list.Add(f with { Labels = o.Labels, LabelsEn = o.LabelsEn ?? f.LabelsEn, Unit = o.Unit });
            else
                list.Add(f);
        }
        if (fam is not null) list.AddRange(fam.ExtraFields);
        list.AddRange(TailFields);
        if (pdf)
            list = list.Select(f => f with { Labels = f.Labels.Concat(f.LabelsEn ?? new List<string>()).ToList() }).ToList();
        return list;
    }

    public string FamilyForSheet(string? sheet)
    {
        foreach (var p in FamilyBySheet)
            if (!string.IsNullOrEmpty(sheet) && Regex.IsMatch(sheet, p.Pattern, RegexOptions.CultureInvariant))
                return p.Family;
        return DefaultFamily;
    }

    /// <summary>bicName = BuiltInCategory enum name, e.g. "OST_MechanicalEquipment".</summary>
    public string FamilyForCategory(string? bicName) =>
        bicName is not null && FamilyByRevitCategory.TryGetValue(bicName, out var f) ? f : DefaultFamily;

    /// <summary>All TS_* names the form can contain (for header mapping and binding checks).</summary>
    public IEnumerable<FieldRule> AllFields() =>
        CommonFields.Concat(Families.Values.SelectMany(f => f.ExtraFields)).Concat(TailFields)
            .GroupBy(f => f.Param, StringComparer.OrdinalIgnoreCase).Select(g => g.First());

    private void Validate(string path)
    {
        if (Prefixes.Count == 0 || string.IsNullOrEmpty(ValueTail) || string.IsNullOrEmpty(Separator))
            throw new InvalidDataException($"Thiếu prefixes / valueTail / separator trong {path}");
        // Compile every pattern once so a typo in the JSON fails at load, not in the middle of an import.
        var all = Prefixes.Concat(new[] { ValueTail, Separator, Pdf.ValueTailNoColon })
            .Concat(AllFieldsIncludingOverrides().SelectMany(f => f.Labels.Concat(f.LabelsEn ?? new List<string>())))
            .Concat(AllFieldsIncludingOverrides().Select(f => f.Unit ?? ""))
            .Concat(FamilyBySheet.Select(p => p.Pattern));
        foreach (var p in all.Where(x => !string.IsNullOrEmpty(x)))
        {
            try { _ = new Regex(p, RegexOptions.CultureInvariant); }
            catch (ArgumentException ex) { throw new InvalidDataException($"Regex lỗi trong {path}: {p}\n{ex.Message}"); }
        }
    }

    private IEnumerable<FieldRule> AllFieldsIncludingOverrides() =>
        AllFields().Concat(Families.Values.SelectMany(f => f.Overrides.Select(o =>
            new FieldRule { Param = o.Key, Labels = o.Value.Labels, LabelsEn = o.Value.LabelsEn, Unit = o.Value.Unit })));
}

public sealed record FieldRule
{
    public string Param { get; init; } = "";
    public string Header { get; init; } = "";
    public List<string> Labels { get; init; } = new();
    public List<string>? LabelsEn { get; init; }
    /// <summary>Case-sensitive fallback regex on units (e.g. "22 kW"); null = none.</summary>
    public string? Unit { get; init; }
}

public sealed class FamilyRules
{
    public List<FieldRule> ExtraFields { get; set; } = new();
    public Dictionary<string, FieldOverride> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Replaces labels and unit of a common field for one family (labelsEn kept from base when absent).</summary>
public sealed class FieldOverride
{
    public List<string> Labels { get; set; } = new();
    public List<string>? LabelsEn { get; set; }
    public string? Unit { get; set; }
}

public sealed class FamilyPattern
{
    public string Pattern { get; set; } = "";
    public string Family { get; set; } = "";
}

public sealed class PdfOptions
{
    public int MinTextCharsPerPage { get; set; } = 40;
    public int MaxPagesPerFile { get; set; } = 400;
    public int LinesBefore { get; set; } = 3;
    public int LinesAfter { get; set; } = 40;
    public int TableModelThreshold { get; set; } = 3;
    public int MinModelTokenLength { get; set; } = 4;
    public bool UseUnitFallback { get; set; }
    public bool ProposeValues { get; set; }
    public string ValueTailNoColon { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
