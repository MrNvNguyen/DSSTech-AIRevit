namespace QSNormEngine.Core;

public sealed class WorkItemClassifier
{
    private readonly RuntimeData _data;
    public WorkItemClassifier(RuntimeData data) => _data = data;

    public (string WorkItemId, double Confidence, List<string> Warnings) Classify(ElementFacts element)
    {
        var warnings = new List<string>();
        if (element.Properties.TryGetValue("WorkItemOverride", out var overrideValue) ||
            element.Properties.TryGetValue("QS_WorkItemId", out overrideValue))
        {
            var explicitId = Convert.ToString(overrideValue)?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(explicitId) && _data.WorkFamilies.Any(x => x.WorkItemId.Equals(explicitId, StringComparison.OrdinalIgnoreCase)))
                return (explicitId, 1.0, warnings);
        }

        var category = element.RevitCategory;
        var maps = _data.BimPropertyMaps.Where(x => x.RevitCategory.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        var facts = WorkItemClassifier.BuildClassificationFacts(element);

        foreach (var rule in _data.BimMappingRules
                     .Where(r => string.IsNullOrWhiteSpace(r.RevitCategory) || r.RevitCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(r => r.Priority)
                     .ThenBy(r => r.RuleId, StringComparer.OrdinalIgnoreCase))
        {
            if (!_data.WorkFamilies.Any(x => x.WorkItemId.Equals(rule.WorkItemId, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!BimMappingEvaluator.Matches(rule, element, facts)) continue;
            return (rule.WorkItemId, .99, warnings);
        }

        foreach (var rule in _data.ClassificationRuleSets
                     .Where(r => r.RevitCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(r => r.Priority)
                     .ThenBy(r => r.SetId, StringComparer.OrdinalIgnoreCase))
        {
            if (!_data.WorkFamilies.Any(x => x.WorkItemId.Equals(rule.WorkItemId, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!ClassificationEvaluator.Matches(rule, facts)) continue;
            return (rule.WorkItemId, .98, warnings);
        }

        if (maps.Count == 0)
            return ("", 0, warnings);

        if (maps.Count > 1)
            warnings.Add($"Category {category} có {maps.Count} WorkItem mappings; cần material/spec để phân loại chắc chắn.");

        return (maps[0].DefaultWorkItem, maps.Count == 1 ? .90 : .70, warnings);
    }

    internal static FactBag BuildClassificationFacts(ElementFacts element)
    {
        var clone = new ElementFacts
        {
            ElementId = element.ElementId,
            RevitCategory = element.RevitCategory,
            FamilyName = element.FamilyName,
            TypeName = element.TypeName,
            MaterialName = element.MaterialName,
            Quantity = element.Quantity,
            QuantityUnit = element.QuantityUnit,
            Properties = new Dictionary<string, object?>(element.Properties, StringComparer.OrdinalIgnoreCase)
        };
        clone.Properties["_ClassifyText"] = string.Join(" ",
            element.MaterialName,
            GetText(element, "MaterialName"),
            GetText(element, "StructuralMaterialType"),
            element.FamilyName,
            element.TypeName,
            GetText(element, "EquipmentType"),
            GetText(element, "PipeMaterial"),
            GetText(element, "Description"),
            GetText(element, "TypeComments"),
            GetText(element, "Keynote"));
        return new FactBag(clone, new EngineConfig(), "");
    }

    private static string GetText(ElementFacts e, string key) =>
        e.Properties.TryGetValue(key, out var v) ? Convert.ToString(v) ?? "" : "";
}
