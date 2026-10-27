namespace DSSTech.Core;

public static class BoqInspectionBuilder
{
    public static List<BoqInspectionRow> Build(
        IEnumerable<BoqLineItem> rows,
        string groupingModeLabel,
        int sourceElementCount)
    {
        var list = rows.ToList();
        var mapped = list.Count(x => x.Status == "Đã ánh xạ" && LeafCodeRegex().IsMatch(x.NormCode));
        var missing = list.Count(x => x.Status == "Thiếu thông số mô hình");
        var waiting = list.Count(x => x.Status == "Chờ mã lá TT38");
        var review = list.Count(x => x.Status is "Cần kiểm tra" or "Cần xác nhận thông số");
        var official = list.Count(x => x.CanIssueOfficially);

        var histogram = list.SelectMany(x => x.SelectorMissing)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new BoqInspectionRow { Metric = $"Thiếu: {g.Key}", Value = g.Count().ToString() })
            .ToList();

        var result = new List<BoqInspectionRow>
        {
            new() { Metric = "Chế độ gom nhóm", Value = groupingModeLabel },
            new() { Metric = "Số cấu kiện nguồn", Value = sourceElementCount.ToString() },
            new() { Metric = "Số dòng BOQ", Value = list.Count.ToString() },
            new() { Metric = "Đã ánh xạ (leaf 5 số)", Value = mapped.ToString() },
            new() { Metric = "Official-ready", Value = official.ToString() },
            new() { Metric = "Thiếu thông số", Value = missing.ToString() },
            new() { Metric = "Chờ mã lá", Value = waiting.ToString() },
            new() { Metric = "Cần kiểm tra / QC", Value = review.ToString() },
            new() { Metric = "Tổng KL BOQ", Value = list.Sum(x => x.BoqQuantity).ToString("0.######") }
        };
        result.AddRange(histogram);
        return result;
    }

    private static System.Text.RegularExpressions.Regex LeafCodeRegex() =>
        new(@"^\w+\.\d{5}$", System.Text.RegularExpressions.RegexOptions.Compiled);
}
