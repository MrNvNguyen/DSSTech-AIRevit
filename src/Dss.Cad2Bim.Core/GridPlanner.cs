using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

/// <summary>One grid to create: its name and its line (model mm).</summary>
public sealed class GridLine
{
    public string Name { get; set; } = "";
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
}

/// <summary>
/// Grids read from the drawing, whatever their layer: a grid is a straight line running into a bubble (a circle
/// holding a short label such as 1, A, 1.A, 2'). Lines drawn as dashes are joined. Grids of one direction are made
/// exactly parallel and placed on whole 5 mm spacings from the first one, so every grid-to-grid dimension is even.
/// </summary>
public static class GridPlanner
{
    private const double MinBubbleMm = 150;
    private const double MaxBubbleMm = 1500;
    /// <summary>The line's end comes this close to the bubble's rim (lines stop at the rim or just short of it).</summary>
    private const double RimReachMm = 300;
    private const double MinGridMm = 1000;
    private const double SameLineMm = 5;
    private const double ParallelDeg = 0.5;

    /// <summary>A grid's name: A, AA, 1, 1A, 1.A, 2', A-1, and a letter with a number (B1, C12: skewed grids).</summary>
    private static readonly Regex Label = new(@"^([A-Z]{1,2}'?|\d{1,3}[A-Z]?'?|\d{1,2}[.\-][A-Z0-9]{1,3}'?|[A-Z][.\-]\d{1,3}'?|[A-Z]{1,2}\d{1,3}'?)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly struct Seg
    {
        public Seg(double x0, double y0, double x1, double y1, string layer)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Layer = layer;
        }

        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public string Layer { get; }
        public double Len => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
    }

    public static List<GridLine> Plan(CadGeometry geom, Action<string>? log = null)
    {
        var texts = geom.Entities.OfType<CadText>().Concat(geom.NumberTexts).ToList();
        var bubbles = new List<(double X, double Y, double R, string Name)>();
        foreach (var c in geom.Entities.OfType<CadCircle>().Where(c => c.Radius >= MinBubbleMm && c.Radius <= MaxBubbleMm))
        {
            var label = texts
                .Select(t => (Text: t, D: Math.Sqrt(Sq((t.CenterX ?? t.X) - c.Cx) + Sq((t.CenterY ?? t.Y) - c.Cy))))
                .Where(t => t.D <= c.Radius && Label.IsMatch(t.Text.Value.Trim()))
                .OrderBy(t => t.D)
                .Select(t => t.Text.Value.Trim())
                .FirstOrDefault();
            if (label is not null)
                bubbles.Add((c.Cx, c.Cy, c.Radius, label));
        }

        var segs = new List<Seg>();
        foreach (var e in geom.Entities)
        {
            if (e is CadLine l)
                segs.Add(new Seg(l.X0, l.Y0, l.X1, l.Y1, l.Layer));
            else if (e is CadPolyline p)
            {
                for (var i = 0; i + 1 < p.Points.Count; i++)
                    segs.Add(new Seg(p.Points[i].X, p.Points[i].Y, p.Points[i + 1].X, p.Points[i + 1].Y, p.Layer));
            }
        }

        segs = segs.Where(s => s.Len > 1).ToList();
        // Grid layers: the layers of lines aimed at a bubble's center that end at its rim.
        var gridLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in bubbles)
        {
            foreach (var s in segs)
            {
                if (TouchesBubble(s, b.X, b.Y, b.R, out _))
                    gridLayers.Add(s.Layer);
            }
        }

        var candidates = segs.Where(s => gridLayers.Contains(s.Layer)).ToList();
        var grids = new List<(GridLine Line, double Ux, double Uy)>();
        foreach (var b in bubbles)
        {
            var touching = candidates
                .Select(s => (Seg: s, Ok: TouchesBubble(s, b.X, b.Y, b.R, out var dir), Dir: dir))
                .Where(t => t.Ok)
                .OrderByDescending(t => t.Seg.Len)
                .FirstOrDefault();
            if (!touching.Ok)
            {
                log?.Invoke($"grid {b.Name}: bubble at ({b.X:0},{b.Y:0}) has no line");
                continue;
            }

            var (ux, uy) = touching.Dir;
            // The grid lies where the line is drawn; the bubble only names it and may sit a little off.
            var ax = touching.Seg.X0;
            var ay = touching.Seg.Y0;
            // Every piece on this line (dashes, the part beyond a crossing grid): one grid from end to end.
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var s in candidates)
            {
                if (!OnLine(s, ax, ay, ux, uy))
                    continue;
                var a = (s.X0 - ax) * ux + (s.Y0 - ay) * uy;
                var c = (s.X1 - ax) * ux + (s.Y1 - ay) * uy;
                lo = Math.Min(lo, Math.Min(a, c));
                hi = Math.Max(hi, Math.Max(a, c));
            }

