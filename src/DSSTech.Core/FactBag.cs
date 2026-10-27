using System.Text.Json;

namespace DSSTech.Core;

public sealed class FactBag
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    public FactBag(ElementFacts element, EngineConfig config, string workItemId)
    {
        foreach (var kv in config.GlobalDefaults) Set(kv.Key, kv.Value);
        if (config.WorkItemDefaults.TryGetValue(workItemId, out var defaults))
            foreach (var kv in defaults) Set(kv.Key, kv.Value);
        foreach (var kv in element.Properties) Set(kv.Key, kv.Value);

        Set("RevitCategory", element.RevitCategory);
        Set("FamilyName", element.FamilyName);
        Set("TypeName", element.TypeName);
        Set("MaterialName", element.MaterialName);
        Set("Quantity", element.Quantity);
        Set("QuantityUnit", element.QuantityUnit);
    }

    public void Set(string key, object? value)
    {
        if (value is JsonElement je)
        {
            _values[key] = je.ValueKind switch
            {
                JsonValueKind.Number when je.TryGetDouble(out var d) => d,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Null => null,
                _ => je.ToString()
            };
        }
        else _values[key] = value;
    }

    public bool TryGet(string key, out object? value) => _values.TryGetValue(key, out value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString());
    public object? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
    public IReadOnlyDictionary<string, object?> Values => _values;
}
