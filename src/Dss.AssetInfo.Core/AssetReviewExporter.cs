using ClosedXML.Excel;

namespace Dss.AssetInfo.Core;

/// <summary>Review workbook: what will be / was matched and written, plus PDF links and proposals.</summary>
public static class AssetReviewExporter
{
    public static void Export(string path, IEnumerable<ReviewRow> matches, IEnumerable<PdfScanResult> pdfs)
    {
        using var wb = new XLWorkbook();
        var pdfList = pdfs.ToList();

        Table(wb.Worksheets.Add("DoiChieu_Revit"),
            new[] { "Mã (TS_ID)", "Sheet", "Tên vật tư", "Model", "Tên Type kỳ vọng", "Revit Type khớp", "Cách khớp", "Số instance", "Số tham số", "Trạng thái" },
            matches.Select(m => new object[] { m.Key, m.Sheet, m.Name, m.Model, m.ExpectedTypeName, m.RevitTypes, m.MatchMethod, m.InstanceCount, m.ParamCount, m.Status }));

        Table(wb.Worksheets.Add("PDF_TepDaDoc"),
            new[] { "File", "Số trang", "Tình trạng" },
            pdfList.Select(p => new object[] { p.FilePath, p.PageCount, p.StatusText }));

        Table(wb.Worksheets.Add("PDF_LienKet"),
            new[] { "Mã (TS_ID)", "Model", "File", "Trang", "Giá trị TS_Catalogue" },
            pdfList.SelectMany(p => p.Links).Select(l => new object[] { l.RecordKey, l.Model, l.FilePath, string.Join(",", l.Pages), l.ToCatalogueValue() }));

        Table(wb.Worksheets.Add("PDF_DeXuat"),
            new[] { "Mã (TS_ID)", "Tham số", "Giá trị đề xuất", "Tin cậy", "Đã chấp nhận", "File", "Trang", "Trích đoạn" },
            pdfList.SelectMany(p => p.Proposals).Select(x => new object[]
                { x.RecordKey, x.Param, x.Value, x.Confidence.ToString(), x.Accepted ? "Có" : "Không", x.FilePath, x.Page, x.Snippet }));

        wb.SaveAs(path);
    }

    private static void Table(IXLWorksheet ws, string[] headers, IEnumerable<object[]> rows)
    {
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        var head = ws.Range(1, 1, 1, headers.Length);
        head.Style.Font.Bold = true;
        head.Style.Fill.BackgroundColor = XLColor.FromHtml("#E7F4ED");

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                XLCellValue value = row[c] switch
                {
                    int i => (double)i,
                    double d => d,
                    null => "",
                    var o => o.ToString() ?? ""
                };
                ws.Cell(r, c + 1).Value = value;
            }
            r++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
        foreach (var col in ws.ColumnsUsed())
            if (col.Width > 60) col.Width = 60;
    }
}
