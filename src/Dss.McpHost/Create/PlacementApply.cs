using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace Dss.McpHost.Create;

internal static class PlacementApply
{
    public static void Apply(Element element, JsonElement fields, string? units)
    {
        var topOff = Num(fields, "topOffsetMm");
        var freeOff = Num(fields, "offsetMm");
        var beamEnd = element.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION);
        if (beamEnd is not null && !beamEnd.IsReadOnly)
        {
            // Beams: Start/End Level Offset from UI only. Clear sticky NewFamilyInstance /
            // last-draw defaults (Y Right, z Offset ≠ 0) that fight centerline plan layout.
            SetMm(element, BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION, topOff ?? freeOff, units);
            SetMm(element, BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION, topOff ?? freeOff, units);
            ApplyBeamJustificationDefaults(element);
        }
        else
        {
            SetMm(element, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM, Num(fields, "baseOffsetMm"), units);
            SetMm(element, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, topOff, units);
            SetMm(element, BuiltInParameter.WALL_BASE_OFFSET, Num(fields, "baseOffsetMm"), units);
            SetMm(element, BuiltInParameter.WALL_TOP_OFFSET, topOff, units);
            SetMm(element, BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM, Num(fields, "baseOffsetMm"), units);
            SetMm(element, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, topOff ?? freeOff, units);
        }
        SetMm(element, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM, Num(fields, "offsetMm"), units);
        SetMm(element, BuiltInParameter.RBS_OFFSET_PARAM, Num(fields, "offsetMm"), units);
        SetNamed(element, Str(fields, "widthParam"), Num(fields, "widthMm"), units);
        SetNamed(element, Str(fields, "heightParam"), Num(fields, "heightMm") ?? Num(fields, "depthMm"), units);

        TrySetMark(element, Str(fields, "mark"));
        var materialName = Str(fields, "material");
        if (string.IsNullOrWhiteSpace(materialName))
            return;
        var material = new FilteredElementCollector(element.Document).OfClass(typeof(Material)).Cast<Material>()
            .FirstOrDefault(m => string.Equals(m.Name, materialName, StringComparison.OrdinalIgnoreCase));
        if (material is null)
            return;
        try
        {
            var parameter = element.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
            if (parameter is not null && !parameter.IsReadOnly)
                parameter.Set(material.Id);
        }
        catch (Exception)
        {
            // Material stays on the chosen type when the instance parameter is locked.
        }
    }

    public static bool TrySetMark(Element element, string? mark)
    {
        if (string.IsNullOrWhiteSpace(mark))
            return false;
        mark = mark.Trim().ToUpperInvariant();
        try
        {
            var parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (parameter is not null && !parameter.IsReadOnly)
            {
                parameter.Set(mark);
                return true;
            }
        }
        catch (Exception)
        {
            // Mark is optional. A locked parameter must not roll back the element.
        }

        return false;
    }

    /// <summary>
    /// Force plan-centerline justification after create (or skipIfExists rematch).
    /// NewFamilyInstance inherits Revit's last Beam draw settings / family defaults
    /// (often Y Justification=Right, z Offset Value≠0).
    /// </summary>
    public static void ApplyBeamJustificationDefaults(Element element)
    {
        SetInt(element, BuiltInParameter.Y_JUSTIFICATION, (int)YJustification.Center);
        SetMm(element, BuiltInParameter.Y_OFFSET_VALUE, 0, units: null);
        // Keep Z Justification Top (under-slab); elevation comes from Start/End Level Offset only.
        SetMm(element, BuiltInParameter.Z_OFFSET_VALUE, 0, units: null);
    }

    private static void SetInt(Element element, BuiltInParameter builtIn, int value)
    {
        try
        {
            var parameter = element.get_Parameter(builtIn);
            if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer)
                return;
            if (parameter.AsInteger() != value)
                parameter.Set(value);
        }
        catch (Exception)
        {
            // Justification params are instance-only on framing; skip if locked/missing.
        }
    }

    private static void SetNamed(Element element, string? name, double? mm, string? units)
    {
        if (string.IsNullOrWhiteSpace(name) || mm is null)
            return;
        try
        {
            var parameter = element.LookupParameter(name);
            if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
                return;
            if (element is FamilyInstance instance)
            {
                foreach (Parameter typeParameter in instance.Symbol.Parameters)
                {
                    if (typeParameter.Id == parameter.Id)
                        return;
                }
            }

            parameter.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(mm.Value, units)));
        }
        catch (Exception)
        {
            // Named section parameters are a hint. The type size is already set when possible.
        }
    }

    private static void SetMm(Element element, BuiltInParameter builtIn, double? mm, string? units)
    {
        if (mm is null)
            return;
        try
        {
            var parameter = element.get_Parameter(builtIn);
            if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
                return;
            parameter.Set(UnitConv.MmToInternal(UnitConv.ScaleToMm(mm.Value, units)));
        }
        catch (Exception)
        {
            // Offset parameters differ by category. Skip the ones this element does not own.
        }
    }

    private static string? Str(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static double? Num(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;
    }
}
