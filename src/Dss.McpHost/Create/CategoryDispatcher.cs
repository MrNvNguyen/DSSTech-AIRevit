using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Dss.McpContracts;

namespace Dss.McpHost.Create;

internal static class CategoryDispatcher
{
    private const double LevelTolMm = 5.0;
    private const double GridDistTolMm = 50.0;
    private const double GridAngleTolDeg = 2.0;
    private const double GeomTolMm = 10.0;

    public static CreateItemResult Execute(Document doc, CreateOperation op, bool forceDryRun)
    {
        var category = op.Category ?? "";
        var skip = op.SkipIfExists ?? true;
        var dryRun = forceDryRun || (op.DryRun ?? false);
        var units = op.Units;

        try
        {
            return category.Trim().ToLowerInvariant() switch
            {
                "levels" or "level" or "ost_levels" => CreateLevel(doc, op, skip, dryRun, units),
                "grids" or "grid" or "ost_grids" => CreateGrid(doc, op, skip, dryRun, units),
                "walls" or "wall" or "ost_walls" => CreateWall(doc, op, skip, dryRun, units),
                "structuralcolumns" or "columns" or "ost_structuralcolumns" => CreateColumn(doc, op, skip, dryRun, units),
                "pipes" or "pipe" or "ost_pipecurves" or "pipecurves" => CreatePipe(doc, op, skip, dryRun, units),
                "floors" or "floor" or "ost_floors" => CatalogCreate.CreateFloor(doc, op, skip, dryRun, units),
                "ceilings" or "ceiling" or "ost_ceilings" => CatalogCreate.CreateCeiling(doc, op, skip, dryRun, units),
                "structuralframing" or "beams" or "beam" or "ost_structuralframing" =>
                    CatalogCreate.CreateBeam(doc, op, skip, dryRun, units),
                "doors" or "door" or "ost_doors" => CatalogCreate.CreateDoorOrWindow(doc, op, skip, dryRun, units, isDoor: true),
                "windows" or "window" or "ost_windows" => CatalogCreate.CreateDoorOrWindow(doc, op, skip, dryRun, units, isDoor: false),
                "rooms" or "room" or "ost_rooms" => CatalogCreate.CreateRoom(doc, op, skip, dryRun, units),
                "ducts" or "duct" or "ost_ductcurves" => CatalogCreate.CreateDuct(doc, op, skip, dryRun, units),
                "cabletrays" or "cabletray" or "ost_cabletray" => CatalogCreate.CreateCableTray(doc, op, skip, dryRun, units),
                "structuralfoundation" or "foundations" or "foundation" or "ost_structuralfoundation" =>
                    CatalogCreate.CreateFoundation(doc, op, skip, dryRun, units),
                "mechanicalequipment" or "ost_mechanicalequipment" =>
                    CatalogCreate.CreateFamilyAtPoint(doc, op, skip, dryRun, units, BuiltInCategory.OST_MechanicalEquipment),
                "electricalequipment" or "ost_electricalequipment" =>
                    CatalogCreate.CreateFamilyAtPoint(doc, op, skip, dryRun, units, BuiltInCategory.OST_ElectricalEquipment),
                "plumbingfixtures" or "ost_plumbingfixtures" =>
                    CatalogCreate.CreateFamilyAtPoint(doc, op, skip, dryRun, units, BuiltInCategory.OST_PlumbingFixtures),
                "lightingfixtures" or "ost_lightingfixtures" =>
                    CatalogCreate.CreateFamilyAtPoint(doc, op, skip, dryRun, units, BuiltInCategory.OST_LightingFixtures),
                "sprinklers" or "ost_sprinklers" =>
                    CatalogCreate.CreateFamilyAtPoint(doc, op, skip, dryRun, units, BuiltInCategory.OST_Sprinklers),
                "genericmodels" or "ost_genericmodel" =>
                    CatalogCreate.CreateFamilyAtPoint(doc, op, skip, dryRun, units, BuiltInCategory.OST_GenericModel),
                _ => Fail(op, "unsupported_category", $"Unsupported category '{category}'. OUT: stairs/rebar/fittings/in-place.")
            };
        }
        catch (Exception ex)
        {
            return Fail(op, "exception", ex.Message);
        }
    }

