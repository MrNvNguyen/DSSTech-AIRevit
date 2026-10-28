using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

public enum BeamSpanKind
{
    /// <summary>Support (column / structural wall) face to support face. Not broken at crossing beams.</summary>
    Main,
    /// <summary>At least one end is not on a support. Broken at main-beam faces.</summary>
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
/// Collinear pieces merge into axes, axes reach the next support, split support face to support face (main),
/// and the remaining spans break at main-beam faces (secondary).
/// </summary>
public static class BeamCenterlineReader
{
    public const double MinLengthMm = 200;
    /// <summary>~3° collinear axis grouping.</summary>
    public const double AxisAlignCos = 0.9986;
    public const double ColumnLateralTolMm = 40;
    /// <summary>Longest stretch of a collinear gap left uncovered by crossing beams / supports / CAD faces that still merges.</summary>
    public const double MergeGapMm = 1500;
    /// <summary>Longest stretch between a beam end and the next on-axis support not covered by a CAD face of this beam.</summary>
    public const double ExtendMaxMm = 1500;
    /// <summary>A CAD face belongs to the beam band when it lies within half width + this.</summary>
    private const double FaceBandTolMm = 30;

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
    /// Merge collinear pieces into axes → extend axis ends to the next on-axis support →
    /// support-to-support spans are main beams → other spans break at main-beam faces.
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
        var oriented = OrientAll(runs.ToList());
        var faces = FaceLines(layerEntities);
        var axes = MergeAxes(oriented, supports, faces);

        var main = new List<WallRun>();
        var secondary = new List<WallRun>();
        foreach (var axis in axes)
            SplitAtSupports(ExtendToSupports(axis, supports, oriented, faces), supports, main, secondary);

        foreach (var run in main)
            result.Add(new BeamSegment(run, BeamSpanKind.Main));
        var bodies = main.Select(m => (Run: m, Body: Body(m))).ToList();
        foreach (var run in secondary)
        {
            var len = Length(run);
            var ux = (run.X1 - run.X0) / len;
            var uy = (run.Y1 - run.Y0) / len;
            var intervals = new List<(double Enter, double Exit)>();
            foreach (var (m, body) in bodies)
            {
                if (Parallel(run, m))
                    continue;
                if (Clip(run, ux, uy, 0, body, out var enter, out var exit) && exit >= -1 && enter <= len + 1)
                    intervals.Add((enter, exit));
            }

            foreach (var piece in Between(run, ux, uy, len, intervals))
                result.Add(new BeamSegment(piece, BeamSpanKind.Secondary));
        }

        return result;
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
    /// Each end reaches the near face of the next support on the axis when no crossing beam lies in between
    /// and the stretch not covered by this beam's CAD faces is ≤ <see cref="ExtendMaxMm"/>.
    /// </summary>
    private static WallRun ExtendToSupports(
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
            if (!SupportInterval(run, ux, uy, support, out var enter, out var exit))
                continue;
            if (enter <= 1 && exit >= -1)
                touchStart = true;
            if (enter <= len + 1 && exit >= len - 1)
                touchEnd = true;
            if (enter > len - 1)
                ahead = Math.Min(ahead, enter);
            if (exit < 1)
                behind = Math.Max(behind, exit);
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

    /// <summary>
    /// Supports on the axis cut it at their faces. Spans between two supports are main;
    /// spans with a free end (or axes without supports) are secondary.
    /// </summary>
    private static void SplitAtSupports(
        WallRun run, IReadOnlyList<SectionFootprint> supports, List<WallRun> main, List<WallRun> secondary)
    {
        var len = Length(run);
        if (len < MinLengthMm)
            return;
        var ux = (run.X1 - run.X0) / len;
        var uy = (run.Y1 - run.Y0) / len;
        var intervals = new List<(double Enter, double Exit)>();
        foreach (var support in supports)
        {
            if (SupportInterval(run, ux, uy, support, out var enter, out var exit) && exit >= -1 && enter <= len + 1)
                intervals.Add((enter, exit));
        }

        if (intervals.Count == 0)
        {
            secondary.Add(run);
            return;
        }

        var merged = MergeIntervals(intervals);
        if (merged[0].Enter >= MinLengthMm)
            secondary.Add(OrientRun(Slice(run, ux, uy, 0, merged[0].Enter)));
        for (var i = 0; i < merged.Count - 1; i++)
        {
            if (merged[i + 1].Enter - merged[i].Exit >= MinLengthMm)
                main.Add(OrientRun(Slice(run, ux, uy, merged[i].Exit, merged[i + 1].Enter)));
        }

        var last = merged[merged.Count - 1].Exit;
        if (len - last >= MinLengthMm)
            secondary.Add(OrientRun(Slice(run, ux, uy, last, len)));
    }

    /// <summary>Pieces of [0,len] outside the intervals (≥ MinLength). No intervals → the whole run.</summary>
    private static List<WallRun> Between(
        WallRun run, double ux, double uy, double len, List<(double Enter, double Exit)> intervals)
    {
        if (intervals.Count == 0)
            return new List<WallRun> { run };
        var merged = MergeIntervals(intervals);
        var pieces = new List<WallRun>();
        var cursor = 0.0;
        foreach (var (enter, exit) in merged)
        {
            if (enter - cursor >= MinLengthMm)
                pieces.Add(OrientRun(Slice(run, ux, uy, cursor, enter)));
            cursor = Math.Max(cursor, exit);
        }

        if (len - cursor >= MinLengthMm)
            pieces.Add(OrientRun(Slice(run, ux, uy, cursor, len)));
        return pieces;
    }

    private static List<(double Enter, double Exit)> MergeIntervals(List<(double Enter, double Exit)> intervals)
    {
        intervals.Sort((a, b) => a.Enter.CompareTo(b.Enter));
        var merged = new List<(double Enter, double Exit)>();
        foreach (var iv in intervals)
        {
            if (merged.Count == 0 || iv.Enter > merged[merged.Count - 1].Exit + 1)
                merged.Add(iv);
            else
            {
                var last = merged[merged.Count - 1];
                merged[merged.Count - 1] = (last.Enter, Math.Max(last.Exit, iv.Exit));
            }
        }

        return merged;
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
