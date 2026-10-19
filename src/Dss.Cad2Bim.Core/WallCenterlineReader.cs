using System;
using System.Collections.Generic;

namespace Dss.Cad2Bim.Core;

public readonly struct WallRun
{
    public WallRun(double x0, double y0, double x1, double y1, double thicknessMm)
    {
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        ThicknessMm = thicknessMm;
    }

    public double X0 { get; }
    public double Y0 { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public double ThicknessMm { get; }
}

/// <summary>
/// Wall centerline and thickness from a pair of parallel CAD faces.
/// Linked dimension text is not required.
/// </summary>
public static class WallCenterlineReader
{
    public const double MinThicknessMm = 80;
    public const double MaxThicknessMm = 1200;
    public const double MinOverlapMm = 250;

    public static List<WallRun> FromEntities(IEnumerable<CadEntity> entities)
    {
        var faces = new List<Seg>();
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case CadLine line:
                    AddSeg(faces, line.X0, line.Y0, line.X1, line.Y1);
                    break;
                case CadPolyline poly:
                    AddPoly(faces, poly);
                    break;
            }
        }

        faces = MergeCollinear(faces);
        var pairs = new List<Pair>();
        for (var i = 0; i < faces.Count; i++)
        {
            for (var j = i + 1; j < faces.Count; j++)
            {
                if (TryPair(faces[i], faces[j], out var x0, out var y0, out var x1, out var y1, out var thick, out var overlap))
                    pairs.Add(new Pair(i, j, x0, y0, x1, y1, thick, overlap));
            }
        }

        pairs.Sort(static (a, b) =>
        {
            var thick = a.Thickness.CompareTo(b.Thickness);
            return thick != 0 ? thick : b.Overlap.CompareTo(a.Overlap);
        });

        var used = new bool[faces.Count];
        var runs = new List<WallRun>();
        foreach (var pair in pairs)
        {
            if (used[pair.I] || used[pair.J])
                continue;
            used[pair.I] = used[pair.J] = true;
            runs.Add(new WallRun(pair.X0, pair.Y0, pair.X1, pair.Y1, Round10(pair.Thickness)));
        }

        return MergeRuns(runs);
    }

    private static void AddPoly(List<Seg> faces, CadPolyline poly)
    {
        var pts = poly.Points;
        if (pts.Count < 2)
            return;
        var count = poly.Closed ? pts.Count : pts.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            AddSeg(faces, a.X, a.Y, b.X, b.Y);
        }
    }

    private static void AddSeg(List<Seg> faces, double x0, double y0, double x1, double y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < MinOverlapMm)
            return;
        faces.Add(new Seg(x0, y0, x1, y1, dx / len, dy / len, len));
    }

    private static List<Seg> MergeCollinear(List<Seg> faces)
    {
        var open = new List<Seg>(faces);
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < open.Count && !changed; i++)
            {
                for (var j = i + 1; j < open.Count; j++)
                {
                    if (!TryMerge(open[i], open[j], out var merged))
                        continue;
                    open[i] = merged;
                    open.RemoveAt(j);
                    changed = true;
                    break;
                }
            }
        }

        return open;
    }

    private static bool TryMerge(Seg a, Seg b, out Seg merged)
    {
        merged = default;
        if (Math.Abs(a.Ux * b.Ux + a.Uy * b.Uy) < 0.999)
            return false;
        var ox = b.X0 - a.X0;
        var oy = b.Y0 - a.Y0;
        if (Math.Abs(ox * -a.Uy + oy * a.Ux) > 8)
            return false;

        var a0 = 0.0;
        var a1 = a.Len;
        var b0 = ox * a.Ux + oy * a.Uy;
        var b1 = b0 + ((b.X1 - b.X0) * a.Ux + (b.Y1 - b.Y0) * a.Uy);
        var lo = Math.Min(Math.Min(a0, a1), Math.Min(b0, b1));
        var hi = Math.Max(Math.Max(a0, a1), Math.Max(b0, b1));
        var gap = hi - lo - a.Len - Math.Abs(b1 - b0);
        if (gap > 40)
            return false;

        var x0 = a.X0 + a.Ux * lo;
        var y0 = a.Y0 + a.Uy * lo;
        var len = hi - lo;
        merged = new Seg(x0, y0, x0 + a.Ux * len, y0 + a.Uy * len, a.Ux, a.Uy, len);
        return true;
    }

    private static bool TryPair(
        Seg a, Seg b,
        out double cx0, out double cy0, out double cx1, out double cy1,
        out double thick, out double overlap)
    {
        cx0 = cy0 = cx1 = cy1 = thick = overlap = 0;
        var dot = Math.Abs(a.Ux * b.Ux + a.Uy * b.Uy);
        if (dot < 0.9986)
            return false;

        var ox = b.X0 - a.X0;
        var oy = b.Y0 - a.Y0;
        var nx = -a.Uy;
        var ny = a.Ux;
        thick = ox * nx + oy * ny;
        if (thick < 0)
        {
            thick = -thick;
            nx = -nx;
            ny = -ny;
        }

        if (thick < MinThicknessMm || thick > MaxThicknessMm)
            return false;

        var b0 = ox * a.Ux + oy * a.Uy;
        var b1 = b0 + ((b.X1 - b.X0) * a.Ux + (b.Y1 - b.Y0) * a.Uy);
        var lo = Math.Max(0, Math.Min(b0, b1));
        var hi = Math.Min(a.Len, Math.Max(b0, b1));
        overlap = hi - lo;
        var shorter = Math.Min(a.Len, b.Len);
        if (overlap < MinOverlapMm || overlap < shorter * 0.6)
            return false;

        var halfX = nx * thick / 2;
        var halfY = ny * thick / 2;
        cx0 = a.X0 + a.Ux * lo + halfX;
        cy0 = a.Y0 + a.Uy * lo + halfY;
        cx1 = a.X0 + a.Ux * hi + halfX;
        cy1 = a.Y0 + a.Uy * hi + halfY;
        return true;
    }

    private static List<WallRun> MergeRuns(List<WallRun> runs)
    {
        var open = new List<WallRun>(runs);
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < open.Count && !changed; i++)
            {
                for (var j = i + 1; j < open.Count; j++)
                {
                    if (!TryJoin(open[i], open[j], out var joined))
                        continue;
                    open[i] = joined;
                    open.RemoveAt(j);
                    changed = true;
                    break;
                }
            }
        }

        var result = new List<WallRun>();
        foreach (var run in open)
        {
            var x0 = Round10(run.X0);
            var y0 = Round10(run.Y0);
            var x1 = Round10(run.X1);
            var y1 = Round10(run.Y1);
            var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            if (len < MinOverlapMm)
                continue;
            result.Add(new WallRun(x0, y0, x1, y1, run.ThicknessMm));
        }

        return result;
    }

    private static bool TryJoin(WallRun a, WallRun b, out WallRun joined)
    {
        joined = default;
        if (Math.Abs(a.ThicknessMm - b.ThicknessMm) > 0.1)
            return false;
        var adx = a.X1 - a.X0;
        var ady = a.Y1 - a.Y0;
        var alen = Math.Sqrt(adx * adx + ady * ady);
        var bdx = b.X1 - b.X0;
        var bdy = b.Y1 - b.Y0;
        var blen = Math.Sqrt(bdx * bdx + bdy * bdy);
        if (alen < 1 || blen < 1)
            return false;
        var ux = adx / alen;
        var uy = ady / alen;
        if (Math.Abs(ux * bdx / blen + uy * bdy / blen) < 0.999)
            return false;
        if (Math.Abs((b.X0 - a.X0) * -uy + (b.Y0 - a.Y0) * ux) > 15)
            return false;

        var a0 = 0.0;
        var a1 = alen;
        var b0 = (b.X0 - a.X0) * ux + (b.Y0 - a.Y0) * uy;
        var b1 = b0 + ((b.X1 - b.X0) * ux + (b.Y1 - b.Y0) * uy);
        var lo = Math.Min(Math.Min(a0, a1), Math.Min(b0, b1));
        var hi = Math.Max(Math.Max(a0, a1), Math.Max(b0, b1));
        if (hi - lo - alen - Math.Abs(b1 - b0) > 40)
            return false;

        joined = new WallRun(a.X0 + ux * lo, a.Y0 + uy * lo, a.X0 + ux * hi, a.Y0 + uy * hi, a.ThicknessMm);
        return true;
    }

    private static double Round10(double value) =>
        Math.Round(value / 10.0, MidpointRounding.AwayFromZero) * 10.0;

    private readonly struct Seg
    {
        public Seg(double x0, double y0, double x1, double y1, double ux, double uy, double len)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Ux = ux;
            Uy = uy;
            Len = len;
        }

        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public double Ux { get; }
        public double Uy { get; }
        public double Len { get; }
    }

    private readonly struct Pair
    {
        public Pair(int i, int j, double x0, double y0, double x1, double y1, double thickness, double overlap)
        {
            I = i;
            J = j;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Thickness = thickness;
            Overlap = overlap;
        }

        public int I { get; }
        public int J { get; }
        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public double Thickness { get; }
        public double Overlap { get; }
    }
}
