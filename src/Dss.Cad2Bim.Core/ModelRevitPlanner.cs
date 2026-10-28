using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dss.McpContracts;

namespace Dss.Cad2Bim.Core;

public enum ModelRevitPartKind
{
    Column,
    StructuralWall,
    Beam,
    Floor,
    FloorOpening,
    IsolatedFoundation,
    Pile,
    StripFoundation,
    Door,
    Window,
    Room,
    Ceiling,
    Pipe,
    Duct,
    CableTray,
    Equipment
}

/// <summary>
/// Level, offset and family choices collected before create. Sizes still come from the picked annotation.
/// </summary>
public sealed class ModelRevitPlacement
{
    public string BaseLevelName { get; set; } = "";
    public string TopLevelName { get; set; } = "";
    public double BaseOffsetMm { get; set; }
    public double TopOffsetMm { get; set; }
    public string? FamilyName { get; set; }
    public string? TypeName { get; set; }
    public string? WidthParam { get; set; }
    public string? HeightParam { get; set; }
    public string? Material { get; set; }
    public bool AssignMark { get; set; }
    public string? SystemName { get; set; }
    public string? SectionShape { get; set; }
    public string? Function { get; set; }
    /// <summary>Beam supports already in the model (columns, structural walls) as plan rectangles in CAD/model mm.</summary>
    public IReadOnlyList<SectionFootprint>? Supports { get; set; }
    public Action<string>? Log { get; set; }
}

