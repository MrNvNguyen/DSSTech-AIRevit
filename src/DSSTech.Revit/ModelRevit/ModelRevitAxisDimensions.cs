using System.Text.Json;
using Autodesk.Revit.DB;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// After ModelRevit creates columns/walls/beams, snap offsets to whole millimetres and place
/// linear dimensions from element faces to the nearest parallel grids.
/// </summary>
internal static class ModelRevitAxisDimensions
{
    /// <summary>Auto-dim only when face/center is this close to a grid.</summary>
    private const double MaxDimOffsetMm = 3000;
    /// <summary>Snap search — mid-bay columns are often 4–8 m from grids.</summary>
    private const double SnapSearchMm = 25000;
    /// <summary>Within this, snap the FACE (e.g. 250 from grid); beyond, snap CENTER.</summary>
    private const double NearFaceSnapMm = 1200;
    private const double DimLineSideMm = 350;
    private const double ParallelCos = 0.9986; // ~3°
    private const double WholeMmTol = 1e-6;
    /// <summary>Wall/beam snap never moves an end further than this from where ModelRevit placed it.</summary>
    private const double WallMaxMoveMm = 2.0;

    public static int CreateFromBatch(Document doc, View view, JsonElement batchData)
    {
        if (view is not ViewPlan || batchData.ValueKind != JsonValueKind.Object)
            return 0;
        if (!batchData.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return 0;

        // Include created + skipped: re-snap / replace fractional dims on existing columns too.
        var ids = new List<ElementId>();
        foreach (var item in results.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;
            if (!long.TryParse(idEl.GetString(), out var id) || id <= 0)
                continue;
            var eid = new ElementId(id);
            if (ids.Any(x => x == eid))
                continue;
            ids.Add(eid);
        }

        var grids = ids.Count == 0 ? new List<GridSeg>() : CollectGrids(doc, view);
        DSSTech.Revit.DssTechLog.Write($"axis snap: view={view.Name} ids={ids.Count} gridSegs={grids.Count}");
        if (ids.Count == 0 || grids.Count == 0)
            return 0;

        using (var snapTx = new Transaction(doc, "DSSTech MCP snap to grids"))
        {
            snapTx.Start();
            try
            {
                foreach (var id in ids)
                {
                    var element = doc.GetElement(id);
                    switch (element)
                    {
                        case FamilyInstance column when IsStructuralColumn(column):
                            SnapColumnToGridWholeMm(doc, column, grids);
                            break;
                        case FamilyInstance beam when IsStructuralFraming(beam):
                            SnapBeamToGridWholeMm(doc, beam, grids);
                            break;
                        case Wall wall:
                            SnapWallToGridWholeMm(doc, wall, grids);
                            break;
                    }
                }

                var status = snapTx.Commit();
                DSSTech.Revit.DssTechLog.Write($"axis snap commit={status}");
            }
            catch (Exception ex)
            {
                if (snapTx.GetStatus() == TransactionStatus.Started)
                    snapTx.RollBack();
                DSSTech.Revit.DssTechLog.Write($"axis snap failed: {ex.Message}");
            }
        }

        var created = 0;
        using var tx = new Transaction(doc, "DSSTech MCP dimension to grids");
        tx.Start();
        try
        {
            DeleteDimensionsReferencing(doc, view, ids);

            foreach (var id in ids)
            {
                var element = doc.GetElement(id);
                switch (element)
                {
                    case FamilyInstance column when IsStructuralColumn(column):
                        created += DimensionColumn(doc, view, column, grids);
                        break;
                    case FamilyInstance beam when IsStructuralFraming(beam):
                        created += DimensionBeam(doc, view, beam, grids);
                        break;
                    case Wall wall:
                        created += DimensionWall(doc, view, wall, grids);
                        break;
                }
            }

            var dimStatus = tx.Commit();
            DSSTech.Revit.DssTechLog.Write($"axis dims commit={dimStatus} created={created}");
            if (dimStatus != TransactionStatus.Committed)
                created = 0;
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started)
                tx.RollBack();
            DSSTech.Revit.DssTechLog.Write($"axis dimensions failed: {ex.Message}");
            return 0;
        }

        return created;
    }

