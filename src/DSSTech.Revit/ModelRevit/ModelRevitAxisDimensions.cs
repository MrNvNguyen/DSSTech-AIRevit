using System.Text.Json;
using Autodesk.Revit.DB;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// After ModelRevit creates columns/walls/beams/piles, snap offsets to 5 mm steps (xxx0 / xxx5) and place
/// linear dimensions from element faces (or pile center ± diameter/2) to the nearest parallel grids.
/// </summary>
internal static class ModelRevitAxisDimensions
{
    /// <summary>Offsets to grids land on multiples of this. Not 10/50 — that moves elements visibly off the CAD.</summary>
    private const double SnapStepMm = UnitConv.SnapStepMm;
    /// <summary>Auto-dim only when face/center is this close to a grid.</summary>
    private const double MaxDimOffsetMm = 3000;
    /// <summary>Snap search — mid-bay columns are often 4–8 m from grids.</summary>
    private const double SnapSearchMm = 25000;
    /// <summary>Within this, snap the FACE (e.g. 250 from grid); beyond, snap CENTER.</summary>
    private const double NearFaceSnapMm = 1200;
    private const double DimLineSideMm = 350;
    private const double ParallelCos = 0.9986; // ~3°
    /// <summary>Geometry is clean when offset is this close to a 5 mm station (mm).</summary>
    private const double GeomOkMm = 0.001;
    /// <summary>Below this a nudge is pointless (double precision on model coordinates).</summary>
    private const double ExactTolMm = 1e-9;
    /// <summary>Dimension types show 8 decimals: 699.99999998 is visible noise, so dims must be within half of 1e-8.</summary>
    private const double DimDisplayTolMm = 5e-9;
    /// <summary>Always apply a nudge this small (float dust / near-integer).</summary>
    private const double TinyNudgeMm = 0.05;
    /// <summary>
    /// Half of <see cref="SnapStepMm"/> — distance to the nearest 5 mm station never exceeds this.
    /// Snap moves within this are always allowed; larger end rebuilds may still run up to SafetyMaxMoveMm.
    /// </summary>
    private const double HalfStepMm = 2.5;
    /// <summary>Hard safety: refuse only when the intended station is absurdly far (should not happen for nearest).</summary>
    private const double SafetyMaxMoveMm = 5.0;

    private static double RoundStep(double mm) => UnitConv.Round5Mm(mm);

    private static bool OnStep(double mm, double tolMm = GeomOkMm) =>
        Math.Abs(mm - RoundStep(mm)) <= tolMm;

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
        var existing = ids.Count == 0 ? new List<ElementId>() : ExistingInView(doc, view, new HashSet<ElementId>(ids));
        DSSTech.Revit.DssTechLog.Write(
            $"axis snap: view={view.Name} ids={ids.Count} existing={existing.Count} gridSegs={grids.Count}");
        if (ids.Count == 0 || grids.Count == 0)
            return 0;

        using (var snapTx = new Transaction(doc, "DSSTech MCP snap to grids"))
        {
            snapTx.Start();
            try
            {
                // Sweep 1: new batch (rebuild allowed). Sweep 2 re-measures after a full regenerate (moves of
                // joined neighbours) and also snaps elements from earlier runs: translate-only, ≤ half step.
                // Existing dims follow their references, so they are neither deleted nor duplicated.
                SnapAll(doc, ids, grids, allowRebuild: true);
                doc.Regenerate();
                SnapAll(doc, ids.Concat(existing), grids, allowRebuild: false);

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
                    case FamilyInstance pile when IsStructuralFoundation(pile) && IsCircularPileLike(pile):
                        created += DimensionPile(doc, view, pile, grids);
                        break;
                    case FamilyInstance beam when IsStructuralFraming(beam):
                        created += DimensionBeam(doc, view, beam, grids);
                        break;
                    case Wall wall:
                        created += DimensionWall(doc, view, wall, grids);
                        break;
                }
            }