/// <summary>
/// Plans create ops from user-picked CAD layer (exact name) + annotation sizes + view levels.
/// </summary>
public static class ModelRevitPlanner
{
    public static IReadOnlyList<CreateOperation> Plan(
        CadGeometry geom,
        string layerName,
        ModelRevitPartKind part,
        string baseLevelName,
        string topLevelName,
        AnnotationSize size,
        string? equipmentFamilyType = null,
        IReadOnlyList<CadPoint2>? gridPoints = null,
        string? columnFamily = null,
        ModelRevitPlacement? placement = null,
        IReadOnlyList<double>? gridAnglesDeg = null)
    {
        var place = placement ?? new ModelRevitPlacement();
        var baseLevel = string.IsNullOrWhiteSpace(place.BaseLevelName) ? baseLevelName : place.BaseLevelName;
        var topLevel = string.IsNullOrWhiteSpace(place.TopLevelName) ? topLevelName : place.TopLevelName;
        var family = string.IsNullOrWhiteSpace(place.FamilyName) ? columnFamily : place.FamilyName;
        var entities = geom.Entities
            .Where(e => string.Equals(e.Layer, layerName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entities.Count == 0)
            return Array.Empty<CreateOperation>();

        return part switch
        {
            ModelRevitPartKind.Column => PlanColumns(geom, entities, baseLevel, topLevel, size, gridPoints, family, place, gridAnglesDeg),
            ModelRevitPartKind.StructuralWall => PlanWallsSharedLayer(
                geom, entities, baseLevel, topLevel, size, gridPoints, family, place, gridAnglesDeg),
            // Beams sit on the user-picked level (UI "Level" / TopLevelName). Fall back to base when blank.
            ModelRevitPartKind.Beam => PlanBeams(
                geom, entities,
                string.IsNullOrWhiteSpace(topLevel) ? baseLevel : topLevel,
                size, place, gridPoints, gridAnglesDeg),
            ModelRevitPartKind.Floor => PlanFloors(entities, baseLevel, size, structural: true, place),
            ModelRevitPartKind.FloorOpening => Array.Empty<CreateOperation>(),
            ModelRevitPartKind.IsolatedFoundation => PlanFoundations(entities, baseLevel, size, "Isolated", place),
            ModelRevitPartKind.Pile => PlanFoundations(entities, baseLevel, size, "Pile", place),
            ModelRevitPartKind.StripFoundation => PlanStripFoundations(entities, baseLevel, size, place),
            ModelRevitPartKind.Door => PlanInserts(geom, entities, baseLevel, "Doors", FirstNonEmpty(place.FamilyName, place.TypeName, equipmentFamilyType, size.Raw), size, place),
            ModelRevitPartKind.Window => PlanInserts(geom, entities, baseLevel, "Windows", FirstNonEmpty(place.FamilyName, place.TypeName, equipmentFamilyType, size.Raw), size, place),
            ModelRevitPartKind.Room => PlanRooms(entities, baseLevel, size.Raw),
            ModelRevitPartKind.Ceiling => PlanCeilings(entities, baseLevel, size, place),
            ModelRevitPartKind.Pipe => PlanMepLines(entities, baseLevel, "Pipes", size, place),
            ModelRevitPartKind.Duct => PlanMepLines(entities, baseLevel, "Ducts", size, place),
            ModelRevitPartKind.CableTray => PlanMepLines(entities, baseLevel, "CableTrays", size, place),
            ModelRevitPartKind.Equipment => PlanInserts(geom, entities, baseLevel, "MechanicalEquipment", FirstNonEmpty(place.FamilyName, equipmentFamilyType, size.Raw), size, place),
            _ => Array.Empty<CreateOperation>()
        };
    }

    /// <summary>
    /// Shared CAD column/wall layer: walls when L/b &gt; 4; optional columns when L/b ≤ 4 and a column family is chosen.
    /// </summary>
    private static IReadOnlyList<CreateOperation> PlanWallsSharedLayer(
        CadGeometry geom,
        List<CadEntity> entities,
        string baseLevel,
        string topLevel,
        AnnotationSize size,
        IReadOnlyList<CadPoint2>? gridPoints,
        string? columnFamily,
        ModelRevitPlacement place,
        IReadOnlyList<double>? gridAnglesDeg)
    {
        var ops = new List<CreateOperation>();
        var runs = WallCenterlineReader.FromEntities(entities);
        ops.AddRange(PlanWalls(runs, baseLevel, topLevel, size, structural: true, place, gridPoints, gridAnglesDeg));
        if (!string.IsNullOrWhiteSpace(columnFamily) || !string.IsNullOrWhiteSpace(place.FamilyName))
            ops.AddRange(PlanColumns(geom, entities, baseLevel, topLevel, size, gridPoints, columnFamily, place, gridAnglesDeg, runs));
        return ops;
    }

    private static IReadOnlyList<CreateOperation> PlanColumns(
        CadGeometry geom,
        List<CadEntity> entities,
        string baseLevel,
        string topLevel,
        AnnotationSize size,
        IReadOnlyList<CadPoint2>? gridPoints,
        string? columnFamily,
        ModelRevitPlacement place,
        IReadOnlyList<double>? gridAnglesDeg,
        IReadOnlyList<WallRun>? walls = null)
    {
        var ops = new List<CreateOperation>();
        foreach (var footprint in SectionFootprintReader.FromEntities(entities))
        {
            if (walls is not null && InsideWalls(footprint, walls))
                continue;
            var rotation = SnapToGridAngle(footprint.RotationDeg, gridAnglesDeg);
            var (x, y) = RoundColumnPosition(footprint.CenterX, footprint.CenterY, rotation);
            var (gx, gy, snapped) = SnapToGrid(x, y, gridPoints);
            if (snapped && OnRoundModule(gx, gy, rotation))
            {
                x = gx;
                y = gy;
            }
            ColumnSection(footprint, size, place.SectionShape, out var w, out var d);
            // Whole millimetres only — avoids face-to-grid dims like 250.06244451 from float centres.
            // Rotated columns keep whole mm along their own axes instead (world rounding breaks that).
            var orthogonal = IsOrthogonal(rotation);
            if (orthogonal)
            {
                x = Math.Round(x, MidpointRounding.AwayFromZero);
                y = Math.Round(y, MidpointRounding.AwayFromZero);
            }
            var typeHint = $"{w}x{d}";
            var mark = place.AssignMark ? NearestMark(geom, x, y) : "";
            ops.Add(Op("StructuralColumns", new
            {
                xyzMm = new[] { x, y, 0 },
                familyType = typeHint,
                widthMm = w,
                depthMm = d,
                rotationDeg = rotation,
                baseLevel,
                topLevel,
                baseOffsetMm = place.BaseOffsetMm,
                topOffsetMm = place.TopOffsetMm,
                sourceFamily = columnFamily ?? "",
                widthParam = place.WidthParam ?? "",
                heightParam = place.HeightParam ?? "",
                material = place.Material ?? "",
                sectionShape = place.SectionShape ?? "",
                mark,
                rotatedFrame = !orthogonal
            }, skipIfExists: true));
        }

        return ops;
    }

    private static void ColumnSection(SectionFootprint footprint, AnnotationSize size, string? shape, out int width, out int depth)
    {
        var circle = string.Equals(shape, "Circle", StringComparison.OrdinalIgnoreCase);
        var rectangle = string.Equals(shape, "Rectangle", StringComparison.OrdinalIgnoreCase);
        if (circle && size.DiameterMm is double forcedDia && forcedDia > 0)
        {
            width = depth = (int)Math.Round(forcedDia);
            return;
        }

        if (circle)
        {
            var side = Math.Max(1, (int)Math.Round(Math.Min(footprint.WidthMm, footprint.DepthMm)));
            if (size.WidthMm is double sideText && sideText > 0)
                side = (int)Math.Round(sideText);
            width = depth = side;
            return;
        }

        if (!rectangle && size.DiameterMm is double dia && dia > 0)
        {
            width = depth = (int)Math.Round(dia);
            return;
        }

        if (size.WidthMm is double w && size.HeightMm is double h && w > 0 && h > 0)
        {
            width = (int)Math.Round(w);
            depth = (int)Math.Round(h);
            return;
        }

        width = Math.Max(1, (int)Math.Round(footprint.WidthMm));
        depth = Math.Max(1, (int)Math.Round(footprint.DepthMm));
    }

    private static string NearestMark(CadGeometry geom, double x, double y)
    {
        string best = "";
        var bestD = 2000.0 * 2000.0;
        foreach (var text in geom.Entities.OfType<CadText>())
        {
            if (!CadMark.IsLabel(text.Value))
                continue;
            var dx = text.X - x;
            var dy = text.Y - y;
            var distance = dx * dx + dy * dy;
            if (distance >= bestD)
                continue;
            bestD = distance;
            best = text.Value.Trim();
        }

        return best;
    }

    private const double GridAngleSnapDeg = 8.0;

    /// <summary>
    /// Pull an outline angle onto a grid direction when they already nearly match.
    /// 0° and 180° are the same column axis.
    /// </summary>
    private static double SnapToGridAngle(double rotationDeg, IReadOnlyList<double>? gridAnglesDeg)
    {
        if (gridAnglesDeg is null || gridAnglesDeg.Count == 0)
            return rotationDeg;
        var best = rotationDeg;
        var bestDelta = GridAngleSnapDeg;
        foreach (var angle in gridAnglesDeg)
        {
            var delta = AngleDelta(rotationDeg, angle);
            if (delta >= bestDelta)
                continue;
            bestDelta = delta;
            best = NormHalfTurn(angle);
        }

        return best;
    }

    private static double AngleDelta(double a, double b)
    {
        var d = Math.Abs(a - b) % 180.0;
        if (d > 90.0)
            d = 180.0 - d;
        return d;
    }

    private static double NormHalfTurn(double degrees)
    {
        while (degrees <= -90.0)
            degrees += 180.0;
        while (degrees > 90.0)
            degrees -= 180.0;
        return degrees;
    }

    /// <summary>Drop only the fractional millimetre. 2175.4 becomes 2175.</summary>
    public const double PositionRoundMm = 1.0;

    /// <summary>
    /// Round a column center to the nearest millimetre along its own axes.
    /// </summary>
    public static (double X, double Y) RoundColumnPosition(double x, double y, double rotationDeg)
    {
        var rad = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var along = RoundStep(x * cos + y * sin, PositionRoundMm);
        var across = RoundStep(-x * sin + y * cos, PositionRoundMm);
        return (along * cos - across * sin, along * sin + across * cos);
    }

    private static bool OnRoundModule(double x, double y, double rotationDeg)
    {
        var (rx, ry) = RoundColumnPosition(x, y, rotationDeg);
        var dx = rx - x;
        var dy = ry - y;
        return dx * dx + dy * dy <= 1.0;
    }

    private static double RoundStep(double value, double step) =>
        Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

    private static bool IsOrthogonal(double rotationDeg) =>
        AngleDelta(rotationDeg, 0) < 1e-6 || AngleDelta(rotationDeg, 90) < 1e-6;

    /// <summary>Footprint lies in a wall body (wall stub on a shared layer) — the wall already models it.</summary>
    private static bool InsideWalls(SectionFootprint footprint, IReadOnlyList<WallRun> walls)
    {
        var rad = footprint.RotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        for (var i = 0; i < 4; i++)
        {
            var along = (i is 0 or 3 ? -0.5 : 0.5) * footprint.WidthMm;
            var across = (i < 2 ? -0.5 : 0.5) * footprint.DepthMm;
            var px = footprint.CenterX + along * cos - across * sin;
            var py = footprint.CenterY + along * sin + across * cos;
            if (!walls.Any(w => InsideWallBody(w, px, py)))
                return false;
        }

        return true;
    }

    private static bool InsideWallBody(WallRun wall, double x, double y)
    {
        const double tolMm = 5;
        var dx = wall.X1 - wall.X0;
        var dy = wall.Y1 - wall.Y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
            return false;
        var along = ((x - wall.X0) * dx + (y - wall.Y0) * dy) / len;
        var across = Math.Abs((x - wall.X0) * -dy + (y - wall.Y0) * dx) / len;
        // Ends sit on crossing centerlines; the joined corner covers half a thickness past them.
        var half = wall.ThicknessMm / 2 + tolMm;
        return across <= half && along >= -half && along <= len + half;
    }

    private const double WallAngleSnapDeg = 3.0;
    private const double WallMaxMoveMm = 2.0;

    /// <summary>
    /// Wall direction: a grid or world axis direction (parallel or perpendicular) when it is within 3°
    /// and turning onto it moves the ends at most 2 mm; otherwise the CAD direction.
    /// </summary>
    private static double WallFrameAngle(double angleDeg, double lengthMm, IReadOnlyList<double>? gridAnglesDeg)
    {
        var best = angleDeg;
        var bestDelta = WallAngleSnapDeg;
        foreach (var grid in (gridAnglesDeg ?? Array.Empty<double>()).Append(0.0))
        {
            foreach (var candidate in new[] { grid, grid + 90.0 })
            {
                var delta = AngleDelta(angleDeg, candidate);
                if (delta >= bestDelta || lengthMm / 2 * Math.Sin(delta * Math.PI / 180.0) > WallMaxMoveMm)
                    continue;
                bestDelta = delta;
                best = candidate;
            }
        }

        return best;
    }

    private static (double X, double Y) NearestGridPoint(double x, double y, IReadOnlyList<CadPoint2>? gridPoints)
    {
        if (gridPoints is null || gridPoints.Count == 0)
            return (0, 0);
        var best = gridPoints[0];
        var bestD = double.MaxValue;
        foreach (var p in gridPoints)
        {
            var d = (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
            if (d >= bestD)
                continue;
            bestD = d;
            best = p;
        }

        return (best.X, best.Y);
    }

    /// <summary>
    /// Rotated wall: whole-mm length and whole-mm offsets along/across its own axes, measured from the
    /// nearest grid intersection (so face/end offsets to those grids are whole mm). The end nearer a
    /// 5 mm station is kept; the other end follows the rounded length.
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1) RoundWallInFrame(
        double x0, double y0, double x1, double y1, double angleDeg, double originX = 0, double originY = 0)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var ux = Math.Cos(rad);
        var uy = Math.Sin(rad);
        double Along(double x, double y) => (x - originX) * ux + (y - originY) * uy;
        double Across(double x, double y) => -(x - originX) * uy + (y - originY) * ux;
        var a0 = Along(x0, y0);
        var a1 = Along(x1, y1);
        var across = RoundStep((Across(x0, y0) + Across(x1, y1)) / 2, PositionRoundMm);
        var len = RoundStep(a1 - a0, PositionRoundMm);
        if (Off5(a1) < Off5(a0))
        {
            a1 = RoundStep(a1, PositionRoundMm);
            a0 = a1 - len;
        }
        else
        {
            a0 = RoundStep(a0, PositionRoundMm);
            a1 = a0 + len;
        }

        return (
            originX + a0 * ux - across * uy, originY + a0 * uy + across * ux,
            originX + a1 * ux - across * uy, originY + a1 * uy + across * ux);
    }

