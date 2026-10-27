namespace DSSTech.Core.Formwork;

/// <summary>One grid/write row per element (segments aggregated).</summary>
public sealed class FormworkTakeoffRow
{
    public required string ElementId { get; init; }
    public string Kind { get; init; } = "";
    public FormworkElementRole Role { get; init; }
    public int SegmentCount { get; init; }
    public double SideAreaM2 { get; init; }
    public double BottomAreaM2 { get; init; }
    public double DeductAreaM2 { get; init; }
    /// <summary>Column shoulder leftover beside narrower beams (beam-depth band above soffit).</summary>
    public double WaitPourAreaM2 { get; init; }
    public double TotalAreaM2 { get; init; }
    public bool GroundContactDropBottom { get; init; }
    public string Confidence { get; init; } = "High";
    public string Warnings { get; init; } = "";
    public string Status { get; init; } = "OK";
}

public static class FormworkTakeoffRowAggregator
{
    public static IReadOnlyList<FormworkTakeoffRow> FromBatch(FormworkBatchResult batch)
    {
        if (batch.Elements.Count == 0)
            return [];

        return batch.Elements
            .GroupBy(e => e.ElementId, StringComparer.Ordinal)
            .Select(g =>
            {
                var list = g.ToList();
                var first = list[0];
                var warnings = list
                    .Select(x => string.IsNullOrWhiteSpace(x.Reason) ? x.Status : x.Reason!)
                    .Where(s => !string.IsNullOrWhiteSpace(s) &&
                                !s.Equals("OK", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var low = list.Any(x =>
                    !string.Equals(x.Status, "OK", StringComparison.OrdinalIgnoreCase) ||
                    x.Status.Contains("UNSUPPORTED", StringComparison.OrdinalIgnoreCase));

                var side = RoundM2(list.Sum(x => x.SideAreaM2));
                var bottom = RoundM2(list.Sum(x => x.BottomAreaM2));
                return new FormworkTakeoffRow
                {
                    ElementId = g.Key,
                    Kind = first.Kind,
                    Role = first.Role,
                    SegmentCount = list.Count,
                    SideAreaM2 = side,
                    BottomAreaM2 = bottom,
                    DeductAreaM2 = RoundM2(list.Sum(x => x.DeductAreaM2)),
                    WaitPourAreaM2 = RoundM2(list.Sum(x => x.WaitPourAreaM2)),
                    TotalAreaM2 = RoundM2(side + bottom),
                    GroundContactDropBottom = list.Any(x => x.GroundContactDropBottom),
                    Confidence = low ? "Low" : "High",
                    Warnings = string.Join("|", warnings),
                    Status = low ? (list.FirstOrDefault(x => x.Status != "OK")?.Status ?? "Low") : "OK"
                };
            })
            .OrderBy(r => r.ElementId, StringComparer.Ordinal)
            .ToList();
    }

    private static double RoundM2(double m2) =>
        Math.Round(m2, 3, MidpointRounding.AwayFromZero);
}