            if (hi - lo < MinGridMm)
                continue;
            var line = new GridLine
            {
                Name = b.Name,
                X0 = ax + ux * lo,
                Y0 = ay + uy * lo,
                X1 = ax + ux * hi,
                Y1 = ay + uy * hi
            };
            // A grid with a bubble at both ends is found twice.
            if (grids.Any(g => SameAxis(g.Line, g.Ux, g.Uy, line)))
                continue;
            grids.Add((line, ux, uy));
        }

        return EvenSpacing(grids, log);
    }

    /// <summary>
    /// Grids of one direction share that direction exactly and sit on whole 5 mm steps from the first one, so
    /// grid-to-grid dimensions read 8000, not 7999.98.
    /// </summary>
    private static List<GridLine> EvenSpacing(List<(GridLine Line, double Ux, double Uy)> grids, Action<string>? log)
    {
        var result = new List<GridLine>();
        var families = new List<List<(GridLine Line, double Ux, double Uy)>>();
        foreach (var g in grids)
        {
            var family = families.FirstOrDefault(f =>
                Math.Abs(f[0].Ux * g.Uy - f[0].Uy * g.Ux) < Math.Sin(ParallelDeg * Math.PI / 180));
            if (family is null)
                families.Add(new List<(GridLine, double, double)> { g });
            else
                family.Add(g);
        }

        foreach (var family in families)
        {
            // Direction of the longest grid, the others turned onto it (same sense).
            var main = family.OrderByDescending(g => Length(g.Line)).First();
            var ux = main.Ux;
            var uy = main.Uy;
            if (ux < -1e-9 || (Math.Abs(ux) < 1e-9 && uy < 0))
            {
                ux = -ux;
                uy = -uy;
            }

            // Offsets grow to +X / +Y: the first grid is the left or bottom one.
            var nx = -uy;
            var ny = ux;
            if (nx < -1e-9 || (Math.Abs(nx) < 1e-9 && ny < 0))
            {
                nx = -nx;
                ny = -ny;
            }
            var placed = family
                .Select(g => (g.Line, Off: ((g.Line.X0 + g.Line.X1) / 2) * nx + ((g.Line.Y0 + g.Line.Y1) / 2) * ny))
                .OrderBy(g => g.Off)
                .ToList();
            var first = placed[0].Off;
            foreach (var (line, off) in placed)
            {
                var even = first + UnitRound5(off - first);
                var a0 = line.X0 * ux + line.Y0 * uy;
                var a1 = line.X1 * ux + line.Y1 * uy;
                var (s0, s1) = (Math.Min(a0, a1), Math.Max(a0, a1));
                if (Math.Abs(even - off) > 0.001)
                    log?.Invoke($"grid {line.Name}: spacing {off - first:0.###} → {even - first:0}");
                result.Add(new GridLine
                {
                    Name = line.Name,
                    X0 = ux * s0 + nx * even,
                    Y0 = uy * s0 + ny * even,
                    X1 = ux * s1 + nx * even,
                    Y1 = uy * s1 + ny * even
                });
            }
        }

        return result;
    }

    private static double UnitRound5(double mm) => Math.Round(mm / 5.0, MidpointRounding.AwayFromZero) * 5.0;

    /// <summary>The segment runs on a line through the bubble center and one of its ends reaches the rim.</summary>
    private static bool TouchesBubble(Seg s, double cx, double cy, double r, out (double Ux, double Uy) dir)
    {
        dir = default;
        var len = s.Len;
        if (len < 200)
            return false;
        var ux = (s.X1 - s.X0) / len;
        var uy = (s.Y1 - s.Y0) / len;
        var perp = Math.Abs((cx - s.X0) * -uy + (cy - s.Y0) * ux);
        if (perp > r * 0.25)
            return false;
        var d0 = Math.Sqrt(Sq(s.X0 - cx) + Sq(s.Y0 - cy));
        var d1 = Math.Sqrt(Sq(s.X1 - cx) + Sq(s.Y1 - cy));
        var near = Math.Min(d0, d1);
        if (near > r + RimReachMm || near < r * 0.5)
            return false;
        // Pointing away from the bubble: from the near end toward the far end.
        dir = d0 <= d1 ? (ux, uy) : (-ux, -uy);
        return true;
    }

    private static bool OnLine(Seg s, double px, double py, double ux, double uy)
    {
        var len = s.Len;
        var sx = (s.X1 - s.X0) / len;
        var sy = (s.Y1 - s.Y0) / len;
        if (Math.Abs(sx * uy - sy * ux) > Math.Sin(ParallelDeg * Math.PI / 180))
            return false;
        return Math.Abs((s.X0 - px) * -uy + (s.Y0 - py) * ux) <= SameLineMm &&
               Math.Abs((s.X1 - px) * -uy + (s.Y1 - py) * ux) <= SameLineMm;
    }

    private static bool SameAxis(GridLine a, double ux, double uy, GridLine b)
    {
        var off0 = (b.X0 - a.X0) * -uy + (b.Y0 - a.Y0) * ux;
        var off1 = (b.X1 - a.X0) * -uy + (b.Y1 - a.Y0) * ux;
        return Math.Abs(off0) <= SameLineMm && Math.Abs(off1) <= SameLineMm;
    }

    private static double Length(GridLine g) => Math.Sqrt(Sq(g.X1 - g.X0) + Sq(g.Y1 - g.Y0));

    private static double Sq(double v) => v * v;
}
