using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Lines read from the DWG file against the lines Revit shows for the link. A DWG drawn far from its origin (tens of
/// km) is moved near the origin by Revit when linked, and the link's transform does not carry that move: placed by
/// the transform alone, everything read from the file lands kilometres away from what is on screen.
/// </summary>
public static class CadAlign
{
    /// <summary>
    /// The shift (mm) that puts the read lines on the shown ones: the offset most pairs agree on, a pair being two
    /// long lines of the same length and direction. Null when fewer than <paramref name="minVotes"/> agree (nothing to
    /// match), or when the lines already sit where they are shown.
    /// </summary>
    public static (double Dx, double Dy)? Offset(IEnumerable<CadLine> shown, IEnumerable<CadLine> read, int minVotes = 3,
        double minLengthMm = 500, int sample = 300)
    {
        static double Len(CadLine l) => Math.Sqrt((l.X1 - l.X0) * (l.X1 - l.X0) + (l.Y1 - l.Y0) * (l.Y1 - l.Y0));
        var a = shown.Where(l => Len(l) >= minLengthMm).OrderByDescending(Len).Take(sample).ToList();
        var b = read.Where(l => Len(l) >= minLengthMm).OrderByDescending(Len).Take(sample * 3).ToList();
        if (a.Count == 0 || b.Count == 0)
            return null;

        // Lines of the file by length (to 1 mm), for the shown ones to find their twin.
        var byLength = b.GroupBy(l => Math.Round(Len(l))).ToDictionary(g => g.Key, g => g.ToList());
        var votes = new Dictionary<(long, long), int>();
        foreach (var s in a)
        {
            var key = Math.Round(Len(s));
            foreach (var k in new[] { key - 1, key, key + 1 })
            {
                if (!byLength.TryGetValue(k, out var twins))
                    continue;
                foreach (var t in twins)
                {
                    // Same way round or reversed.
                    foreach (var (tx, ty, ux, uy) in new[] { (t.X0, t.Y0, t.X1, t.Y1), (t.X1, t.Y1, t.X0, t.Y0) })
                    {
                        if (Math.Abs(s.X1 - s.X0 - (ux - tx)) > 1 || Math.Abs(s.Y1 - s.Y0 - (uy - ty)) > 1)
                            continue;
                        var cell = ((long)Math.Round(s.X0 - tx), (long)Math.Round(s.Y0 - ty));
                        votes[cell] = votes.TryGetValue(cell, out var n) ? n + 1 : 1;
                    }
                }
            }
        }

        if (votes.Count == 0)
            return null;
        var best = votes.OrderByDescending(v => v.Value).First();
        if (best.Value < minVotes)
            return null;
        // The neighbouring cells (rounding) refine the shift.
        var near = votes.Where(v => Math.Abs(v.Key.Item1 - best.Key.Item1) <= 1 && Math.Abs(v.Key.Item2 - best.Key.Item2) <= 1).ToList();
        var dx = near.Sum(v => v.Key.Item1 * (double)v.Value) / near.Sum(v => v.Value);
        var dy = near.Sum(v => v.Key.Item2 * (double)v.Value) / near.Sum(v => v.Value);
        return Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 ? null : (dx, dy);
    }
}
