using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Dss.AssetInfo.Core;

/// <summary>
/// Pulls "label: value" pairs out of a free-text spec (Excel "Quy cách" or PDF text).
/// Algorithm = tools/validate_rules.py <c>extract()</c>:
///   pass 1: label at start of a line / bullet; pass 2: label anywhere;
///   then (Excel only, or when pdf.useUnitFallback) the first value that matches the unit regex.
/// </summary>
public sealed class SpecTextExtractor
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private readonly SpecRuleSet _rules;
    private readonly ConcurrentDictionary<string, Regex> _cache = new();

    public SpecTextExtractor(SpecRuleSet rules) => _rules = rules;

    public IReadOnlyDictionary<string, string> Extract(string? text, string? family, bool pdf = false)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return result;
        text = text!.Replace("\r", "");

        var useUnit = !pdf || _rules.Pdf.UseUnitFallback;
        foreach (var field in _rules.FieldsFor(family, pdf))
        {
            var value = Grab(text, field.Labels, useUnit ? field.Unit : null, pdf);
            if (!string.IsNullOrEmpty(value) && !result.ContainsKey(field.Param))
                result[field.Param] = value;
        }
        return result;
    }

    private string Grab(string text, IReadOnlyList<string> labels, string? unit, bool pdf)
    {
        var max = _rules.MaxValueLength;
        var tails = pdf && !string.IsNullOrEmpty(_rules.Pdf.ValueTailNoColon)
            ? new[] { _rules.ValueTail, _rules.Pdf.ValueTailNoColon }
            : new[] { _rules.ValueTail };

        foreach (var prefix in _rules.Prefixes)
        foreach (var label in labels)
        foreach (var tail in tails)
        {
            var m = SafeMatch(Get(prefix + label + tail + _rules.Separator, ignoreCase: true), text);
            if (m is null) continue;
            var v = Whitespace.Replace(m.Groups[1].Value, " ").Trim(_rules.ValueTrim.ToCharArray());
            // A value must carry at least one letter/digit ("+" or "-" alone is a parsing artefact).
            if (v.Length > 0 && v.Any(char.IsLetterOrDigit) && v.Length <= max * 2)
                return v.Length > max ? v[..max] : v;
        }

        if (!string.IsNullOrEmpty(unit))
        {
            var m = SafeMatch(Get(unit!, ignoreCase: false), text);
            if (m is not null)
            {
                var v = Whitespace.Replace(m.Value, " ").Trim();
                return v.Length > max ? v[..max] : v;
            }
        }
        return "";
    }

    private Regex Get(string pattern, bool ignoreCase)
    {
        var key = (ignoreCase ? "i:" : "c:") + pattern;
        return _cache.GetOrAdd(key, _ =>
        {
            var opts = RegexOptions.Singleline | RegexOptions.CultureInvariant;
            if (ignoreCase) opts |= RegexOptions.IgnoreCase;
            return new Regex(pattern, opts, MatchTimeout);
        });
    }

    private static Match? SafeMatch(Regex re, string text)
    {
        try
        {
            var m = re.Match(text);
            return m.Success ? m : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null; // pathological text: skip this label instead of freezing Revit
        }
    }
}
