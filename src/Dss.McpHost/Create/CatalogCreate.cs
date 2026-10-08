using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;
using Dss.McpContracts;

namespace Dss.McpHost.Create;

/// <summary>
/// R4-CATALOG extensions. Stairs/rebar/fittings/in-place remain OUT.
/// </summary>
internal static class CatalogCreate
{
    private const double GeomTolMm = 10.0;

    public static CreateItemResult CreateFloor(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        if (!TryLoops(op.Fields, units, out var loops) || loops.Count == 0)
            return Fail(op, "missing_field", "fields.loops required (array of xyzMm rings).");

        var typeName = ReqString(op.Fields, "floorType") ?? ReqString(op.Fields, "typeName");
        if (typeName is null)
            return Fail(op, "type_unresolved", "fields.floorType required — call list_types.");

        var floorType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
            .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (floorType is null)
            return Fail(op, "type_unresolved", $"FloorType '{typeName}' not found.");

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        if (skip)
        {
            foreach (var floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                if (floor.FloorType.Id != floorType.Id)
                    continue;
                if (floor.LevelId == level.Id)
                    return Skip(op, floor.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        var curveLoops = new List<CurveLoop>();
        foreach (var ring in loops)
        {
            if (ring.Count < 3)
                continue;
            var loop = new CurveLoop();
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                loop.Append(Line.CreateBound(a, b));
            }
            curveLoops.Add(loop);
        }

        if (curveLoops.Count == 0)
            return Fail(op, "invalid_loops", "Need at least one closed loop with ≥3 points.");

        var floorNew = Floor.Create(doc, curveLoops, floorType.Id, level.Id);
        return Created(op, floorNew.Id.Value.ToString());
    }

    public static CreateItemResult CreateBeam(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        if (!TryXyz(op.Fields, "startMm", "start", units, out var start) ||
            !TryXyz(op.Fields, "endMm", "end", units, out var end))
            return Fail(op, "missing_field", "fields.startMm/endMm required.");

        var typeName = ReqString(op.Fields, "familyType") ?? ReqString(op.Fields, "typeName");
        if (typeName is null)
            return Fail(op, "type_unresolved", "fields.familyType required — call list_types.");

        var symbol = FindSymbol(doc, BuiltInCategory.OST_StructuralFraming, typeName);
        if (symbol is null)
            return Fail(op, "type_unresolved", $"Beam type '{typeName}' not found.");

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        if (skip)
        {
            foreach (var fi in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType()
                         .OfType<FamilyInstance>())
            {
                if (fi.Symbol.Id != symbol.Id)
                    continue;
                if (fi.Location is LocationCurve lc && lc.Curve is Line line &&
                    LinesNear(line, start, end))
                    return Skip(op, fi.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        if (!symbol.IsActive)
            symbol.Activate();
        var curve = Line.CreateBound(start, end);
        var created = doc.Create.NewFamilyInstance(curve, symbol, level, StructuralType.Beam);
        return Created(op, created.Id.Value.ToString());
    }

    public static CreateItemResult CreateDoorOrWindow(Document doc, CreateOperation op, bool skip, bool dryRun, string? units, bool isDoor)
    {
        var bic = isDoor ? BuiltInCategory.OST_Doors : BuiltInCategory.OST_Windows;
        var typeName = ReqString(op.Fields, "familyType") ?? ReqString(op.Fields, "typeName");
        if (typeName is null)
            return Fail(op, "type_unresolved", "fields.familyType required.");

        var symbol = FindSymbol(doc, bic, typeName);
        if (symbol is null)
            return Fail(op, "type_unresolved", $"Type '{typeName}' not found.");

        Wall? host = null;
        if (ReqString(op.Fields, "wallId") is { } wallId && long.TryParse(wallId, out var wid))
            host = doc.GetElement(new ElementId(wid)) as Wall;

        if (host is null && TryXyz(op.Fields, "xyzMm", "xyz", units, out var near))
        {
            host = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Select(w => (w, dist: DistanceToWallMm(w, near)))
                .Where(x => x.dist < 200)
                .OrderBy(x => x.dist)
                .Select(x => x.w)
                .FirstOrDefault();
        }

        if (host is null)
            return Fail(op, "missing_host", "Need wallId or xyzMm near a wall (<200 mm).");

        var level = FindLevel(doc, ReqString(op.Fields, "level")) ??
                    doc.GetElement(host.LevelId) as Level;
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        XYZ point;
        if (!TryXyz(op.Fields, "xyzMm", "xyz", units, out point))
        {
            if (host.Location is LocationCurve lc)
                point = lc.Curve.Evaluate(0.5, true);
            else
                return Fail(op, "missing_field", "fields.xyzMm required.");
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        if (!symbol.IsActive)
            symbol.Activate();
        var created = doc.Create.NewFamilyInstance(point, symbol, host, level, StructuralType.NonStructural);
        return Created(op, created.Id.Value.ToString());
    }

    public static CreateItemResult CreateRoom(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        if (!TryXyz(op.Fields, "xyzMm", "uv", units, out var xyz) &&
            !TryXyz(op.Fields, "locationMm", "location", units, out xyz))
            return Fail(op, "missing_field", "fields.xyzMm/locationMm required.");

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        var name = ReqString(op.Fields, "name");
        var number = ReqString(op.Fields, "number");

        if (dryRun)
            return Created(op, null, "dryRun");

        var uv = new UV(xyz.X, xyz.Y);
        var room = doc.Create.NewRoom(level, uv);
        if (!string.IsNullOrWhiteSpace(name))
            room.Name = name;
        if (!string.IsNullOrWhiteSpace(number))
            room.Number = number;
        return Created(op, room.Id.Value.ToString());
    }

    public static CreateItemResult CreateCeiling(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        if (!TryLoops(op.Fields, units, out var loops) || loops.Count == 0)
            return Fail(op, "missing_field", "fields.loops required.");

        var typeName = ReqString(op.Fields, "ceilingType") ?? ReqString(op.Fields, "typeName");
        var ceilingType = typeName is null
            ? null
            : new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>()
                .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (typeName is not null && ceilingType is null)
            return Fail(op, "type_unresolved", $"CeilingType '{typeName}' not found.");
        ceilingType ??= new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>().FirstOrDefault();
        if (ceilingType is null)
            return Fail(op, "type_unresolved", "No CeilingType in document.");

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        if (dryRun)
            return Created(op, null, "dryRun");

        var curveLoops = BuildLoops(loops);
        var ceiling = Ceiling.Create(doc, curveLoops, ceilingType.Id, level.Id);
        var offsetMm = ReqDouble(op.Fields, "offsetMm");
        if (offsetMm is not null)
            ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM)
                ?.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(offsetMm.Value, units)));
        return Created(op, ceiling.Id.Value.ToString());
    }

    public static CreateItemResult CreateDuct(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        var ductTypes = new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>().ToList();
        var systemTypes = new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType)).Cast<MechanicalSystemType>().ToList();
        if (ductTypes.Count == 0 || systemTypes.Count == 0)
            return Skip(op, null, "no_duct_type");

        if (!TryXyz(op.Fields, "startMm", "start", units, out var start) ||
            !TryXyz(op.Fields, "endMm", "end", units, out var end))
            return Fail(op, "missing_field", "fields.startMm/endMm required.");

        var ductTypeName = ReqString(op.Fields, "ductType");
        var systemName = ReqString(op.Fields, "systemType") ?? ReqString(op.Fields, "system");
        var ductType = ductTypeName is null
            ? null
            : ductTypes.FirstOrDefault(t => string.Equals(t.Name, ductTypeName, StringComparison.OrdinalIgnoreCase));
        var systemType = systemName is null
            ? null
            : systemTypes.FirstOrDefault(t => string.Equals(t.Name, systemName, StringComparison.OrdinalIgnoreCase));
        if (ductTypeName is not null && ductType is null)
            return Fail(op, "type_unresolved", $"DuctType '{ductTypeName}' not found.");
        if (systemName is not null && systemType is null)
            return Fail(op, "type_unresolved", $"System '{systemName}' not found.");
        if (ductType is null || systemType is null)
            return Fail(op, "type_unresolved", "fields.ductType and systemType required.");

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        if (dryRun)
            return Created(op, null, "dryRun");

        var created = Duct.Create(doc, systemType.Id, ductType.Id, level.Id, start, end);
        var w = ReqDouble(op.Fields, "widthMm");
        var h = ReqDouble(op.Fields, "heightMm");
        var d = ReqDouble(op.Fields, "diameterMm");
        if (w is not null)
            created.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(w.Value, units)));
        if (h is not null)
            created.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(h.Value, units)));
        if (d is not null)
            created.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(d.Value, units)));
        return Created(op, created.Id.Value.ToString());
    }

    public static CreateItemResult CreateCableTray(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        var types = new FilteredElementCollector(doc).OfClass(typeof(CableTrayType)).Cast<CableTrayType>().ToList();
        if (types.Count == 0)
            return Skip(op, null, "no_tray_type");

        if (!TryXyz(op.Fields, "startMm", "start", units, out var start) ||
            !TryXyz(op.Fields, "endMm", "end", units, out var end))
            return Fail(op, "missing_field", "fields.startMm/endMm required.");

        var typeName = ReqString(op.Fields, "type") ?? ReqString(op.Fields, "typeName") ?? ReqString(op.Fields, "familyType");
        var trayType = typeName is null
            ? types[0]
            : types.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (trayType is null)
            return Fail(op, "type_unresolved", $"CableTrayType '{typeName}' not found.");

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        if (dryRun)
            return Created(op, null, "dryRun");

        var created = CableTray.Create(doc, trayType.Id, start, end, level.Id);
        var width = ReqDouble(op.Fields, "widthMm");
        if (width is not null)
            created.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)
                ?.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(width.Value, units)));
        return Created(op, created.Id.Value.ToString());
    }

    public static CreateItemResult CreateFamilyAtPoint(Document doc, CreateOperation op, bool skip, bool dryRun, string? units, BuiltInCategory bic)
    {
        if (!TryXyz(op.Fields, "xyzMm", "xyz", units, out var xyz))
            return Fail(op, "missing_field", "fields.xyzMm required.");

        var typeName = ReqString(op.Fields, "familyType") ?? ReqString(op.Fields, "typeName");
        if (typeName is null)
            return Fail(op, "type_unresolved", "fields.familyType required.");

        var symbol = FindSymbol(doc, bic, typeName);
        if (symbol is null)
            return Fail(op, "type_unresolved", $"Type '{typeName}' not found.");

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        if (dryRun)
            return Created(op, null, "dryRun");

        if (!symbol.IsActive)
            symbol.Activate();
        var created = doc.Create.NewFamilyInstance(xyz, symbol, level, StructuralType.NonStructural);
        var rot = ReqDouble(op.Fields, "rotationDeg");
        if (rot is not null)
        {
            var axis = Line.CreateBound(xyz, xyz + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, created.Id, axis, rot.Value * Math.PI / 180.0);
        }

        return Created(op, created.Id.Value.ToString());
    }

    public static CreateItemResult CreateFoundation(Document doc, CreateOperation op, bool skip, bool dryRun, string? units)
    {
        var typeName = ReqString(op.Fields, "familyType") ?? ReqString(op.Fields, "typeName");
        if (typeName is null)
            return Fail(op, "type_unresolved", "fields.familyType required.");

        var symbol = FindSymbol(doc, BuiltInCategory.OST_StructuralFoundation, typeName);
        if (symbol is null)
            return Fail(op, "type_unresolved", $"Foundation '{typeName}' not found.");

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        if (!TryXyz(op.Fields, "xyzMm", "xyz", units, out var xyz))
            return Fail(op, "missing_field", "fields.xyzMm required for isolated footing/pile.");

        if (dryRun)
            return Created(op, null, "dryRun");

        if (!symbol.IsActive)
            symbol.Activate();
        var created = doc.Create.NewFamilyInstance(xyz, symbol, level, StructuralType.Footing);
        return Created(op, created.Id.Value.ToString());
    }

    private static List<CurveLoop> BuildLoops(List<List<XYZ>> loops)
    {
        var curveLoops = new List<CurveLoop>();
        foreach (var ring in loops)
        {
            if (ring.Count < 3)
                continue;
            var loop = new CurveLoop();
            for (var i = 0; i < ring.Count; i++)
                loop.Append(Line.CreateBound(ring[i], ring[(i + 1) % ring.Count]));
            curveLoops.Add(loop);
        }

        return curveLoops;
    }

    private static bool TryLoops(JsonElement fields, string? units, out List<List<XYZ>> loops)
    {
        loops = new List<List<XYZ>>();
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty("loops", out var el) ||
            el.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var ringEl in el.EnumerateArray())
        {
            if (ringEl.ValueKind != JsonValueKind.Array)
                continue;
            var ring = new List<XYZ>();
            foreach (var pt in ringEl.EnumerateArray())
            {
                if (pt.ValueKind != JsonValueKind.Array)
                    continue;
                var arr = pt.EnumerateArray().Select(x => x.GetDouble()).ToArray();
                if (arr.Length < 2)
                    continue;
                var scaled = arr.Select(v => UnitConv.ScaleToMm(v, units)).ToArray();
                if (scaled.Length == 2)
                    scaled = new[] { scaled[0], scaled[1], 0 };
                ring.Add(UnitConv.XyzFromMm(scaled));
            }

            if (ring.Count >= 3)
                loops.Add(ring);
        }

        return loops.Count > 0;
    }

    private static FamilySymbol? FindSymbol(Document doc, BuiltInCategory bic, string typeName) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(bic)
            .Cast<FamilySymbol>()
            .FirstOrDefault(s =>
                string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"{s.FamilyName} : {s.Name}", typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"{s.FamilyName}:{s.Name}", typeName, StringComparison.OrdinalIgnoreCase));

    private static Level? FindLevel(Document doc, string? name)
    {
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        if (string.IsNullOrWhiteSpace(name))
            return levels.OrderBy(l => l.Elevation).FirstOrDefault();
        return levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryXyz(JsonElement fields, string mmKey, string altKey, string? units, out XYZ xyz)
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

    private static bool LinesNear(Line existing, XYZ start, XYZ end)
    {
        var a0 = existing.GetEndPoint(0);
        var a1 = existing.GetEndPoint(1);
        var d = Math.Min(
            UnitConv.ToMm(a0.DistanceTo(start)) + UnitConv.ToMm(a1.DistanceTo(end)),
            UnitConv.ToMm(a0.DistanceTo(end)) + UnitConv.ToMm(a1.DistanceTo(start)));
        return d <= GeomTolMm * 2;
    }

    private static double DistanceToWallMm(Wall wall, XYZ p)
    {
        if (wall.Location is not LocationCurve lc)
            return double.MaxValue;
        return UnitConv.ToMm(lc.Curve.Distance(p));
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
