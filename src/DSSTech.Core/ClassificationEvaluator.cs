namespace DSSTech.Core;

public static class ClassificationEvaluator
{
    public static bool Matches(ClassificationRuleSet ruleSet, FactBag facts)
    {
        if (ruleSet.Conditions.Count == 0) return true;

        var groups = ruleSet.Conditions.GroupBy(c => c.ConditionGroup <= 0 ? 1 : c.ConditionGroup);
        foreach (var group in groups)
        {
            var all = true;
            foreach (var condition in group)
            {
                var norm = ToNormCondition(condition);
                var (match, _) = ConditionEvaluator.Evaluate(norm, facts);
                if (!match) { all = false; break; }
            }
            if (all) return true;
        }
        return false;
    }

    private static NormCondition ToNormCondition(ClassificationRuleCondition c) => new()
    {
        ParamKey = c.ParamKey,
        Operator = c.Operator,
        Value1 = c.Value1,
        Value2 = c.Value2,
        Unit = c.Unit,
        ConditionGroup = c.ConditionGroup
    };
}
