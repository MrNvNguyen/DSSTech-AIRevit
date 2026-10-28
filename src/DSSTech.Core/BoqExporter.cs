using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DSSTech.Core;

public static class BoqExporter
{
    public static readonly string[] ExportColumnOrder =
    [
        "STT", "Bộ môn", "Mã công tác BIM", "Mã định mức", "Tên công tác TT 38",
        "Loại cấu kiện / Host", "Biện pháp", "Kích thước danh nghĩa", "Dải hình học",
        "Vật liệu / mác", "Hệ MEPF", "System nguồn", "Schedule / Type", "Tầng / Level",
        "Zone", "Type cấu kiện", "ĐVT", "Khối lượng BIM", "Đơn vị BIM", "Hệ số QS", "Khối lượng BOQ",
        "Số cấu kiện", "Trạng thái", "Thông số thiếu", "Ứng viên mã", "Description",
        "Nguồn Category", "Element ID", "External ID"
    ];

    public static Dictionary<string, string> ToExportRecord(BoqLineItem row, int index) => new()
    {
        ["STT"] = index.ToString(CultureInfo.InvariantCulture),
        ["Bộ môn"] = row.Discipline,
        ["Mã công tác BIM"] = row.Code,
        ["Mã định mức"] = string.IsNullOrWhiteSpace(row.NormCode) ? "Chưa xác định" : row.NormCode,
        ["Tên công tác TT 38"] = row.NormName,
        ["Loại cấu kiện / Host"] = row.HostKind,
        ["Biện pháp"] = row.PlacementMethod,
        ["Kích thước danh nghĩa"] = row.Dimensions,
        ["Dải hình học"] = string.IsNullOrWhiteSpace(row.GeometryBand) ? row.Dimensions : row.GeometryBand,
        ["Vật liệu / mác"] = string.IsNullOrWhiteSpace(row.MaterialLabel) ? row.Material : row.MaterialLabel,
        ["Hệ MEPF"] = string.Join(" · ", new[] { row.SystemCode, row.SystemName }.Where(x => !string.IsNullOrWhiteSpace(x))),
        ["System nguồn"] = row.SourceSystem,
        ["Schedule / Type"] = row.ScheduleType,
        ["Tầng / Level"] = row.Level,
        ["Zone"] = row.Zone,
        ["Type cấu kiện"] = row.ElementType,
        ["ĐVT"] = row.Unit,
        ["Khối lượng BIM"] = row.BimQuantity.ToString("0.######", CultureInfo.InvariantCulture),
        ["Đơn vị BIM"] = row.BimUnit,
        ["Hệ số QS"] = row.QsFactor.ToString("0.######", CultureInfo.InvariantCulture),
        ["Khối lượng BOQ"] = row.BoqQuantity.ToString("0.######", CultureInfo.InvariantCulture),
        ["Số cấu kiện"] = row.ElementCount.ToString(CultureInfo.InvariantCulture),
        ["Trạng thái"] = row.Status,
        ["Thông số thiếu"] = string.Join("; ", row.SelectorMissing),
        ["Ứng viên mã"] = string.Join(", ", row.CandidateNormCodes),
        ["Description"] = row.Description,
        ["Nguồn Category"] = row.RevitCategory,
        ["Element ID"] = string.Join(", ", row.ElementIds),
        ["External ID"] = string.Join(", ", row.ExternalIds)
    };

    public static void ExportCsv(IEnumerable<BoqLineItem> rows, string path)
    {
        var list = rows.ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ExportColumnOrder.Select(Escape)));
        for (var i = 0; i < list.Count; i++)
        {
            var record = ToExportRecord(list[i], i + 1);
            sb.AppendLine(string.Join(",", ExportColumnOrder.Select(key =>
                Escape(record.TryGetValue(key, out var val) ? val : ""))));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static void ExportJson(IEnumerable<BoqLineItem> rows, string path)
    {
        var payload = rows.Select((row, index) => ToExportRecord(row, index + 1)).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(true));
    }

    public static void ExportInspectionCsv(IEnumerable<BoqInspectionRow> rows, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Chỉ số,Giá trị");
        foreach (var row in rows)
            sb.AppendLine($"{Escape(row.Metric)},{Escape(row.Value)}");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Escape(string? value)
    {
        var s = value ?? "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
