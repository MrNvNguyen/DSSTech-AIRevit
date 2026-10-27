namespace DSSTech.Core;

public static class ConditionEvaluator
{
    public static (bool Match, bool Missing) Evaluate(NormCondition condition, FactBag facts)
    {
        if (!facts.TryGet(condition.ParamKey, out var fact)) return (false, true);
        var op = (condition.Operator ?? "EQ").Trim().ToUpperInvariant();
        var expected = condition.Value1 ?? "";

        if (op is ">" or ">=" or "<" or "<=" or "=" or "==" or "EQ")
        {
            if (ValueTools.TryDouble(fact, out var actualN) && ValueTools.TryDouble(expected, out var expectedN))
            {
                var eps = Math.Max(1e-9, Math.Abs(expectedN) * 1e-8);
                return op switch
                {
                    ">" => (actualN > expectedN + eps, false),
                    ">=" => (actualN >= expectedN - eps, false),
                    "<" => (actualN < expectedN - eps, false),
                    "<=" => (actualN <= expectedN + eps, false),
                    _ => (Math.Abs(actualN - expectedN) <= eps, false)
                };
            }
        }

        if (op is "EQ" or "=" or "==")
        {
            if (ValueTools.TryBool(fact, out var actualB) && ValueTools.TryBool(expected, out var expectedB))
                return (actualB == expectedB, false);
            return (ValueTools.NormalizeText(fact) == ValueTools.NormalizeText(expected), false);
        }

        if (op is "NE" or "!=" or "<>")
            return (ValueTools.NormalizeText(fact) != ValueTools.NormalizeText(expected), false);

        if (op == "IN")
        {
            var actual = ValueTools.NormalizeText(fact);
            var allowed = ValueTools.SplitTokens(expected).Select(ValueTools.NormalizeText);
            return (allowed.Contains(actual), false);
        }

        if (op == "CONTAINS")
        {
            var actual = ValueTools.NormalizeText(fact);
            var tokens = expected.Contains('|') ? ValueTools.SplitTokens(expected) : new[] { expected };
            return (tokens.Any(t => actual.Contains(ValueTools.NormalizeText(t))), false);
        }

        if (op == "NOT_CONTAINS")
        {
            var actual = ValueTools.NormalizeText(fact);
            var tokens = ValueTools.SplitTokens(expected);
            return (!tokens.Any(t => actual.Contains(ValueTools.NormalizeText(t))), false);
        }

        return (false, false);
    }

    internal static CandidateEvaluation EvaluateCandidate(
        NormVariant variant,
        NormIndexRecord index,
        IReadOnlyList<NormCondition> conditions,
        FactBag facts)
    {
        var result = new CandidateEvaluation { Variant = variant, Index = index };
        if (conditions.Count == 0)
        {
            result.IsMatch = true;
            return result;
        }

        var groups = conditions.GroupBy(c => c.ConditionGroup <= 0 ? 1 : c.ConditionGroup).ToList();
        var anyGroupMatches = false;
        var bestMatched = -1;
        List<string> bestMissing = [];
        List<string> bestFailed = [];
        var bestTotal = 0;

        foreach (var group in groups)
        {
            var matched = 0;
            var missing = new List<string>();
            var failed = new List<string>();
            var all = true;
            foreach (var condition in group)
            {
                var (match, isMissing) = Evaluate(condition, facts);
                if (match) matched++;
                else
                {
                    all = false;
                    if (isMissing) missing.Add(condition.ParamKey);
                    else failed.Add($"{condition.ParamKey} {condition.Operator} {condition.Value1}");
                }
            }
            if (all) anyGroupMatches = true;
            if (matched > bestMatched)
            {
                bestMatched = matched;
                bestMissing = missing;
                bestFailed = failed;
                bestTotal = group.Count();
            }
        }

        result.IsMatch = anyGroupMatches;
        result.MatchedConditions = Math.Max(0, bestMatched);
        result.TotalConditions = bestTotal;
        result.Missing = bestMissing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.Failed = bestFailed;
        return result;
    }
}
