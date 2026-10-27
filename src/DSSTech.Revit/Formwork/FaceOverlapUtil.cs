using Autodesk.Revit.DB;

namespace DSSTech.Revit.Formwork;

/// <summary>
/// Coplanar face overlap — OOG FaceUtil.OverlappingResult
/// (<c>docs/formwork_logic_code.html</c>):
/// same plane → extrude both loops → boolean Intersect → area = |V| / extrudeDist.
/// </summary>
internal static class FaceOverlapUtil
{
    private const double ExtrudeDistFt = 0.1;
    private const double OffsetRetry1Ft = 0.005;
    private const double OffsetRetry2Ft = 0.001;
    private const double MinVolumeFt3 = 1e-6;
    /// <summary>~3 mm — OOG Round(3) ft is stricter; gaps after join need a little slack.</summary>
    private const double PlaneGapTolFt = 0.01;
    private const double ParallelDotMin = 0.999;

    /// <summary>(hasOverlap, areaFt2, failed). Area in Revit square feet.</summary>
    public static (bool Overlap, double AreaFt2, bool Failed) OverlappingResult(Face f1, Face f2)
    {
        if (!IsPlanarFaceOnSamePlane(f1, f2))
            return (false, 0, false);

        var loops1 = SafeLoops(f1);
        var loops2 = SafeLoops(f2);
        if (loops1.Count == 0 || loops2.Count == 0)
            return (false, 0, true);

        var normal = f1.ComputeNormal(new UV(0.5, 0.5));
        try
        {
            if (TryIntersectArea(loops1, loops2, normal, ExtrudeDistFt, out var area))
                return (area > 0, area, false);
            return (false, 0, false);
        }
        catch
        {
            try
            {
                var off1 = OffsetLoops(loops1, f1, OffsetRetry1Ft);
                var off2 = OffsetLoops(loops2, f2, OffsetRetry2Ft);
                if (off1.Count == 0 || off2.Count == 0)
                    return (false, 0, true);
                if (TryIntersectArea(off1, off2, normal, ExtrudeDistFt, out var area))
                    return (area > 0, area, false);
                return (false, 0, false);
            }
            catch
            {
                return (false, 0, true);
            }
        }
    }

    /// <summary>
    /// OOG: normals parallel (incl. anti-parallel) and same plane distance from origin.
    /// Implemented via projection on f1 normal + gap tol (more stable for beam top ↔ slab soffit).
    /// </summary>
    public static bool IsPlanarFaceOnSamePlane(Face f1, Face f2)
    {
        if (f1 is not PlanarFace || f2 is not PlanarFace)
            return false;

        var n1 = f1.ComputeNormal(new UV(0.5, 0.5));
        var n2 = f2.ComputeNormal(new UV(0.5, 0.5));
        if (!IsParallel(n1, n2))
            return false;

        var pt1 = f1.Evaluate(new UV(0.5, 0.5));
        var pt2 = f2.Evaluate(new UV(0.5, 0.5));
        // Project both points onto f1 normal — works for parallel and anti-parallel n2.
        var d1 = pt1.DotProduct(n1);
        var d2 = pt2.DotProduct(n1);
        return Math.Abs(d1 - d2) <= PlaneGapTolFt;
    }

    public static IReadOnlyList<Face> SideFaces(Solid solid, double verticalTolDeg = 15)
    {
        var list = new List<Face>();
        if (solid is null) return list;
        foreach (Face face in solid.Faces)
        {
            if (FaceClassifier.Classify(face, verticalTolDeg) == Core.Formwork.FormworkFaceRole.Side)
                list.Add(face);
        }

        return list;
    }

    public static double SumFaceAreasFt2(IEnumerable<Face> faces)
    {
        double sum = 0;
        foreach (var f in faces)
        {
            try { sum += f.Area; }
            catch { /* skip */ }
        }

        return sum;
    }

    public static Solid? TryGetMergedSolid(Element element)
    {
        try
        {
            var opt = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = true,
                IncludeNonVisibleObjects = false
            };
            var geo = element.get_Geometry(opt);
            if (geo is null) return null;
            var solids = new List<Solid>();
            CollectSolids(geo, solids);
            return SolidProvider.MergeSolids(solids);
        }
        catch
        {
            return null;
        }
    }

    private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
    {
        foreach (var obj in geometry)
        {
            switch (obj)
            {
                case Solid solid when solid.Volume > 1e-9:
                    solids.Add(solid);
                    break;
                case GeometryInstance instance:
                    var inst = instance.GetInstanceGeometry();
                    if (inst is not null) CollectSolids(inst, solids);
                    break;
            }
        }
    }

    private static bool TryIntersectArea(
        IList<CurveLoop> loops1,
        IList<CurveLoop> loops2,
        XYZ extrudeDir,
        double extrudeDist,
        out double areaFt2)
    {
        areaFt2 = 0;
        var solid1 = GeometryCreationUtilities.CreateExtrusionGeometry(loops1, extrudeDir, extrudeDist);
        var solid2 = GeometryCreationUtilities.CreateExtrusionGeometry(loops2, extrudeDir, extrudeDist);
        if (solid1 is null || solid2 is null) return false;

        var intersect = BooleanOperationsUtils.ExecuteBooleanOperation(
            solid1, solid2, BooleanOperationsType.Intersect);
        var volume = Math.Abs(intersect?.Volume ?? 0);
        if (volume <= MinVolumeFt3)
            return false;

        areaFt2 = volume / extrudeDist;
        return true;
    }

    private static IList<CurveLoop> SafeLoops(Face face)
    {
        try
        {
            return face.GetEdgesAsCurveLoops() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IList<CurveLoop> OffsetLoops(IList<CurveLoop> loops, Face face, double offsetFt)
    {
        var normal = face.ComputeNormal(new UV(0.5, 0.5));
        var result = new List<CurveLoop>();
        foreach (var cl in loops)
        {
            try
            {
                result.Add(CurveLoop.CreateViaOffset(cl, offsetFt, normal));
            }
            catch
            {
                // skip
            }
        }

        return result;
    }

    private static bool IsParallel(XYZ a, XYZ b)
    {
        var lenA = a.GetLength();
        var lenB = b.GetLength();
        if (lenA < 1e-12 || lenB < 1e-12) return false;
        var dot = Math.Abs(a.DotProduct(b) / (lenA * lenB));
        return dot >= ParallelDotMin;
    }
}
