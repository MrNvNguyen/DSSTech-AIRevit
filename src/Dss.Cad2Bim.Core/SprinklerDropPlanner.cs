using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

/// <summary>Where a sprinkler joins its branch pipe (plan millimetres).</summary>
public readonly struct SprinklerDrop
{
    public SprinklerDrop(double tapX, double tapY, double offsetMm, bool arm, int end)
    {
        TapX = tapX;
        TapY = tapY;
        OffsetMm = offsetMm;
        Arm = arm;
        End = end;
    }

    /// <summary>The point on the pipe's axis the drop (or the arm) comes off.</summary>
    public double TapX { get; }

    public double TapY { get; }

    /// <summary>Plan distance from that point to the sprinkler.</summary>
    public double OffsetMm { get; }

    /// <summary>True: off to the side of the pipe, a level arm out to over the head and an elbow down; false: straight up.</summary>
    public bool Arm { get; }

    /// <summary>0: off the pipe's middle (a tap or a tee); 1 / 2: at its open end A / B, drawn on (or back) to the tap point, an elbow.</summary>
    public int End { get; }
}

/// <summary>
/// A sprinkler head under a branch pipe: a drop straight up into the pipe when the head is under it, else an arm out
/// of the pipe square to it to over the head. Off the middle, the point keeps clear of the pipe's ends (room for the
/// fitting); a head at or past an open end (the last head of a branch) takes the pipe drawn to it and an elbow.
/// </summary>
public static class SprinklerDropPlanner
{
    /// <param name="straightMm">Within this of the pipe's axis in plan the drop goes straight up into it.</param>
    public static SprinklerDrop? Plan(double ax, double ay, double bx, double by, double x, double y,
        double reachMm, bool openA = false, bool openB = false, double marginMm = 100, double straightMm = 25)
    {
        var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (len < 1)
            return null;
        var (ux, uy) = ((bx - ax) / len, (by - ay) / len);
        var along = (x - ax) * ux + (y - ay) * uy;
        var end = openB && along > len - marginMm && along <= len + reachMm ? 2
            : openA && along < marginMm && along >= -reachMm ? 1
            : 0;
        if (end == 0 && len < 2 * marginMm)
            return null;
        var t = end != 0 ? along : Math.Max(marginMm, Math.Min(len - marginMm, along));
        // An end drawn back must leave the pipe its fitting room.
        if ((end == 2 && t < marginMm) || (end == 1 && t > len - marginMm))
            return null;
        var (px, py) = (ax + ux * t, ay + uy * t);
        var offset = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
        if (offset > reachMm)
            return null;
        return new SprinklerDrop(px, py, offset, offset > straightMm, end);
    }

    /// <summary>
    /// A head a little off its branch (no further than <paramref name="alignMm"/>) is moved onto the branch's axis, so
    /// the heads of a branch stand in line with it and drop straight from it (a short arm over is where joints fail).
    /// </summary>
    public static bool Aligns(SprinklerDrop drop, double alignMm, double straightMm = 0.5) =>
        drop.OffsetMm > straightMm && drop.OffsetMm <= alignMm;

    /// <summary>
    /// An upright head (its inlet facing down) stands on its branch: its inlet this far over the branch's axis, the
    /// tee's outlet and a short nipple above the pipe's top, and no less than a rigid drop needs (minMm).
    /// </summary>
    public static double UprightRiseMm(double outerMm, double minMm = 100, double nippleMm = 75) =>
        Math.Max(minMm, outerMm / 2 + nippleMm);
}

/// <summary>
/// The flexible hose from a branch to a sprinkler head: a stock length (DN20, 1.5 m at least, 2 m or 2.5 m further
/// off), and the slack of a hose longer than the run laid out as a bow to the side.
/// </summary>
public static class SprinklerHose
{
    public const double DiameterMm = 20;

    /// <summary>The stock lengths (listed hoses run to 6 ft, 1.8 m).</summary>
    public static readonly double[] LengthsMm = { 700, 1000, 1200, 1500, 1800 };

    /// <summary>The shortest of the lengths used that covers the run; null past the longest.</summary>
    public static double? Pick(double runMm, IEnumerable<double> lengthsMm)
    {
        double? best = null;
        foreach (var l in lengthsMm)
        {
            if (runMm <= l && (best is null || l < best))
                best = l;
        }

        return best;
    }

    /// <summary>
    /// The few lengths a site keeps (at most maxTypes of the stock): the longest run still covered, then the hose
    /// wasted (length less run) kept down; a further length only where it cuts the waste by a fifth or more. Runs past
    /// the longest stock length are left out (no hose reaches them).
    /// </summary>
    public static double[] PickSet(IEnumerable<double> runsMm, int maxTypes = 3)
    {
        var runs = runsMm.Where(r => r <= LengthsMm[LengthsMm.Length - 1]).ToList();
        if (runs.Count == 0)
            return new[] { 1000.0 };
        double[]? chosen = null;
        var chosenWaste = double.MaxValue;
        for (var k = 1; k <= Math.Min(maxTypes, LengthsMm.Length); k++)
        {
            double[]? bestK = null;
            var wasteK = double.MaxValue;
            foreach (var set in Subsets(LengthsMm, k))
            {
                var waste = 0.0;
                foreach (var r in runs)
                {
                    if (Pick(r, set) is not double l)
                    {
                        waste = double.MaxValue;
                        break;
                    }

                    waste += l - r;
                }

                if (waste < wasteK)
                    (bestK, wasteK) = (set, waste);
            }

            if (bestK is null)
                continue;
            if (chosen is null || wasteK <= 0.8 * chosenWaste)
                (chosen, chosenWaste) = (bestK, wasteK);
        }

        return chosen ?? new[] { LengthsMm[LengthsMm.Length - 1] };
    }

    private static IEnumerable<double[]> Subsets(double[] items, int k, int from = 0)
    {
        if (k == 0)
        {
            yield return new double[0];
            yield break;
        }

        for (var i = from; i <= items.Length - k; i++)
        {
            foreach (var rest in Subsets(items, k - 1, i + 1))
                yield return new[] { items[i] }.Concat(rest).ToArray();
        }
    }

    /// <summary>
    /// The middle of a bow from p to q whose two legs together are spanMm long: off the straight line, level and
    /// square to it in plan (along (sideX, sideY) where p is over q). Null where the span is no longer than the line.
    /// </summary>
    public static (double X, double Y, double Z)? Bow(double px, double py, double pz, double qx, double qy, double qz,
        double spanMm, double sideX, double sideY)
    {
        var (dx, dy, dz) = (qx - px, qy - py, qz - pz);
        var d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (spanMm <= d + 1)
            return null;
        var plan = Math.Sqrt(dx * dx + dy * dy);
        double nx, ny;
        if (plan > 1)
            (nx, ny) = (-dy / plan, dx / plan);
        else
        {
            var s = Math.Sqrt(sideX * sideX + sideY * sideY);
            (nx, ny) = s > 1e-9 ? (sideX / s, sideY / s) : (1.0, 0.0);
        }

        var h = Math.Sqrt(spanMm * spanMm / 4 - d * d / 4);
        return ((px + qx) / 2 + nx * h, (py + qy) / 2 + ny * h, (pz + qz) / 2);
    }
}
