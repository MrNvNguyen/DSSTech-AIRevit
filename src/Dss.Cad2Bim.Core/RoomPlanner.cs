using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

/// <summary>A room named on the plan: the name text's point, its name, and the number written beside it if any.</summary>
public sealed class RoomLabel
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
}

/// <summary>
/// Rooms from the plan's room-name texts, and the room separation lines that close the open gaps in wall lines
/// (passages without a door) so each named room comes out bounded.
/// </summary>
public static class RoomPlanner
{
    /// <summary>A number text this close to a name is that room's number.</summary>
    private const double NumberReachMm = 800;
    /// <summary>Wall-line gaps closed by a separation line: passages up to this wide.</summary>
    public const double MaxGapMm = 4000;
    private const double MinGapMm = 50;
    private const double CollinearTolMm = 50;
    private const double ParallelSin = 0.02;

    private static readonly Regex Area = new(@"(\bS\s*=|\bDT\b|\bm2\b|m²|\d\s*m2)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"^(P\.?\s*)?[A-Z]?\d{1,4}[A-Z]?$|^[A-Z]{1,3}[.\-]\d{1,4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Elevation = new(@"^[+\-±]\s*\d", RegexOptions.CultureInvariant);

    public static List<RoomLabel> Labels(IEnumerable<CadText> texts, IReadOnlyCollection<string> skipMarks)
    {
        var names = new List<RoomLabel>();
        var numbers = new List<(double X, double Y, string Value)>();
        foreach (var text in texts)
        {
            var value = Clean(text.Value);
            if (value.Length == 0 || Area.IsMatch(value) || Elevation.IsMatch(value) || CeilingHeightReader.TryParse(value, out _) ||
                skipMarks.Any(m => FoundationCatalogFilter.TypeMarkEquals(m, value)))
                continue;
            var x = text.CenterX ?? text.X;
            var y = text.CenterY ?? text.Y;
            if (Number.IsMatch(value))
            {
                numbers.Add((x, y, value));
                continue;
            }

            // A name has letters: "PHÒNG NGỦ 1", "WC", "SẢNH".
            if (!value.Any(char.IsLetter))
                continue;
            names.Add(new RoomLabel { X = x, Y = y, Name = value });
        }

        foreach (var (x, y, value) in numbers)
        {
            var nearest = names
                .Select(n => (Label: n, D: (n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y)))
                .Where(n => n.D <= NumberReachMm * NumberReachMm && n.Label.Number.Length == 0)
                .OrderBy(n => n.D)
                .Select(n => n.Label)
                .FirstOrDefault();
            if (nearest is not null)
                nearest.Number = value;
        }

        return names;
    }

    /// <summary>Name has one of these words (accents dropped): it is a room name wherever it sits.</summary>
    private static readonly HashSet<string> RoomWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PHONG", "NGU", "KHACH", "BEP", "WC", "VS", "TAM", "KHO", "SANH", "HANH", "LANG", "HL", "LOGIA", "LOGGIA",
        "CONG", "THANG", "KY", "THUAT", "KT", "GARA", "XE", "BAO", "TAN", "HOP", "HOI", "TRUONG", "CANTEEN", "CANTIN",
        "SINH", "HOAT", "THO", "GIAT", "PHOI", "CAFE", "SHOP", "KIOT", "KIOSK", "CAN", "HO", "LAM", "VIEC", "HOC", "DOC",
        "ROOM", "BED", "BEDROOM", "LIVING", "DINING", "KITCHEN", "TOILET", "BATH", "BATHROOM", "LOBBY", "CORRIDOR",
        "STORE", "STORAGE", "OFFICE", "MEETING", "HALL", "STAIR", "STAIRS", "LIFT", "ELEV", "SHAFT", "PANTRY",
        "LAUNDRY", "BALCONY", "TERRACE", "GARAGE", "PARKING", "SECURITY", "RECEPTION", "LOUNGE", "GYM", "POOL",
        "MECH", "ELEC", "ELECTRICAL", "UTILITY", "WAITING", "CLASS", "LAB"
    };

    /// <summary>Titles and notes, not room names.</summary>
    private static readonly string[] NotRoom = { "MAT BANG", "MAT CAT", "MAT DUNG", "TY LE", "GHI CHU", "NOTE", "SCALE", "BAN VE", "CHI TIET" };

    /// <summary>Layers that carry room names (A-AREA-IDEN, ROOM-NAME, TEN PHONG …).</summary>
    private static readonly Regex RoomLayer = new(@"ROOM|PHONG|IDEN|NAME|TEN|AREA|\bRM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AreaTail = new(@"(\bS\s*[=:].*$|\bDT\s*[=:].*$|\d+([.,]\d+)?\s*M2.*$|\d+([.,]\d+)?\s*M².*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LeadingNumber = new(@"^((?:P\.?\s*)?[A-Z]?\d{1,4}[A-Z]?)\s*[-–.:]?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Room names from every text of the drawing: a text with a room word (PHÒNG, WC, BẾP, SẢNH, LOBBY…) or on a
    /// room-name layer. "101 PHÒNG NGỦ" splits into number and name; an area written after the name is dropped.
    /// Strongest first (room layer and room word), so the best name of a room is met first.
    /// </summary>
    public static List<RoomLabel> Scan(IEnumerable<CadText> texts, IReadOnlyCollection<string> skipMarks)
    {
        var found = new List<(RoomLabel Label, int Score)>();
        var numbers = new List<(double X, double Y, string Value)>();
        foreach (var text in texts)
        {
            var value = AreaTail.Replace(Clean(text.Value), "").Trim(' ', '-', ',', ';');
            if (value.Length == 0 || Elevation.IsMatch(value) || CeilingHeightReader.TryParse(value, out _) ||
                skipMarks.Any(m => FoundationCatalogFilter.TypeMarkEquals(m, value)))
                continue;
            var x = text.CenterX ?? text.X;
            var y = text.CenterY ?? text.Y;
            if (Number.IsMatch(value))
            {
                numbers.Add((x, y, value));
                continue;
            }

            var number = "";
            var lead = LeadingNumber.Match(value);
            if (lead.Success)
            {
                number = lead.Groups[1].Value;
                value = lead.Groups[2].Value.Trim();
            }

            var plain = Plain(value);
            if (!value.Any(char.IsLetter) || NotRoom.Any(n => plain.Contains(n)))
                continue;
            var word = Regex.Split(plain, @"[^A-Z0-9]+").Any(t => RoomWords.Contains(t));
            var layer = RoomLayer.IsMatch(Plain(text.Layer ?? ""));
            if (!word && !layer)
                continue;
            found.Add((new RoomLabel { X = x, Y = y, Name = value, Number = number }, (word ? 1 : 0) + (layer ? 2 : 0)));
        }

        var names = found.OrderByDescending(f => f.Score).Select(f => f.Label).ToList();
        foreach (var (x, y, value) in numbers)
        {
            var nearest = names
                .Select(n => (Label: n, D: (n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y)))
                .Where(n => n.D <= NumberReachMm * NumberReachMm && n.Label.Number.Length == 0)
                .OrderBy(n => n.D)
                .Select(n => n.Label)
                .FirstOrDefault();
            if (nearest is not null)
                nearest.Number = value;
        }

        return names;
    }

    /// <summary>Upper case without accents: "Phòng ngủ" → "PHONG NGU".</summary>
    private static string Plain(string value)
    {
        var d = value.ToUpperInvariant().Replace('Đ', 'D').Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>MText codes and line breaks out: "\\PPHÒNG\\PNGỦ" → "PHÒNG NGỦ".</summary>
    private static string Clean(string? raw)
    {
        var s = raw ?? "";
        s = Regex.Replace(s, @"\\[Pp]", " ");
        s = Regex.Replace(s, @"\\[A-Za-z][^;\\]*;", "");
        s = s.Replace("{", "").Replace("}", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Separation lines across the gaps between two walls on one line (open passages): from a wall end to the
    /// facing end of the next wall on that line, when no other wall crosses the gap.
    /// </summary>
    public static List<(double X0, double Y0, double X1, double Y1)> Gaps(IReadOnlyList<HostWallAxis> walls)
    {
        var result = new List<(double, double, double, double)>();
        var used = new HashSet<(int, int)>();
        for (var i = 0; i < walls.Count; i++)
        {
            var a = walls[i];
            var (ux, uy, len) = Dir(a);
            if (len < 1)
                continue;
            foreach (var end in new[] { 0, 1 })
            {
                // From this end, outward along the wall.
                var ex = end == 0 ? a.X0 : a.X1;
                var ey = end == 0 ? a.Y0 : a.Y1;
                var dx = end == 0 ? -ux : ux;
                var dy = end == 0 ? -uy : uy;
                var best = -1;
                var bestEnd = 0;
                var bestGap = MaxGapMm;
                for (var j = 0; j < walls.Count; j++)
                {
                    if (j == i)
                        continue;
                    var b = walls[j];
                    var (bx, by, blen) = Dir(b);
                    if (blen < 1 || Math.Abs(bx * uy - by * ux) > ParallelSin)
                        continue;
                    if (Math.Abs((b.X0 - ex) * -dy + (b.Y0 - ey) * dx) > CollinearTolMm)
                        continue;
                    var s0 = (b.X0 - ex) * dx + (b.Y0 - ey) * dy;
                    var s1 = (b.X1 - ex) * dx + (b.Y1 - ey) * dy;
                    // The other wall starts ahead of this end, it does not overlap it.
                    var near = Math.Min(s0, s1);
                    if (near < MinGapMm || near >= bestGap || Math.Max(s0, s1) <= near)
                        continue;
                    best = j;
                    bestEnd = s0 <= s1 ? 0 : 1;
                    bestGap = near;
                }

                if (best < 0 || !used.Add((Math.Min(i, best), Math.Max(i, best))))
                    continue;
                var other = walls[best];
                var fx = bestEnd == 0 ? other.X0 : other.X1;
                var fy = bestEnd == 0 ? other.Y0 : other.Y1;
                if (Crossed(walls, i, best, ex, ey, fx, fy))
                    continue;
                result.Add((ex, ey, fx, fy));
            }
        }

        return result;
    }

    /// <summary>Another wall runs through the gap: it is a junction, not a passage.</summary>
    private static bool Crossed(IReadOnlyList<HostWallAxis> walls, int i, int j, double x0, double y0, double x1, double y1)
    {
        for (var k = 0; k < walls.Count; k++)
        {
            if (k == i || k == j)
                continue;
            var w = walls[k];
            var half = w.ThicknessMm / 2;
            if (SegDistance(x0, y0, x1, y1, w.X0, w.Y0, w.X1, w.Y1) <= half)
                return true;
        }

        return false;
    }

    private static double SegDistance(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double Cross(double px, double py, double qx, double qy, double rx, double ry) => (qx - px) * (ry - py) - (qy - py) * (rx - px);
        var d1 = Cross(cx, cy, dx, dy, ax, ay);
        var d2 = Cross(cx, cy, dx, dy, bx, by);
        var d3 = Cross(ax, ay, bx, by, cx, cy);
        var d4 = Cross(ax, ay, bx, by, dx, dy);
        if (d1 * d2 < 0 && d3 * d4 < 0)
            return 0;
        return Math.Min(Math.Min(PointSeg(ax, ay, cx, cy, dx, dy), PointSeg(bx, by, cx, cy, dx, dy)),
            Math.Min(PointSeg(cx, cy, ax, ay, bx, by), PointSeg(dx, dy, ax, ay, bx, by)));
    }

    private static double PointSeg(double px, double py, double x0, double y0, double x1, double y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var l2 = dx * dx + dy * dy;
        var t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((px - x0) * dx + (py - y0) * dy) / l2));
        var qx = x0 + dx * t - px;
        var qy = y0 + dy * t - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    private static (double Ux, double Uy, double Len) Dir(HostWallAxis w)
    {
        var dx = w.X1 - w.X0;
        var dy = w.Y1 - w.Y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-9 ? (1, 0, 0) : (dx / len, dy / len, len);
    }
}
