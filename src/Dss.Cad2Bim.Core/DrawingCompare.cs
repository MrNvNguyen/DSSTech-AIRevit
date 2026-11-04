using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

public enum CompareStatus
{
    Match,
    /// <summary>On the drawing, not in the model.</summary>
    Missing,
    /// <summary>In the model, no longer on the drawing.</summary>
    Extra,
    Moved,
    Changed,
    /// <summary>Drawing against drawing: only in the new one.</summary>
    Added,
    /// <summary>Drawing against drawing: only in the old one.</summary>
    Removed
}

public enum CompareShape
{
    Point,
    Line
}

/// <summary>One thing to compare, from the drawing or the model, in plan millimetres.</summary>
public sealed class CompareItem
{
    public string Kind { get; set; } = "";
    public CompareShape Shape { get; set; }
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    /// <summary>Grid name, room name, Type Mark: compared as text.</summary>
    public string Name { get; set; } = "";
    /// <summary>Size as text (400x600, D150): compared as numbers within 5 mm.</summary>
    public string Size { get; set; } = "";
    /// <summary>Revit element id on the model side; 0 on the drawing side.</summary>
    public long Id { get; set; }
    public string Layer { get; set; } = "";

    public double Length => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
    public string Label => Name.Length > 0 ? Name : Size;
}

public sealed class CompareResult
{
    public CompareStatus Status { get; set; }
    public string Kind { get; set; } = "";
    public CompareItem? Cad { get; set; }
    public CompareItem? Model { get; set; }
    public double OffsetMm { get; set; }
    public string Detail { get; set; } = "";
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double X => (MinX + MaxX) / 2;
    public double Y => (MinY + MaxY) / 2;

    public static string StatusText(CompareStatus s) => s switch
    {
        CompareStatus.Match => "Khớp",
        CompareStatus.Missing => "Thiếu trong mô hình",
        CompareStatus.Extra => "Thừa (bản vẽ đã bỏ)",
        CompareStatus.Moved => "Lệch vị trí",
        CompareStatus.Changed => "Khác thông số",
        CompareStatus.Added => "Bản mới thêm",
        CompareStatus.Removed => "Bản mới xoá",
        _ => s.ToString()
    };
}

public sealed class CompareOptions
{
    /// <summary>Closer than this counts as the same place.</summary>
    public double ToleranceMm { get; set; } = 10;
    /// <summary>A point further than this from every counterpart is missing, not moved.</summary>
    public double PointReachMm { get; set; } = 1000;
    /// <summary>A line further than this (sideways) from every counterpart is missing, not moved.</summary>
    public double LineReachMm { get; set; } = 600;
    public double SizeToleranceMm { get; set; } = 5;
}

/// <summary>
/// Model against drawing: what the drawing asks for (read by the same readers the modelling tools use) against
/// what the model holds, kind by kind. Points pair by distance; lines by how much of each is covered by the
/// other side; grids by name.
/// </summary>
public static class DrawingCompare
{
    private const double ParallelDeg = 3;
    private const double CoveredShare = 0.95;
    private const double TouchedShare = 0.05;

    public static List<CompareResult> Compare(IReadOnlyList<CompareItem> cad, IReadOnlyList<CompareItem> model, CompareOptions? options = null)
    {
        var opt = options ?? new CompareOptions();
        var results = new List<CompareResult>();
        foreach (var kind in cad.Select(c => c.Kind).Concat(model.Select(m => m.Kind)).Distinct())
        {
            var c = cad.Where(x => x.Kind == kind).ToList();
            var m = model.Where(x => x.Kind == kind).ToList();
            var named = c.Concat(m).All(x => x.Shape == CompareShape.Line && x.Name.Length > 0);
            if (named && c.Count > 0 && m.Count > 0)
                results.AddRange(Named(kind, c, m, opt));
            else if (c.Concat(m).All(x => x.Shape == CompareShape.Point))
                results.AddRange(Points(kind, c, m, opt));
            else
                results.AddRange(Lines(kind, c.Where(x => x.Shape == CompareShape.Line).ToList(), m.Where(x => x.Shape == CompareShape.Line).ToList(), opt));
        }

        return results;
    }

