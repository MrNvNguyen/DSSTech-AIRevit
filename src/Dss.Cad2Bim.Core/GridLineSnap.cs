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

    private sealed class Family
    {
        public double AngleDeg;
        public double Sin, Cos;
        public List<(double Offset, double From, double To)> Lines = new();
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
        (Family F, double N)? a = null, b = null;
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
                    a = (along.Family, along.Line!.Value.Offset);
                    b = (square.Family, square.Line!.Value.Offset);
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
                a = (near[i].Family, near[i].Line!.Value.Offset);
                b = (near[j].Family, near[j].Line!.Value.Offset);
            }
        }

        if (a is null || b is null)
            return (x, y, false);
        var (fa, ga) = a.Value;
        var (fb, gb) = b.Value;
        var na = ga + ModelRevitPlanner.Round5Mm(Normal(fa, x, y) - ga);
        var nb = gb + ModelRevitPlanner.Round5Mm(Normal(fb, x, y) - gb);
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
    private static (double Offset, double Distance)? Nearest(Family f, double x, double y)
    {
        var n = Normal(f, x, y);
        var s = x * f.Cos + y * f.Sin;
        var reaching = f.Lines.Where(l => s >= l.From - ReachPastEndMm && s <= l.To + ReachPastEndMm).ToList();
        var pool = reaching.Count > 0 ? reaching : f.Lines;
        if (pool.Count == 0)
            return null;
        var line = pool.OrderBy(l => Math.Abs(n - l.Offset)).First();
        return (line.Offset, n - line.Offset);
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
            var s0 = g.X0 * f.Cos + g.Y0 * f.Sin;
            var s1 = g.X1 * f.Cos + g.Y1 * f.Sin;
            var same = f.Lines.FindIndex(l => Math.Abs(l.Offset - offset) <= SameOffsetMm);
            if (same >= 0)
            {
                var l = f.Lines[same];
                f.Lines[same] = (l.Offset, Math.Min(l.From, Math.Min(s0, s1)), Math.Max(l.To, Math.Max(s0, s1)));
            }
            else
                f.Lines.Add((offset, Math.Min(s0, s1), Math.Max(s0, s1)));
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
