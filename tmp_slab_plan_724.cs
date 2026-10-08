using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit.Formwork;

/// <summary>
/// Join-independent slab formwork bottom:
/// sketch − large openings − plan contact of beams/columns under the soffit.
/// Never uses joined solid Area / JoinGeometryUtils (Switch Join must not change result).
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
        IReadOnlyList<SolidEntry>? _ = null)
    {
        if (!TrySketchMetrics(floor, out var outerAreaM2, out var outerPeriM, out var sketchOpenings))
            return null;

        var threshold = ruleSet.Settings.SlabOpeningThresholdM2 > 0
            ? ruleSet.Settings.SlabOpeningThresholdM2
            : 1.0;
        var thicknessM = ResolveThicknessM(floor);

        var openings = new List<(double AreaM2, double PeriM)>(sketchOpenings);
        AppendHostedOpenings(doc, floor, openings);

        double bottom = outerAreaM2;
        double sideAdd = 0;
        var small = 0;
        var large = 0;
        var seenHole = new HashSet<string>();
        foreach (var (area, peri) in openings)
        {
            var key = $"{area:0.000}|{peri:0.000}";
            if (!seenHole.Add(key))
                continue;

            if (area < threshold)
            {
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
        var slabTopZ = slabSoffitZ + thicknessM / 0.3048;
        var slabXy = ResolveSlabXyBounds(floor) ?? floor.get_BoundingBox(null);

        double supportDeduct = 0;
        var beamStrips = new List<(Curve Curve, double HalfWidthFt)>();

        if (slabXy is not null)
        {
            var seen = new HashSet<long>();
            foreach (var el in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                if (!seen.Add(el.Id.Value)) continue;
                if (!TryBeamContact(el, slabXy, slabSoffitZ, slabTopZ, zTol, out var area, out var strip))
                    continue;
                supportDeduct += area;
                if (strip is not null)
                    beamStrips.Add(strip.Value);
            }

            foreach (var el in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_StructuralColumns)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                if (!seen.Add(el.Id.Value)) continue;
                if (!TryColumnContact(el, slabXy, slabSoffitZ, slabTopZ, pad, zTol, out var area))
                    continue;
                // Column top already covered by a beam strip → do not double-count.
                if (ColumnCoveredByBeamStrip(el, beamStrips, pad))
                    continue;
                supportDeduct += area;
            }
        }

        supportDeduct = Math.Min(supportDeduct, bottom);
        bottom = Math.Max(0, bottom - supportDeduct);

        var side = outerPeriM * thicknessM + sideAdd;
        return new Result(bottom, side, supportDeduct, outerAreaM2, outerPeriM, small, large, true);
    }

    private static bool TryBeamContact(
        Element beam,
        BoundingBoxXYZ slabXy,
        double slabSoffitZ,
        double slabTopZ,
        double zTol,
        out double areaM2,
        out (Curve Curve, double HalfWidthFt)? strip)
    {
        areaM2 = 0;
        strip = null;
        var beamTopZ = ResolveBeamTopZ(beam);
        if (beamTopZ < slabSoffitZ - zTol || beamTopZ > slabTopZ + zTol)
            return false;

        var widthM = TryBeamWidthM(beam);
        var lengthM = TryBeamLengthUnderSlabM(beam, slabXy);
        if (widthM <= 1e-9 || lengthM <= 1e-9)
            return false;

        areaM2 = widthM * lengthM;
        if (beam.Location is LocationCurve lc)
            strip = (lc.Curve, widthM * 0.5 / 0.3048);
        return true;
    }

    private static bool TryColumnContact(
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

        XYZ? p = column.Location is LocationPoint lp ? lp.Point : null;
        if (p is null) return false;
        if (p.X < slabXy.Min.X - pad || p.X > slabXy.Max.X + pad ||
            p.Y < slabXy.Min.Y - pad || p.Y > slabXy.Max.Y + pad)
            return false;

        areaM2 = section;
        return true;
    }

    private static bool ColumnCoveredByBeamStrip(
        Element column,
        IReadOnlyList<(Curve Curve, double HalfWidthFt)> beamStrips,
        double pad)
    {
        if (beamStrips.Count == 0) return false;
        if (column.Location is not LocationPoint lp) return false;
        var p = lp.Point;

        var (bm, hm) = TryColumnSectionDimsM(column);
        var halfColFt = Math.Max(bm, hm) * 0.5 / 0.3048;

        foreach (var (curve, halfW) in beamStrips)
        {
            try
            {
                var proj = curve.Project(p);
                if (proj is null) continue;
                if (p.DistanceTo(proj.XYZPoint) <= halfW + halfColFt + pad)
                    return true;
            }
            catch
            {
                // continue
            }
        }

        return false;
    }

    private static void AppendHostedOpenings(
        Document doc,
        Floor floor,
        List<(double AreaM2, double PeriM)> openings)
    {
        try
        {
            foreach (Opening op in new FilteredElementCollector(doc)
                         .OfClass(typeof(Opening))
                         .Cast<Opening>())
            {
                if (op.Host?.Id != floor.Id) continue;
                if (!TryOpeningMetrics(op, out var area, out var peri) || area <= 1e-9)
                    continue;
                openings.Add((area, peri));
            }
        }
        catch
        {
            // optional
        }

        try
        {
            foreach (var el in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_ShaftOpening)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                var bb = el.get_BoundingBox(null);
                var slabBb = floor.get_BoundingBox(null);
                if (bb is null || slabBb is null) continue;
                if (!FootprintsOverlap(bb, slabBb, FormworkUnits.MmToFeet(5))) continue;
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
            if (curves is null || curves.Size == 0) return false;
            var pts = new List<XYZ>();
            foreach (Curve c in curves)
            {
                if (c is null) continue;
                periM += FormworkUnits.FeetToM(c.Length);
                pts.Add(c.GetEndPoint(0));
            }

            if (pts.Count < 3) return false;
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
            if (sketchId == ElementId.InvalidElementId) return false;
            if (floor.Document.GetElement(sketchId) is not Sketch sketch) return false;
            var profile = sketch.Profile;
            if (profile is null || profile.Size == 0) return false;

            var loops = new List<(double AreaM2, double PeriM)>();
            foreach (CurveArray arr in profile)
            {
                if (!TryLoopMetrics(arr, out var area, out var peri) || area <= 1e-9)
                    continue;
                loops.Add((area, peri));
            }

            if (loops.Count == 0) return false;
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

        if (pts.Count < 3) return false;
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

    private static BoundingBoxXYZ? ResolveSlabXyBounds(Floor floor)
    {
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

    private static double ResolveSlabSoffitZ(Floor floor)
    {
        var p = floor.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM)
                ?? floor.LookupParameter("Elevation at Bottom");
        if (p is not null && p.HasValue)
            return p.AsDouble();

        var level = floor.Document.GetElement(floor.LevelId) as Level;
        var offset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
        var thick = ResolveThicknessM(floor) / 0.3048;
        return (level?.Elevation ?? 0) + offset - thick;
    }

    private static double ResolveBeamTopZ(Element beam)
    {
        var p = beam.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_TOP)
                ?? beam.LookupParameter("Elevation at Top");
        if (p is not null && p.HasValue)
            return p.AsDouble();

        try
        {
            var refId = beam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId();
            var level = refId is not null ? beam.Document.GetElement(refId) as Level : null;
            var zOff = beam.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.AsDouble() ?? 0;
            var h = TryBeamHeightFt(beam);
            return (level?.Elevation ?? 0) + zOff + h;
        }
        catch
        {
            return beam.get_BoundingBox(null)?.Max.Z ?? 0;
        }
    }

    private static double ResolveColumnTopZ(Element column)
    {
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

        return column.get_BoundingBox(null)?.Max.Z ?? 0;
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

                return FormworkUnits.FeetToM(lenFt * (inside / (double)(steps + 1)));
            }

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

    private static double TryColumnSectionAreaM2(Element e)
    {
        var (b, h) = TryColumnSectionDimsM(e);
        return b > 1e-9 && h > 1e-9 ? b * h : 0;
    }

    private static (double B, double H) TryColumnSectionDimsM(Element e)
    {
        try
        {
            var symbol = (e as FamilyInstance)?.Symbol;
            var b = symbol?.LookupParameter("b")?.AsDouble()
                    ?? symbol?.LookupParameter("Width")?.AsDouble();
            var h = symbol?.LookupParameter("h")?.AsDouble()
                    ?? symbol?.LookupParameter("Depth")?.AsDouble();
            if (b is > 1e-9 && h is > 1e-9)
                return (FormworkUnits.FeetToM(b.Value), FormworkUnits.FeetToM(h.Value));
        }
        catch
        {
            // fall through
        }

        return (0, 0);
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
