using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

public readonly struct BeamLabel
{
    public BeamLabel(string? mark, int? widthMm, int? depthMm, string raw)
    {
        Mark = mark;
        WidthMm = widthMm;
        DepthMm = depthMm;
        Raw = raw ?? "";
    }

    public string? Mark { get; }
    public int? WidthMm { get; }
    public int? DepthMm { get; }
    public string Raw { get; }

    public bool HasSize => WidthMm is > 0 && DepthMm is > 0;
    public bool HasMark => !string.IsNullOrWhiteSpace(Mark);
}

/// <summary>
/// Parse beam CAD labels: <c>D1Y19-40x60</c>, <c>D1Y-2 (60x70)</c>, <c>GTC3(30x40)</c>,
/// <c>D-40x60</c>, <c>30x60</c>, <c>D1PY6</c>.
/// If either WxH side has fewer than 3 digits, both sides are centimetres (×10);
/// only when both sides have 3+ digits are they millimetres as written.
/// Mark is the name only — never trailing <c>(</c> / size text.
/// </summary>
public static class BeamLabelParser
{
    private static readonly Regex SizeRx = new(
        @"(?<w>\d+(?:\.\d+)?)\s*[xX×*]\s*(?<h>\d+(?:\.\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Mark + optional separator + optional brackets around WxH.</summary>
    private static readonly Regex MarkSizeRx = new(
        @"^(?<mark>[A-Za-zĐđ][A-Za-zĐđ0-9\-]*?)\s*[-–—]?\s*[\(\[]?\s*(?<w>\d+(?:\.\d+)?)\s*[xX×*]\s*(?<h>\d+(?:\.\d+)?)\s*[\)\]]?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MarkOnlyRx = new(
        @"^(?<mark>[A-Za-zĐđ][A-Za-zĐđ0-9\-]{0,23})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? raw, out BeamLabel label)
    {
        label = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        // Keep hyphenated marks; drop spaces so "D1Y-2 (60x70)" and "D1Y-2(60x70)" share one path.
        var input = Regex.Replace(trimmed, @"\s+", "");
        input = input.Replace("×", "x").Replace("*", "x");
        input = Regex.Replace(input, "mm", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var markSize = MarkSizeRx.Match(input);
        if (markSize.Success &&
            TrySizePair(markSize.Groups["w"].Value, markSize.Groups["h"].Value, out var w1, out var h1))
        {
            var mark = CleanMark(markSize.Groups["mark"].Value);
            if (mark is null)
                return false;
            label = new BeamLabel(mark, w1, h1, trimmed);
            return true;
        }

        var sizeOnly = SizeRx.Match(input);
        if (sizeOnly.Success &&
            sizeOnly.Value.Length == input.Length &&
            TrySizePair(sizeOnly.Groups["w"].Value, sizeOnly.Groups["h"].Value, out var w2, out var h2))
        {
            label = new BeamLabel(null, w2, h2, trimmed);
            return true;
        }

        var markOnly = MarkOnlyRx.Match(input);
        if (markOnly.Success)
        {
            var mark = CleanMark(markOnly.Groups["mark"].Value);
            if (mark is not null && CadMark.IsLabel(mark))
            {
                label = new BeamLabel(mark, null, null, trimmed);
                return true;
            }
        }

        // Embedded size inside a longer string (e.g. leftover formatting / paren residue).
        if (sizeOnly.Success &&
            TrySizePair(sizeOnly.Groups["w"].Value, sizeOnly.Groups["h"].Value, out var w3, out var h3))
        {
            var prefix = input.Substring(0, sizeOnly.Index);
            var mark = CleanMark(prefix);
            label = new BeamLabel(mark, w3, h3, trimmed);
            return true;
        }

        var cleaned = CleanMark(input);
        if (cleaned is not null && CadMark.IsLabel(cleaned))
        {
            label = new BeamLabel(cleaned, null, null, trimmed);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Strip size separators and trailing <c>(</c>/<c>)</c> so Mark is name-only
    /// (<c>D1Y-2(</c> → <c>D1Y-2</c>).
    /// </summary>
    public static string? CleanMark(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var mark = value.Trim();
        mark = mark.TrimEnd('(', ')', '[', ']', '-', '–', '—', ' ', '\t');
        mark = mark.TrimStart('(', ')', '[', ']', ' ', '\t');
        if (mark.Length == 0)
            return null;
        mark = mark.ToUpperInvariant();
        return CadMark.IsLabel(mark) && !CadMark.IsSizeOnly(mark) ? mark : null;
    }

    /// <summary>
    /// Either side &lt; 3 digits → both cm×10; both sides 3+ digits → mm as written.
    /// </summary>
    public static bool TrySizePair(string wToken, string hToken, out int widthMm, out int depthMm)
    {
        widthMm = depthMm = 0;
        if (!TryParseSide(wToken, out var w) || !TryParseSide(hToken, out var h))
            return false;
        var scale = DigitLen(wToken) < 3 || DigitLen(hToken) < 3 ? 10.0 : 1.0;
        widthMm = Math.Max(1, (int)Math.Round(w * scale, MidpointRounding.AwayFromZero));
        depthMm = Math.Max(1, (int)Math.Round(h * scale, MidpointRounding.AwayFromZero));
        return true;
    }

    private static bool TryParseSide(string token, out double value) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;

    private static int DigitLen(string token)
    {
        var digits = token.IndexOf('.') is var dot and >= 0 ? token.Substring(0, dot) : token;
        digits = digits.TrimStart('0');
        if (digits.Length == 0)
            digits = "0";
        return digits.Length;
    }
}
