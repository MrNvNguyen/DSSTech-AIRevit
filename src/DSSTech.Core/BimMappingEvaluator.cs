namespace DSSTech.Core;

public static class BimMappingEvaluator
{
    public static bool Matches(BimMappingRule rule, ElementFacts element, FactBag facts)
    {
        if (!string.IsNullOrWhiteSpace(rule.RevitCategory) &&
            !rule.RevitCategory.Equals(element.RevitCategory, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!PatternMatches(rule.FamilyPattern, element.FamilyName) &&
            !PatternMatches(rule.FamilyPattern, element.TypeName))
            return false;

        if (!PatternMatches(rule.TypePattern, element.TypeName))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.SystemCode) &&
            !TextContains(facts.Get("SystemCode")?.ToString(), rule.SystemCode) &&
            !TextContains(facts.Get("SystemName")?.ToString(), rule.SystemCode) &&
            !TextContains(facts.Get("QS_SystemType")?.ToString(), rule.SystemCode))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.MatchParameterNames) &&
            !string.IsNullOrWhiteSpace(rule.MatchParameterValues))
        {
            var names = ValueTools.SplitTokens(rule.MatchParameterNames, '|');
            var values = ValueTools.SplitTokens(rule.MatchParameterValues, '|');
            var hit = false;
            foreach (var name in names)
            {
                if (!facts.TryGet(name, out var actual)) continue;
                var normalized = ValueTools.NormalizeText(actual);
                if (values.Any(v => normalized == ValueTools.NormalizeText(v)))
                {
                    hit = true;
                    break;
                }
            }
            if (!hit) return false;
        }

        foreach (var condition in rule.Conditions)
        {
            var (match, _) = ConditionEvaluator.Evaluate(new NormCondition
            {
                ParamKey = condition.ParamKey,
                Operator = condition.Operator,
                Value1 = condition.Value1,
                Value2 = condition.Value2,
                Unit = condition.Unit,
                ConditionGroup = condition.ConditionGroup
            }, facts);
            if (!match) return false;
        }

        return true;
    }

    private static bool PatternMatches(string? pattern, string? text)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        var normalized = ValueTools.NormalizeText(text);
        return ValueTools.SplitTokens(pattern).Any(token => normalized.Contains(ValueTools.NormalizeText(token)));
    }

    private static bool TextContains(string? text, string expected)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ValueTools.NormalizeText(text).Contains(ValueTools.NormalizeText(expected));
    }
}
