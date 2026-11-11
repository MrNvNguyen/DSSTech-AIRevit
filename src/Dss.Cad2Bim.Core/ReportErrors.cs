using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// The lines of a finished command's report that tell of something not done: those are shown to the user, the rest
/// of the report (counts, rules applied, fallbacks taken) only goes to the log.
/// </summary>
public static class ReportErrors
{
    // "Không tạo được: 0 đoạn, 8 fitting", "Không đặt được tên …", "Lỗi: …", "  lỗi: …", "Preflight FAIL".
    private static readonly Regex Failed = new(
        @"^\s*(lỗi\b|không\s+(tạo|dựng|đặt|nối|đọc|ghi|cắt|đục|gán|xuất|mở|xử\s+lý)\s+được|thất\s+bại|fail(ed)?\b|error\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A count of errors inside a line: "lỗi 3", "lỗi: 2", "| lỗi 1 (xem log)".
    private static readonly Regex Count = new(@"lỗi\s*[:(]?\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<string> Lines(string? report)
    {
        var lines = new List<string>();
        foreach (var raw in (report ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0)
                continue;
            var counts = Count.Matches(line).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
            if (counts.Count > 0 ? counts.Any(c => c > 0) : Failed.IsMatch(line) && !AllZero(line))
                lines.Add(line.Trim());
        }

        return lines;
    }

    /// <summary>"Không tạo được: 0 đoạn, 0 fitting": every number in it nought.</summary>
    private static bool AllZero(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
            return false;
        var numbers = Regex.Matches(line.Substring(colon), @"\d+").Cast<Match>().Select(m => m.Value).ToList();
        return numbers.Count > 0 && numbers.All(n => n.Trim('0').Length == 0);
    }
}
