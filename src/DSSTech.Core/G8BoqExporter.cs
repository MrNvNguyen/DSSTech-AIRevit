using System.Globalization;
using System.Text;

namespace DSSTech.Core;

public static class G8BoqExporter
{
    // Sheet Công trình — data from row 6; columns A,B,E,F,G,H,I,J,K,L,P
    public static void ExportCongTrinhCsv(IEnumerable<BoqLineItem> rows, string path, bool officialOnly = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("STT,Ky hieu ban ve,Ma so Don gia,Ten cong tac,Don vi,So cau kien,Chieu dai,Chieu rong,Chieu cao,He so phu,Khoi luong");
        var index = 1;
        foreach (var row in rows)
        {
            if (officialOnly && !row.CanIssueOfficially) continue;
            if (string.IsNullOrWhiteSpace(row.NormCode)) continue;
            var line = new[]
            {
                index.ToString(CultureInfo.InvariantCulture),
                row.ElementIds.FirstOrDefault() ?? row.ExternalIds.FirstOrDefault() ?? "",
                row.NormCode,
                row.NormName,
                row.Unit,
                row.ElementCount.ToString(CultureInfo.InvariantCulture),
                Format(row.LengthM),
                Format(row.WidthM),
                Format(row.HeightM),
                row.QsFactor.ToString("0.######", CultureInfo.InvariantCulture),
                row.BoqQuantity.ToString("0.######", CultureInfo.InvariantCulture)
            };
            sb.AppendLine(string.Join(",", line.Select(Escape)));
            index++;
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("0.######", CultureInfo.InvariantCulture) : "";

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
