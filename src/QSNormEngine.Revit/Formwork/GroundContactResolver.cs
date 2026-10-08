using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit.Formwork;

/// <summary>
/// Drop Bottom formwork for on-grade Beam / Slab / Footing.
/// Footing / pile cap: always Bottom=0 (cast on grade or blinding) — not gated by autoDetect.
/// Slab: lowest floor soffit when slabOnGradeSkipsBottom.
/// Beam: at framing grade when groundContactAutoDetect.
/// </summary>
internal static class GroundContactResolver
{
    public static void Apply(Document doc, IReadOnlyList<SolidEntry> entries, FormworkRuleSet ruleSet)
    {
        foreach (var entry in entries)
            entry.GroundContactDropBottom = false;

        if (entries.Count == 0)
            return;

        // Footings always skip Bottom — independent of auto-detect (pile-supported or on grade).
        foreach (var entry in entries)
        {
            if (entry.Role == FormworkElementRole.Footing ||
                string.Equals(entry.Rule?.Kind, "Footing", StringComparison.OrdinalIgnoreCase))
                entry.GroundContactDropBottom = true;
        }

        if (!ruleSet.Settings.GroundContactAutoDetect)
            return;

        var tolFt = FormworkUnits.MmToFeet(ruleSet.Settings.GroundContactToleranceMm);
        var lowestSlabSoffit = ResolveLowestSlabSoffitZ(doc);
        var framingGradeZ = ResolveFramingGradeZ(doc, entries);

        foreach (var entry in entries)
        {
            if (entry.GroundContactDropBottom)
                continue;

            var rule = entry.Rule;
            if (rule is null || !rule.DropBottomWhenGroundContact)
                continue;

            if (entry.Role == FormworkElementRole.Slab ||
                string.Equals(rule.Kind, "Slab", StringComparison.OrdinalIgnoreCase))
            {
                if (!ruleSet.Settings.SlabOnGradeSkipsBottom)
                    continue;

                if (lowestSlabSoffit is double slabGrade)
                {
                    var soffit = ResolveElementSoffitZ(entry);
                    if (soffit - slabGrade <= tolFt)
                        entry.GroundContactDropBottom = true;
                }

                continue;
            }

            // Grade beams: soffit at framing grade.
            if (framingGradeZ is double grade)
            {
                var soffit = ResolveElementSoffitZ(entry);
                if (soffit - grade <= tolFt)
                    entry.GroundContactDropBottom = true;
            }
        }
    }

    private static double ResolveElementSoffitZ(SolidEntry entry)
    {
        try
        {
            if (entry.Element is Floor floor)
            {
                var p = floor.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM)
                        ?? floor.LookupParameter("Elevation at Bottom");
                if (p is not null && p.HasValue)
                    return p.AsDouble();
            }

            var bot = entry.Element.LookupParameter("Elevation at Bottom")
                      ?? entry.Element.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM);
            if (bot is not null && bot.HasValue)
                return bot.AsDouble();
        }
        catch
        {
            // fall through
        }

        return entry.BoundingBox.Min.Z;
    }

    /// <summary>Lowest floor soffit (Elevation at Bottom) — defines slab-on-grade band.</summary>
    private static double? ResolveLowestSlabSoffitZ(Document doc)
    {
        double? minZ = null;
        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Floors)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (el is not Floor floor) continue;
            var p = floor.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM)
                    ?? floor.LookupParameter("Elevation at Bottom");
            double z;
            if (p is not null && p.HasValue)
                z = p.AsDouble();
            else
            {
                var bb = floor.get_BoundingBox(null);
                if (bb is null) continue;
                z = bb.Min.Z;
            }

            minZ = minZ is double m ? Math.Min(m, z) : z;
        }

        return minZ;
    }

    /// <summary>
    /// Grade for beams: lowest framing/column soffit (not foundation dig bottoms).
    /// </summary>
    private static double? ResolveFramingGradeZ(Document doc, IReadOnlyList<SolidEntry> entries)
    {
        double? minZ = null;
        foreach (var cat in new[]
                 {
                     BuiltInCategory.OST_StructuralFraming,
                     BuiltInCategory.OST_StructuralColumns
                 })
        {
            foreach (var el in new FilteredElementCollector(doc)
                         .OfCategory(cat)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                var bot = el.LookupParameter("Elevation at Bottom")
                          ?? el.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM);
                double z;
                if (bot is not null && bot.HasValue)
                    z = bot.AsDouble();
                else
                {
                    var bb = el.get_BoundingBox(null);
                    if (bb is null) continue;
                    z = bb.Min.Z;
                }

                minZ = minZ is double m ? Math.Min(m, z) : z;
            }
        }

        if (minZ is not null)
            return minZ;

        var gradeRoles = entries
            .Where(e => e.Role is FormworkElementRole.Beam or FormworkElementRole.Column)
            .ToList();
        if (gradeRoles.Count > 0)
            return gradeRoles.Min(e => e.BoundingBox.Min.Z);

        return null;
    }
}
