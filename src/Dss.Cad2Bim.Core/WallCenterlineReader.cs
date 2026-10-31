using System;
using System.Collections.Generic;

namespace Dss.Cad2Bim.Core;

public readonly struct WallRun
{
    public WallRun(double x0, double y0, double x1, double y1, double thicknessMm, double nx = 0, double ny = 1)
    {
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        ThicknessMm = thicknessMm;
        var len = Math.Sqrt(nx * nx + ny * ny);
        Nx = len > 1e-9 ? nx / len : 0;
        Ny = len > 1e-9 ? ny / len : 1;
    }

    /// <summary>Centerline start.</summary>
    public double X0 { get; }
    public double Y0 { get; }
    /// <summary>Centerline end.</summary>
    public double X1 { get; }
    public double Y1 { get; }
    public double ThicknessMm { get; }
    /// <summary>Unit normal from face0 toward face1 (perpendicular to run).</summary>
    public double Nx { get; }
    public double Ny { get; }

    public void Face0(out double ax0, out double ay0, out double ax1, out double ay1)
    {
        var hx = Nx * ThicknessMm / 2;
        var hy = Ny * ThicknessMm / 2;
        ax0 = X0 - hx;
        ay0 = Y0 - hy;
        ax1 = X1 - hx;
        ay1 = Y1 - hy;
    }

    public void Face1(out double bx0, out double by0, out double bx1, out double by1)
    {
        var hx = Nx * ThicknessMm / 2;
        var hy = Ny * ThicknessMm / 2;
        bx0 = X0 + hx;
        by0 = Y0 + hy;
        bx1 = X1 + hx;
        by1 = Y1 + hy;
    }
}

/// <summary>
/// Wall centerline and thickness from a pair of parallel CAD faces.
/// Linked dimension text is not required.
/// </summary>
public static class WallCenterlineReader
{
    public const double MinThicknessMm = 80;
    public const double MaxThicknessMm = 1200;
    public const double MinOverlapMm = 200;
    /// <summary>Beam faces this far apart along one line join (a crossing beam, 600 along a skew one).</summary>
    public const double BeamFaceGapMm = 650;
    /// <summary>
    /// After a beam face-pair is accepted, parallel CAD faces within this of either face are treated as
    /// ghost/double-outline and blocked on the same span (finish lines, hatch offsets ~25–35 mm).
    /// Walls leave this unused (<see cref="FromEntities(IEnumerable{CadEntity}, bool, Func{WallRun, int}?)"/>).
    /// </summary>
    public const double GhostFacePadMm = 40;
    /// <summary>
    /// On shared column/wall CAD layers, only elongated faces become walls (vách). L/b ≤ 4 stays for columns.
    /// </summary>
    public const double MinWallAspect = 4.0;

    public static List<WallRun> FromEntities(IEnumerable<CadEntity> entities) =>
        FromEntities(entities, wallFilter: true);

