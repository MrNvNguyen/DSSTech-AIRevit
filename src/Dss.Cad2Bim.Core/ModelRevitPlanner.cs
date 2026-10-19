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
            ModelRevitPartKind.StructuralWall => PlanWalls(entities, baseLevel, topLevel, size, structural: true, place),
            ModelRevitPartKind.Beam => PlanBeams(entities, topLevel, size, place),
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

    private static IReadOnlyList<CreateOperation> PlanColumns(
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
        foreach (var footprint in SectionFootprintReader.FromEntities(entities))
        {
            var rotation = SnapToGridAngle(footprint.RotationDeg, gridAnglesDeg);
            var (x, y) = RoundColumnPosition(footprint.CenterX, footprint.CenterY, rotation);
            var (gx, gy, snapped) = SnapToGrid(x, y, gridPoints);
            if (snapped && OnRoundModule(gx, gy, rotation))
            {
                x = gx;
                y = gy;
            }
            ColumnSection(footprint, size, place.SectionShape, out var w, out var d);
            var typeHint = $"{w} x {d}";
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
                mark
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
        List<CadEntity> entities,
        string baseLevel,
        string topLevel,
        AnnotationSize size,
        bool structural,
        ModelRevitPlacement place)
    {
        var sourceType = place.TypeName ?? "";
        var forced = size.WidthMm is > 0 ? (int)Math.Round(size.WidthMm.Value) : 0;
        var ops = new List<CreateOperation>();
        foreach (var run in WallCenterlineReader.FromEntities(entities))
        {
            var thick = forced > 0 ? forced : (int)Math.Round(run.ThicknessMm);
            ops.Add(Op("Walls", new
            {
                startMm = new[] { run.X0, run.Y0, 0 },
                endMm = new[] { run.X1, run.Y1, 0 },
                wallType = sourceType,
                sourceType,
                thicknessMm = thick,
                level = baseLevel,
                topLevel,
                baseOffsetMm = place.BaseOffsetMm,
                topOffsetMm = place.TopOffsetMm,
                material = place.Material ?? "",
                function = place.Function ?? "",
                structural
            }));
        }

        return ops;
    }

    private static IReadOnlyList<CreateOperation> PlanBeams(
        List<CadEntity> entities,
        string topLevel,
        AnnotationSize size,
        ModelRevitPlacement place)
    {
        var typeHint = FirstNonEmpty(place.TypeName, place.FamilyName, AnnotationSizeParser.BeamFamilyTypeHint(size));
        return entities.OfType<CadLine>().Select(line => Op("StructuralFraming", new
        {
            startMm = new[] { line.X0, line.Y0, 0 },
            endMm = new[] { line.X1, line.Y1, 0 },
            level = topLevel,
            topOffsetMm = place.TopOffsetMm,
            familyType = typeHint,
            widthParam = place.WidthParam ?? "",
            heightParam = place.HeightParam ?? "",
            material = place.Material ?? ""
        })).ToList();
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
