using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

/// <summary>A wall already in the model, as its plan axis (model mm). Doors and windows are hosted on these.</summary>
public sealed class HostWallAxis
{
    public HostWallAxis(string id, double x0, double y0, double x1, double y1, double thicknessMm)
    {
        Id = id;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        ThicknessMm = thicknessMm;
    }

    public string Id { get; }
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double ThicknessMm { get; }

    public HostWallAxis Copy() => new(Id, X0, Y0, X1, Y1, ThicknessMm);
}

/// <summary>One door or window opening found on the plan, located on its host wall's axis.</summary>
public sealed class WallOpening
{
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Unit direction of the host wall.</summary>
    public double DirX { get; set; }
    public double DirY { get; set; }
    /// <summary>Opening length along the wall, read from the drawn door/window (frame to frame).</summary>
    public double WidthMm { get; set; }
    public string HostId { get; set; } = "";
    /// <summary>
    /// The wall is broken at the opening on the plan, so Revit has nothing to cut: this wall's end that faces the
    /// opening runs on to <see cref="BridgeX"/>/<see cref="BridgeY"/> and hosts the door.
    /// </summary>
    public bool Bridge { get; set; }
    public double BridgeX { get; set; }
    public double BridgeY { get; set; }
    /// <summary>
    /// +1 / −1: side of the wall (left / right of its direction) the leaves open to; 0 when no leaf is drawn
    /// (windows, sliding doors): then the door keeps the family's own orientation.
    /// </summary>
    public int Side { get; set; }
    /// <summary>Single-leaf door: the hinge jamb (where the drawn leaf meets the wall), on the wall axis.</summary>
    public bool HasHinge { get; set; }
    public double HingeX { get; set; }
    public double HingeY { get; set; }
}

/// <summary>
/// Finds door and window openings from the curves drawn on their layer (blocks arrive exploded into lines), and the
/// model wall each one sits in. The opening spans the part of the drawing that lies inside the wall band; leaves and
/// swings outside the band only tell which side the door opens to.
/// </summary>
public static class OpeningReader
{
    /// <summary>Curves closer than this belong to the same door (frame lines lie 2–25 mm apart).</summary>
    private const double JoinTolMm = 30;
    private const double MinOpeningMm = 300;
    private const double MaxOpeningMm = 6000;
    /// <summary>A wall's axis may sit this far from the drawing's band (finish lines, rounding).</summary>
    private const double BandTolMm = 40;
    /// <summary>Walls on one line: their axes this close and parallel.</summary>
    private const double CollinearTolMm = 30;
    private const double ParallelSin = 0.02;
    /// <summary>A wall end this close to the opening's edge is the wall broken by it.</summary>
    private const double EdgeReachMm = 600;
    /// <summary>A drawn leaf: a straight line out of the wall at least this long, starting at the wall face.</summary>
    private const double LeafMinMm = 300;
    private const double LeafStartReachMm = 150;

    private readonly struct Seg
    {
        public Seg(double x0, double y0, double x1, double y1)
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
        public double Len => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
    }

    public static List<WallOpening> Read(
        IEnumerable<CadEntity> entities, IReadOnlyList<HostWallAxis> walls, Action<string>? log = null)
    {
        var hosts = walls.Select(w => w.Copy()).ToList();
        var spans = new List<Span>();
        foreach (var cluster in Clusters(Segments(entities)))
        {
            var span = InWall(cluster, hosts);
            if (span is null)
            {
                var (cx, cy) = Middle(cluster);
                log?.Invoke($"opening skip at ({cx:0},{cy:0}): no model wall through the drawing");
                continue;
            }

            spans.Add(span);
        }

        var result = new List<WallOpening>();
        foreach (var span in MergeSpans(spans))
        {
            var opening = Locate(span, hosts, out var why);
            if (opening is null)
            {
                var (cx, cy) = span.Point((span.S0 + span.S1) / 2);
                log?.Invoke($"opening skip at ({cx:0},{cy:0}): {why}");
                continue;
            }

            result.Add(opening);
        }

        return result;
    }

    /// <summary>The part of one drawn cluster lying inside a wall's band, as a range along that wall.</summary>
    private sealed class Span
    {
        public Span(HostWallAxis wall, double s0, double s1, double left, double right)
        {
            Wall = wall;
            S0 = s0;
            S1 = s1;
            Left = left;
            Right = right;
        }

