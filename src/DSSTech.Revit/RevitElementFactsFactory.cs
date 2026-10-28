using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DSSTech.Core;

namespace DSSTech.Revit;

internal sealed class RevitElementFactsFactory
{
    private readonly Document _doc;
    public RevitElementFactsFactory(Document doc) => _doc = doc;

    public ElementFacts Create(Element e)
    {
        var category = e.Category?.BuiltInCategory.ToString() ?? "";
        var type = _doc.GetElement(e.GetTypeId());
        var facts = new ElementFacts
        {
            ElementId = e.Id.ToString(),
            RevitCategory = category,
            FamilyName = GetFamilyName(e, type),
            TypeName = type?.Name ?? e.Name,
            MaterialName = GetMaterialNames(e)
        };

        AddCategoryFacts(e, type, facts);
        AddSharedParameters(e, type, facts);
        ApplyInOutClassification(facts);
        AddDocumentMetadata(e, facts);
        CaptureQuantityCandidates(e, facts);
        ApplyMappedQuantity(e, facts);
        AddFallbackGeometry(e, facts);
        AddQuantityTableFacts(e, type, facts);
        return facts;
    }

    /// <summary>Q1 fields for element quantity table (Mark/Host/Thickness/Diameter/Grade/Weight).</summary>
    private void AddQuantityTableFacts(Element e, Element? type, ElementFacts f)
    {
        var mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        if (!string.IsNullOrWhiteSpace(mark))
            f.Properties["Mark"] = mark.Trim();

        var hostId = ResolveHostId(e);
        if (!string.IsNullOrWhiteSpace(hostId))
            f.Properties["HostId"] = hostId;

        if (TryThicknessM(e, out var thickM))
            f.Properties["ThicknessM"] = thickM;

        if (TryDiameterMm(e, out var diaMm))
            f.Properties["DiameterMm"] = diaMm;

        var grade = FirstNonEmpty(f,
            "ConcreteGrade", "SteelGrade", "MortarGrade", "BrickType", "MaterialGrade");
        if (!string.IsNullOrWhiteSpace(grade))
            f.Properties["MaterialGrade"] = grade;

        if (TryWeightKg(e, f, out var kg))
            f.Properties["WeightKg"] = kg;

        if (!f.Properties.ContainsKey("LengthM") && TryLength(e, out var lenM))
            f.Properties["LengthM"] = lenM;
        if (!f.Properties.ContainsKey("HeightM") && TryElementHeight(e, out var hM))
            f.Properties["HeightM"] = hM;
        if (!f.Properties.ContainsKey("WidthM") && TryPlanWidthCm(e, out var wCm) && wCm > 0)
            f.Properties["WidthM"] = wCm / 100.0;
    }

    private static string ResolveHostId(Element e)
    {
        try
        {
            if (e is FamilyInstance fi && fi.Host is not null)
                return fi.Host.Id.ToString();
            if (e is Rebar rebar)
            {
                var hostId = rebar.GetHostId();
                if (hostId != ElementId.InvalidElementId)
                    return hostId.ToString();
            }
        }
        catch
        {
            // ignore
        }

        return "";
    }

