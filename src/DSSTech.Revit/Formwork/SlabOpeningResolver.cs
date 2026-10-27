using Autodesk.Revit.DB;
using DSSTech.Core.Formwork;

namespace DSSTech.Revit.Formwork;

/// <summary>
/// Slab opening formwork:
/// solid bottom is already net of openings — restore openings &lt; threshold to Bottom;
/// for openings ≥ threshold keep hole out of Bottom and add vertical Side (perimeter × thickness).
/// </summary>
internal static class SlabOpeningResolver
{
    public readonly record struct OpeningAdjust(double BottomAddM2, double SideAddM2, int SmallCount, int LargeCount);

    public static OpeningAdjust Compute(Document doc, SolidEntry entry, FormworkRuleSet ruleSet)
    {
        if (entry.Role is not FormworkElementRole.Slab)
            return default;
        if (entry.Element is not Floor floor)
            return default;

        var threshold = ruleSet.Settings.SlabOpeningThresholdM2;
        if (threshold <= 0)
            threshold = 1.0;

        var thicknessM = ResolveThicknessM(floor);
        if (thicknessM <= 1e-6)
            thicknessM = FormworkUnits.FeetToM(floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0);

        double bottomAdd = 0;
        double sideAdd = 0;
        var small = 0;
        var large = 0;

        foreach (var openingId in floor.FindInserts(true, false, false, false))
        {
            var el = doc.GetElement(openingId);
            if (el is null) continue;

            if (!TryOpeningMetrics(el, out var areaM2, out var perimeterM))
                continue;
            if (areaM2 <= 1e-6)
                continue;

            if (areaM2 + 0.001 >= threshold)
            {
                // Large hole (≥ threshold, 0.001 m² tol): keep bottom cut; add vertical formwork.
                sideAdd += perimeterM * thicknessM;
                large++;
            }
            else
            {
                // Small hole: form as continuous soffit — add area back (solid already cut it out).
                bottomAdd += areaM2;
                small++;
            }
        }

        return new OpeningAdjust(bottomAdd, sideAdd, small, large);
    }

    private static double ResolveThicknessM(Floor floor)
    {
        var p = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
        if (p is not null && p.HasValue)
            return FormworkUnits.FeetToM(p.AsDouble());
        var type = floor.Document.GetElement(floor.GetTypeId()) as FloorType;
        var compound = type?.GetCompoundStructure();
        if (compound is not null)
            return FormworkUnits.FeetToM(compound.GetWidth());
        var bb = floor.get_BoundingBox(null);
        return bb is null ? 0 : FormworkUnits.FeetToM(bb.Max.Z - bb.Min.Z);
    }

    private static bool TryOpeningMetrics(Element el, out double areaM2, out double perimeterM)
    {
        areaM2 = 0;
        perimeterM = 0;

        if (el is Opening opening)
        {
            // Shaft / rectangular opening boundary in XY.
            var curves = opening.BoundaryCurves;
            if (curves is not null && curves.Size > 0)
                return TryFromCurves(curves, out areaM2, out perimeterM);

            var bb = opening.get_BoundingBox(null);
            if (bb is null) return false;
            var dx = FormworkUnits.FeetToM(bb.Max.X - bb.Min.X);
            var dy = FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y);
            areaM2 = Math.Abs(dx * dy);
            perimeterM = 2 * (Math.Abs(dx) + Math.Abs(dy));
            return areaM2 > 0;
        }

        // Family-based floor opening / shaft opening as FamilyInstance
        var bb2 = el.get_BoundingBox(null);
        if (bb2 is null) return false;
        var w = FormworkUnits.FeetToM(bb2.Max.X - bb2.Min.X);
        var d = FormworkUnits.FeetToM(bb2.Max.Y - bb2.Min.Y);
        areaM2 = Math.Abs(w * d);
        perimeterM = 2 * (Math.Abs(w) + Math.Abs(d));
        return areaM2 > 0;
    }

    private static bool TryFromCurves(CurveArray curves, out double areaM2, out double perimeterM)
    {
        areaM2 = 0;
        perimeterM = 0;
        var pts = new List<XYZ>();
        foreach (Curve c in curves)
        {
            perimeterM += FormworkUnits.FeetToM(c.Length);
            pts.Add(c.GetEndPoint(0));
        }

        if (pts.Count < 3)
            return false;

        // Planar shoelace in XY (ft² → m²).
        double sum = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        areaM2 = FormworkUnits.SquareFeetToM2(Math.Abs(sum) * 0.5);
        return areaM2 > 0 && perimeterM > 0;
    }
}
