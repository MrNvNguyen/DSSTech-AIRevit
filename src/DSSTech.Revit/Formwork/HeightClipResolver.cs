using Autodesk.Revit.DB;
using DSSTech.Core.Formwork;

namespace DSSTech.Revit.Formwork;

/// <summary>
/// N3 height clip for Column/Wall when clipHeight:true.
/// Product modeling SSOT: one column instance per story (top at floor above).
/// Clip uses a single clear band [zBottom, zTop] excluding slab thickness
/// (zTop = intersecting slab/beam soffit). Continuous multi-story columns may
/// still emit multiple bands if interior clear shafts exist.
/// Column shoulder leftover (narrower beams) is handled by ColumnShoulderResolver.
/// </summary>
internal static class HeightClipResolver
{
    private const double MinBandHeightFt = 1e-4;
    private const double MinClipReductionFt = 1e-3; // ~0.3 mm — ignore no-op clips

    private readonly record struct HorizontalPlane(
        BoundingBoxXYZ BoundingBox,
        double SoffitZ,
        double TopZ);

    public static List<SolidEntry> ExpandSegments(
        Document doc,
        IReadOnlyList<SolidEntry> entries,
        FormworkRuleSet ruleSet)
    {
        var horizontals = GatherHorizontals(doc, entries);
        var result = new List<SolidEntry>();

        foreach (var entry in entries)
        {
            if (entry.Rule?.ClipHeight != true ||
                entry.Role is not (FormworkElementRole.Column or FormworkElementRole.Wall))
            {
                result.Add(entry);
                continue;
            }

            var clipped = TryClipStoryColumn(entry, horizontals, ruleSet);
            result.Add(clipped ?? entry);
        }

        return result;
    }

    /// <summary>
    /// Horizontals from the live document (floors + framing), not only the
    /// filtered takeoff set — so column-only selection still sees slab soffits.
    /// </summary>
    private static List<HorizontalPlane> GatherHorizontals(
        Document doc,
        IReadOnlyList<SolidEntry> entries)
    {
        var planes = new List<HorizontalPlane>();
        var seen = new HashSet<long>();

        foreach (var entry in entries.Where(e =>
                     e.Role is FormworkElementRole.Beam or FormworkElementRole.Slab))
        {
            if (!seen.Add(entry.ElementId.Value)) continue;
            planes.Add(new HorizontalPlane(
                entry.BoundingBox,
                entry.BoundingBox.Min.Z,
                entry.BoundingBox.Max.Z));
        }

        foreach (var cat in new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFraming })
        {
            foreach (var el in new FilteredElementCollector(doc)
                         .OfCategory(cat)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                if (!seen.Add(el.Id.Value)) continue;
                var bb = el.get_BoundingBox(null);
                if (bb is null) continue;
                planes.Add(new HorizontalPlane(bb, bb.Min.Z, bb.Max.Z));
            }
        }

        return planes;
    }

    private static SolidEntry? TryClipStoryColumn(
        SolidEntry column,
        IReadOnlyList<HorizontalPlane> horizontals,
        FormworkRuleSet ruleSet)
    {
        var colBb = column.BoundingBox;
        var colMinZ = colBb.Min.Z;
        var colMaxZ = colBb.Max.Z;
        var bboxHeightM = FormworkUnits.FeetToM(colMaxZ - colMinZ);
        var pad = FormworkUnits.MmToFeet(ruleSet.Settings.ProbeEpsilonMm);
        var midZ = (colMinZ + colMaxZ) * 0.5;

        var crossing = horizontals
            .Where(h => FootprintsOverlap(colBb, h.BoundingBox, pad))
            .Where(h => h.TopZ > colMinZ + MinBandHeightFt &&
                        h.SoffitZ < colMaxZ - MinBandHeightFt)
            .ToList();

        if (crossing.Count == 0)
            return null;

        // Story SSOT: one clear shaft — top at soffit of member(s) near column top.
        var zBottom = colMinZ;
        var soffitsNearTop = crossing
            .Select(h => h.SoffitZ)
            .Where(z => z > midZ && z < colMaxZ - MinClipReductionFt)
            .ToList();

        // Also allow soffit at/near top when column stops at slab top (typical story column).
        if (soffitsNearTop.Count == 0)
        {
            soffitsNearTop = crossing
                .Select(h => h.SoffitZ)
                .Where(z => z > colMinZ + MinBandHeightFt && z <= colMaxZ + MinBandHeightFt)
                .Where(z => colMaxZ - z >= MinClipReductionFt)
                .ToList();
        }

        if (soffitsNearTop.Count == 0)
            return null;

        var zTop = soffitsNearTop.Min();
        if (zTop - zBottom < MinBandHeightFt)
            return null;
        if (colMaxZ - zTop < MinClipReductionFt)
            return null;

        var solid = ClipWithBboxSolid(column.Solid, colBb, zBottom, zTop, pad);
        if (solid is null)
            return null;

        var segBb = new BoundingBoxXYZ
        {
            Min = new XYZ(colBb.Min.X, colBb.Min.Y, zBottom),
            Max = new XYZ(colBb.Max.X, colBb.Max.Y, zTop)
        };

        return new SolidEntry
        {
            Element = column.Element,
            ElementId = column.ElementId,
            Role = column.Role,
            Rule = column.Rule,
            Priority = column.Priority,
            Solid = solid,
            BoundingBox = segBb,
            SegmentIndex = 0,
            SegmentCount = 1,
            ClipBottomZM = FormworkUnits.FeetToM(zBottom),
            ClipTopZM = FormworkUnits.FeetToM(zTop),
            BboxHeightM = bboxHeightM,
            ClipHeightApplied = true
        };
    }

    private static bool FootprintsOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b, double pad) =>
        a.Min.X - pad <= b.Max.X + pad &&
        a.Max.X + pad >= b.Min.X - pad &&
        a.Min.Y - pad <= b.Max.Y + pad &&
        a.Max.Y + pad >= b.Min.Y - pad;

    private static Solid? ClipWithBboxSolid(
        Solid solid,
        BoundingBoxXYZ colBb,
        double lo,
        double hi,
        double pad)
    {
        try
        {
            var min = new XYZ(colBb.Min.X - pad, colBb.Min.Y - pad, lo);
            var max = new XYZ(colBb.Max.X + pad, colBb.Max.Y + pad, hi);
            var dx = max.X - min.X;
            var dy = max.Y - min.Y;
            var dz = max.Z - min.Z;
            if (dx <= 0 || dy <= 0 || dz <= 0)
                return null;

            var p0 = min;
            var p1 = new XYZ(max.X, min.Y, min.Z);
            var p2 = new XYZ(max.X, max.Y, min.Z);
            var p3 = new XYZ(min.X, max.Y, min.Z);
            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(p0, p1));
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p0));

            var boxSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                [loop], XYZ.BasisZ, dz);
            return SolidProvider.TryBoolean(solid, boxSolid, BooleanOperationsType.Intersect);
        }
        catch
        {
            return null;
        }
    }
}
