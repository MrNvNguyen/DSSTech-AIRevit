namespace DSSTech.Core;

public sealed class ResourceCalculator
{
    private readonly RuntimeData _data;
    private readonly Dictionary<string, ResourceDefinition> _resourceById;

    public ResourceCalculator(RuntimeData data)
    {
        _data = data;
        _resourceById = data.Resources.ToDictionary(x => x.ResourceId, StringComparer.OrdinalIgnoreCase);
    }

    public (double Factor, List<ResourceDemand> Resources, List<AppliedAdjustment> Adjustments, List<string> Warnings)
        Calculate(NormVariant variant, NormIndexRecord index, ElementFacts element, FactBag facts)
    {
        var warnings = new List<string>();
        var factor = QuantityFactor(element.Quantity, element.QuantityUnit, variant.Unit, warnings);
        var baseRows = _data.NormResources.Where(x => x.VariantCode.Equals(variant.VariantCode, StringComparison.OrdinalIgnoreCase)).ToList();
        var demands = new List<ResourceDemand>();

        foreach (var row in baseRows)
        {
            if (!_resourceById.TryGetValue(row.ResourceId, out var def))
            {
                warnings.Add($"Thiếu resource definition: {row.ResourceId}");
                continue;
            }
            var pct = def.ResourceType.EndsWith("_PCT", StringComparison.OrdinalIgnoreCase) || def.Unit == "%";
            demands.Add(new ResourceDemand
            {
                ResourceId = def.ResourceId,
                ResourceType = def.ResourceType,
                ResourceName = def.ResourceName,
                Unit = def.Unit,
                NormQuantity = row.Quantity,
                TotalQuantity = pct ? null : row.Quantity * factor,
                IsPercentage = pct,
                Note = pct ? $"{row.Quantity}% trên cơ sở chi phí tương ứng; không quy đổi thành khối lượng vật lý." : def.Note
            });
        }

        var applied = ApplyAdjustments(variant, facts, demands, warnings);
        return (factor, demands, applied, warnings);
    }

    private List<AppliedAdjustment> ApplyAdjustments(NormVariant variant, FactBag facts, List<ResourceDemand> demands, List<string> warnings)
    {
        var applied = new List<AppliedAdjustment>();
        foreach (var rule in _data.AdjustmentRules.Where(r => ScopeMatches(r.NormScope, variant)))
        {
            var condition = new NormCondition { ParamKey = rule.TriggerParam, Operator = rule.TriggerOperator, Value1 = rule.TriggerValue };
            var (match, _) = ConditionEvaluator.Evaluate(condition, facts);
            if (!match) continue;

            var targets = demands.Where(d => d.ResourceId.Equals(rule.TargetResourceId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (targets.Count == 0 && rule.ActionType.StartsWith("LAB_", StringComparison.OrdinalIgnoreCase))
                targets = demands.Where(d => d.ResourceType.Equals("LAB", StringComparison.OrdinalIgnoreCase)).ToList();

            if (rule.ActionType.Equals("RESOURCE_REMOVE", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var t in targets) demands.Remove(t);
            }
            else if (ValueTools.ContainsIgnoreCase(rule.ActionType, "MULTIPLIER"))
            {
                foreach (var t in targets)
                {
                    t.NormQuantity *= rule.Factor;
                    if (t.TotalQuantity.HasValue) t.TotalQuantity *= rule.Factor;
                    t.Note = string.Join(" | ", new[] { t.Note, $"Adjusted ×{rule.Factor:0.###}: {rule.Description}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
                }
            }

            if (targets.Count == 0)
                warnings.Add($"Adjustment {rule.AdjustmentId} kích hoạt nhưng không tìm thấy resource target {rule.TargetResourceId}.");

            applied.Add(new AppliedAdjustment
            {
                AdjustmentId = rule.AdjustmentId,
                Description = rule.Description,
                ActionType = rule.ActionType,
                TargetResourceId = rule.TargetResourceId,
                Factor = rule.Factor
            });
        }
        return applied;
    }

    private static bool ScopeMatches(string scope, NormVariant variant)
    {
        if (string.IsNullOrWhiteSpace(scope)) return false;
        if (scope.Equals(variant.VariantCode, StringComparison.OrdinalIgnoreCase) || scope.Equals(variant.ParentNormCode, StringComparison.OrdinalIgnoreCase)) return true;
        if (!scope.Contains('.') || !variant.VariantCode.Contains('.')) return false;
        var s = scope.Split('.'); var v = variant.VariantCode.Split('.');
        if (!s[0].Equals(v[0], StringComparison.OrdinalIgnoreCase)) return false;
        // Group scopes such as AF.10000 / AF.30000 apply to AF.1xxxx / AF.3xxxx.
        return s[1].Length == 5 && s[1].EndsWith("0000") && v[1].Length == 5 && s[1][0] == v[1][0];
    }

    public static double QuantityFactor(double quantity, string quantityUnit, string normUnit, List<string>? warnings = null)
    {
        var q = ValueTools.NormalizeText(quantityUnit).Replace("²", "2").Replace("³", "3");
        var n = ValueTools.NormalizeText(normUnit).Replace("²", "2").Replace("³", "3");
        if (string.IsNullOrWhiteSpace(n)) return quantity;
        if (n == q || (n == "m3" && q is "m3" or "m^3") || (n == "m2" && q is "m2" or "m^2")) return quantity;
        if (n == "100m2" && (q == "m2" || q == "m^2")) return quantity / 100.0;
        if (n == "t" && q is "kg" or "kilogram") return quantity / 1000.0;
        if (n == "kg" && q is "t" or "ton") return quantity * 1000.0;
        if ((n is "each" or "cai" or "bo" or "set") && (q is "count" or "each" or "cai" or "bo" or "set")) return quantity;
        warnings?.Add($"Chưa có conversion '{quantityUnit}' → '{normUnit}'. Engine dùng factor = Quantity trực tiếp.");
        return quantity;
    }
}
