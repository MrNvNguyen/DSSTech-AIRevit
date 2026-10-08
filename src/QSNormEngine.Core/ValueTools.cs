using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QSNormEngine.Core;

public static class ValueTools
{
    public static string NormalizeText(object? value)
    {
        if (value is null) return "";
        var s = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        s = RemoveDiacritics(s).ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", " ");
        return s;
    }

    public static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ','d').Replace('Đ','D');
    }

    public static bool TryDouble(object? value, out double number)
    {
        number = 0;
        if (value is null) return false;
        if (value is double d) { number = d; return true; }
        if (value is float f) { number = f; return true; }
        if (value is decimal m) { number = (double)m; return true; }
        if (value is int i) { number = i; return true; }
        if (value is long l) { number = l; return true; }
        if (value is JsonElementWrapper j && j.TryGetDouble(out number)) return true;
        var s = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim().Replace(',', '.') ?? "";
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }

    public static bool TryBool(object? value, out bool result)
    {
        result = false;
        if (value is bool b) { result = b; return true; }
        var s = NormalizeText(value);
        if (s is "1" or "true" or "yes" or "y" or "co" or "có") { result = true; return true; }
        if (s is "0" or "false" or "no" or "n" or "khong" or "không") { result = false; return true; }
        return false;
    }

    public static string[] SplitTokens(string text, params char[] separators)
    {
        var seps = separators.Length > 0 ? separators : ['|', ';', ','];
        return text.Split(seps, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => s.Length > 0)
                   .ToArray();
    }

    public static bool ContainsIgnoreCase(string text, string value) =>
        text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    // Placeholder abstraction for future non-System.Text.Json adapters.
    public readonly struct JsonElementWrapper
    {
        private readonly object _value;
        public JsonElementWrapper(object value) => _value = value;
        public bool TryGetDouble(out double d) => double.TryParse(Convert.ToString(_value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
    }
}
