using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Ceiling height written inside the ceiling outline on the plan: "+2.700", "CT +2700", "TRẦN THẠCH CAO H=2700",
/// "+2,7". Meters with a decimal point, millimetres without; heights outside 1500–10000 mm are not ceilings.
/// </summary>
public static class CeilingHeightReader
{
    private const double MinMm = 1500;
    private const double MaxMm = 10000;

    private static readonly Regex Meters = new(@"(?<![\d.])\+?\s*(\d{1,2}[.,]\d{1,3})(?![\d.,])", RegexOptions.CultureInvariant);
    private static readonly Regex Millimetres = new(
        @"(?:\+|CT\s*\+?|H\s*=\s*|CAO\s*ĐỘ\s*\+?|CĐ\s*\+?)\s*(\d{4,5})(?!\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex CeilingWord = new(@"\b(CT|TRẦN|TRAN|CEILING|CLG)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string? text, out double mm)
    {
        mm = 0;
        var s = (text ?? "").Trim();
        if (s.Length == 0)
            return false;
        var m = Millimetres.Match(s);
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
            v >= MinMm && v <= MaxMm)
        {
            mm = v;
            return true;
        }

        m = Meters.Match(s);
        if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) &&
            v * 1000 >= MinMm && v * 1000 <= MaxMm)
        {
            mm = Math.Round(v * 1000);
            return true;
        }

        return false;
    }

    /// <summary>Height from the texts inside the outline; a text naming the ceiling (CT, TRẦN) wins.</summary>
    public static bool TryFind(IReadOnlyList<CadPoint2> outline, IEnumerable<CadText> texts, out double mm, out string source)
    {
        mm = 0;
        source = "";
        var found = false;
        var named = false;
        foreach (var text in texts)
        {
            var x = text.CenterX ?? text.X;
            var y = text.CenterY ?? text.Y;
            if (!Inside(outline, x, y) || !TryParse(text.Value, out var h))
                continue;
            var isNamed = CeilingWord.IsMatch(text.Value);
            if (found && (named || !isNamed))
                continue;
            mm = h;
            source = text.Value.Trim();
            found = true;
            named = isNamed;
        }

        return found;
    }

    public static bool Inside(IReadOnlyList<CadPoint2> ring, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }
}
