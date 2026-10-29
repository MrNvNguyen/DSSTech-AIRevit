using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

public enum BeamSpanKind
{
    /// <summary>Support (column / structural wall) center to support center. Not broken at crossing beams.</summary>
    Main,
    /// <summary>No column/wall pair on the CAD axis. Broken at every main-beam centerline crossing.</summary>
    Secondary
}

public readonly struct BeamSegment
{
    public BeamSegment(WallRun run, BeamSpanKind kind)
    {
        Run = run;
        Kind = kind;
    }

    public WallRun Run { get; }
    public BeamSpanKind Kind { get; }
}

/// <summary>
/// Beam centerlines from parallel CAD edge faces (same pairing as walls), or CadLine when the layer is already centerlines.
/// Collinear pieces merge into axes. Axes with ≥2 real supports on the CAD extent become main
/// (center→center); other axes are secondary and break at every crossing main centerline.
/// </summary>
public static class BeamCenterlineReader
{
    public const double MinLengthMm = 200;
    /// <summary>~3° collinear axis grouping.</summary>
    public const double AxisAlignCos = 0.9986;
    /// <summary>~2° — near-duplicate / planned-segment dedupe.</summary>
    public const double DedupeAlignCos = 0.99939;
    public const double ColumnLateralTolMm = 40;
    /// <summary>After face pairing, midlines this close and parallel collapse to one centerline.</summary>
    public const double NearDuplicateMidlineMm = 30;
    /// <summary>Planned-segment dedupe: lateral ≤ max(thickness)/2 + this.</summary>
    public const double DedupeLateralPadMm = 30;
    /// <summary>
    /// When face-pair thicknesses disagree (e.g. 500 vs 1000), allow parallel midlines this far
    /// apart to still count as one overlapping bay.
    /// </summary>
    public const double DedupeMismatchLateralMm = 300;
    /// <summary>Planned-segment dedupe: keep one when along-axis overlap / shorter length ≥ this.</summary>
    public const double DedupeOverlapRatio = 0.70;
    /// <summary>Unlabeled segment may inherit Mark-WxH from a collinear sibling within this along-gap.</summary>
    public const double InheritAlongGapMm = 250;
    /// <summary>Inheritance requires midlines this close (true collinear — not parallel offset duplicates).</summary>
    public const double InheritLateralMm = 50;
    /// <summary>Longest stretch of a collinear gap left uncovered by crossing beams / supports / CAD faces that still merges.</summary>
    public const double MergeGapMm = 2500;
    /// <summary>Longest stretch between a beam end and the next on-axis support not covered by a CAD face of this beam.</summary>
    public const double ExtendMaxMm = 1500;
    /// <summary>A CAD face belongs to the beam band when it lies within half width + this.</summary>
    private const double FaceBandTolMm = 30;
    /// <summary>Crossing counts when the intersection lies within this past a run end.</summary>
    private const double CrossingPadMm = 50;

    public static List<WallRun> FromEntities(IEnumerable<CadEntity> entities, Func<WallRun, bool>? widthMismatch = null)
    {
        var list = entities as IList<CadEntity> ?? entities.ToList();
        var runs = WallCenterlineReader.FromEntities(list, wallFilter: false, widthMismatch);
        if (runs.Count > 0)
            return OrientAll(runs);

        var lines = new List<WallRun>();
        foreach (var entity in list)
        {
            if (entity is not CadLine line)
                continue;
            var dx = line.X1 - line.X0;
            var dy = line.Y1 - line.Y0;
            if (Math.Sqrt(dx * dx + dy * dy) < MinLengthMm)
                continue;
            lines.Add(OrientRun(new WallRun(line.X0, line.Y0, line.X1, line.Y1, 0)));
        }

        return lines;
    }

    /// <summary>
    /// Mostly-horizontal: left→right. Mostly-vertical: bottom→top.
    /// Diagonal: left→right first; if X tied, bottom→top.
    /// </summary>
    public static WallRun OrientRun(WallRun run)
    {
        Orient(run.X0, run.Y0, run.X1, run.Y1, out var x0, out var y0, out var x1, out var y1);
        return new WallRun(x0, y0, x1, y1, run.ThicknessMm, run.Nx, run.Ny);
    }

    public static void Orient(double x0, double y0, double x1, double y1, out double ox0, out double oy0, out double ox1, out double oy1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var swap = false;
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            if (dx < 0 || (Math.Abs(dx) < 1e-9 && dy < 0))
                swap = true;
        }
        else if (dy < 0 || (Math.Abs(dy) < 1e-9 && dx < 0))
        {
            swap = true;
        }