    private static double Off5(double value) => Math.Abs(value - RoundStep(value, 5));

    private const double GridSnapMaxMm = 10.0;

    private static (double X, double Y, bool Snapped) SnapToGrid(double x, double y, IReadOnlyList<CadPoint2>? gridPoints)
    {
        if (gridPoints == null || gridPoints.Count == 0)
            return (x, y, false);

        var best = GridSnapMaxMm;
        var sx = x;
        var sy = y;
        var snapped = false;
        for (var i = 0; i < gridPoints.Count; i++)
        {
            var dx = gridPoints[i].X - x;
            var dy = gridPoints[i].Y - y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > best)
                continue;
            best = distance;
            sx = gridPoints[i].X;
            sy = gridPoints[i].Y;
            snapped = true;
        }

        return (sx, sy, snapped);
    }

    private static IReadOnlyList<CreateOperation> PlanWalls(
        List<WallRun> runs,
        string baseLevel,
        string topLevel,
        AnnotationSize size,
        bool structural,
        ModelRevitPlacement place,
        IReadOnlyList<CadPoint2>? gridPoints,
        IReadOnlyList<double>? gridAnglesDeg)
    {
        var sourceType = place.TypeName ?? "";
        var forced = size.WidthMm is > 0 ? (int)Math.Round(size.WidthMm.Value) : 0;
        if (runs.Count == 0)
            return Array.Empty<CreateOperation>();

        // Exterior/Interior from envelope applies to architectural walls only.
        // Structural walls default to CoreShaft + Wall Centerline (not Finish Face Exterior/Interior).
        var sides = structural ? null : ClassifyWalls(runs);
        var forcedFunction = place.Function;
        var ops = new List<CreateOperation>();
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var thick = forced > 0 ? forced : (int)Math.Round(run.ThicknessMm);
            string function;
            string locationLine;
            double outwardX = 0, outwardY = 0;
            if (structural)
            {
                function = !string.IsNullOrWhiteSpace(forcedFunction) ? forcedFunction! : "CoreShaft";
                locationLine = "wallcenterline";
            }
            else
            {
                (outwardX, outwardY, var isExterior) = sides![i];
                function = !string.IsNullOrWhiteSpace(forcedFunction)
                    ? forcedFunction!
                    : (isExterior ? "Exterior" : "Interior");
                // Path is the CAD centerline (Wall.Create default). Location Line is applied after the batch,
                // which moves only the location curve, not the wall body.
                locationLine = isExterior ? "exterior" : "interior";
            }

            var dx = run.X1 - run.X0;
            var dy = run.Y1 - run.Y0;
            var angle = WallFrameAngle(Math.Atan2(dy, dx) * 180.0 / Math.PI, Math.Sqrt(dx * dx + dy * dy), gridAnglesDeg);
            var rotated = !IsOrthogonal(angle);
            double sx, sy, ex, ey;
            if (rotated)
            {
                var (ox, oy) = NearestGridPoint((run.X0 + run.X1) / 2, (run.Y0 + run.Y1) / 2, gridPoints);
                (sx, sy, ex, ey) = RoundWallInFrame(run.X0, run.Y0, run.X1, run.Y1, angle, ox, oy);
            }
            else
            {
                (sx, sy, ex, ey) = RoundWallInFrame(run.X0, run.Y0, run.X1, run.Y1, angle);
                sx = Math.Round(sx, MidpointRounding.AwayFromZero);
                sy = Math.Round(sy, MidpointRounding.AwayFromZero);
                ex = Math.Round(ex, MidpointRounding.AwayFromZero);
                ey = Math.Round(ey, MidpointRounding.AwayFromZero);
            }

            ops.Add(Op("Walls", new
            {
                startMm = new[] { sx, sy, 0 },
                endMm = new[] { ex, ey, 0 },
                wallType = sourceType,
                sourceType,
                thicknessMm = thick,
                level = baseLevel,
                topLevel,
                baseOffsetMm = place.BaseOffsetMm,
                topOffsetMm = place.TopOffsetMm,
                material = place.Material ?? "",
                function,
                locationLine,
                outwardMm = new[] { outwardX, outwardY },
                structural,
                rotatedFrame = rotated
            }, skipIfExists: true));
        }

        return ops;
    }

    /// <summary>
    /// Architectural envelope helper: Exterior = a face lies on the convex hull of all wall faces
    /// (rotated plans OK; cores/shafts inside the envelope stay Interior). Outward = that hull face,
    /// else the face farther from the wall-set centroid. Not used for StructuralWall / CoreShaft.
    /// </summary>
    internal static (double OutX, double OutY, bool Exterior)[] ClassifyWalls(IReadOnlyList<WallRun> runs)
    {
        var pts = new List<(double X, double Y)>();
        double cx = 0, cy = 0;
        foreach (var run in runs)
        {
            run.Face0(out var ax0, out var ay0, out var ax1, out var ay1);
            run.Face1(out var bx0, out var by0, out var bx1, out var by1);
            pts.Add((ax0, ay0));
            pts.Add((ax1, ay1));
            pts.Add((bx0, by0));
            pts.Add((bx1, by1));
            cx += (run.X0 + run.X1) * 0.5;
            cy += (run.Y0 + run.Y1) * 0.5;
        }

        cx /= Math.Max(runs.Count, 1);
        cy /= Math.Max(runs.Count, 1);
        var hull = ConvexHull(pts);
        var result = new (double, double, bool)[runs.Count];
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            run.Face0(out var ax0, out var ay0, out var ax1, out var ay1);
            run.Face1(out var bx0, out var by0, out var bx1, out var by1);
            var m0 = ((ax0 + ax1) * 0.5, (ay0 + ay1) * 0.5);
            var m1 = ((bx0 + bx1) * 0.5, (by0 + by1) * 0.5);
            var h0 = DistanceToHull(hull, m0);
            var h1 = DistanceToHull(hull, m1);
            var tol = Math.Max(run.ThicknessMm * 1.5, 150);
            var exterior = Math.Min(h0, h1) <= tol;
            bool face1Outer;
            if (exterior && Math.Abs(h0 - h1) > 1)
                face1Outer = h1 < h0;
            else
            {
                var c0 = (m0.Item1 - cx) * (m0.Item1 - cx) + (m0.Item2 - cy) * (m0.Item2 - cy);
                var c1 = (m1.Item1 - cx) * (m1.Item1 - cx) + (m1.Item2 - cy) * (m1.Item2 - cy);
                face1Outer = c1 >= c0;
            }

            result[i] = face1Outer ? (run.Nx, run.Ny, exterior) : (-run.Nx, -run.Ny, exterior);
        }

        return result;
    }

    private static List<(double X, double Y)> ConvexHull(List<(double X, double Y)> pts)
    {
        var sorted = pts.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
            return sorted;
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<(double X, double Y)>();
        for (var pass = 0; pass < 2; pass++)
        {
            var start = hull.Count;
            foreach (var p in sorted)
            {
                while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
            sorted.Reverse();
        }

        return hull;
    }

    private static double DistanceToHull(List<(double X, double Y)> hull, (double X, double Y) p)
    {
        var best = double.MaxValue;
        for (var i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len2 = dx * dx + dy * dy;
            var t = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            var ex = a.X + dx * t - p.X;
            var ey = a.Y + dy * t - p.Y;
            best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
        }

        return best;
    }

    private static IReadOnlyList<CreateOperation> PlanBeams(
        CadGeometry geom,
        List<CadEntity> entities,
        string topLevel,
        AnnotationSize size,
        ModelRevitPlacement place,
        IReadOnlyList<CadPoint2>? gridPoints,
        IReadOnlyList<double>? gridAnglesDeg)
    {
        var texts = BeamTexts(geom);
        var runs = BeamCenterlineReader.FromEntities(entities, run => WidthMismatch(texts, run));
        if (runs.Count == 0)
            return Array.Empty<CreateOperation>();

        // Model columns / structural walls win: CAD outlines on every layer also catch section markers
        // and junction boxes, so they are only the fallback.
        var fromModel = place.Supports is { Count: > 0 };
        var supports = fromModel ? place.Supports! : CadSupports(geom, entities);
        var planned = BeamCenterlineReader.PrepareRuns(runs, supports, entities);
        if (planned.Count == 0)
            return Array.Empty<CreateOperation>();
        var segments = planned.Select(p => p.Run).ToList();
        place.Log?.Invoke(
            $"beam plan runs={runs.Count} supports={supports.Count} ({(fromModel ? "model" : "cad")}) " +
            $"segments={segments.Count} main={planned.Count(p => p.Kind == BeamSpanKind.Main)}");

        var labels = ResolveBeamAxisLabels(texts, segments, out var sources);

        var uiW = size.WidthMm is > 0 ? (int)Math.Round(size.WidthMm.Value) : 0;
        var uiD = size.HeightMm is > 0 ? (int)Math.Round(size.HeightMm.Value) : uiW;
        var sourceFamily = FirstNonEmpty(place.FamilyName, FamilyFromTypeName(place.TypeName));
        var ops = new List<CreateOperation>();
        for (var i = 0; i < segments.Count; i++)
        {
            var run = segments[i];
            var label = labels[i];
            var width = label.WidthMm ?? (uiW > 0 ? uiW : (run.ThicknessMm > 0 ? (int)Math.Round(run.ThicknessMm) : 0));
            var depth = label.DepthMm ?? (uiD > 0 ? uiD : 0);
            if (width <= 0 && depth <= 0 && !string.IsNullOrWhiteSpace(place.TypeName))
            {
                // Type-only fallback: place existing type without inventing a section.
                width = 0;
                depth = 0;
            }
            else if (width <= 0 || depth <= 0)
            {
                if (width <= 0 && run.ThicknessMm > 0)
                    width = (int)Math.Round(run.ThicknessMm);
                if (depth <= 0)
                    depth = width > 0 ? width * 2 : 0;
            }

            var dx = run.X1 - run.X0;
            var dy = run.Y1 - run.Y0;
            var angle = WallFrameAngle(Math.Atan2(dy, dx) * 180.0 / Math.PI, Math.Sqrt(dx * dx + dy * dy), gridAnglesDeg);
            var rotated = !IsOrthogonal(angle);
            double sx, sy, ex, ey;
            if (rotated)
            {
                var (ox, oy) = NearestGridPoint((run.X0 + run.X1) / 2, (run.Y0 + run.Y1) / 2, gridPoints);
                (sx, sy, ex, ey) = RoundWallInFrame(run.X0, run.Y0, run.X1, run.Y1, angle, ox, oy);
            }
            else
            {
                (sx, sy, ex, ey) = RoundWallInFrame(run.X0, run.Y0, run.X1, run.Y1, angle);
                sx = Math.Round(sx, MidpointRounding.AwayFromZero);
                sy = Math.Round(sy, MidpointRounding.AwayFromZero);
                ex = Math.Round(ex, MidpointRounding.AwayFromZero);
                ey = Math.Round(ey, MidpointRounding.AwayFromZero);
            }

            BeamCenterlineReader.Orient(sx, sy, ex, ey, out sx, out sy, out ex, out ey);
            var typeHint = width > 0 && depth > 0
                ? $"{width}x{depth}"
                : FirstNonEmpty(place.TypeName, place.FamilyName, AnnotationSizeParser.BeamFamilyTypeHint(size));
            var mark = label.Mark ?? "";
            place.Log?.Invoke(
                $"beam seg {i} {planned[i].Kind} ({sx:0},{sy:0})-({ex:0},{ey:0}) t={run.ThicknessMm:0} " +
                $"type={typeHint} mark={mark} {sources[i]}");

            ops.Add(Op("StructuralFraming", new
            {
                startMm = new[] { sx, sy, 0 },
                endMm = new[] { ex, ey, 0 },
                level = topLevel,
                topOffsetMm = place.TopOffsetMm,
                familyType = typeHint,
                widthMm = width,
                depthMm = depth,
                sourceFamily = sourceFamily,
                widthParam = place.WidthParam ?? "",
                heightParam = place.HeightParam ?? "",
                material = place.Material ?? "",
                mark,
                rotatedFrame = rotated
            }, skipIfExists: true));
        }

        return ops;
    }

    private static string FamilyFromTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return "";
        var sep = typeName.IndexOf(':');
        return sep > 0 ? typeName.Substring(0, sep).Trim() : "";
    }

    /// <summary>Text middle may sit this far past a segment end and still belong to it.</summary>
    private const double BeamLabelAlongPadMm = 150;
    /// <summary>Text beside the beam: lateral reach is max(beam width, this).</summary>
    private const double BeamLabelAcrossMm = 600;
    /// <summary>Text not parallel to the beam counts only when it sits on the beam body (half width + this).</summary>
    private const double BeamLabelBodyPadMm = 100;
    private const double BeamLabelAlignDeg = 10;
    /// <summary>CAD face spacing vs label width before a face pair is ranked as "not this beam".</summary>
    private const double BeamWidthTolMm = 50;
    /// <summary>Fallback CAD walls are read per layer; larger layers (hatches, details) are skipped.</summary>
    private const int CadWallLayerMaxCurves = 1500;

    private readonly struct BeamText
    {
        public BeamText(double x, double y, double dirX, double dirY, BeamLabel label)
        {
            X = x;
            Y = y;
            DirX = dirX;
            DirY = dirY;
            Label = label;
        }

        public double X { get; }
        public double Y { get; }
        public double DirX { get; }
        public double DirY { get; }
        public BeamLabel Label { get; }
    }

    private static List<BeamText> BeamTexts(CadGeometry geom)
    {
        var result = new List<BeamText>();
        foreach (var text in geom.Entities.OfType<CadText>())
        {
            if (!BeamLabelParser.TryParse(text.Value, out var parsed))
                continue;
            var rad = text.RotationDeg * Math.PI / 180.0;
            result.Add(new BeamText(text.CenterX ?? text.X, text.CenterY ?? text.Y, Math.Cos(rad), Math.Sin(rad), parsed));
        }

        return result;
    }

    /// <summary>
    /// Fallback supports from CAD: L/b ≤ 4 outlines on every layer, plus wall pairs (≥ 150 mm) on the other layers.
    /// </summary>
    private static List<SectionFootprint> CadSupports(CadGeometry geom, List<CadEntity> beamLayer)
    {
        var supports = SectionFootprintReader.FromEntities(geom.Entities);
        var beam = new HashSet<CadEntity>(beamLayer);
        var layers = geom.Entities
            .Where(e => e is CadLine or CadPolyline && !beam.Contains(e))
            .GroupBy(e => e.Layer, StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers)
        {
            var curves = layer.ToList();
            if (curves.Count > CadWallLayerMaxCurves)
                continue;
            foreach (var wall in WallCenterlineReader.FromEntities(curves))
            {
                if (wall.ThicknessMm >= 150)
                    supports.Add(BeamCenterlineReader.Body(wall));
            }
        }

        return supports;
    }

    /// <summary>
    /// Lateral distance from the run to a text that labels it, or null. The text middle must project onto the span;
    /// parallel text (±10°, either reading direction) may sit beside the beam up to max(width, 600);
    /// other text only on the beam body and always ranks after parallel text.
    /// </summary>
    private static double? LabelDistance(BeamText text, WallRun run, double ux, double uy, double len)
    {
        var ox = text.X - run.X0;
        var oy = text.Y - run.Y0;
        var along = ox * ux + oy * uy;
        if (along < -BeamLabelAlongPadMm || along > len + BeamLabelAlongPadMm)
            return null;
        var across = Math.Abs(ox * -uy + oy * ux);
        var aligned = Math.Abs(text.DirX * ux + text.DirY * uy) >= Math.Cos(BeamLabelAlignDeg * Math.PI / 180.0);
        if (aligned)
            return across <= Math.Max(run.ThicknessMm, BeamLabelAcrossMm) ? across : null;
        return across <= Math.Max(run.ThicknessMm / 2, 50) + BeamLabelBodyPadMm ? across + BeamLabelAcrossMm : null;
    }

    /// <summary>A face pair whose spacing matches none of the WxH texts labelling it (when there are any).</summary>
    private static bool WidthMismatch(List<BeamText> texts, WallRun run)
    {
        var len = Math.Sqrt((run.X1 - run.X0) * (run.X1 - run.X0) + (run.Y1 - run.Y0) * (run.Y1 - run.Y0));
        if (len < 1)
            return false;
        var ux = (run.X1 - run.X0) / len;
        var uy = (run.Y1 - run.Y0) / len;
        var labelled = false;
        foreach (var text in texts)
        {
            if (!text.Label.HasSize || LabelDistance(text, run, ux, uy, len) is null)
                continue;
            if (Math.Abs(text.Label.WidthMm!.Value - run.ThicknessMm) <= BeamWidthTolMm)
                return false;
            labelled = true;
        }

        return labelled;
    }

    /// <summary>
    /// Per-segment CadText match (size on this segment always wins), then axis rules: one distinct Mark → all
    /// segments on that axis; a segment without its own WxH takes the nearest labelled segment / text on the same axis.
    /// </summary>
    private static BeamLabel[] ResolveBeamAxisLabels(List<BeamText> texts, List<WallRun> segments, out string[] sources)
    {
        var n = segments.Count;
        var local = new BeamLabel[n];
        sources = new string[n];
        for (var i = 0; i < n; i++)
        {
            local[i] = MatchBeamLabel(texts, segments[i], out var debug);
            sources[i] = debug;
        }

        var groups = GroupBeamAxes(segments);
        var result = new BeamLabel[n];
        for (var i = 0; i < n; i++)
            result[i] = local[i];

        foreach (var group in groups)
        {
            var axisTexts = CollectAxisBeamTexts(texts, segments, group);
            var distinctMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in group)
            {
                if (local[i].HasMark)
                    distinctMarks.Add(local[i].Mark!);
            }

            foreach (var t in axisTexts)
            {
                if (t.Label.HasMark)
                    distinctMarks.Add(t.Label.Mark!);
            }

            string? sharedMark = null;
            if (distinctMarks.Count == 1)
            {
                foreach (var m in distinctMarks)
                {
                    sharedMark = m;
                    break;
                }
            }

            foreach (var i in group)
            {
                string? mark;
                if (!string.IsNullOrWhiteSpace(sharedMark))
                    mark = sharedMark;
                else if (local[i].HasMark)
                    mark = local[i].Mark;
                else
                    mark = NearestAxisMark(axisTexts, segments[i]);

                var w = local[i].WidthMm;
                var d = local[i].DepthMm;
                var raw = local[i].Raw;
                if (w is > 0 && d is > 0)
                    sources[i] = "size=local " + sources[i];
                else if (TryInheritSize(group, local, segments, i, out var iw, out var id, out var iraw))
                {
                    w = iw;
                    d = id;
                    raw = string.IsNullOrWhiteSpace(raw) ? iraw : raw + "+" + iraw;
                    sources[i] = "size=axis-segment " + sources[i];
                }
                else if (TryNearestAxisSize(axisTexts, segments[i], out iw, out id, out iraw))
                {
                    w = iw;
                    d = id;
                    raw = string.IsNullOrWhiteSpace(raw) ? iraw : raw + "+" + iraw;
                    sources[i] = "size=axis-text " + sources[i];
                }
                else
                    sources[i] = "size=default " + sources[i];

                result[i] = new BeamLabel(mark, w, d, raw ?? "");
            }
        }

        return result;
    }

    private static List<List<int>> GroupBeamAxes(List<WallRun> segments)
    {
        var parent = new int[segments.Count];
        for (var i = 0; i < parent.Length; i++)
            parent[i] = i;

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
                parent[b] = a;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            for (var j = i + 1; j < segments.Count; j++)
            {
                if (BeamCenterlineReader.SameAxis(segments[i], segments[j]))
                    Union(i, j);
            }
        }

        var map = new Dictionary<int, List<int>>();
        for (var i = 0; i < segments.Count; i++)
        {
            var r = Find(i);
            if (!map.TryGetValue(r, out var list))
                map[r] = list = new List<int>();
            list.Add(i);
        }

        return map.Values.ToList();
    }

    /// <summary>Texts labelling anywhere along the axis span covered by the group (same rules as one segment).</summary>
    private static List<BeamText> CollectAxisBeamTexts(List<BeamText> texts, List<WallRun> segments, List<int> group)
    {
        var result = new List<BeamText>();
        if (group.Count == 0)
            return result;

        var best = group[0];
        var bestLen = 0.0;
        foreach (var i in group)
        {
            var dx = segments[i].X1 - segments[i].X0;
            var dy = segments[i].Y1 - segments[i].Y0;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len > bestLen)
            {
                bestLen = len;
                best = i;
            }
        }

        var run = segments[best];
        var rdx = run.X1 - run.X0;
        var rdy = run.Y1 - run.Y0;
        var rlen = Math.Sqrt(rdx * rdx + rdy * rdy);
        if (rlen < 1)
            return result;
        var ux = rdx / rlen;
        var uy = rdy / rlen;

        var tMin = double.MaxValue;
        var tMax = double.MinValue;
        foreach (var i in group)
        {
            var s = segments[i];
            var t0 = (s.X0 - run.X0) * ux + (s.Y0 - run.Y0) * uy;
            var t1 = (s.X1 - run.X0) * ux + (s.Y1 - run.Y0) * uy;
            tMin = Math.Min(tMin, Math.Min(t0, t1));
            tMax = Math.Max(tMax, Math.Max(t0, t1));
        }

        var span = new WallRun(
            run.X0 + ux * tMin, run.Y0 + uy * tMin, run.X0 + ux * tMax, run.Y0 + uy * tMax, run.ThicknessMm);
        foreach (var text in texts)
        {
            if (LabelDistance(text, span, ux, uy, tMax - tMin) is not null)
                result.Add(text);
        }

        return result;
    }

    private static string? NearestAxisMark(List<BeamText> texts, WallRun run)
    {
        var mx = (run.X0 + run.X1) / 2;
        var my = (run.Y0 + run.Y1) / 2;
        string? best = null;
        var bestD = double.MaxValue;
        foreach (var t in texts)
        {
            if (!t.Label.HasMark)
                continue;
            var dx = t.X - mx;
            var dy = t.Y - my;
            var d = dx * dx + dy * dy;
            if (d >= bestD)
                continue;
            bestD = d;
            best = t.Label.Mark;
        }

        return best;
    }

    private static bool TryInheritSize(
        List<int> group,
        BeamLabel[] local,
        List<WallRun> segments,
        int index,
        out int widthMm,
        out int depthMm,
        out string raw)
    {
        widthMm = depthMm = 0;
        raw = "";
        var mx = (segments[index].X0 + segments[index].X1) / 2;
        var my = (segments[index].Y0 + segments[index].Y1) / 2;
        var bestD = double.MaxValue;
        var found = false;
        foreach (var j in group)
        {
            if (j == index || !local[j].HasSize)
                continue;
            var jx = (segments[j].X0 + segments[j].X1) / 2;
            var jy = (segments[j].Y0 + segments[j].Y1) / 2;
            var d = (jx - mx) * (jx - mx) + (jy - my) * (jy - my);
            if (d >= bestD)
                continue;
            bestD = d;
            widthMm = local[j].WidthMm!.Value;
            depthMm = local[j].DepthMm!.Value;
            raw = local[j].Raw;
            found = true;
        }

        return found;
    }

    private static bool TryNearestAxisSize(
        List<BeamText> texts,
        WallRun run,
        out int widthMm,
        out int depthMm,
        out string raw)
    {
        widthMm = depthMm = 0;
        raw = "";
        var mx = (run.X0 + run.X1) / 2;
        var my = (run.Y0 + run.Y1) / 2;
        var bestD = double.MaxValue;
        var found = false;
        foreach (var t in texts)
        {
            if (!t.Label.HasSize)
                continue;
            var dx = t.X - mx;
            var dy = t.Y - my;
            var d = dx * dx + dy * dy;
            if (d >= bestD)
                continue;
            bestD = d;
            widthMm = t.Label.WidthMm!.Value;
            depthMm = t.Label.DepthMm!.Value;
            raw = t.Label.Raw;
            found = true;
        }

        return found;
    }

    /// <summary>Nearest labelling text for size and for mark, resolved separately (a local 30x60 beats a farther D1X18-50x60).</summary>
    private static BeamLabel MatchBeamLabel(List<BeamText> texts, WallRun run, out string debug)
    {
        debug = "cands=0";
        var dx = run.X1 - run.X0;
        var dy = run.Y1 - run.Y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
            return default;
        var ux = dx / len;
        var uy = dy / len;

        BeamLabel bestSize = default;
        var bestSizeD = double.MaxValue;
        BeamLabel bestMark = default;
        var bestMarkD = double.MaxValue;
        var candidates = 0;
        foreach (var text in texts)
        {
            if (LabelDistance(text, run, ux, uy, len) is not double d)
                continue;
            candidates++;
            if (text.Label.HasSize && d < bestSizeD)
            {
                bestSizeD = d;
                bestSize = text.Label;
            }

            if (text.Label.HasMark && d < bestMarkD)
            {
                bestMarkD = d;
                bestMark = text.Label;
            }
        }

        debug = $"cands={candidates} text={(bestSize.HasSize ? $"{bestSize.Raw}@{bestSizeD:0}" : "-")}" +
                $" markText={(bestMark.HasMark ? $"{bestMark.Raw}@{bestMarkD:0}" : "-")}";
        if (bestSize.HasSize && bestMark.HasMark)
        {
            var raw = string.Equals(bestSize.Raw, bestMark.Raw, StringComparison.Ordinal)
                ? bestSize.Raw
                : bestMark.Raw + "+" + bestSize.Raw;
            return new BeamLabel(bestMark.Mark, bestSize.WidthMm, bestSize.DepthMm, raw);
        }

        if (bestSize.HasSize)
            return bestSize;
        if (bestMark.HasMark)
            return bestMark;
        return default;
    }

    private static IReadOnlyList<CreateOperation> PlanFloors(
        List<CadEntity> entities,
        string baseLevel,
        AnnotationSize size,
        bool structural,
        ModelRevitPlacement place)
    {
        var floorType = FirstNonEmpty(place.TypeName, AnnotationSizeParser.FloorTypeHint(size));
        var ops = new List<CreateOperation>();
        foreach (var entity in entities.OfType<CadPolyline>())
        {
            if (!entity.Closed || entity.Points.Count < 3)
                continue;
            var ring = entity.Points.Select(p => new[] { p.X, p.Y, 0.0 }).ToList();
            ops.Add(Op("Floors", new
            {
                loops = new[] { ring },
                level = baseLevel,
                baseOffsetMm = place.BaseOffsetMm,
                floorType,
                material = place.Material ?? "",
                structural
            }));
        }

        return ops;
    }

    private static IReadOnlyList<CreateOperation> PlanFoundations(
        List<CadEntity> entities,
        string baseLevel,
        AnnotationSize size,
        string foundationKind,
        ModelRevitPlacement place)
    {
        var typeName = FirstNonEmpty(place.TypeName, place.FamilyName, size.Raw);
        var ops = new List<CreateOperation>();
        foreach (var entity in entities)
        {
            if (!TryPoint(entity, out var x, out var y))
                continue;
            ops.Add(Op("StructuralFoundation", new
            {
                xyzMm = new[] { x, y, 0 },
                familyType = typeName,
                level = baseLevel,
                baseOffsetMm = place.BaseOffsetMm,
                material = place.Material ?? "",
                kind = foundationKind
            }));
        }

        return ops;
    }

    private static IReadOnlyList<CreateOperation> PlanStripFoundations(
        List<CadEntity> entities,
        string baseLevel,
        AnnotationSize size,
        ModelRevitPlacement place)
    {
        var typeName = FirstNonEmpty(place.TypeName, place.FamilyName, size.Raw);
        return entities.OfType<CadLine>().Select(line => Op("StructuralFoundation", new
        {
            startMm = new[] { line.X0, line.Y0, 0 },
            endMm = new[] { line.X1, line.Y1, 0 },
            familyType = typeName,
            level = baseLevel,
            baseOffsetMm = place.BaseOffsetMm,
            material = place.Material ?? "",
            kind = "Strip"
        })).ToList();
    }

    private static IReadOnlyList<CreateOperation> PlanInserts(
        CadGeometry geom,
        List<CadEntity> entities,
        string baseLevel,
        string category,
        string familyType,
        AnnotationSize size,
        ModelRevitPlacement place)
    {
        var ops = new List<CreateOperation>();
        foreach (var ins in entities.OfType<CadInsert>())
        {
            ops.Add(Op(category, new
            {
                xyzMm = new[] { ins.X, ins.Y, 0 },
                familyType,
                level = baseLevel,
                baseOffsetMm = place.BaseOffsetMm,
                offsetMm = place.BaseOffsetMm,
                rotationDeg = ins.RotationDeg,
                widthMm = size.WidthMm,
                heightMm = size.HeightMm,
                widthParam = place.WidthParam ?? "",
                heightParam = place.HeightParam ?? "",
                mark = place.AssignMark ? NearestMark(geom, ins.X, ins.Y) : ""
            }));
        }

        return ops;
    }

    private static IReadOnlyList<CreateOperation> PlanRooms(
        List<CadEntity> entities,
        string baseLevel,
        string roomName)
    {
        var ops = new List<CreateOperation>();
        foreach (var pl in entities.OfType<CadPolyline>().Where(p => p.Closed && p.Points.Count >= 3))
        {
            var cx = pl.Points.Average(p => p.X);
            var cy = pl.Points.Average(p => p.Y);
            ops.Add(Op("Rooms", new { xyzMm = new[] { cx, cy, 0 }, level = baseLevel, name = roomName }));
        }

        return ops;
    }

    private static IReadOnlyList<CreateOperation> PlanCeilings(
        List<CadEntity> entities,
        string baseLevel,
        AnnotationSize size,
        ModelRevitPlacement place)
    {
        var ceilingType = FirstNonEmpty(place.TypeName, string.IsNullOrWhiteSpace(size.Raw) ? "Generic" : size.Raw);
        var ops = new List<CreateOperation>();
        foreach (var pl in entities.OfType<CadPolyline>().Where(p => p.Closed && p.Points.Count >= 3))
        {
            var ring = pl.Points.Select(p => new[] { p.X, p.Y, 0.0 }).ToList();
            ops.Add(Op("Ceilings", new
            {
                loops = new[] { ring },
                level = baseLevel,
                offsetMm = place.BaseOffsetMm,
                ceilingType
            }));
        }

        return ops;
    }

    private static IReadOnlyList<CreateOperation> PlanMepLines(
        List<CadEntity> entities,
        string baseLevel,
        string category,
        AnnotationSize size,
        ModelRevitPlacement place)
    {
        var ops = new List<CreateOperation>();
        foreach (var line in entities.OfType<CadLine>())
        {
            var fields = new Dictionary<string, object>
            {
                ["startMm"] = new[] { line.X0, line.Y0, 0 },
                ["endMm"] = new[] { line.X1, line.Y1, 0 },
                ["level"] = baseLevel,
                ["offsetMm"] = place.BaseOffsetMm
            };
            if (!string.IsNullOrWhiteSpace(place.SystemName))
                fields["systemType"] = place.SystemName!;
            if (!string.IsNullOrWhiteSpace(place.TypeName))
            {
                var typeKey = category switch
                {
                    "Pipes" => "pipeType",
                    "Ducts" => "ductType",
                    _ => "type"
                };
                fields[typeKey] = place.TypeName!;
            }

            if (size.DiameterMm is { } dia)
                fields["diameterMm"] = dia;
            else if (size.WidthMm is { } w && size.HeightMm is { } h)
            {
                fields["widthMm"] = w;
                fields["heightMm"] = h;
            }
            else if (size.WidthMm is { } t)
                fields["diameterMm"] = t;

            ops.Add(Op(category, fields));
        }

        return ops;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return "";
    }

    private static bool TryPoint(CadEntity entity, out double x, out double y)
    {
        x = y = 0;
        switch (entity)
        {
            case CadCircle c:
                x = c.Cx;
                y = c.Cy;
                return true;
            case CadInsert ins:
                x = ins.X;
                y = ins.Y;
                return true;
            case CadPolyline pl when pl.Closed && pl.Points.Count >= 3:
                x = pl.Points.Average(p => p.X);
                y = pl.Points.Average(p => p.Y);
                return true;
            default:
                return false;
        }
    }

    private static CreateOperation Op(string category, object fields, bool skipIfExists = false)
    {
        var json = JsonSerializer.Serialize(fields);
        using var doc = JsonDocument.Parse(json);
        return new CreateOperation
        {
            OpId = Guid.NewGuid().ToString("N").Substring(0, 8),
            Category = category,
            Fields = doc.RootElement.Clone(),
            SkipIfExists = skipIfExists,
            Units = "mm"
        };
    }
}
