using System.Globalization;
using System.Text;

namespace DSSTech.Core.Formwork;

public static class FormworkExporter
{
    public static void ExportCsv(IEnumerable<FormworkTakeoffRow> rows, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "ElementId,Kind,Role,SegmentCount,SideAreaM2,BottomAreaM2,DeductAreaM2,WaitPourAreaM2,TotalAreaM2,GroundContact,Confidence,Status,Warnings");
        foreach (var r in rows)
        {
            var cells = new[]
            {
                r.ElementId,
                r.Kind,
                r.Role.ToString(),
                r.SegmentCount.ToString(CultureInfo.InvariantCulture),
                F(r.SideAreaM2),
                F(r.BottomAreaM2),
                F(r.DeductAreaM2),
                F(r.WaitPourAreaM2),
                F(r.TotalAreaM2),
                r.GroundContactDropBottom.ToString(),
                r.Confidence,
                r.Status,
                r.Warnings
            };
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string F(double v) =>
        Math.Round(v, 3, MidpointRounding.AwayFromZero).ToString("0.000", CultureInfo.InvariantCulture);

    private static string Escape(string? s)
    {
        s ??= "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