    /// <summary>Grids: the same name on both sides; the offset is the sideways gap between the lines.</summary>
    private static IEnumerable<CompareResult> Named(string kind, List<CompareItem> cad, List<CompareItem> model, CompareOptions opt)
    {
        var byName = model.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<CompareItem>();
        foreach (var c in cad)
        {
            if (!byName.TryGetValue(c.Name, out var m))
            {
                yield return Result(CompareStatus.Missing, kind, c, null, 0, $"{kind} {c.Name} chưa có trong mô hình");
                continue;
            }

            used.Add(m);
            var angle = AngleDeg(c, m);
            var offset = Math.Abs(Side(m, (c.X0 + c.X1) / 2, (c.Y0 + c.Y1) / 2));
            if (angle > 0.1)
                yield return Result(CompareStatus.Moved, kind, c, m, offset, $"{kind} {c.Name}: lệch góc {angle:0.##}°, lệch {offset:0} mm");
            else if (offset > opt.ToleranceMm)
                yield return Result(CompareStatus.Moved, kind, c, m, offset, $"{kind} {c.Name}: lệch {offset:0} mm");
            else
                yield return Result(CompareStatus.Match, kind, c, m, offset, "");
        }

        foreach (var m in model.Where(m => !used.Contains(m)))
            yield return Result(CompareStatus.Extra, kind, null, m, 0, $"{kind} {m.Name} không còn trên bản vẽ");
    }

    private static IEnumerable<CompareResult> Points(string kind, List<CompareItem> cad, List<CompareItem> model, CompareOptions opt)
    {
        var pairs = new List<(double D, int C, int M)>();
        for (var i = 0; i < cad.Count; i++)
        for (var j = 0; j < model.Count; j++)
        {
            var d = Dist(cad[i].X0, cad[i].Y0, model[j].X0, model[j].Y0);
            if (d <= opt.PointReachMm)
                pairs.Add((d, i, j));
        }

        var cadOf = new int[model.Count];
        var modelOf = Enumerable.Repeat(-1, cad.Count).ToArray();
        for (var j = 0; j < cadOf.Length; j++)
            cadOf[j] = -1;
        // A label match pulls the pair together before distance does (two doors side by side, D1 and D2).
        foreach (var (_, i, j) in pairs.OrderBy(p => SameText(cad[p.C].Name, model[p.M].Name) ? 0 : 1).ThenBy(p => p.D))
        {
            if (modelOf[i] >= 0 || cadOf[j] >= 0)
                continue;
            modelOf[i] = j;
            cadOf[j] = i;
        }

        for (var i = 0; i < cad.Count; i++)
        {
            var c = cad[i];
            if (modelOf[i] < 0)
            {
                yield return Result(CompareStatus.Missing, kind, c, null, 0, $"{kind} {c.Label} chưa có trong mô hình".Replace("  ", " "));
                continue;
            }

            var m = model[modelOf[i]];
            var d = Dist(c.X0, c.Y0, m.X0, m.Y0);
            var changes = Differences(c, m, opt);
            if (changes.Length > 0)
                yield return Result(CompareStatus.Changed, kind, c, m, d, $"{kind}: {changes}" + (d > opt.ToleranceMm ? $"; lệch {d:0} mm" : ""));
            else if (d > opt.ToleranceMm)
                yield return Result(CompareStatus.Moved, kind, c, m, d, $"{kind} {c.Label}: lệch {d:0} mm".Replace("  ", " "));
            else
                yield return Result(CompareStatus.Match, kind, c, m, d, "");
        }

        for (var j = 0; j < model.Count; j++)
        {
            if (cadOf[j] < 0)
                yield return Result(CompareStatus.Extra, kind, null, model[j], 0, $"{kind} {model[j].Label} không còn trên bản vẽ".Replace("  ", " "));
        }
    }

