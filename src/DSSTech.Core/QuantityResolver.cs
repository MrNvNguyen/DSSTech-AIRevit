namespace DSSTech.Core;

public static class QuantityResolver
{
    private static readonly Dictionary<string, (string PropertyKey, string Unit)> ParameterMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["HOST_VOLUME_COMPUTED"] = ("QuantityVolume", "m3"),
            ["HOST_AREA_COMPUTED"] = ("QuantityArea", "m2"),
            ["CURVE_ELEM_LENGTH"] = ("QuantityLength", "m"),
            ["REBAR_ELEM_TOTAL_LENGTH"] = ("QuantityRebarLength", "m"),
            ["QS_FormworkArea"] = ("FormworkArea", "m2"),
            ["FormworkArea"] = ("FormworkArea", "m2"),
            ["__COUNT__"] = ("QuantityCount", "count")
        };

    public static (double Quantity, string Unit) Resolve(RuntimeData data, ElementFacts element, string workItemId)
    {
        var map = data.BimPropertyMaps.FirstOrDefault(x =>
                      x.RevitCategory.Equals(element.RevitCategory, StringComparison.OrdinalIgnoreCase) &&
                      x.DefaultWorkItem.Equals(workItemId, StringComparison.OrdinalIgnoreCase))
                  ?? data.BimPropertyMaps.FirstOrDefault(x =>
                      x.RevitCategory.Equals(element.RevitCategory, StringComparison.OrdinalIgnoreCase));

        var parameter = map?.QuantityParameter?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(parameter))
            return (element.Quantity, element.QuantityUnit);

        if (parameter.Equals("__COUNT__", StringComparison.OrdinalIgnoreCase))
            return (1, "count");

        if (ParameterMap.TryGetValue(parameter, out var mapped) &&
            element.Properties.TryGetValue(mapped.PropertyKey, out var value) &&
            ValueTools.TryDouble(value, out var number) && number > 0)
            return (number, mapped.Unit);

        if (element.Properties.TryGetValue(parameter, out var direct) &&
            ValueTools.TryDouble(direct, out var directNumber) && directNumber > 0)
            return (directNumber, InferUnit(parameter, element.QuantityUnit));

        return (element.Quantity, element.QuantityUnit);
    }

    private static string InferUnit(string parameter, string fallback) =>
        ParameterMap.TryGetValue(parameter, out var mapped) ? mapped.Unit : fallback;
}