            created -= DeleteOffStepDimensions(doc, view, ids);
            var dimStatus = tx.Commit();
            DSSTech.Revit.DssTechLog.Write($"axis dims commit={dimStatus} created={created}");
            LogOffStepDimensions(doc, view);
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

    private static void SnapAll(Document doc, IEnumerable<ElementId> ids, List<GridSeg> grids, bool allowRebuild)
    {
        foreach (var id in ids)
        {
            switch (doc.GetElement(id))
            {
                case FamilyInstance column when IsStructuralColumn(column):
                    SnapColumnToGridWholeMm(doc, column, grids);
                    break;
                // Circular piles only — rectangular đài/footing Width≠diameter; MovesWithGrids +
                // face-as-rim was jumping hundreds of mm off the CAD centroid (log ~500 mm).
                case FamilyInstance pile when IsStructuralFoundation(pile) && IsCircularPileLike(pile):
                    SnapPileToGridWholeMm(doc, pile, grids);
                    break;
                case FamilyInstance beam when IsStructuralFraming(beam):
                    SnapBeamToGridWholeMm(doc, beam, grids, allowRebuild);
                    break;
                case Wall wall:
                    SnapWallToGridWholeMm(doc, wall, grids, allowRebuild);
                    break;
            }
        }
    }

    /// <summary>Structural columns / framing / walls / foundations visible in the plan, not pinned, not in groups.</summary>
    private static List<ElementId> ExistingInView(Document doc, View view, HashSet<ElementId> skip)
    {
        var filter = new ElementMulticategoryFilter(new[]
        {
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_StructuralFoundation
        });
        var list = new List<ElementId>();
        foreach (var e in new FilteredElementCollector(doc, view.Id).WherePasses(filter).WhereElementIsNotElementType())
        {
            if (skip.Contains(e.Id) || e.Pinned || e.GroupId != ElementId.InvalidElementId)
                continue;
            if (e is Wall wall && wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() != 1)
                continue;
            // Point foundations / piles only (strip foundations use LocationCurve).
            if (e is FamilyInstance fi
                && fi.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralFoundation
                && fi.Location is not LocationPoint)
                continue;
            if (e is not (FamilyInstance or Wall))
                continue;
            list.Add(e.Id);
        }

        return list;
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

    private static bool IsStructuralFoundation(FamilyInstance fi) =>
        fi.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralFoundation
        && fi.Location is LocationPoint;

    /// <summary>
    /// True piles (D### / equal round section). False for đài/footing where Width is a side length —
    /// using it as radius made MeasurePile face-snap pull instances toward grids.
    /// </summary>
    private static bool IsCircularPileLike(FamilyInstance fi)
    {
        if (fi.Symbol is not { } symbol)
            return false;
        foreach (var name in new[] { "Diameter", "diameter", "Đường kính", "Dia" })
        {
            var p = symbol.LookupParameter(name);
            if (p is { StorageType: StorageType.Double } && p.AsDouble() > 0)
                return true;
        }

        var typeName = symbol.Name ?? "";
        if (System.Text.RegularExpressions.Regex.IsMatch(typeName, @"[Dd]\s*\d{2,4}\b"))
            return true;

        // Equal b×h and name looks round — treat as pile section.
        var w = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
        var h = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
        if (w is { StorageType: StorageType.Double } && h is { StorageType: StorageType.Double }
            && w.AsDouble() > 0 && Math.Abs(w.AsDouble() - h.AsDouble()) < 1e-9
            && (typeName.Contains("PHC", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("pile", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("cọc", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private readonly record struct ColumnOffset(GridSeg Grid, string Anchor, double SignedInternal)
    {
        public double Mm => UnitConv.ToMm(SignedInternal);
    }

    /// <summary>
    /// Snap column so offsets to grids are 5 mm steps. Near a grid → face; mid-bay → center.
    /// Absolute world rounding is wrong when grids themselves sit on fractional mm.
    /// Only two grid directions are solved together: a third (diagonal) grid would pull
    /// the first two off the 5 mm step again.
    /// </summary>
    private static void SnapColumnToGridWholeMm(Document doc, FamilyInstance column, List<GridSeg> grids) =>
        SnapPointFamilyToGrid(doc, column, grids, MeasureColumn, "col");

    /// <summary>
    /// Point piles / isolated foundations: LocationPoint + type diameter (not solid faces — cylinders
    /// are unreliable). Near a grid → rim; mid-bay → center. Same 5 mm exactMm move as columns.
    /// </summary>
    private static void SnapPileToGridWholeMm(Document doc, FamilyInstance pile, List<GridSeg> grids) =>
        SnapPointFamilyToGrid(doc, pile, grids, MeasurePile, "pile");

    private static void SnapPointFamilyToGrid(
        Document doc,
        FamilyInstance instance,
        List<GridSeg> grids,
        Func<FamilyInstance, GridSeg, ColumnOffset> measure,
        string tag)
    {
        var snapGrids = PointSnapGrids(instance, grids);
        if (snapGrids.Count == 0)
            return;
        var before = snapGrids.Select(g => measure(instance, g)).ToList();
        var after = before;
        for (var pass = 0; pass < 2; pass++)
        {
            var move = WholeMmMove(after);
            var moveMm = UnitConv.ToMm(move.GetLength());
            if (moveMm < 1e-15)
                break;
            var stationResidual = after.Max(o => Math.Abs(o.Mm - RoundStep(o.Mm)));
            if (!AllowSnapMove(moveMm, stationResidual))
            {
                DSSTech.Revit.DssTechLog.Write(
                    $"axis snap {tag}={instance.Id.Value} skip moveMm={moveMm:0.000} stationResidual={stationResidual:0.000}");
                break;
            }

            // Release before the first nudge — MovesWithGrids can amplify a ≤2.5 mm snap into a grid jump.
            ReleaseMovesWithGrids(instance);
            try
            {
                ElementTransformUtils.MoveElement(doc, instance.Id, move);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                DSSTech.Revit.DssTechLog.Write($"axis snap {tag}={instance.Id.Value} move failed: {ex.Message}");
                break;
            }

            doc.Regenerate();
            after = snapGrids.Select(g => measure(instance, g)).ToList();
            if (after.All(o => OnStep(o.Mm)))
                break;
        }

        for (var i = 0; i < before.Count; i++)
            DSSTech.Revit.DssTechLog.Write(
                $"axis snap {tag}={instance.Id.Value} grid={before[i].Grid.Grid.Name} anchor={after[i].Anchor} " +
                $"before={before[i].Mm:0.00000000} after={after[i].Mm:0.00000000}");
    }

    private static List<GridSeg> PointSnapGrids(FamilyInstance instance, List<GridSeg> grids)
    {
        if (instance.Location is not LocationPoint lp)
            return new List<GridSeg>();
        var center = lp.Point;
        var faces = VerticalFaces(instance);
        var candidates = new List<(GridSeg Grid, bool Aligned, double AbsMm)>();
        foreach (var axis in DistinctAxes(grids))
        {
            var nearest = NearestGrid(center, axis.Normal, grids, SnapSearchMm);
            if (nearest is null)
                continue;
            var aligned = faces.Any(f => Math.Abs(f.FaceNormal.DotProduct(nearest.Normal)) >= ParallelCos)
                || HalfPileRadiusInternal(instance) is > 0;
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

    /// <summary>
    /// Pile offset from LocationPoint ± type radius (D400 → 200). Avoids cylinder solid faces.
    /// </summary>
    private static ColumnOffset MeasurePile(FamilyInstance pile, GridSeg grid)
    {
        var center = ((LocationPoint)pile.Location).Point;
        var midOff = (center - grid.Origin).DotProduct(grid.Normal);
        if (HalfPileRadiusInternal(pile) is double half && half > 0)
        {
            var midMm = UnitConv.ToMm(midOff);
            if (Math.Abs(midMm) > ExactTolMm)
            {
                var faceOff = midOff - Math.Sign(midMm) * half;
                if (Math.Abs(UnitConv.ToMm(faceOff)) <= NearFaceSnapMm)
                    return new ColumnOffset(grid, "face", faceOff);
            }
        }

        return new ColumnOffset(grid, "center", midOff);
    }

    /// <summary>Half diameter from type Diameter / D / b / Width, or parse D400 from type name.</summary>
    private static double? HalfPileRadiusInternal(FamilyInstance pile)
    {
        if (pile.Symbol is not { } symbol)
            return null;
        foreach (var name in PileDiameterParamNames)
        {
            var p = symbol.LookupParameter(name);
            if (p is { StorageType: StorageType.Double } && p.AsDouble() > 0)
                return p.AsDouble() / 2;
        }

        var width = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
        if (width is { StorageType: StorageType.Double } && width.AsDouble() > 0)
            return width.AsDouble() / 2;

        var typeName = symbol.Name ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(typeName, @"[Dd]\s*(\d{2,4})\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var diaMm) && diaMm > 0)
            return UnitConv.MmToInternal(diaMm) / 2;
        return null;
    }

    private static readonly string[] PileDiameterParamNames =
    {
        "Diameter", "diameter", "D", "b", "B", "Width", "width", "Đường kính", "Dia"
    };

    /// <summary>
    /// Move that puts every offset on a 5 mm step via exact MmToInternal(Round5(mm)) − current.
    /// Tiny float residuals are always applied (no WholeMmTol short-circuit).
    /// </summary>
    private static XYZ WholeMmMove(List<ColumnOffset> offsets)
    {
        var d = offsets
            .Select(o =>
            {
                var targetInternal = UnitConv.MmToInternal(RoundStep(o.Mm));
                return targetInternal - o.SignedInternal;
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

    private static void SnapWallToGridWholeMm(Document doc, Wall wall, List<GridSeg> grids, bool allowRebuild)
    {
        if (wall.Location is not LocationCurve { Curve: Line })
            return;
        SnapLocationCurveToGrid(doc, wall, grids, "wall", allowRebuild);
    }

    private static void SnapBeamToGridWholeMm(Document doc, FamilyInstance beam, List<GridSeg> grids, bool allowRebuild)
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
        SnapLocationCurveToGrid(doc, beam, grids, "beam", allowRebuild);
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

    private readonly record struct CurveAnchor(Line Line, GridSeg Grid, XYZ Anchor, double AnchorOff, double MidOff);

    /// <summary>
    /// Nearest parallel grid to the location-line mid and the anchor snapped to it: the face nearer the grid
    /// at location line ± b/2 (type width / wall width), else the location line. Solid faces from get_Geometry
    /// are not used — they read clean while the beam's Left/Right references stayed 0.2386 mm off.
    /// </summary>
    private static CurveAnchor? MeasureCurveAnchor(Element element, List<GridSeg> grids)
    {
        if (element.Location is not LocationCurve { Curve: Line line })
            return null;
        var mid = line.Evaluate(0.5, true);
        var dir = line.Direction.Normalize();
        var normal = new XYZ(-dir.Y, dir.X, 0).Normalize();
        var nearest = NearestGrid(mid, normal, grids, SnapSearchMm);
        if (nearest is null)
            return null;
        var midOff = (mid - nearest.Origin).DotProduct(nearest.Normal);
        var anchorOff = midOff;
        if (HalfWidthInternal(element) is double half && half > 0)
        {
            var face = midOff - Math.Sign(midOff) * half;
            if (Math.Abs(UnitConv.ToMm(face)) <= NearFaceSnapMm)
                anchorOff = face;
        }

        return new CurveAnchor(line, nearest, mid + nearest.Normal * (anchorOff - midOff), anchorOff, midOff);
    }

    /// <summary>Half of the beam type width b / wall width (internal units).</summary>
    private static double? HalfWidthInternal(Element element)
    {
        if (element is Wall wall)
            return wall.Width / 2;
        if (element is not FamilyInstance { Symbol: { } symbol })
            return null;
        var p = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
        if (p is not { StorageType: StorageType.Double } || p.AsDouble() <= 0)
            p = BeamWidthParamNames.Select(symbol.LookupParameter)
                .FirstOrDefault(x => x is { StorageType: StorageType.Double } && x.AsDouble() > 0);
        return p is null ? null : p.AsDouble() / 2;
    }

    private static readonly string[] BeamWidthParamNames = { "b", "B", "Width", "width", "W", "Rộng", "Chiều rộng" };

    /// <summary>
    /// Target location-line offset is fixed once from the location line (+ b/2); every check reads only the
    /// location curve, so there is no face re-query to lag behind a move.
    /// </summary>
    private static void SnapLocationCurveToGrid(Document doc, Element element, List<GridSeg> grids, string kind, bool allowRebuild)
    {
        if (MeasureCurveAnchor(element, grids) is not CurveAnchor m)
            return;
        var beforeMm = UnitConv.ToMm(m.AnchorOff);
        var stationResidual = Math.Abs(beforeMm - RoundStep(beforeMm));
        if (stationResidual <= ExactTolMm)
            return;
        var targetMid = m.MidOff + (UnitConv.MmToInternal(RoundStep(beforeMm)) - m.AnchorOff);

        // Rebuild (parallel + 5 mm ends) only for newly created, visibly off-step elements.
        // Clean geometry is never rebuilt: end-rounding re-introduced .761 CAD residue before.
        if (allowRebuild && stationResidual > GeomOkMm &&
            TryRebuildLocationCurveOnGrids(element, m.Line, m.Anchor, m.Grid, grids, kind))
            doc.Regenerate();

        for (var pass = 0; pass < 2; pass++)
        {
            if (element.Location is not LocationCurve { Curve: Line now })
                return;
            var d = targetMid - (now.Evaluate(0.5, true) - m.Grid.Origin).DotProduct(m.Grid.Normal);
            var moveMm = Math.Abs(UnitConv.ToMm(d));
            DSSTech.Revit.DssTechLog.Write(
                $"axis snap {kind}={element.Id.Value} grid={m.Grid.Grid.Name} " +
                $"before={beforeMm:0.00000000} moveMm={UnitConv.ToMm(d):0.00000000} pass={pass}");
            if (moveMm <= ExactTolMm)
                return;
            if (!AllowSnapMove(moveMm, stationResidual) || (!allowRebuild && moveMm > HalfStepMm + TinyNudgeMm))
            {
                DSSTech.Revit.DssTechLog.Write(
                    $"axis snap {kind}={element.Id.Value} skip moveMm={moveMm:0.000} stationResidual={stationResidual:0.000}");
                return;
            }

            try
            {
                // Revit drops sub-tolerance moves (1e-7 mm stayed 249.9999999): go out 1 mm and back instead.
                if (moveMm < GeomOkMm)
                {
                    var detour = UnitConv.MmToInternal(1);
                    ElementTransformUtils.MoveElement(doc, element.Id, m.Grid.Normal * (d + detour));
                    ElementTransformUtils.MoveElement(doc, element.Id, m.Grid.Normal * -detour);
                }
                else
                {
                    ElementTransformUtils.MoveElement(doc, element.Id, m.Grid.Normal * d);
                }

                doc.Regenerate();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                DSSTech.Revit.DssTechLog.Write($"axis snap {kind}={element.Id.Value} move failed: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>
    /// Allow snap when residual to nearest 5 mm station ≤ half-step (always for nearest).
    /// Refuse only when station residual &gt; 3 mm or move exceeds safety cap.
    /// </summary>
    private static bool AllowSnapMove(double moveMm, double stationResidualMm)
    {
        if (stationResidualMm <= HalfStepMm || moveMm <= TinyNudgeMm)
            return moveMm <= SafetyMaxMoveMm;
        if (stationResidualMm > 3.0)
            return false;
        return moveMm <= SafetyMaxMoveMm;
    }

    /// <summary>
    /// Rotate-aware wall snap: make the location line exactly parallel to the grid, put the face/center
    /// offset on a 5 mm step, and put both ends on 5 mm steps from the nearest perpendicular grid (else along
    /// the parallel grid). Skipped when any end would move more than <see cref="SafetyMaxMoveMm"/>.
    /// </summary>
    private static bool TryRebuildLocationCurveOnGrids(
        Element element, Line line, XYZ anchor, GridSeg parallel, List<GridSeg> grids, string kind)
    {
        var p0 = line.GetEndPoint(0);
        var p1 = line.GetEndPoint(1);
        var n = parallel.Normal;
        var u = parallel.Direction.DotProduct(line.Direction) >= 0 ? parallel.Direction : parallel.Direction.Negate();
        var anchorOff = (anchor - parallel.Origin).DotProduct(n);
        var targetAcross = UnitConv.MmToInternal(RoundStep(UnitConv.ToMm(anchorOff)));
        var mid = line.Evaluate(0.5, true);
        // Keep mid→face offset: shift centerline so the measured anchor lands on exact targetInternal.
        var midOff = (mid - parallel.Origin).DotProduct(n);
        var across = midOff + (targetAcross - anchorOff);

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
        var acrossResidual = Math.Abs(UnitConv.ToMm(anchorOff) - RoundStep(UnitConv.ToMm(anchorOff)));
        DSSTech.Revit.DssTechLog.Write(
            $"axis snap {kind}={element.Id.Value} grid={parallel.Grid.Name} perp={perp?.Grid.Name ?? "-"} " +
            $"across={UnitConv.ToMm(anchorOff):0.00000000} ends=({s0:0.###},{s1:0.###}) moveMm=({move0:0.000},{move1:0.000})");
        // Prefer across snap: allow end moves up to safety when across needs a ≤half-step fix.
        if (move0 > SafetyMaxMoveMm || move1 > SafetyMaxMoveMm)
            return false;
        if (acrossResidual > HalfStepMm && (move0 > HalfStepMm || move1 > HalfStepMm) &&
            !AllowSnapMove(Math.Max(move0, move1), acrossResidual))
            return false;
        if (move0 < 1e-9 && move1 < 1e-9)
            return acrossResidual <= GeomOkMm;
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

    /// <summary>Both ends on 5 mm stations from the perpendicular grid, so the length is a 5 mm multiple too.</summary>
    private static (double Start, double End) WholeMmSpan(double s0, double s1) => (RoundStep(s0), RoundStep(s1));

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
            // Only auto-dim when geometry is already on a 5 mm station (after snap).
            if (!OnStep(offsetMm, TinyNudgeMm))
                continue;
            if (TryNewDimension(doc, view, nearest.Grid, faceToward.Reference, facePt, faceToward.FaceNormal))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Pile dims: prefer planar faces when present; else dim grid → instance strong reference at
    /// the snapped rim/center (type diameter), same 5 mm gate as columns.
    /// </summary>
    private static int DimensionPile(Document doc, View view, FamilyInstance pile, List<GridSeg> grids)
    {
        var faces = VerticalFaces(pile);
        if (faces.Count > 0)
            return DimensionColumn(doc, view, pile, grids);

        if (pile.Location is not LocationPoint lp)
            return 0;
        var center = lp.Point;
        var count = 0;
        foreach (var axis in DistinctAxes(grids))
        {
            var nearest = NearestGrid(center, axis.Normal, grids, MaxDimOffsetMm);
            if (nearest is null)
                continue;
            var measured = MeasurePile(pile, nearest);
            var offsetMm = Math.Abs(measured.Mm);
            if (offsetMm < 1 || offsetMm > MaxDimOffsetMm || !OnStep(offsetMm, TinyNudgeMm))
                continue;
            var facePt = center + nearest.Normal * (measured.SignedInternal
                - (center - nearest.Origin).DotProduct(nearest.Normal));
            if (!TryPileReference(pile, out var pileRef))
                continue;
            if (TryNewDimension(doc, view, nearest.Grid, pileRef, facePt, nearest.Normal))
                count++;
        }

        return count;
    }

    private static bool TryPileReference(FamilyInstance pile, out Reference reference)
    {
        reference = null!;
        try
        {
            foreach (var kind in new[]
                     {
                         FamilyInstanceReferenceType.CenterLeftRight,
                         FamilyInstanceReferenceType.CenterFrontBack,
                         FamilyInstanceReferenceType.StrongReference
                     })
            {
                var refs = pile.GetReferences(kind);
                if (refs is null || refs.Count == 0)
                    continue;
                reference = refs[0];
                return true;
            }
        }
        catch
        {
            // Family has no strong refs — skip auto-dim (snap still applied).
        }

        return false;
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
        var nearest = NearestGrid(mid, normal, grids, MaxDimOffsetMm);
        if (nearest is null)
            return 0;
        // Same face the snap anchored: the far face carries width noise (e.g. 699.99999998 on rotated beams).
        var face = FaceTowardGrid(VerticalFaces(element), mid, nearest, MaxDimOffsetMm);
        if (face?.Reference is null)
            return 0;
        var facePt = FacePoint(face, mid);
        var offsetMm = Math.Abs(UnitConv.ToMm((facePt - nearest.Origin).DotProduct(nearest.Normal)));
        if (offsetMm < 1 || offsetMm > MaxDimOffsetMm)
            return 0;
        if (!OnStep(offsetMm, TinyNudgeMm))
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
            doc.Create.NewDimension(view, dimLine, refs);
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

    /// <summary>
    /// Diagnostic: linear dims left off the 5 mm step in the view and what they reference. A dim to an
    /// ImportInstance (CAD link line under the beam) keeps the CAD residue whatever the snap does.
    /// </summary>
    private static void LogOffStepDimensions(Document doc, View view)
    {
        var logged = 0;
        foreach (var dim in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (logged >= 40)
                break;
            try
            {
                if (dim.Value is not double feet)
                    continue;
                var mm = Math.Abs(UnitConv.ToMm(feet));
                if (Math.Abs(mm - RoundStep(mm)) <= DimDisplayTolMm)
                    continue;
                var refs = new List<string>();
                for (var i = 0; i < dim.References.Size; i++)
                {
                    var el = doc.GetElement(dim.References.get_Item(i));
                    refs.Add(el is null ? "?" : $"{el.GetType().Name}:{el.Category?.Name}:{el.Id.Value}");
                }

                DSSTech.Revit.DssTechLog.Write(
                    $"axis dim offstep id={dim.Id.Value} mm={mm:0.00000000} refs={string.Join(" | ", refs)}");
                logged++;
            }
            catch
            {
                // Dims we cannot inspect.
            }
        }
    }

    /// <summary>
    /// Refuse auto dims off the 5 mm step. Values are read after one regenerate: right after NewDimension
    /// Value can still be the 1 ft placeholder (304.8 mm). Dims referencing the batch were deleted before
    /// creation, so every dim found here is one of ours.
    /// </summary>
    private static int DeleteOffStepDimensions(Document doc, View view, List<ElementId> elementIds)
    {
        doc.Regenerate();
        var set = new HashSet<ElementId>(elementIds);
        var noisy = new List<ElementId>();
        foreach (var dim in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            try
            {
                if (dim.Value is not double feet)
                    continue;
                var ours = false;
                for (var i = 0; i < dim.References.Size && !ours; i++)
                    ours = doc.GetElement(dim.References.get_Item(i)) is { } el && set.Contains(el.Id);
                if (!ours)
                    continue;
                var mm = Math.Abs(UnitConv.ToMm(feet));
                if (Math.Abs(mm - RoundStep(mm)) <= DimDisplayTolMm)
                    continue;
                DSSTech.Revit.DssTechLog.Write($"axis dim delete noisy mm={mm:0.0000000000}");
                noisy.Add(dim.Id);
            }
            catch
            {
                // Dims we cannot inspect.
            }
        }

        if (noisy.Count > 0)
            doc.Delete(noisy);
        return noisy.Count;
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
