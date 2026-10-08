using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit.Formwork;

/// <summary>
/// Join-independent slab formwork:
/// sketch footprint (+ Opening/Shaft holes) − plan contact of beams/columns.
/// Never uses joined solid area / JoinGeometryUtils for bottom.
/// </summary>
internal static class SlabPlanFormwork
{
    public readonly record struct Result(
        double BottomM2,
        double SideM2,
        double SupportDeductM2,
        double OuterAreaM2,
        double OuterPerimeterM,
        int SmallOpenings,
        int LargeOpenings,
        bool UsedSketch);

    public static Result? TryCompute(
        Document doc,
        Floor floor,
        FormworkRuleSet ruleSet,
        IReadOnlyList<SolidEntry>? _supportCandidatesIgnored = null)
    {
        if (!TrySketchMetrics(floor, out var outerAreaM2, out var outerPeriM, out var sketchOpenings))
            return null;

        var threshold = ruleSet.Settings.SlabOpeningThresholdM2 > 0
            ? ruleSet.Settings.SlabOpeningThresholdM2
            : 1.0;
        var thicknessM = ResolveThicknessM(floor);

        // Merge sketch holes + hosted Opening / Shaft cutters (join-independent).
        var openings = new List<(double AreaM2, double PeriM)>(sketchOpenings);
        AppendHostedOpenings(doc, floor, openings);

        double bottom = outerAreaM2;
        double sideAdd = 0;
        var small = 0;
        var large = 0;
        var seenHole = new HashSet<string>();
        foreach (var (area, peri) in openings)
        {
            // De-dupe near-identical holes from sketch + Opening collector.
            var key = $"{area:0.000}|{peri:0.000}";
            if (!seenHole.Add(key))
                continue;

            if (area < threshold)
            {
                // Keep soffit continuous — do not remove small hole from bottom.
                small++;
            }
            else
            {
                bottom -= area;
                sideAdd += peri * thicknessM;
                large++;
            }
        }

        bottom = Math.Max(0, bottom);

        var pad = FormworkUnits.MmToFeet(ruleSet.Settings.ProbeEpsilonMm);
        var zTol = FormworkUnits.MmToFeet(Math.Max(ruleSet.Settings.ProbeEpsilonMm, 80));
        var slabSoffitZ = ResolveSlabSoffitZ(floor);
        var slabTopZ = ResolveSlabTopZ(floor, slabSoffitZ, thicknessM);
        var slabXy = ResolveSlabXyBounds(floor);
        if (slabXy is null)
        {
            var sideOnly = outerPeriM * thicknessM + sideAdd;
            return new Result(bottom, sideOnly, 0, outerAreaM2, outerPeriM, small, large, true);
        }

        double supportDeduct = 0;
        var seen = new HashSet<long>();

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralFraming)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (!seen.Add(el.Id.Value)) continue;
            if (!TryBeamSupportContact(el, slabXy, slabSoffitZ, slabTopZ, pad, zTol, out var area))
                continue;
            supportDeduct += area;
        }

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (!seen.Add(el.Id.Value)) continue;
            if (!TryColumnSupportContact(el, slabXy, slabSoffitZ, slabTopZ, pad, zTol, out var area))
                continue;
            supportDeduct += area;
        }

        supportDeduct = Math.Min(supportDeduct, bottom);
        bottom = Math.Max(0, bottom - supportDeduct);

        var side = outerPeriM * thicknessM + sideAdd;
        return new Result(bottom, side, supportDeduct, outerAreaM2, outerPeriM, small, large, true);
    }

    private static void AppendHostedOpenings(
        Document doc,
        Floor floor,
        List<(double AreaM2, double PeriM)> openings)
    {
        // Rectangular / shaft openings hosted on this floor (may be absent from sketch loops).
        try
        {
            foreach (Opening op in new FilteredElementCollector(doc)
                         .OfClass(typeof(Opening))
                         .Cast<Opening>())
            {
                if (op.Host?.Id != floor.Id && op.HostId != floor.Id)
                    continue;
                if (!TryOpeningMetrics(op, out var area, out var peri) || area <= 1e-9)
                    continue;
                openings.Add((area, peri));
            }
        }
        catch
        {
            // Opening API varies by host version — sketch holes still apply.
        }

        try
        {
            foreach (var el in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_ShaftOpening)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                // Shaft may cut multiple floors; only count if XY overlaps this slab footprint.
                var bb = el.get_BoundingBox(null);
                var slabBb = floor.get_BoundingBox(null);
                if (bb is null || slabBb is null) continue;
                if (!FootprintsOverlap(bb, slabBb, FormworkUnits.MmToFeet(5)))
                    continue;
                // Vertical shaft through this level.
                if (bb.Max.Z < slabBb.Min.Z - FormworkUnits.MmToFeet(50) ||
                    bb.Min.Z > slabBb.Max.Z + FormworkUnits.MmToFeet(50))
                    continue;

                var area = FormworkUnits.FeetToM(bb.Max.X - bb.Min.X) *
                           FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y);
                var peri = 2.0 * (FormworkUnits.FeetToM(bb.Max.X - bb.Min.X) +
                                  FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y));
                if (area > 1e-9)
                    openings.Add((area, peri));
            }
        }
        catch
        {
            // optional
        }
    }

    private static bool TryOpeningMetrics(Opening op, out double areaM2, out double periM)
    {
        areaM2 = 0;
        periM = 0;
        try
        {
            var curves = op.BoundaryCurves;
            if (curves is null || curves.Size == 0)
                return false;

            var pts = new List<XYZ>();
            foreach (Curve c in curves)
            {
                if (c is null) continue;
                periM += FormworkUnits.FeetToM(c.Length);
                pts.Add(c.GetEndPoint(0));
            }

            if (pts.Count < 3)
                return false;

            double sum = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }

            areaM2 = FormworkUnits.SquareFeetToM2(Math.Abs(sum) * 0.5);
            return areaM2 > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBeamSupportContact(
        Element beam,
        BoundingBoxXYZ slabXy,
        double slabSoffitZ,
        double slabTopZ,
        double pad,
        double zTol,
        out double areaM2)
    {
        areaM2 = 0;
        var beamTopZ = ResolveBeamTopZ(beam);
        // Beam must reach slab soffit (may embed up into slab thickness).
        if (beamTopZ < slabSoffitZ - zTol || beamTopZ > slabTopZ + zTol)
            return false;

        // XY from location curve + type width (not joined BB).
        var widthM = TryBeamWidthM(beam);
        var lengthM = TryBeamLengthUnderSlabM(beam, slabXy);
        if (widthM <= 1e-9 || lengthM <= 1e-9)
            return false;

        areaM2 = widthM * lengthM;
        return areaM2 > 1e-9;
    }

    private static bool TryColumnSupportContact(
        Element column,
        BoundingBoxXYZ slabXy,
        double slabSoffitZ,
        double slabTopZ,
        double pad,
        double zTol,
        out double areaM2)
    {
        areaM2 = 0;
        var colTopZ = ResolveColumnTopZ(column);
        if (colTopZ < slabSoffitZ - zTol || colTopZ > slabTopZ + zTol)
            return false;

        var section = TryColumnSectionAreaM2(column);
        if (section <= 1e-9)
            return false;

        // Must sit under slab footprint (location point / type box vs slab XY).
        if (!ColumnFootprintOverlapsSlab(column, slabXy, pad))
            return false;

        areaM2 = section;
        return true;
    }

    private static bool ColumnFootprintOverlapsSlab(Element column, BoundingBoxXYZ slabXy, double pad)
    {
        XYZ? p = null;
        if (column.Location is LocationPoint lp)
            p = lp.Point;
        if (p is null)
        {
            var bb = column.get_BoundingBox(null);
            if (bb is null) return false;
            p = (bb.Min + bb.Max) * 0.5;
        }

        // Point-in-slab XY with pad (section extents handled by area itself).
        return p.X >= slabXy.Min.X - pad && p.X <= slabXy.Max.X + pad &&
               p.Y >= slabXy.Min.Y - pad && p.Y <= slabXy.Max.Y + pad;
    }

    private static double ResolveSlabSoffitZ(Floor floor)
    {
        var p = floor.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM)
                ?? floor.LookupParameter("Elevation at Bottom");
        if (p is not null && p.HasValue)
            return p.AsDouble();

        // Level + offset − thickness.
        var level = floor.Document.GetElement(floor.LevelId) as Level;
        var offset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
        var thick = ResolveThicknessM(floor) / 0.3048;
        return (level?.Elevation ?? 0) + offset - thick;
    }

    private static double ResolveSlabTopZ(Floor floor, double soffitZ, double thicknessM) =>
        soffitZ + thicknessM / 0.3048;

    private static BoundingBoxXYZ? ResolveSlabXyBounds(Floor floor)
    {
        // Prefer sketch extents (join-independent) over element BB.
        try
        {
            var sketchId = floor.SketchId;
            if (sketchId != ElementId.InvalidElementId &&
                floor.Document.GetElement(sketchId) is Sketch sketch &&
                sketch.Profile is { Size: > 0 })
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (CurveArray arr in sketch.Profile)
                {
                    foreach (Curve c in arr)
                    {
                        if (c is null) continue;
                        foreach (var p in new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                        {
                            minX = Math.Min(minX, p.X);
                            minY = Math.Min(minY, p.Y);
                            maxX = Math.Max(maxX, p.X);
                            maxY = Math.Max(maxY, p.Y);
                        }
                    }
                }

                if (minX < maxX && minY < maxY)
                {
                    return new BoundingBoxXYZ
                    {
                        Min = new XYZ(minX, minY, 0),
                        Max = new XYZ(maxX, maxY, 0)
                    };
                }
            }
        }
        catch
        {
            // fall through
        }

        return floor.get_BoundingBox(null);
    }

    private static double ResolveBeamTopZ(Element beam)
    {
        var p = beam.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_TOP)
                ?? beam.LookupParameter("Elevation at Top");
        if (p is not null && p.HasValue)
            return p.AsDouble();

        // Reference level + z offset + height (Top justification).
        try
        {
            var refId = beam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId();
            var level = refId is not null ? beam.Document.GetElement(refId) as Level : null;
            var zOff = beam.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.AsDouble() ?? 0;
            var h = TryBeamHeightFt(beam);
            return (level?.Elevation ?? 0) + zOff + (h > 0 ? h : 0);
        }
        catch
        {
            var bb = beam.get_BoundingBox(null);
            return bb?.Max.Z ?? 0;
        }
    }

    private static double ResolveColumnTopZ(Element column)
    {
        var p = column.get_Parameter(BuiltInParameter.COLUMN_TOP_ATTACH_CUT_PARAM); // may not exist
        var top = column.LookupParameter("Elevation at Top")
                  ?? column.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_TOP);
        if (top is not null && top.HasValue)
            return top.AsDouble();

        try
        {
            if (column is FamilyInstance fi)
            {
                var topLevelId = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId();
                var topLevel = topLevelId is not null ? column.Document.GetElement(topLevelId) as Level : null;
                var topOff = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;
                if (topLevel is not null)
                    return topLevel.Elevation + topOff;
            }
        }
        catch
        {
            // fall through
        }

        var bb = column.get_BoundingBox(null);
        return bb?.Max.Z ?? 0;
    }

    private static double TryBeamHeightFt(Element e)
    {
        try
        {
            var symbol = (e as FamilyInstance)?.Symbol;
            var h = symbol?.LookupParameter("h")?.AsDouble()
                    ?? symbol?.LookupParameter("Height")?.AsDouble();
            return h is > 1e-9 ? h.Value : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TrySketchMetrics(
        Floor floor,
        out double outerAreaM2,
        out double outerPeriM,
        out List<(double AreaM2, double PeriM)> openings)
    {
        outerAreaM2 = 0;
        outerPeriM = 0;
        openings = [];

        try
        {
            var sketchId = floor.SketchId;
            if (sketchId == ElementId.InvalidElementId)
                return false;
            if (floor.Document.GetElement(sketchId) is not Sketch sketch)
                return false;
            var profile = sketch.Profile;
            if (profile is null || profile.Size == 0)
                return false;

            var loops = new List<(double AreaM2, double PeriM)>();
            foreach (CurveArray arr in profile)
            {
                if (!TryLoopMetrics(arr, out var area, out var peri) || area <= 1e-9)
                    continue;
                loops.Add((area, peri));
            }

            if (loops.Count == 0)
                return false;

            loops.Sort((a, b) => b.AreaM2.CompareTo(a.AreaM2));
            outerAreaM2 = loops[0].AreaM2;
            outerPeriM = loops[0].PeriM;
            for (var i = 1; i < loops.Count; i++)
                openings.Add(loops[i]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoopMetrics(CurveArray arr, out double areaM2, out double periM)
    {
        areaM2 = 0;
        periM = 0;
        var pts = new List<XYZ>();
        foreach (Curve c in arr)
        {
            if (c is null) continue;
            periM += FormworkUnits.FeetToM(c.Length);
            pts.Add(c.GetEndPoint(0));
        }

        if (pts.Count < 3)
            return false;

        double sum = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        areaM2 = FormworkUnits.SquareFeetToM2(Math.Abs(sum) * 0.5);
        return areaM2 > 0;
    }

    private static double TryColumnSectionAreaM2(Element e)
    {
        try
        {
            if (e is not FamilyInstance fi)
                return 0;
            var symbol = fi.Symbol;
            var b = symbol?.LookupParameter("b")?.AsDouble()
                    ?? symbol?.LookupParameter("Width")?.AsDouble()
                    ?? e.LookupParameter("b")?.AsDouble();
            var h = symbol?.LookupParameter("h")?.AsDouble()
                    ?? symbol?.LookupParameter("Depth")?.AsDouble()
                    ?? e.LookupParameter("h")?.AsDouble();
            if (b is > 1e-9 && h is > 1e-9)
                return FormworkUnits.FeetToM(b.Value) * FormworkUnits.FeetToM(h.Value);
        }
        catch
        {
            // fall through
        }

        return 0;
    }

    private static double TryBeamWidthM(Element e)
    {
        try
        {
            var symbol = (e as FamilyInstance)?.Symbol;
            var w = symbol?.LookupParameter("b")?.AsDouble()
                    ?? symbol?.LookupParameter("Width")?.AsDouble()
                    ?? e.LookupParameter("b")?.AsDouble();
            if (w is > 1e-9)
                return FormworkUnits.FeetToM(w.Value);
        }
        catch
        {
            // fall through
        }

        return 0;
    }

    private static double TryBeamLengthUnderSlabM(Element e, BoundingBoxXYZ slabXy)
    {
        try
        {
            if (e.Location is LocationCurve lc)
            {
                var curve = lc.Curve;
                var lenFt = curve.Length;
                var steps = Math.Max(4, (int)Math.Ceiling(lenFt / FormworkUnits.MmToFeet(200)));
                var inside = 0;
                for (var i = 0; i <= steps; i++)
                {
                    var p = curve.Evaluate(i / (double)steps, true);
                    if (p.X >= slabXy.Min.X && p.X <= slabXy.Max.X &&
                        p.Y >= slabXy.Min.Y && p.Y <= slabXy.Max.Y)
                        inside++;
                }

                var ratio = inside / (double)(steps + 1);
                return FormworkUnits.FeetToM(lenFt * ratio);
            }

            // Cut Length fallback when no location curve.
            var cut = e.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH)?.AsDouble();
            if (cut is > 1e-9)
                return FormworkUnits.FeetToM(cut.Value);
        }
        catch
        {
            // fall through
        }

        return 0;
    }

    private static bool FootprintsOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b, double pad) =>
        a.Min.X - pad <= b.Max.X + pad &&
        a.Max.X + pad >= b.Min.X - pad &&
        a.Min.Y - pad <= b.Max.Y + pad &&
        a.Max.Y + pad >= b.Min.Y - pad;

    private static double ResolveThicknessM(Floor floor)
    {
        var p = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
        if (p is not null && p.HasValue)
            return FormworkUnits.FeetToM(p.AsDouble());
        var type = floor.Document.GetElement(floor.GetTypeId()) as FloorType;
        var compound = type?.GetCompoundStructure();
        if (compound is not null)
            return FormworkUnits.FeetToM(compound.GetWidth());
        return 0.15;
    }
}
