using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    PileCap,
    RaftFoundation,
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
    /// <summary>Pile length / chiều sâu cọc (mm). Top at BaseLevel (+ offset); bottom = top − length.</summary>
    public double LengthMm { get; set; }
    /// <summary>
    /// When &gt; 0 with <see cref="CapBottomOffsetMm"/> set, pile top offset =
    /// <see cref="FoundationCatalogFilter.PileTopOffsetFromCapBottomMm"/>.
    /// Otherwise <see cref="BaseOffsetMm"/> is the pile top offset directly.
    /// </summary>
    public double EmbedMm { get; set; }
    /// <summary>Offset of pile-cap bottom from Level (mm). Used with <see cref="EmbedMm"/>.</summary>
    public double? CapBottomOffsetMm { get; set; }
    /// <summary>Beam supports already in the model (columns, structural walls) as plan rectangles in CAD/model mm.</summary>
    public IReadOnlyList<SectionFootprint>? Supports { get; set; }
    /// <summary>
    /// Revit Structural Foundation Type Mark values (Đài cọc). When set, CAD labels must match
    /// one of these or the outline is skipped (no CAD WxH invent).
    /// </summary>
    public IReadOnlyList<string>? FoundationTypeMarks { get; set; }
    public Action<string>? Log { get; set; }

    /// <summary>Effective free-host offset for piles (đỉnh cọc relative to Level).</summary>
    public double EffectivePileTopOffsetMm()
    {
        if (CapBottomOffsetMm is double capBottom)
            return FoundationCatalogFilter.PileTopOffsetFromCapBottomMm(capBottom, EmbedMm);
        return BaseOffsetMm;
    }
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
            .Where(e => SameLayer(e.Layer, layerName))
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
            ModelRevitPartKind.IsolatedFoundation => PlanFoundations(
                geom, entities, baseLevel, size, FoundationCatalogFilter.KindIsolated, place, gridPoints,
                resizeFromCad: true, resolveByTypeMark: false),
            ModelRevitPartKind.PileCap => PlanFoundations(
                geom, entities, baseLevel, size, FoundationCatalogFilter.KindPileCap, place, gridPoints,
                resizeFromCad: false, resolveByTypeMark: true),
            ModelRevitPartKind.RaftFoundation => PlanFoundations(
                geom, entities, baseLevel, size, FoundationCatalogFilter.KindRaft, place, gridPoints,
                resizeFromCad: false, resolveByTypeMark: false),
            // Piles: host level = selected floor (BaseLevel); LengthMm drives depth. XY like columns (5 mm vs grids).
            ModelRevitPartKind.Pile => PlanPiles(geom, entities, baseLevel, size, place, gridPoints),
            ModelRevitPartKind.StripFoundation => PlanStripFoundations(geom, entities, baseLevel, size, place, gridPoints),
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
            // 5 mm steps along the column's own axes from the nearest grid intersection, so offsets to grids
            // read xxx0 / xxx5 even when the grids themselves sit on fractional world millimetres.
            var (ox, oy) = NearestGridPoint(footprint.CenterX, footprint.CenterY, gridPoints);
            var (x, y) = RoundColumnPosition(footprint.CenterX, footprint.CenterY, rotation, ox, oy);
            var (gx, gy, snapped) = SnapToGrid(x, y, gridPoints);
            if (snapped && OnRoundModule(gx, gy, rotation, ox, oy))
            {
                x = gx;
                y = gy;
            }
            ColumnSection(footprint, size, place.SectionShape, out var w, out var d);
            var orthogonal = IsOrthogonal(rotation);
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
            TextPoint(text, out var tx, out var ty);
            var dx = tx - x;
            var dy = ty - y;
            var distance = dx * dx + dy * dy;
            if (distance >= bestD)
                continue;
            bestD = distance;
            best = text.Value.Trim();
        }

        return best;
    }

    private static void TextPoint(CadText text, out double x, out double y)
    {
        if (text.CenterX is double cx && text.CenterY is double cy)
        {
            x = cx;
            y = cy;
            return;
        }

        x = text.X;
        y = text.Y;
    }

    /// <summary>
    /// Đài cọc label lookup: prefer Đ* pile-cap ids (even if slightly farther than beam/grid text).
    /// Returns (label, kind) where kind is pilecap / other / none.
    /// </summary>
    private static (string Label, string Kind) NearestPileCapLabel(CadGeometry geom, double x, double y)
    {
        const double pileR = 3500.0;
        const double anyR = 2000.0;
        string bestPile = "";
        var bestPileD = pileR * pileR;
        string bestOther = "";
        var bestOtherD = anyR * anyR;

        foreach (var text in geom.Entities.OfType<CadText>())
        {
            var raw = text.Value?.Trim() ?? "";
            if (!CadMark.IsLabel(raw))
                continue;
            TextPoint(text, out var tx, out var ty);
            var dx = tx - x;
            var dy = ty - y;
            var distance = dx * dx + dy * dy;

            string? pileId = null;
            if (CadMark.LooksLikePileCapId(raw))
                pileId = raw;
            else if (CadMark.TryExtractPileCapId(raw, out var extracted) && CadMark.LooksLikePileCapId(extracted))
                pileId = extracted;
            if (pileId is not null)
            {
                if (distance >= bestPileD)
                    continue;
                bestPileD = distance;
                bestPile = pileId;
                continue;
            }

            if (distance >= bestOtherD)
                continue;
            bestOtherD = distance;
            bestOther = raw;
        }

        if (bestPile.Length > 0)
            return (bestPile, "pilecap");
        if (bestOther.Length > 0)
            return (bestOther, "other");
        return ("", "none");
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

    /// <summary>Nearest 5 mm (moves at most 2.5 mm per axis). 2172.4 becomes 2170, 2173 becomes 2175.</summary>
    public const double PositionRoundMm = 5.0;

    /// <summary>
    /// Map CAD outline long-edge angle to Revit <c>FamilyInstance</c> rotation about Z.
    /// Planner <c>rotationDeg</c> = angle of the CAD long edge (world +X = 0°).
    /// Family width = local +X, depth = local +Y. When depth &gt; width the long side is +Y, so
    /// Revit needs <c>cadLongEdgeDeg − 90</c> (horizontal CAD → −90°, not 0° — otherwise the
    /// instance reads vertical). When width ≥ depth, long side is +X → use <paramref name="cadLongEdgeDeg"/> as-is.
    /// </summary>
    public static double FoundationInstanceRotationDeg(double cadLongEdgeDeg, int familyWidthMm, int familyDepthMm)
    {
        if (familyDepthMm > familyWidthMm)
            return cadLongEdgeDeg - 90.0;
        return cadLongEdgeDeg;
    }

    /// <summary>
    /// Round a column center to the nearest <see cref="PositionRoundMm"/> along its own axes, measured from
    /// (<paramref name="originX"/>, <paramref name="originY"/>).
    /// </summary>
    public static (double X, double Y) RoundColumnPosition(
        double x, double y, double rotationDeg, double originX = 0, double originY = 0)
    {
        var rad = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        x -= originX;
        y -= originY;
        var along = RoundStep(x * cos + y * sin, PositionRoundMm);
        var across = RoundStep(-x * sin + y * cos, PositionRoundMm);
        return (originX + along * cos - across * sin, originY + along * sin + across * cos);
    }

    private static bool OnRoundModule(double x, double y, double rotationDeg, double originX, double originY)
    {
        var (rx, ry) = RoundColumnPosition(x, y, rotationDeg, originX, originY);
        var dx = rx - x;
        var dy = ry - y;
        return dx * dx + dy * dy <= 1.0;
    }

    private static double RoundStep(double value, double step) =>
        Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

    /// <summary>Same rule as <c>UnitConv.Round5Mm</c> — keep Core free of Revit API.</summary>
    public static double Round5Mm(double mm) => RoundStep(mm, PositionRoundMm);

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
    /// Wall / beam: length and offsets along/across its own axes on <see cref="PositionRoundMm"/> steps, measured
    /// from the nearest grid intersection (so face/end offsets to those grids read xxx0 / xxx5). The end nearer a
    /// 5 mm station is kept; the other end follows the rounded length.
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1) RoundWallInFrame(
        double x0, double y0, double x1, double y1, double angleDeg, double originX = 0, double originY = 0,
        double halfWidthMm = 0)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var ux = Math.Cos(rad);
        var uy = Math.Sin(rad);
        double Along(double x, double y) => (x - originX) * ux + (y - originY) * uy;
        double Across(double x, double y) => -(x - originX) * uy + (y - originY) * ux;
        var a0 = Along(x0, y0);
        var a1 = Along(x1, y1);
        var across = RoundAcross((Across(x0, y0) + Across(x1, y1)) / 2, halfWidthMm);
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

    /// <summary>
    /// Centerline offset from the grid. When half the width is a 5 mm multiple, centerline and both faces
    /// land on 5 mm together; otherwise the face nearer the grid is put on 5 mm (faces are what gets dimensioned).
    /// </summary>
    public static double RoundAcross(double centerMm, double halfWidthMm)
    {
        if (halfWidthMm <= 0 || Off5(halfWidthMm) < 1e-9)
            return RoundStep(centerMm, PositionRoundMm);
        var side = centerMm >= 0 ? 1.0 : -1.0;
        return RoundStep(centerMm - side * halfWidthMm, PositionRoundMm) + side * halfWidthMm;
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
            var (ox, oy) = NearestGridPoint((run.X0 + run.X1) / 2, (run.Y0 + run.Y1) / 2, gridPoints);
            var (sx, sy, ex, ey) = RoundWallInFrame(run.X0, run.Y0, run.X1, run.Y1, angle, ox, oy);

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
        entities = WithLabelledBeamLayers(geom, entities, texts, place);
        var runs = BeamCenterlineReader.FromEntities(entities, run => BeamPairRank(texts, run));
        if (runs.Count == 0)
            return Array.Empty<CreateOperation>();

        // Model columns / structural walls win: CAD outlines on every layer also catch section markers
        // and junction boxes, so they are only the fallback. CAD supports are filtered to joints
        // (footprint on ≥2 non-parallel beam runs) so lone hatches do not mid-span-break mains.
        var fromModel = place.Supports is { Count: > 0 };
        var supports = fromModel
            ? place.Supports!
            : FilterCadSupports(CadSupports(geom, entities, gridPoints), runs);
        var oneFace = BeamCenterlineReader.OneFaceRuns(
            entities, SameDrawing(geom, entities), runs, supports, face => OneFaceLabelWidth(texts, face),
            run => PairHasOwnLabel(texts, run));
        var falsePairs = runs
            .Where(r => !PairHasOwnLabel(texts, r) && oneFace.Any(f => BeamCenterlineReader.OversizedPair(r, f)))
            .ToList();
        runs = BeamCenterlineReader.ReachPastOversized(runs.Except(falsePairs).ToList(), falsePairs, oneFace);
        foreach (var f in oneFace)
            place.Log?.Invoke($"beam one-face ({f.X0:0},{f.Y0:0})-({f.X1:0},{f.Y1:0}) t={f.ThicknessMm:0}");
        runs.AddRange(oneFace);
        var planned = BeamCenterlineReader.PrepareRuns(runs, supports, entities, SameDrawing(geom, entities)).ToList();
        if (planned.Count == 0)
            return Array.Empty<CreateOperation>();
        place.Log?.Invoke(
            $"beam plan runs={runs.Count} oneFace={oneFace.Count} supports={supports.Count} ({(fromModel ? "model" : "cad")}) " +
            $"segments={planned.Count} main={planned.Count(p => p.Kind == BeamSpanKind.Main)}");

        // Labels after final split: local text wins → size inherit → Mark inherit on axis →
        // same-section collinear merge (no real support between) → junk/dedupe.
        var nearest = texts.Select(t => planned.Min(s => LabelNearestCost(t, s.Run))).ToList();
        var labels = new BeamLabel[planned.Count];
        var sources = new string[planned.Count];
        for (var i = 0; i < planned.Count; i++)
            labels[i] = MatchBeamLabel(texts, nearest, planned[i].Run, out sources[i]);
        var layers = SegmentLayers(planned, entities);
        InheritCollinearBeamLabels(planned, labels, sources, layers, place.Log);
        InheritAxisMarks(planned, labels, sources, layers, place.Log);
        InheritCornerLabels(planned, labels, sources, place.Log);
        KeepCadWidthOverForeignLabel(planned, labels, sources, place.Log);
        CenterOwnLabelsOnDrawnFaces(planned, labels, sources, SameDrawing(geom, entities), place.Log);
        MergeSameSectionCollinear(planned, ref labels, ref sources, supports, fromModel, place.Log);

        // Drop ultra-short junk, then collapse near-coincident parallel bays to one segment.
        var keep = new bool[planned.Count];
        for (var i = 0; i < planned.Count; i++)
            keep[i] = !IsJunkShortBeam(planned[i].Run, labels[i]);
        DeduplicateOverlappingBeams(planned, labels, sources, keep, place.Log);
        var kept = keep.Count(k => k);
        if (kept < planned.Count)
            place.Log?.Invoke($"beam plan after dedupe/short: {kept}/{planned.Count} segments");

        var uiW = size.WidthMm is > 0 ? (int)Math.Round(size.WidthMm.Value) : 0;
        var uiD = size.HeightMm is > 0 ? (int)Math.Round(size.HeightMm.Value) : uiW;
        var sourceFamily = FirstNonEmpty(place.FamilyName, FamilyFromTypeName(place.TypeName));
        // Flush-to-column-face only for perimeter/edge beams (outermost face on the beam-set hull).
        var edgeBeam = ClassifyEdgeBeams(planned.Select(p => p.Run).ToList());
        var ops = new List<CreateOperation>();
        for (var i = 0; i < planned.Count; i++)
        {
            if (!keep[i])
                continue;
            var run = planned[i].Run;
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
                // width×2 only when no local/inherited Mark-WxH (InheritCollinearBeamLabels already ran).
                if (depth <= 0)
                    depth = width > 0 ? width * 2 : 0;
            }

            var dx = run.X1 - run.X0;
            var dy = run.Y1 - run.Y0;
            var angle = WallFrameAngle(Math.Atan2(dy, dx) * 180.0 / Math.PI, Math.Sqrt(dx * dx + dy * dy), gridAnglesDeg);
            var rotated = !IsOrthogonal(angle);
            var (ox, oy) = NearestGridPoint((run.X0 + run.X1) / 2, (run.Y0 + run.Y1) / 2, gridPoints);
            var (sx, sy, ex, ey) = RoundWallInFrame(run.X0, run.Y0, run.X1, run.Y1, angle, ox, oy, width / 2.0);

            BeamCenterlineReader.Orient(sx, sy, ex, ey, out sx, out sy, out ex, out ey);
            var placedW = width > 0 ? width : run.ThicknessMm;
            var flushShift = FlushToSupportFace(run, sx, sy, ex, ey, placedW, supports);
            var isEdge = edgeBeam[i];
            // Grow-inward flush only when type ≥ CAD face spacing (250 CAD → 300 type). Wall-top
            // outlines wider than the WxH label (500 CAD, 30x70) must keep the CAD midline.
            var cadWiderThanType = placedW + FlushFaceTolMm < run.ThicknessMm;
            if (flushShift is double shift && isEdge && !cadWiderThanType)
            {
                var flen = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
                var fnx = -(ey - sy) / flen;
                var fny = (ex - sx) / flen;
                sx += fnx * shift;
                sy += fny * shift;
                ex += fnx * shift;
                ey += fny * shift;
                place.Log?.Invoke($"beam flush seg {i} edge=true shift={shift:0.###} to support face");
            }
            else if (flushShift is double wideSkip && isEdge && cadWiderThanType)
            {
                place.Log?.Invoke(
                    $"beam flush seg {i} edge=true skipped CAD wider than type shift={wideSkip:0.###}");
            }
            else if (flushShift is double interiorSkip)
            {
                place.Log?.Invoke($"beam flush seg {i} edge=false skipped shift={interiorSkip:0.###}");
            }

            var typeHint = width > 0 && depth > 0
                ? $"{width}x{depth}"
                : FirstNonEmpty(place.TypeName, place.FamilyName, AnnotationSizeParser.BeamFamilyTypeHint(size));
            var mark = label.Mark ?? "";
            var sizeSrc = sources[i].StartsWith("cands=", StringComparison.Ordinal) ? "size=local"
                : sources[i].StartsWith("inherit=", StringComparison.Ordinal) ? "size=inherit"
                : sources[i].StartsWith("axisMark=", StringComparison.Ordinal) ? "size=axis-segment"
                : sources[i].StartsWith("merge=", StringComparison.Ordinal) ? "size=merge"
                : label.HasSize ? "size=kept" : "size=default";
            place.Log?.Invoke(
                $"beam seg {i} {planned[i].Kind} ({sx:0},{sy:0})-({ex:0},{ey:0}) t={run.ThicknessMm:0} " +
                $"type={typeHint} mark={mark} {sizeSrc} {sources[i]}");

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
                rotatedFrame = rotated,
                // Offsets are 5 mm from grids that sit on fractional world mm: world whole-mm rounding
                // at create put every beam 0.2386 / 0.1158 mm off its grid.
                exactMm = true
            }, skipIfExists: true));
        }

        return ops;
    }

    /// <summary>CAD beam face within this of a parallel support face counts as drawn flush with it.</summary>
    public const double FlushFaceTolMm = 15;

    /// <summary>
    /// Edge/perimeter beam = at least one face on the convex hull of all planned beam faces
    /// (same envelope as <see cref="ClassifyWalls"/>). Interior beams have structure outward of both faces.
    /// </summary>
    internal static bool[] ClassifyEdgeBeams(IReadOnlyList<WallRun> runs)
    {
        var sides = ClassifyWalls(runs);
        var edge = new bool[runs.Count];
        for (var i = 0; i < runs.Count; i++)
            edge[i] = sides[i].Exterior;
        return edge;
    }

    /// <summary>
    /// Edge beams drawn flush with a column/wall face: keep that face exactly flush with the placed type width b
    /// (beam grows inward), instead of centering b on the CAD centerline (300 type on a 250 CAD pair stuck out 25).
    /// Callers must gate on <see cref="ClassifyEdgeBeams"/> — interior beams stay on CAD centerline (+ 5 mm round).
    /// Returns the across shift of the placed line (normal = left of start→end), or null.
    /// </summary>
    public static double? FlushToSupportFace(
        WallRun run, double sx, double sy, double ex, double ey, double widthMm, IReadOnlyList<SectionFootprint> supports)
    {
        var len = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
        if (len < 1 || widthMm <= 0 || run.ThicknessMm <= 0)
            return null;
        var ux = (ex - sx) / len;
        var uy = (ey - sy) / len;
        var nx = -uy;
        var ny = ux;
        double Across(double x, double y) => (x - sx) * nx + (y - sy) * ny;
        double Along(double x, double y) => (x - sx) * ux + (y - sy) * uy;
        var cadCenter = Across((run.X0 + run.X1) / 2, (run.Y0 + run.Y1) / 2);
        double? best = null;
        var bestGap = FlushFaceTolMm;
        foreach (var s in supports)
        {
            var rad = s.RotationDeg * Math.PI / 180.0;
            var ax = Math.Cos(rad);
            var ay = Math.Sin(rad);
            var an = Math.Abs(ax * nx + ay * ny);
            if (an > 1e-3 && an < 1 - 1e-3)
                continue;
            var halfN = an >= 0.5 ? s.WidthMm / 2 : s.DepthMm / 2;
            var halfU = an >= 0.5 ? s.DepthMm / 2 : s.WidthMm / 2;
            var sc = Across(s.CenterX, s.CenterY);
            var sa = Along(s.CenterX, s.CenterY);
            if (sa < -halfU - 1 || sa > len + halfU + 1 || Math.Abs(sc - cadCenter) > halfN + run.ThicknessMm / 2)
                continue;
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var cadFace = cadCenter + side * run.ThicknessMm / 2;
                var supportFace = sc + side * halfN;
                var gap = Math.Abs(cadFace - supportFace);
                if (gap > bestGap)
                    continue;
                bestGap = gap;
                best = supportFace - side * widthMm / 2;
            }
        }

        return best is double target && Math.Abs(target) > 1e-9 ? target : null;
    }

    /// <summary>
    /// Length &lt; max(200, thickness) with no local size text → junk stub (e.g. 250 mm bridge between
    /// double outlines). Size-labelled short pieces are kept as intentional cantilevers.
    /// </summary>
    private static bool IsJunkShortBeam(WallRun run, BeamLabel label)
    {
        var len = Math.Sqrt((run.X1 - run.X0) * (run.X1 - run.X0) + (run.Y1 - run.Y0) * (run.Y1 - run.Y0));
        var minKeep = Math.Max(BeamCenterlineReader.MinLengthMm, run.ThicknessMm);
        if (len + 1e-6 >= minKeep)
            return false;
        return !label.HasSize;
    }

    /// <summary>
    /// After local text assignment: unlabeled end-to-end collinear siblings take Mark-WxH from the
    /// nearest labeled piece on the same midline (flood until stable). Parallel offset pairs are
    /// excluded by <see cref="BeamCenterlineReader.CollinearInheritSibling"/>.
    /// </summary>
    private static void InheritCollinearBeamLabels(
        IReadOnlyList<BeamSegment> planned, BeamLabel[] labels, string[] sources, string[] layers, Action<string>? log)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < planned.Count; i++)
            {
                if (labels[i].HasSize)
                    continue;
                var best = -1;
                var bestGap = double.MaxValue;
                for (var j = 0; j < planned.Count; j++)
                {
                    if (i == j || !labels[j].HasSize || !SameBeamGroup(layers, i, j))
                        continue;
                    if (!BeamCenterlineReader.CollinearInheritSibling(
                            planned[i].Run, planned[j].Run, out var gap, out _))
                        continue;
                    if (gap < bestGap - 1e-6 ||
                        (Math.Abs(gap - bestGap) <= 1e-6 && best >= 0 &&
                         BeamPreferScore(planned[j], labels[j]) >
                         BeamPreferScore(planned[best], labels[best])))
                    {
                        bestGap = gap;
                        best = j;
                    }
                }

                if (best < 0)
                    continue;
                var src = labels[best];
                var mark = labels[i].HasMark ? labels[i].Mark : src.Mark;
                var raw = string.IsNullOrEmpty(labels[i].Raw) ? src.Raw : labels[i].Raw + "+" + src.Raw;
                labels[i] = new BeamLabel(mark, src.WidthMm, src.DepthMm, raw);
                sources[i] = $"inherit={src.Raw}@seg{best}";
                log?.Invoke($"beam inherit seg {i} ← seg {best} {src.Raw} gap={bestGap:0}");
                changed = true;
            }
        }
    }

    /// <summary>
    /// H1: empty Mark inherits from the same beam axis. Prefer the unique Mark on that axis when
    /// labeled pieces agree; otherwise the nearest labeled sibling. Size-only texts never invent a Mark.
    /// </summary>
    private static void InheritAxisMarks(
        IReadOnlyList<BeamSegment> planned, BeamLabel[] labels, string[] sources, string[] layers, Action<string>? log)
    {
        var n = planned.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++)
            parent[i] = i;
        int Find(int x)
        {
            while (parent[x] != x)
                x = parent[x] = parent[parent[x]];
            return x;
        }

        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
                parent[b] = a;
        }

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                // Far pieces on one drawn line are different beams (GTC4 at the edge vs a stub at the core).
                if (SameBeamGroup(layers, i, j) &&
                    BeamCenterlineReader.CollinearAxisSibling(planned[i].Run, planned[j].Run, out var gap, out _) &&
                    gap <= BeamCenterlineReader.MaxBridgeMm)
                    Union(i, j);
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
                groups[root] = list = new List<int>();
            list.Add(i);
        }

        foreach (var members in groups.Values)
        {
            var distinct = members
                .Where(i => labels[i].HasMark)
                .Select(i => labels[i].Mark!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinct.Count == 1)
            {
                var mark = distinct[0];
                foreach (var i in members)
                {
                    if (labels[i].HasMark)
                        continue;
                    labels[i] = new BeamLabel(mark, labels[i].WidthMm, labels[i].DepthMm,
                        string.IsNullOrEmpty(labels[i].Raw) ? mark : labels[i].Raw + "+" + mark);
                    sources[i] = AxisMarkSource(sources[i], $"axisMark={mark}");
                    log?.Invoke($"beam axis-mark seg {i} ← {mark} (unique on axis)");
                }

                continue;
            }

            if (distinct.Count == 0)
                continue;

            foreach (var i in members)
            {
                if (labels[i].HasMark)
                    continue;
                var best = -1;
                var bestGap = double.MaxValue;
                foreach (var j in members)
                {
                    if (i == j || !labels[j].HasMark)
                        continue;
                    if (!BeamCenterlineReader.CollinearAxisSibling(
                            planned[i].Run, planned[j].Run, out var gap, out _))
                        continue;
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        best = j;
                    }
                }

                if (best < 0)
                    continue;
                var mark = labels[best].Mark!;
                labels[i] = new BeamLabel(mark, labels[i].WidthMm, labels[i].DepthMm,
                    string.IsNullOrEmpty(labels[i].Raw) ? mark : labels[i].Raw + "+" + mark);
                sources[i] = AxisMarkSource(sources[i], $"axisMark={mark}@seg{best}");
                log?.Invoke($"beam axis-mark seg {i} ← seg {best} {mark} gap={bestGap:0}");
            }
        }
    }

    /// <summary>
    /// H2: merge consecutive collinear same-WxH MAIN pieces (compatible Marks) into one
    /// support-to-support run when no real support sits between them. Secondaries stay split at
    /// main centerlines. CAD-only false hatches that do not form a perpendicular joint are ignored
    /// so fragments like D1Y7 Length 2100 rejoin the bay.
    /// </summary>
    private static void MergeSameSectionCollinear(
        List<BeamSegment> planned,
        ref BeamLabel[] labels,
        ref string[] sources,
        IReadOnlyList<SectionFootprint> supports,
        bool supportsFromModel,
        Action<string>? log)
    {
        var alive = Enumerable.Range(0, planned.Count).ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var ai = 0; ai < alive.Count; ai++)
            {
                for (var bi = ai + 1; bi < alive.Count; bi++)
                {
                    var i = alive[ai];
                    var j = alive[bi];
                    // MAIN only — secondary pieces broken at mains must not rejoin across them.
                    if (planned[i].Kind != BeamSpanKind.Main || planned[j].Kind != BeamSpanKind.Main)
                        continue;
                    if (!SameBeamSection(labels[i], labels[j]))
                        continue;
                    if (!MarksMergeCompatible(labels[i], labels[j]))
                        continue;
                    if (!BeamCenterlineReader.CollinearAxisSibling(
                            planned[i].Run, planned[j].Run, out var gap, out _))
                        continue;
                    if (gap > BeamCenterlineReader.MergeGapMm)
                        continue;
                    if (SupportBlocksSameSectionMerge(
                            planned[i].Run, planned[j].Run, planned, alive, supports, supportsFromModel))
                        continue;
                    if (CrossingBeamBlocksMerge(planned[i].Run, planned[j].Run, planned, alive, i, j))
                        continue;

                    var mergedRun = BeamCenterlineReader.MergeCollinearRuns(planned[i].Run, planned[j].Run);
                    var kind = BeamSpanKind.Main;
                    var keepLabel = PreferMergeLabel(labels[i], labels[j]);
                    var drop = j;
                    var keep = i;
                    planned[keep] = new BeamSegment(mergedRun, kind);
                    labels[keep] = keepLabel;
                    sources[keep] = $"merge={sources[keep]}+{sources[drop]}";
                    log?.Invoke(
                        $"beam merge seg {keep} ← seg {drop} {keepLabel.WidthMm}x{keepLabel.DepthMm} " +
                        $"mark={keepLabel.Mark ?? ""} gap={gap:0}");
                    alive.RemoveAt(bi);
                    changed = true;
                    break;
                }

                if (changed)
                    break;
            }
        }

        if (alive.Count == planned.Count)
            return;

        var nextPlanned = new List<BeamSegment>(alive.Count);
        var nextLabels = new BeamLabel[alive.Count];
        var nextSources = new string[alive.Count];
        for (var k = 0; k < alive.Count; k++)
        {
            var i = alive[k];
            nextPlanned.Add(planned[i]);
            nextLabels[k] = labels[i];
            nextSources[k] = sources[i];
        }

        planned.Clear();
        planned.AddRange(nextPlanned);
        labels = nextLabels;
        sources = nextSources;
    }

    /// <summary>
    /// Secondary pieces broken at a main centerline (or at a wider secondary passing through) stay broken
    /// even with the same Mark/WxH: no column there, but the crossing beam is the support.
    /// </summary>
    private static bool CrossingBeamBlocksMerge(
        WallRun a, WallRun b, IReadOnlyList<BeamSegment> planned, IReadOnlyList<int> alive, int ia, int ib)
    {
        var merged = BeamCenterlineReader.MergeCollinearRuns(a, b);
        var ml = Math.Sqrt(
            (merged.X1 - merged.X0) * (merged.X1 - merged.X0) +
            (merged.Y1 - merged.Y0) * (merged.Y1 - merged.Y0));
        if (ml < 1)
            return false;
        var mux = (merged.X1 - merged.X0) / ml;
        var muy = (merged.Y1 - merged.Y0) / ml;
        double S(double x, double y) => (x - merged.X0) * mux + (y - merged.Y0) * muy;
        var aHi = Math.Max(S(a.X0, a.Y0), S(a.X1, a.Y1));
        var aLo = Math.Min(S(a.X0, a.Y0), S(a.X1, a.Y1));
        var bHi = Math.Max(S(b.X0, b.Y0), S(b.X1, b.Y1));
        var bLo = Math.Min(S(b.X0, b.Y0), S(b.X1, b.Y1));
        var junction = aHi <= bLo + 1 ? (aHi + bLo) / 2 : (bHi + aLo) / 2;
        var px = merged.X0 + mux * junction;
        var py = merged.Y0 + muy * junction;

        foreach (var k in alive)
        {
            if (k == ia || k == ib)
                continue;
            var other = planned[k];
            var r = other.Run;
            var ol = Math.Sqrt((r.X1 - r.X0) * (r.X1 - r.X0) + (r.Y1 - r.Y0) * (r.Y1 - r.Y0));
            if (ol < 1)
                continue;
            var oux = (r.X1 - r.X0) / ol;
            var ouy = (r.Y1 - r.Y0) / ol;
            if (Math.Abs(oux * mux + ouy * muy) > 0.9)
                continue;
            var t = (px - r.X0) * oux + (py - r.Y0) * ouy;
            var pad = Math.Max(r.ThicknessMm / 2, 50);
            var across = Math.Abs((px - r.X0) * -ouy + (py - r.Y0) * oux);
            if (across > pad)
                continue;
            if (other.Kind == BeamSpanKind.Main && t >= -pad && t <= ol + pad)
                return true;
            // Wider secondary passing through (both sides of the junction) also broke this axis.
            if (t > pad && t < ol - pad)
                return true;
        }

        return false;
    }

    private static bool SameBeamSection(BeamLabel a, BeamLabel b) =>
        a.HasSize && b.HasSize && a.WidthMm == b.WidthMm && a.DepthMm == b.DepthMm;

    private static bool MarksMergeCompatible(BeamLabel a, BeamLabel b) =>
        !a.HasMark || !b.HasMark ||
        string.Equals(a.Mark, b.Mark, StringComparison.OrdinalIgnoreCase);

    private static BeamLabel PreferMergeLabel(BeamLabel a, BeamLabel b)
    {
        var mark = a.HasMark ? a.Mark : b.Mark;
        if (b.HasMark && a.HasMark &&
            !string.Equals(a.Mark, b.Mark, StringComparison.OrdinalIgnoreCase))
            mark = a.Mark;
        var raw = string.IsNullOrEmpty(a.Raw) ? b.Raw
            : string.IsNullOrEmpty(b.Raw) ? a.Raw
            : a.Raw + "+" + b.Raw;
        return new BeamLabel(mark, a.WidthMm ?? b.WidthMm, a.DepthMm ?? b.DepthMm, raw);
    }

    /// <summary>
    /// A support between two collinear pieces blocks merge when it is a model column/wall, or when a
    /// CAD support is corroborated by a parallel bay (same station on another midline) or by a
    /// perpendicular planned endpoint. Lone CAD hatches that only nick this bay (D1Y7 @ 2100) do not block.
    /// </summary>
    private static bool SupportBlocksSameSectionMerge(
        WallRun a,
        WallRun b,
        IReadOnlyList<BeamSegment> planned,
        IReadOnlyList<int> alive,
        IReadOnlyList<SectionFootprint> supports,
        bool supportsFromModel)
    {
        var merged = BeamCenterlineReader.MergeCollinearRuns(a, b);
        var ml = Math.Sqrt(
            (merged.X1 - merged.X0) * (merged.X1 - merged.X0) +
            (merged.Y1 - merged.Y0) * (merged.Y1 - merged.Y0));
        if (ml < 1)
            return false;
        var mux = (merged.X1 - merged.X0) / ml;
        var muy = (merged.Y1 - merged.Y0) / ml;
        var a0 = (a.X0 - merged.X0) * mux + (a.Y0 - merged.Y0) * muy;
        var a1 = (a.X1 - merged.X0) * mux + (a.Y1 - merged.Y0) * muy;
        var b0 = (b.X0 - merged.X0) * mux + (b.Y0 - merged.Y0) * muy;
        var b1 = (b.X1 - merged.X0) * mux + (b.Y1 - merged.Y0) * muy;
        if (a0 > a1)
            (a0, a1) = (a1, a0);
        if (b0 > b1)
            (b0, b1) = (b1, b0);
        // Open interval covering the gap / shared junction between the two pieces (not outer ends).
        var joinLo = Math.Min(a1, b1);
        var joinHi = Math.Max(a0, b0);
        if (joinHi < joinLo)
            (joinLo, joinHi) = (joinHi, joinLo);
        // Abutting at a point: treat a small pad around the junction as the "between" zone.
        if (joinHi - joinLo < 1)
        {
            var mid = (joinLo + joinHi) / 2;
            joinLo = mid - 25;
            joinHi = mid + 25;
        }

        var others = alive.Select(i => planned[i].Run).ToList();
        foreach (var support in supports)
        {
            // Model supports count on the junction too: a column center at the end of the gap is still a joint.
            if (!BeamCenterlineReader.SupportBetween(merged, joinLo, joinHi, support, endPadMm: supportsFromModel ? -1 : 1))
                continue;
            if (supportsFromModel)
                return true;
            if (BeamCenterlineReader.PerpendicularEndpointNear(merged, support, others))
                return true;
            // Parallel bay also broken at this station → real grid column line, not a lone hatch.
            if (ParallelBayAlsoSplitAt(merged, joinLo, joinHi, planned, alive, a, b))
                return true;
            // No parallel bay exists to corroborate → keep the CAD support (single-axis fixtures).
            if (!HasDistantParallelBay(merged, others, a, b))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when another kept run is parallel to <paramref name="axis"/> but on a different midline
    /// (lateral &gt; near-dup), excluding the two pieces being merged.
    /// </summary>
    private static bool HasDistantParallelBay(WallRun axis, IReadOnlyList<WallRun> others, WallRun skipA, WallRun skipB)
    {
        foreach (var other in others)
        {
            if (ReferenceEqualsRun(other, skipA) || ReferenceEqualsRun(other, skipB))
                continue;
            if (!BeamCenterlineReader.CollinearAxisSibling(axis, other, out _, out var lat))
            {
                // CollinearAxisSibling fails on large lateral; check parallel + lateral separately.
                var al = Math.Sqrt((axis.X1 - axis.X0) * (axis.X1 - axis.X0) + (axis.Y1 - axis.Y0) * (axis.Y1 - axis.Y0));
                var ol = Math.Sqrt((other.X1 - other.X0) * (other.X1 - other.X0) + (other.Y1 - other.Y0) * (other.Y1 - other.Y0));
                if (al < 1 || ol < 1)
                    continue;
                var aux = (axis.X1 - axis.X0) / al;
                var auy = (axis.Y1 - axis.Y0) / al;
                var oux = (other.X1 - other.X0) / ol;
                var ouy = (other.Y1 - other.Y0) / ol;
                if (Math.Abs(aux * oux + auy * ouy) < BeamCenterlineReader.DedupeAlignCos)
                    continue;
                var midX = (other.X0 + other.X1) / 2;
                var midY = (other.Y0 + other.Y1) / 2;
                lat = Math.Abs((midX - axis.X0) * -auy + (midY - axis.Y0) * aux);
            }

            if (lat > BeamCenterlineReader.NearDuplicateMidlineMm + 50)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Another parallel bay (not a near-duplicate midline) also has an endpoint near the join station.
    /// </summary>
    private static bool ParallelBayAlsoSplitAt(
        WallRun axis, double joinLo, double joinHi,
        IReadOnlyList<BeamSegment> planned, IReadOnlyList<int> alive, WallRun skipA, WallRun skipB)
    {
        var al = Math.Sqrt((axis.X1 - axis.X0) * (axis.X1 - axis.X0) + (axis.Y1 - axis.Y0) * (axis.Y1 - axis.Y0));
        if (al < 1)
            return false;
        var aux = (axis.X1 - axis.X0) / al;
        var auy = (axis.Y1 - axis.Y0) / al;
        var joinMid = (joinLo + joinHi) / 2;
        var jx = axis.X0 + aux * joinMid;
        var jy = axis.Y0 + auy * joinMid;
        foreach (var idx in alive)
        {
            var other = planned[idx].Run;
            if (ReferenceEqualsRun(other, skipA) || ReferenceEqualsRun(other, skipB))
                continue;
            var ol = Math.Sqrt((other.X1 - other.X0) * (other.X1 - other.X0) + (other.Y1 - other.Y0) * (other.Y1 - other.Y0));
            if (ol < 1)
                continue;
            var oux = (other.X1 - other.X0) / ol;
            var ouy = (other.Y1 - other.Y0) / ol;
            if (Math.Abs(aux * oux + auy * ouy) < BeamCenterlineReader.DedupeAlignCos)
                continue;
            var midX = (other.X0 + other.X1) / 2;
            var midY = (other.Y0 + other.Y1) / 2;
            var lat = Math.Abs((midX - axis.X0) * -auy + (midY - axis.Y0) * aux);
            if (lat <= BeamCenterlineReader.NearDuplicateMidlineMm + 50)
                continue; // near-duplicate outline of the same bay (500 vs 1000) — not corroboration
            if (lat > Math.Max(axis.ThicknessMm, other.ThicknessMm) / 2 + 2000)
                continue; // far parallel — different zone
            const double near = 150;
            if (Hypot(other.X0 - jx, other.Y0 - jy) <= near || Hypot(other.X1 - jx, other.Y1 - jy) <= near)
                return true;
        }

        return false;
    }

    private static bool ReferenceEqualsRun(WallRun a, WallRun b) =>
        Math.Abs(a.X0 - b.X0) < 1e-6 && Math.Abs(a.Y0 - b.Y0) < 1e-6 &&
        Math.Abs(a.X1 - b.X1) < 1e-6 && Math.Abs(a.Y1 - b.Y1) < 1e-6;

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    /// <summary>
    /// When two planned segments share an axis (≤2°, lateral ≤ max(t)/2+30 or ≤300 on thickness
    /// mismatch, overlap ≥ 70%), keep one: Mark-WxH first (thickness match preferred), else larger
    /// section, else longer, else Main over Secondary.
    /// </summary>
    internal static void DeduplicateOverlappingBeams(
        IReadOnlyList<BeamSegment> planned,
        BeamLabel[] labels,
        string[] sources,
        bool[] keep,
        Action<string>? log)
    {
        for (var i = 0; i < planned.Count; i++)
        {
            if (!keep[i])
                continue;
            for (var j = i + 1; j < planned.Count; j++)
            {
                if (!keep[j])
                    continue;
                if (!BeamCenterlineReader.OverlappingBay(planned[i].Run, planned[j].Run, out var lat, out var ratio))
                    continue;
                var ownI = OwnSizeLabel(labels[i], sources[i]);
                var ownJ = OwnSizeLabel(labels[j], sources[j]);
                var log0 = $"a={labels[i].Raw}|{planned[i].Run.ThicknessMm:0} b={labels[j].Raw}|{planned[j].Run.ThicknessMm:0}";
                int drop;
                if (ownI != ownJ)
                {
                    // Size text on this bay beats Mark-WxH inherited from a neighbour on the axis: rank both
                    // pairs with the own label and give it to the survivor.
                    var own = ownI ? i : j;
                    var other = own == i ? j : i;
                    var label = labels[own].HasMark
                        ? labels[own]
                        : new BeamLabel(labels[other].Mark, labels[own].WidthMm, labels[own].DepthMm, labels[own].Raw);
                    var scoreI = BeamPreferScore(planned[i], label);
                    var scoreJ = BeamPreferScore(planned[j], label);
                    var survivor = scoreI != scoreJ
                        ? (scoreI > scoreJ ? i : j)
                        : (WidthMismatchMm(planned[i], label) <= WidthMismatchMm(planned[j], label) ? i : j);
                    drop = survivor == i ? j : i;
                    labels[survivor] = label;
                    if (survivor != own)
                        sources[survivor] = $"dedupeOwn={sources[own]}";
                }
                else
                {
                    drop = PreferBeamSegment(planned, labels, i, j) == i ? j : i;
                    var survivor = drop == i ? j : i;
                    // Same size: a Mark-WxH text on the dropped pair beats a Mark inherited along the axis.
                    if (OwnMarkLabel(labels[drop], sources[drop]) && labels[drop].HasSize &&
                        !OwnMarkLabel(labels[survivor], sources[survivor]) &&
                        labels[drop].WidthMm == labels[survivor].WidthMm && labels[drop].DepthMm == labels[survivor].DepthMm)
                    {
                        labels[survivor] = new BeamLabel(
                            labels[drop].Mark, labels[survivor].WidthMm, labels[survivor].DepthMm, labels[drop].Raw);
                        sources[survivor] = $"dedupeOwn={sources[drop]}";
                    }
                }

                keep[drop] = false;
                log?.Invoke(
                    $"beam dedupe drop seg {drop} keep {(drop == i ? j : i)} " +
                    $"lat={lat:0} overlap={ratio:0.00} {log0}" +
                    (ownI != ownJ ? $" own={(ownI ? i : j)}" : ""));
            }
        }
    }

    private static double WidthMismatchMm(BeamSegment seg, BeamLabel label) =>
        label.WidthMm is int w ? Math.Abs(w - seg.Run.ThicknessMm) : 0;

    /// <summary>Size read from text on this segment (axisMark only adds a Mark; inherit= copies the size too).</summary>
    private static bool OwnSizeLabel(BeamLabel label, string source) =>
        label.HasSize && !source.StartsWith("inherit=", StringComparison.Ordinal);

    /// <summary>Mark read from text on this segment, not inherited along the axis.</summary>
    private static bool OwnMarkLabel(BeamLabel label, string source) =>
        label.HasMark &&
        !source.StartsWith("inherit=", StringComparison.Ordinal) &&
        !source.StartsWith("axisMark=", StringComparison.Ordinal);

    /// <summary>Index of the segment to keep when i and j overlap on the same bay.</summary>
    private static int PreferBeamSegment(
        IReadOnlyList<BeamSegment> planned, BeamLabel[] labels, int i, int j)
    {
        var si = BeamPreferScore(planned[i], labels[i]);
        var sj = BeamPreferScore(planned[j], labels[j]);
        if (si != sj)
            return si > sj ? i : j;
        var li = Math.Sqrt(
            (planned[i].Run.X1 - planned[i].Run.X0) * (planned[i].Run.X1 - planned[i].Run.X0) +
            (planned[i].Run.Y1 - planned[i].Run.Y0) * (planned[i].Run.Y1 - planned[i].Run.Y0));
        var lj = Math.Sqrt(
            (planned[j].Run.X1 - planned[j].Run.X0) * (planned[j].Run.X1 - planned[j].Run.X0) +
            (planned[j].Run.Y1 - planned[j].Run.Y0) * (planned[j].Run.Y1 - planned[j].Run.Y0));
        if (Math.Abs(li - lj) > 1)
            return li > lj ? i : j;
        return planned[i].Kind == BeamSpanKind.Main ? i : j;
    }

    private static long BeamPreferScore(BeamSegment seg, BeamLabel label)
    {
        var score = 0L;
        if (label.HasMark && label.HasSize)
            score += 1_000_000_000;
        else if (label.HasSize)
            score += 100_000_000;
        else if (label.HasMark)
            score += 10_000_000;
        if (label.HasSize &&
            Math.Abs(label.WidthMm!.Value - seg.Run.ThicknessMm) <= BeamWidthTolMm)
            score += 50_000_000;
        var w = label.WidthMm ?? (int)Math.Round(seg.Run.ThicknessMm);
        var d = label.DepthMm ?? w;
        score += (long)w * d;
        return score;
    }

    private static string FamilyFromTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return "";
        var sep = typeName.IndexOf(':');
        return sep > 0 ? typeName.Substring(0, sep).Trim() : "";
    }

    /// <summary>Text middle may sit this far past a segment end and still belong to it.</summary>
    private const double BeamLabelAlongPadMm = 50;
    /// <summary>Text beside the beam: its middle may sit this far past the beam face (large text below/above).</summary>
    private const double BeamLabelAcrossMm = 600;
    /// <summary>Text not parallel to the beam counts only when it sits on the beam body (half width + this).</summary>
    private const double BeamLabelBodyPadMm = 100;
    private const double BeamLabelAlignDeg = 10;
    /// <summary>CAD face spacing vs label width before a face pair is ranked as "not this beam".</summary>
    private const double BeamWidthTolMm = 50;
    /// <summary>
    /// Tighter than <see cref="BeamWidthTolMm"/> for face-pair claim order: a hatch face 50 mm inside a
    /// true 500 outline (t=450) must not outrank the real 500 pair (legacy ≤50 matched both → thinner won).
    /// </summary>
    private const double BeamPairMatchTolMm = 25;
    /// <summary>Fallback CAD walls are read per layer; larger layers (hatches, details) are skipped.</summary>
    private const int CadWallLayerMaxCurves = 1500;
    /// <summary>CAD column outlines used as beam supports; drops grid bubbles / detail boxes.</summary>
    private const double CadSupportMinSideMm = 150;

    private const double OneFaceLabelReachMm = 800;

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

    /// <summary>Width of the nearest sized label written along <paramref name="face"/>; 0 when none is in reach.</summary>
    private static double OneFaceLabelWidth(List<BeamText> texts, WallRun face)
    {
        var len = Math.Sqrt((face.X1 - face.X0) * (face.X1 - face.X0) + (face.Y1 - face.Y0) * (face.Y1 - face.Y0));
        if (len < 1)
            return 0;
        var ux = (face.X1 - face.X0) / len;
        var uy = (face.Y1 - face.Y0) / len;
        var best = 0.0;
        var bestAcross = OneFaceLabelReachMm;
        foreach (var text in texts)
        {
            if (!text.Label.HasSize || Math.Abs(text.DirX * ux + text.DirY * uy) < 0.95)
                continue;
            var along = (text.X - face.X0) * ux + (text.Y - face.Y0) * uy;
            var across = Math.Abs((text.X - face.X0) * -uy + (text.Y - face.Y0) * ux);
            if (along < 0 || along > len || across >= bestAcross)
                continue;
            bestAcross = across;
            best = text.Label.WidthMm!.Value;
        }

        return best;
    }

    /// <summary>Where the size came from (own text or a neighbour) survives when only the Mark comes from the axis.</summary>
    private static string AxisMarkSource(string source, string axisMark) =>
        source.StartsWith("inherit=", StringComparison.Ordinal) ||
        (source.StartsWith("cands=", StringComparison.Ordinal) && !source.StartsWith("cands=0", StringComparison.Ordinal))
            ? $"{source} {axisMark}"
            : axisMark;

    /// <summary>
    /// Layer holding most of each segment's faces. Beam groups live on their own layers (D1… on 3MBKC, DG… on 4MBKC),
    /// so a Mark or section only travels along an axis within one layer. "" = no face found (one-face or wall-top run).
    /// </summary>
    private static string[] SegmentLayers(IReadOnlyList<BeamSegment> planned, List<CadEntity> entities)
    {
        var groups = entities.GroupBy(e => FlatLayer(e.Layer), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Layer: g.Key, Curves: (IReadOnlyList<CadEntity>)g.ToList()))
            .ToList();
        var result = new string[planned.Count];
        for (var i = 0; i < planned.Count; i++)
        {
            result[i] = "";
            var best = 0.0;
            foreach (var (layer, curves) in groups)
            {
                var length = groups.Count > 1 ? BeamCenterlineReader.FaceLengthOnRun(planned[i].Run, curves) : 1;
                if (length <= best)
                    continue;
                best = length;
                result[i] = layer;
            }
        }

        return result;
    }

    private static bool SameBeamGroup(string[] layers, int a, int b) =>
        layers[a].Length == 0 || layers[b].Length == 0 ||
        string.Equals(layers[a], layers[b], StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A label written beside the beam decides its section, but one inherited along the axis belongs to another beam
    /// when the CAD faces are far from it (DGX14 100x60 ran on over a 500 wide bay): that bay keeps its CAD width.
    /// </summary>
    private static void KeepCadWidthOverForeignLabel(
        IReadOnlyList<BeamSegment> planned, BeamLabel[] labels, string[] sources, Action<string>? log)
    {
        for (var i = 0; i < planned.Count; i++)
        {
            var thickness = planned[i].Run.ThicknessMm;
            if (sources[i].StartsWith("cands=", StringComparison.Ordinal) || !labels[i].HasSize || thickness <= 0)
                continue;
            double width = labels[i].WidthMm!.Value;
            if (Math.Max(width, thickness) < ForeignLabelWidthRatio * Math.Min(width, thickness))
                continue;
            // The same outline as the beam the label was written on: one beam, drawn off its label throughout.
            var label = labels[i];
            var sameOutline = Enumerable.Range(0, planned.Count).Any(j =>
                sources[j].StartsWith("cands=", StringComparison.Ordinal) && labels[j].WidthMm == label.WidthMm &&
                string.Equals(labels[j].Mark, label.Mark, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(planned[j].Run.ThicknessMm - thickness) <= BeamWidthTolMm);
            if (sameOutline)
                continue;
            log?.Invoke($"beam width seg {i} CAD {thickness:0} over inherited {width:0} ({labels[i].Raw})");
            labels[i] = new BeamLabel(null, (int)Math.Round(thickness), labels[i].DepthMm, labels[i].Raw);
        }
    }

    private const double ForeignLabelWidthRatio = 1.5;

    /// <summary>A span with its own label off the axis width follows its own CAD lines, the width stays the label's.</summary>
    private static void CenterOwnLabelsOnDrawnFaces(
        List<BeamSegment> planned, BeamLabel[] labels, string[] sources, List<CadEntity> drawing, Action<string>? log)
    {
        for (var i = 0; i < planned.Count; i++)
        {
            var run = planned[i].Run;
            if (!labels[i].HasSize || Math.Abs(labels[i].WidthMm!.Value - run.ThicknessMm) < 1 ||
                !sources[i].StartsWith("cands=", StringComparison.Ordinal) ||
                sources[i].StartsWith("cands=0", StringComparison.Ordinal))
                continue;
            var moved = BeamCenterlineReader.CenterOnDrawnFaces(run, labels[i].WidthMm!.Value, drawing);
            var shift = Math.Sqrt((moved.X0 - run.X0) * (moved.X0 - run.X0) + (moved.Y0 - run.Y0) * (moved.Y0 - run.Y0));
            if (shift < 1)
                continue;
            log?.Invoke($"beam drawn faces seg {i} moved {shift:0} ({labels[i].Raw})");
            planned[i] = new BeamSegment(moved, planned[i].Kind);
        }
    }

    /// <summary>A sized label within reach matches this pair width, so the pair is one real beam.</summary>
    private static bool PairHasOwnLabel(List<BeamText> texts, WallRun run) =>
        texts.Any(t => t.Label.HasSize && Math.Abs(t.Label.WidthMm!.Value - run.ThicknessMm) <= BeamPairMatchTolMm &&
                       LabelDistance(t, run) is not null);

    /// <summary>
    /// A bound xref nested in itself doubles the layer prefix ("Plans$0$Plans$0$04.HIDDEN"): that is the same layer
    /// as "Plans$0$04.HIDDEN", and one drawing can hold both (floor 2 had most beams on the doubled name).
    /// </summary>
    private static bool SameLayer(string? a, string? b) =>
        string.Equals(FlatLayer(a), FlatLayer(b), StringComparison.OrdinalIgnoreCase);

    private static string FlatLayer(string? layer)
    {
        var parts = (layer ?? "").Split(new[] { "$0$" }, StringSplitOptions.None);
        var flat = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (flat.Count == 0 || !string.Equals(flat[flat.Count - 1], part, StringComparison.OrdinalIgnoreCase))
                flat.Add(part);
        }

        return string.Join("$0$", flat);
    }

    /// <summary>Grid bubbles ("F", "C5") read as size-less marks; a beam mark has more to it (D1PY6, DGX1).</summary>
    private static bool IsGridBubble(BeamLabel label) =>
        !label.HasSize && Regex.IsMatch(label.Mark ?? "", @"^[A-Za-z]\d?$");

    /// <summary>
    /// One plan can draw its beam groups on separate layers (D1… on 3MBKC, DG… on 4MBKC). Another layer of the drawing
    /// joins the picked one when several long face pairs on it carry their own WxH label and are not supports.
    /// </summary>
    private static List<CadEntity> WithLabelledBeamLayers(
        CadGeometry geom, List<CadEntity> entities, List<BeamText> texts, ModelRevitPlacement place)
    {
        var picked = entities.Count > 0 ? FlatLayer(entities[0].Layer) : "";
        var result = new List<CadEntity>(entities);
        foreach (var group in SameDrawing(geom, entities).GroupBy(e => FlatLayer(e.Layer), StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(group.Key, picked, StringComparison.OrdinalIgnoreCase))
                continue;
            var curves = group.ToList();
            var runs = BeamCenterlineReader.FromEntities(curves);
            var labelled = runs.Count(r =>
                r.ThicknessMm > 0 && RunLength(r) >= LabelledLayerMinRunMm && PairHasOwnLabel(texts, r) &&
                !(place.Supports?.Any(s => InFootprint(s, (r.X0 + r.X1) / 2, (r.Y0 + r.Y1) / 2)) ?? false));
            if (labelled < LabelledLayerMinRuns)
                continue;
            place.Log?.Invoke($"beam layer + {group.Key} labelled runs={labelled}/{runs.Count}");
            result.AddRange(curves);
        }

        return result;
    }

    private static bool InFootprint(SectionFootprint s, double x, double y)
    {
        var rad = s.RotationDeg * Math.PI / 180.0;
        var dx = x - s.CenterX;
        var dy = y - s.CenterY;
        return Math.Abs(dx * Math.Cos(rad) + dy * Math.Sin(rad)) <= s.WidthMm / 2 &&
               Math.Abs(-dx * Math.Sin(rad) + dy * Math.Cos(rad)) <= s.DepthMm / 2;
    }

    private static double RunLength(WallRun r) =>
        Math.Sqrt((r.X1 - r.X0) * (r.X1 - r.X0) + (r.Y1 - r.Y0) * (r.Y1 - r.Y0));

    private const int LabelledLayerMinRuns = 5;
    private const double LabelledLayerMinRunMm = 2000;

    /// <summary>Curves from the same xref as the beam layer ("Structural Plans$0$…"), or the whole drawing.</summary>
    private static List<CadEntity> SameDrawing(CadGeometry geom, List<CadEntity> layerEntities)
    {
        var layer = layerEntities.Count > 0 ? layerEntities[0].Layer ?? "" : "";
        var cut = layer.LastIndexOf('$');
        var prefix = cut > 0 ? layer.Substring(0, cut) : "";
        return geom.Entities
            .Where(e => e is CadLine or CadPolyline && (e.Layer ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        !Regex.IsMatch(e.Layer ?? "", @"(^|[$ .-])(TRUC|AXIS|GRID|DIM)[^$]*$", RegexOptions.IgnoreCase))
            .ToList();
    }

    /// <summary>An unlabelled secondary leg of a U/L beam takes Mark-WxH from the same-width labelled beam at its corner.</summary>
    private static void InheritCornerLabels(
        IReadOnlyList<BeamSegment> planned, BeamLabel[] labels, string[] sources, Action<string>? log)
    {
        static double Dist(double x0, double y0, double x1, double y1) =>
            Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var given = labels.ToArray();
        for (var i = 0; i < planned.Count; i++)
        {
            if (planned[i].Kind != BeamSpanKind.Secondary || given[i].HasSize)
                continue;
            var a = planned[i].Run;
            for (var j = 0; j < planned.Count; j++)
            {
                var b = planned[j].Run;
                if (i == j || !given[j].HasMark || !given[j].HasSize ||
                    given[j].WidthMm != (int)Math.Round(a.ThicknessMm))
                    continue;
                var reach = a.ThicknessMm;
                if (Dist(a.X0, a.Y0, b.X0, b.Y0) > reach && Dist(a.X0, a.Y0, b.X1, b.Y1) > reach &&
                    Dist(a.X1, a.Y1, b.X0, b.Y0) > reach && Dist(a.X1, a.Y1, b.X1, b.Y1) > reach)
                    continue;
                labels[i] = given[j];
                sources[i] = $"inherit={given[j].Raw}@seg{j}";
                log?.Invoke($"beam corner seg {i} ← seg {j} {given[j].Raw}");
                break;
            }
        }
    }

    private static List<BeamText> BeamTexts(CadGeometry geom)
    {
        var result = new List<BeamText>();
        foreach (var text in geom.Entities.OfType<CadText>())
        {
            if (!BeamLabelParser.TryParse(text.Value, out var parsed) || IsGridBubble(parsed))
                continue;
            var rad = text.RotationDeg * Math.PI / 180.0;
            result.Add(new BeamText(text.CenterX ?? text.X, text.CenterY ?? text.Y, Math.Cos(rad), Math.Sin(rad), parsed));
        }

        return result;
    }

    /// <summary>
    /// Fallback supports from CAD: L/b ≤ 4 column outlines (min side ≥ 150 mm — drops grid bubbles),
    /// plus elongated wall pairs (L/b &gt; 4, thickness ≥ 150) on non-beam layers.
    /// Beam-layer curves are excluded (their face boxes were false mid-span supports).
    /// When grids exist, keep only footprints near a grid point so hatch/detail noise drops.
    /// </summary>
    private static List<SectionFootprint> CadSupports(
        CadGeometry geom, List<CadEntity> beamLayer, IReadOnlyList<CadPoint2>? gridPoints)
    {
        var beam = new HashSet<CadEntity>(beamLayer);
        var nonBeam = geom.Entities.Where(e => !beam.Contains(e)).ToList();
        var supports = new List<SectionFootprint>();
        foreach (var fp in SectionFootprintReader.FromEntities(nonBeam))
        {
            if (Math.Min(fp.WidthMm, fp.DepthMm) >= CadSupportMinSideMm)
                supports.Add(fp);
        }

        var layers = nonBeam
            .Where(e => e is CadLine or CadPolyline)
            .GroupBy(e => e.Layer, StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers)
        {
            var curves = layer.ToList();
            if (curves.Count > CadWallLayerMaxCurves)
                continue;
            foreach (var wall in WallCenterlineReader.FromEntities(curves, wallFilter: true))
            {
                // Structural walls only (L/b > 4). Beam-like pairs on other layers must not fake column supports.
                if (wall.ThicknessMm >= 150)
                    supports.Add(BeamCenterlineReader.Body(wall));
            }
        }

        if (gridPoints is { Count: > 0 })
        {
            supports = supports.Where(fp => NearGridSupport(fp, gridPoints)).ToList();
        }

        return supports;
    }

    /// <summary>CAD column/wall outline is a support only when its centre sits near a grid intersection.</summary>
    private const double CadSupportGridTolMm = 900;

    private static bool NearGridSupport(SectionFootprint fp, IReadOnlyList<CadPoint2> grids)
    {
        foreach (var g in grids)
        {
            var dx = fp.CenterX - g.X;
            var dy = fp.CenterY - g.Y;
            if (dx * dx + dy * dy <= CadSupportGridTolMm * CadSupportGridTolMm)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Keep CAD footprints that sit on ≥2 non-parallel beam runs (grid joints). Lone mid-span
    /// hatches / section boxes otherwise split every main into fragments.
    /// </summary>
    private static List<SectionFootprint> FilterCadSupports(
        IReadOnlyList<SectionFootprint> supports, IReadOnlyList<WallRun> beamRuns)
    {
        if (supports.Count == 0 || beamRuns.Count == 0)
            return supports.ToList();

        var kept = new List<SectionFootprint>();
        foreach (var support in supports)
        {
            var hit = new List<(double Ux, double Uy)>();
            foreach (var run in beamRuns)
            {
                var len = Math.Sqrt(
                    (run.X1 - run.X0) * (run.X1 - run.X0) + (run.Y1 - run.Y0) * (run.Y1 - run.Y0));
                if (len < BeamCenterlineReader.MinLengthMm)
                    continue;
                var ux = (run.X1 - run.X0) / len;
                var uy = (run.Y1 - run.Y0) / len;
                if (!SupportOnRun(run, ux, uy, len, support))
                    continue;
                if (hit.Any(h => Math.Abs(h.Ux * ux + h.Uy * uy) >= 0.95))
                    continue;
                hit.Add((ux, uy));
                if (hit.Count >= 2)
                    break;
            }

            if (hit.Count >= 2)
                kept.Add(support);
        }

        // If filtering wiped everything (sparse beam set), fall back to raw list rather than
        // classifying every axis as secondary with no supports at all.
        return kept.Count > 0 ? kept : supports.ToList();
    }

    private static bool SupportOnRun(WallRun run, double ux, double uy, double len, SectionFootprint support)
    {
        var dx = support.CenterX - run.X0;
        var dy = support.CenterY - run.Y0;
        var t = dx * ux + dy * uy;
        var across = Math.Abs(dx * -uy + dy * ux);
        var half = Math.Max(Math.Min(support.WidthMm, support.DepthMm) / 2, run.ThicknessMm / 2) +
                   BeamCenterlineReader.ColumnLateralTolMm;
        return across <= half && t >= -BeamCenterlineReader.ExtendMaxMm && t <= len + BeamCenterlineReader.ExtendMaxMm;
    }

    /// <summary>
    /// Lateral distance from the run to a text that labels it, or null. The text middle must project onto the span;
    /// parallel text (±10°, either reading direction) may sit beside the beam up to half width + 600;
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
            return across <= run.ThicknessMm / 2 + BeamLabelAcrossMm ? across : null;
        return across <= Math.Max(run.ThicknessMm / 2, 50) + BeamLabelBodyPadMm ? across + BeamLabelAcrossMm : null;
    }

    private static double? LabelDistance(BeamText text, WallRun run)
    {
        var len = Math.Sqrt((run.X1 - run.X0) * (run.X1 - run.X0) + (run.Y1 - run.Y0) * (run.Y1 - run.Y0));
        return len < 1 ? null : LabelDistance(text, run, (run.X1 - run.X0) / len, (run.Y1 - run.Y0) / len, len);
    }

    /// <summary>
    /// Cost for "which segment owns this text": lateral distance, plus a large penalty when WxH width
    /// disagrees with the face-pair thickness (so D1Y7-100x65 prefers the 1000 run over a parallel 500).
    /// </summary>
    private static double LabelNearestCost(BeamText text, WallRun run)
    {
        if (LabelDistance(text, run) is not double d)
            return double.MaxValue;
        if (text.Label.HasSize &&
            Math.Abs(text.Label.WidthMm!.Value - run.ThicknessMm) > BeamWidthTolMm)
            return d + 10_000;
        return d;
    }

    /// <summary>Along-axis distance from the text projection to the segment midpoint (tie-break).</summary>
    private static double LabelMidpointAlong(BeamText text, WallRun run, double ux, double uy, double len)
    {
        var along = (text.X - run.X0) * ux + (text.Y - run.Y0) * uy;
        return Math.Abs(along - len / 2);
    }

    /// <summary>
    /// Face-pair preference for beams (lower wins).
    /// Matching WxH: <c>1000*|t−W| + 50*|across|</c> — exact mid-of-faces near the label beats a 525
    /// true+ghost cross-pair and a parallel ghost outline. A wall-hatch hybrid that matches W but sits
    /// far from the text (e.g. faces 200/500 for a 300 label at y=250) loses to the wider outline that
    /// hosts the text on center (<c>soft</c> band). Unlabeled = 500_000; disagree-only = 1_000_000.
    /// </summary>
    private static int BeamPairRank(List<BeamText> texts, WallRun run)
    {
        var len = Math.Sqrt((run.X1 - run.X0) * (run.X1 - run.X0) + (run.Y1 - run.Y0) * (run.Y1 - run.Y0));
        if (len < 1)
            return 500_000;
        var ux = (run.X1 - run.X0) / len;
        var uy = (run.Y1 - run.Y0) / len;
        var labelled = false;
        var best = int.MaxValue;
        foreach (var text in texts)
        {
            if (!text.Label.HasSize || LabelDistance(text, run, ux, uy, len) is not double d)
                continue;
            var delta = Math.Abs(text.Label.WidthMm!.Value - run.ThicknessMm);
            if (delta <= BeamPairMatchTolMm)
            {
                // Across dominates over small thickness slop so mid-of-label outlines win.
                // Mark-WxH beats size-only on the same bay (D1Y7-100x65 over bare 50x65 → keep 1000).
                var markBias = text.Label.HasMark ? 0 : 10_000;
                var score = (int)Math.Round(delta) * 1000 + (int)Math.Min(9999, Math.Round(d * 50)) + markBias;
                if (score < best)
                    best = score;
            }
            else if (d <= Math.Max(30, run.ThicknessMm * 0.15) &&
                     delta <= Math.Max(BeamWidthTolMm * 4, run.ThicknessMm))
            {
                // Text sits on this midline but W disagrees (beam outline 500, label 30x70 / wall 200):
                // keep as a candidate behind true matches with low across, ahead of unlabeled hatch.
                labelled = true;
                var soft = 3_000 + (int)Math.Round(delta) + (int)Math.Min(99, Math.Round(d));
                if (soft < best)
                    best = soft;
            }
            else
            {
                labelled = true;
            }
        }

        if (best != int.MaxValue)
            return best;
        return labelled ? 1_000_000 : 500_000;
    }

    /// <summary>
    /// Nearest CadText whose projection falls on this final segment. Prefer combined Mark-WxH
    /// (closer to midpoint wins ties); never use a text whose projection is outside the segment.
    /// Size-only loses to Mark-WxH on the same bay.
    /// </summary>
    private static BeamLabel MatchBeamLabel(List<BeamText> texts, List<double> nearest, WallRun run, out string debug)
    {
        debug = "cands=0";
        var dx = run.X1 - run.X0;
        var dy = run.Y1 - run.Y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
            return default;
        var ux = dx / len;
        var uy = dy / len;

        BeamLabel bestCombined = default;
        var bestCombinedD = double.MaxValue;
        var bestCombinedMid = double.MaxValue;
        BeamLabel bestSize = default;
        var bestSizeD = double.MaxValue;
        var bestSizeMid = double.MaxValue;
        BeamLabel bestSizeMatch = default;
        var bestSizeMatchD = double.MaxValue;
        var bestSizeMatchMid = double.MaxValue;
        BeamLabel bestMark = default;
        var bestMarkD = double.MaxValue;
        var bestMarkMid = double.MaxValue;
        var candidates = 0;
        for (var k = 0; k < texts.Count; k++)
        {
            var text = texts[k];
            if (LabelDistance(text, run, ux, uy, len) is not double d)
                continue;
            var cost = LabelNearestCost(text, run);
            if (cost > nearest[k] + 1)
                continue;
            candidates++;
            var mid = LabelMidpointAlong(text, run, ux, uy, len);
            bool Better(double bestD, double bestMid) =>
                d < bestD - 1e-6 || (Math.Abs(d - bestD) <= 1e-6 && mid < bestMid);

            if (text.Label.HasMark && text.Label.HasSize && Better(bestCombinedD, bestCombinedMid))
            {
                bestCombinedD = d;
                bestCombinedMid = mid;
                bestCombined = text.Label;
            }

            if (text.Label.HasSize && Better(bestSizeD, bestSizeMid))
            {
                bestSizeD = d;
                bestSizeMid = mid;
                bestSize = text.Label;
            }

            if (text.Label.HasSize &&
                Math.Abs(text.Label.WidthMm!.Value - run.ThicknessMm) <= BeamWidthTolMm &&
                Better(bestSizeMatchD, bestSizeMatchMid))
            {
                bestSizeMatchD = d;
                bestSizeMatchMid = mid;
                bestSizeMatch = text.Label;
            }

            if (text.Label.HasMark && Better(bestMarkD, bestMarkMid))
            {
                bestMarkD = d;
                bestMarkMid = mid;
                bestMark = text.Label;
            }
        }

        // Prefer Mark-WxH when its width matches this face pair. If nothing matches thickness,
        // still keep the nearest Mark-WxH on this segment (CAD face width may be wrong) —
        // but never when a thickness-matching size exists (that wins instead).
        if (bestCombined.HasSize)
        {
            var combinedMatches = run.ThicknessMm <= 0 ||
                Math.Abs(bestCombined.WidthMm!.Value - run.ThicknessMm) <= BeamWidthTolMm;
            if (combinedMatches || !bestSizeMatch.HasSize)
            {
                debug = $"cands={candidates} text={bestCombined.Raw}@{bestCombinedD:0} markText={bestCombined.Raw}@{bestCombinedD:0}";
                return bestCombined;
            }
        }

        // Competing size-only vs Mark-WxH on a mismatched width: keep the thickness-matching size.
        if (bestSizeMatch.HasSize)
        {
            debug = $"cands={candidates} text={bestSizeMatch.Raw}@{bestSizeMatchD:0}" +
                    $" markText={(bestMark.HasMark ? $"{bestMark.Raw}@{bestMarkD:0}" : "-")}";
            // Only attach a foreign Mark when the size text itself has no Mark (pure WxH).
            if (bestMark.HasMark && !bestSizeMatch.HasMark)
                return new BeamLabel(bestMark.Mark, bestSizeMatch.WidthMm, bestSizeMatch.DepthMm,
                    bestMark.Raw + "+" + bestSizeMatch.Raw);
            return bestSizeMatch;
        }

        debug = $"cands={candidates} text={(bestSize.HasSize ? $"{bestSize.Raw}@{bestSizeD:0}" : "-")}" +
                $" markText={(bestMark.HasMark ? $"{bestMark.Raw}@{bestMarkD:0}" : "-")}";
        if (bestSize.HasSize && bestMark.HasMark)
        {
            // Never mix Mark from one Mark-WxH with size from a different Mark-WxH
            // (e.g. D1Y-3 (50x70) Mark + GTC3(30x40) size → 300x400 with wrong Mark).
            if (bestSize.HasMark &&
                !string.Equals(bestSize.Mark, bestMark.Mark, StringComparison.OrdinalIgnoreCase))
            {
                if (bestMark.HasSize)
                    return bestMark;
                return bestSize;
            }

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

    /// <summary>
    /// Point footings / pile caps / rafts from closed CAD outlines (and inserts).
    /// XY 5 mm vs grids like columns/piles. Isolated: plan size from CAD outline
    /// (<paramref name="resizeFromCad"/>). PileCap: Type Mark = nearby CAD label (no WxH invent).
    /// Raft keeps UI type size. Instance Mark when AssignMark.
    /// </summary>
    private static IReadOnlyList<CreateOperation> PlanFoundations(
        CadGeometry geom,
        List<CadEntity> entities,
        string baseLevel,
        AnnotationSize size,
        string foundationKind,
        ModelRevitPlacement place,
        IReadOnlyList<CadPoint2>? gridPoints,
        bool resizeFromCad,
        bool resolveByTypeMark)
    {
        var uiType = FirstNonEmpty(place.TypeName, place.FamilyName, size.Raw);
        SplitFamilyType(uiType, out var sourceFamily, out _);
        if (string.IsNullOrWhiteSpace(sourceFamily))
            sourceFamily = place.FamilyName ?? "";
        var ops = new List<CreateOperation>();
        var placed = new List<(double X, double Y)>();

        foreach (var footprint in SectionFootprintReader.FromEntitiesFoundation(entities))
        {
            var x = footprint.CenterX;
            var y = footprint.CenterY;
            var rot = footprint.RotationDeg;
            var (ox, oy) = NearestGridPoint(x, y, gridPoints);
            (x, y) = RoundColumnPosition(x, y, 0, ox, oy);
            var (gx, gy, snapped) = SnapToGrid(x, y, gridPoints);
            if (snapped && OnRoundModule(gx, gy, 0, ox, oy))
            {
                x = gx;
                y = gy;
            }

            if (!TryAddFoundationOp(
                    ops, geom, place, baseLevel, foundationKind, sourceFamily, uiType,
                    x, y, footprint.WidthMm, footprint.DepthMm, rot, resizeFromCad, resolveByTypeMark))
                continue;

            placed.Add((x, y));
        }

        foreach (var entity in entities)
        {
            if (entity is CadCircle)
                continue;
            if (entity is CadPolyline pl && pl.Points.Count >= 3 &&
                (pl.Closed || SectionFootprintReaderNearlyClosed(pl)))
                continue;
            if (!TryPoint(entity, out var x, out var y))
                continue;
            if (NearPlacedPile(placed, x, y))
                continue;

            var (ox, oy) = NearestGridPoint(x, y, gridPoints);
            (x, y) = RoundColumnPosition(x, y, 0, ox, oy);
            var (gx, gy, snapped) = SnapToGrid(x, y, gridPoints);
            if (snapped && OnRoundModule(gx, gy, 0, ox, oy))
            {
                x = gx;
                y = gy;
            }

            if (!TryAddFoundationOp(
                    ops, geom, place, baseLevel, foundationKind, sourceFamily, uiType,
                    x, y, widthMm: 0, depthMm: 0, rotationDeg: 0, resizeFromCad: false, resolveByTypeMark))
                continue;

            placed.Add((x, y));
        }

        return ops;
    }

    /// <summary>Mirror of foundation nearly-closed check without exposing reader private API.</summary>
    private static bool SectionFootprintReaderNearlyClosed(CadPolyline pl)
    {
        if (pl.Points.Count < 3)
            return false;
        var a = pl.Points[0];
        var b = pl.Points[pl.Points.Count - 1];
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy <= 25.0;
    }

    /// <summary>
    /// Build one foundation create op. PileCap (resolveByTypeMark): CAD label → Type Mark;
    /// miss skips (no WxH invent). No pile-cap label → UI type only when no nearby CAD text at all.
    /// Nearby beam/grid text without a Đ* id → skip (do not place UI fallback).
    /// </summary>
    private static bool TryAddFoundationOp(
        List<CreateOperation> ops,
        CadGeometry geom,
        ModelRevitPlacement place,
        string baseLevel,
        string foundationKind,
        string sourceFamily,
        string uiType,
        double x,
        double y,
        double widthMm,
        double depthMm,
        double rotationDeg,
        bool resizeFromCad,
        bool resolveByTypeMark)
    {
        if (resolveByTypeMark)
        {
            var (label, kind) = NearestPileCapLabel(geom, x, y);
            var instanceMark = place.AssignMark && kind == "pilecap" ? label : "";

            if (kind == "pilecap")
            {
                string typeMark;
                if (place.FoundationTypeMarks is { Count: > 0 })
                {
                    if (!FoundationCatalogFilter.TryMatchTypeMark(label, place.FoundationTypeMarks, out typeMark))
                    {
                        place.Log?.Invoke(
                            $"pilecap skip: no Type Mark match for '{label}' at ({x:0},{y:0}) rot={rotationDeg:0.#}");
                        return false;
                    }
                }
                else
                {
                    typeMark = label;
                }

                place.Log?.Invoke(
                    $"pilecap place: mark={typeMark} xyzMm=({x:0.###},{y:0.###}) rotationDeg={rotationDeg:0.##}");
                ops.Add(Op("StructuralFoundation", new
                {
                    xyzMm = new[] { x, y, 0 },
                    familyType = typeMark,
                    sourceFamily,
                    typeMark,
                    resolveByTypeMark = true,
                    rotationDeg,
                    level = baseLevel,
                    baseOffsetMm = place.BaseOffsetMm,
                    offsetMm = place.BaseOffsetMm,
                    material = place.Material ?? "",
                    kind = foundationKind,
                    mark = instanceMark,
                    exactMm = true
                }, skipIfExists: true));
                return true;
            }

            if (kind == "other")
            {
                // Beam/grid/misc text nearer than any Đ* — do not invent UI type here.
                place.Log?.Invoke(
                    $"pilecap skip: no pile-cap Type Mark label near ({x:0},{y:0}); nearest '{label}'");
                return false;
            }

            // No CAD label at all: UI-selected type is family/size fallback.
            if (string.IsNullOrWhiteSpace(uiType))
            {
                place.Log?.Invoke($"pilecap skip: no CAD Type Mark label and no UI type at ({x:0},{y:0})");
                return false;
            }

            ops.Add(Op("StructuralFoundation", new
            {
                xyzMm = new[] { x, y, 0 },
                familyType = uiType,
                sourceFamily,
                rotationDeg,
                level = baseLevel,
                baseOffsetMm = place.BaseOffsetMm,
                offsetMm = place.BaseOffsetMm,
                material = place.Material ?? "",
                kind = foundationKind,
                mark = instanceMark,
                exactMm = true
            }, skipIfExists: true));
            return true;
        }

        var genericLabel = NearestMark(geom, x, y);
        var genericMark = place.AssignMark ? genericLabel : "";

        if (resizeFromCad && widthMm > 0 && depthMm > 0)
        {
            var w = Math.Max(1, (int)Math.Round(widthMm));
            var d = Math.Max(1, (int)Math.Round(depthMm));
            ops.Add(Op("StructuralFoundation", new
            {
                xyzMm = new[] { x, y, 0 },
                familyType = $"{w}x{d}",
                sourceFamily,
                widthMm = w,
                depthMm = d,
                rotationDeg,
                level = baseLevel,
                baseOffsetMm = place.BaseOffsetMm,
                offsetMm = place.BaseOffsetMm,
                material = place.Material ?? "",
                kind = foundationKind,
                mark = genericMark,
                exactMm = true
            }, skipIfExists: true));
            return true;
        }

        ops.Add(Op("StructuralFoundation", new
        {
            xyzMm = new[] { x, y, 0 },
            familyType = uiType,
            sourceFamily,
            rotationDeg,
            level = baseLevel,
            baseOffsetMm = place.BaseOffsetMm,
            offsetMm = place.BaseOffsetMm,
            material = place.Material ?? "",
            kind = foundationKind,
            mark = genericMark,
            exactMm = true
        }, skipIfExists: true));
        return true;
    }

    /// <summary>
    /// Point piles (circles / inserts / closed polys). Top at <paramref name="baseLevel"/> (+ offset);
    /// <see cref="ModelRevitPlacement.LengthMm"/> is chiều sâu cọc. XY rounded to 5 mm from grids like columns.
    /// Section from CAD footprint (largest outline at each pile center); Mark from nearby CadText.
    /// </summary>
    private static IReadOnlyList<CreateOperation> PlanPiles(
        CadGeometry geom,
        List<CadEntity> entities,
        string baseLevel,
        AnnotationSize size,
        ModelRevitPlacement place,
        IReadOnlyList<CadPoint2>? gridPoints)
    {
        var uiType = FirstNonEmpty(place.TypeName, place.FamilyName, size.Raw);
        SplitFamilyType(uiType, out var sourceFamily, out _);
        if (string.IsNullOrWhiteSpace(sourceFamily))
            sourceFamily = place.FamilyName ?? "";
        var lengthMm = place.LengthMm > 0
            ? place.LengthMm
            : size.HeightMm ?? size.WidthMm ?? size.DiameterMm ?? 0;
        var topOffsetMm = place.EffectivePileTopOffsetMm();
        var ops = new List<CreateOperation>();
        var placed = new List<(double X, double Y)>();

        // "Largest" = max diameter (circle) or max of b/h extents (rect) among CAD entities
        // at that pile center within PileClusterTolMm (concentric rings / nested outlines).
        foreach (var cluster in ClusterLargestPileSections(SectionFootprintReader.FromEntities(entities)))
        {
            var x = cluster.CenterX;
            var y = cluster.CenterY;
            var (ox, oy) = NearestGridPoint(x, y, gridPoints);
            (x, y) = RoundColumnPosition(x, y, 0, ox, oy);
            var (gx, gy, snapped) = SnapToGrid(x, y, gridPoints);
            if (snapped && OnRoundModule(gx, gy, 0, ox, oy))
            {
                x = gx;
                y = gy;
            }

            var w = Math.Max(1, (int)Math.Round(cluster.WidthMm));
            var d = Math.Max(1, (int)Math.Round(cluster.DepthMm));
            var circular = PreferPileDiameterType(w, d, uiType, sourceFamily);
            string typeHint;
            if (circular)
            {
                var dia = Math.Max(w, d);
                w = d = dia;
                typeHint = $"D{dia}";
            }
            else
            {
                typeHint = $"{w}x{d}";
            }

            var mark = place.AssignMark ? NearestMark(geom, x, y) : "";
            ops.Add(PileOp(x, y, typeHint, sourceFamily, baseLevel, topOffsetMm, lengthMm, w, d, circular, mark, place.Material));
            placed.Add((x, y));
        }

        // Blocks / inserts without a measurable outline — keep UI type size.
        foreach (var entity in entities)
        {
            if (entity is CadCircle)
                continue;
            if (entity is CadPolyline pl && pl.Closed && pl.Points.Count >= 3)
                continue;
            if (!TryPoint(entity, out var x, out var y))
                continue;
            if (NearPlacedPile(placed, x, y))
                continue;

            var (ox, oy) = NearestGridPoint(x, y, gridPoints);
            (x, y) = RoundColumnPosition(x, y, 0, ox, oy);
            var (gx, gy, snapped) = SnapToGrid(x, y, gridPoints);
            if (snapped && OnRoundModule(gx, gy, 0, ox, oy))
            {
                x = gx;
                y = gy;
            }

            var mark = place.AssignMark ? NearestMark(geom, x, y) : "";
            ops.Add(Op("StructuralFoundation", new
            {
                xyzMm = new[] { x, y, 0 },
                familyType = uiType,
                sourceFamily,
                level = baseLevel,
                baseOffsetMm = topOffsetMm,
                offsetMm = topOffsetMm,
                lengthMm,
                material = place.Material ?? "",
                kind = "Pile",
                mark,
                exactMm = true
            }, skipIfExists: true));
            placed.Add((x, y));
        }

        return ops;
    }

    /// <summary>Centers within this distance share one pile; keep the largest CAD outline.</summary>
    private const double PileClusterTolMm = 150.0;

    private static CreateOperation PileOp(
        double x, double y, string typeHint, string sourceFamily, string baseLevel,
        double topOffsetMm, double lengthMm, int widthMm, int depthMm, bool circular, string mark, string? material)
    {
        if (circular)
        {
            return Op("StructuralFoundation", new
            {
                xyzMm = new[] { x, y, 0 },
                familyType = typeHint,
                sourceFamily,
                widthMm,
                depthMm,
                diameterMm = widthMm,
                level = baseLevel,
                baseOffsetMm = topOffsetMm,
                offsetMm = topOffsetMm,
                lengthMm,
                material = material ?? "",
                kind = "Pile",
                mark,
                exactMm = true
            }, skipIfExists: true);
        }

        return Op("StructuralFoundation", new
        {
            xyzMm = new[] { x, y, 0 },
            familyType = typeHint,
            sourceFamily,
            widthMm,
            depthMm,
            level = baseLevel,
            baseOffsetMm = topOffsetMm,
            offsetMm = topOffsetMm,
            lengthMm,
            material = material ?? "",
            kind = "Pile",
            mark,
            exactMm = true
        }, skipIfExists: true);
    }

    /// <summary>
    /// Merge footprints whose centers lie within <see cref="PileClusterTolMm"/>.
    /// Largest = max diameter when W≈D; else the outline with the greatest max(b,h) (then area).
    /// </summary>
    internal static List<SectionFootprint> ClusterLargestPileSections(IReadOnlyList<SectionFootprint> footprints)
    {
        var clusters = new List<SectionFootprint>();
        foreach (var fp in footprints)
        {
            var w = Math.Max(1, (int)Math.Round(fp.WidthMm));
            var d = Math.Max(1, (int)Math.Round(fp.DepthMm));
            var candidate = new SectionFootprint(fp.CenterX, fp.CenterY, w, d, fp.RotationDeg);
            var idx = -1;
            for (var i = 0; i < clusters.Count; i++)
            {
                var dx = clusters[i].CenterX - candidate.CenterX;
                var dy = clusters[i].CenterY - candidate.CenterY;
                if (dx * dx + dy * dy <= PileClusterTolMm * PileClusterTolMm)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                clusters.Add(candidate);
                continue;
            }

            if (IsLargerPileSection(candidate, clusters[idx]))
            {
                // Keep the winner's extents; average centers so nested rings stay co-located.
                var keep = clusters[idx];
                clusters[idx] = new SectionFootprint(
                    (keep.CenterX + candidate.CenterX) / 2.0,
                    (keep.CenterY + candidate.CenterY) / 2.0,
                    candidate.WidthMm,
                    candidate.DepthMm,
                    candidate.RotationDeg);
            }
        }

        return clusters;
    }

    private static bool IsLargerPileSection(SectionFootprint a, SectionFootprint b)
    {
        var aMax = Math.Max(a.WidthMm, a.DepthMm);
        var bMax = Math.Max(b.WidthMm, b.DepthMm);
        if (Math.Abs(aMax - bMax) > 0.5)
            return aMax > bMax;
        return a.WidthMm * a.DepthMm > b.WidthMm * b.DepthMm;
    }

    private static bool PreferPileDiameterType(int widthMm, int depthMm, string uiType, string sourceFamily)
    {
        if (Math.Abs(widthMm - depthMm) <= 10)
        {
            if (LooksCircularPileFamily(uiType, sourceFamily))
                return true;
            // Equal extents from a circle footprint → D naming unless UI type looks rectangular (nxm).
            if (!System.Text.RegularExpressions.Regex.IsMatch(uiType ?? "", @"\d+\s*[xX×]\s*\d+"))
                return true;
        }

        return LooksCircularPileFamily(uiType, sourceFamily) && Math.Abs(widthMm - depthMm) <= 10;
    }

    private static bool LooksCircularPileFamily(string? uiType, string? sourceFamily)
    {
        var s = $"{sourceFamily} {uiType}";
        return s.IndexOf("Cir", StringComparison.OrdinalIgnoreCase) >= 0
               || System.Text.RegularExpressions.Regex.IsMatch(s, @"\bD\s*\d{2,4}\b",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static void SplitFamilyType(string? typed, out string family, out string typeName)
    {
        family = "";
        typeName = "";
        if (string.IsNullOrWhiteSpace(typed))
            return;
        typed = typed.Trim();
        var sep = typed.IndexOf(" : ", StringComparison.Ordinal);
        if (sep < 0)
            sep = typed.IndexOf(':');
        if (sep <= 0 || sep >= typed.Length - 1)
        {
            typeName = typed;
            return;
        }

        family = typed.Substring(0, sep).Trim();
        typeName = typed.Substring(sep + (typed.IndexOf(" : ", StringComparison.Ordinal) == sep ? 3 : 1)).Trim();
    }

    private static bool NearPlacedPile(List<(double X, double Y)> placed, double x, double y)
    {
        foreach (var p in placed)
        {
            var dx = p.X - x;
            var dy = p.Y - y;
            if (dx * dx + dy * dy <= PileClusterTolMm * PileClusterTolMm)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Strip footing: point family at mid-span of each CadLine, rotated along the line.
    /// (CreateFoundation is point-hosted; start/end curve create is not available for Footing.)
    /// </summary>
    private static IReadOnlyList<CreateOperation> PlanStripFoundations(
        CadGeometry geom,
        List<CadEntity> entities,
        string baseLevel,
        AnnotationSize size,
        ModelRevitPlacement place,
        IReadOnlyList<CadPoint2>? gridPoints)
    {
        var uiType = FirstNonEmpty(place.TypeName, place.FamilyName, size.Raw);
        SplitFamilyType(uiType, out var sourceFamily, out _);
        if (string.IsNullOrWhiteSpace(sourceFamily))
            sourceFamily = place.FamilyName ?? "";
        var ops = new List<CreateOperation>();
        foreach (var line in entities.OfType<CadLine>())
        {
            var x = (line.X0 + line.X1) / 2.0;
            var y = (line.Y0 + line.Y1) / 2.0;
            var (ox, oy) = NearestGridPoint(x, y, gridPoints);
            (x, y) = RoundColumnPosition(x, y, 0, ox, oy);
            var (gx, gy, snapped) = SnapToGrid(x, y, gridPoints);
            if (snapped && OnRoundModule(gx, gy, 0, ox, oy))
            {
                x = gx;
                y = gy;
            }

            var rotationDeg = Math.Atan2(line.Y1 - line.Y0, line.X1 - line.X0) * 180.0 / Math.PI;
            var mark = place.AssignMark ? NearestMark(geom, x, y) : "";
            ops.Add(Op("StructuralFoundation", new
            {
                xyzMm = new[] { x, y, 0 },
                familyType = uiType,
                sourceFamily,
                level = baseLevel,
                baseOffsetMm = place.BaseOffsetMm,
                offsetMm = place.BaseOffsetMm,
                rotationDeg,
                material = place.Material ?? "",
                kind = FoundationCatalogFilter.KindStrip,
                mark,
                exactMm = true
            }, skipIfExists: true));
        }

        return ops;
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
