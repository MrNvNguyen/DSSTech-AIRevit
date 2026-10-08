using ClosedXML.Excel;

namespace QSNormEngine.Core;

public static class BoqWorkbookExporter
{
    public static void Export(string path, IEnumerable<BoqLineItem> rows, IEnumerable<BoqInspectionRow> inspection, bool officialOnlyForG8 = true, ElementQuantityExporter.ColumnConfig? quantityConfig = null)
    {
        var list = rows.ToList();
        var inspect = inspection.ToList();
        using var wb = new XLWorkbook();

        WriteBoqAuditSheet(wb, list);
        WriteInspectionSheet(wb, inspect);
        WriteG8CongTrinhSheet(wb, list, officialOnlyForG8);
        ElementQuantityExporter.WriteWorksheet(wb, list, quantityConfig ?? ElementQuantityExporter.DefaultConfig());

        wb.SaveAs(path);
    }

    private static void WriteBoqAuditSheet(XLWorkbook wb, List<BoqLineItem> rows)
    {
        var ws = wb.Worksheets.Add("BOQ Audit");
        for (var c = 0; c < BoqExporter.ExportColumnOrder.Length; c++)
            ws.Cell(1, c + 1).Value = BoqExporter.ExportColumnOrder[c];
        var header = ws.Range(1, 1, 1, BoqExporter.ExportColumnOrder.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");

        for (var i = 0; i < rows.Count; i++)
        {
            var record = BoqExporter.ToExportRecord(rows[i], i + 1);
            for (var c = 0; c < BoqExporter.ExportColumnOrder.Length; c++)
            {
                var key = BoqExporter.ExportColumnOrder[c];
                ws.Cell(i + 2, c + 1).Value = record.TryGetValue(key, out var val) ? val : "";
            }
        }
        ws.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
        ws.SheetView.FreezeRows(1);
    }

    private static void WriteInspectionSheet(XLWorkbook wb, List<BoqInspectionRow> rows)
    {
        var ws = wb.Worksheets.Add("Inspection");
        ws.Cell(1, 1).Value = "Chỉ số";
        ws.Cell(1, 2).Value = "Giá trị";
        ws.Range(1, 1, 1, 2).Style.Font.Bold = true;
        for (var i = 0; i < rows.Count; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Metric;
            ws.Cell(i + 2, 2).Value = rows[i].Value;
        }
        ws.Columns().AdjustToContents();
    }

    private static void WriteG8CongTrinhSheet(XLWorkbook wb, List<BoqLineItem> rows, bool officialOnly)
    {
        var ws = wb.Worksheets.Add("G8 Cong trinh");
        var headers = new[]
        {
            "STT", "Ky hieu ban ve", "Ma so Don gia", "Ten cong tac", "Don vi",
            "So cau kien", "Chieu dai", "Chieu rong", "Chieu cao", "He so phu", "Khoi luong"
        };
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(6, c + 1).Value = headers[c];
        ws.Range(6, 1, 6, headers.Length).Style.Font.Bold = true;

        var index = 1;
        var rowNum = 7;
        foreach (var row in rows)
        {
            if (officialOnly && !row.CanIssueOfficially) continue;
            if (string.IsNullOrWhiteSpace(row.NormCode)) continue;
            ws.Cell(rowNum, 1).Value = index;
            ws.Cell(rowNum, 2).Value = row.ElementIds.FirstOrDefault() ?? row.ExternalIds.FirstOrDefault() ?? "";
            ws.Cell(rowNum, 3).Value = row.NormCode;
            ws.Cell(rowNum, 4).Value = row.NormName;
            ws.Cell(rowNum, 5).Value = row.Unit;
            ws.Cell(rowNum, 6).Value = row.ElementCount;
            ws.Cell(rowNum, 7).Value = row.LengthM;
            ws.Cell(rowNum, 8).Value = row.WidthM;
            ws.Cell(rowNum, 9).Value = row.HeightM;
            ws.Cell(rowNum, 10).Value = row.QsFactor;
            ws.Cell(rowNum, 11).Value = row.BoqQuantity;
            index++;
            rowNum++;
        }
        ws.Columns().AdjustToContents();
    }
}