    private static bool TryThicknessM(Element e, out double thickM)
    {
        thickM = 0;
        try
        {
            if (e is Wall wall)
            {
                thickM = UnitUtils.ConvertFromInternalUnits(wall.WallType.Width, UnitTypeId.Meters);
                return thickM > 1e-9;
            }

            if (e is Floor)
            {
                var p = e.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                if (p?.StorageType == StorageType.Double && p.HasValue)
                {
                    thickM = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Meters);
                    return thickM > 1e-9;
                }
            }

            if (e is RoofBase)
            {
                var p = e.get_Parameter(BuiltInParameter.ROOF_ATTR_THICKNESS_PARAM);
                if (p?.StorageType == StorageType.Double && p.HasValue)
                {
                    thickM = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Meters);
                    return thickM > 1e-9;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private bool TryDiameterMm(Element e, out double diaMm)
    {
        diaMm = 0;
        try
        {
            if (e is Rebar rebar)
            {
                var barType = _doc.GetElement(rebar.GetTypeId()) as RebarBarType;
                if (barType is not null)
                {
                    diaMm = UnitUtils.ConvertFromInternalUnits(barType.BarNominalDiameter, UnitTypeId.Millimeters);
                    return diaMm > 1e-9;
                }
            }

            var pipe = e.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (pipe?.StorageType == StorageType.Double && pipe.HasValue)
            {
                diaMm = UnitUtils.ConvertFromInternalUnits(pipe.AsDouble(), UnitTypeId.Millimeters);
                return diaMm > 1e-9;
            }

            if (e.LookupParameter("Diameter") is { StorageType: StorageType.Double, HasValue: true } d)
            {
                diaMm = UnitUtils.ConvertFromInternalUnits(d.AsDouble(), UnitTypeId.Millimeters);
                return diaMm > 1e-9;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static string FirstNonEmpty(ElementFacts f, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (f.Properties.TryGetValue(key, out var v))
            {
                var s = Convert.ToString(v)?.Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }
        }

        return "";
    }

    private bool TryWeightKg(Element e, ElementFacts f, out double kg)
    {
        kg = 0;
        // Rebar already sets Quantity in kg.
        if (string.Equals(f.QuantityUnit, "kg", StringComparison.OrdinalIgnoreCase) && f.Quantity > 0)
        {
            kg = f.Quantity;
            return true;
        }

        if (!f.Properties.TryGetValue("QuantityVolume", out var volObj) ||
            volObj is not double volM3 ||
            volM3 <= 0)
            return false;

        var density = ResolveDensityKgPerM3(f.MaterialName);
        if (density is null || density <= 0)
            return false;

        kg = volM3 * density.Value;
        return kg > 1e-9;
    }

    private static double? ResolveDensityKgPerM3(string materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName))
            return null;

        var path = Path.Combine(PluginPaths.Root, "Config", "material-density.json");
        if (!File.Exists(path))
            return DefaultDensity(materialName);

        try
        {
            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("densities", out var dens) &&
                dens.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in dens.EnumerateObject())
                {
                    if (materialName.Contains(prop.Name, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.TryGetDouble(out var d) && d > 0)
                        return d;
                }
            }
        }
        catch
        {
            // fall through
        }

        return DefaultDensity(materialName);
    }

    private static double? DefaultDensity(string materialName)
    {
        var n = materialName.ToLowerInvariant();
        if (n.Contains("concrete") || n.Contains("bê tông") || n.Contains("be tong"))
            return 2400;
        if (n.Contains("steel") || n.Contains("thép") || n.Contains("thep") || n.Contains("rebar"))
            return 7850;
        if (n.Contains("brick") || n.Contains("gạch") || n.Contains("gach"))
            return 1800;
        return null;
    }

    private void AddCategoryFacts(Element e, Element? type, ElementFacts f)
    {
        switch (f.RevitCategory)
        {
            case "OST_StructuralColumns": f.Properties["ElementType"] = "Column"; break;
            case "OST_Walls": f.Properties["ElementType"] = "Wall"; break;
            case "OST_Floors": f.Properties["ElementType"] = "Slab"; break;
            case "OST_StructuralFraming": f.Properties["ElementType"] = "Beam"; break;
            case "OST_StructuralFoundation": f.Properties["ElementType"] = "Foundation"; break;
            case "OST_Roofs": f.Properties["ElementType"] = "Roof"; break;
            case "OST_Ceilings": f.Properties["ElementType"] = "Ceiling"; break;
        }

        if (e is Wall wall)
        {
            f.Properties["Thickness"] = UnitUtils.ConvertFromInternalUnits(wall.WallType.Width, UnitTypeId.Centimeters);
            f.Properties["WallThickness"] = f.Properties["Thickness"];
            var h = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble();
            if (h.HasValue) f.Properties["ElementHeight"] = UnitUtils.ConvertFromInternalUnits(h.Value, UnitTypeId.Meters);
            f.Properties["SurfaceLocation"] = wall.WallType.Function == WallFunction.Exterior ? "Exterior" : "Interior";
            f.Properties["WallFunction"] = wall.WallType.Function.ToString();
            TrySetRoomFlags(wall, f);
        }

        if (e is FamilyInstance fi && fi.StructuralType == StructuralType.Column)
        {
            if (TryVolume(e, out var volume) && TryElementHeight(e, out var heightM) && heightM > 1e-9)
                f.Properties["CrossSectionArea"] = volume / heightM;
        }

        if (e is Rebar rebar)
        {
            var barType = _doc.GetElement(rebar.GetTypeId()) as RebarBarType;
            if (barType is not null)
                f.Properties["RebarDiameter"] = UnitUtils.ConvertFromInternalUnits(barType.BarNominalDiameter, UnitTypeId.Millimeters);
        }
    }

    private void AddSharedParameters(Element instance, Element? type, ElementFacts facts)
    {
        foreach (var kv in ParameterAliases.Map)
        {
            if (TryAlias(instance, kv.Key, kv.Value, out var value) || (type is not null && TryAlias(type, kv.Key, kv.Value, out value)))
                facts.Properties[kv.Key] = value;
        }
        if (facts.Properties.TryGetValue("QS_WorkItemId", out var wi) && wi is not null)
            facts.Properties["WorkItemOverride"] = wi;
    }

    private static bool TryAlias(Element e, string key, IEnumerable<string> aliases, out object? value)
    {
        foreach (var name in aliases)
        {
            var p = e.LookupParameter(name);
            if (p is null || !p.HasValue) continue;
            value = ParameterValue(p, key);
            if (value is not null && !string.IsNullOrWhiteSpace(value.ToString())) return true;
        }
        value = null;
        return false;
    }

    // Canonical selector units (parameterDictionary in the engine DB). Revit stores Length
    // in feet and Area in ft²; QS_* NUMBER parameters are already canonical and pass through.
    private static readonly Dictionary<string, ForgeTypeId> LengthKeyUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Height"] = UnitTypeId.Meters,
        ["Depth"] = UnitTypeId.Meters,
        ["Thickness"] = UnitTypeId.Centimeters,
        ["Width"] = UnitTypeId.Centimeters,
        ["WallThickness"] = UnitTypeId.Centimeters,
        ["Diameter"] = UnitTypeId.Centimeters,
        ["PaintThickness"] = UnitTypeId.Millimeters,
        ["RebarDiameter"] = UnitTypeId.Millimeters
    };