    private static IEnumerable<CompareResult> Lines(string kind, List<CompareItem> cad, List<CompareItem> model, CompareOptions opt)
    {
        var modelSeen = new HashSet<CompareItem>();
        foreach (var c in cad)
        {
            if (c.Length < 1)
                continue;
            var exact = Coverage(c, model, opt.ToleranceMm, out _);
            CompareItem? best = null;
            double bestOverlap = 0, bestOffset = 0;
            foreach (var m in model)
            {
                if (AngleDeg(c, m) > ParallelDeg)
                    continue;
                var offset = Math.Abs(Side(m, (c.X0 + c.X1) / 2, (c.Y0 + c.Y1) / 2));
                if (offset > opt.LineReachMm)
                    continue;
                var overlap = Overlap(c, m);
                if (overlap > bestOverlap)
                {
                    best = m;
                    bestOverlap = overlap;
                    bestOffset = offset;
                }
            }

            if (best is not null)
                modelSeen.Add(best);
            foreach (var m in model)
            {
                if (AngleDeg(c, m) <= ParallelDeg && Math.Abs(Side(m, (c.X0 + c.X1) / 2, (c.Y0 + c.Y1) / 2)) <= opt.ToleranceMm && Overlap(c, m) > 0)
                    modelSeen.Add(m);
            }

            var share = exact / c.Length;
            if (share >= CoveredShare)
            {
                var size = best is null ? "" : Differences(c, best, opt);
                yield return size.Length > 0
                    ? Result(CompareStatus.Changed, kind, c, best, 0, $"{kind}: {size}")
                    : Result(CompareStatus.Match, kind, c, best, 0, "");
            }
            else if (best is not null && bestOverlap >= c.Length * 0.5 && bestOffset > opt.ToleranceMm)
            {
                var size = Differences(c, best, opt);
                yield return Result(size.Length > 0 ? CompareStatus.Changed : CompareStatus.Moved, kind, c, best, bestOffset,
                    $"{kind}: lệch {bestOffset:0} mm" + (size.Length > 0 ? "; " + size : ""));
            }
            else if (share >= TouchedShare)
                yield return Result(CompareStatus.Changed, kind, c, best, 0, $"{kind}: chiều dài khác, thiếu {c.Length - exact:0} mm trên {c.Length:0} mm");
            else
                yield return Result(CompareStatus.Missing, kind, c, null, 0, $"{kind} {c.Label} dài {c.Length:0} mm chưa có trong mô hình".Replace("  ", " "));
        }

        foreach (var m in model)
        {
            if (modelSeen.Contains(m) || m.Length < 1)
                continue;
            var reach = Coverage(m, cad, opt.LineReachMm, out _);
            if (reach / m.Length < TouchedShare)
                yield return Result(CompareStatus.Extra, kind, null, m, 0, $"{kind} {m.Label} dài {m.Length:0} mm không còn trên bản vẽ".Replace("  ", " "));
        }
    }

    /// <summary>Length of <paramref name="line"/> lying within <paramref name="gapMm"/> of parallel lines in <paramref name="others"/>.</summary>
    private static double Coverage(CompareItem line, IEnumerable<CompareItem> others, double gapMm, out int count)
    {
        var len = line.Length;
        var ux = (line.X1 - line.X0) / len;
        var uy = (line.Y1 - line.Y0) / len;
        var spans = new List<(double A, double B)>();
        foreach (var o in others)
        {
            if (AngleDeg(line, o) > ParallelDeg)
                continue;
            if (Math.Abs(Side(line, o.X0, o.Y0)) > gapMm || Math.Abs(Side(line, o.X1, o.Y1)) > gapMm)
                continue;
            var a = (o.X0 - line.X0) * ux + (o.Y0 - line.Y0) * uy;
            var b = (o.X1 - line.X0) * ux + (o.Y1 - line.Y0) * uy;
            var lo = Math.Max(0, Math.Min(a, b));
            var hi = Math.Min(len, Math.Max(a, b));
            if (hi > lo)
                spans.Add((lo, hi));
        }

        count = spans.Count;
        double covered = 0, end = 0;
        foreach (var (a, b) in spans.OrderBy(s => s.A))
        {
            var from = Math.Max(a, end);
            if (b > from)
            {
                covered += b - from;
                end = b;
            }
        }

        return covered;
    }

