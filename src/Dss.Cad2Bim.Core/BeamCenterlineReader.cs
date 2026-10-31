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
    public const double NearDuplicateMidlineMm = 20;
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
    /// <summary>Crossing beams never join collinear pieces further apart than this (the two end legs of one building edge).</summary>
    public const double MaxBridgeMm = 12000;
    /// <summary>A CAD face belongs to the beam band when it lies within half width + this.</summary>
    private const double FaceBandTolMm = 30;
    private const double DrawnFaceSnapMm = 5;
    private const double DrawnFaceMinCover = 0.6;
    /// <summary>Drawn pair and label may differ this much and still be the same beam (D1Y16: 300 against 400).</summary>
    private const double DrawnWidthTolMm = 100;
    /// <summary>Crossing counts when the intersection lies within this past a run end.</summary>
    private const double CrossingPadMm = 50;
    /// <summary>Midlines further apart than this are two drawn stretches of one axis, not one line.</summary>
    private const double LateralStepMm = 20;
    /// <summary>A run shorter than this cannot start a new stretch: it is a nick on the one it touches.</summary>
    private const double LateralNickMm = 500;
    /// <summary>A face pair this many label widths wide is the inner faces of two beams, not one beam.</summary>
    private const double OversizedPairRatio = 2;
    /// <summary>Label width spreads one face per pass at most along an interrupted beam; this caps the sweep.</summary>
    private const int OneFacePasses = 30;
    /// <summary>A partner face counts when its distance is within this of the label width.</summary>
    private const double PartnerWidthTolMm = 25;
    /// <summary>Longest wall treated as a lift or stair shaft side; longer ones are perimeter walls carrying a wall-top beam.</summary>
    private const double ShaftWallMaxMm = 6000;
    /// <summary>Shortest beam-layer face that may stand for a wall-top beam drawn with one face only.</summary>
    private const double WallTopMinFaceMm = 2000;

    public static List<WallRun> FromEntities(IEnumerable<CadEntity> entities, Func<WallRun, int>? pairRank = null)
    {
        var list = entities as IList<CadEntity> ?? entities.ToList();
        var runs = WallCenterlineReader.FromEntities(list, wallFilter: false, pairRank);
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
        IReadOnlyList<CadEntity>? layerEntities = null,
        IReadOnlyList<CadEntity>? drawingEntities = null)
    {
        var result = new List<BeamSegment>();
        if (runs == null || runs.Count == 0)
            return result;
        supports ??= Array.Empty<SectionFootprint>();
        var oriented = MergeNearDuplicateCenterlines(OrientAll(runs.ToList()));
        var faces = FaceLines(layerEntities);
        var edges = FaceLines(drawingEntities ?? layerEntities);
        var axes = MergeAxes(oriented, supports, faces, edges);

        var main = new List<WallRun>();
        var secondary = new List<WallRun>();
        foreach (var axis in axes)
            ClassifyAxis(axis, supports, oriented, faces, edges, main, secondary);
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

            // A column under a secondary (carried by beams at both ends) still breaks it.
            foreach (var support in supports)
            {
                var stations = SupportStations(run, ux, uy, support);
                if (stations.Count == 1 && !IsElongated(support) &&
                    stations[0] >= MinLengthMm && stations[0] <= len - MinLengthMm)
                    cuts.Add(stations[0]);
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
        // Different face-pair thicknesses (500 vs 1000 shared-face double outline) stay separate so
        // label-aware dedupe can keep the Mark-WxH width; averaging/modal-thinner would drop 1000.
        if (Math.Abs(a.ThicknessMm - b.ThicknessMm) > 30)
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
        // Prefer the modal thickness (true outline), not max — a true+ghost cross-pair (t≈530 on a
        // 500 beam) must not pull the merged midline or inflate thickness.
        var thick = group
            .GroupBy(r => Math.Round(r.ThicknessMm / 10.0) * 10.0)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First().Key;
        var members = group.Where(r => Math.Abs(r.ThicknessMm - thick) <= 15).ToList();
        if (members.Count == 0)
            members = group.ToList();

        var rep = members.OrderByDescending(Length).First();
        var len = Length(rep);
        var ux = (rep.X1 - rep.X0) / len;
        var uy = (rep.Y1 - rep.Y0) / len;
        var lo = double.MaxValue;
        var hi = double.MinValue;
        var weight = 0.0;
        var lateral = 0.0;
        foreach (var m in members)
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
        }

        // Single surviving outline: keep its midline (no average with a blocked ghost).
        // Multiple same-thickness midlines (legacy): length-weighted mean of those members only.
        lateral = members.Count == 1 || weight <= 0 ? 0 : lateral / weight;
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

    private static List<WallRun> MergeAxes(
        List<WallRun> runs, IReadOnlyList<SectionFootprint> supports, List<WallRun> faces, List<WallRun> edges)
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
            double Lateral(WallRun r) =>
                ((r.X0 + r.X1) / 2 - rep.X0) * -uy + ((r.Y0 + r.Y1) / 2 - rep.Y0) * ux;
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
                // CAD may move a bay off the grid (D1Y-3: x=-11950 south of the core, -12050 north), so each
                // stretch keeps its own drawn midline; only a short nick follows the stretch it touches.
                var stepped = s.Lo >= hi - 1 && Length(s.Run) >= LateralNickMm &&
                    Math.Abs(Lateral(s.Run) - DominantLateral(rep, ux, uy, members)) > LateralStepMm;
                if (!stepped && (s.Lo <= hi + 1 || GapCovered(rep, ux, uy, hi, s.Lo, runs, supports, faces, edges)))
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

    /// <summary>
    /// Length-weighted modal lateral in ~20 mm bins — the true beam outline usually dominates a
    /// shorter wall-hybrid nick on the same axis.
    /// </summary>
    private static double DominantLateral(WallRun rep, double ux, double uy, List<WallRun> members)
    {
        var bins = new Dictionary<int, double>();
        foreach (var m in members)
        {
            var l = Length(m);
            var mx = (m.X0 + m.X1) / 2 - rep.X0;
            var my = (m.Y0 + m.Y1) / 2 - rep.Y0;
            var lat = mx * -uy + my * ux;
            var key = (int)Math.Round(lat / 20.0);
            bins[key] = bins.TryGetValue(key, out var w) ? w + l : l;
        }

        var bestKey = bins.OrderByDescending(kv => kv.Value).ThenBy(kv => Math.Abs(kv.Key)).First().Key;
        var weight = 0.0;
        var sum = 0.0;
        foreach (var m in members)
        {
            var mx = (m.X0 + m.X1) / 2 - rep.X0;
            var my = (m.Y0 + m.Y1) / 2 - rep.Y0;
            var lat = mx * -uy + my * ux;
            if ((int)Math.Round(lat / 20.0) != bestKey)
                continue;
            var l = Length(m);
            sum += lat * l;
            weight += l;
        }

        return weight > 0 ? sum / weight : 0;
    }

    /// <summary>Merged stretch on the representative direction at the stretch's own dominant lateral.</summary>
    private static WallRun AxisRun(
        WallRun rep, double ux, double uy, double lo, double hi, List<WallRun> members)
    {
        var lateral = DominantLateral(rep, ux, uy, members);
        var thick = 0.0;
        foreach (var m in members)
            thick = Math.Max(thick, m.ThicknessMm);

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
        List<WallRun> runs, IReadOnlyList<SectionFootprint> supports, List<WallRun> faces, List<WallRun> edges)
    {
        if (g1 - g0 > MaxBridgeMm || OpenWallStretch(axis, ux, uy, g0, g1, supports, edges))
            return false;
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
            // A wall along the axis does not join two beams: past it the plan shows no beam (lift core walls).
            if (!(IsElongated(support) && SupportAlong(support, ux, uy)) &&
                SupportInterval(axis, ux, uy, support, out var enter, out var exit))
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
        List<WallRun> edges,
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
            foreach (var t in SupportStations(axis, ux, uy, support))
            {
                if (t >= -1 && t <= len + 1)
                {
                    near.Add(t);
                    continue;
                }

                // Reachable measures the open stretch; a deep support adds its own half on top.
                if (t > len + 1 && t <= len + 2 * ExtendMaxMm && Reachable(axis, ux, uy, len, t, runs, faces, supports, edges))
                    near.Add(t);
                else if (t < -1 && t >= -2 * ExtendMaxMm && Reachable(axis, ux, uy, t, 0, runs, faces, supports, edges))
                    near.Add(t);
            }
        }

        if (UniqueSorted(near).Count < 2)
        {
            secondary.Add(axis);
            return;
        }

        var extended = ExtendToSupportCenters(axis, supports, runs, faces, edges);
        var elen = Length(extended);
        if (elen < MinLengthMm)
            return;
        var eux = (extended.X1 - extended.X0) / elen;
        var euy = (extended.Y1 - extended.Y0) / elen;
        var centers = new List<double>();
        var wallEnds = new List<double>();
        var columns = new List<(double T, double Half)>();
        var crossingWalls = new List<(double T, double Half)>();
        foreach (var support in supports)
        {
            var stations = SupportStations(extended, eux, euy, support);
            foreach (var t in stations)
            {
                if (t < -1 || t > elen + 1)
                    continue;
                if (stations.Count > 1)
                {
                    wallEnds.Add(t);
                    continue;
                }

                SupportInterval(extended, eux, euy, support, out var enter, out var exit);
                if (IsElongated(support))
                {
                    crossingWalls.Add((t, (exit - enter) / 2));
                    continue;
                }

                centers.Add(t);
                columns.Add((t, (exit - enter) / 2));
            }
        }

        // A crossing wall against a column is one joint with it: a center→center piece between them
        // would lie wholly inside the two supports.
        foreach (var (t, half) in crossingWalls)
        {
            if (!columns.Any(c => Math.Abs(c.T - t) <= c.Half + half + 1))
                centers.Add(t);
        }

        // A wall end next to a column (wall between two columns) is not a joint of its own: the span runs
        // column center → column center over the wall instead of leaving a junk stub at each column.
        var minSpan = Math.Max(MinLengthMm, axis.ThicknessMm);
        foreach (var t in wallEnds)
        {
            if (!centers.Any(c => Math.Abs(c - t) < minSpan))
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
            if (centers[i + 1] - centers[i] >= MinLengthMm &&
                !InsideSupports(extended, eux, euy, centers[i], centers[i + 1], supports))
                main.Add(OrientRun(Slice(extended, eux, euy, centers[i], centers[i + 1])));
        }

        var last = centers[centers.Count - 1];
        if (centers[0] >= MinLengthMm && !InsideSupports(extended, eux, euy, 0, centers[0], supports))
            secondary.Add(OrientRun(Slice(extended, eux, euy, 0, centers[0])));
        if (elen - last >= MinLengthMm && !InsideSupports(extended, eux, euy, last, elen, supports))
            secondary.Add(OrientRun(Slice(extended, eux, euy, last, elen)));
    }

    private static bool IsElongated(SectionFootprint support) =>
        Math.Max(support.WidthMm, support.DepthMm) >= 4 * Math.Max(Math.Min(support.WidthMm, support.DepthMm), 1);

    private static bool InsideSupports(
        WallRun run, double ux, double uy, double t0, double t1, IReadOnlyList<SectionFootprint> supports)
    {
        var cover = new List<(double Lo, double Hi)>();
        foreach (var support in supports)
        {
            // A beam along a wall sits on it, not inside it: wall-top spans stay.
            if (IsElongated(support) && SupportAlong(support, ux, uy))
                continue;
            if (SupportInterval(run, ux, uy, support, out var enter, out var exit))
                cover.Add((enter, exit));
        }

        return MaxUncovered(t0, t1, cover) <= 1;
    }

    /// <summary>
    /// Beams with one face on the beam layer and the other on another layer of the same drawing (slab edge, border):
    /// a labelled face whose partner lies one label width away gives a run there, even inside a wider false pair
    /// (GTC1/GTC2 share a 680 "pair"; the real faces are 300 and 220). Labelled faces with no partner but a parallel
    /// wall take the label width on the wall side. An unlabelled leg takes the width of the beam at its corner.
    /// </summary>
    public static List<WallRun> OneFaceRuns(
        IReadOnlyList<CadEntity> layerEntities,
        IReadOnlyList<CadEntity> drawingEntities,
        IReadOnlyList<WallRun> runs,
        IReadOnlyList<SectionFootprint> supports,
        Func<WallRun, double> labelWidth,
        Func<WallRun, bool> labelledPair)
    {
        var partners = FaceLines(drawingEntities);
        // A labelled beam may lie wholly on another layer of the drawing (D2X-8B on S-HIDD).
        var faces = FaceLines(layerEntities).Concat(partners.Where(f => labelWidth(f) > 0)).ToList();
        var result = new List<WallRun>();
        // Only labelled beams spread along their line: a corner leg must not run on into the next beam.
        var labelled = new List<WallRun>();
        for (var pass = 0; pass < OneFacePasses; pass++)
        {
            var added = result.Count;
            foreach (var face in faces)
            {
                var len = Length(face);
                if (len < LateralNickMm)
                    continue;
                var width = labelWidth(face);
                var ux = (face.X1 - face.X0) / len;
                var uy = (face.Y1 - face.Y0) / len;
                if (width <= 0)
                    width = CollinearWidth(face, ux, uy, len, labelled);
                var corner = width <= 0;
                if (corner && len < WallTopMinFaceMm)
                    width = CornerWidth(face, labelled);
                if (width <= 0)
                    continue;
                if (runs.Any(r => (labelledPair(r) || r.ThicknessMm < OversizedPairRatio * width) && FaceOfRun(face, ux, uy, len, r)) ||
                    result.Any(r => FaceOfRun(face, ux, uy, len, r)))
                    continue;
                var paired = 0.0;
                foreach (var other in partners)
                {
                    if (!Parallel(face, other))
                        continue;
                    if (PartnerRun(face, ux, uy, len, other, width) is not WallRun run)
                        continue;
                    paired += Length(run);
                    if (runs.Concat(result).Any(r => SameStretch(r, run, width)))
                        continue;
                    result.Add(OrientRun(run));
                    if (!corner)
                        labelled.Add(run);
                }

                // The partner may stop short of the face (slab edge open at the tower): the wall carries the rest.
                if (paired < len - WallTopMinFaceMm && len >= WallTopMinFaceMm && WallSide(face, ux, uy, len, width, supports) is double side)
                {
                    result.Add(OrientRun(new WallRun(
                        face.X0 - uy * side, face.Y0 + ux * side, face.X1 - uy * side, face.Y1 + ux * side, width)));
                }
            }

            if (result.Count == added)
                break;
        }

        return result;
    }

    /// <summary>True for a pair at least <see cref="OversizedPairRatio"/> × as wide as a one-face run beside it.</summary>
    public static bool OversizedPair(WallRun pair, WallRun oneFace)
    {
        var len = Length(pair);
        if (len < 1 || !Parallel(pair, oneFace) || pair.ThicknessMm < OversizedPairRatio * oneFace.ThicknessMm)
            return false;
        var ux = (pair.X1 - pair.X0) / len;
        var uy = (pair.Y1 - pair.Y0) / len;
        var across = Math.Abs(((oneFace.X0 + oneFace.X1) / 2 - pair.X0) * -uy + ((oneFace.Y0 + oneFace.Y1) / 2 - pair.Y0) * ux);
        if (across > pair.ThicknessMm / 2 + oneFace.ThicknessMm)
            return false;
        return Overlap(pair, ux, uy, len, oneFace) > 0;
    }

    /// <summary>
    /// Pairing clips a crossing beam at the midline of a false wide pair (GTC3 at y=-30940, between GTC1 and GTC2).
    /// With the pair gone, such a run goes on to the midline of the nearest one-face run ahead of it.
    /// </summary>
    public static List<WallRun> ReachPastOversized(List<WallRun> runs, List<WallRun> removed, List<WallRun> oneFace)
    {
        var result = new List<WallRun>(runs.Count);
        foreach (var run in runs)
        {
            var extended = run;
            foreach (var pair in removed)
            {
                if (Parallel(extended, pair))
                    continue;
                var len = Length(extended);
                if (len < 1)
                    continue;
                var ux = (extended.X1 - extended.X0) / len;
                var uy = (extended.Y1 - extended.Y0) / len;
                var pl = Length(pair);
                var px = (pair.X1 - pair.X0) / pl;
                var py = (pair.Y1 - pair.Y0) / pl;
                double Off(double x, double y) => (x - pair.X0) * -py + (y - pair.Y0) * px;
                double Along(double x, double y) => (x - pair.X0) * px + (y - pair.Y0) * py;
                var startOn = Math.Abs(Off(extended.X0, extended.Y0)) <= 5;
                var endOn = Math.Abs(Off(extended.X1, extended.Y1)) <= 5;
                if (startOn == endOn)
                    continue;
                var (ex, ey) = startOn ? (extended.X0, extended.Y0) : (extended.X1, extended.Y1);
                var dir = startOn ? -1.0 : 1.0;
                var alongPair = Along(ex, ey);
                if (alongPair < 0 || alongPair > pl)
                    continue;
                var best = double.MaxValue;
                foreach (var face in oneFace)
                {
                    if (!Parallel(face, pair))
                        continue;
                    var gap = Off((face.X0 + face.X1) / 2, (face.Y0 + face.Y1) / 2) / (dir * (ux * -py + uy * px));
                    if (gap > 1 && gap <= pair.ThicknessMm && gap < best)
                        best = gap;
                }

                if (best == double.MaxValue)
                    continue;
                extended = startOn
                    ? new WallRun(ex - ux * best, ey - uy * best, extended.X1, extended.Y1, extended.ThicknessMm, extended.Nx, extended.Ny)
                    : new WallRun(extended.X0, extended.Y0, ex + ux * best, ey + uy * best, extended.ThicknessMm, extended.Nx, extended.Ny);
            }

            result.Add(extended);
        }

        return result;
    }

    private static double Overlap(WallRun axis, double ux, double uy, double len, WallRun other)
    {
        var a0 = (other.X0 - axis.X0) * ux + (other.Y0 - axis.Y0) * uy;
        var a1 = (other.X1 - axis.X0) * ux + (other.Y1 - axis.Y0) * uy;
        return Math.Min(Math.Max(a0, a1), len) - Math.Max(Math.Min(a0, a1), 0);
    }

    private static WallRun? PartnerRun(WallRun face, double ux, double uy, double len, WallRun other, double width)
    {
        var c0 = (other.X0 - face.X0) * -uy + (other.Y0 - face.Y0) * ux;
        var c1 = (other.X1 - face.X0) * -uy + (other.Y1 - face.Y0) * ux;
        if (Math.Abs(c0 - c1) > 1 || Math.Abs(Math.Abs(c0) - width) > PartnerWidthTolMm)
            return null;
        var a0 = (other.X0 - face.X0) * ux + (other.Y0 - face.Y0) * uy;
        var a1 = (other.X1 - face.X0) * ux + (other.Y1 - face.Y0) * uy;
        var oLo = Math.Min(a0, a1);
        var oHi = Math.Max(a0, a1);
        var lo = Math.Max(0, oLo);
        var hi = Math.Min(len, oHi);
        if (hi - lo < LateralNickMm)
            return null;
        // The partner running on past this end of the face is a corner: reach the corner beam midline.
        if (lo <= 0 && oLo <= -width + PartnerWidthTolMm)
            lo -= width / 2;
        if (hi >= len && oHi >= len + width - PartnerWidthTolMm)
            hi += width / 2;
        var mid = c0 / 2;
        return new WallRun(
            face.X0 + ux * lo - uy * mid, face.Y0 + uy * lo + ux * mid,
            face.X0 + ux * hi - uy * mid, face.Y0 + uy * hi + ux * mid, width);
    }

    private static bool SameStretch(WallRun a, WallRun b, double width)
    {
        var len = Length(b);
        if (len < 1 || !Parallel(a, b) || a.ThicknessMm >= OversizedPairRatio * width)
            return false;
        var ux = (b.X1 - b.X0) / len;
        var uy = (b.Y1 - b.Y0) / len;
        var across = Math.Abs(((a.X0 + a.X1) / 2 - b.X0) * -uy + ((a.Y0 + a.Y1) / 2 - b.Y0) * ux);
        return across <= NearDuplicateMidlineMm && Overlap(b, ux, uy, len, a) >= len / 2;
    }

    /// <summary>Width of a one-face run this face continues (the same beam, interrupted by crossing beams).</summary>
    private static double CollinearWidth(WallRun face, double ux, double uy, double len, List<WallRun> oneFace)
    {
        foreach (var run in oneFace)
        {
            if (!Parallel(face, run))
                continue;
            var across = Math.Abs(((run.X0 + run.X1) / 2 - face.X0) * -uy + ((run.Y0 + run.Y1) / 2 - face.Y0) * ux);
            if (Math.Abs(across - run.ThicknessMm / 2) > FaceBandTolMm)
                continue;
            if (Overlap(face, ux, uy, len, run) >= -MergeGapMm)
                return run.ThicknessMm;
        }

        return 0;
    }

    private static double CornerWidth(WallRun face, List<WallRun> oneFace)
    {
        foreach (var run in oneFace)
        {
            if (Parallel(face, run))
                continue;
            foreach (var (x, y) in new[] { (face.X0, face.Y0), (face.X1, face.Y1) })
            {
                if (Hypot(run.X0 - x, run.Y0 - y) <= run.ThicknessMm || Hypot(run.X1 - x, run.Y1 - y) <= run.ThicknessMm)
                    return run.ThicknessMm;
            }
        }

        return 0;
    }

    private static double? WallSide(
        WallRun face, double ux, double uy, double len, double width, IReadOnlyList<SectionFootprint> supports)
    {
        foreach (var wall in supports)
        {
            var across = (wall.CenterX - face.X0) * -uy + (wall.CenterY - face.Y0) * ux;
            var along = (wall.CenterX - face.X0) * ux + (wall.CenterY - face.Y0) * uy;
            if (IsElongated(wall) && Math.Abs(across) <= width && along >= 0 && along <= len && SupportAlong(wall, ux, uy))
                return Math.Sign(across) * width / 2;
        }

        return null;
    }

    /// <summary>
    /// A span labelled off the width of its axis lies on its own CAD lines, which may be on another layer of the
    /// drawing (D1Y16-40X60 on the 500 axis, its lines 300 apart on 3MBKC and 7MBKC): the run moves onto the drawn
    /// pair nearest the label, flush with the line the axis keeps from its neighbours, else on the pair's midline.
    /// No such pair leaves the run where it is.
    /// </summary>
    public static WallRun CenterOnDrawnFaces(WallRun run, double widthMm, IReadOnlyList<CadEntity> drawingEntities)
    {
        var len = Length(run);
        if (len < 1)
            return run;
        var ux = (run.X1 - run.X0) / len;
        var uy = (run.Y1 - run.Y0) / len;
        var reach = Math.Max(run.ThicknessMm, widthMm) / 2 + FaceBandTolMm;
        var drawn = new Dictionary<double, double>();
        foreach (var f in FaceLines(drawingEntities))
        {
            if (!Parallel(f, run))
                continue;
            var across = (f.X0 - run.X0) * -uy + (f.Y0 - run.Y0) * ux;
            if (Math.Abs(across) > reach)
                continue;
            var key = Math.Round(across / DrawnFaceSnapMm) * DrawnFaceSnapMm;
            drawn.TryGetValue(key, out var covered);
            drawn[key] = covered + Math.Max(0, Overlap(run, ux, uy, len, f));
        }

        var lines = drawn.Where(d => d.Value >= DrawnFaceMinCover * len).Select(d => d.Key).OrderBy(c => c).ToList();
        // Both faces of the run are drawn on this span: it already lies on its own lines.
        if (lines.Any(c => Math.Abs(c - run.ThicknessMm / 2) <= FaceBandTolMm) &&
            lines.Any(c => Math.Abs(c + run.ThicknessMm / 2) <= FaceBandTolMm))
            return run;
        var half = run.ThicknessMm / 2;
        var pairs = new List<(double Off, double Mid)>();
        for (var a = 0; a < lines.Count; a++)
        {
            for (var b = a + 1; b < lines.Count; b++)
            {
                var off = Math.Abs(lines[b] - lines[a] - widthMm);
                if (off > DrawnWidthTolMm)
                    continue;
                var mid = Math.Abs(lines[a] + half) <= FaceBandTolMm ? lines[a] + widthMm / 2
                    : Math.Abs(lines[b] - half) <= FaceBandTolMm ? lines[b] - widthMm / 2
                    : (lines[a] + lines[b]) / 2;
                pairs.Add((off, mid));
            }
        }

        if (pairs.Count == 0)
            return run;
        var pair = pairs.OrderBy(p => p.Off).ThenBy(p => Math.Abs(p.Mid)).First();
        return new WallRun(
            run.X0 - uy * pair.Mid, run.Y0 + ux * pair.Mid, run.X1 - uy * pair.Mid, run.Y1 + ux * pair.Mid, run.ThicknessMm);
    }

    /// <summary>Length of the curves of <paramref name="entities"/> lying in the band of <paramref name="run"/>.</summary>
    public static double FaceLengthOnRun(WallRun run, IReadOnlyList<CadEntity> entities)
    {
        var len = Length(run);
        if (len < 1)
            return 0;
        var ux = (run.X1 - run.X0) / len;
        var uy = (run.Y1 - run.Y0) / len;
        var total = 0.0;
        foreach (var (lo, hi) in FaceIntervals(run, ux, uy, FaceLines(entities)))
            total += Math.Max(0, Math.Min(hi, len) - Math.Max(lo, 0));
        return total;
    }

    private static bool SupportAlong(SectionFootprint support, double ux, double uy)
    {
        var rad = support.RotationDeg * Math.PI / 180.0;
        var alongWidth = support.WidthMm >= support.DepthMm;
        var lx = alongWidth ? Math.Cos(rad) : -Math.Sin(rad);
        var ly = alongWidth ? Math.Sin(rad) : Math.Cos(rad);
        return Math.Abs(lx * ux + ly * uy) >= 0.95;
    }

    private static bool FaceOfRun(WallRun face, double ux, double uy, double len, WallRun run)
    {
        if (!Parallel(face, run))
            return false;
        var across = Math.Abs(((run.X0 + run.X1) / 2 - face.X0) * -uy + ((run.Y0 + run.Y1) / 2 - face.Y0) * ux);
        if (across > run.ThicknessMm / 2 + FaceBandTolMm)
            return false;
        return Overlap(face, ux, uy, len, run) >= len / 2;
    }

    /// <summary>
    /// Each end reaches the center of the next support on the axis when no crossing beam lies in between
    /// and the stretch not covered by this beam's CAD faces is ≤ <see cref="ExtendMaxMm"/>.
    /// </summary>
    private static WallRun ExtendToSupportCenters(
        WallRun run, IReadOnlyList<SectionFootprint> supports, List<WallRun> runs, List<WallRun> faces, List<WallRun> edges)
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
            var stations = SupportStations(run, ux, uy, support);
            foreach (var t in stations)
            {
                // Ending on a collinear wall's end still reaches on to a column center just past it.
                var wallEnd = stations.Count > 1;
                if (Math.Abs(t) <= 1 && !wallEnd)
                    touchStart = true;
                if (Math.Abs(t - len) <= 1 && !wallEnd)
                    touchEnd = true;
                if (t > len + 1)
                    ahead = Math.Min(ahead, t);
                if (t < -1)
                    behind = Math.Max(behind, t);
            }
        }

        var t0 = 0.0;
        var t1 = len;
        if (!touchEnd && ahead < double.MaxValue && Reachable(run, ux, uy, len, ahead, runs, faces, supports, edges))
            t1 = PastAdjacentColumn(run, ux, uy, ahead, 1, supports);
        if (!touchStart && behind > double.MinValue && Reachable(run, ux, uy, behind, 0, runs, faces, supports, edges))
            t0 = PastAdjacentColumn(run, ux, uy, behind, -1, supports);
        if (Math.Abs(t0) < 1e-9 && Math.Abs(t1 - len) < 1e-9)
            return run;
        return OrientRun(Slice(run, ux, uy, t0, t1));
    }

    /// <summary>A crossing wall reached first stands against the column behind it: the span ends at that column.</summary>
    private static double PastAdjacentColumn(
        WallRun run, double ux, double uy, double t, int dir, IReadOnlyList<SectionFootprint> supports)
    {
        var best = t;
        foreach (var support in supports)
        {
            if (IsElongated(support))
                continue;
            var stations = SupportStations(run, ux, uy, support);
            if (stations.Count != 1 || (stations[0] - t) * dir <= 1)
                continue;
            var c = stations[0];
            if ((c - best) * dir > 0 && best != t)
                continue;
            if (InsideSupports(run, ux, uy, Math.Min(t, c), Math.Max(t, c), supports))
                best = c;
        }

        return best;
    }

    private static bool Reachable(
        WallRun run, double ux, double uy, double g0, double g1, List<WallRun> runs, List<WallRun> faces,
        IReadOnlyList<SectionFootprint>? supports = null, List<WallRun>? edges = null)
    {
        if (supports != null && OpenWallStretch(run, ux, uy, g0, g1, supports, edges ?? faces))
            return false;
        // Wholly inside supports nothing can stop the beam: a crossing edge beam sits on the same wall.
        if (supports != null && InsideSupports(run, ux, uy, g0, g1, supports))
            return true;
        foreach (var other in runs)
        {
            if (Parallel(run, other))
            {
                // A neighbouring beam already holds that stretch (DGX14 stops where D1X19 starts).
                var across = Math.Abs(((other.X0 + other.X1) / 2 - run.X0) * -uy + ((other.Y0 + other.Y1) / 2 - run.Y0) * ux);
                var a0 = (other.X0 - run.X0) * ux + (other.Y0 - run.Y0) * uy;
                var a1 = (other.X1 - run.X0) * ux + (other.Y1 - run.Y0) * uy;
                if (across > LateralStepMm && across <= (run.ThicknessMm + other.ThicknessMm) / 2 &&
                    Math.Min(Math.Max(a0, a1), g1) - Math.Max(Math.Min(a0, a1), g0) > LateralNickMm)
                    return false;
                continue;
            }

            if (Clip(run, ux, uy, 0, Body(other), out var enter, out var exit) && exit > g0 + 1 && enter < g1 - 1)
                return false;
        }

        var cover = FaceIntervals(run, ux, uy, faces);
        foreach (var support in supports ?? Array.Empty<SectionFootprint>())
        {
            // A wall along the beam is no CAD evidence for it: only crossing supports close the stretch.
            if (!(IsElongated(support) && SupportAlong(support, ux, uy)) &&
                SupportInterval(run, ux, uy, support, out var enter, out var exit))
                cover.Add((enter, exit));
        }

        return MaxUncovered(g0, g1, cover) <= ExtendMaxMm;
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
        var stations = SupportStations(run, ux, uy, support);
        if (stations.Count == 0)
        {
            t = 0;
            return false;
        }

        t = stations.Count == 1 ? stations[0] : (stations[0] + stations[stations.Count - 1]) / 2;
        return true;
    }

    /// <summary>
    /// Bearing stations along the beam. Columns → midpoint. A long wall collinear with the beam
    /// (dầm giằng / wall-top) → interval ends so the wall alone still yields ≥2 supports and a Main span
    /// instead of a Secondary chopped at every crossing.
    /// </summary>
    private static List<double> SupportStations(WallRun run, double ux, double uy, SectionFootprint support)
    {
        var result = new List<double>();
        if (!SupportInterval(run, ux, uy, support, out var enter, out var exit))
            return result;
        var span = exit - enter;
        var minSide = Math.Min(support.WidthMm, support.DepthMm);
        var maxSide = Math.Max(support.WidthMm, support.DepthMm);
        var rad = support.RotationDeg * Math.PI / 180.0;
        // Long axis of the footprint (Width when W≥D, else Depth) must align with the beam —
        // a crossing wall is elongated perpendicular to the beam and must stay a single mid station.
        var longAlongWidth = support.WidthMm >= support.DepthMm;
        var longUx = longAlongWidth ? Math.Cos(rad) : -Math.Sin(rad);
        var longUy = longAlongWidth ? Math.Sin(rad) : Math.Cos(rad);
        var parallel = Math.Abs(longUx * ux + longUy * uy) >= 0.95;
        // Elongated + parallel = structural wall under/along the beam, not a crossing wall stub.
        if (parallel && maxSide >= 4 * Math.Max(minSide, 1) && span >= MinLengthMm)
        {
            result.Add(enter);
            result.Add(exit);
            return result;
        }

        result.Add((enter + exit) / 2);
        return result;
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

    /// <summary>
    /// A stretch along a wall where no line of the drawing marks one beam edge is no beam: the other line is the
    /// wall or core edge, and a beam there would stand out into the shaft (lift core walls). A wall-top beam
    /// with its outer edge on another layer (slab border) keeps both edges.
    /// </summary>
    private static bool OpenWallStretch(
        WallRun axis, double ux, double uy, double g0, double g1, IReadOnlyList<SectionFootprint> supports, List<WallRun> edges)
    {
        foreach (var support in supports)
        {
            if (!IsElongated(support) || Math.Max(support.WidthMm, support.DepthMm) > ShaftWallMaxMm ||
                !SupportAlong(support, ux, uy) ||
                !SupportInterval(axis, ux, uy, support, out var enter, out var exit))
                continue;
            var lo = Math.Max(g0, enter);
            var hi = Math.Min(g1, exit);
            if (hi - lo < LateralNickMm)
                continue;
            foreach (var side in new[] { -1, 1 })
            {
                if (MaxUncovered(lo, hi, FaceIntervals(axis, ux, uy, edges, side)) >= hi - lo - 1)
                    return true;
            }
        }

        return false;
    }

    private static List<(double Lo, double Hi)> FaceIntervals(WallRun axis, double ux, double uy, List<WallRun> faces, int side = 0)
    {
        var result = new List<(double Lo, double Hi)>();
        var band = Math.Max(axis.ThicknessMm / 2, 50) + FaceBandTolMm;
        foreach (var f in faces)
        {
            if (!Parallel(axis, f))
                continue;
            var c0 = (f.X0 - axis.X0) * -uy + (f.Y0 - axis.Y0) * ux;
            var c1 = (f.X1 - axis.X0) * -uy + (f.Y1 - axis.Y0) * ux;
            if (Math.Abs(c0) > band || Math.Abs(c1) > band || c0 * side < 0)
                continue;
            // One-sided: only a line at the beam edge counts, not a wall face inside the beam.
            if (side != 0 && Math.Abs(c0) < axis.ThicknessMm / 2 - FaceBandTolMm)
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
