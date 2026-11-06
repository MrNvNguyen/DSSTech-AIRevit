using System.Text;
using System.Text.RegularExpressions;

namespace Dss.AssetInfo.Core;

/// <summary>
/// Normalised tokens used to find a model code in PDF text or Revit type names.
/// Same as tools/validate_rules.py <c>model_tokens()</c>.
/// </summary>
public static class ModelTokens
{
    private static readonly Regex Splitter = new(@"[\s/,;+]+|:", RegexOptions.Compiled);

    /// <summary>Upper-case, accents removed, only A-Z and 0-9 kept ("APL 450 4P-1" → "APL4504P1").</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s!.ToUpperInvariant().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Whole model, its parts and adjacent pairs — each must hold a letter AND a digit,
    /// so "4000" or "UPS" alone never match (tested: "UPS SDU 4000" must not hit a "16V4000" genset page).
    /// </summary>
    public static IReadOnlyList<string> From(string? model, int minLength = 4)
    {
        var parts = Splitter.Split(model ?? "").Select(Normalize).Where(p => p.Length > 0).ToList();
        var candidates = new List<string> { string.Concat(parts) };
        candidates.AddRange(parts);
        for (var i = 0; i + 1 < parts.Count; i++)
            candidates.Add(parts[i] + parts[i + 1]);

        var tokens = new List<string>();
        foreach (var t in candidates)
        {
            if (t.Length < minLength || tokens.Contains(t)) continue;
            if (t.Any(char.IsDigit) && t.Any(c => c is >= 'A' and <= 'Z'))
                tokens.Add(t);
        }
        return tokens;
    }

    public static bool AnyIn(IReadOnlyList<string> tokens, string normalizedText) =>
        tokens.Any(t => normalizedText.Contains(t, StringComparison.Ordinal));
}
