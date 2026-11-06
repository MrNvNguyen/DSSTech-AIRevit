namespace Dss.AssetInfo.Core;

/// <summary>Canonical TS_* parameter names used by the BIM asset form (row 5 of each sheet).</summary>
public static class AssetParams
{
    public const string Prefix = "TS_";
    public const string Id = "TS_ID";
    public const string Name = "TS_TenVatTu";
    public const string TypeName = "TS_TenTaiSan";
    public const string Spec = "TS_QuyCach";
    public const string Model = "TS_MaSanPham";
    public const string Manufacturer = "TS_NhaSanXuat";
    public const string Catalogue = "TS_Catalogue";

    public static bool IsAssetParam(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name!.StartsWith(Prefix, StringComparison.Ordinal) && name.Length > Prefix.Length;
}

/// <summary>One asset type (= one Revit ElementType) read from Excel, PDF or the model.</summary>
public sealed class AssetRecord
{
    public string SourceFile { get; init; } = "";
    public string Sheet { get; init; } = "";
    public int Row { get; init; }
    public string Family { get; set; } = "";

    /// <summary>TS_* parameter → value. Only non-empty values are stored.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>TS_* parameter → where the value came from (shown in review, never written to Revit).</summary>
    public Dictionary<string, string> ValueSources { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Id => Get(AssetParams.Id);
    public string Name => Get(AssetParams.Name);
    public string TypeName => Get(AssetParams.TypeName);
    public string Model => Get(AssetParams.Model);

    /// <summary>Stable key: TS_ID when present, otherwise sheet!row.</summary>
    public string Key => !string.IsNullOrWhiteSpace(Id) ? Id : $"{Sheet}!{Row}";

    public string Get(string param) => Values.TryGetValue(param, out var v) ? v : "";

    public bool Set(string param, string? value, string source, bool overwrite = true)
    {
        if (!AssetParams.IsAssetParam(param) || string.IsNullOrWhiteSpace(value)) return false;
        if (!overwrite && Values.ContainsKey(param)) return false;
        Values[param] = value!.Trim();
        ValueSources[param] = source;
        return true;
    }
}

public enum ProposalConfidence
{
    Low,
    Medium,
    High
}

/// <summary>A model code found on PDF pages — used to fill TS_Catalogue.</summary>
public sealed record PdfPageLink(string RecordKey, string Model, string FilePath, IReadOnlyList<int> Pages)
{
    public string ToCatalogueValue() =>
        Pages.Count == 0 ? FilePath : $"{FilePath}#page={Pages[0]}" + (Pages.Count > 1 ? $" (tr. {string.Join(",", Pages.Take(8))})" : "");
}

/// <summary>A value read from a PDF next to a model code. Always a proposal: the user must accept it.</summary>
public sealed class ValueProposal
{
    public string RecordKey { get; init; } = "";
    public string Param { get; init; } = "";
    public string Value { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Page { get; init; }
    public ProposalConfidence Confidence { get; init; }
    public string Snippet { get; init; } = "";
    public bool Accepted { get; set; }
}

public sealed class PdfScanResult
{
    public string FilePath { get; init; } = "";
    public int PageCount { get; init; }
    public bool IsScanned { get; init; }
    public string Error { get; init; } = "";
    public List<PdfPageLink> Links { get; } = new();
    public List<ValueProposal> Proposals { get; } = new();

    public string StatusText =>
        !string.IsNullOrEmpty(Error) ? $"Lỗi: {Error}"
        : IsScanned ? "PDF scan (ảnh) – cần OCR, không đọc được chữ"
        : $"{PageCount} trang, khớp {Links.Count} thiết bị";
}

public sealed class AssetReadResult
{
    public List<AssetRecord> Records { get; } = new();
    public List<string> Warnings { get; } = new();
    public int SheetsRead { get; set; }
}

/// <summary>One line of the Excel ↔ Revit review export (filled by the Revit layer).</summary>
public sealed class ReviewRow
{
    public string Key { get; init; } = "";
    public string Sheet { get; init; } = "";
    public string Name { get; init; } = "";
    public string Model { get; init; } = "";
    public string ExpectedTypeName { get; init; } = "";
    public string RevitTypes { get; init; } = "";
    public string MatchMethod { get; init; } = "";
    public int InstanceCount { get; init; }
    public int ParamCount { get; init; }
    public string Status { get; init; } = "";
}
