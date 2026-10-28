using System.Text.Json;
using System.Text.RegularExpressions;
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

    /// <summary>Half of the 50 mm station, plus a little, so a rounded column still finds the one it replaces.</summary>
    private const double ColumnLocateMm = 10.0;

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

        var typeName = ReqString(op.Fields, "sourceType") ?? ReqString(op.Fields, "wallType") ?? ReqString(op.Fields, "typeName");
        if (typeName is null)
            return Fail(op, "type_unresolved", "Chọn type tường gốc — call list_types.");

        var sourceType = FindWallType(doc, typeName);
        if (sourceType is null)
            return Fail(op, "type_unresolved", $"WallType '{typeName}' not found — call list_types.");

        var thicknessMm = ReqInt(op.Fields, "thicknessMm");
        WallType wallType;
        try
        {
            wallType = ResolveWallType(doc, sourceType, thicknessMm, ReqString(op.Fields, "function"));
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or InvalidOperationException)
        {
            return Fail(op, "type_unresolved", ex.Message);
        }

        var levelName = ReqString(op.Fields, "level") ?? ReqString(op.Fields, "levelName");
        var level = FindLevel(doc, levelName);
        if (level is null)
            return Fail(op, "missing_level", $"Level '{levelName}' not found.");

        // Curve Z follows the base level. CAD Z is not used.
        start = new XYZ(start.X, start.Y, level.Elevation);
        end = new XYZ(end.X, end.Y, level.Elevation);
        if (start.DistanceTo(end) < 1e-6)
            return Fail(op, "bad_curve", "Hai đầu tường trùng nhau.");

        var topName = ReqString(op.Fields, "topLevel");
        Level? topLevel = null;
        if (!string.IsNullOrWhiteSpace(topName))
        {
            topLevel = FindLevel(doc, topName);
            if (topLevel is null)
                return Fail(op, "missing_level", $"topLevel '{topName}' not found.");
            if (topLevel.Elevation <= level.Elevation + 1e-6)
                return Fail(op, "bad_level", "Top level must be above the base level.");
        }

        var heightMm = ReqDouble(op.Fields, "heightMm");
        var heightInternal = topLevel is not null
            ? topLevel.Elevation - level.Elevation
            : UnitConv.MmToInternal(UnitConv.ScaleToMm(heightMm ?? 3000, units));
        var structural = ReqBool(op.Fields, "structural") ?? false;

        if (skip)
        {
            // Earlier runs may have left the body up to one thickness off CAD and ends short of corners.
            var thickForMatch = thicknessMm is > 0 ? thicknessMm.Value : WallWidthMm(wallType);
            var matchTol = Math.Max(GeomTolMm, thickForMatch * 1.5 + 50);
            Wall? match = null;
            var bestMm = double.MaxValue;
            foreach (var wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
            {
                if (wall.Location is not LocationCurve { Curve: Line line })
                    continue;
                var center = BodyCenterline(wall, line);
                if (!LinesNear(center, start, end, matchTol))
                    continue;
                var d = EndsDistanceMm(center, start, end);
                if (d >= bestMm)
                    continue;
                bestMm = d;
                match = wall;
            }

            if (match is not null)
            {
                if (!dryRun)
                {
                    try
                    {
                        if (match.WallType.Id != wallType.Id)
                            match.WallType = wallType;
                        SetLocationLine(match, (int)WallLocationLine.WallCenterline);
                        ((LocationCurve)match.Location).Curve = Line.CreateBound(start, end);
                        WallUtils.AllowWallJoinAtEnd(match, 0);
                        WallUtils.AllowWallJoinAtEnd(match, 1);
                        FaceOutward(match, op.Fields);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // Keep the existing wall when it is pinned/locked.
                    }
                }

                return Skip(op, match.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        var curve = Line.CreateBound(start, end);
        var wallNew = Wall.Create(doc, curve, wallType.Id, level.Id, heightInternal, 0, false, structural);
        if (SetLocationLine(wallNew, (int)WallLocationLine.WallCenterline))
            ((LocationCurve)wallNew.Location).Curve = curve;
        WallUtils.AllowWallJoinAtEnd(wallNew, 0);
        WallUtils.AllowWallJoinAtEnd(wallNew, 1);
        try
        {
            var baseParam = wallNew.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            if (baseParam is not null && !baseParam.IsReadOnly)
                baseParam.Set(level.Id);
            if (topLevel is not null)
            {
                var topParam = wallNew.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                if (topParam is null || topParam.IsReadOnly)
                {
                    doc.Delete(wallNew.Id);
                    return Fail(op, "bad_level", "Không gán được Top Constraint của tường.");
                }

                topParam.Set(topLevel.Id);
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            try { doc.Delete(wallNew.Id); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            return Fail(op, "bad_level", ex.Message);
        }

        FaceOutward(wallNew, op.Fields);
        PlacementApply.Apply(wallNew, op.Fields, units);
        return Created(op, wallNew.Id.Value.ToString());
    }

    /// <summary>
    /// Run after every wall of the batch exists on its centerline (joins formed). Changing Location Line
    /// moves only the location curve onto the finish face; the wall body stays on the CAD outline.
    /// </summary>
    internal static void ApplyWallLocationLines(Document doc, IReadOnlyList<CreateOperation> operations, IReadOnlyList<CreateItemResult> results)
    {
        var pending = new List<(Wall Wall, int Value)>();
        for (var i = 0; i < operations.Count && i < results.Count; i++)
        {
            var op = operations[i];
            if ((op.Category ?? "").Trim().ToLowerInvariant() is not ("walls" or "wall" or "ost_walls"))
                continue;
            if (results[i].Status is not ("created" or "skipped") || !long.TryParse(results[i].Id, out var id))
                continue;
            if (doc.GetElement(new ElementId(id)) is not Wall wall)
                continue;
            // Structural / CoreShaft walls stay on Wall Centerline — do not map envelope Exterior/Interior to Finish Face.
            var structural = ReqBool(op.Fields, "structural") ?? false;
            var rawLine = (ReqString(op.Fields, "locationLine") ?? "").Trim().ToLowerInvariant();
            if (structural && rawLine is "exterior" or "finishfaceexterior" or "interior" or "finishfaceinterior")
                rawLine = "wallcenterline";
            var value = rawLine switch
            {
                "exterior" or "finishfaceexterior" => (int)WallLocationLine.FinishFaceExterior,
                "interior" or "finishfaceinterior" => (int)WallLocationLine.FinishFaceInterior,
                "center" or "centerline" or "wallcenterline" => (int)WallLocationLine.WallCenterline,
                _ => -1
            };
            if (value >= 0)
                pending.Add((wall, value));
        }

        if (pending.Count == 0)
            return;
        doc.Regenerate();
        foreach (var (wall, value) in pending)
        {
            try
            {
                SetLocationLine(wall, value);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Keep centerline if the parameter is locked.
            }
        }
    }

    private static bool SetLocationLine(Wall wall, int value)
    {
        var key = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
        if (key is null || key.IsReadOnly || key.StorageType != StorageType.Integer || key.AsInteger() == value)
            return false;
        return key.Set(value);
    }

    /// <summary>Flip while the location line is the centerline so the body does not move. Architectural only.</summary>
    private static void FaceOutward(Wall wall, JsonElement fields)
    {
        // Structural walls use CoreShaft + centerline; do not flip from envelope outward.
        if (ReqBool(fields, "structural") == true)
            return;
        if (!TryOutward(fields, out var outward))
            return;
        try
        {
            // Orientation points to the exterior side of the wall.
            if (wall.Orientation.DotProduct(outward) < 0)
                wall.Flip();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Keep wall if flip is locked.
        }
    }

    /// <summary>Body centerline from the location curve; exterior face is on the Orientation side.</summary>
    private static Line BodyCenterline(Wall wall, Line location)
    {
        var key = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.AsInteger() ?? 0;
        var half = wall.Width / 2;
        var shift = key switch
        {
            (int)WallLocationLine.FinishFaceExterior or (int)WallLocationLine.CoreExterior => -half,
            (int)WallLocationLine.FinishFaceInterior or (int)WallLocationLine.CoreInterior => half,
            _ => 0
        };
        if (Math.Abs(shift) < 1e-9)
            return location;
        var v = wall.Orientation * shift;
        return Line.CreateBound(location.GetEndPoint(0) + v, location.GetEndPoint(1) + v);
    }

    private static double EndsDistanceMm(Line existing, XYZ start, XYZ end)
    {
        var a0 = existing.GetEndPoint(0);
        var a1 = existing.GetEndPoint(1);
        return Math.Min(DistanceMm(a0, start) + DistanceMm(a1, end), DistanceMm(a0, end) + DistanceMm(a1, start));
    }

    private static bool TryOutward(JsonElement fields, out XYZ outward)
    {
        outward = XYZ.Zero;
        if (fields.ValueKind != JsonValueKind.Object)
            return false;
        if (!fields.TryGetProperty("outwardMm", out var arr) && !fields.TryGetProperty("outward", out arr))
            return false;
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 2)
            return false;
        var x = arr[0].ValueKind == JsonValueKind.Number ? arr[0].GetDouble() : 0;
        var y = arr[1].ValueKind == JsonValueKind.Number ? arr[1].GetDouble() : 0;
        var len = Math.Sqrt(x * x + y * y);
        if (len < 1e-9)
            return false;
        outward = new XYZ(x / len, y / len, 0);
        return true;
    }

    private static CreateItemResult CreateColumn(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        if (!TryXyzMm(op.Fields, "xyzMm", "xyz", units, out var xyz))
            return Fail(op, "missing_field", "fields.xyzMm required.");

        var typeName = ReqString(op.Fields, "familyType") ?? ReqString(op.Fields, "typeName");
        var widthMm = ReqInt(op.Fields, "widthMm");
        var depthMm = ReqInt(op.Fields, "depthMm");
        var cadRotationDeg = ReqDouble(op.Fields, "rotationDeg") ?? 0;

        FamilySymbol? symbol;
        var needsRotate90 = false;
        var createdNewType = false;
        var sourceFamily = ReqString(op.Fields, "sourceFamily");
        if (widthMm is > 0 && depthMm is > 0)
        {
            symbol = ResolveColumnSymbol(
                doc, widthMm.Value, depthMm.Value, sourceFamily, out needsRotate90, out createdNewType,
                ReqString(op.Fields, "widthParam"), ReqString(op.Fields, "heightParam"));
            if (symbol is null)
                return Fail(op, "type_unresolved",
                    "Không tạo được tiết diện " + $"{widthMm}x{depthMm} mm" +
                    (string.IsNullOrWhiteSpace(sourceFamily) ? "" : $" trong family '{sourceFamily}'") +
                    ". Family cần tham số b/h.");
        }
        else
        {
            if (typeName is null)
                return Fail(op, "type_unresolved", "fields.familyType required — call list_types.");
            symbol = FindColumnSymbol(doc, typeName);
            if (symbol is null)
                return Fail(op, "type_unresolved", $"Column type '{typeName}' not found — call list_types.");
        }

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
            FamilyInstance? sameSymbol = null;
            var sameSymbolDist = double.MaxValue;
            foreach (var fi in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_StructuralColumns)
                         .WhereElementIsNotElementType()
                         .OfType<FamilyInstance>()
                         .ToList())
            {
                if (fi.LevelId != baseLevel.Id || fi.Location is not LocationPoint lp)
                    continue;

                var distance = DistanceMm(lp.Point, xyz);

                // A column created exactly on the internal origin often has no visible geometry.
                if (IsExactInternalOrigin(lp.Point))
                {
                    if (dryRun)
                        continue;
                    try
                    {
                        doc.Delete(fi.Id);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        return Skip(op, fi.Id.Value.ToString(), "skipIfExists");
                    }

                    continue;
                }

                if (fi.Symbol.Id == symbol.Id)
                {
                    if (distance <= ColumnLocateMm && distance < sameSymbolDist)
                    {
                        sameSymbol = fi;
                        sameSymbolDist = distance;
                    }

                    continue;
                }

                if (distance > ColumnLocateMm)
                    continue;
                var sameFamily = !string.IsNullOrWhiteSpace(sourceFamily) &&
                                 string.Equals(fi.Symbol.FamilyName, sourceFamily, StringComparison.OrdinalIgnoreCase);
                if (!sameFamily || dryRun)
                    continue;
                try
                {
                    doc.Delete(fi.Id);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    return Skip(op, fi.Id.Value.ToString(), "skipIfExists");
                }
            }

            if (sameSymbol is not null)
            {
                var moved = false;
                if (!dryRun && sameSymbol.Location is LocationPoint hit)
                {
                    var target = PlacementPoint(xyz);
                    if (DistanceMm(hit.Point, target) > 0.5)
                    {
                        try
                        {
                            hit.Point = target;
                            moved = true;
                        }
                        catch (Autodesk.Revit.Exceptions.ApplicationException)
                        {
                        }
                    }
                }

                var filled = PlacementApply.TrySetMark(sameSymbol, ReqString(op.Fields, "mark"));
                var skipReason = moved
                    ? (filled ? "rounded,mark" : "rounded")
                    : (filled ? "skipIfExists,mark" : "skipIfExists");
                return Skip(op, sameSymbol.Id.Value.ToString(), skipReason);
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        if (!symbol.IsActive)
            symbol.Activate();

        var place = PlacementPoint(xyz);
        var created = doc.Create.NewFamilyInstance(place, symbol, baseLevel, StructuralType.Column);
        // Top level is ignored until the new column has geometry. Without this regen only the
        // rotated columns (and the origin nudge) get a story height; the rest stay 0 and vanish in plan.
        doc.Regenerate();
        if (topLevel is not null)
        {
            var topParam = created.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            if (topParam is not null && !topParam.IsReadOnly)
                topParam.Set(topLevel.Id);
        }

        var rotateReasons = new List<string>();
        if (createdNewType)
            rotateReasons.Add("newType");
        if (!place.IsAlmostEqualTo(xyz))
            rotateReasons.Add("originNudge");
        var totalRotateDeg = cadRotationDeg + (needsRotate90 ? 90.0 : 0.0);
        if (Math.Abs(totalRotateDeg) > 0.01)
        {
            var axis = Line.CreateBound(place, place + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, created.Id, axis, totalRotateDeg * Math.PI / 180.0);
            if (needsRotate90)
                rotateReasons.Add("rotated90");
            else if (Math.Abs(cadRotationDeg) > 0.01)
                rotateReasons.Add("rotatedCad");
        }

        PlacementApply.Apply(created, op.Fields, units);
        var reason = rotateReasons.Count == 0 ? null : string.Join(",", rotateReasons);
        return Created(op, created.Id.Value.ToString(), reason);
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

        PlacementApply.Apply(created, op.Fields, units);
        return Created(op, created.Id.Value.ToString());
    }

    private static int? ReqInt(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
            return i;
        return null;
    }

    public static IReadOnlyList<string> ListRectangularColumnFamilies(Document doc) =>
        ColumnSymbols(doc)
            .Where(s => TryGetColumnSectionMm(s, out _, out _))
            .Select(s => s.FamilyName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> ListRectangularBeamFamilies(Document doc) =>
        BeamSymbols(doc)
            .Where(s => TryGetColumnSectionMm(s, out _, out _))
            .Select(s => s.FamilyName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Find or duplicate a Structural Framing type with the requested b×h (mm).</summary>
    public static FamilySymbol? ResolveBeamSymbol(
        Document doc,
        int widthMm,
        int depthMm,
        string? sourceFamily,
        out bool createdNewType,
        string? widthParam = null,
        string? heightParam = null)
    {
        createdNewType = false;
        var symbols = BeamSymbols(doc);
        if (!string.IsNullOrWhiteSpace(sourceFamily))
        {
            symbols = symbols
                .Where(s => string.Equals(s.FamilyName, sourceFamily, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var symbol in symbols)
        {
            if (!TryGetColumnSectionMm(symbol, out var b, out var h, widthParam, heightParam))
                continue;
            if (b == widthMm && h == depthMm)
                return symbol;
        }

        var template = PickBeamTemplate(symbols);
        if (template is null || !TryGetColumnSectionMm(template, out _, out _, widthParam, heightParam))
            return null;

        var typeName = $"{widthMm}x{depthMm}";
        var existing = symbols.FirstOrDefault(s =>
            string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (TryGetColumnSectionMm(existing, out var eb, out var eh, widthParam, heightParam) &&
                eb == widthMm && eh == depthMm)
                return existing;
            if (TrySetColumnSectionMm(existing, widthMm, depthMm, widthParam, heightParam))
            {
                doc.Regenerate();
                return existing;
            }
        }

        FamilySymbol? duplicate;
        try
        {
            duplicate = template.Duplicate(typeName) as FamilySymbol;
        }
        catch
        {
            return null;
        }

        if (duplicate is null || !TrySetColumnSectionMm(duplicate, widthMm, depthMm, widthParam, heightParam))
            return null;

        doc.Regenerate();
        if (!TryGetColumnSectionMm(duplicate, out var wb, out var wh, widthParam, heightParam) ||
            wb != widthMm || wh != depthMm)
            return null;

        createdNewType = true;
        return duplicate;
    }

    private static List<FamilySymbol> BeamSymbols(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .Cast<FamilySymbol>()
            .ToList();

    private static FamilySymbol? PickBeamTemplate(IReadOnlyList<FamilySymbol> symbols)
    {
        foreach (var symbol in symbols)
        {
            if (TryGetColumnSectionMm(symbol, out _, out _) &&
                (symbol.FamilyName.Contains("BEAM", StringComparison.OrdinalIgnoreCase) ||
                 symbol.FamilyName.Contains("DAM", StringComparison.OrdinalIgnoreCase) ||
                 symbol.FamilyName.Contains("FRAM", StringComparison.OrdinalIgnoreCase)))
                return symbol;
        }

        foreach (var symbol in symbols)
        {
            if (TryGetColumnSectionMm(symbol, out _, out _))
                return symbol;
        }

        return null;
    }

    private static bool IsExactInternalOrigin(XYZ point) =>
        Math.Abs(point.X) < 1e-8 && Math.Abs(point.Y) < 1e-8;

    /// <summary>
    /// Revit often draws no solid for a column inserted exactly at the internal origin.
    /// Keep the same Y and shift X by 1 mm, which is inside the 10 mm skip tolerance.
    /// </summary>
    private static XYZ PlacementPoint(XYZ xyz) =>
        IsExactInternalOrigin(xyz)
            ? new XYZ(xyz.X + UnitConv.MmToInternal(1), xyz.Y, xyz.Z)
            : xyz;

    private static FamilySymbol? ResolveColumnSymbol(
        Document doc,
        int widthMm,
        int depthMm,
        string? sourceFamily,
        out bool needsRotate90,
        out bool createdNewType,
        string? widthParam = null,
        string? heightParam = null)
    {
        needsRotate90 = false;
        createdNewType = false;
        var symbols = ColumnSymbols(doc);
        if (!string.IsNullOrWhiteSpace(sourceFamily))
        {
            symbols = symbols
                .Where(s => string.Equals(s.FamilyName, sourceFamily, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        FamilySymbol? direct = null;
        FamilySymbol? swapped = null;
        foreach (var symbol in symbols)
        {
            if (!TryGetColumnSectionMm(symbol, out var b, out var h, widthParam, heightParam))
                continue;
            if (b == widthMm && h == depthMm)
            {
                direct = symbol;
                break;
            }

            if (b == depthMm && h == widthMm)
                swapped ??= symbol;
        }

        if (direct is not null)
            return direct;
        if (swapped is not null)
        {
            needsRotate90 = true;
            return swapped;
        }

        var template = PickColumnTemplate(symbols);
        if (template is null || !TryGetColumnSectionMm(template, out _, out _, widthParam, heightParam))
            return null;

        var typeName = $"{widthMm}x{depthMm}";
        var existing = symbols.FirstOrDefault(s =>
            string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (TryGetColumnSectionMm(existing, out var eb, out var eh, widthParam, heightParam) &&
                ((eb == widthMm && eh == depthMm) || (eb == depthMm && eh == widthMm)))
            {
                if (eb == depthMm && eh == widthMm)
                    needsRotate90 = true;
                return existing;
            }

            // Type name matches but b/h drifted — rewrite section instead of failing Duplicate.
            if (TrySetColumnSectionMm(existing, widthMm, depthMm, widthParam, heightParam))
            {
                doc.Regenerate();
                return existing;
            }
        }

        FamilySymbol? duplicate;
        try
        {
            duplicate = template.Duplicate(typeName) as FamilySymbol;
        }
        catch
        {
            return null;
        }

        if (duplicate is null || !TrySetColumnSectionMm(duplicate, widthMm, depthMm, widthParam, heightParam))
            return null;

        doc.Regenerate();
        if (!TryGetColumnSectionMm(duplicate, out var wb, out var wh, widthParam, heightParam) ||
            !((wb == widthMm && wh == depthMm) || (wb == depthMm && wh == widthMm)))
            return null;

        createdNewType = true;
        return duplicate;
    }

    private static List<FamilySymbol> ColumnSymbols(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .Cast<FamilySymbol>()
            .ToList();

    private static FamilySymbol? PickColumnTemplate(IReadOnlyList<FamilySymbol> symbols)
    {
        var preferred = symbols.FirstOrDefault(s =>
            s.FamilyName.Contains("COLU", StringComparison.OrdinalIgnoreCase) &&
            s.FamilyName.Contains("Rec", StringComparison.OrdinalIgnoreCase));
        if (preferred is not null && TryGetColumnSectionMm(preferred, out _, out _))
            return preferred;

        foreach (var symbol in symbols)
        {
            if (TryGetColumnSectionMm(symbol, out _, out _))
                return symbol;
        }

        return null;
    }

    private static readonly string[] WidthParamNames = { "b", "B", "Width", "width", "W", "Rộng", "Chiều rộng" };
    private static readonly string[] DepthParamNames = { "h", "H", "Depth", "depth", "D", "Cao", "Chiều cao" };
    private static readonly string[] SectionWidthNames = { "b", "B", "Width", "width", "W", "Rộng" };
    private static readonly string[] SectionDepthNames = { "h", "Depth", "depth", "D" };

    private static bool TryGetColumnSectionMm(FamilySymbol symbol, out int bMm, out int hMm, string? widthParam = null, string? heightParam = null)
    {
        bMm = hMm = 0;
        if (!TryGetSectionParams(symbol, out var w, out var d, widthParam, heightParam) || !w.HasValue || !d.HasValue)
            return false;
        bMm = (int)Math.Round(UnitConv.ToMm(w.AsDouble()));
        hMm = (int)Math.Round(UnitConv.ToMm(d.AsDouble()));
        return bMm > 0 && hMm > 0;
    }

    private static bool TrySetColumnSectionMm(Element element, int bMm, int hMm, string? widthParam = null, string? heightParam = null)
    {
        var wrote = false;
        wrote |= SetLength(element, element.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH), bMm);
        wrote |= SetLength(element, element.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT), hMm);
        if (!string.IsNullOrWhiteSpace(widthParam))
            wrote |= SetLength(element, element.LookupParameter(widthParam), bMm);
        if (!string.IsNullOrWhiteSpace(heightParam))
            wrote |= SetLength(element, element.LookupParameter(heightParam), hMm);
        foreach (var name in SectionWidthNames)
            wrote |= SetLength(element, element.LookupParameter(name), bMm);
        foreach (var name in SectionDepthNames)
            wrote |= SetLength(element, element.LookupParameter(name), hMm);
        return wrote;
    }

    private static bool SetLength(Element element, Parameter? parameter, int mm)
    {
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            return false;
        // b/h on S-COLU-RFC-Rec are type parameters. Writing them through each instance
        // edits the FamilySymbol during regen and Revit drops the solid for the rest of the batch.
        if (element is FamilyInstance && parameter.Element.Id != element.Id)
            return false;
        parameter.Set(UnitConv.MmToInternal(mm));
        return true;
    }

    private static bool TryGetSectionParams(FamilySymbol symbol, out Parameter width, out Parameter depth, string? widthParam = null, string? heightParam = null)
    {
        width = depth = null!;
        if (!string.IsNullOrWhiteSpace(widthParam) && !string.IsNullOrWhiteSpace(heightParam))
        {
            var namedWidth = symbol.LookupParameter(widthParam);
            var namedHeight = symbol.LookupParameter(heightParam);
            if (IsLength(namedWidth) && IsLength(namedHeight))
            {
                width = namedWidth!;
                depth = namedHeight!;
                return true;
            }
        }

        var w = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
        var d = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
        if (IsLength(w) && IsLength(d))
        {
            width = w!;
            depth = d!;
            return true;
        }

        w = LookupLength(symbol, WidthParamNames);
        d = LookupLength(symbol, DepthParamNames);
        if (w is null || d is null)
            return false;
        width = w;
        depth = d;
        return true;
    }

    private static Parameter? LookupLength(FamilySymbol symbol, string[] names)
    {
        foreach (var name in names)
        {
            var p = symbol.LookupParameter(name);
            if (IsLength(p))
                return p;
        }

        return null;
    }

    private static bool IsLength(Parameter? p) =>
        p is not null && p.StorageType == StorageType.Double;

    private static WallType ResolveWallType(Document doc, WallType source, int? thicknessMm, string? function)
    {
        var thick = thicknessMm is > 0 ? thicknessMm.Value : (int)Math.Round(WallWidthMm(source));
        var name = WallTypeName(source.Name, thick, function, source);
        var existing = FindWallType(doc, name);
        var target = existing;
        if (target is null && string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase))
            target = source;
        if (target is null)
            target = source.Duplicate(name) as WallType ?? source;

        if (Math.Abs(WallWidthMm(target) - thick) > 5)
            SetWallThickness(target, thick);
        SetWallFunction(target, function);
        return target;
    }

    private static string WallTypeName(string sourceName, int thicknessMm, string? function, WallType source)
    {
        var match = Regex.Match(sourceName, @"^(.*?)(\d+)(\D*)$");
        var stem = match.Success
            ? match.Groups[1].Value + thicknessMm + match.Groups[3].Value
            : sourceName + "-" + thicknessMm;
        if (string.IsNullOrWhiteSpace(function) || !TryWallFunction(function, out var wanted))
            return stem;
        var current = source.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
        if (current is not null && current.StorageType == StorageType.Integer && current.AsInteger() == (int)wanted)
            return stem;
        return stem + "-" + function;
    }

    private static void SetWallThickness(WallType type, int thicknessMm)
    {
        var structure = type.GetCompoundStructure();
        if (structure is null || structure.LayerCount == 0)
            throw new InvalidOperationException(
                $"Type '{type.Name}' không phải tường basic, không đổi được bề dày.");

        var widths = new double[structure.LayerCount];
        var sum = 0.0;
        for (var i = 0; i < structure.LayerCount; i++)
        {
            widths[i] = structure.GetLayerWidth(i);
            sum += widths[i];
        }

        if (sum < 1e-9)
            throw new InvalidOperationException(
                $"Type '{type.Name}' không có lớp để đặt bề dày.");

        var target = UnitConv.MmToInternal(thicknessMm);
        var scale = target / sum;
        for (var i = 0; i < structure.LayerCount; i++)
        {
            if (widths[i] <= 1e-9)
                continue;
            structure.SetLayerWidth(i, widths[i] * scale);
        }

        type.SetCompoundStructure(structure);
    }

    private static void SetWallFunction(WallType type, string? function)
    {
        if (!TryWallFunction(function, out var value))
            return;
        var parameter = type.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer)
            return;
        parameter.Set((int)value);
    }

    private static bool TryWallFunction(string? function, out WallFunction value)
    {
        value = WallFunction.Interior;
        return !string.IsNullOrWhiteSpace(function) &&
               Enum.TryParse(function, ignoreCase: true, out value);
    }

    private static double WallWidthMm(WallType type) =>
        UnitConv.ToMm(type.Width);

    private static WallType? FindWallType(Document doc, string typeName) =>
        new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
            .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));

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
        if (exact != null)
            return exact;

        var want = SectionNumbers(typeName);
        if (want.Count != 2)
            return null;

        FamilySymbol? swapped = null;
        foreach (var symbol in symbols)
        {
            var have = SectionNumbers(symbol.Name);
            if (have.Count < 2 || !have.Contains(want[0]) || !have.Contains(want[1]))
                continue;
            if (have[0] == want[0] && have[1] == want[1])
                return symbol;
            swapped ??= symbol;
        }

        return swapped;
    }

    private static List<int> SectionNumbers(string name)
    {
        var numbers = new List<int>();
        foreach (Match match in Regex.Matches(name, @"\d+"))
        {
            if (!int.TryParse(match.Value, out var value))
                continue;
            if (value < 50 || value > 3000)
                continue;
            numbers.Add(value);
        }

        return numbers;
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

        var v1 = (a1 - a0).Normalize();
        var v2 = (end - start).Normalize();
        if (v1.IsZeroLength() || v2.IsZeroLength())
            return d1 <= tolMm * 2;

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
        // Rotated walls/columns are whole mm along their own axes; world rounding would break that.
        xyz = ReqBool(fields, "rotatedFrame") == true
            ? new XYZ(UnitConv.MmToInternal(scaled[0]), UnitConv.MmToInternal(scaled[1]), UnitConv.MmToInternal(scaled[2]))
            : UnitConv.XyzFromMm(scaled);
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