    /// <param name="wallFilter">
    /// When true, keep L/b &gt; 4 (and attached stubs) as walls.
    /// When false (beams), keep every elongated parallel pair with L/b &gt; 1.5.
    /// </param>
    /// <param name="pairRank">
    /// Lower ranks first. Beams encode WxH match quality (exact thickness + text distance); unlabeled /
    /// mismatched ranks sit far above so a thin wall-hatch pair cannot steal faces from a labelled outline.
    /// </param>
    public static List<WallRun> FromEntities(IEnumerable<CadEntity> entities, bool wallFilter, Func<WallRun, int>? pairRank = null)
    {
        var faces = new List<Seg>();
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case CadLine line:
                    AddSeg(faces, line.X0, line.Y0, line.X1, line.Y1, 0);
                    break;
                case CadPolyline poly:
                    AddPoly(faces, poly);
                    break;
            }
        }

        // A beam face stops at each crossing beam (300 to 600 gap in D5X17): it is still one face.
        faces = MergeCollinear(faces, wallFilter ? 80 : BeamFaceGapMm);
        var pairs = new List<Pair>();
        for (var i = 0; i < faces.Count; i++)
        {
            for (var j = i + 1; j < faces.Count; j++)
            {
                if (!TryPair(faces[i], faces[j], out var thick, out var overlap, out var nx, out var ny, out var lo, out var hi, out var conflicts))
                    continue;
                var run = new WallRun(
                    faces[i].X0 + faces[i].Ux * lo + nx * thick / 2, faces[i].Y0 + faces[i].Uy * lo + ny * thick / 2,
                    faces[i].X0 + faces[i].Ux * hi + nx * thick / 2, faces[i].Y0 + faces[i].Uy * hi + ny * thick / 2,
                    thick, nx, ny);
                var rank = pairRank?.Invoke(run) ?? 0;
                pairs.Add(new Pair(i, j, thick, overlap, nx, ny, lo, hi, conflicts, rank));
            }
        }

        // Label-matching beam pairs before unlabeled / mismatched; then nearest faces (thinner first)
        // so a wall's own faces beat the gap to the next wall. Closed-outline solid side breaks ties.
        pairs.Sort(static (a, b) =>
        {
            var side = a.Conflicts.CompareTo(b.Conflicts);
            if (side != 0)
                return side;
            var label = a.Mismatch.CompareTo(b.Mismatch);
            if (label != 0)
                return label;
            var thick = a.Thickness.CompareTo(b.Thickness);
            return thick != 0 ? thick : b.Overlap.CompareTo(a.Overlap);
        });

        // A face may serve several walls on disjoint spans (T-junction: one long face, two short opposite faces).
        var used = new List<(double Lo, double Hi)>[faces.Count];
        for (var i = 0; i < used.Length; i++)
            used[i] = new List<(double Lo, double Hi)>();
        var runs = new List<WallRun>();
        foreach (var pair in pairs)
        {
            var a = faces[pair.I];
            var b = faces[pair.J];
            var c = a.Ux * b.Ux + a.Uy * b.Uy;
            var off = (a.X0 - b.X0) * b.Ux + (a.Y0 - b.Y0) * b.Uy;
            var blocked = new List<(double Lo, double Hi)>(used[pair.I]);
            foreach (var (blo, bhi) in used[pair.J])
            {
                var s0 = (blo - off) / c;
                var s1 = (bhi - off) / c;
                blocked.Add((Math.Min(s0, s1), Math.Max(s0, s1)));
            }

            if (!LongestFree(pair.Lo, pair.Hi, blocked, out var lo, out var hi) || hi - lo < MinOverlapMm)
                continue;
            used[pair.I].Add((lo, hi));
            var t0 = off + lo * c;
            var t1 = off + hi * c;
            used[pair.J].Add((Math.Min(t0, t1), Math.Max(t0, t1)));
            // Beam double-outlines: consume parallel ghost faces within GhostFacePadMm of either face.
            if (!wallFilter)
                BlockGhostFaces(faces, used, pair.I, pair.J, a, lo, hi, pair.Thickness);

            var halfX = pair.Nx * pair.Thickness / 2;
            var halfY = pair.Ny * pair.Thickness / 2;
            runs.Add(new WallRun(
                a.X0 + a.Ux * lo + halfX, a.Y0 + a.Uy * lo + halfY,
                a.X0 + a.Ux * hi + halfX, a.Y0 + a.Uy * hi + halfY,
                Round10(pair.Thickness), pair.Nx, pair.Ny));
        }

        var merged = MergeRuns(runs);
        return MeetAtCorners(wallFilter ? KeepWalls(merged) : KeepElongated(merged, 1.5));
    }

    /// <summary>
    /// Mark parallel faces that sit on (or within <see cref="GhostFacePadMm"/> of) either accepted face
    /// as used on the overlap span so a second outline 25–35 mm away cannot form another midline.
    /// Also block faces strictly between the pair (wall hatch / shared wall face) so a partial span
    /// cannot form a hybrid wall+beam midline ~half a wall thickness off the true beam axis.
    /// </summary>
    private static void BlockGhostFaces(
        List<Seg> faces, List<(double Lo, double Hi)>[] used,
        int faceI, int faceJ, Seg a, double lo, double hi, double thickness)
    {
        var nx = -a.Uy;
        var ny = a.Ux;
        // Face A at across=0; face B at ±thickness in the pair normal frame.
        for (var k = 0; k < faces.Count; k++)
        {
            if (k == faceI || k == faceJ)
                continue;
            var f = faces[k];
            if (Math.Abs(a.Ux * f.Ux + a.Uy * f.Uy) < 0.9986)
                continue;
            var across = (f.X0 - a.X0) * nx + (f.Y0 - a.Y0) * ny;
            var nearFace = Math.Abs(across) <= GhostFacePadMm ||
                           Math.Abs(across - thickness) <= GhostFacePadMm ||
                           Math.Abs(across + thickness) <= GhostFacePadMm;
            // Interior of the accepted band (not on the faces themselves).
            var interior = across > GhostFacePadMm && across < thickness - GhostFacePadMm ||
                           across < -GhostFacePadMm && across > -thickness + GhostFacePadMm;
            if (!nearFace && !interior)
                continue;
            var off = (a.X0 - f.X0) * f.Ux + (a.Y0 - f.Y0) * f.Uy;
            var c = a.Ux * f.Ux + a.Uy * f.Uy;
            if (Math.Abs(c) < 0.5)
                continue;
            // Span on A mapped onto f (a mirrored map here blocked unrelated collinear pieces).
            var s0 = off + lo * c;
            var s1 = off + hi * c;
            used[k].Add((Math.Min(s0, s1), Math.Max(s0, s1)));
        }
    }

    private static List<WallRun> KeepElongated(List<WallRun> runs, double minAspect)
    {
        var result = new List<WallRun>();
        foreach (var run in runs)
        {
            if (Length(run) / Math.Max(run.ThicknessMm, 1) > minAspect)
                result.Add(run);
        }

        return result;
    }

    private static bool LongestFree(double lo, double hi, List<(double Lo, double Hi)> blocked, out double bestLo, out double bestHi)
    {
        blocked.Sort(static (x, y) => x.Lo.CompareTo(y.Lo));
        bestLo = bestHi = 0;
        var cursor = lo;
        foreach (var (blo, bhi) in blocked)
        {
            if (bhi <= cursor)
                continue;
            if (blo >= hi)
                break;
            if (blo - cursor > bestHi - bestLo)
            {
                bestLo = cursor;
                bestHi = blo;
            }

            cursor = Math.Max(cursor, bhi);
        }

        if (hi - cursor > bestHi - bestLo)
        {
            bestLo = cursor;
            bestHi = hi;
        }

        return bestHi > bestLo;
    }

    private static void AddPoly(List<Seg> faces, CadPolyline poly)
    {
        var pts = poly.Points;
        if (pts.Count < 2)
            return;
        // Closed outline: solid is on the left of each edge when CCW, on the right when CW.
        var side = 0;
        if (poly.Closed && pts.Count >= 3)
        {
            var area = 0.0;
            for (var i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % pts.Count];
                area += p.X * q.Y - q.X * p.Y;
            }

            side = area > 0 ? 1 : area < 0 ? -1 : 0;
        }

        var count = poly.Closed ? pts.Count : pts.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            AddSeg(faces, a.X, a.Y, b.X, b.Y, side);
        }
    }

    private static void AddSeg(List<Seg> faces, double x0, double y0, double x1, double y1, int side)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < MinOverlapMm)
            return;
        faces.Add(new Seg(x0, y0, x1, y1, dx / len, dy / len, len, side));
    }

    private static List<Seg> MergeCollinear(List<Seg> faces, double maxGap)
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
                    if (!TryMerge(open[i], open[j], maxGap, out var merged))
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

    private static bool TryMerge(Seg a, Seg b, double maxGap, out Seg merged)
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
        // Allow slightly larger gaps so faces broken at columns still merge.
        if (gap > maxGap)
            return false;

        var x0 = a.X0 + a.Ux * lo;
        var y0 = a.Y0 + a.Uy * lo;
        var len = hi - lo;
        var bSide = a.Ux * b.Ux + a.Uy * b.Uy > 0 ? b.Side : -b.Side;
        var side = a.Side == 0 ? bSide : bSide == 0 || bSide == a.Side ? a.Side : 0;
        merged = new Seg(x0, y0, x0 + a.Ux * len, y0 + a.Uy * len, a.Ux, a.Uy, len, side);
        return true;
    }

    /// <summary>
    /// <paramref name="lo"/>/<paramref name="hi"/> are the overlap span along face <paramref name="a"/>.
    /// <paramref name="conflicts"/> counts faces whose known solid side points away from the other face.
    /// </summary>
    private static bool TryPair(
        Seg a, Seg b,
        out double thick, out double overlap,
        out double nx, out double ny,
        out double lo, out double hi,
        out int conflicts)
    {
        thick = overlap = nx = ny = lo = hi = 0;
        conflicts = 0;
        var dot = Math.Abs(a.Ux * b.Ux + a.Uy * b.Uy);
        if (dot < 0.9986)
            return false;

        var ox = b.X0 - a.X0;
        var oy = b.Y0 - a.Y0;
        nx = -a.Uy;
        ny = a.Ux;
        thick = ox * nx + oy * ny;
        var bOnLeftOfA = thick > 0;
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
        lo = Math.Max(0, Math.Min(b0, b1));
        hi = Math.Min(a.Len, Math.Max(b0, b1));
        overlap = hi - lo;
        var shorter = Math.Min(a.Len, b.Len);
        // 0.45 keeps staggered / column-broken faces that still form one wall.
        if (overlap < MinOverlapMm || overlap < shorter * 0.45)
            return false;

        if (a.Side != 0 && a.Side != (bOnLeftOfA ? 1 : -1))
            conflicts++;
        var aOnLeftOfB = -ox * -b.Uy + -oy * b.Ux > 0;
        if (b.Side != 0 && b.Side != (aOnLeftOfB ? 1 : -1))
            conflicts++;
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

        // Centerline stays exact (CAD faces); only thickness is Round10. Whole-mm rounding happens in the planner.
        var result = new List<WallRun>();
        foreach (var run in open)
        {
            if (Length(run) < MinOverlapMm)
                continue;
            result.Add(run);
        }

        return result;
    }

    /// <summary>
    /// L/b &gt; 4 runs are walls. A short run (L/b ≤ 4) stays a wall only when it joins a wall
    /// (core return / lip / collinear stub) and is not thicker than it; isolated short runs are columns.
    /// </summary>
    private static List<WallRun> KeepWalls(List<WallRun> runs)
    {
        var keep = new bool[runs.Count];
        for (var i = 0; i < runs.Count; i++)
            keep[i] = Length(runs[i]) / Math.Max(runs[i].ThicknessMm, 1) > MinWallAspect;
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < runs.Count; i++)
            {
                if (keep[i])
                    continue;
                for (var j = 0; j < runs.Count; j++)
                {
                    if (!keep[j] || !Attached(runs[i], runs[j]))
                        continue;
                    keep[i] = true;
                    changed = true;
                    break;
                }
            }
        }

        var result = new List<WallRun>();
        for (var i = 0; i < runs.Count; i++)
        {
            if (keep[i])
                result.Add(runs[i]);
        }

        return result;
    }

    private static bool Attached(WallRun stub, WallRun wall)
    {
        const double reachMm = 50;
        if (stub.ThicknessMm > wall.ThicknessMm + reachMm)
            return false;
        var sl = Length(stub);
        var wl = Length(wall);
        var sux = (stub.X1 - stub.X0) / sl;
        var suy = (stub.Y1 - stub.Y0) / sl;
        var wux = (wall.X1 - wall.X0) / wl;
        var wuy = (wall.Y1 - wall.Y0) / wl;
        if (Math.Abs(sux * wux + suy * wuy) >= 0.9986)
        {
            var offset = Math.Abs((stub.X0 - wall.X0) * -wuy + (stub.Y0 - wall.Y0) * wux);
            if (offset > Math.Abs(wall.ThicknessMm - stub.ThicknessMm) / 2 + 15)
                return false;
            var s0 = (stub.X0 - wall.X0) * wux + (stub.Y0 - wall.Y0) * wuy;
            var s1 = (stub.X1 - wall.X0) * wux + (stub.Y1 - wall.Y0) * wuy;
            return Math.Min(s0, s1) <= wl + 80 && Math.Max(s0, s1) >= -80;
        }

        return SegmentDistance(stub, wall) <= (stub.ThicknessMm + wall.ThicknessMm) / 2 + reachMm;
    }

    private static double SegmentDistance(WallRun a, WallRun b)
    {
        if (TryCross(a, b, out var px, out var py) && OnSegment(a, px, py) && OnSegment(b, px, py))
            return 0;
        return Math.Min(
            Math.Min(PointDistance(b, a.X0, a.Y0), PointDistance(b, a.X1, a.Y1)),
            Math.Min(PointDistance(a, b.X0, b.Y0), PointDistance(a, b.X1, b.Y1)));
    }

    private static bool OnSegment(WallRun run, double x, double y)
    {
        var len = Length(run);
        var s = ((x - run.X0) * (run.X1 - run.X0) + (y - run.Y0) * (run.Y1 - run.Y0)) / len;
        return s >= 0 && s <= len;
    }

    private static double PointDistance(WallRun run, double x, double y)
    {
        var dx = run.X1 - run.X0;
        var dy = run.Y1 - run.Y0;
        var len2 = dx * dx + dy * dy;
        var t = Math.Max(0, Math.Min(1, ((x - run.X0) * dx + (y - run.Y0) * dy) / len2));
        var ex = run.X0 + dx * t - x;
        var ey = run.Y0 + dy * t - y;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private static double Length(WallRun run) =>
        Math.Sqrt((run.X1 - run.X0) * (run.X1 - run.X0) + (run.Y1 - run.Y0) * (run.Y1 - run.Y0));

    /// <summary>
    /// Face pairs stop at the inner face of a crossing wall, so L/T ends fall short of the other centerline.
    /// Move each end onto the nearest crossing centerline within half its thickness + 50 mm (extend or trim).
    /// </summary>
    private static List<WallRun> MeetAtCorners(List<WallRun> runs)
    {
        const double reachMm = 50;
        var result = new List<WallRun>(runs.Count);
        for (var i = 0; i < runs.Count; i++)
        {
            var a = runs[i];
            var ends = new[] { (X: a.X0, Y: a.Y0), (X: a.X1, Y: a.Y1) };
            var alen = Math.Sqrt((a.X1 - a.X0) * (a.X1 - a.X0) + (a.Y1 - a.Y0) * (a.Y1 - a.Y0));
            var ux = (a.X1 - a.X0) / alen;
            var uy = (a.Y1 - a.Y0) / alen;
            for (var k = 0; k < 2; k++)
            {
                var sign = k == 0 ? -1.0 : 1.0;
                var best = double.MaxValue;
                for (var j = 0; j < runs.Count; j++)
                {
                    if (j == i)
                        continue;
                    var b = runs[j];
                    if (!TryCross(runs[Math.Min(i, j)], runs[Math.Max(i, j)], out var px, out var py))
                        continue;
                    var t = ((px - ends[k].X) * ux + (py - ends[k].Y) * uy) * sign;
                    if (Math.Abs(t) > b.ThicknessMm / 2 + reachMm || Math.Abs(t) >= Math.Abs(best))
                        continue;
                    var blen = Math.Sqrt((b.X1 - b.X0) * (b.X1 - b.X0) + (b.Y1 - b.Y0) * (b.Y1 - b.Y0));
                    var sb = ((px - b.X0) * (b.X1 - b.X0) + (py - b.Y0) * (b.Y1 - b.Y0)) / blen;
                    var slack = a.ThicknessMm / 2 + reachMm;
                    if (sb < -slack || sb > blen + slack)
                        continue;
                    best = t;
                    ends[k] = (px, py);
                }
            }

            result.Add(new WallRun(ends[0].X, ends[0].Y, ends[1].X, ends[1].Y, a.ThicknessMm, a.Nx, a.Ny));
        }

        return result;
    }

    private static bool TryCross(WallRun a, WallRun b, out double px, out double py)
    {
        px = py = 0;
        var adx = a.X1 - a.X0;
        var ady = a.Y1 - a.Y0;
        var bdx = b.X1 - b.X0;
        var bdy = b.Y1 - b.Y0;
        var la = Math.Sqrt(adx * adx + ady * ady);
        var lb = Math.Sqrt(bdx * bdx + bdy * bdy);
        if (la < 1 || lb < 1)
            return false;
        var cross = adx * bdy - ady * bdx;
        // Skip near-parallel runs (< ~15°).
        if (Math.Abs(cross) < 0.26 * la * lb)
            return false;
        var s = ((b.X0 - a.X0) * bdy - (b.Y0 - a.Y0) * bdx) / cross;
        px = a.X0 + adx * s;
        py = a.Y0 + ady * s;
        return true;
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
        // Same side of the thickness (normals roughly parallel).
        if (a.Nx * b.Nx + a.Ny * b.Ny < 0.98 && a.Nx * b.Nx + a.Ny * b.Ny > -0.98)
            return false;

        var a0 = 0.0;
        var a1 = alen;
        var b0 = (b.X0 - a.X0) * ux + (b.Y0 - a.Y0) * uy;
        var b1 = b0 + ((b.X1 - b.X0) * ux + (b.Y1 - b.Y0) * uy);
        var lo = Math.Min(Math.Min(a0, a1), Math.Min(b0, b1));
        var hi = Math.Max(Math.Max(a0, a1), Math.Max(b0, b1));
        if (hi - lo - alen - Math.Abs(b1 - b0) > 80)
            return false;

        var nx = a.Nx * b.Nx + a.Ny * b.Ny >= 0 ? a.Nx : -a.Nx;
        var ny = a.Nx * b.Nx + a.Ny * b.Ny >= 0 ? a.Ny : -a.Ny;
        joined = new WallRun(a.X0 + ux * lo, a.Y0 + uy * lo, a.X0 + ux * hi, a.Y0 + uy * hi, a.ThicknessMm, nx, ny);
        return true;
    }

    private static double Round10(double value) =>
        Math.Round(value / 10.0, MidpointRounding.AwayFromZero) * 10.0;

    private readonly struct Seg
    {
        public Seg(double x0, double y0, double x1, double y1, double ux, double uy, double len, int side)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Ux = ux;
            Uy = uy;
            Len = len;
            Side = side;
        }

        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public double Ux { get; }
        public double Uy { get; }
        public double Len { get; }
        /// <summary>+1 solid on the left of (Ux,Uy), -1 on the right, 0 unknown (loose lines).</summary>
        public int Side { get; }
    }

    private readonly struct Pair
    {
        public Pair(int i, int j, double thickness, double overlap, double nx, double ny, double lo, double hi, int conflicts, int mismatch)
        {
            Mismatch = mismatch;
            I = i;
            J = j;
            Thickness = thickness;
            Overlap = overlap;
            Nx = nx;
            Ny = ny;
            Lo = lo;
            Hi = hi;
            Conflicts = conflicts;
        }

        public int I { get; }
        public int J { get; }
        public double Thickness { get; }
        public double Overlap { get; }
        public double Nx { get; }
        public double Ny { get; }
        public double Lo { get; }
        public double Hi { get; }
        public int Conflicts { get; }
        public int Mismatch { get; }
    }
}
