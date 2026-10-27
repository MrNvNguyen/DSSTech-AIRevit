using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;

namespace DSSTech.Core;

/// <summary>Element quantity table exporter (Q2) — separate from 28-col BOQ audit / G8.</summary>
public static class ElementQuantityExporter
{
    public const string DefaultConfigFileName = "element-quantity-columns.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public sealed record QtyColumn(string Header, string Group, Func<BoqLineItem, string> Read);

    public sealed class ColumnConfig
    {
        [JsonPropertyName("enabledGroups")]
        public List<string> EnabledGroups { get; set; } = ["1", "4", "5"];

        [JsonPropertyName("quantitySchedule")]
        public ScheduleConfig QuantitySchedule { get; set; } = new();
    }

    public sealed class ScheduleConfig
    {
        [JsonPropertyName("createPerCategory")]
        public bool CreatePerCategory { get; set; }
    }

    public static ColumnConfig LoadConfig(string? configPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<ColumnConfig>(json, JsonOptions) ?? DefaultConfig();
        }
        return DefaultConfig();
    }

    public static ColumnConfig DefaultConfig() => new()
    {
        EnabledGroups = ["1", "4", "5"],
        QuantitySchedule = new ScheduleConfig { CreatePerCategory = false }
    };

    public static IReadOnlyList<QtyColumn> AllColumns { get; } =
    [
        // Group 1 — PO 21 columns
        new("Element_ID", "1", r => JoinIds(r.ElementIds, r.ElementId)),
        new("GUID", "1", r => JoinIds(r.ExternalIds, r.ExternalId)),
        new("Category", "1", r => r.RevitCategory),
        new("Type", "1", r => r.ScheduleType),
        new("Mark", "1", r => r.Mark),
        new("Level", "1", r => r.Level),
        new("QS_InOutBuilding", "1", r => r.InOutBuilding),
        new("Vật liệu", "1", r => string.IsNullOrWhiteSpace(r.MaterialLabel) ? r.Material : r.MaterialLabel),
        new("Mác vật liệu", "1", r => r.MaterialGrade),
        new("Chiều rộng (m)", "1", r => Fmt(r.WidthM)),
        new("Chiều cao (m)", "1", r => Fmt(r.HeightM)),
        new("Chiều dày (m)", "1", r => Fmt(r.ThicknessM)),
        new("Chiều dài (m)", "1", r => Fmt(r.LengthM)),
        new("Diameter (mm)", "1", r => Fmt(r.DiameterMm)),
        new("Description", "1", r => r.Description),
        new("Count", "1", r => r.ElementCount.ToString(CultureInfo.InvariantCulture)),
        new("Area (m2)", "1", r => Fmt(r.AreaM2)),
        new("Volume (m3)", "1", r => Fmt(r.VolumeM3)),
        new("Weight (kg)", "1", r => Fmt(r.WeightKg)),
        new("QS_FormworkArea", "1", r => Fmt(r.FormworkAreaM2)),
        new("Host ID", "1", r => r.HostId),

        // Group 2 — model trace
        new("Family", "2", r => r.FamilyName),
        new("Type Mark", "2", _ => ""),
        new("Comments", "2", _ => ""),
        new("Workset", "2", _ => ""),
        new("Phase Created", "2", _ => ""),
        new("Phase Demolished", "2", _ => ""),
        new("Design Option", "2", _ => ""),
        new("Source File", "2", _ => ""),

        // Group 3 — location detail (stubs until facts extended)
        new("Base Level", "3", _ => ""),
        new("Top Level", "3", _ => ""),
        new("Base Offset", "3", _ => ""),
        new("Top Offset", "3", _ => ""),
        new("Elevation đáy (m)", "3", _ => ""),
        new("Elevation đỉnh (m)", "3", _ => ""),
        new("Zone / Room", "3", r => r.Zone),

        // Group 4 — formwork detail
        new("QS_FormworkSide", "4", r => Fmt(r.FormworkSideM2)),
        new("QS_FormworkBottom", "4", r => Fmt(r.FormworkBottomM2)),
        new("QS_FormworkDeduct", "4", r => Fmt(r.FormworkDeductM2)),
        new("QS_FormworkWaitPour", "4", r => r.FormworkWaitPour),
        new("QS_FormworkGroundContact", "4", r => r.FormworkGroundContact),
        new("QS_FormworkConfidence", "4", r => r.FormworkConfidence),
        new("Contact Element IDs", "4", r => r.ContactElementIds),

        // Group 5 — norm mapping
        new("WorkItemId", "5", r => r.WorkItemId),
        new("QS_NormCode", "5", r => r.NormCode),
        new("Tên công tác", "5", r => r.NormName),
        new("ĐVT định mức", "5", r => r.Unit),
        new("Trạng thái", "5", r => r.Status),
        new("Official", "5", r => r.CanIssueOfficially ? "true" : "false"),
        new("Thông số thiếu", "5", r => string.Join("; ", r.SelectorMissing))
    ];

    public static IReadOnlyList<QtyColumn> ResolveColumns(ColumnConfig? config = null)
    {
        var cfg = config ?? DefaultConfig();
        var enabled = new HashSet<string>(
            cfg.EnabledGroups.Select(g => g.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (enabled.Count == 0)
            enabled.Add("1");
        return AllColumns.Where(c => enabled.Contains(c.Group)).ToList();
    }

    public static void ExportCsv(IEnumerable<BoqLineItem> rows, string path, ColumnConfig? config = null)
    {
        var cols = ResolveColumns(config);
        var list = rows.ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", cols.Select(c => Escape(c.Header))));
        foreach (var row in list)
            sb.AppendLine(string.Join(",", cols.Select(c => Escape(c.Read(row)))));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static void WriteWorksheet(XLWorkbook workbook, IEnumerable<BoqLineItem> rows, ColumnConfig? config = null, string sheetName = "Khối lượng cấu kiện")
    {
        var cols = ResolveColumns(config);
        var list = rows.ToList();
        var ws = workbook.Worksheets.Add(sheetName);
        for (var c = 0; c < cols.Count; c++)
            ws.Cell(1, c + 1).Value = cols[c].Header;
        var header = ws.Range(1, 1, 1, Math.Max(cols.Count, 1));
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE");

        for (var i = 0; i < list.Count; i++)
        {
            for (var c = 0; c < cols.Count; c++)
                ws.Cell(i + 2, c + 1).Value = cols[c].Read(list[i]);
        }
        ws.Columns().AdjustToContents(1, Math.Min(list.Count + 1, 200));
        ws.SheetView.FreezeRows(1);
    }

    private static string Fmt(double? value) =>
        value is null ? "" : value.Value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string JoinIds(IReadOnlyList<string> ids, string fallback)
    {
        if (ids.Count > 0)
            return string.Join(", ", ids);
        return fallback ?? "";
    }

    private static string Escape(string? value)
    {
        var s = value ?? "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
    }
}
