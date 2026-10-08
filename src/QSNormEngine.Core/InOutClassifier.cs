namespace QSNormEngine.Core;

/// <summary>
/// Classify Trong nhà / Ngoài nhà / Hỗn hợp from element facts (Revit-API-free).
/// Priority: existing QS value → WallFunction → Room → Envelope → category default.
/// </summary>
public static class InOutClassifier
{
    public const string Interior = "Interior";
    public const string Exterior = "Exterior";
    public const string Mixed = "Mixed";

    public static (string Value, string Source) Classify(IReadOnlyDictionary<string, object?> properties)
    {
        if (TryGet(properties, "InOutBuilding", out var existing) ||
            TryGet(properties, "QS_InOutBuilding", out existing))
        {
            var canon = Canonical(existing!.ToString() ?? "");
            if (canon is not null)
                return (canon, "Existing");
        }

        if (TryGet(properties, "WallFunction", out var wf))
        {
            var s = wf!.ToString() ?? "";
            if (s.Contains("Exterior", StringComparison.OrdinalIgnoreCase))
                return (Exterior, "WallFunction");
            if (s.Contains("Interior", StringComparison.OrdinalIgnoreCase))
                return (Interior, "WallFunction");
        }

        if (IsTruthy(properties, "HasRoom") || IsTruthy(properties, "InRoom"))
            return (Interior, "Room");

        if (TryGet(properties, "EnvelopeHint", out var env))
        {
            var s = env!.ToString() ?? "";
            if (s.Contains("Exterior", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Ngoài", StringComparison.OrdinalIgnoreCase))
                return (Exterior, "Envelope");
            if (s.Contains("Interior", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Trong", StringComparison.OrdinalIgnoreCase))
                return (Interior, "Envelope");
            if (s.Contains("Mixed", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Hỗn", StringComparison.OrdinalIgnoreCase))
                return (Mixed, "Envelope");
        }

        if (TryGet(properties, "SurfaceLocation", out var surf))
        {
            // Hint only — never write QS_SurfaceLocation from this classifier.
            var s = surf!.ToString() ?? "";
            if (s.Equals("Exterior", StringComparison.OrdinalIgnoreCase))
                return (Exterior, "SurfaceLocationHint");
            if (s.Equals("Interior", StringComparison.OrdinalIgnoreCase))
                return (Interior, "SurfaceLocationHint");
        }

        if (TryGet(properties, "ElementType", out var et) || TryGet(properties, "RevitCategory", out et))
        {
            var s = et!.ToString() ?? "";
            if (s.Contains("Roof", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("OST_Roofs", StringComparison.OrdinalIgnoreCase))
                return (Exterior, "CategoryDefault");
            if (s.Contains("Curtain", StringComparison.OrdinalIgnoreCase))
                return (Exterior, "CategoryDefault");
        }

        return (Interior, "CategoryDefault");
    }

    public static string? Canonical(string raw)
    {
        var s = raw.Trim();
        if (s.Equals(Interior, StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Trong", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Indoor", StringComparison.OrdinalIgnoreCase))
            return Interior;
        if (s.Equals(Exterior, StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Ngoài", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Outdoor", StringComparison.OrdinalIgnoreCase))
            return Exterior;
        if (s.Equals(Mixed, StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Hỗn", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Both", StringComparison.OrdinalIgnoreCase))
            return Mixed;
        return null;
    }

    private static bool TryGet(IReadOnlyDictionary<string, object?> p, string key, out object? value)
    {
        if (p.TryGetValue(key, out value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
            return true;
        value = null;
        return false;
    }

    private static bool IsTruthy(IReadOnlyDictionary<string, object?> p, string key)
    {
        if (!TryGet(p, key, out var v)) return false;
        var s = v!.ToString()!.Trim();
        return s is "1" or "true" or "True" or "yes" or "Yes" ||
               (double.TryParse(s, out var d) && d > 0);
    }
}
