using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit.Formwork;

/// <summary>
/// Column formwork above clear-height clip (beam soffit → slab soffit / beam top).
/// Join-independent: each framing beam occludes column faces by footprint overlap;
/// exposed = faceWidth − covered; area = exposed × bandHeight.
/// Band stops at slab soffit when present (zone cast with slab has no side formwork).
/// </summary>
internal static class ColumnShoulderResolver
{
    private const double MinHeightFt = 1e-4;
    private const double MinStripM = 1e-4;

    private enum FaceId { PosX, NegX, PosY, NegY }

    /// <summary>
    /// Returns WaitPour leftover m² keyed by (elementId, segmentIndex).
    /// Folded into Side by FormworkCalculator.
    /// </summary>
    public static Dictionary<(long Id, int Seg), double> Compute(
        Document doc,
        IReadOnlyList<SolidEntry> entries,
        FormworkRuleSet ruleSet)
    {
        var result = new Dictionary<(long Id, int Seg), double>();
        var pad = FormworkUnits.MmToFeet(ruleSet.Settings.ProbeEpsilonMm);
        var widthEpsM = ruleSet.Settings.ProbeEpsilonMm / 1000.0;
        var faceEpsFt = pad;
        var beams = GatherBeams(doc);
        var slabSoffits = GatherSlabSoffits(doc);

        foreach (var column in entries)
        {
            if (!column.ClipHeightApplied ||
                column.Role is not (FormworkElementRole.Column or FormworkElementRole.Wall) ||
                column.ClipTopZM is not double clipTopM)
                continue;

            var keep = column.Rule?.KeepUnjoinedAround;
            if (keep is null ||
                !keep.Any(k => k.Equals("Beam", StringComparison.OrdinalIgnoreCase)))
                continue;

            var fullBb = column.Element.get_BoundingBox(null) ?? column.BoundingBox;
            var clipTopFt = clipTopM / 0.3048;
            var (colXm, colYm) = ResolveColumnSectionM(column.Element, fullBb);

            var framing = beams
                .Where(b => FootprintsOverlap(fullBb, b.BoundingBox, pad))
                .Where(b => Math.Abs(b.BoundingBox.Min.Z - clipTopFt) <=
                            FormworkUnits.MmToFeet(80) + pad)
                .ToList();
            if (framing.Count == 0)
                continue;

            var bandHeightM = ResolveBandHeightM(
                framing, clipTopFt, fullBb.Max.Z, slabSoffits, pad);
            if (bandHeightM < MinStripM)
                continue;

            var covered = new Dictionary<FaceId, double>
            {
                [FaceId.PosX] = 0,
                [FaceId.NegX] = 0,
                [FaceId.PosY] = 0,
                [FaceId.NegY] = 0
            };

            foreach (var beam in framing)
            {
                var alongX = BeamSpansAlongX(beam.Element, beam.BoundingBox);
                ApplyBeamCoverage(
                    fullBb, beam.BoundingBox, colXm, colYm, alongX,
                    faceEpsFt, widthEpsM, covered);
            }

            double total = 0;
            foreach (FaceId face in Enum.GetValues(typeof(FaceId)))
            {
                var faceW = FaceWidth(face, colXm, colYm);
                var exposed = Math.Max(0, faceW - covered[face]);
                if (exposed < MinStripM)
                    continue;
                total += exposed * bandHeightM;
            }

            if (total > 1e-9)
                result[(column.ElementId.Value, column.SegmentIndex ?? -1)] = total;
        }

        return result;
    }

    /// <summary>
    /// Occlude faces from beam footprint (axis-aligned):
    /// - beam along face → can fully cover that face;
    /// - beam width overlaps side faces → covers that length on ±normal faces.
    /// </summary>
    private static void ApplyBeamCoverage(
        BoundingBoxXYZ colBb,
        BoundingBoxXYZ beamBb,
        double colXm,
        double colYm,
        bool alongX,
        double faceEpsFt,
        double widthEpsM,
        IDictionary<FaceId, double> covered)
    {
        var overlapX0 = Math.Max(colBb.Min.X, beamBb.Min.X);
        var overlapX1 = Math.Min(colBb.Max.X, beamBb.Max.X);
        var overlapY0 = Math.Max(colBb.Min.Y, beamBb.Min.Y);
        var overlapY1 = Math.Min(colBb.Max.Y, beamBb.Max.Y);
        if (overlapX1 - overlapX0 < faceEpsFt || overlapY1 - overlapY0 < faceEpsFt)
            return;

        var coverXM = FormworkUnits.FeetToM(overlapX1 - overlapX0);
        var coverYM = FormworkUnits.FeetToM(overlapY1 - overlapY0);

        if (alongX)
        {
            // Spans X: sits on ±Y; width occupies Y on ±X faces (beam punches ends).
            if (Math.Abs(beamBb.Min.Y - colBb.Min.Y) <= faceEpsFt ||
                overlapY0 <= colBb.Min.Y + faceEpsFt)
                AddCover(covered, FaceId.NegY, Math.Min(colXm, coverXM));

            if (Math.Abs(beamBb.Max.Y - colBb.Max.Y) <= faceEpsFt ||
                overlapY1 >= colBb.Max.Y - faceEpsFt)
                AddCover(covered, FaceId.PosY, Math.Min(colXm, coverXM));

            if (coverYM > widthEpsM)
            {
                AddCover(covered, FaceId.PosX, Math.Min(colYm, coverYM));
                AddCover(covered, FaceId.NegX, Math.Min(colYm, coverYM));
            }
        }
        else
        {
            // Spans Y: sits on ±X; width occupies X only on ±Y faces the beam reaches.
            if (Math.Abs(beamBb.Min.X - colBb.Min.X) <= faceEpsFt ||
                overlapX0 <= colBb.Min.X + faceEpsFt)
                AddCover(covered, FaceId.NegX, Math.Min(colYm, coverXM));

            if (Math.Abs(beamBb.Max.X - colBb.Max.X) <= faceEpsFt ||
                overlapX1 >= colBb.Max.X - faceEpsFt)
                AddCover(covered, FaceId.PosX, Math.Min(colYm, coverXM));

            if (coverXM > widthEpsM)
            {
                if (overlapY1 >= colBb.Max.Y - faceEpsFt)
                    AddCover(covered, FaceId.PosY, Math.Min(colXm, coverXM));
                if (overlapY0 <= colBb.Min.Y + faceEpsFt)
                    AddCover(covered, FaceId.NegY, Math.Min(colXm, coverXM));
            }
        }
    }