    private sealed class GridSeg
    {
        public required Grid Grid { get; init; }
        public required XYZ Origin { get; init; }
        public required XYZ Direction { get; init; }
        public required XYZ Normal { get; init; }
    }

    private static List<GridSeg> CollectGrids(Document doc, View view)
    {
        var list = new List<GridSeg>();
        foreach (var grid in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
        {
            foreach (var extent in new[] { DatumExtentType.ViewSpecific, DatumExtentType.Model })
            {
                IList<Curve>? curves;
                try
                {
                    curves = grid.GetCurvesInView(extent, view);
                }
                catch
                {
                    continue;
                }

                if (curves is null)
                    continue;
                foreach (var curve in curves)
                {
                    if (curve is not Line line || !line.IsBound)
                        continue;
                    var dir = line.Direction.Normalize();
                    if (Math.Abs(dir.Z) > 0.1)
                        continue;
                    var normal = new XYZ(-dir.Y, dir.X, 0).Normalize();
                    list.Add(new GridSeg { Grid = grid, Origin = line.Origin, Direction = dir, Normal = normal });
                }

                if (list.Any(g => g.Grid.Id == grid.Id))
                    break;
            }
        }

        return list;
    }

    private static bool IsStructuralColumn(FamilyInstance column) =>
        column.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns;

    private static bool IsStructuralFraming(FamilyInstance beam) =>
        beam.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralFraming;

    private readonly record struct ColumnOffset(GridSeg Grid, string Anchor, double SignedInternal)
    {
        public double Mm => UnitConv.ToMm(SignedInternal);
    }

    /// <summary>
    /// Snap column so offsets to grids are whole mm. Near a grid → face; mid-bay → center.
    /// Absolute world rounding is wrong when grids themselves sit on fractional mm.
    /// Only two grid directions are solved together: a third (diagonal) grid would pull
    /// the first two off whole mm again.
    /// </summary>
    private static void SnapColumnToGridWholeMm(Document doc, FamilyInstance column, List<GridSeg> grids)
    {
        var snapGrids = ColumnSnapGrids(column, grids);
        if (snapGrids.Count == 0)
            return;
        var before = snapGrids.Select(g => MeasureColumn(column, g)).ToList();
        var after = before;
        for (var pass = 0; pass < 2; pass++)
        {
            var move = WholeMmMove(after);
            if (move.GetLength() < 1e-12)
                break;
            if (pass == 1)
                ReleaseMovesWithGrids(column);
            try
            {
                ElementTransformUtils.MoveElement(doc, column.Id, move);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                DSSTech.Revit.DssTechLog.Write($"axis snap col={column.Id.Value} move failed: {ex.Message}");
                break;
            }

            doc.Regenerate();
            after = snapGrids.Select(g => MeasureColumn(column, g)).ToList();
        }

        for (var i = 0; i < before.Count; i++)
            DSSTech.Revit.DssTechLog.Write(
                $"axis snap col={column.Id.Value} grid={before[i].Grid.Grid.Name} anchor={after[i].Anchor} " +
                $"before={before[i].Mm:0.00000000} after={after[i].Mm:0.00000000}");
    }

    private static List<GridSeg> ColumnSnapGrids(FamilyInstance column, List<GridSeg> grids)
    {
        if (column.Location is not LocationPoint lp)
            return new List<GridSeg>();
        var center = lp.Point;
        var faces = VerticalFaces(column);
        var candidates = new List<(GridSeg Grid, bool Aligned, double AbsMm)>();
        foreach (var axis in DistinctAxes(grids))
        {
            var nearest = NearestGrid(center, axis.Normal, grids, SnapSearchMm);
            if (nearest is null)
                continue;
            var aligned = faces.Any(f => Math.Abs(f.FaceNormal.DotProduct(nearest.Normal)) >= ParallelCos);
            var absMm = Math.Abs(UnitConv.ToMm((center - nearest.Origin).DotProduct(nearest.Normal)));
            candidates.Add((nearest, aligned, absMm));
        }

        return candidates
            .OrderByDescending(c => c.Aligned)
            .ThenBy(c => c.AbsMm)
            .Take(2)
            .Select(c => c.Grid)
            .ToList();
    }

    private static ColumnOffset MeasureColumn(FamilyInstance column, GridSeg grid)
    {
        var center = ((LocationPoint)column.Location).Point;
        var faces = VerticalFaces(column);
        var face = faces.Count > 0 ? FaceTowardGrid(faces, center, grid, NearFaceSnapMm) : null;
        var anchor = face is not null ? FacePoint(face, center) : center;
        return new ColumnOffset(grid, face is not null ? "face" : "center",
            (anchor - grid.Origin).DotProduct(grid.Normal));
    }

    /// <summary>Move that puts every offset on whole mm; two grid normals solved exactly even when not 90°.</summary>
    private static XYZ WholeMmMove(List<ColumnOffset> offsets)
    {
        var d = offsets
            .Select(o =>
            {
                var target = UnitConv.RoundWholeMm(o.Mm);
                return Math.Abs(target - o.Mm) < WholeMmTol ? 0 : UnitConv.MmToInternal(target) - o.SignedInternal;
            })
            .ToList();
        var n1 = offsets[0].Grid.Normal;
        if (offsets.Count == 1)
            return n1 * d[0];
        var n2 = offsets[1].Grid.Normal;
        var c = n1.DotProduct(n2);
        var det = 1 - c * c;
        if (det < 1e-9)
            return n1 * d[0];
        return n1 * ((d[0] - c * d[1]) / det) + n2 * ((d[1] - c * d[0]) / det);
    }

    private static void ReleaseMovesWithGrids(FamilyInstance column)
    {
        var p = column.get_Parameter(BuiltInParameter.INSTANCE_MOVES_WITH_GRID_PARAM);
        if (p is null || p.IsReadOnly || p.AsInteger() == 0)
            return;
        p.Set(0);
        DSSTech.Revit.DssTechLog.Write($"axis snap col={column.Id.Value} movesWithGrids off");
    }

    private static void SnapWallToGridWholeMm(Document doc, Wall wall, List<GridSeg> grids)
    {
        if (wall.Location is not LocationCurve { Curve: Line line })
            return;
        SnapLocationCurveToGrid(doc, wall, line, grids, "wall");
    }

    private static void SnapBeamToGridWholeMm(Document doc, FamilyInstance beam, List<GridSeg> grids)
    {
        if (beam.Location is not LocationCurve { Curve: Line line })
            return;
        // Preserve Start/End Level Offset — XY snap must not introduce Z / end-elevation drift.
        var end0 = beam.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION);
        var end1 = beam.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION);
        double? keep0 = end0 is { IsReadOnly: false } ? end0.AsDouble() : null;
        double? keep1 = end1 is { IsReadOnly: false } ? end1.AsDouble() : null;
        var z0 = line.GetEndPoint(0).Z;
        var z1 = line.GetEndPoint(1).Z;
        SnapLocationCurveToGrid(doc, beam, line, grids, "beam");
        if (beam.Location is LocationCurve { Curve: Line after })
        {
            var a0 = after.GetEndPoint(0);
            var a1 = after.GetEndPoint(1);
            if (Math.Abs(a0.Z - z0) > 1e-9 || Math.Abs(a1.Z - z1) > 1e-9)
            {
                try
                {
                    ((LocationCurve)beam.Location).Curve = Line.CreateBound(
                        new XYZ(a0.X, a0.Y, z0), new XYZ(a1.X, a1.Y, z1));
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Keep snapped XY if Z restore fails.
                }
            }
        }