        if (swap)
        {
            ox0 = x1;
            oy0 = y1;
            ox1 = x0;
            oy1 = y0;
        }
        else
        {
            ox0 = x0;
            oy0 = y0;
            ox1 = x1;
            oy1 = y1;
        }
    }

    /// <summary>
    /// Merge collinear pieces into axes → axes with ≥2 supports on the CAD extent become main
    /// (extended to support centers) → other axes are secondary and break at main centerlines.
    /// </summary>
    /// <param name="supports">Columns and structural walls as plan rectangles.</param>
    /// <param name="layerEntities">Beam-layer CAD curves; a single continuing face line bridges gaps.</param>
    public static List<BeamSegment> PrepareRuns(
        IReadOnlyList<WallRun> runs,
        IReadOnlyList<SectionFootprint> supports,
        IReadOnlyList<CadEntity>? layerEntities = null)
    {
        var result = new List<BeamSegment>();
        if (runs == null || runs.Count == 0)
            return result;
        supports ??= Array.Empty<SectionFootprint>();
        var oriented = MergeNearDuplicateCenterlines(OrientAll(runs.ToList()));
        var faces = FaceLines(layerEntities);
        var axes = MergeAxes(oriented, supports, faces);

        var main = new List<WallRun>();
        var secondary = new List<WallRun>();
        foreach (var axis in axes)
            ClassifyAxis(axis, supports, oriented, faces, main, secondary);
        DemoteCrossedMains(main, secondary);

        foreach (var run in main)
            result.Add(new BeamSegment(run, BeamSpanKind.Main));

        var pieces = new List<(WallRun Run, double AxisLen)>();
        foreach (var run in secondary)
        {
            var len = Length(run);
            if (len < MinLengthMm)
                continue;
            var ux = (run.X1 - run.X0) / len;
            var uy = (run.Y1 - run.Y0) / len;
            var cuts = new List<double>();
            foreach (var m in main)
            {
                if (Parallel(run, m))
                    continue;
                if (CenterlineCrossing(run, ux, uy, len, m, out var t))
                    cuts.Add(t);
            }

            foreach (var piece in SplitAtCuts(run, ux, uy, len, cuts))
                pieces.Add((piece, len));
        }

        foreach (var piece in SplitSecondaryCrossings(pieces))
            result.Add(new BeamSegment(piece, BeamSpanKind.Secondary));

        return result;
    }

    /// <summary>
    /// A main span crossed in its interior by another main span has no support at that point, so it is carried
    /// there: the narrower one (tie: the longer) becomes secondary and is broken at the other's centerline.
    /// </summary>
    private static void DemoteCrossedMains(List<WallRun> main, List<WallRun> secondary)
    {
        var kept = new List<WallRun>();
        var demoted = new List<WallRun>();
        foreach (var run in main.OrderByDescending(r => Math.Round(r.ThicknessMm)).ThenBy(Length))
        {
            if (kept.Any(k => InteriorCrossing(run, k, out _, out _)))
                demoted.Add(run);
            else
                kept.Add(run);
        }

        if (demoted.Count == 0)
            return;
        main.Clear();
        main.AddRange(kept);
        secondary.AddRange(demoted);
    }

    /// <summary>
    /// Two secondary pieces crossing in both interiors: the wider one (tie: the longer CAD axis) stays continuous,
    /// the other is broken at its centerline.
    /// </summary>
    private static List<WallRun> SplitSecondaryCrossings(List<(WallRun Run, double AxisLen)> pieces)
    {
        var cuts = pieces.Select(_ => new List<double>()).ToArray();
        for (var i = 0; i < pieces.Count; i++)
        {
            for (var j = i + 1; j < pieces.Count; j++)
            {
                if (!InteriorCrossing(pieces[i].Run, pieces[j].Run, out var ti, out var tj))
                    continue;
                var a = pieces[i];
                var b = pieces[j];
                var iLoses = Math.Abs(a.Run.ThicknessMm - b.Run.ThicknessMm) > 1
                    ? a.Run.ThicknessMm < b.Run.ThicknessMm
                    : a.AxisLen < b.AxisLen;
                if (iLoses)
                    cuts[i].Add(ti);
                else
                    cuts[j].Add(tj);
            }
        }

        var result = new List<WallRun>();
        for (var i = 0; i < pieces.Count; i++)
        {
            var run = pieces[i].Run;
            var len = Length(run);
            result.AddRange(SplitAtCuts(run, (run.X1 - run.X0) / len, (run.Y1 - run.Y0) / len, len, cuts[i]));
        }

        return result;
    }

    /// <summary>Centerlines cross at least <see cref="MinLengthMm"/> inside both runs; t along each run.</summary>
    private static bool InteriorCrossing(WallRun a, WallRun b, out double ta, out double tb)
    {
        ta = tb = 0;
        var al = Length(a);
        var bl = Length(b);
        if (al < 2 * MinLengthMm || bl < 2 * MinLengthMm || Parallel(a, b))
            return false;
        var aux = (a.X1 - a.X0) / al;
        var auy = (a.Y1 - a.Y0) / al;
        var bux = (b.X1 - b.X0) / bl;
        var buy = (b.Y1 - b.Y0) / bl;
        var den = aux * buy - auy * bux;
        var dx = b.X0 - a.X0;
        var dy = b.Y0 - a.Y0;
        ta = (dx * buy - dy * bux) / den;
        tb = (dx * auy - dy * aux) / den;
        return ta >= MinLengthMm && ta <= al - MinLengthMm && tb >= MinLengthMm && tb <= bl - MinLengthMm;
    }

    /// <summary>
    /// Collapse face-pair midlines that sit within <see cref="NearDuplicateMidlineMm"/> and share direction —
    /// double CAD outlines of the same beam otherwise survive as two parallel axes.
    /// </summary>
    public static List<WallRun> MergeNearDuplicateCenterlines(IReadOnlyList<WallRun> runs)
    {
        if (runs == null || runs.Count == 0)
            return new List<WallRun>();
        var remaining = OrientAll(runs.ToList());
        var result = new List<WallRun>();
        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            remaining.RemoveAt(0);
            var group = new List<WallRun> { seed };
            for (var i = remaining.Count - 1; i >= 0; i--)
            {
                if (!NearDuplicateAxis(seed, remaining[i]))
                    continue;
                group.Add(remaining[i]);
                remaining.RemoveAt(i);
            }

            result.Add(group.Count == 1 ? seed : MergeDuplicateGroup(group));
        }

        return result;
    }

    /// <summary>
    /// Two planned segments on the same physical bay: parallel within ~2°, lateral ≤ max(t)/2 + pad
    /// (or ≤ <see cref="DedupeMismatchLateralMm"/> when thicknesses disagree), and along-axis overlap
    /// ≥ <see cref="DedupeOverlapRatio"/> of the shorter length.
    /// </summary>
    public static bool OverlappingBay(WallRun a, WallRun b, out double lateralMm, out double overlapRatio)
    {
        lateralMm = 0;
        overlapRatio = 0;
        var al = Length(a);
        var bl = Length(b);
        if (al < 1 || bl < 1)
            return false;
        var aux = (a.X1 - a.X0) / al;
        var auy = (a.Y1 - a.Y0) / al;
        var bux = (b.X1 - b.X0) / bl;
        var buy = (b.Y1 - b.Y0) / bl;
        if (Math.Abs(aux * bux + auy * buy) < DedupeAlignCos)
            return false;

        var midX = (b.X0 + b.X1) / 2;
        var midY = (b.Y0 + b.Y1) / 2;
        lateralMm = Math.Abs((midX - a.X0) * -auy + (midY - a.Y0) * aux);
        var latTol = Math.Max(a.ThicknessMm, b.ThicknessMm) / 2.0 + DedupeLateralPadMm;
        if (Math.Abs(a.ThicknessMm - b.ThicknessMm) > 50)
            latTol = Math.Max(latTol, DedupeMismatchLateralMm);
        if (lateralMm > latTol)
            return false;

        var a0 = 0.0;
        var a1 = al;
        var b0 = (b.X0 - a.X0) * aux + (b.Y0 - a.Y0) * auy;
        var b1 = (b.X1 - a.X0) * aux + (b.Y1 - a.Y0) * auy;
        if (b0 > b1)
            (b0, b1) = (b1, b0);
        var lo = Math.Max(a0, b0);
        var hi = Math.Min(a1, b1);
        var overlap = hi - lo;
        if (overlap < 1)
            return false;
        overlapRatio = overlap / Math.Min(al, bl);
        return overlapRatio >= DedupeOverlapRatio;
    }

    /// <summary>
    /// True when runs are end-to-end (or nearly) on the same midline — unlabeled sibling may inherit
    /// Mark-WxH. Parallel offset duplicates (lateral &gt; <see cref="InheritLateralMm"/>) are excluded.
    /// </summary>
    public static bool CollinearInheritSibling(WallRun a, WallRun b, out double alongGapMm, out double lateralMm)
    {
        alongGapMm = double.MaxValue;
        lateralMm = double.MaxValue;
        if (!TryCollinearStations(a, b, DedupeAlignCos, InheritLateralMm, out alongGapMm, out lateralMm, out _, out _, out _))
            return false;
        return alongGapMm <= InheritAlongGapMm;
    }

    /// <summary>
    /// Same physical beam axis for Mark inheritance / same-section merge: direction ≤~2°,
    /// lateral ≤ max(thickness)/2 + <see cref="InheritLateralMm"/> (midline wobble), any along-gap.
    /// </summary>
    public static bool CollinearAxisSibling(WallRun a, WallRun b, out double alongGapMm, out double lateralMm)
    {
        alongGapMm = double.MaxValue;
        lateralMm = double.MaxValue;
        var latTol = Math.Max(a.ThicknessMm, b.ThicknessMm) / 2.0 + InheritLateralMm;
        return TryCollinearStations(a, b, DedupeAlignCos, latTol, out alongGapMm, out lateralMm, out _, out _, out _);
    }

    /// <summary>
    /// Along-axis stations of <paramref name="b"/> in <paramref name="a"/>'s frame when collinear within
    /// <paramref name="alignCos"/> / <paramref name="lateralTolMm"/>.
    /// </summary>
    public static bool TryCollinearStations(
        WallRun a, WallRun b, double alignCos, double lateralTolMm,
        out double alongGapMm, out double lateralMm, out double aLen, out double b0, out double b1)
    {
        alongGapMm = double.MaxValue;
        lateralMm = double.MaxValue;
        b0 = b1 = 0;
        aLen = Length(a);
        var bl = Length(b);
        if (aLen < 1 || bl < 1)
            return false;
        var aux = (a.X1 - a.X0) / aLen;
        var auy = (a.Y1 - a.Y0) / aLen;
        var bux = (b.X1 - b.X0) / bl;
        var buy = (b.Y1 - b.Y0) / bl;
        if (Math.Abs(aux * bux + auy * buy) < alignCos)
            return false;

        var midX = (b.X0 + b.X1) / 2;
        var midY = (b.Y0 + b.Y1) / 2;
        lateralMm = Math.Abs((midX - a.X0) * -auy + (midY - a.Y0) * aux);
        if (lateralMm > lateralTolMm)
            return false;

        b0 = (b.X0 - a.X0) * aux + (b.Y0 - a.Y0) * auy;
        b1 = (b.X1 - a.X0) * aux + (b.Y1 - a.Y0) * auy;
        if (b0 > b1)
            (b0, b1) = (b1, b0);
        if (b1 < 0)
            alongGapMm = -b1;
        else if (b0 > aLen)
            alongGapMm = b0 - aLen;
        else
            alongGapMm = 0;
        return true;
    }

    /// <summary>
    /// Outermost collinear merge of two runs on <paramref name="a"/>'s direction (length-weighted lateral).
    /// </summary>
    public static WallRun MergeCollinearRuns(WallRun a, WallRun b)
    {
        var al = Length(a);
        if (al < 1)
            return OrientRun(b);
        var aux = (a.X1 - a.X0) / al;
        var auy = (a.Y1 - a.Y0) / al;
        var a0 = 0.0;
        var a1 = al;
        var b0 = (b.X0 - a.X0) * aux + (b.Y0 - a.Y0) * auy;
        var b1 = (b.X1 - a.X0) * aux + (b.Y1 - a.Y0) * auy;
        if (b0 > b1)
            (b0, b1) = (b1, b0);
        var lo = Math.Min(a0, b0);
        var hi = Math.Max(a1, b1);
        var la = al;
        var lb = Length(b);
        var midAx = (a.X0 + a.X1) / 2 - a.X0;
        var midAy = (a.Y0 + a.Y1) / 2 - a.Y0;
        var midBx = (b.X0 + b.X1) / 2 - a.X0;
        var midBy = (b.Y0 + b.Y1) / 2 - a.Y0;
        var latA = midAx * -auy + midAy * aux;
        var latB = midBx * -auy + midBy * aux;
        var lateral = (latA * la + latB * lb) / Math.Max(la + lb, 1);
        var ox = a.X0 - auy * lateral;
        var oy = a.Y0 + aux * lateral;
        var thick = Math.Max(a.ThicknessMm, b.ThicknessMm);
        return OrientRun(new WallRun(ox + aux * lo, oy + auy * lo, ox + aux * hi, oy + auy * hi, thick, a.Nx, a.Ny));
    }

    /// <summary>
    /// Support center lies on the axis between <paramref name="lo"/> and <paramref name="hi"/> (exclusive ends by
    /// <paramref name="endPadMm"/>) — used to refuse same-section merges across a real column/wall.
    /// </summary>
    public static bool SupportBetween(WallRun axis, double lo, double hi, SectionFootprint support, double endPadMm = 50)
    {
        var len = Length(axis);
        if (len < 1 || hi - lo < 1)
            return false;
        var ux = (axis.X1 - axis.X0) / len;
        var uy = (axis.Y1 - axis.Y0) / len;
        if (!SupportCenter(axis, ux, uy, support, out var t))
            return false;
        return t > lo + endPadMm && t < hi - endPadMm;
    }

    /// <summary>
    /// True when a planned segment endpoint sits near the support and is not parallel to <paramref name="axis"/> —
    /// evidence the support is a real grid joint (not a lone CAD hatch that only nicks this bay).
    /// </summary>
    public static bool PerpendicularEndpointNear(
        WallRun axis, SectionFootprint support, IReadOnlyList<WallRun> others, double nearMm = 150)
    {
        var len = Length(axis);
        if (len < 1)
            return false;
        var ux = (axis.X1 - axis.X0) / len;
        var uy = (axis.Y1 - axis.Y0) / len;
        if (!SupportCenter(axis, ux, uy, support, out var t))
            return false;
        var sx = axis.X0 + ux * t;
        var sy = axis.Y0 + uy * t;
        foreach (var other in others)
        {
            var ol = Length(other);
            if (ol < 1 || Parallel(axis, other))
                continue;
            if (Hypot(other.X0 - sx, other.Y0 - sy) <= nearMm ||
                Hypot(other.X1 - sx, other.Y1 - sy) <= nearMm)
                return true;
        }

        return false;
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    private static bool NearDuplicateAxis(WallRun a, WallRun b)
    {
        var al = Length(a);
        var bl = Length(b);
        if (al < 1 || bl < 1)
            return false;
        var aux = (a.X1 - a.X0) / al;
        var auy = (a.Y1 - a.Y0) / al;
        var bux = (b.X1 - b.X0) / bl;
        var buy = (b.Y1 - b.Y0) / bl;
        if (Math.Abs(aux * bux + auy * buy) < DedupeAlignCos)
            return false;
        var midX = (b.X0 + b.X1) / 2;
        var midY = (b.Y0 + b.Y1) / 2;
        var across = Math.Abs((midX - a.X0) * -auy + (midY - a.Y0) * aux);
        if (across > NearDuplicateMidlineMm)
            return false;
        // Require some along-axis overlap so unrelated parallel beams far apart do not merge.
        var b0 = (b.X0 - a.X0) * aux + (b.Y0 - a.Y0) * auy;
        var b1 = (b.X1 - a.X0) * aux + (b.Y1 - a.Y0) * auy;
        var lo = Math.Max(0, Math.Min(b0, b1));
        var hi = Math.Min(al, Math.Max(b0, b1));
        return hi - lo >= Math.Min(al, bl) * 0.5;
    }

    private static WallRun MergeDuplicateGroup(List<WallRun> group)
    {
        var rep = group.OrderByDescending(Length).First();
        var len = Length(rep);
        var ux = (rep.X1 - rep.X0) / len;
        var uy = (rep.Y1 - rep.Y0) / len;
        var lo = double.MaxValue;
        var hi = double.MinValue;
        var weight = 0.0;
        var lateral = 0.0;
        var thick = 0.0;
        foreach (var m in group)
        {
            var l = Length(m);
            var a0 = (m.X0 - rep.X0) * ux + (m.Y0 - rep.Y0) * uy;
            var a1 = (m.X1 - rep.X0) * ux + (m.Y1 - rep.Y0) * uy;
            lo = Math.Min(lo, Math.Min(a0, a1));
            hi = Math.Max(hi, Math.Max(a0, a1));
            var mx = (m.X0 + m.X1) / 2 - rep.X0;
            var my = (m.Y0 + m.Y1) / 2 - rep.Y0;
            lateral += (mx * -uy + my * ux) * l;
            weight += l;
            thick = Math.Max(thick, m.ThicknessMm);
        }

        lateral = weight > 0 ? lateral / weight : 0;
        var ox = rep.X0 - uy * lateral;
        var oy = rep.Y0 + ux * lateral;
        return OrientRun(new WallRun(ox + ux * lo, oy + uy * lo, ox + ux * hi, oy + uy * hi, thick, rep.Nx, rep.Ny));
    }

    /// <summary>True when runs are collinear (same direction within ~3°) and laterally within half thickness.</summary>
    public static bool SameAxis(WallRun a, WallRun b)
    {
        var al = Length(a);
        var bl = Length(b);
        if (al < 1 || bl < 1)
            return false;
        var aux = (a.X1 - a.X0) / al;
        var auy = (a.Y1 - a.Y0) / al;
        var bux = (b.X1 - b.X0) / bl;
        var buy = (b.Y1 - b.Y0) / bl;
        if (Math.Abs(aux * bux + auy * buy) < AxisAlignCos)
            return false;

        var midX = (b.X0 + b.X1) / 2;
        var midY = (b.Y0 + b.Y1) / 2;
        var across = Math.Abs((midX - a.X0) * -auy + (midY - a.Y0) * aux);
        var lat = Math.Max(a.ThicknessMm, b.ThicknessMm) / 2.0 + ColumnLateralTolMm;
        if (lat < 100)
            lat = 100;
        return across <= lat;
    }

    /// <summary>Plan rectangle of a beam run (length × width) for clipping crossing axes.</summary>
    public static SectionFootprint Body(WallRun run)
    {
        var len = Length(run);
        var angle = Math.Atan2(run.Y1 - run.Y0, run.X1 - run.X0) * 180.0 / Math.PI;
        return new SectionFootprint(
            (run.X0 + run.X1) / 2, (run.Y0 + run.Y1) / 2, len, Math.Max(run.ThicknessMm, 100), angle);
    }

    private static List<WallRun> MergeAxes(List<WallRun> runs, IReadOnlyList<SectionFootprint> supports, List<WallRun> faces)
    {
        var axes = new List<List<WallRun>>();
        foreach (var run in runs.OrderByDescending(Length))
        {
            if (Length(run) < 1)
                continue;
            var axis = axes.FirstOrDefault(a => SameAxis(a[0], run) && SimilarWidth(a[0], run));
            if (axis is null)
                axes.Add(new List<WallRun> { run });
            else
                axis.Add(run);
        }

        var result = new List<WallRun>();
        foreach (var axis in axes)
        {
            var rep = axis[0];
            var len = Length(rep);
            var ux = (rep.X1 - rep.X0) / len;
            var uy = (rep.Y1 - rep.Y0) / len;
            var spans = axis
                .Select(r =>
                {
                    var a = (r.X0 - rep.X0) * ux + (r.Y0 - rep.Y0) * uy;
                    var b = (r.X1 - rep.X0) * ux + (r.Y1 - rep.Y0) * uy;
                    return (Lo: Math.Min(a, b), Hi: Math.Max(a, b), Run: r);
                })
                .OrderBy(s => s.Lo)
                .ToList();

            var lo = spans[0].Lo;
            var hi = spans[0].Hi;
            var members = new List<WallRun> { spans[0].Run };
            for (var i = 1; i < spans.Count; i++)
            {
                var s = spans[i];
                if (s.Lo <= hi + 1 || GapCovered(rep, ux, uy, hi, s.Lo, runs, supports, faces))
                {
                    hi = Math.Max(hi, s.Hi);
                    members.Add(s.Run);
                    continue;
                }

                result.Add(AxisRun(rep, ux, uy, lo, hi, members));
                lo = s.Lo;
                hi = s.Hi;
                members = new List<WallRun> { s.Run };
            }

            result.Add(AxisRun(rep, ux, uy, lo, hi, members));
        }

        return result;
    }

    private static bool SimilarWidth(WallRun a, WallRun b) =>
        a.ThicknessMm <= 0 || b.ThicknessMm <= 0 || Math.Abs(a.ThicknessMm - b.ThicknessMm) <= 25;

    /// <summary>Merged axis on the representative direction, laterally at the length-weighted member centerline.</summary>
    private static WallRun AxisRun(WallRun rep, double ux, double uy, double lo, double hi, List<WallRun> members)
    {
        var weight = 0.0;
        var lateral = 0.0;
        var thick = 0.0;
        foreach (var m in members)
        {
            var l = Length(m);
            var mx = (m.X0 + m.X1) / 2 - rep.X0;
            var my = (m.Y0 + m.Y1) / 2 - rep.Y0;
            lateral += (mx * -uy + my * ux) * l;
            weight += l;
            thick = Math.Max(thick, m.ThicknessMm);
        }

        lateral = weight > 0 ? lateral / weight : 0;
        var ox = rep.X0 - uy * lateral;
        var oy = rep.Y0 + ux * lateral;
        return OrientRun(new WallRun(ox + ux * lo, oy + uy * lo, ox + ux * hi, oy + uy * hi, thick, rep.Nx, rep.Ny));
    }

    /// <summary>
    /// Gap [g0,g1] along the axis is bridged when every stretch not covered by a crossing beam,
    /// a support or a CAD face line of this beam is ≤ <see cref="MergeGapMm"/>.
    /// </summary>
    private static bool GapCovered(
        WallRun axis, double ux, double uy, double g0, double g1,
        List<WallRun> runs, IReadOnlyList<SectionFootprint> supports, List<WallRun> faces)
    {
        if (g1 - g0 <= MergeGapMm)
            return true;
        var cover = new List<(double Lo, double Hi)>();
        foreach (var run in runs)
        {
            if (Parallel(axis, run))
                continue;
            if (Clip(axis, ux, uy, 0, Body(run), out var enter, out var exit))
                cover.Add((enter, exit));
        }

        foreach (var support in supports)
        {
            if (SupportInterval(axis, ux, uy, support, out var enter, out var exit))
                cover.Add((enter, exit));
        }

        cover.AddRange(FaceIntervals(axis, ux, uy, faces));
        return MaxUncovered(g0, g1, cover) <= MergeGapMm;
    }

    /// <summary>
    /// ≥2 supports on/near the CAD axis (within <see cref="ExtendMaxMm"/>, path clear) → main spans
    /// at support centers. One lonely/far support does not make a fake main (beam-to-beam stays secondary).
    /// </summary>
    private static void ClassifyAxis(
        WallRun axis,
        IReadOnlyList<SectionFootprint> supports,
        List<WallRun> runs,
        List<WallRun> faces,
        List<WallRun> main,
        List<WallRun> secondary)
    {
        var len = Length(axis);
        if (len < MinLengthMm)
            return;
        var ux = (axis.X1 - axis.X0) / len;
        var uy = (axis.Y1 - axis.Y0) / len;

        var near = new List<double>();
        foreach (var support in supports)
        {
            if (!SupportCenter(axis, ux, uy, support, out var t))
                continue;
            if (t >= -1 && t <= len + 1)
            {
                near.Add(t);
                continue;
            }

            if (t > len + 1 && t <= len + ExtendMaxMm && Reachable(axis, ux, uy, len, t, runs, faces))
                near.Add(t);
            else if (t < -1 && t >= -ExtendMaxMm && Reachable(axis, ux, uy, t, 0, runs, faces))
                near.Add(t);
        }

        if (UniqueSorted(near).Count < 2)
        {
            secondary.Add(axis);
            return;
        }

        var extended = ExtendToSupportCenters(axis, supports, runs, faces);
        var elen = Length(extended);
        if (elen < MinLengthMm)
            return;
        var eux = (extended.X1 - extended.X0) / elen;
        var euy = (extended.Y1 - extended.Y0) / elen;
        var centers = new List<double>();
        foreach (var support in supports)
        {
            if (SupportCenter(extended, eux, euy, support, out var t) && t >= -1 && t <= elen + 1)
                centers.Add(t);
        }

        centers = UniqueSorted(centers);
        if (centers.Count < 2)
        {
            secondary.Add(axis);
            return;
        }

        for (var i = 0; i < centers.Count - 1; i++)
        {
            if (centers[i + 1] - centers[i] >= MinLengthMm)
                main.Add(OrientRun(Slice(extended, eux, euy, centers[i], centers[i + 1])));
        }

        if (centers[0] >= MinLengthMm)
            secondary.Add(OrientRun(Slice(extended, eux, euy, 0, centers[0])));
        if (elen - centers[centers.Count - 1] >= MinLengthMm)
            secondary.Add(OrientRun(Slice(extended, eux, euy, centers[centers.Count - 1], elen)));
    }

    /// <summary>
    /// Each end reaches the center of the next support on the axis when no crossing beam lies in between
    /// and the stretch not covered by this beam's CAD faces is ≤ <see cref="ExtendMaxMm"/>.
    /// </summary>
    private static WallRun ExtendToSupportCenters(
        WallRun run, IReadOnlyList<SectionFootprint> supports, List<WallRun> runs, List<WallRun> faces)
    {
        if (supports.Count == 0)
            return run;
        var len = Length(run);
        if (len < 1)
            return run;
        var ux = (run.X1 - run.X0) / len;
        var uy = (run.Y1 - run.Y0) / len;

        var ahead = double.MaxValue;
        var behind = double.MinValue;
        var touchStart = false;
        var touchEnd = false;
        foreach (var support in supports)
        {
            if (!SupportCenter(run, ux, uy, support, out var t))
                continue;
            if (Math.Abs(t) <= 1)
                touchStart = true;
            if (Math.Abs(t - len) <= 1)
                touchEnd = true;
            if (t > len + 1)
                ahead = Math.Min(ahead, t);
            if (t < -1)
                behind = Math.Max(behind, t);
        }

        var t0 = 0.0;
        var t1 = len;
        if (!touchEnd && ahead < double.MaxValue && Reachable(run, ux, uy, len, ahead, runs, faces))
            t1 = ahead;
        if (!touchStart && behind > double.MinValue && Reachable(run, ux, uy, behind, 0, runs, faces))
            t0 = behind;
        if (Math.Abs(t0) < 1e-9 && Math.Abs(t1 - len) < 1e-9)
            return run;
        return OrientRun(Slice(run, ux, uy, t0, t1));
    }

    private static bool Reachable(WallRun run, double ux, double uy, double g0, double g1, List<WallRun> runs, List<WallRun> faces)
    {
        foreach (var other in runs)
        {
            if (Parallel(run, other))
                continue;
            if (Clip(run, ux, uy, 0, Body(other), out var enter, out var exit) && exit > g0 + 1 && enter < g1 - 1)
                return false;
        }

        return MaxUncovered(g0, g1, FaceIntervals(run, ux, uy, faces)) <= ExtendMaxMm;
    }

    /// <summary>Intersection of two centerlines; t along <paramref name="run"/>.</summary>
    private static bool CenterlineCrossing(
        WallRun run, double ux, double uy, double len, WallRun main, out double t)
    {
        t = 0;
        var ml = Length(main);
        if (ml < 1)
            return false;
        var mx = (main.X1 - main.X0) / ml;
        var my = (main.Y1 - main.Y0) / ml;
        var den = ux * my - uy * mx;
        if (Math.Abs(den) < 1e-9)
            return false;

        var dx = main.X0 - run.X0;
        var dy = main.Y0 - run.Y0;
        t = (dx * my - dy * mx) / den;
        var s = (dx * uy - dy * ux) / den;
        if (t < -CrossingPadMm || t > len + CrossingPadMm)
            return false;
        if (s < -CrossingPadMm || s > ml + CrossingPadMm)
            return false;
        t = Math.Max(0, Math.Min(len, t));
        return true;
    }

    /// <summary>Pieces of [0,len] split at cut parameters (inclusive ends at each cut).</summary>
    private static List<WallRun> SplitAtCuts(WallRun run, double ux, double uy, double len, List<double> cuts)
    {
        if (cuts.Count == 0)
            return new List<WallRun> { run };

        var sorted = UniqueSorted(cuts);
        var pieces = new List<WallRun>();
        var cursor = 0.0;
        foreach (var cut in sorted)
        {
            if (cut <= CrossingPadMm)
            {
                cursor = Math.Max(cursor, cut);
                continue;
            }

            if (cut - cursor >= MinLengthMm)
                pieces.Add(OrientRun(Slice(run, ux, uy, cursor, cut)));
            cursor = cut;
        }

        if (len - cursor >= MinLengthMm)
            pieces.Add(OrientRun(Slice(run, ux, uy, cursor, len)));
        return pieces;
    }

    private static List<double> UniqueSorted(List<double> values)
    {
        values.Sort();
        var result = new List<double>();
        foreach (var v in values)
        {
            if (result.Count == 0 || Math.Abs(v - result[result.Count - 1]) > 1)
                result.Add(v);
        }

        return result;
    }

    /// <summary>Along-axis station of the support center when the axis meets the footprint.</summary>
    private static bool SupportCenter(WallRun run, double ux, double uy, SectionFootprint support, out double t)
    {
        if (!SupportInterval(run, ux, uy, support, out var enter, out var exit))
        {
            t = 0;
            return false;
        }

        t = (enter + exit) / 2;
        return true;
    }

    /// <summary>
    /// Along-axis interval where the beam meets a support: the centerline crossing, or (column flush with a beam face)
    /// the span hit by lines just inside either beam face.
    /// </summary>
    private static bool SupportInterval(WallRun run, double ux, double uy, SectionFootprint support, out double enter, out double exit)
    {
        if (Clip(run, ux, uy, 0, support, out enter, out exit))
            return true;
        var inset = Math.Max(run.ThicknessMm / 2 - 10, 0);
        if (inset < 1)
            return false;
        var hit = false;
        enter = double.MaxValue;
        exit = double.MinValue;
        foreach (var side in new[] { -inset, inset })
        {
            if (!Clip(run, ux, uy, side, support, out var e0, out var e1))
                continue;
            hit = true;
            enter = Math.Min(enter, e0);
            exit = Math.Max(exit, e1);
        }

        return hit;
    }

    /// <summary>Clip the (infinite) axis line, offset <paramref name="lateral"/> to the left, with a plan rectangle.</summary>
    private static bool Clip(WallRun run, double ux, double uy, double lateral, SectionFootprint box, out double enter, out double exit)
    {
        enter = double.MinValue;
        exit = double.MaxValue;
        var px = run.X0 - uy * lateral - box.CenterX;
        var py = run.Y0 + ux * lateral - box.CenterY;
        var rad = box.RotationDeg * Math.PI / 180.0;
        var c = Math.Cos(rad);
        var s = Math.Sin(rad);
        foreach (var (ax, ay, half) in new[] { (c, s, box.WidthMm / 2), (-s, c, box.DepthMm / 2) })
        {
            var p = px * ax + py * ay;
            var d = ux * ax + uy * ay;
            if (Math.Abs(d) < 1e-9)
            {
                if (Math.Abs(p) > half)
                    return false;
                continue;
            }

            var a = (-half - p) / d;
            var b = (half - p) / d;
            enter = Math.Max(enter, Math.Min(a, b));
            exit = Math.Min(exit, Math.Max(a, b));
        }

        return exit > enter;
    }

    private static List<(double Lo, double Hi)> FaceIntervals(WallRun axis, double ux, double uy, List<WallRun> faces)
    {
        var result = new List<(double Lo, double Hi)>();
        var band = Math.Max(axis.ThicknessMm / 2, 50) + FaceBandTolMm;
        foreach (var f in faces)
        {
            if (!Parallel(axis, f))
                continue;
            var c0 = (f.X0 - axis.X0) * -uy + (f.Y0 - axis.Y0) * ux;
            var c1 = (f.X1 - axis.X0) * -uy + (f.Y1 - axis.Y0) * ux;
            if (Math.Abs(c0) > band || Math.Abs(c1) > band)
                continue;
            var a0 = (f.X0 - axis.X0) * ux + (f.Y0 - axis.Y0) * uy;
            var a1 = (f.X1 - axis.X0) * ux + (f.Y1 - axis.Y0) * uy;
            result.Add((Math.Min(a0, a1), Math.Max(a0, a1)));
        }

        return result;
    }

    private static double MaxUncovered(double g0, double g1, List<(double Lo, double Hi)> cover)
    {
        cover.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        var worst = 0.0;
        var cursor = g0;
        foreach (var (lo, hi) in cover)
        {
            if (hi <= cursor)
                continue;
            if (lo >= g1)
                break;
            worst = Math.Max(worst, lo - cursor);
            cursor = Math.Max(cursor, hi);
        }

        return Math.Max(worst, g1 - cursor);
    }

    private static List<WallRun> FaceLines(IReadOnlyList<CadEntity>? entities)
    {
        var result = new List<WallRun>();
        if (entities == null)
            return result;
        void Add(double x0, double y0, double x1, double y1)
        {
            if ((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) >= 50.0 * 50.0)
                result.Add(new WallRun(x0, y0, x1, y1, 0));
        }

        foreach (var entity in entities)
        {
            switch (entity)
            {
                case CadLine line:
                    Add(line.X0, line.Y0, line.X1, line.Y1);
                    break;
                case CadPolyline poly:
                    var count = poly.Closed ? poly.Points.Count : poly.Points.Count - 1;
                    for (var i = 0; i < count; i++)
                    {
                        var a = poly.Points[i];
                        var b = poly.Points[(i + 1) % poly.Points.Count];
                        Add(a.X, a.Y, b.X, b.Y);
                    }

                    break;
            }
        }

        return result;
    }

    private static bool Parallel(WallRun a, WallRun b)
    {
        var al = Length(a);
        var bl = Length(b);
        if (al < 1 || bl < 1)
            return true;
        return Math.Abs((a.X1 - a.X0) * (b.X1 - b.X0) + (a.Y1 - a.Y0) * (b.Y1 - b.Y0)) / (al * bl) >= AxisAlignCos;
    }

    private static WallRun Slice(WallRun run, double ux, double uy, double t0, double t1) =>
        new WallRun(
            run.X0 + ux * t0, run.Y0 + uy * t0,
            run.X0 + ux * t1, run.Y0 + uy * t1,
            run.ThicknessMm, run.Nx, run.Ny);

    private static double Length(WallRun run) =>
        Math.Sqrt((run.X1 - run.X0) * (run.X1 - run.X0) + (run.Y1 - run.Y0) * (run.Y1 - run.Y0));

    private static List<WallRun> OrientAll(List<WallRun> runs)
    {
        var result = new List<WallRun>(runs.Count);
        foreach (var run in runs)
            result.Add(OrientRun(run));
        return result;
    }
}
