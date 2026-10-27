using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using DSSTech.Core.Formwork;

namespace DSSTech.Revit.Formwork;

internal static class FormworkRuleMatcher
{
    public static FormworkRule? Match(Element element, FormworkRuleSet ruleSet)
    {
        var catName = element.Category?.BuiltInCategory.ToString();
        if (string.IsNullOrEmpty(catName))
            return null;

        var typeName = ResolveTypeName(element);

        foreach (var rule in ruleSet.Rules)
        {
            if (rule.Categories.Count > 0 &&
                !rule.Categories.Any(c => string.Equals(c, catName, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!string.IsNullOrWhiteSpace(rule.TypePattern))
            {
                var haystack = $"{element.Name} {typeName}";
                if (!Regex.IsMatch(haystack, rule.TypePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    continue;
            }

            return rule;
        }

        return null;
    }

    public static FormworkElementRole ToRole(FormworkRule? rule)
    {
        if (rule is null) return FormworkElementRole.Unknown;
        return rule.Kind.Trim().ToLowerInvariant() switch
        {
            "column" => FormworkElementRole.Column,
            "wall" or "vách" or "vach" => FormworkElementRole.Wall,
            "beam" => FormworkElementRole.Beam,
            "slab" => FormworkElementRole.Slab,
            "footing" or "pilecap" or "foundation" => FormworkElementRole.Footing,
            _ => FormworkElementRole.Unknown
        };
    }

    public static HashSet<FormworkFaceRole> ParseFaces(FormworkRule? rule)
    {
        var set = new HashSet<FormworkFaceRole>();
        if (rule is null) return set;
        foreach (var face in rule.Faces)
        {
            if (Enum.TryParse<FormworkFaceRole>(face, ignoreCase: true, out var role) &&
                role != FormworkFaceRole.Excluded)
                set.Add(role);
        }
        return set;
    }

    public static IReadOnlyList<BuiltInCategory> CollectCategories(FormworkRuleSet ruleSet)
    {
        var cats = new HashSet<BuiltInCategory>();
        foreach (var rule in ruleSet.Rules)
        {
            if (rule.Skip) continue;
            foreach (var name in rule.Categories)
            {
                if (Enum.TryParse(name, ignoreCase: true, out BuiltInCategory cat))
                    cats.Add(cat);
            }
        }

        if (cats.Count == 0)
        {
            cats.Add(BuiltInCategory.OST_StructuralColumns);
            cats.Add(BuiltInCategory.OST_StructuralFraming);
            cats.Add(BuiltInCategory.OST_Floors);
            cats.Add(BuiltInCategory.OST_Walls);
        }

        return cats.ToList();
    }

    /// <summary>True when element satisfies structuralOnly (walls must be Structural).</summary>
    public static bool PassesStructuralOnly(Element element, FormworkRule? rule)
    {
        if (rule is not { StructuralOnly: true })
            return true;
        if (element is not Wall)
            return true;
        try
        {
            var p = element.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
            return p is not null && p.AsInteger() == 1;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveTypeName(Element element)
    {
        try
        {
            if (element is ElementType et) return et.Name;
            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId) return "";
            return element.Document.GetElement(typeId)?.Name ?? "";
        }
        catch
        {
            return "";
        }
    }
}