    private static CreateItemResult CreateLevel(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        var name = ReqString(op.Fields, "name");
        if (name is null)
            return Fail(op, "missing_field", "fields.name required.");

        var elevMm = ReqDouble(op.Fields, "elevationMm") ?? ReqDouble(op.Fields, "elevation") ?? 0;
        elevMm = UnitConv.ScaleToMm(elevMm, units);
        var elev = UnitConv.MmToInternal(elevMm);

        if (skip)
        {
            foreach (var level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                if (string.Equals(level.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    Math.Abs(UnitConv.ToMm(level.Elevation) - elevMm) <= LevelTolMm)
                    return Skip(op, level.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        var created = Level.Create(doc, elev);
        created.Name = name;
        return Created(op, created.Id.Value.ToString());
    }

    private static CreateItemResult CreateGrid(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        var name = ReqString(op.Fields, "name");
        if (name is null)
            return Fail(op, "missing_field", "fields.name required.");

        if (!TryXyzMm(op.Fields, "startMm", "start", units, out var start) ||
            !TryXyzMm(op.Fields, "endMm", "end", units, out var end))
            return Fail(op, "missing_field", "fields.startMm and endMm required.");

        if (skip)
        {
            foreach (var grid in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
            {
                if (string.Equals(grid.Name, name, StringComparison.OrdinalIgnoreCase))
                    return Skip(op, grid.Id.Value.ToString(), "skipIfExists");
                if (grid.Curve is Line existing && LinesNear(existing, start, end))
                    return Skip(op, grid.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        var line = Line.CreateBound(start, end);
        var created = Grid.Create(doc, line);
        created.Name = name;
        return Created(op, created.Id.Value.ToString());
    }

    private static CreateItemResult CreateWall(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        if (!TryXyzMm(op.Fields, "startMm", "start", units, out var start) ||
            !TryXyzMm(op.Fields, "endMm", "end", units, out var end))
            return Fail(op, "missing_field", "fields.startMm and endMm required.");

        var typeName = ReqString(op.Fields, "wallType") ?? ReqString(op.Fields, "typeName");
        if (typeName is null)
            return Fail(op, "type_unresolved", "fields.wallType required — call list_types.");

        var wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
            .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (wallType is null)
            return Fail(op, "type_unresolved", $"WallType '{typeName}' not found — call list_types.");

        var levelName = ReqString(op.Fields, "level") ?? ReqString(op.Fields, "levelName");
        var level = FindLevel(doc, levelName);
        if (level is null)
            return Fail(op, "missing_level", $"Level '{levelName}' not found.");

        var heightMm = ReqDouble(op.Fields, "heightMm") ?? 3000;
        heightMm = UnitConv.ScaleToMm(heightMm, units);
        var structural = ReqBool(op.Fields, "structural") ?? false;

        if (skip)
        {
            foreach (var wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
            {
                if (wall.WallType.Id != wallType.Id)
                    continue;
                if (wall.Location is LocationCurve lc && lc.Curve is Line line &&
                    LinesNear(line, start, end, GeomTolMm))
                    return Skip(op, wall.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        var curve = Line.CreateBound(start, end);
        var wallNew = Wall.Create(doc, curve, wallType.Id, level.Id, UnitConv.MmToInternal(heightMm), 0, false, structural);
        return Created(op, wallNew.Id.Value.ToString());
    }

    private static CreateItemResult CreateColumn(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        if (!TryXyzMm(op.Fields, "xyzMm", "xyz", units, out var xyz))
            return Fail(op, "missing_field", "fields.xyzMm required.");

        var typeName = ReqString(op.Fields, "familyType") ?? ReqString(op.Fields, "typeName");
        if (typeName is null)
            return Fail(op, "type_unresolved", "fields.familyType required — call list_types.");

        var symbol = FindColumnSymbol(doc, typeName);
        if (symbol is null)
            return Fail(op, "type_unresolved", $"Column type '{typeName}' not found — call list_types.");

        var baseName = ReqString(op.Fields, "baseLevel") ?? ReqString(op.Fields, "level");
        var baseLevel = FindLevel(doc, baseName);
        if (baseLevel is null)
            return Fail(op, "missing_level", $"baseLevel '{baseName}' not found.");

        Level? topLevel = null;
        var topName = ReqString(op.Fields, "topLevel");
        if (!string.IsNullOrWhiteSpace(topName))
            topLevel = FindLevel(doc, topName);

        if (skip)
        {
            foreach (var fi in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_StructuralColumns)
                         .WhereElementIsNotElementType()
                         .OfType<FamilyInstance>())
            {
                if (fi.Symbol.Id != symbol.Id)
                    continue;
                if (fi.Location is LocationPoint lp &&
                    DistanceMm(lp.Point, xyz) <= GeomTolMm)
                    return Skip(op, fi.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        EnsureSymbolActive(doc, symbol);

        var created = doc.Create.NewFamilyInstance(xyz, symbol, baseLevel, StructuralType.Column);
        if (topLevel is not null)
        {
            var topParam = created.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            topParam?.Set(topLevel.Id);
        }

        return Created(op, created.Id.Value.ToString());
    }

    private static CreateItemResult CreatePipe(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        var pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().ToList();
        var systemTypes = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().ToList();
        if (pipeTypes.Count == 0 || systemTypes.Count == 0)
            return Skip(op, null, "no_pipe_type");

        if (!TryXyzMm(op.Fields, "startMm", "start", units, out var start) ||
            !TryXyzMm(op.Fields, "endMm", "end", units, out var end))
            return Fail(op, "missing_field", "fields.startMm and endMm required.");

        var pipeTypeName = ReqString(op.Fields, "pipeType");
        var systemTypeName = ReqString(op.Fields, "systemType");
        var pipeType = pipeTypeName is null
            ? null
            : pipeTypes.FirstOrDefault(t => string.Equals(t.Name, pipeTypeName, StringComparison.OrdinalIgnoreCase));
        var systemType = systemTypeName is null
            ? null
            : systemTypes.FirstOrDefault(t => string.Equals(t.Name, systemTypeName, StringComparison.OrdinalIgnoreCase));

        if (pipeTypeName is not null && pipeType is null)
            return Fail(op, "type_unresolved", $"PipeType '{pipeTypeName}' not found — call list_types.");
        if (systemTypeName is not null && systemType is null)
            return Fail(op, "type_unresolved", $"PipingSystemType '{systemTypeName}' not found — call list_types.");

        // If names omitted, refuse to guess — but DoD allows skip when template has no types (handled above).
        if (pipeType is null || systemType is null)
            return Fail(op, "type_unresolved", "fields.pipeType and systemType required — call list_types.");

        var levelName = ReqString(op.Fields, "level");
        var level = FindLevel(doc, levelName);
        if (level is null)
            return Fail(op, "missing_level", $"Level '{levelName}' not found.");

        if (skip)
        {
            foreach (var pipe in new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>())
            {
                if (pipe.PipeType.Id != pipeType.Id)
                    continue;
                if (pipe.Location is LocationCurve lc && lc.Curve is Line line &&
                    LinesNear(line, start, end, GeomTolMm))
                    return Skip(op, pipe.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        var created = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, start, end);
        var diameterMm = ReqDouble(op.Fields, "diameterMm");
        if (diameterMm is not null)
        {
            var diam = created.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            diam?.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(diameterMm.Value, units)));
        }

        return Created(op, created.Id.Value.ToString());
    }

    private static FamilySymbol? FindColumnSymbol(Document doc, string typeName)
    {
        var symbols = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .Cast<FamilySymbol>()
            .ToList();

        var exact = symbols.FirstOrDefault(s =>
            string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{s.FamilyName} : {s.Name}", typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{s.FamilyName}:{s.Name}", typeName, StringComparison.OrdinalIgnoreCase));
        return exact;
    }

    private static void EnsureSymbolActive(Document doc, FamilySymbol symbol)
    {
        if (symbol.IsActive)
            return;
        symbol.Activate();
        doc.Regenerate();
    }

    private static Level? FindLevel(Document doc, string? name)
    {
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        if (string.IsNullOrWhiteSpace(name))
            return levels.OrderBy(l => l.Elevation).FirstOrDefault();
        return levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LinesNear(Line existing, XYZ start, XYZ end, double tolMm = GridDistTolMm, double angleTolDeg = GridAngleTolDeg)
    {
        var a0 = existing.GetEndPoint(0);
        var a1 = existing.GetEndPoint(1);
        var d1 = Math.Min(DistanceMm(a0, start) + DistanceMm(a1, end), DistanceMm(a0, end) + DistanceMm(a1, start));
        if (d1 > tolMm * 2)
            return false;

        // Normalize() throws on a zero-length vector. Degenerate grid/wall lines must not crash skipIfExists.
        var dir1 = a1 - a0;
        var dir2 = end - start;
        if (dir1.IsZeroLength() || dir2.IsZeroLength())
            return d1 <= tolMm * 2;

        var v1 = dir1.Normalize();
        var v2 = dir2.Normalize();

        var angle = v1.AngleTo(v2) * 180.0 / Math.PI;
        if (angle > 90)
            angle = 180 - angle;
        return angle <= angleTolDeg;
    }

    private static double DistanceMm(XYZ a, XYZ b) =>
        UnitConv.ToMm(a.DistanceTo(b));

    private static bool TryXyzMm(JsonElement fields, string mmKey, string altKey, string? units, out XYZ xyz)
    {
        xyz = XYZ.Zero;
        if (!TryArray(fields, mmKey, out var arr) && !TryArray(fields, altKey, out arr))
            return false;
        if (arr.Length < 2)
            return false;
        var scaled = arr.Select(v => UnitConv.ScaleToMm(v, units)).ToArray();
        if (scaled.Length == 2)
            scaled = new[] { scaled[0], scaled[1], 0 };
        xyz = UnitConv.XyzFromMm(scaled);
        return true;
    }

    private static bool TryArray(JsonElement fields, string key, out double[] values)
    {
        values = Array.Empty<double>();
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el) ||
            el.ValueKind != JsonValueKind.Array)
            return false;
        values = el.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        return true;
    }

    private static string? ReqString(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static double? ReqDouble(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;
    }

    private static bool? ReqBool(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        return el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : null;
    }

    private static CreateItemResult Created(CreateOperation op, string? id, string? reason = null) =>
        new()
        {
            OpId = op.OpId,
            Category = op.Category,
            Status = reason == "dryRun" ? "planned" : "created",
            Id = id,
            Reason = reason
        };

    private static CreateItemResult Skip(CreateOperation op, string? id, string reason) =>
        new() { OpId = op.OpId, Category = op.Category, Status = "skipped", Id = id, Reason = reason };

    private static CreateItemResult Fail(CreateOperation op, string reason, string message) =>
        new() { OpId = op.OpId, Category = op.Category, Status = "failed", Reason = $"{reason}: {message}" };
}
