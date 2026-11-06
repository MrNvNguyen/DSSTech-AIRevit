using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Dss.AssetInfo.Core;

/// <summary>
/// Reads the "Form thông tin phục vụ BIM" workbooks (one sheet per equipment group, one row per asset type).
/// Column → TS_* mapping, in order of preference:
///   1. a row holding ≥3 TS_* names (row 5 of the generated form) — data starts on the next row;
///   2. otherwise the header row of the original form (row 3: "ID", "Tên vật tư, thiết bị", "Công suất"…),
///      mapped through the headers in asset-spec-rules.json + the fixed form columns below.
/// </summary>
public sealed class AssetFormReader
{
    private static readonly Regex ParamName = new(@"^TS_[A-Za-z0-9_]+$", RegexOptions.Compiled);
    private static readonly string[] SkipSheets = { "MUC_LUC", "HuongDan", "DanhMuc", "THAM_SO_REVIT" };

    /// <summary>Fixed columns of the original form (not spec fields).</summary>
    private static readonly Dictionary<string, string> FormHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ID"] = "TS_ID",
        ["Tên vật tư, thiết bị"] = "TS_TenVatTu",
        ["Tên tài sản mặc định"] = "TS_TenTaiSan",
        ["Quy cách, Thông số kỹ thuật"] = "TS_QuyCach",
        ["Số lượng"] = "TS_SoLuong",
        ["Các thông tin kỹ thuật khác"] = "TS_ThongTinKhac",
        ["Nhà sản xuất"] = "TS_NhaSanXuat",
        ["Nước xuất xứ"] = "TS_XuatXu",
        ["Năm sản xuất"] = "TS_NamSanXuat",
        ["Mã sản phẩm"] = "TS_MaSanPham",
        ["Nhà cung cấp"] = "TS_NhaCungCap",
        ["Tiêu chuẩn sản xuất"] = "TS_TieuChuan",
        ["Tài liệu kỹ thuật/Catalogue"] = "TS_Catalogue",
        ["Chứng nhận CoCq"] = "TS_CoCq",
        ["Nhà thầu lắp đặt"] = "TS_NhaThau",
        ["Phạm vi công việc nhà thầu"] = "TS_PhamViNhaThau",
        ["Biên bản nghiệm thu"] = "TS_BienBanNghiemThu",
    };

    private readonly SpecRuleSet _rules;
    private readonly SpecTextExtractor _extractor;

    public AssetFormReader(SpecRuleSet rules)
    {
        _rules = rules;
        _extractor = new SpecTextExtractor(rules);
    }

    /// <param name="fillEmptyFromSpec">
    /// Fill empty spec cells by re-reading the "Quy cách" text with the same rules that produced the form
    /// (validated: 3 430 / 3 430 values identical). Source is marked so the review shows it.
    /// </param>
    public AssetReadResult Read(string path, bool fillEmptyFromSpec = true)
    {
        var result = new AssetReadResult();
        // FileShare.ReadWrite: the workbook is often still open in Excel.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var fileName = Path.GetFileName(path);
        var headerMap = BuildHeaderMap();

        foreach (var ws in wb.Worksheets)
        {
            if (SkipSheets.Contains(ws.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow == 0 || lastCol == 0) continue;

            var (columns, firstDataRow) = FindColumns(ws, lastCol, headerMap);
            if (columns.Count < 3 || !columns.ContainsValue(AssetParams.Id))
            {
                result.Warnings.Add($"[{fileName}] Bỏ qua sheet '{ws.Name}': không tìm thấy dòng tên tham số TS_* hoặc tiêu đề form.");
                continue;
            }

            result.SheetsRead++;
            var family = _rules.FamilyForSheet(ws.Name);
            var idCol = columns.First(kv => kv.Value.Equals(AssetParams.Id, StringComparison.OrdinalIgnoreCase)).Key;

            for (var r = firstDataRow; r <= lastRow; r++)
            {
                var id = CellText(ws.Cell(r, idCol));
                if (string.IsNullOrWhiteSpace(id)) continue;

                var rec = new AssetRecord { SourceFile = fileName, Sheet = ws.Name, Row = r, Family = family };
                foreach (var (col, param) in columns)
                    rec.Set(param, CellText(ws.Cell(r, col)), $"Excel {fileName} › {ws.Name}!{ws.Cell(r, col).Address}");

                if (fillEmptyFromSpec)
                {
                    foreach (var (param, value) in _extractor.Extract(rec.Get(AssetParams.Spec), family))
                        rec.Set(param, value, $"Tự tách từ cột Quy cách ({ws.Name}!R{r})", overwrite: false);
                }
                result.Records.Add(rec);
            }
        }

        foreach (var dup in result.Records.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            result.Warnings.Add($"Trùng TS_ID '{dup.Key}' ở {dup.Count()} dòng: " +
                                string.Join(", ", dup.Select(x => $"{x.Sheet}!{x.Row}")));
        return result;
    }

    private Dictionary<string, string> BuildHeaderMap()
    {
        var map = new Dictionary<string, string>(FormHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (var f in _rules.AllFields())
            if (!string.IsNullOrWhiteSpace(f.Header) && !map.ContainsKey(f.Header))
                map[f.Header] = f.Param;
        return map;
    }

    private static (Dictionary<int, string> Columns, int FirstDataRow) FindColumns(
        IXLWorksheet ws, int lastCol, IReadOnlyDictionary<string, string> headerMap)
    {
        const int scanRows = 12;

        // 1) TS_* row (generated form, row 5).
        for (var r = 1; r <= scanRows; r++)
        {
            var cols = new Dictionary<int, string>();
            for (var c = 1; c <= lastCol; c++)
            {
                var t = CellText(ws.Cell(r, c));
                if (ParamName.IsMatch(t)) cols[c] = t;
            }
            if (cols.Count >= 3) return (cols, r + 1);
        }

        // 2) Display headers (original form, row 3).
        for (var r = 1; r <= scanRows; r++)
        {
            var cols = new Dictionary<int, string>();
            for (var c = 1; c <= lastCol; c++)
            {
                var t = Regex.Replace(CellText(ws.Cell(r, c)), @"\s+", " ");
                if (headerMap.TryGetValue(t, out var p)) cols[c] = p;
            }
            if (cols.Count < 3) continue;

            // The original form has a description row under the headers ("Hệ thống tự tạo"…): skip
            // any following row whose ID cell has no digit.
            var first = r + 1;
            var idCol = cols.FirstOrDefault(kv => kv.Value == AssetParams.Id).Key;
            while (idCol > 0 && first <= r + 3 && !CellText(ws.Cell(first, idCol)).Any(char.IsDigit))
                first++;
            return (cols, first);
        }
        return (new Dictionary<int, string>(), 0);
    }

    /// <summary>Cell value as invariant text (numbers without thousands separators, dates ISO).</summary>
    internal static string CellText(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return "";
        if (v.IsText) return v.GetText().Trim();
        if (v.IsNumber) return v.GetNumber().ToString(CultureInfo.InvariantCulture);
        if (v.IsBoolean) return v.GetBoolean() ? "Có" : "Không";
        if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (v.IsTimeSpan) return v.GetTimeSpan().ToString();
        return ""; // error values (#N/A…) are treated as empty
    }
}