        public HostWallAxis Wall { get; }
        public double S0 { get; set; }
        public double S1 { get; set; }
        /// <summary>Drawn length beside the band on each side (door leaf and swing).</summary>
        public double Left { get; set; }
        public double Right { get; set; }
        /// <summary>Drawn leaves: where each meets the wall (along) and the side it opens to.</summary>
        public List<(double S, int Side)> Leaves { get; } = new();

        public (double X, double Y) Point(double s)
        {
            var (ux, uy, _) = Dir(Wall);
            return (Wall.X0 + ux * s, Wall.Y0 + uy * s);
        }
    }

    /// <summary>
    /// Pieces of one opening drawn apart (two leaves, jamb marks 50 mm off the frame) become one: same wall,
    /// ranges overlapping or touching.
    /// </summary>
    private static List<Span> MergeSpans(List<Span> spans)
    {
        var merged = new List<Span>();
        foreach (var group in spans.GroupBy(s => s.Wall.Id))
        {
            Span? open = null;
            foreach (var span in group.OrderBy(s => s.S0))
            {
                if (open is not null && span.S0 <= open.S1 + MergeGapMm)
                {
                    open.S1 = Math.Max(open.S1, span.S1);
                    open.Left += span.Left;
                    open.Right += span.Right;
                    open.Leaves.AddRange(span.Leaves);
                    continue;
                }

                open = new Span(span.Wall, span.S0, span.S1, span.Left, span.Right);
                open.Leaves.AddRange(span.Leaves);
                merged.Add(open);
            }
        }

        return merged;
    }

    private const double MergeGapMm = 60;