    /// <summary>How long the two lines run side by side (projection of <paramref name="b"/> onto <paramref name="a"/>).</summary>
    private static double Overlap(CompareItem a, CompareItem b)
    {
        var len = a.Length;
        var ux = (a.X1 - a.X0) / len;
        var uy = (a.Y1 - a.Y0) / len;
        var s0 = (b.X0 - a.X0) * ux + (b.Y0 - a.Y0) * uy;
        var s1 = (b.X1 - a.X0) * ux + (b.Y1 - a.Y0) * uy;
        return Math.Max(0, Math.Min(len, Math.Max(s0, s1)) - Math.Max(0, Math.Min(s0, s1)));
    }

    /// <summary>Signed distance of a point from the infinite line through <paramref name="line"/>.</summary>
    private static double Side(CompareItem line, double x, double y)
    {
        var len = line.Length;
        if (len < 1e-9)
            return Dist(line.X0, line.Y0, x, y);
        return ((x - line.X0) * (line.Y1 - line.Y0) - (y - line.Y0) * (line.X1 - line.X0)) / len;
    }

    private static double AngleDeg(CompareItem a, CompareItem b)
    {
        var t = Math.Abs(Math.Atan2(a.Y1 - a.Y0, a.X1 - a.X0) - Math.Atan2(b.Y1 - b.Y0, b.X1 - b.X0)) * 180 / Math.PI % 180;
        return Math.Min(t, 180 - t);
    }

    private static string Differences(CompareItem cad, CompareItem model, CompareOptions opt)
    {
        var parts = new List<string>();
        if (cad.Name.Length > 0 && model.Name.Length > 0 && !SameText(cad.Name, model.Name))
            parts.Add($"bản vẽ «{cad.Name}», mô hình «{model.Name}»");
        if (cad.Size.Length > 0 && model.Size.Length > 0 && !SameSize(cad.Size, model.Size, opt.SizeToleranceMm))
            parts.Add($"size bản vẽ {cad.Size}, mô hình {model.Size}");
        return string.Join("; ", parts);
    }

    public static bool SameSize(string a, string b, double tolMm)
    {
        var x = Numbers(a);
        var y = Numbers(b);
        if (x.Count == 0 || y.Count == 0)
            return true;
        if (x.Count != y.Count)
            return false;
        x.Sort();
        y.Sort();
        return x.Zip(y, (p, q) => Math.Abs(p - q) <= tolMm).All(ok => ok);
    }

    private static List<double> Numbers(string s) =>
        Regex.Matches(s ?? "", @"\d+(\.\d+)?").Cast<Match>()
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();

    private static bool SameText(string a, string b) =>
        string.Equals(DevicePlanner.Plain(a), DevicePlanner.Plain(b), StringComparison.Ordinal);

    private static double Dist(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    private static CompareResult Result(CompareStatus status, string kind, CompareItem? cad, CompareItem? model, double offset, string detail)
    {
        var pts = new[] { cad, model }.Where(i => i is not null)
            .SelectMany(i => new[] { (i!.X0, i.Y0), (i.Shape == CompareShape.Line ? i.X1 : i.X0, i.Shape == CompareShape.Line ? i.Y1 : i.Y0) }).ToList();
        return new CompareResult
        {
            Status = status,
            Kind = kind,
            Cad = cad,
            Model = model,
            OffsetMm = offset,
            Detail = detail,
            MinX = pts.Min(p => p.Item1),
            MinY = pts.Min(p => p.Item2),
            MaxX = pts.Max(p => p.Item1),
            MaxY = pts.Max(p => p.Item2)
        };
    }
}

/// <summary>
/// Drawing against drawing: every line, circle, polyline, block and text of the old drawing looked for in the new
/// one (same layer, within the tolerance), and the other way round. Changes close together on a layer become one
/// area to cloud.
/// </summary>
public static class CadDiff
{
    private sealed class Shape
    {
        public string Layer = "";
        public char Type;
        public double[] Coords = Array.Empty<double>();
        public string Text = "";
        public double MinX, MinY, MaxX, MaxY;
    }

