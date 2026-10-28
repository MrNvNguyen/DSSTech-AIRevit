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
/// Parse beam CAD labels: <c>D1Y19-40x60</c>, <c>D-40x60</c>, <c>30x60</c>, <c>D1PY6</c>.
/// If either WxH side has fewer than 3 digits, both sides are centimetres (×10);
/// only when both sides have 3+ digits are they millimetres as written.
/// </summary>
public static class BeamLabelParser
{
    private static readonly Regex SizeRx = new(
        @"(?<w>\d+(?:\.\d+)?)\s*[xX×*]\s*(?<h>\d+(?:\.\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MarkSizeRx = new(
        @"^(?<mark>[A-Za-z][A-Za-z0-9]*)\s*[-–—]?\s*(?<w>\d+(?:\.\d+)?)\s*[xX×*]\s*(?<h>\d+(?:\.\d+)?)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MarkOnlyRx = new(
        @"^(?<mark>[A-Za-z][A-Za-z0-9]{0,23})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? raw, out BeamLabel label)
    {
        label = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var input = raw.Trim().Replace(" ", "").Replace("×", "x").Replace("*", "x");
        input = Regex.Replace(input, "mm", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var markSize = MarkSizeRx.Match(input);
        if (markSize.Success &&
            TrySizePair(markSize.Groups["w"].Value, markSize.Groups["h"].Value, out var w1, out var h1))
        {
            var mark = markSize.Groups["mark"].Value.Trim().ToUpperInvariant();
            label = new BeamLabel(mark, w1, h1, raw.Trim());
            return true;
        }

        var sizeOnly = SizeRx.Match(input);
        if (sizeOnly.Success &&
            sizeOnly.Value.Length == input.Length &&
            TrySizePair(sizeOnly.Groups["w"].Value, sizeOnly.Groups["h"].Value, out var w2, out var h2))
        {
            label = new BeamLabel(null, w2, h2, raw.Trim());
            return true;
        }

        var markOnly = MarkOnlyRx.Match(input);
        if (markOnly.Success && CadMark.IsLabel(markOnly.Groups["mark"].Value))
        {
            label = new BeamLabel(markOnly.Groups["mark"].Value.Trim().ToUpperInvariant(), null, null, raw.Trim());
            return true;
        }

        // Embedded size inside a longer string (e.g. leftover formatting).
        if (sizeOnly.Success &&
            TrySizePair(sizeOnly.Groups["w"].Value, sizeOnly.Groups["h"].Value, out var w3, out var h3))
        {
            var prefix = input.Substring(0, sizeOnly.Index).Trim('-', '–', '—', ' ');
            string? mark = null;
            if (!string.IsNullOrWhiteSpace(prefix) && CadMark.IsLabel(prefix))
                mark = prefix.ToUpperInvariant();
            label = new BeamLabel(mark, w3, h3, raw.Trim());
            return true;
        }

        return false;
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
