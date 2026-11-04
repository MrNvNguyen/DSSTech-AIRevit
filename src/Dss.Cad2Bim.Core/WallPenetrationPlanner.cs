using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

/// <summary>A straight wall in model mm: plan axis, thickness, bottom and top elevations.</summary>
public sealed class WallBand
{
    public string Id { get; set; } = "";
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double ThicknessMm { get; set; }
    public double BottomMm { get; set; }
    public double TopMm { get; set; }
}

/// <summary>A straight duct: centerline ends (model mm) and section (round: width = height = diameter).</summary>
public sealed class DuctRun
{
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double Z0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double Z1 { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>An opening to cut: along the wall from its start, and bottom/top elevations (model mm).</summary>
public sealed class WallHole
{
    public string WallId { get; set; } = "";
    public double FromMm { get; set; }
    public double ToMm { get; set; }
    public double BottomMm { get; set; }
    public double TopMm { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>
/// Openings for ducts crossing walls and for fire-hose cabinets recessed in walls: the duct's section (widened by
/// the skew through the wall's thickness) or the cabinet's extent, plus clearance on every side; sizes rounded up
/// to 10 mm so the opening reads in whole numbers.
/// </summary>
public static class WallPenetrationPlanner
{
    /// <summary>Ducts steeper than this are risers: they go through slabs, not walls.</summary>
    private const double MaxSlope = 0.3;
    /// <summary>A crossing closer than this to the wall angle is a duct running along the wall, not through it.</summary>
    private const double MinSin = 0.2;

    public static List<WallHole> Ducts(IReadOnlyList<WallBand> walls, IReadOnlyList<DuctRun> ducts, double clearanceMm)
    {
        var holes = new List<WallHole>();
        foreach (var d in ducts)
        {
            var dx = d.X1 - d.X0;
            var dy = d.Y1 - d.Y0;
            var plan = Math.Sqrt(dx * dx + dy * dy);
            if (plan < 1 || Math.Abs(d.Z1 - d.Z0) / plan > MaxSlope)
                continue;
            foreach (var w in walls)
            {
                var (ux, uy, len) = Dir(w);
                if (len < 1)
                    continue;
                var sin = Math.Abs(ux * dy - uy * dx) / plan;
                if (sin < MinSin)
                    continue;
                // Duct centerline ∩ wall axis, both as segments.
                if (!Cross(d.X0, d.Y0, d.X1, d.Y1, w.X0, w.Y0, w.X1, w.Y1, out var td, out var tw))
                    continue;
                var z = d.Z0 + (d.Z1 - d.Z0) * td;
                if (z < w.BottomMm || z > w.TopMm)
                    continue;
                var cos = Math.Sqrt(Math.Max(0, 1 - sin * sin));
                var along = d.WidthMm / sin + w.ThicknessMm * cos / sin;
                var s = tw * len;
                holes.Add(Hole(w, s - along / 2, s + along / 2, z - d.HeightMm / 2, z + d.HeightMm / 2, clearanceMm, d.Source));
            }
        }

        return holes;
    }

    /// <summary>A cabinet (its corner points) recessed in a wall: the part of its extent inside the wall's band.</summary>
    public static List<WallHole> Cabinets(
        IReadOnlyList<WallBand> walls, IReadOnlyList<(string Source, IReadOnlyList<(double X, double Y, double Z)> Points)> cabinets,
        double clearanceMm)
    {
        var holes = new List<WallHole>();
        foreach (var (source, points) in cabinets)
        {
            if (points.Count == 0)
                continue;
            foreach (var w in walls)
            {
                var (ux, uy, len) = Dir(w);
                if (len < 1)
                    continue;
                var along = points.Select(p => (p.X - w.X0) * ux + (p.Y - w.Y0) * uy).ToList();
                var off = points.Select(p => (p.X - w.X0) * -uy + (p.Y - w.Y0) * ux).ToList();
                var half = w.ThicknessMm / 2;
                // Recessed: the cabinet reaches into the wall's thickness, over the wall's length.
                if (off.Max() <= -half + 1 || off.Min() >= half - 1 || along.Max() <= 0 || along.Min() >= len)
                    continue;
                var bottom = points.Min(p => p.Z);
                var top = points.Max(p => p.Z);
                if (top <= w.BottomMm || bottom >= w.TopMm)
                    continue;
                holes.Add(Hole(w, Math.Max(0, along.Min()), Math.Min(len, along.Max()), bottom, top, clearanceMm, source));
            }
        }

        return holes;
    }

    private static WallHole Hole(WallBand w, double from, double to, double bottom, double top, double clearance, string source)
    {
        var mid = (from + to) / 2;
        var zmid = (bottom + top) / 2;
        var width = Math.Ceiling((to - from + 2 * clearance) / 10) * 10;
        var height = Math.Ceiling((top - bottom + 2 * clearance) / 10) * 10;
        return new WallHole
        {
            WallId = w.Id,
            FromMm = mid - width / 2,
            ToMm = mid + width / 2,
            BottomMm = Math.Max(w.BottomMm, zmid - height / 2),
            TopMm = Math.Min(w.TopMm, zmid + height / 2),
            Source = source
        };
    }

    private static bool Cross(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy, out double t, out double u)
    {
        t = u = 0;
        var rx = bx - ax;
        var ry = by - ay;
        var sx = dx - cx;
        var sy = dy - cy;
        var den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-9)
            return false;
        t = ((cx - ax) * sy - (cy - ay) * sx) / den;
        u = ((cx - ax) * ry - (cy - ay) * rx) / den;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }

    private static (double Ux, double Uy, double Len) Dir(WallBand w)
    {
        var dx = w.X1 - w.X0;
        var dy = w.Y1 - w.Y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-9 ? (1, 0, 0) : (dx / len, dy / len, len);
    }
}