    private static List<Seg> Segments(IEnumerable<CadEntity> entities)
    {
        var segs = new List<Seg>();
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case CadLine l:
                    segs.Add(new Seg(l.X0, l.Y0, l.X1, l.Y1));
                    break;
                case CadPolyline p:
                    for (var i = 0; i + 1 < p.Points.Count; i++)
                        segs.Add(new Seg(p.Points[i].X, p.Points[i].Y, p.Points[i + 1].X, p.Points[i + 1].Y));
                    if (p.Closed && p.Points.Count > 2)
                        segs.Add(new Seg(p.Points[p.Points.Count - 1].X, p.Points[p.Points.Count - 1].Y, p.Points[0].X, p.Points[0].Y));
                    break;
            }
        }

        return segs.Where(s => s.Len > 0.5).ToList();
    }

    private static List<List<Seg>> Clusters(List<Seg> segs)
    {
        var parent = Enumerable.Range(0, segs.Count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }

        var order = Enumerable.Range(0, segs.Count).OrderBy(i => Math.Min(segs[i].X0, segs[i].X1)).ToArray();
        for (var a = 0; a < order.Length; a++)
        {
            var sa = segs[order[a]];
            var maxX = Math.Max(sa.X0, sa.X1) + JoinTolMm;
            for (var b = a + 1; b < order.Length; b++)
            {
                var sb = segs[order[b]];
                if (Math.Min(sb.X0, sb.X1) > maxX)
                    break;
                if (Math.Min(sa.Y0, sa.Y1) - JoinTolMm > Math.Max(sb.Y0, sb.Y1) ||
                    Math.Min(sb.Y0, sb.Y1) - JoinTolMm > Math.Max(sa.Y0, sa.Y1))
                    continue;
                if (SegDistance(sa, sb) <= JoinTolMm)
                    parent[Find(order[a])] = Find(order[b]);
            }
        }

        return Enumerable.Range(0, segs.Count).GroupBy(Find).Select(g => g.Select(i => segs[i]).ToList()).ToList();
    }

    private static Span? InWall(List<Seg> cluster, List<HostWallAxis> walls)
    {
        // The wall the drawing lies in: most drawn length inside its band.
        HostWallAxis? best = null;
        double bestInBand = 0, bestS0 = 0, bestS1 = 0;
        foreach (var wall in walls)
        {
            var (ux, uy, len) = Dir(wall);
            if (len < 1)
                continue;
            var half = wall.ThicknessMm / 2 + BandTolMm;
            double inBand = 0, s0 = double.MaxValue, s1 = double.MinValue;
            foreach (var seg in cluster)
            {
                if (!ClipToBand(seg, wall, ux, uy, half, out var a, out var b))
                    continue;
                inBand += Math.Abs(b - a);
                s0 = Math.Min(s0, Math.Min(a, b));
                s1 = Math.Max(s1, Math.Max(a, b));
            }

            // The opening sits in the wall or in its break, not somewhere further along the same line.
            if (inBand <= bestInBand || s1 < -EdgeReachMm - (s1 - s0) || s0 > len + EdgeReachMm + (s1 - s0))
                continue;
            best = wall;
            bestInBand = inBand;
            bestS0 = s0;
            bestS1 = s1;
        }

        if (best is null)
            return null;
        var (bx, by, _) = Dir(best);
        var band = best.ThicknessMm / 2 + BandTolMm;
        double left = 0, right = 0;
        var leaves = new List<(double S, int Side)>();
        foreach (var seg in cluster)
        {
            var off = ((seg.X0 + seg.X1) / 2 - best.X0) * -by + ((seg.Y0 + seg.Y1) / 2 - best.Y0) * bx;
            if (off > band)
                left += seg.Len;
            else if (off < -band)
                right += seg.Len;
            else
                continue;
            // The leaf: a long straight line leaving the wall face (open 90° or at an angle); swing arcs arrive
            // as short chords, frame lines run along the wall.
            var cos = Math.Abs(((seg.X1 - seg.X0) * bx + (seg.Y1 - seg.Y0) * by) / seg.Len);
            if (seg.Len < LeafMinMm || cos > 0.95)
                continue;
            var o0 = (seg.X0 - best.X0) * -by + (seg.Y0 - best.Y0) * bx;
            var o1 = (seg.X1 - best.X0) * -by + (seg.Y1 - best.Y0) * bx;
            var nearFirst = Math.Abs(o0) <= Math.Abs(o1);
            if (Math.Min(Math.Abs(o0), Math.Abs(o1)) > band + LeafStartReachMm)
                continue;
            var nx = nearFirst ? seg.X0 : seg.X1;
            var ny = nearFirst ? seg.Y0 : seg.Y1;
            leaves.Add(((nx - best.X0) * bx + (ny - best.Y0) * by, off > 0 ? 1 : -1));
        }

        var span = new Span(best, bestS0, bestS1, left, right);
        span.Leaves.AddRange(leaves);
        return span;
    }

    private static WallOpening? Locate(Span span, List<HostWallAxis> walls, out string why)
    {
        var best = span.Wall;
        var bestS0 = span.S0;
        var bestS1 = span.S1;
        var width = bestS1 - bestS0;
        if (width < MinOpeningMm || width > MaxOpeningMm)
        {
            why = $"width {width:0} outside {MinOpeningMm:0}–{MaxOpeningMm:0} along wall {best.Id}";
            return null;
        }

        var (dx, dy, _) = Dir(best);
        var host = HostOver(best, dx, dy, bestS0, bestS1, walls, out var bridgeTo);
        if (host is null)
        {
            why = $"no wall end at the opening on the line of wall {best.Id}";
            return null;
        }

        var mid = (bestS0 + bestS1) / 2;
        // Center on the host's own axis: walls on one line can sit a few mm apart.
        var px = best.X0 + dx * mid;
        var py = best.Y0 + dy * mid;
        var t = (px - host.X0) * dx + (py - host.Y0) * dy;
        var opening = new WallOpening
        {
            X = host.X0 + dx * t,
            Y = host.Y0 + dy * t,
            DirX = dx,
            DirY = dy,
            WidthMm = width,
            HostId = host.Id
        };
        // Leaves pick the swing; a door drawn without one keeps the family's orientation.
        var hinges = new List<(double S, int Side)>();
        foreach (var leaf in span.Leaves.OrderBy(l => l.S))
        {
            if (hinges.Count == 0 || Math.Abs(leaf.S - hinges[hinges.Count - 1].S) > LeafStartReachMm)
                hinges.Add(leaf);
        }

        if (hinges.Count > 0 && hinges.All(h => h.Side == hinges[0].Side))
            opening.Side = hinges[0].Side;
        if (hinges.Count == 1 && opening.Side != 0)
        {
            var hs = hinges[0].S;
            var hx = best.X0 + dx * hs;
            var hy = best.Y0 + dy * hs;
            var ht = (hx - host.X0) * dx + (hy - host.Y0) * dy;
            opening.HasHinge = true;
            opening.HingeX = host.X0 + dx * ht;
            opening.HingeY = host.Y0 + dy * ht;
        }
        if (bridgeTo is double reach)
        {
            var (bx, by) = Near(host, best.X0 + dx * reach, best.Y0 + dy * reach);
            opening.Bridge = true;
            opening.BridgeX = bx;
            opening.BridgeY = by;
            // Later openings in the same break see the wall already through it.
            ExtendEnd(host, bx, by);
        }

        why = "";
        return opening;
    }

    /// <summary>
    /// The wall that spans [s0, s1] on <paramref name="line"/>'s line, or the wall whose end meets the opening,
    /// extended to the far edge (the next wall's end when the opening sits between two walls).
    /// </summary>
    private static HostWallAxis? HostOver(
        HostWallAxis line, double dx, double dy, double s0, double s1, List<HostWallAxis> walls, out double? bridgeTo)
    {
        bridgeTo = null;
        var onLine = new List<(HostWallAxis Wall, double A, double B)>();
        foreach (var wall in walls)
        {
            var (ux, uy, len) = Dir(wall);
            if (len < 1 || Math.Abs(ux * dy - uy * dx) > ParallelSin)
                continue;
            var off = (wall.X0 - line.X0) * -dy + (wall.Y0 - line.Y0) * dx;
            if (Math.Abs(off) > CollinearTolMm)
                continue;
            var a = (wall.X0 - line.X0) * dx + (wall.Y0 - line.Y0) * dy;
            var b = (wall.X1 - line.X0) * dx + (wall.Y1 - line.Y0) * dy;
            onLine.Add((wall, Math.Min(a, b), Math.Max(a, b)));
        }

        const double cover = 10;
        var spanning = onLine.FirstOrDefault(w => w.A <= s0 + cover && w.B >= s1 - cover);
        if (spanning.Wall is not null)
            return spanning.Wall;

        var before = onLine.Where(w => w.A < s0 && w.B <= s1 && w.B >= s0 - EdgeReachMm)
            .OrderByDescending(w => w.B).FirstOrDefault();
        var after = onLine.Where(w => w.B > s1 && w.A >= s0 && w.A <= s1 + EdgeReachMm)
            .OrderBy(w => w.A).FirstOrDefault();
        if (before.Wall is not null)
        {
            bridgeTo = after.Wall is not null ? after.A : s1;
            return before.Wall;
        }

        if (after.Wall is not null)
        {
            bridgeTo = s0;
            return after.Wall;
        }

        return null;
    }

    /// <summary>Moves the wall end nearer to the point onto it (the point lies on the wall's line).</summary>
    private static void ExtendEnd(HostWallAxis wall, double x, double y)
    {
        var d0 = (wall.X0 - x) * (wall.X0 - x) + (wall.Y0 - y) * (wall.Y0 - y);
        var d1 = (wall.X1 - x) * (wall.X1 - x) + (wall.Y1 - y) * (wall.Y1 - y);
        if (d0 < d1)
        {
            wall.X0 = x;
            wall.Y0 = y;
        }
        else
        {
            wall.X1 = x;
            wall.Y1 = y;
        }
    }

    private static (double X, double Y) Near(HostWallAxis wall, double x, double y)
    {
        var (ux, uy, _) = Dir(wall);
        var t = (x - wall.X0) * ux + (y - wall.Y0) * uy;
        return (wall.X0 + ux * t, wall.Y0 + uy * t);
    }

    /// <summary>Clips a segment to the band |offset| ≤ half around the wall axis; returns the along-range.</summary>
    private static bool ClipToBand(Seg seg, HostWallAxis wall, double ux, double uy, double half, out double a, out double b)
    {
        var s0 = (seg.X0 - wall.X0) * ux + (seg.Y0 - wall.Y0) * uy;
        var s1 = (seg.X1 - wall.X0) * ux + (seg.Y1 - wall.Y0) * uy;
        var o0 = (seg.X0 - wall.X0) * -uy + (seg.Y0 - wall.Y0) * ux;
        var o1 = (seg.X1 - wall.X0) * -uy + (seg.Y1 - wall.Y0) * ux;
        double t0 = 0, t1 = 1;
        var dO = o1 - o0;
        if (Math.Abs(dO) < 1e-9)
        {
            if (Math.Abs(o0) > half)
            {
                a = b = 0;
                return false;
            }
        }
        else
        {
            var ta = (-half - o0) / dO;
            var tb = (half - o0) / dO;
            t0 = Math.Max(t0, Math.Min(ta, tb));
            t1 = Math.Min(t1, Math.Max(ta, tb));
            if (t0 > t1)
            {
                a = b = 0;
                return false;
            }
        }

        a = s0 + (s1 - s0) * t0;
        b = s0 + (s1 - s0) * t1;
        return true;
    }

    private static (double Ux, double Uy, double Len) Dir(HostWallAxis wall)
    {
        var dx = wall.X1 - wall.X0;
        var dy = wall.Y1 - wall.Y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-9 ? (1, 0, 0) : (dx / len, dy / len, len);
    }

    private static (double X, double Y) Middle(List<Seg> cluster) =>
        ((cluster.Min(s => Math.Min(s.X0, s.X1)) + cluster.Max(s => Math.Max(s.X0, s.X1))) / 2,
            (cluster.Min(s => Math.Min(s.Y0, s.Y1)) + cluster.Max(s => Math.Max(s.Y0, s.Y1))) / 2);

    private static double SegDistance(Seg a, Seg b)
    {
        if (SegmentsCross(a, b))
            return 0;
        return Math.Min(
            Math.Min(PointSeg(a.X0, a.Y0, b), PointSeg(a.X1, a.Y1, b)),
            Math.Min(PointSeg(b.X0, b.Y0, a), PointSeg(b.X1, b.Y1, a)));
    }

    private static double PointSeg(double px, double py, Seg s)
    {
        var dx = s.X1 - s.X0;
        var dy = s.Y1 - s.Y0;
        var l2 = dx * dx + dy * dy;
        var t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((px - s.X0) * dx + (py - s.Y0) * dy) / l2));
        var qx = s.X0 + dx * t - px;
        var qy = s.Y0 + dy * t - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    private static bool SegmentsCross(Seg a, Seg b)
    {
        double Cross(double ax, double ay, double bx, double by, double cx, double cy) =>
            (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        var d1 = Cross(b.X0, b.Y0, b.X1, b.Y1, a.X0, a.Y0);
        var d2 = Cross(b.X0, b.Y0, b.X1, b.Y1, a.X1, a.Y1);
        var d3 = Cross(a.X0, a.Y0, a.X1, a.Y1, b.X0, b.Y0);
        var d4 = Cross(a.X0, a.Y0, a.X1, a.Y1, b.X1, b.Y1);
        return d1 * d2 < 0 && d3 * d4 < 0;
    }

    private static readonly Regex TokenSplit = new(@"[\s(),;:/\\]+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Type Mark for each opening: the plan label (D1, S2, "D1 (900x2200)") matching a loaded type's Type Mark.
    /// Each label goes to the opening nearest to it, so a label between two doors names only one.
    /// </summary>
    public static string[] TypeMarks(
        IReadOnlyList<WallOpening> openings, IEnumerable<CadText> texts, IReadOnlyList<string> typeMarks, double reachMm)
    {
        var marks = new string[openings.Count];
        var bestD = Enumerable.Repeat(double.MaxValue, openings.Count).ToArray();
        if (openings.Count == 0 || typeMarks.Count == 0)
            return marks.Select(_ => "").ToArray();
        foreach (var text in texts)
        {
            if (!MatchMark(text.Value, typeMarks, out var mark))
                continue;
            var tx = text.CenterX ?? text.X;
            var ty = text.CenterY ?? text.Y;
            var nearest = -1;
            var nearestD = reachMm * reachMm;
            for (var i = 0; i < openings.Count; i++)
            {
                var d = (openings[i].X - tx) * (openings[i].X - tx) + (openings[i].Y - ty) * (openings[i].Y - ty);
                if (d < nearestD)
                {
                    nearestD = d;
                    nearest = i;
                }
            }

            if (nearest < 0 || nearestD >= bestD[nearest])
                continue;
            bestD[nearest] = nearestD;
            marks[nearest] = mark;
        }

        return marks.Select(m => m ?? "").ToArray();
    }

    public static bool MatchMark(string? label, IReadOnlyList<string> typeMarks, out string matched)
    {
        matched = "";
        var raw = (label ?? "").Trim();
        if (raw.Length == 0)
            return false;
        var candidates = new List<string> { raw };
        foreach (var token in TokenSplit.Split(raw).Where(t => t.Length > 0))
        {
            candidates.Add(token);
            candidates.AddRange(token.Split('-').Where(t => t.Length > 0));
        }

        foreach (var candidate in candidates)
        {
            var hit = typeMarks.FirstOrDefault(m => FoundationCatalogFilter.TypeMarkEquals(m, candidate));
            if (hit is null)
                continue;
            matched = hit.Trim();
            return true;
        }

        return false;
    }
}