    private static void AddCover(IDictionary<FaceId, double> covered, FaceId face, double w)
    {
        if (w <= 0) return;
        covered[face] += w;
    }

    private static double FaceWidth(FaceId face, double colXm, double colYm) =>
        face is FaceId.PosX or FaceId.NegX ? colYm : colXm;

    private static bool BeamSpansAlongX(Element beam, BoundingBoxXYZ bb)
    {
        if (beam.Location is LocationCurve lc)
        {
            var dir = lc.Curve.ComputeDerivatives(0.5, true).BasisX.Normalize();
            return Math.Abs(dir.X) >= Math.Abs(dir.Y);
        }

        return bb.Max.X - bb.Min.X >= bb.Max.Y - bb.Min.Y;
    }

    private static double ResolveBandHeightM(
        IReadOnlyList<(Element Element, BoundingBoxXYZ BoundingBox)> framing,
        double clipTopFt,
        double colMaxZ,
        IReadOnlyList<double> slabSoffits,
        double pad)
    {
        // Prefer slab soffit above clip (no side formwork inside slab thickness).
        var slabAbove = slabSoffits
            .Where(z => z > clipTopFt + MinHeightFt && z <= colMaxZ + pad)
            .DefaultIfEmpty(double.NaN)
            .Min();

        double bandTopFt;
        if (!double.IsNaN(slabAbove))
        {
            bandTopFt = slabAbove;
        }
        else
        {
            var fromType = framing
                .Select(b => TryBeamHeightFt(b.Element))
                .Where(h => h > MinHeightFt)
                .DefaultIfEmpty(0)
                .Max();
            bandTopFt = fromType > MinHeightFt
                ? Math.Min(colMaxZ, clipTopFt + fromType)
                : framing.Max(b => Math.Min(b.BoundingBox.Max.Z, colMaxZ));
        }

        var h = bandTopFt - clipTopFt;
        return h > MinHeightFt ? FormworkUnits.FeetToM(h) : 0;
    }

    private static (double X, double Y) ResolveColumnSectionM(Element e, BoundingBoxXYZ bb)
    {
        try
        {
            var symbol = (e as FamilyInstance)?.Symbol;
            var b = symbol?.LookupParameter("b")?.AsDouble()
                    ?? symbol?.LookupParameter("Width")?.AsDouble()
                    ?? e.LookupParameter("b")?.AsDouble();
            var h = symbol?.LookupParameter("h")?.AsDouble()
                    ?? symbol?.LookupParameter("Depth")?.AsDouble()
                    ?? e.LookupParameter("h")?.AsDouble();
            if (b is > 1e-9 && h is > 1e-9)
                return (FormworkUnits.FeetToM(b.Value), FormworkUnits.FeetToM(h.Value));
        }
        catch
        {
            // fall through
        }

        return (
            FormworkUnits.FeetToM(bb.Max.X - bb.Min.X),
            FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y));
    }

    private static double TryBeamHeightFt(Element beam)
    {
        try
        {
            var symbol = (beam as FamilyInstance)?.Symbol;
            var h = symbol?.LookupParameter("h")?.AsDouble()
                    ?? symbol?.LookupParameter("Height")?.AsDouble()
                    ?? beam.LookupParameter("h")?.AsDouble();
            return h is > 1e-9 ? h.Value : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static List<(Element Element, BoundingBoxXYZ BoundingBox)> GatherBeams(Document doc)
    {
        var list = new List<(Element, BoundingBoxXYZ)>();
        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralFraming)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            var bb = el.get_BoundingBox(null);
            if (bb is null) continue;
            list.Add((el, bb));
        }

        return list;
    }

    private static List<double> GatherSlabSoffits(Document doc)
    {
        var list = new List<double>();
        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Floors)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            var bb = el.get_BoundingBox(null);
            if (bb is null) continue;
            list.Add(bb.Min.Z);
        }

        return list;
    }

    private static bool FootprintsOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b, double pad) =>
        a.Min.X - pad <= b.Max.X + pad &&
        a.Max.X + pad >= b.Min.X - pad &&
        a.Min.Y - pad <= b.Max.Y + pad &&
        a.Max.Y + pad >= b.Min.Y - pad;
}