    private static object? ParameterValue(Parameter p, string key)
    {
        if (p.StorageType == StorageType.Double)
        {
            var raw = p.AsDouble();
            var spec = p.Definition.GetDataType();
            if (spec == SpecTypeId.Length && LengthKeyUnits.TryGetValue(key, out var unit))
                return UnitUtils.ConvertFromInternalUnits(raw, unit);
            if (spec == SpecTypeId.Area)
                return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.SquareMeters);
            if (spec == SpecTypeId.Volume)
                return UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.CubicMeters);
            return raw;
        }
        return p.StorageType switch
        {
            StorageType.String => p.AsString(),
            StorageType.Integer => p.AsInteger(),
            StorageType.ElementId => p.AsElementId().ToString(),
            _ => p.AsValueString()
        };
    }

    private void AddDocumentMetadata(Element e, ElementFacts f)
    {
        f.Properties["Description"] = e.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString() ?? "";
        f.Properties["TypeComments"] = e.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS)?.AsString() ?? "";
        f.Properties["Keynote"] = e.get_Parameter(BuiltInParameter.KEYNOTE_PARAM)?.AsString() ?? "";
        f.Properties["ExternalId"] = e.UniqueId;

        var level = e.LevelId != ElementId.InvalidElementId ? _doc.GetElement(e.LevelId) as Level : null;
        if (level is not null)
        {
            f.Properties["LevelName"] = level.Name;
            f.Properties["Level"] = level.Name;
        }

        if (e is MEPCurve mep)
        {
            var system = mep.MEPSystem;
            if (system is not null)
            {
                f.Properties["SystemName"] = system.Name;
                f.Properties["SystemCode"] = system.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.AsString() ?? system.Name;
            }
        }

        if (e.Location is LocationPoint lp && lp.Point is not null)
            f.Properties["ZoneName"] = "";
    }

    private void CaptureQuantityCandidates(Element e, ElementFacts f)
    {
        if (TryVolume(e, out var m3)) f.Properties["QuantityVolume"] = m3;
        if (TryArea(e, out var m2)) f.Properties["QuantityArea"] = m2;
        if (TryLength(e, out var m)) f.Properties["QuantityLength"] = m;
        f.Properties["QuantityCount"] = 1;
    }

    private void ApplyMappedQuantity(Element e, ElementFacts f)
    {
        if (e is Rebar rebar)
        {
            var barType = _doc.GetElement(rebar.GetTypeId()) as RebarBarType;
            if (barType is not null)
            {
                var diameterM = UnitUtils.ConvertFromInternalUnits(barType.BarNominalDiameter, UnitTypeId.Meters);
                var totalLengthM = UnitUtils.ConvertFromInternalUnits(rebar.TotalLength, UnitTypeId.Meters);
                f.Properties["QuantityRebarLength"] = totalLengthM;
                f.Quantity = totalLengthM * Math.PI * diameterM * diameterM / 4.0 * 7850.0;
                f.QuantityUnit = "kg";
                return;
            }
        }

        if (f.Properties.TryGetValue("QuantityVolume", out var vol) && vol is double vd && vd > 0)
        { f.Quantity = vd; f.QuantityUnit = "m3"; return; }
        if (f.Properties.TryGetValue("QuantityArea", out var area) && area is double ad && ad > 0)
        { f.Quantity = ad; f.QuantityUnit = "m2"; return; }
        if (f.Properties.TryGetValue("QuantityLength", out var len) && len is double ld && ld > 0)
        { f.Quantity = ld; f.QuantityUnit = "m"; return; }
        f.Quantity = 1; f.QuantityUnit = "count";
    }

    private void AddFallbackGeometry(Element e, ElementFacts f)
    {
        if (!f.Properties.ContainsKey("Height") && TryConstructionHeight(e, out var h)) f.Properties["Height"] = h;
        if (!f.Properties.ContainsKey("MaterialName")) f.Properties["MaterialName"] = f.MaterialName;
        if (!f.Properties.ContainsKey("CrossSectionArea") && f.QuantityUnit == "m3" && TryElementHeight(e, out var physicalHeight) && physicalHeight > 1e-9)
            f.Properties["CrossSectionArea"] = f.Quantity / physicalHeight;
        if (!f.Properties.ContainsKey("Width") && TryPlanWidthCm(e, out var widthCm))
            f.Properties["Width"] = widthCm;
    }

    /// <summary>Plan footprint max side in cm — used by AF footing Width bands.</summary>
    private bool TryPlanWidthCm(Element e, out double widthCm)
    {
        widthCm = 0;
        var bb = e.get_BoundingBox(null);
        if (bb is null) return false;
        var dx = Math.Abs(UnitUtils.ConvertFromInternalUnits(bb.Max.X - bb.Min.X, UnitTypeId.Centimeters));
        var dy = Math.Abs(UnitUtils.ConvertFromInternalUnits(bb.Max.Y - bb.Min.Y, UnitTypeId.Centimeters));
        widthCm = Math.Max(dx, dy);
        return widthCm > 1e-6;
    }

    private bool TryVolume(Element e, out double m3)
    {
        foreach (var bip in new[] { BuiltInParameter.HOST_VOLUME_COMPUTED })
        {
            var p = e.get_Parameter(bip);
            if (p?.StorageType == StorageType.Double && p.HasValue)
            { m3 = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.CubicMeters); return m3 > 0; }
        }
        m3 = 0; return false;
    }

    private bool TryArea(Element e, out double m2)
    {
        var p = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        if (p?.StorageType == StorageType.Double && p.HasValue)
        { m2 = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.SquareMeters); return m2 > 0; }
        m2 = 0; return false;
    }

    private bool TryLength(Element e, out double m)
    {
        var p = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
        if (p?.StorageType == StorageType.Double && p.HasValue)
        { m = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Meters); return m > 0; }
        m = 0; return false;
    }

    private bool TryElementHeight(Element e, out double m)
    {
        var p = e.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
        if (p?.StorageType == StorageType.Double && p.HasValue)
        {
            m = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Meters);
            if (m > 0) return true;
        }
        var bb = e.get_BoundingBox(null);
        if (bb is not null)
        {
            m = UnitUtils.ConvertFromInternalUnits(bb.Max.Z - bb.Min.Z, UnitTypeId.Meters);
            return m > 0;
        }
        m = 0; return false;
    }

    private bool TryConstructionHeight(Element e, out double m)
    {
        var bb = e.get_BoundingBox(null);
        if (bb is null) { m = 0; return false; }
        try
        {
            var projectBasePoint = BasePoint.GetProjectBasePoint(_doc);
            var topRelativeFeet = bb.Max.Z - projectBasePoint.Position.Z;
            m = Math.Max(0.0, UnitUtils.ConvertFromInternalUnits(topRelativeFeet, UnitTypeId.Meters));
            return true;
        }
        catch
        {
            m = Math.Max(0.0, UnitUtils.ConvertFromInternalUnits(bb.Max.Z, UnitTypeId.Meters));
            return true;
        }
    }

    private string GetMaterialNames(Element e)
    {
        try
        {
            return string.Join(" | ", e.GetMaterialIds(false).Select(id => _doc.GetElement(id)?.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        }
        catch { return ""; }
    }

    private static string GetFamilyName(Element e, Element? type)
    {
        if (e is FamilyInstance fi) return fi.Symbol?.FamilyName ?? "";
        return type?.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString() ?? "";
    }

    /// <summary>
    /// Propose InOutBuilding into facts only; never overwrites an existing QS value already loaded.
    /// Does not write the model — use Norm Selector «Gán InOut đề xuất».
    /// </summary>
    private static void ApplyInOutClassification(ElementFacts facts)
    {
        facts.Properties["RevitCategory"] = facts.RevitCategory;
        var (value, source) = InOutClassifier.Classify(facts.Properties);
        if (!facts.Properties.ContainsKey("InOutBuilding") ||
            facts.Properties["InOutBuilding"] is null ||
            string.IsNullOrWhiteSpace(facts.Properties["InOutBuilding"]?.ToString()))
        {
            facts.Properties["InOutBuilding"] = value;
            facts.Properties["InOutSource"] = source;
        }
        else if (!facts.Properties.ContainsKey("InOutSource"))
        {
            facts.Properties["InOutSource"] = "Existing";
        }
    }

    private void TrySetRoomFlags(Wall wall, ElementFacts f)
    {
        try
        {
            var bb = wall.get_BoundingBox(null);
            if (bb is null) return;
            var mid = (bb.Min + bb.Max) * 0.5;
            var phase = _doc.Phases.get_Item(_doc.Phases.Size - 1);
            var room = _doc.GetRoomAtPoint(mid, phase);
            if (room is not null)
            {
                f.Properties["HasRoom"] = true;
                f.Properties["RoomName"] = room.Name;
            }
        }
        catch
        {
            // Room API optional — WallFunction still drives classifier.
        }
    }
}

