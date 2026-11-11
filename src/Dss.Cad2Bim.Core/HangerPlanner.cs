using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Hangers along one straight sprinkler pipe (mm from its start), steel pipe: no further apart than the spacing for
/// its size (3.6 m to DN32, 4.5 m from DN40 — NFPA 13 table, check against TCVN 7336 Đ5.5.21 / the project's
/// specification), half of that at most from an end joined on (the next pipe's hanger the other half); at a free end
/// (an open end, or the last head's drop) one within 0.9 m (to DN25) / 1.2 m (larger) of it (TCVN 7336 Đ5.5.22), and
/// none on the fittings (clear of each end).
/// </summary>
public static class HangerPlanner
{
    public const double ClearMm = 150;

    /// <summary>A hanger this far in from a free end.</summary>
    public const double FreeEndMm = 300;

    /// <summary>A pipe between fittings shorter than this carries none (the pipes either side do).</summary>
    public const double ShortMm = 600;

    public static double SpacingMm(double dnMm, double smallMm = 3600, double largeMm = 4500) => dnMm <= 32 ? smallMm : largeMm;

    public static double EndMm(double dnMm) => dnMm <= 25 ? 900 : 1200;

    public static List<double> Plan(double lengthMm, double dnMm, bool freeA, bool freeB, double smallMm = 3600, double largeMm = 4500)
    {
        var at = new List<double>();
        if (lengthMm < 2 * ClearMm || (lengthMm < ShortMm && !freeA && !freeB))
            return at;
        var spacing = SpacingMm(dnMm, smallMm, largeMm);
        var n = Math.Max(1, (int)Math.Ceiling(lengthMm / spacing - 1e-9));
        for (var i = 0; i < n; i++)
            at.Add((i + 0.5) * lengthMm / n);
        var end = EndMm(dnMm);
        var free = Math.Min(FreeEndMm, lengthMm / 2);
        if (freeA && at[0] > end)
            at.Insert(0, free);
        if (freeB && lengthMm - at[at.Count - 1] > end)
            at.Add(lengthMm - free);
        return at.Select(s => Math.Max(ClearMm, Math.Min(lengthMm - ClearMm, s))).Distinct().ToList();
    }
}
