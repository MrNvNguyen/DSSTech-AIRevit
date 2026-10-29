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
                if (floor.FloorType.Id != floorType.Id || floor.LevelId != level.Id)
                    continue;
                if (BoundaryNear(floor, loops))
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
        var widthMm = ReqInt(op.Fields, "widthMm");
        var depthMm = ReqInt(op.Fields, "depthMm") ?? ReqInt(op.Fields, "heightMm");
        var sourceFamily = ReqString(op.Fields, "sourceFamily");

        FamilySymbol? symbol;
        var createdNewType = false;
        if (widthMm is > 0 && depthMm is > 0)
        {
            symbol = CategoryDispatcher.ResolveBeamSymbol(
                doc, widthMm.Value, depthMm.Value, sourceFamily, out createdNewType,
                ReqString(op.Fields, "widthParam"), ReqString(op.Fields, "heightParam"));
            if (symbol is null)
                return Fail(op, "type_unresolved",
                    "Không tạo được tiết diện dầm " + $"{widthMm}x{depthMm} mm" +
                    (string.IsNullOrWhiteSpace(sourceFamily) ? "" : $" trong family '{sourceFamily}'") +
                    ". Family cần tham số b/h.");
        }
        else
        {
            if (typeName is null)
                return Fail(op, "type_unresolved", "fields.familyType required — call list_types.");
            symbol = FindSymbol(doc, BuiltInCategory.OST_StructuralFraming, typeName);
            if (symbol is null)
                return Fail(op, "type_unresolved", $"Beam type '{typeName}' not found.");
        }

        var levelName = ReqString(op.Fields, "level");
        var level = FindLevel(doc, levelName);
        if (level is null)
            return Fail(op, "missing_level",
                string.IsNullOrWhiteSpace(levelName)
                    ? "Level not found."
                    : $"Level '{levelName}' not found.");

        // Curve Z must match the reference level before skip/create. CAD Z is unused; offsets go
        // on Start/End Level Offset. Z≈0 would match/re-host onto the lowest level (often HẦM).
        start = new XYZ(start.X, start.Y, level.Elevation);
        end = new XYZ(end.X, end.Y, level.Elevation);
        if (start.DistanceTo(end) < 1e-6)
            return Fail(op, "bad_curve", "Hai đầu dầm trùng nhau.");

        if (skip)
        {
            // Match by centerline proximity (not end-point equality): prior runs may have face-trimmed
            // ends while the plan uses support centers — still the same physical bay.
            FamilyInstance? match = null;
            var bestMm = double.MaxValue;
            foreach (var fi in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType()
                         .OfType<FamilyInstance>())
            {
                if (!SameBeamReferenceLevel(fi, level))
                    continue;
                if (fi.Location is not LocationCurve lc || lc.Curve is not Line line)
                    continue;
                if (!BeamCurvesNear(line, start, end, out var distMm))
                    continue;
                if (distMm >= bestMm)
                    continue;
                bestMm = distMm;
                match = fi;
            }

            if (match is not null)
            {
                if (!dryRun)
                {
                    try
                    {
                        if (match.Symbol.Id != symbol.Id)
                        {
                            EnsureSymbolActive(doc, symbol);
                            match.ChangeTypeId(symbol.Id);
                        }

                        if (match.Location is LocationCurve loc)
                        {
                            var z0 = loc.Curve.GetEndPoint(0).Z;
                            var z1 = loc.Curve.GetEndPoint(1).Z;
                            loc.Curve = Line.CreateBound(
                                new XYZ(start.X, start.Y, z0),
                                new XYZ(end.X, end.Y, z1));
                        }

                        PlacementApply.Apply(match, op.Fields, units);
                        EnsureBeamReferenceLevel(match, level);
                        PlacementApply.ApplyBeamJustificationDefaults(match);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // Keep the existing beam when pinned/locked; still report skip.
                    }
                }

                return Skip(op, match.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        EnsureSymbolActive(doc, symbol);
        var curve = Line.CreateBound(start, end);
        var created = doc.Create.NewFamilyInstance(curve, symbol, level, StructuralType.Beam);
        EnsureBeamReferenceLevel(created, level);
        PlacementApply.Apply(created, op.Fields, units);
        EnsureBeamReferenceLevel(created, level);
        // Last: wipe sticky Y Right / z Offset after level/offset writes.
        PlacementApply.ApplyBeamJustificationDefaults(created);
        var reason = createdNewType ? "newType" : null;
        return Created(op, created.Id.Value.ToString(), reason);
    }

    /// <summary>
    /// Same physical bay: parallel within ~2°, planned midpoint within 100 mm of the existing curve,
    /// and along-axis overlap of at least 50% of the shorter length.
    /// </summary>
    private static bool BeamCurvesNear(Line existing, XYZ start, XYZ end, out double midDistMm)
    {
        midDistMm = double.MaxValue;
        var a0 = existing.GetEndPoint(0);
        var a1 = existing.GetEndPoint(1);
        var aLen = a0.DistanceTo(a1);
        var bLen = start.DistanceTo(end);
        if (aLen < 1e-9 || bLen < 1e-9)
            return false;

        var au = (a1 - a0).Normalize();
        var bu = (end - start).Normalize();
        var angle = au.AngleTo(bu) * 180.0 / Math.PI;
        if (angle > 90)
            angle = 180 - angle;
        if (angle > 2.0)
            return false;

        var mid = start + (end - start) / 2;
        var projected = a0 + au * (mid - a0).DotProduct(au);
        // Drop Z so level-offset differences do not break plan match.
        var midXy = new XYZ(mid.X, mid.Y, 0);
        var projXy = new XYZ(projected.X, projected.Y, 0);
        midDistMm = UnitConv.ToMm(midXy.DistanceTo(projXy));
        if (midDistMm > 100)
            return false;

        var t0 = (a0 - start).DotProduct(bu);
        var t1 = (a1 - start).DotProduct(bu);
        var lo = Math.Max(0, Math.Min(t0, t1));
        var hi = Math.Min(bLen, Math.Max(t0, t1));
        var overlap = hi - lo;
        return UnitConv.ToMm(overlap) >= UnitConv.ToMm(Math.Min(aLen, bLen)) * 0.5;
    }

    private static bool SameBeamReferenceLevel(FamilyInstance fi, Level level)
    {
        var refId = fi.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId();
        if (refId is not null && refId != ElementId.InvalidElementId)
            return refId == level.Id;
        return fi.LevelId == level.Id;
    }

    private static void EnsureBeamReferenceLevel(FamilyInstance beam, Level level)
    {
        try
        {
            var parameter = beam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            if (parameter is null || parameter.IsReadOnly)
                return;
            if (parameter.AsElementId() != level.Id)
                parameter.Set(level.Id);
        }
        catch (Exception)
        {
            // Reference Level stays whatever NewFamilyInstance assigned.
        }
    }

    private static int? ReqInt(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
            return i;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
            return (int)Math.Round(d);
        return null;
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

        if (skip)
        {
            foreach (var fi in new FilteredElementCollector(doc)
                         .OfCategory(bic)
                         .WhereElementIsNotElementType()
                         .OfType<FamilyInstance>())
            {
                if (fi.Symbol.Id != symbol.Id || fi.Host?.Id != host.Id)
                    continue;
                if (fi.Location is LocationPoint lp && PointNear(lp.Point, point))
                    return Skip(op, fi.Id.Value.ToString(), "skipIfExists");
            }
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        EnsureSymbolActive(doc, symbol);
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

        if (skip)
        {
            foreach (var existing in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_Rooms)
                         .WhereElementIsNotElementType())
            {
                if (existing.LevelId != level.Id)
                    continue;
                if (existing.Location is LocationPoint lp && PlanPointNear(lp.Point, xyz))
                    return Skip(op, existing.Id.Value.ToString(), "skipIfExists");
            }
        }

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

        if (skip && TryExistingAtPoint(doc, bic, symbol.Id, xyz) is { } existingFamily)
            return Skip(op, existingFamily, "skipIfExists");

        if (dryRun)
            return Created(op, null, "dryRun");

        EnsureSymbolActive(doc, symbol);
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

        var sourceFamily = ReqString(op.Fields, "sourceFamily");
        var kind = ReqString(op.Fields, "kind");
        var typeMark = ReqString(op.Fields, "typeMark");
        var isPileCap = string.Equals(kind, "PileCap", StringComparison.OrdinalIgnoreCase);
        var resolveByTypeMark = ReqBool(op.Fields, "resolveByTypeMark") == true
                                || (!string.IsNullOrWhiteSpace(typeMark) && isPileCap);

        FamilySymbol? symbol = null;

        // Đài cọc: place the pre-setup Type whose Type Mark matches CAD label — never invent WxH.
        if (resolveByTypeMark || isPileCap)
        {
            var markKey = !string.IsNullOrWhiteSpace(typeMark) ? typeMark : null;
            if (markKey is not null)
            {
                symbol = FindFoundationByTypeMark(doc, markKey, sourceFamily);
                if (symbol is null)
                    return Fail(op, "type_unresolved",
                        $"Đài cọc: không có Structural Foundation Type Mark khớp '{markKey.Trim()}' (bỏ qua; không nhân WxH từ CAD).");
            }
            else
            {
                // No CAD label: UI type fallback only.
                symbol = FindSymbol(doc, BuiltInCategory.OST_StructuralFoundation, typeName);
                if (symbol is null && !string.IsNullOrWhiteSpace(sourceFamily))
                {
                    symbol = FoundationSymbols(doc)
                        .FirstOrDefault(s =>
                            string.Equals(s.FamilyName, sourceFamily, StringComparison.OrdinalIgnoreCase));
                }

                if (symbol is null)
                    return Fail(op, "type_unresolved", $"Foundation '{typeName}' not found.");
            }
        }
        else
        {
            var widthMm = ReqInt(op.Fields, "widthMm");
            var depthMm = ReqInt(op.Fields, "depthMm");
            var diameterMm = ReqInt(op.Fields, "diameterMm");
            var circular = diameterMm is > 0
                           || (widthMm is > 0 && depthMm is > 0 && widthMm == depthMm
                               && LooksCircularFoundationType(typeName, sourceFamily));

            // CAD footprint size wins over the UI-selected type leaf (UI still supplies the family).
            var resolvedDia = diameterMm ?? widthMm;
            if (circular && resolvedDia is > 0)
                symbol = ResolveFoundationSymbol(doc, resolvedDia.Value, resolvedDia.Value, sourceFamily, typeName, circular: true, out _);
            else if (widthMm is > 0 && depthMm is > 0)
                symbol = ResolveFoundationSymbol(doc, widthMm.Value, depthMm.Value, sourceFamily, typeName, circular: false, out _);

            symbol ??= FindSymbol(doc, BuiltInCategory.OST_StructuralFoundation, typeName);
            if (symbol is null && !string.IsNullOrWhiteSpace(sourceFamily))
            {
                // familyType may be "D500" after CAD sizing — fall back to any type in the UI family.
                symbol = FoundationSymbols(doc)
                    .FirstOrDefault(s => string.Equals(s.FamilyName, sourceFamily, StringComparison.OrdinalIgnoreCase));
            }

            if (symbol is null)
                return Fail(op, "type_unresolved", $"Foundation '{typeName}' not found.");
        }

        var level = FindLevel(doc, ReqString(op.Fields, "level"));
        if (level is null)
            return Fail(op, "missing_level", "Level not found.");

        if (!TryXyz(op.Fields, "xyzMm", "xyz", units, out var xyz))
            return Fail(op, "missing_field", "fields.xyzMm required for isolated footing/pile.");

        if (skip && TryExistingFoundationAtPoint(doc, symbol.Id, xyz, out var existingFi) && existingFi is not null)
        {
            var markFilled = PlacementApply.TrySetMark(existingFi, ReqString(op.Fields, "mark"));
            return Skip(op, existingFi.Id.Value.ToString(), markFilled ? "skipIfExists,mark" : "skipIfExists");
        }

        if (dryRun)
            return Created(op, null, "dryRun");

        EnsureSymbolActive(doc, symbol);
        var created = doc.Create.NewFamilyInstance(xyz, symbol, level, StructuralType.Footing);
        // Foundations use INSTANCE_FREE_HOST_OFFSET_PARAM (not column base/top). Length = chiều sâu cọc.
        PlacementApply.Apply(created, op.Fields, units);
        ApplyFoundationOffset(created, op.Fields, units);
        ApplyFoundationLength(created, op.Fields, units);
        // Planner rotationDeg = CAD long-edge angle (world +X = 0°). Family width→+X, depth→+Y.
        // When depth > width the long side is +Y → RevitRot = cad − 90° (else CAD horizontal reads vertical).
        var cadRot = ReqDouble(op.Fields, "rotationDeg") ?? 0;
        var rot = cadRot;
        if (TryGetFoundationSectionMm(symbol, out var famB, out var famH) && famH > famB)
            rot = cadRot - 90.0;
        if (Math.Abs(rot) > 0.01)
        {
            var axis = Line.CreateBound(xyz, xyz + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, created.Id, axis, rot * Math.PI / 180.0);
        }

        return Created(op, created.Id.Value.ToString());
    }

    private static readonly string[] FoundationDiameterNames =
    {
        "Diameter", "diameter", "Đường kính", "Dia", "D", "b", "B", "Width", "width"
    };

    private static readonly string[] FoundationWidthNames =
    {
        "b", "B", "Width", "width", "W", "Rộng", "Chiều rộng"
    };

    private static readonly string[] FoundationDepthNames =
    {
        "h", "H", "Depth", "depth", "Height", "height", "Cao", "Chiều cao"
    };

    /// <summary>
    /// Find or duplicate a Structural Foundation type for CAD pile size.
    /// Circular → type name D{dia}; rectangular → {w}x{d}. UI family is the template source.
    /// </summary>
    private static FamilySymbol? ResolveFoundationSymbol(
        Document doc,
        int widthMm,
        int depthMm,
        string? sourceFamily,
        string? typeHint,
        bool circular,
        out bool createdNewType)
    {
        createdNewType = false;
        var symbols = FoundationSymbols(doc);
        if (!string.IsNullOrWhiteSpace(sourceFamily))
        {
            symbols = symbols
                .Where(s => string.Equals(s.FamilyName, sourceFamily, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else if (!string.IsNullOrWhiteSpace(typeHint))
        {
            var hint = FindSymbol(doc, BuiltInCategory.OST_StructuralFoundation, typeHint);
            if (hint is not null)
            {
                symbols = symbols
                    .Where(s => string.Equals(s.FamilyName, hint.FamilyName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                sourceFamily = hint.FamilyName;
            }
        }

        if (symbols.Count == 0)
            return null;

        foreach (var symbol in symbols)
        {
            if (circular)
            {
                if (TryGetFoundationDiameterMm(symbol, out var dia) && dia == widthMm)
                    return symbol;
                if (TypeNameMatchesDiameter(symbol.Name, widthMm))
                    return symbol;
            }
            else if (TryGetFoundationSectionMm(symbol, out var b, out var h) && b == widthMm && h == depthMm)
            {
                return symbol;
            }
        }

        var wantedName = circular ? $"D{widthMm}" : $"{widthMm}x{depthMm}";
        var existing = symbols.FirstOrDefault(s =>
            string.Equals(s.Name, wantedName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (circular && TrySetFoundationDiameterMm(existing, widthMm))
            {
                doc.Regenerate();
                return existing;
            }

            if (!circular && TrySetFoundationSectionMm(existing, widthMm, depthMm))
            {
                doc.Regenerate();
                return existing;
            }

            return existing;
        }

        var template = symbols.FirstOrDefault(s =>
            circular ? TryGetFoundationDiameterMm(s, out _) || TypeNameLooksDiameter(s.Name)
                     : TryGetFoundationSectionMm(s, out _, out _))
                      ?? symbols[0];

        FamilySymbol? duplicate;
        try
        {
            duplicate = template.Duplicate(wantedName) as FamilySymbol;
        }
        catch
        {
            return null;
        }

        if (duplicate is null)
            return null;

        if (circular)
        {
            if (!TrySetFoundationDiameterMm(duplicate, widthMm) && !TypeNameMatchesDiameter(duplicate.Name, widthMm))
                return duplicate; // name encodes size even if params are locked
        }
        else if (!TrySetFoundationSectionMm(duplicate, widthMm, depthMm))
        {
            return duplicate;
        }

        doc.Regenerate();
        createdNewType = true;
        return duplicate;
    }

    private static List<FamilySymbol> FoundationSymbols(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_StructuralFoundation)
            .Cast<FamilySymbol>()
            .ToList();

    /// <summary>
    /// Match Structural Foundation by Type Mark (trim + case-insensitive; Đ preserved),
    /// or by type-name pile-cap id prefix when Type Mark is empty (<c>Đ4A-…</c>).
    /// Prefer UI sourceFamily when set, then any foundation with that mark.
    /// </summary>
    private static FamilySymbol? FindFoundationByTypeMark(Document doc, string typeMark, string? sourceFamily)
    {
        var want = NormalizeTypeMark(typeMark);
        if (want.Length == 0)
            return null;

        var all = FoundationSymbols(doc);
        if (!string.IsNullOrWhiteSpace(sourceFamily))
        {
            var inFamily = all
                .Where(s => string.Equals(s.FamilyName, sourceFamily, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var hit = inFamily.FirstOrDefault(s => FoundationSymbolMatchesMark(s, want));
            if (hit is not null)
                return hit;
        }

        // Prefer exact Type Mark over type-name token across the whole catalog.
        return all.FirstOrDefault(s => TypeMarkEquals(ReadTypeMark(s), want))
               ?? all.FirstOrDefault(s => FoundationSymbolMatchesMark(s, want));
    }

    private static bool FoundationSymbolMatchesMark(FamilySymbol symbol, string normalizedWant)
    {
        if (TypeMarkEquals(ReadTypeMark(symbol), normalizedWant))
            return true;
        return TypeNameLeafMatchesMark(symbol.Name, normalizedWant);
    }

    /// <summary>
    /// Type name leaf encodes mark: <c>Đ4A</c> or <c>Đ4A-2500x2500x1100mm</c> (Đ4 ≠ Đ4A).
    /// </summary>
    private static bool TypeNameLeafMatchesMark(string? typeName, string normalizedWant)
    {
        if (string.IsNullOrWhiteSpace(typeName) || normalizedWant.Length == 0)
            return false;
        var leaf = typeName.Trim();
        var colon = leaf.LastIndexOf(':');
        if (colon >= 0 && colon + 1 < leaf.Length)
            leaf = leaf.Substring(colon + 1).Trim();
        var norm = NormalizeTypeMark(leaf);
        if (norm == normalizedWant)
            return true;
        return norm.StartsWith(normalizedWant + "-", StringComparison.Ordinal)
               || norm.StartsWith(normalizedWant + "_", StringComparison.Ordinal)
               || norm.StartsWith(normalizedWant + " ", StringComparison.Ordinal);
    }

    private static string? ReadTypeMark(FamilySymbol symbol)
    {
        try
        {
            return symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeTypeMark(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();

    private static bool TypeMarkEquals(string? mark, string normalizedWant) =>
        NormalizeTypeMark(mark) == normalizedWant;

    private static bool LooksCircularFoundationType(string? typeName, string? sourceFamily)
    {
        var s = $"{sourceFamily} {typeName}";
        return s.IndexOf("Cir", StringComparison.OrdinalIgnoreCase) >= 0
               || System.Text.RegularExpressions.Regex.IsMatch(s, @"\bD\s*\d{2,4}\b",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool TypeNameLooksDiameter(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^[Dd]\s*\d{2,4}\b");

    private static bool TypeNameMatchesDiameter(string? name, int diaMm) =>
        !string.IsNullOrWhiteSpace(name) &&
        System.Text.RegularExpressions.Regex.IsMatch(name, $@"^[Dd]\s*{diaMm}\b");

    private static bool TryGetFoundationDiameterMm(FamilySymbol symbol, out int diaMm)
    {
        diaMm = 0;
        foreach (var name in FoundationDiameterNames)
        {
            var p = symbol.LookupParameter(name);
            if (p is not { StorageType: StorageType.Double } || p.AsDouble() <= 0)
                continue;
            diaMm = (int)Math.Round(UnitConv.ToMm(p.AsDouble()));
            if (diaMm > 0)
                return true;
        }

        var w = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
        if (w is { StorageType: StorageType.Double } && w.AsDouble() > 0)
        {
            diaMm = (int)Math.Round(UnitConv.ToMm(w.AsDouble()));
            return diaMm > 0;
        }

        return false;
    }

    private static bool TrySetFoundationDiameterMm(FamilySymbol symbol, int diaMm)
    {
        var wrote = false;
        var internalLen = UnitConv.MmToInternal(diaMm);
        foreach (var name in FoundationDiameterNames)
        {
            var p = symbol.LookupParameter(name);
            if (p is null || p.IsReadOnly || p.StorageType != StorageType.Double)
                continue;
            p.Set(internalLen);
            wrote = true;
        }

        var w = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
        if (w is { IsReadOnly: false, StorageType: StorageType.Double })
        {
            w.Set(internalLen);
            wrote = true;
        }

        var h = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
        if (h is { IsReadOnly: false, StorageType: StorageType.Double })
        {
            h.Set(internalLen);
            wrote = true;
        }

        return wrote;
    }

    private static bool TryGetFoundationSectionMm(FamilySymbol symbol, out int bMm, out int hMm)
    {
        bMm = hMm = 0;
        var w = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
        var d = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
        if (w is { StorageType: StorageType.Double } && d is { StorageType: StorageType.Double }
            && w.AsDouble() > 0 && d.AsDouble() > 0)
        {
            bMm = (int)Math.Round(UnitConv.ToMm(w.AsDouble()));
            hMm = (int)Math.Round(UnitConv.ToMm(d.AsDouble()));
            return bMm > 0 && hMm > 0;
        }

        Parameter? pw = null, ph = null;
        foreach (var name in FoundationWidthNames)
        {
            var p = symbol.LookupParameter(name);
            if (p is { StorageType: StorageType.Double } && p.AsDouble() > 0)
            {
                pw = p;
                break;
            }
        }

        foreach (var name in FoundationDepthNames)
        {
            var p = symbol.LookupParameter(name);
            if (p is { StorageType: StorageType.Double } && p.AsDouble() > 0)
            {
                ph = p;
                break;
            }
        }

        if (pw is null || ph is null)
            return false;
        bMm = (int)Math.Round(UnitConv.ToMm(pw.AsDouble()));
        hMm = (int)Math.Round(UnitConv.ToMm(ph.AsDouble()));
        return bMm > 0 && hMm > 0;
    }

    private static bool TrySetFoundationSectionMm(FamilySymbol symbol, int widthMm, int depthMm)
    {
        var wrote = false;
        var wi = UnitConv.MmToInternal(widthMm);
        var di = UnitConv.MmToInternal(depthMm);
        var w = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
        var h = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
        if (w is { IsReadOnly: false, StorageType: StorageType.Double })
        {
            w.Set(wi);
            wrote = true;
        }

        if (h is { IsReadOnly: false, StorageType: StorageType.Double })
        {
            h.Set(di);
            wrote = true;
        }

        foreach (var name in FoundationWidthNames)
        {
            var p = symbol.LookupParameter(name);
            if (p is null || p.IsReadOnly || p.StorageType != StorageType.Double)
                continue;
            p.Set(wi);
            wrote = true;
        }

        foreach (var name in FoundationDepthNames)
        {
            var p = symbol.LookupParameter(name);
            if (p is null || p.IsReadOnly || p.StorageType != StorageType.Double)
                continue;
            p.Set(di);
            wrote = true;
        }

        return wrote;
    }

    private static void ApplyFoundationOffset(FamilyInstance created, JsonElement fields, string? units)
    {
        var mm = ReqDouble(fields, "offsetMm") ?? ReqDouble(fields, "baseOffsetMm");
        if (mm is null)
            return;
        try
        {
            var p = created.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
            if (p is null || p.IsReadOnly || p.StorageType != StorageType.Double)
                return;
            p.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(mm.Value, units)));
        }
        catch (Exception)
        {
            // Offset is optional when the family template omits free-host params.
        }
    }

    /// <summary>
    /// Set pile Length on the instance from lengthMm / depthMm / heightMm.
    /// Type Length is left alone (mutating the type would resize every pile of that type).
    /// </summary>
    private static void ApplyFoundationLength(FamilyInstance created, JsonElement fields, string? units)
    {
        var mm = ReqDouble(fields, "lengthMm") ?? ReqDouble(fields, "depthMm") ?? ReqDouble(fields, "heightMm");
        if (mm is null || mm.Value <= 0)
            return;
        var internalLen = UnitConv.MmToInternal(UnitConv.ScaleToMm(mm.Value, units));
        foreach (var name in FoundationLengthNames)
        {
            try
            {
                var p = created.LookupParameter(name);
                if (p is null || p.IsReadOnly || p.StorageType != StorageType.Double)
                    continue;
                // Skip type-bound parameters exposed via LookupParameter on the instance.
                var onType = false;
                foreach (Parameter tp in created.Symbol.Parameters)
                {
                    if (tp.Id == p.Id)
                    {
                        onType = true;
                        break;
                    }
                }

                if (onType)
                    continue;
                p.Set(internalLen);
                return;
            }
            catch (Exception)
            {
                // Try next name.
            }
        }
    }

    private static readonly string[] FoundationLengthNames =
    {
        "Length", "length", "L", "Chiều dài", "Chiều sâu", "Depth", "depth", "Pile Length", "PileDepth"
    };

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
        name = name.Trim();
        var exact = levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;
        // Some projects pad level names; still refuse silent fallback to the lowest level.
        return levels.FirstOrDefault(l =>
            string.Equals(l.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
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
        // Planner offsets are 5 mm from grids on fractional world mm; world rounding would undo that.
        xyz = ReqBool(fields, "exactMm") == true || ReqBool(fields, "rotatedFrame") == true
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

    private static bool? ReqBool(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        return el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : null;
    }

    private static double? ReqDouble(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;
    }

    private static string? TryExistingAtPoint(Document doc, BuiltInCategory bic, ElementId symbolId, XYZ xyz)
    {
        foreach (var fi in new FilteredElementCollector(doc)
                     .OfCategory(bic)
                     .WhereElementIsNotElementType()
                     .OfType<FamilyInstance>())
        {
            if (fi.Symbol.Id != symbolId)
                continue;
            if (fi.Location is LocationPoint lp && PointNear(lp.Point, xyz))
                return fi.Id.Value.ToString();
        }

        return null;
    }

    private static bool TryExistingFoundationAtPoint(
        Document doc, ElementId symbolId, XYZ xyz, out FamilyInstance? existing)
    {
        existing = null;
        foreach (var fi in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                     .WhereElementIsNotElementType()
                     .OfType<FamilyInstance>())
        {
            if (fi.Symbol.Id != symbolId)
                continue;
            if (fi.Location is LocationPoint lp && PointNear(lp.Point, xyz))
            {
                existing = fi;
                return true;
            }
        }

        return false;
    }

    private static bool PointNear(XYZ a, XYZ b) =>
        UnitConv.ToMm(a.DistanceTo(b)) <= GeomTolMm;

    private static bool PlanPointNear(XYZ a, XYZ b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return UnitConv.ToMm(Math.Sqrt((dx * dx) + (dy * dy))) <= GeomTolMm;
    }

    private static bool BoundaryNear(Floor floor, List<List<XYZ>> proposed)
    {
        if (!TrySketchRings(floor, out var existing))
            return false;
        return RingsNear(existing, proposed);
    }

    private static bool TrySketchRings(Floor floor, out List<List<XYZ>> rings)
    {
        rings = new List<List<XYZ>>();
        try
        {
            var sketchId = floor.SketchId;
            if (sketchId == ElementId.InvalidElementId)
                return false;
            if (floor.Document.GetElement(sketchId) is not Sketch sketch || sketch.Profile is null || sketch.Profile.Size == 0)
                return false;

            foreach (CurveArray curveArray in sketch.Profile)
            {
                var ring = new List<XYZ>();
                foreach (Curve curve in curveArray)
                    ring.Add(curve.GetEndPoint(0));
                if (ring.Count >= 3)
                    rings.Add(ring);
            }
        }
        catch
        {
            rings.Clear();
            return false;
        }

        return rings.Count > 0;
    }

    private static bool RingsNear(List<List<XYZ>> existing, List<List<XYZ>> proposed)
    {
        if (existing.Count == 0 || existing.Count != proposed.Count)
            return false;

        var used = new bool[existing.Count];
        foreach (var ring in proposed)
        {
            var matched = false;
            for (var i = 0; i < existing.Count; i++)
            {
                if (used[i] || !RingNear(existing[i], ring))
                    continue;
                used[i] = true;
                matched = true;
                break;
            }

            if (!matched)
                return false;
        }

        return true;
    }

    private static bool RingNear(List<XYZ> a, List<XYZ> b)
    {
        var left = OpenRing(a);
        var right = OpenRing(b);
        if (left.Count != right.Count || left.Count < 3)
            return false;
        if (RingNearDirected(left, right))
            return true;

        var flipped = new List<XYZ>(right);
        flipped.Reverse();
        return RingNearDirected(left, flipped);
    }

    private static List<XYZ> OpenRing(List<XYZ> ring)
    {
        if (ring.Count >= 2 && PlanPointNear(ring[0], ring[ring.Count - 1]))
        {
            var copy = new List<XYZ>(ring);
            copy.RemoveAt(copy.Count - 1);
            return copy;
        }

        return ring;
    }

    private static bool RingNearDirected(List<XYZ> a, List<XYZ> b)
    {
        var count = a.Count;
        for (var shift = 0; shift < count; shift++)
        {
            var matches = true;
            for (var i = 0; i < count; i++)
            {
                if (PlanPointNear(a[i], b[(i + shift) % count]))
                    continue;
                matches = false;
                break;
            }

            if (matches)
                return true;
        }

        return false;
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
