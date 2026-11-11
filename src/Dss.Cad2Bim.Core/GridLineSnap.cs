using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

/// <summary>A grid line of the model, in plan millimetres.</summary>
public readonly struct GridSegment
{
    public GridSegment(double x0, double y0, double x1, double y1)
    {
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
    }

    public double X0 { get; }
    public double Y0 { get; }
    public double X1 { get; }
    public double Y1 { get; }
}

/// <summary>
/// A point element (pile) placed so that its distances to the two grids that locate it are whole 5 mm, measured
/// square to those grids — the way it is dimensioned. The two grids are the nearest lines of two grid directions:
/// the directions of the element's frame (the pile cap it stands in) when known, else the pair nearest the point.
/// </summary>
public static class GridLineSnap
{
    private const double SameAngleDeg = 0.05;
    private const double SameOffsetMm = 0.5;
    private const double MinCrossDeg = 30;
    /// <summary>A grid counts for a point within this far past its drawn ends.</summary>
    private const double ReachPastEndMm = 3000;
    private const double FrameMatchDeg = 10;

    /// <summary>A grid line's own direction and its offset along that direction's normal.</summary>
    private readonly struct Line
    {
        public Line(double sin, double cos, double own)
        {
            Sin = sin;
            Cos = cos;
            Own = own;
        }

        public double Sin { get; }
        public double Cos { get; }
        public double Own { get; }
    }

    private sealed class Family
    {
        public double AngleDeg;
        public double Sin, Cos;
        /// <summary>Each line with its own direction: grids within <see cref="SameAngleDeg"/> are one family, but a
        /// pile is measured square to its own grid (0.0001° off the family over 15 m is 0.03 mm on the dimension).</summary>
        public List<(double Offset, double From, double To, double Sin, double Cos, double Own)> Lines = new();
    }

    /// <param name="refX">Where the locating grids are looked up (a pile: its cap's middle, so every pile of the cap
    /// and the cap itself measure from the same grids even where the grid spacing is not whole 5 mm).</param>
    public static (double X, double Y, bool Snapped) Snap(double x, double y, IReadOnlyList<GridSegment>? grids, double? frameDeg = null,
        double? refX = null, double? refY = null)
    {
        var families = Families(grids);
        if (families.Count < 2)
            return (x, y, false);

        var near = families.Select(f => (Family: f, Line: Nearest(f, refX ?? x, refY ?? y))).Where(t => t.Line is not null).ToList();
        (Family F, Line L)? a = null, b = null;
        if (frameDeg is double frame)
        {
            // The frame's own directions first: the grid family along the frame, then the one most square to it.
            var along = near.Where(t => Delta(t.Family.AngleDeg, frame) <= FrameMatchDeg || Delta(t.Family.AngleDeg, frame + 90) <= FrameMatchDeg)
                .OrderBy(t => Math.Abs(t.Line!.Value.Distance)).FirstOrDefault();
            if (along.Family is not null)
            {
                var square = near.Where(t => Delta(t.Family.AngleDeg, along.Family.AngleDeg) >= MinCrossDeg)
                    .OrderByDescending(t => Delta(t.Family.AngleDeg, along.Family.AngleDeg))
                    .ThenBy(t => Math.Abs(t.Line!.Value.Distance)).FirstOrDefault();
                if (square.Family is not null)
                {
                    a = (along.Family, along.Line!.Value.Line);
                    b = (square.Family, square.Line!.Value.Line);
                }
            }
        }

        if (a is null)
        {
            var best = double.MaxValue;
            for (var i = 0; i < near.Count; i++)
            for (var j = i + 1; j < near.Count; j++)
            {
                if (Delta(near[i].Family.AngleDeg, near[j].Family.AngleDeg) < MinCrossDeg)
                    continue;
                var score = Math.Abs(near[i].Line!.Value.Distance) + Math.Abs(near[j].Line!.Value.Distance);
                if (score >= best)
                    continue;
                best = score;
                a = (near[i].Family, near[i].Line!.Value.Line);
                b = (near[j].Family, near[j].Line!.Value.Line);
            }
        }

        if (a is null || b is null)
            return (x, y, false);
        // Square to each locating grid's own line, not the family's angle (taken from its first grid).
        var fa = a.Value.L;
        var fb = b.Value.L;
        var na = fa.Own + ModelRevitPlanner.Round5Mm(-x * fa.Sin + y * fa.Cos - fa.Own);
        var nb = fb.Own + ModelRevitPlanner.Round5Mm(-x * fb.Sin + y * fb.Cos - fb.Own);
        // -x·sinA + y·cosA = na ; -x·sinB + y·cosB = nb
        var det = -fa.Sin * fb.Cos + fa.Cos * fb.Sin;
        if (Math.Abs(det) < 1e-9)
            return (x, y, false);
        var sx = (na * fb.Cos - fa.Cos * nb) / det;
        var sy = (-fa.Sin * nb + fb.Sin * na) / det;
        return (sx, sy, true);
    }