    public static List<CompareResult> Compare(CadGeometry before, CadGeometry after, double toleranceMm = 5, double groupMm = 1500)
    {
        var a = Shapes(before);
        var b = Shapes(after);
        var cell = Math.Max(toleranceMm * 4, 50);
        var indexB = Index(b, cell);
        var indexA = Index(a, cell);
        var raw = new List<(CompareStatus Status, Shape S, string Note)>();
        var matchedB = new HashSet<Shape>();
        foreach (var s in a)
        {
            var same = Near(indexB, s, cell).FirstOrDefault(o => !matchedB.Contains(o) && Same(s, o, toleranceMm, textToo: true));
            if (same is not null)
            {
                matchedB.Add(same);
                continue;
            }

            // A text at the same place with other words is a text edit, not a delete and an add.
            var edited = s.Type == 'T'
                ? Near(indexB, s, cell).FirstOrDefault(o => !matchedB.Contains(o) && Same(s, o, toleranceMm, textToo: false))
                : null;
            if (edited is not null)
            {
                matchedB.Add(edited);
                raw.Add((CompareStatus.Changed, edited, $"«{s.Text}» → «{edited.Text}»"));
            }
            else
                raw.Add((CompareStatus.Removed, s, ""));
        }

        foreach (var s in b)
        {
            if (!matchedB.Contains(s) && !Near(indexA, s, cell).Any(o => Same(s, o, toleranceMm, textToo: true)))
                raw.Add((CompareStatus.Added, s, ""));
        }

        return Group(raw, groupMm);
    }

    private static List<CompareResult> Group(List<(CompareStatus Status, Shape S, string Note)> raw, double groupMm)
    {
        var results = new List<CompareResult>();
        foreach (var layer in raw.GroupBy(r => r.S.Layer))
        {
            var items = layer.ToList();
            var groupOf = new int[items.Count];
            for (var i = 0; i < groupOf.Length; i++)
                groupOf[i] = i;
            int Find(int i) => groupOf[i] == i ? i : groupOf[i] = Find(groupOf[i]);
            var cell = Math.Max(groupMm, 1);
            var grid = new Dictionary<(long, long), List<int>>();
            for (var i = 0; i < items.Count; i++)
            {
                var s = items[i].S;
                var key = ((long)Math.Floor((s.MinX + s.MaxX) / 2 / cell), (long)Math.Floor((s.MinY + s.MaxY) / 2 / cell));
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!grid.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var list))
                        continue;
                    foreach (var j in list)
                    {
                        var o = items[j].S;
                        if (s.MinX - groupMm <= o.MaxX && o.MinX - groupMm <= s.MaxX && s.MinY - groupMm <= o.MaxY && o.MinY - groupMm <= s.MaxY)
                            groupOf[Find(i)] = Find(j);
                    }
                }

                if (!grid.TryGetValue(key, out var here))
                    grid[key] = here = new List<int>();
                here.Add(i);
            }