        try
        {
            if (keep0 is double v0 && end0 is { IsReadOnly: false })
                end0.Set(v0);
            if (keep1 is double v1 && end1 is { IsReadOnly: false })
                end1.Set(v1);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Offsets stay as Revit left them.
        }
    }

    private static void SnapLocationCurveToGrid(Document doc, Element element, Line line, List<GridSeg> grids, string kind)
    {
        var mid = line.Evaluate(0.5, true);
        var dir = line.Direction.Normalize();
        var normal = new XYZ(-dir.Y, dir.X, 0).Normalize();
        var nearest = NearestGrid(mid, normal, grids, SnapSearchMm);
        if (nearest is null)
            return;
        var faces = VerticalFaces(element);
        var face = FaceTowardGrid(faces, mid, nearest, NearFaceSnapMm)
                   ?? faces.FirstOrDefault(f => Math.Abs(f.FaceNormal.DotProduct(normal)) >= ParallelCos);
        var anchor = face is not null ? FacePoint(face, mid) : mid;
        if (face is null || Math.Abs(UnitConv.ToMm((anchor - nearest.Origin).DotProduct(nearest.Normal))) > NearFaceSnapMm)
            anchor = mid;
        if (TryRebuildLocationCurveOnGrids(element, line, anchor, nearest, grids, kind))
            return;
        var delta = WholeMmDelta(anchor, nearest);
        // Horizontal members: never move Z (grid normals are XY, but keep this explicit).
        delta = new XYZ(delta.X, delta.Y, 0);
        DSSTech.Revit.DssTechLog.Write(
            $"axis snap {kind}={element.Id.Value} grid={nearest.Grid.Name} " +
            $"before={UnitConv.ToMm((anchor - nearest.Origin).DotProduct(nearest.Normal)):0.00000000} " +
            $"moveMm={UnitConv.ToMm(delta.DotProduct(nearest.Normal)):0.00000000}");
        if (delta.GetLength() < 1e-12)
            return;
        try
        {
            ElementTransformUtils.MoveElement(doc, element.Id, delta);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Keep element if locked.
        }
    }

    /// <summary>
    /// Rotate-aware wall snap: make the location line exactly parallel to the grid, put the face/center
    /// offset on whole mm, and put both ends on whole mm from the nearest perpendicular grid (else keep
    /// a whole-mm length). Skipped when any end would move more than <see cref="WallMaxMoveMm"/>.
    /// </summary>
    private static bool TryRebuildLocationCurveOnGrids(
        Element element, Line line, XYZ anchor, GridSeg parallel, List<GridSeg> grids, string kind)
    {
        var p0 = line.GetEndPoint(0);
        var p1 = line.GetEndPoint(1);
        var n = parallel.Normal;
        var u = parallel.Direction.DotProduct(line.Direction) >= 0 ? parallel.Direction : parallel.Direction.Negate();
        var anchorOff = (anchor - parallel.Origin).DotProduct(n);
        var shift = UnitConv.MmToInternal(UnitConv.RoundWholeMm(UnitConv.ToMm(anchorOff))) - anchorOff;
        var mid = line.Evaluate(0.5, true);
        var across = (mid - parallel.Origin).DotProduct(n) + shift;

        var perp = NearestGrid(mid, u, grids, SnapSearchMm);
        XYZ origin;
        XYZ axis;
        if (perp is not null)
        {
            origin = perp.Origin;
            axis = perp.Normal.DotProduct(u) >= 0 ? perp.Normal : perp.Normal.Negate();
        }
        else
        {
            origin = parallel.Origin;
            axis = u;
        }

        var (s0, s1) = WholeMmSpan(UnitConv.ToMm((p0 - origin).DotProduct(axis)), UnitConv.ToMm((p1 - origin).DotProduct(axis)));
        // Keep original Z (level + offset). Grid origins from the view may carry a cut-plane Z.
        var zKeep = 0.5 * (p0.Z + p1.Z);
        var q0 = OnGrids(parallel.Origin, n, across, origin, axis, UnitConv.MmToInternal(s0), zKeep);
        var q1 = OnGrids(parallel.Origin, n, across, origin, axis, UnitConv.MmToInternal(s1), zKeep);
        if (q0 is null || q1 is null)
            return false;
        var move0 = UnitConv.ToMm(q0.DistanceTo(p0));
        var move1 = UnitConv.ToMm(q1.DistanceTo(p1));
        DSSTech.Revit.DssTechLog.Write(
            $"axis snap {kind}={element.Id.Value} grid={parallel.Grid.Name} perp={perp?.Grid.Name ?? "-"} " +
            $"across={UnitConv.ToMm(anchorOff):0.00000000} ends=({s0:0.###},{s1:0.###}) moveMm=({move0:0.000},{move1:0.000})");
        if (move0 > WallMaxMoveMm || move1 > WallMaxMoveMm)
            return false;
        if (move0 < 1e-6 && move1 < 1e-6)
            return true;
        if (element.Location is not LocationCurve location)
            return false;
        try
        {
            location.Curve = Line.CreateBound(q0, q1);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            DSSTech.Revit.DssTechLog.Write($"axis snap {kind}={element.Id.Value} rebuild failed: {ex.Message}");
            return false;
        }

        return true;
    }

    /// <summary>Whole-mm length; the end nearer a 5 mm station keeps its whole-mm offset.</summary>
    private static (double Start, double End) WholeMmSpan(double s0, double s1)
    {
        var len = UnitConv.RoundWholeMm(s1 - s0);
        static double Off5(double v) => Math.Abs(v - Math.Round(v / 5.0, MidpointRounding.AwayFromZero) * 5.0);
        if (Off5(s1) < Off5(s0))
        {
            var end = UnitConv.RoundWholeMm(s1);
            return (end - len, end);
        }

        var start = UnitConv.RoundWholeMm(s0);
        return (start, start + len);
    }

    /// <summary>Plan point with offset <paramref name="a"/> along <paramref name="n"/> from o1 and <paramref name="b"/> along <paramref name="m"/> from o2.</summary>
    private static XYZ? OnGrids(XYZ o1, XYZ n, double a, XYZ o2, XYZ m, double b, double z)
    {
        var det = n.X * m.Y - n.Y * m.X;
        if (Math.Abs(det) < 1e-9)
            return null;
        var c1 = a + o1.X * n.X + o1.Y * n.Y;
        var c2 = b + o2.X * m.X + o2.Y * m.Y;
        return new XYZ((c1 * m.Y - c2 * n.Y) / det, (n.X * c2 - m.X * c1) / det, z);
    }

    private static XYZ WholeMmDelta(XYZ point, GridSeg grid)
    {
        var signedInternal = (point - grid.Origin).DotProduct(grid.Normal);
        var distMm = UnitConv.ToMm(signedInternal);
        var targetMm = UnitConv.RoundWholeMm(distMm);
        if (Math.Abs(targetMm - distMm) < WholeMmTol)
            return XYZ.Zero;
        return grid.Normal * (UnitConv.MmToInternal(targetMm) - signedInternal);
    }

    private static PlanarFace? FaceTowardGrid(List<PlanarFace> faces, XYZ center, GridSeg grid, double maxAbsMm)
    {
        PlanarFace? best = null;
        var bestAbs = double.MaxValue;
        foreach (var face in faces)
        {
            if (Math.Abs(face.FaceNormal.DotProduct(grid.Normal)) < ParallelCos)
                continue;
            var facePt = FacePoint(face, center);
            var abs = Math.Abs(UnitConv.ToMm((facePt - grid.Origin).DotProduct(grid.Normal)));
            if (abs >= bestAbs || abs > maxAbsMm)
                continue;
            bestAbs = abs;
            best = face;
        }

        return best;
    }

    private static int DimensionColumn(Document doc, View view, FamilyInstance column, List<GridSeg> grids)
    {
        var faces = VerticalFaces(column);
        if (faces.Count == 0)
            return 0;
        var center = column.Location is LocationPoint lp ? lp.Point : XYZ.Zero;
        var count = 0;
        foreach (var axis in DistinctAxes(grids))
        {
            var nearest = NearestGrid(center, axis.Normal, grids, MaxDimOffsetMm);
            if (nearest is null)
                continue;
            var faceToward = FaceTowardGrid(faces, center, nearest, MaxDimOffsetMm);
            if (faceToward?.Reference is null)
                continue;
            var facePt = FacePoint(faceToward, center);
            var offsetMm = Math.Abs(UnitConv.ToMm((facePt - nearest.Origin).DotProduct(nearest.Normal)));
            if (offsetMm < 1 || offsetMm > MaxDimOffsetMm)
                continue;
            if (Math.Abs(offsetMm - UnitConv.RoundWholeMm(offsetMm)) > 0.05)
                continue;
            if (TryNewDimension(doc, view, nearest.Grid, faceToward.Reference, facePt, faceToward.FaceNormal))
                count++;
        }

        return count;
    }

    private static int DimensionWall(Document doc, View view, Wall wall, List<GridSeg> grids) =>
        DimensionLocationCurve(doc, view, wall, grids);

    private static int DimensionBeam(Document doc, View view, FamilyInstance beam, List<GridSeg> grids) =>
        DimensionLocationCurve(doc, view, beam, grids);

    private static int DimensionLocationCurve(Document doc, View view, Element element, List<GridSeg> grids)
    {
        if (element.Location is not LocationCurve { Curve: Line line })
            return 0;
        var mid = line.Evaluate(0.5, true);
        var dir = line.Direction.Normalize();
        var normal = new XYZ(-dir.Y, dir.X, 0).Normalize();
        var faces = VerticalFaces(element);
        PlanarFace? face = faces
            .OrderByDescending(f => Math.Abs(f.FaceNormal.DotProduct(normal)))
            .FirstOrDefault(f => f.Reference is not null && Math.Abs(f.FaceNormal.DotProduct(normal)) >= ParallelCos);
        if (face?.Reference is null)
            return 0;
        var nearest = NearestGrid(mid, normal, grids, MaxDimOffsetMm);
        if (nearest is null)
            return 0;
        var facePt = FacePoint(face, mid);
        var offsetMm = Math.Abs(UnitConv.ToMm((facePt - nearest.Origin).DotProduct(nearest.Normal)));
        if (offsetMm < 1 || offsetMm > MaxDimOffsetMm)
            return 0;
        if (Math.Abs(offsetMm - UnitConv.RoundWholeMm(offsetMm)) > 0.05)
            return 0;
        return TryNewDimension(doc, view, nearest.Grid, face.Reference, facePt, face.FaceNormal) ? 1 : 0;
    }

    private static bool TryNewDimension(
        Document doc, View view, Grid grid, Reference faceRef, XYZ facePoint, XYZ measureDir)
    {
        measureDir = new XYZ(measureDir.X, measureDir.Y, 0);
        if (measureDir.GetLength() < 1e-9)
            return false;
        measureDir = measureDir.Normalize();
        var side = new XYZ(-measureDir.Y, measureDir.X, 0);
        var origin = facePoint + side * UnitConv.MmToInternal(DimLineSideMm);
        var half = UnitConv.MmToInternal(500);
        var dimLine = Line.CreateBound(origin - measureDir * half, origin + measureDir * half);
        var refs = new ReferenceArray();
        refs.Append(new Reference(grid));
        refs.Append(faceRef);
        try
        {
            var dim = doc.Create.NewDimension(view, dimLine, refs);
            // Refuse fractional measured values (grid not on whole mm, bad ref, etc.).
            if (dim.Value is double feet)
            {
                var mm = Math.Abs(UnitConv.ToMm(feet));
                if (Math.Abs(mm - UnitConv.RoundWholeMm(mm)) > 0.05)
                {
                    doc.Delete(dim.Id);
                    return false;
                }
            }

            return true;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            return false;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return false;
        }
    }

    private static void DeleteDimensionsReferencing(Document doc, View view, List<ElementId> elementIds)
    {
        if (elementIds.Count == 0)
            return;
        var set = new HashSet<ElementId>(elementIds);
        var toDelete = new List<ElementId>();
        foreach (var dim in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            try
            {
                var refs = dim.References;
                if (refs is null)
                    continue;
                for (var i = 0; i < refs.Size; i++)
                {
                    var el = doc.GetElement(refs.get_Item(i));
                    if (el is null || !set.Contains(el.Id))
                        continue;
                    toDelete.Add(dim.Id);
                    break;
                }
            }
            catch
            {
                // Ignore dims we cannot inspect.
            }
        }

        if (toDelete.Count > 0)
            doc.Delete(toDelete);
    }

    private static List<PlanarFace> VerticalFaces(Element element)
    {
        var faces = new List<PlanarFace>();
        var opt = new Options
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = false
        };
        var geo = element.get_Geometry(opt);
        if (geo is null)
            return faces;
        foreach (var obj in geo)
            CollectFaces(obj, Transform.Identity, faces);
        return faces;
    }

    private static void CollectFaces(GeometryObject obj, Transform transform, List<PlanarFace> faces)
    {
        switch (obj)
        {
            case GeometryInstance gi:
                foreach (var child in gi.GetInstanceGeometry())
                    CollectFaces(child, Transform.Identity, faces);
                break;
            case Solid solid when solid.Faces.Size > 0:
                foreach (Face face in solid.Faces)
                {
                    if (face is not PlanarFace planar)
                        continue;
                    var normal = transform.OfVector(planar.FaceNormal).Normalize();
                    if (Math.Abs(normal.Z) > 0.15)
                        continue;
                    faces.Add(planar);
                }
                break;
        }
    }

    private static XYZ FacePoint(PlanarFace face, XYZ near)
    {
        try
        {
            var result = face.Project(near);
            if (result is not null)
                return result.XYZPoint;
        }
        catch
        {
            // fall through
        }

        return face.Origin;
    }

    private static GridSeg? NearestGrid(XYZ point, XYZ axisNormal, List<GridSeg> grids, double maxAbsMm)
    {
        GridSeg? best = null;
        var bestAbs = double.MaxValue;
        foreach (var grid in grids)
        {
            if (Math.Abs(grid.Normal.DotProduct(axisNormal)) < ParallelCos &&
                Math.Abs(grid.Normal.DotProduct(axisNormal.Negate())) < ParallelCos)
                continue;
            var distMm = Math.Abs(UnitConv.ToMm((point - grid.Origin).DotProduct(grid.Normal)));
            if (distMm >= bestAbs || distMm > maxAbsMm)
                continue;
            bestAbs = distMm;
            best = grid;
        }

        return best;
    }

    private static List<(XYZ Normal, XYZ Direction)> DistinctAxes(List<GridSeg> grids)
    {
        var axes = new List<(XYZ Normal, XYZ Direction)>();
        foreach (var grid in grids)
        {
            var known = false;
            foreach (var axis in axes)
            {
                if (Math.Abs(axis.Normal.DotProduct(grid.Normal)) >= ParallelCos)
                {
                    known = true;
                    break;
                }
            }

            if (!known)
                axes.Add((grid.Normal, grid.Direction));
        }

        return axes;
    }
}
