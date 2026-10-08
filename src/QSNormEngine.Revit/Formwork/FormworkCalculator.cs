using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit.Formwork;

/// <summary>
/// Formwork pipeline aligned with OOG FormworkUtils / FaceUtil
/// (<c>docs/formwork_logic_code.html</c>):
/// SolidProvider → GroundContact → FaceOverlapFormwork for every Kind
/// (Foundation / Column / Wall / Beam / Slab).
/// Gross = included solid faces; deduct = coplanar face-overlap with neighbors.
/// </summary>
public static class FormworkCalculator
{
    public static FormworkBatchResult Compute(
        Document doc,
        IReadOnlyCollection<ElementId>? elementFilter = null) =>
        Compute(doc, FormworkRuleSetLoader.CreateN1ParityDefaults(), elementFilter);

    public static FormworkBatchResult Compute(
        Document doc,
        FormworkRuleSet ruleSet,
        IReadOnlyCollection<ElementId>? elementFilter = null,
        string? rulesPath = null,
        string? rulesSha256 = null)
    {
        var provider = new SolidProvider(doc, ruleSet);
        var collected = provider.Collect(elementFilter).ToList();
        // Keep clip metadata for UI/audit, but face-overlap uses full element solid (OOG).
        var entries = HeightClipResolver.ExpandSegments(doc, collected, ruleSet);
        GroundContactResolver.Apply(doc, entries, ruleSet);

        var cache = new FaceOverlapFormwork.SolidCache();
        // Do not seed with clipped Solids — FaceOverlap loads full element geometry (OOG).

        var overlapByKey = new Dictionary<(long Id, int Seg), FaceOverlapFormwork.Result>();
        foreach (var entry in entries)
        {
            if (entry.Role is FormworkElementRole.Unknown)
                continue;
            var computed = FaceOverlapFormwork.TryCompute(doc, entry, ruleSet, cache);
            if (computed is not null)
                overlapByKey[EntryKey(entry)] = computed.Value;
        }

        var verticalTol = ruleSet.Settings.VerticalToleranceDeg;
        var results = entries
            .Select(entry =>
            {
                var key = EntryKey(entry);
                double sideOut = 0;
                double bottomOut = 0;
                double deduct = 0;
                double top = 0;
                string? reason = null;

                if (overlapByKey.TryGetValue(key, out var ov))
                {
                    sideOut = ov.SideM2;
                    bottomOut = ov.BottomM2;
                    deduct = ov.DeductSideM2 + ov.DeductBottomM2;
                    reason = ov.Reason;
                    if (entry.GroundContactDropBottom)
                    {
                        bottomOut = 0;
                        if (reason is not null && !reason.Contains("Bottom=0", StringComparison.Ordinal))
                            reason += " | GC Bottom=0";
                    }
                }
                else
                {
                    // Fallback: face sum only (no neighbors) — unsupported / empty solid.
                    var gross = SumFaceAreas(entry, verticalTol);
                    sideOut = gross.Side;
                    bottomOut = entry.GroundContactDropBottom ? 0 : gross.Bottom;
                    top = gross.Top;
                    reason = "no-overlap-engine";
                }

                sideOut = FormworkUnits.RoundM2(sideOut);
                bottomOut = FormworkUnits.RoundM2(bottomOut);
                deduct = FormworkUnits.RoundM2(deduct);
                var total = FormworkUnits.RoundM2(sideOut + bottomOut);
                var segH = entry.ClipBottomZM is double lo && entry.ClipTopZM is double hi
                    ? hi - lo
                    : (double?)null;

                return new FormworkResult
                {
                    ElementId = entry.ElementId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Role = entry.Role,
                    Kind = entry.Rule?.Kind ?? entry.Role.ToString(),
                    SegmentIndex = entry.SegmentIndex,
                    SegmentCount = entry.SegmentCount,
                    ClipBottomZM = entry.ClipBottomZM,
                    ClipTopZM = entry.ClipTopZM,
                    SegmentHeightM = segH,
                    BboxHeightM = entry.BboxHeightM,
                    ClipHeightApplied = entry.ClipHeightApplied,
                    SideAreaM2 = sideOut,
                    BottomAreaM2 = bottomOut,
                    TopAreaM2 = top,
                    GrossAreaM2 = FormworkUnits.RoundM2(sideOut + bottomOut + deduct),
                    DeductAreaM2 = deduct,
                    WaitPourAreaM2 = 0,
                    TotalAreaM2 = total,
                    GroundContactDropBottom = entry.GroundContactDropBottom,
                    Status = entry.Role == FormworkElementRole.Unknown && entry.Rule is null
                        ? "UNSUPPORTED"
                        : "OK",
                    Reason = reason
                };
            })
            .OrderBy(r => long.Parse(r.ElementId, System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(r => r.SegmentIndex ?? -1)
            .ToList();

        return new FormworkBatchResult
        {
            Elements = results,
            NeighborPairs = 0,
            ElementsProcessed = results.Count,
            SegmentsEmitted = results.Count(r => r.ClipHeightApplied),
            EpsilonMm = ruleSet.Settings.ProbeEpsilonMm,
            RulesPath = rulesPath,
            RulesSha256 = rulesSha256,
            GroundContactAutoDetect = ruleSet.Settings.GroundContactAutoDetect
        };
    }

    private static (long Id, int Seg) EntryKey(SolidEntry entry) =>
        (entry.ElementId.Value, entry.SegmentIndex ?? -1);

    private static (double Side, double Bottom, double Top) SumFaceAreas(SolidEntry entry, double verticalTolDeg)
    {
        double side = 0, bottom = 0, top = 0;
        var included = IncludedFaces(entry);
        if (included.Count == 0)
            return (0, 0, 0);

        foreach (Face face in entry.Solid.Faces)
        {
            if (face is not PlanarFace planar) continue;
            var role = FaceClassifier.Classify(planar, verticalTolDeg);
            if (!included.Contains(role))
                continue;

            var areaM2 = FormworkUnits.SquareFeetToM2(face.Area);
            switch (role)
            {
                case FormworkFaceRole.Side: side += areaM2; break;
                case FormworkFaceRole.Bottom: bottom += areaM2; break;
                case FormworkFaceRole.Top: top += areaM2; break;
            }
        }

        if (entry.GroundContactDropBottom)
            bottom = 0;

        return (side, bottom, top);
    }

    private static HashSet<FormworkFaceRole> IncludedFaces(SolidEntry entry)
    {
        if (entry.Rule is not null)
            return FormworkRuleMatcher.ParseFaces(entry.Rule);

        return entry.Role switch
        {
            FormworkElementRole.Column => [FormworkFaceRole.Side],
            FormworkElementRole.Wall => [FormworkFaceRole.Side],
            FormworkElementRole.Beam => [FormworkFaceRole.Side, FormworkFaceRole.Bottom],
            FormworkElementRole.Slab => [FormworkFaceRole.Side, FormworkFaceRole.Bottom],
            FormworkElementRole.Footing => [FormworkFaceRole.Side],
            _ => []
        };
    }
}