            foreach (var g in Enumerable.Range(0, items.Count).GroupBy(Find))
            {
                var members = g.Select(i => items[i]).ToList();
                var added = members.Count(m => m.Status == CompareStatus.Added);
                var removed = members.Count(m => m.Status == CompareStatus.Removed);
                var edits = members.Where(m => m.Status == CompareStatus.Changed).Select(m => m.Note).ToList();
                var status = edits.Count > 0 || (added > 0 && removed > 0) ? CompareStatus.Changed
                    : added > 0 ? CompareStatus.Added : CompareStatus.Removed;
                var parts = new List<string>();
                if (added > 0)
                    parts.Add($"thêm {added}");
                if (removed > 0)
                    parts.Add($"xoá {removed}");
                if (edits.Count > 0)
                    parts.Add("sửa chữ " + string.Join(", ", edits.Take(3)) + (edits.Count > 3 ? $" (+{edits.Count - 3})" : ""));
                results.Add(new CompareResult
                {
                    Status = status,
                    Kind = layer.Key,
                    Detail = $"Layer {layer.Key}: {string.Join("; ", parts)} đối tượng",
                    MinX = members.Min(m => m.S.MinX),
                    MinY = members.Min(m => m.S.MinY),
                    MaxX = members.Max(m => m.S.MaxX),
                    MaxY = members.Max(m => m.S.MaxY)
                });
            }
        }

        return results.OrderBy(r => r.Kind).ThenBy(r => r.MinY).ThenBy(r => r.MinX).ToList();
    }

    private static List<Shape> Shapes(CadGeometry g)
    {
        var list = new List<Shape>();
        foreach (var e in g.Entities)
        {
            switch (e)
            {
                case CadLine l:
                    list.Add(Make(e.Layer, 'L', new[] { l.X0, l.Y0, l.X1, l.Y1 }));
                    break;
                case CadCircle c:
                    list.Add(Make(e.Layer, 'C', new[] { c.Cx, c.Cy, c.Radius, 0 }, c.Radius));
                    break;
                case CadPolyline p when p.Points.Count > 0:
                    list.Add(Make(e.Layer, 'P', p.Points.SelectMany(q => new[] { q.X, q.Y }).ToArray()));
                    break;
                case CadInsert i:
                {
                    var s = Make(e.Layer, 'I', new[] { i.X, i.Y, i.RotationDeg, 0 });
                    s.Text = i.BlockName;
                    list.Add(s);
                    break;
                }
                case CadText t:
                {
                    var s = Make(e.Layer, 'T', new[] { t.X, t.Y, 0, 0 });
                    s.Text = t.Value.Trim();
                    list.Add(s);
                    break;
                }
            }
        }

        return list;
    }

    private static Shape Make(string layer, char type, double[] coords, double radius = 0)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var pointCount = type is 'L' or 'P' ? coords.Length / 2 : 1;
        for (var k = 0; k < pointCount; k++)
        {
            minX = Math.Min(minX, coords[2 * k] - radius);
            minY = Math.Min(minY, coords[2 * k + 1] - radius);
            maxX = Math.Max(maxX, coords[2 * k] + radius);
            maxY = Math.Max(maxY, coords[2 * k + 1] + radius);
        }

        return new Shape { Layer = layer, Type = type, Coords = coords, MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
    }

    private static Dictionary<(long, long), List<Shape>> Index(List<Shape> shapes, double cell)
    {
        var index = new Dictionary<(long, long), List<Shape>>();
        foreach (var s in shapes)
        {
            var key = Key(s, cell);
            if (!index.TryGetValue(key, out var list))
                index[key] = list = new List<Shape>();
            list.Add(s);
        }

        return index;
    }

    /// <summary>Shapes are filed by the lower-left corner of their box, which moves little when nothing changed.</summary>
    private static (long, long) Key(Shape s, double cell) => ((long)Math.Floor(s.MinX / cell), (long)Math.Floor(s.MinY / cell));

    private static IEnumerable<Shape> Near(Dictionary<(long, long), List<Shape>> index, Shape s, double cell)
    {
        var (kx, ky) = Key(s, cell);
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (index.TryGetValue((kx + dx, ky + dy), out var list))
            {
                foreach (var o in list)
                    yield return o;
            }
        }
    }

    private static bool Same(Shape a, Shape b, double tol, bool textToo)
    {
        if (a.Type != b.Type || !string.Equals(a.Layer, b.Layer, StringComparison.OrdinalIgnoreCase) || a.Coords.Length != b.Coords.Length)
            return false;
        if (textToo && !string.Equals(a.Text, b.Text, StringComparison.Ordinal))
            return false;
        if (a.Type == 'I' && !string.Equals(a.Text, b.Text, StringComparison.OrdinalIgnoreCase))
            return false;
        if (a.Type == 'I')
        {
            var turn = Math.Abs(a.Coords[2] - b.Coords[2]) % 360;
            return Close(a.Coords, b.Coords, 0, 2, tol) && Math.Min(turn, 360 - turn) < 0.5;
        }

        if (Close(a.Coords, b.Coords, 0, a.Coords.Length, tol))
            return true;
        // Lines and polylines drawn the other way round.
        if (a.Type is 'L' or 'P')
        {
            var n = a.Coords.Length / 2;
            for (var k = 0; k < n; k++)
            {
                if (Math.Abs(a.Coords[2 * k] - b.Coords[2 * (n - 1 - k)]) > tol || Math.Abs(a.Coords[2 * k + 1] - b.Coords[2 * (n - 1 - k) + 1]) > tol)
                    return false;
            }

            return true;
        }

        return false;
    }

    private static bool Close(double[] a, double[] b, int from, int to, double tol)
    {
        for (var k = from; k < to; k++)
        {
            if (Math.Abs(a[k] - b[k]) > tol)
                return false;
        }

        return true;
    }
}