    /// <summary>
    /// An outline direction turned onto the exact direction of a grid (or square to it) when within
    /// <paramref name="tolDeg"/>: a CAD angle of 0.0003° is the grid's 0°, so every edge dimension reads the same.
    /// </summary>
    public static double AlignAngle(double deg, IReadOnlyList<GridSegment>? grids, double tolDeg = 1.0)
    {
        var best = deg;
        var bestDelta = tolDeg;
        foreach (var f in Families(grids))
        {
            for (var k = -2; k <= 2; k++)
            {
                var candidate = f.AngleDeg + 90 * k;
                var delta = Math.Abs(Fold180(deg - candidate));
                if (delta >= bestDelta)
                    continue;
                bestDelta = delta;
                best = deg - Fold180(deg - candidate);
            }
        }

        return best;
    }

    private static double Fold180(double deg)
    {
        while (deg <= -180)
            deg += 360;
        while (deg > 180)
            deg -= 360;
        return deg;
    }

    private static double Normal(Family f, double x, double y) => -x * f.Sin + y * f.Cos;

    /// <summary>Nearest line of the family that runs past the point; any line of it when none does.</summary>
    private static (double Offset, double Distance, Line Line)? Nearest(Family f, double x, double y)
    {
        var n = Normal(f, x, y);
        var s = x * f.Cos + y * f.Sin;
        var reaching = f.Lines.Where(l => s >= l.From - ReachPastEndMm && s <= l.To + ReachPastEndMm).ToList();
        var pool = reaching.Count > 0 ? reaching : f.Lines;
        if (pool.Count == 0)
            return null;
        var line = pool.OrderBy(l => Math.Abs(n - l.Offset)).First();
        return (line.Offset, n - line.Offset, new Line(line.Sin, line.Cos, line.Own));
    }

    private static List<Family> Families(IReadOnlyList<GridSegment>? grids)
    {
        var families = new List<Family>();
        foreach (var g in grids ?? Array.Empty<GridSegment>())
        {
            var len = Math.Sqrt((g.X1 - g.X0) * (g.X1 - g.X0) + (g.Y1 - g.Y0) * (g.Y1 - g.Y0));
            if (len < 1)
                continue;
            var deg = Math.Atan2(g.Y1 - g.Y0, g.X1 - g.X0) * 180 / Math.PI;
            var f = families.FirstOrDefault(x => Delta(x.AngleDeg, deg) <= SameAngleDeg);
            if (f is null)
            {
                deg = Fold(deg);
                var rad = deg * Math.PI / 180;
                f = new Family { AngleDeg = deg, Sin = Math.Sin(rad), Cos = Math.Cos(rad) };
                families.Add(f);
            }

            var offset = -g.X0 * f.Sin + g.Y0 * f.Cos;
            // The line's own direction, turned the family's way round.
            var (ls, lc) = ((g.Y1 - g.Y0) / len, (g.X1 - g.X0) / len);
            if (ls * f.Sin + lc * f.Cos < 0)
                (ls, lc) = (-ls, -lc);
            var own = -g.X0 * ls + g.Y0 * lc;
            var s0 = g.X0 * f.Cos + g.Y0 * f.Sin;
            var s1 = g.X1 * f.Cos + g.Y1 * f.Sin;
            var same = f.Lines.FindIndex(l => Math.Abs(l.Offset - offset) <= SameOffsetMm);
            if (same >= 0)
            {
                var l = f.Lines[same];
                f.Lines[same] = (l.Offset, Math.Min(l.From, Math.Min(s0, s1)), Math.Max(l.To, Math.Max(s0, s1)), l.Sin, l.Cos, l.Own);
            }
            else
                f.Lines.Add((offset, Math.Min(s0, s1), Math.Max(s0, s1), ls, lc, own));
        }

        return families;
    }

    private static double Fold(double deg)
    {
        while (deg <= -90)
            deg += 180;
        while (deg > 90)
            deg -= 180;
        return deg;
    }

    private static double Delta(double a, double b)
    {
        var d = Math.Abs(a - b) % 180;
        return d > 90 ? 180 - d : d;
    }
}
