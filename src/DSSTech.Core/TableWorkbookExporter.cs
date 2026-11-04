using ClosedXML.Excel;

namespace DSSTech.Core;

/// <summary>One sheet: a bold header row, then the rows; numbers stay numbers, columns sized to their content.</summary>
public static class TableWorkbookExporter
{
    public static void Export(string path, string sheetName, string title, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(sheetName);
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(3, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E7F4ED");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var r = 4;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null:
                        break;
                    case double d:
                        cell.Value = d;
                        break;
                    case int i:
                        cell.Value = i;
                        break;
                    case long l:
                        cell.Value = l;
                        break;
                    default:
                        cell.Value = row[c]!.ToString();
                        break;
                }
            }

            r++;
        }

        ws.Range(3, 1, Math.Max(3, r - 1), headers.Count).SetAutoFilter();
        ws.SheetView.FreezeRows(3);
        ws.Columns().AdjustToContents(3, Math.Max(3, r - 1), 8, 60);
        wb.SaveAs(path);
    }
}
