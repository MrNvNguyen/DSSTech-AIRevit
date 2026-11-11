using System;

namespace Dss.Cad2Bim.Core;

/// <summary>How an open branch end joins a main without either one's height changing.</summary>
public enum BranchJoinKind
{
    /// <summary>The branch's section within the main's depth: drawn on to the main and a tap (or a tee) on its side.</summary>
    Side,

    /// <summary>Over or under the main: drawn on to over (under) its axis, an elbow, a riser, a tap (or tee) on top (bottom).</summary>
    Riser,

    /// <summary>
    /// Off the main's centre by less than a riser needs: the branch's last stretch brought to the main's height by an
    /// offset (two 45° elbows) close to the main, then a tap (or tee) on its side; the run keeps its height up to there.
    /// </summary>
    Offset,

    /// <summary>Off the main's centre by less than a riser needs, and too short for an offset: left to join by hand.</summary>
    TooClose,

    /// <summary>A gravity branch lower than its main: it cannot drain into it.</summary>
    Uphill
}

/// <summary>Where and how a branch end joins its main (millimetres).</summary>
public readonly struct BranchJoin
{
    public BranchJoin(BranchJoinKind kind, double x, double y, double branchZ, double mainZ, double alongMain, double reachMm,
        double kneeMm = 0)
    {
        KneeMm = kneeMm;
        Kind = kind;
        X = x;
        Y = y;
        BranchZ = branchZ;
        MainZ = mainZ;
        AlongMain = alongMain;
        ReachMm = reachMm;
    }

    public BranchJoinKind Kind { get; }

    /// <summary>Where the branch's line crosses the main's axis, in plan.</summary>
    public double X { get; }

    public double Y { get; }

    /// <summary>The branch's height there (its own slope carried on), and the main's.</summary>
    public double BranchZ { get; }

    public double MainZ { get; }

    /// <summary>Plan distance along the main from its start to the joint.</summary>
    public double AlongMain { get; }

    /// <summary>Plan distance the branch end moves on its own line to get there (− drawn back).</summary>
    public double ReachMm { get; }

    public double RiseMm => BranchZ - MainZ;

    /// <summary>Offset: plan distance back from the joint (on the main's axis) to the lower elbow, at the main's height;
    /// the upper elbow is <see cref="RiseMm"/> further back (45°).</summary>
    public double KneeMm { get; }
}

/// <summary>
/// An open branch end (after Combine, left at the main's side or moved over / under it) and the main it heads for:
/// the branch drawn on along its own line (its height and slope kept) to the main's axis, then joined on the main's
/// side where it is on the main's centre (a duct's: within its depth), by a riser off the main's top or bottom (an
/// elbow, a riser, a tap or tee) where it is well over or under, else by an offset of two 45° elbows near the main,
/// as built on site. Nothing where the branch does not head across the main near its end, or where the joint
/// would sit at the main's end.
/// </summary>
public static class BranchJoinPlanner
{
    /// <param name="fx">The branch's far end (x, y, z), then its open end.</param>
    /// <param name="ax">The main's ends.</param>
    /// <param name="reachMm">How far past the main's side the branch end may be.</param>
    /// <param name="gravity">A drainage branch: it must come down into the main.</param>
    /// <param name="eccentric">A tap may sit off the main's centre (a duct's flat side); a pipe's must be on it.</param>
    public static BranchJoin? Plan(double fx, double fy, double fz, double ex, double ey, double ez,
        double ax, double ay, double az, double bx, double by, double bz,
        double mainWidthMm, double mainDepthMm, double branchWidthMm, double branchDepthMm,
        double reachMm, bool gravity = false, bool eccentric = true)
    {
        var bLen = Math.Sqrt((ex - fx) * (ex - fx) + (ey - fy) * (ey - fy));
        var mLen = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (bLen < 1 || mLen < 1)
            return null;
        var (ux, uy) = ((ex - fx) / bLen, (ey - fy) / bLen);
        var (mx, my) = ((bx - ax) / mLen, (by - ay) / mLen);
        var sin = ux * my - uy * mx;
        // A branch running along the main is not one coming off it.
        if (Math.Abs(sin) < 0.3)
            return null;
        // e + u·s = a + m·t
        var (wx, wy) = (ax - ex, ay - ey);
        var s = (wx * my - wy * mx) / sin;
        var t = (wx * uy - wy * ux) / sin;
        var side = mainWidthMm / 2 / Math.Abs(sin);
        if (s < -(side + 100) || s > side + reachMm)
            return null;
        // Room for the fitting on the main, and a branch left long enough for its own.
        var margin = branchWidthMm + 100;
        if (t < margin || t > mLen - margin || bLen + s < branchWidthMm + 100)
            return null;
        var branchZ = ez + (ez - fz) / bLen * s;
        var mainZ = az + (bz - az) * t / mLen;
        var rise = branchZ - mainZ;
        var (x, y) = (ex + ux * s, ey + uy * s);
        BranchJoinKind kind;
        var knee = 0.0;
        if (eccentric ? Math.Abs(rise) + branchDepthMm / 2 <= mainDepthMm / 2 + 1 : Math.Abs(rise) <= 2)
            kind = BranchJoinKind.Side;
        else if (gravity && rise < 0)
            kind = BranchJoinKind.Uphill;
        else if (Math.Abs(rise) >= mainDepthMm / 2 + 1.5 * branchDepthMm + 50)
            kind = BranchJoinKind.Riser;
        else
        {
            // Two 45° elbows: the lower one an elbow's room out from the main's side, the upper one the rise further
            // back; the branch must still be long enough beyond them.
            knee = side + 1.5 * branchDepthMm + 50;
            kind = bLen + s - knee - Math.Abs(rise) >= branchWidthMm + 100 ? BranchJoinKind.Offset : BranchJoinKind.TooClose;
        }

        return new BranchJoin(kind, x, y, branchZ, mainZ, t, s, knee);
    }
}
