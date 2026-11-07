using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

/// <summary>Section read from a CAD layer name: W×H for rectangular, diameter for round (mm).</summary>
public readonly struct MepSize
{
    public MepSize(double widthMm, double heightMm, double diameterMm)
    {
        WidthMm = widthMm;
        HeightMm = heightMm;
        DiameterMm = diameterMm;
    }

    public double WidthMm { get; }
    public double HeightMm { get; }
    public double DiameterMm { get; }
    public bool IsRound => DiameterMm > 0;
    /// <summary>Height of the section (diameter when round): what sits under the beam.</summary>
    public double DepthMm => IsRound ? DiameterMm : HeightMm;
    public override string ToString() => IsRound ? $"Ø{DiameterMm:0}" : $"{WidthMm:0}x{HeightMm:0}";
}

/// <summary>One straight run between two nodes of a single-line network.</summary>
public sealed class MepEdge
{
    public int A { get; set; }
    public int B { get; set; }
    public MepSize Size { get; set; }
    public string Layer { get; set; } = "";
    /// <summary>Where the size came from: text (label beside the line), layer, drawn (width between two lines), inherited (next run), default.</summary>
    public string SizeFrom { get; set; } = "";
    /// <summary>Drawn with two lines (the duct's sides): the width between them (mm); 0 for a single line.</summary>
    public double DrawnWidthMm { get; set; }
    /// <summary>
    /// Not a run: a reducer drawn off-centre (one side flat), joining the end of the run at A to the end of the run at
    /// B with a transition fitting.
    /// </summary>
    public bool IsTransition { get; set; }
    /// <summary>Fall written on the run (i=1%, i = 0,5%): percent; null where none is written or carried on.</summary>
    public double? SlopePercent { get; set; }
}

public sealed class MepNetwork
{
    public List<(double X, double Y)> Nodes { get; } = new();
    public List<MepEdge> Edges { get; } = new();
    /// <summary>Flex ducts drawn on the picked layers, left out (joined later by the flex tool).</summary>
    public int FlexSkipped { get; set; }
    /// <summary>Short two-line boxes standing alone with no size written (openings, louvers through a wall), left out.</summary>
    public int OpeningsSkipped { get; set; }
    /// <summary>
    /// Bends drawn round (two-line ducts): the corner and the radius drawn to the duct's centre (mm), for the elbow
    /// to follow the drawing rather than its family's default.
    /// </summary>
    public List<(double X, double Y, double RadiusMm)> Bends { get; } = new();
}

/// <summary>A level run of one edge: from/to along the edge and the centerline elevation (mm, absolute).</summary>
public readonly struct MepPiece
{
    public MepPiece(double from, double to, double z)
    {
        From = from;
        To = to;
        Z = z;
    }

    public double From { get; }
    public double To { get; }
    public double Z { get; }
}

/// <summary>
/// Single-line MEP routes from CAD: the size is in the layer name (M-SAD-600x400, P-CWS-D50, DN100, Ø200); lines
/// of one system on layers that differ only by size are one network; ends meeting, and ends landing on another
/// run (tees), become nodes.
/// </summary>
public static class MepRoutePlanner
{
    public const double NodeTolMm = 50;
    /// <summary>Where the planner says why it read a layer as it did (null: nowhere).</summary>
    public static Action<string>? Trace { get; set; }
    private static readonly Regex Rect = new(@"(?<![\d.])(\d{2,4})\s*[xX×*]\s*(\d{2,4})(?![\d.])", RegexOptions.CultureInvariant);
    private static readonly Regex Round = new(@"(?:DN|[DØ∅ΦФ])\s*(\d{2,4})(?!\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParseSize(string? layer, out MepSize size)
    {
        size = default;
        var s = layer ?? "";
        var m = Rect.Match(s);
        if (m.Success)
        {
            size = new MepSize(double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value), 0);
            return true;
        }

        m = Round.Match(s);
        if (m.Success)
        {
            size = new MepSize(0, 0, double.Parse(m.Groups[1].Value));
            return true;
        }

        return false;
    }

    /// <summary>The layer name with its size taken out: layers of one system share it.</summary>
    public static string Family(string layer)
    {
        var flat = Rect.Replace(layer, "#");
        return Round.Replace(flat, "#").ToUpperInvariant();
    }

    /// <summary>A size label this far from a line (across it) belongs to it.</summary>
    public const double LabelReachMm = 800;
    /// <summary>Reading a whole link: shorter strokes are arrows, ticks and symbols, not runs.</summary>
    private const double MinRunInLinkMm = 150;

    /// <summary>
    /// Network from single lines. <paramref name="pickedLayer"/> null: every line of the CAD link (one link per system,
    /// whatever the consultant's layer names); otherwise the picked layer and the layers differing from it only by
    /// size. Size of each run: the size label written beside it (600x400, Ø200, D250, DN100), else the size in its
    /// layer name, else the size of the run it continues, else <paramref name="fallback"/>.
    /// <paramref name="doubleLine"/> (reading a whole link): true, ducts drawn with both sides (every layer read as
    /// pairs of sides); false, single lines only (pipes, trays); null, each layer read as it looks.
    /// </summary>
    public static MepNetwork Build(
        IEnumerable<CadEntity> entities, string? pickedLayer, MepSize fallback, IEnumerable<CadText>? texts = null, bool? doubleLine = null)
    {
        var family = pickedLayer is null ? null : Family(pickedLayer);
        var flexSkipped = 0;
        var segs = new List<(double X0, double Y0, double X1, double Y1, MepSize? Size, string From, string Layer, bool Arc)>();
        foreach (var e in entities)
        {
            if (family is not null && Family(e.Layer) != family)
                continue;
            MepSize? size = TryParseSize(e.Layer, out var s) ? s : null;
            var from = size is null ? "" : "layer";
            if (e is CadLine l)
                segs.Add((l.X0, l.Y0, l.X1, l.Y1, size, from, l.Layer, false));
            else if (e is CadPolyline p)
            {
                // A closed polyline read without its first point again (a duct drawn as a box) has its closing side.
                var n = p.Points.Count;
                var closing = p.Closed && n >= 3 &&
                              (Math.Abs(p.Points[0].X - p.Points[n - 1].X) > 1 || Math.Abs(p.Points[0].Y - p.Points[n - 1].Y) > 1);
                for (var i = 0; i < (closing ? n : n - 1); i++)
                {
                    var (a, b) = (p.Points[i], p.Points[(i + 1) % n]);
                    var bulge = p.Bulges is { } bs && i < bs.Count ? bs[i] : 0;
                    if (Math.Abs(bulge) < 1e-6)
                    {
                        segs.Add((a.X, a.Y, b.X, b.Y, size, from, p.Layer, p.FromArc));
                        continue;
                    }

                    // A round part (a bend drawn in the polyline): its arc in chords, as an arc read from Revit.
                    var chord = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
                    if (chord < 1e-6)
                        continue;
                    var sweep = 4 * Math.Atan(bulge);
                    var radius = chord / (2 * Math.Sin(Math.Abs(sweep) / 2));
                    double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                    var toCenter = radius * Math.Cos(sweep / 2) * Math.Sign(bulge);
                    double cx = mx - (b.Y - a.Y) / chord * toCenter, cy = my + (b.X - a.X) / chord * toCenter;
                    var start = Math.Atan2(a.Y - cy, a.X - cx);
                    var (px, py) = (a.X, a.Y);
                    for (var k = 1; k <= 8; k++)
                    {
                        var t = start + sweep * k / 8;
                        var (qx, qy) = k == 8 ? (b.X, b.Y) : (cx + radius * Math.Cos(t), cy + radius * Math.Sin(t));
                        segs.Add((px, py, qx, qy, size, from, p.Layer, true));
                        (px, py) = (qx, qy);
                    }
                }
            }
        }

        var minRun = family is null ? MinRunInLinkMm : NodeTolMm;
        var bends = new List<(double X, double Y, double RadiusMm)>();
        var joints = new List<(double X, double Y)>();
        var axisStart = int.MaxValue;
        var singleLine = false;
        // A whole link read with its linetypes comes as dashes: each line's dashes are one line again.
        if (family is null && doubleLine != false)
            segs = Undash(segs);
        // Every stroke, the short ones too (a flex duct's corrugations), by layer: what lies between two sides.
        var fine = segs.GroupBy(x => x.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => new Seg(x.X0, x.Y0, x.X1, x.Y1) { Arc = x.Arc }).Where(x => x.Length > 1).ToList(), StringComparer.OrdinalIgnoreCase);
        segs = segs.Where(x => Math.Sqrt(Sq(x.X1 - x.X0) + Sq(x.Y1 - x.Y0)) > minRun).ToList();
        var textList = (texts ?? Enumerable.Empty<CadText>()).ToList();
        var widths = segs.Select(_ => 0.0).ToList();
        if (family is null && doubleLine != false)
        {
            // Ducts drawn with both sides, layer by layer (a layer drawn single-line stays as drawn): each pair of
            // sides becomes the axis between them, the axes of all layers then joined at bends, tees and reducers.
            var labels = textList.Select(t => TryParseSize(t.Value, out var z) ? (t.CenterX ?? t.X, t.CenterY ?? t.Y, z) : ((double, double, MepSize)?)null)
                .Where(l => l is not null).Select(l => l!.Value).ToList();
            var kept = new List<(double X0, double Y0, double X1, double Y1, MepSize? Size, string From, string Layer, bool Arc)>();
            var keptWidths = new List<double>();
            var axes = new List<Seg>();
            foreach (var layer in segs.GroupBy(x => x.Layer, StringComparer.OrdinalIgnoreCase))
            {
                // A layer of flex ducts (FLEX, ỐNG MỀM) is left to the flex tool.
                if (Regex.IsMatch(Strip(layer.Key), "FLEX|ONG ?MEM|ONGMEM", RegexOptions.IgnoreCase))
                {
                    flexSkipped++;
                    continue;
                }

                var found = DoubleLines(layer.Select(x => new Seg(x.X0, x.Y0, x.X1, x.Y1) { Size = x.Size, From = x.From, Layer = x.Layer, Arc = x.Arc }).ToList(),
                    fine[layer.Key], labels, doubleLine == true, ref flexSkipped, joints);
                if (found is null)
                {
                    kept.AddRange(layer);
                    keptWidths.AddRange(layer.Select(_ => 0.0));
                }
                else
                    axes.AddRange(found);
            }

            if (axes.Count > 0)
            {
                // Where the bends are drawn round: the ends of every arc stroke.
                var arcEnds = new Buckets(CellMm);
                var arcPoints = fine.Values.SelectMany(f => f).Where(f => f.Arc).SelectMany(f => new[] { (f.X0, f.Y0), (f.X1, f.Y1) }).ToList();
                for (var k = 0; k < arcPoints.Count; k++)
                    arcEnds.Add(k, arcPoints[k].Item1, arcPoints[k].Item2, arcPoints[k].Item1, arcPoints[k].Item2);
                bool RoundNear(double x, double y, double reach) =>
                    arcEnds.Query(x - reach, y - reach, x + reach, y + reach).Any(k => Math.Sqrt(Sq(arcPoints[k].Item1 - x) + Sq(arcPoints[k].Item2 - y)) <= reach);
                WholeDegrees(axes);
                JoinAxisEnds(axes, RoundNear, bends);
                axisStart = kept.Count;
                kept.AddRange(axes.Select(a => (a.X0, a.Y0, a.X1, a.Y1, a.Size, a.Transition ? TransitionFrom : a.From, a.Layer, false)));
                keptWidths.AddRange(axes.Select(a => a.Width));
                segs = kept;
                widths = keptWidths;
            }
        }

        // A whole link read single-line (pipes): the lines are the runs, joined as two-line axes are (with no width,
        // within 300 mm): across the gap a valve's symbol leaves, to the corner of a bend drawn round, onto the main a
        // branch stops short of. The round parts of bends are left out.
        if (family is null && doubleLine == false && segs.Count > 0)
        {
            var lines = segs.Where(x => !x.Arc).Select(x => new Seg(x.X0, x.Y0, x.X1, x.Y1) { Size = x.Size, From = x.From, Layer = x.Layer }).ToList();
            WholeDegrees(lines);
            JoinAxisEnds(lines, (_, _, _) => false, bends);
            segs = lines.Select(a => (a.X0, a.Y0, a.X1, a.Y1, a.Size, a.From, a.Layer, false)).ToList();
            widths = segs.Select(_ => 0.0).ToList();
            singleLine = true;
        }

        // Tees: an end landing on another run splits that run there. Runs are looked up by plan cell (a whole
        // drawing has tens of thousands of strokes; comparing every pair hangs Revit).
        var cuts = segs.Select(_ => new List<double>()).ToList();
        var segCells = new Buckets(CellMm);
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            segCells.Add(i, Math.Min(s.X0, s.X1) - NodeTolMm, Math.Min(s.Y0, s.Y1) - NodeTolMm,
                Math.Max(s.X0, s.X1) + NodeTolMm, Math.Max(s.Y0, s.Y1) + NodeTolMm);
        }

        for (var i = 0; i < segs.Count; i++)
        {
            foreach (var (px, py) in new[] { (segs[i].X0, segs[i].Y0), (segs[i].X1, segs[i].Y1) })
            {
                foreach (var j in segCells.At(px, py))
                {
                    if (j == i)
                        continue;
                    var s = segs[j];
                    var len = Math.Sqrt(Sq(s.X1 - s.X0) + Sq(s.Y1 - s.Y0));
                    var t = ((px - s.X0) * (s.X1 - s.X0) + (py - s.Y0) * (s.Y1 - s.Y0)) / (len * len);
                    if (t * len <= NodeTolMm || (1 - t) * len <= NodeTolMm)
                        continue;
                    var dx = s.X0 + (s.X1 - s.X0) * t - px;
                    var dy = s.Y0 + (s.Y1 - s.Y0) * t - py;
                    if (Math.Sqrt(dx * dx + dy * dy) <= NodeTolMm)
                        cuts[j].Add(t);
                }
            }
        }

        // Two-line runs cut at their joints for now (a run labelled 600x250 above a joint and 600x200 below it changes
        // there); pieces of one size are made one again once sized.
        if (joints.Count > 0)
        {
            var jointCells = new Buckets(CellMm);
            for (var k = 0; k < joints.Count; k++)
                jointCells.Add(k, joints[k].X, joints[k].Y, joints[k].X, joints[k].Y);
            for (var i = axisStart; i < segs.Count; i++)
            {
                var s = segs[i];
                if (widths[i] <= 0 || s.From == TransitionFrom)
                    continue;
                var len = Math.Sqrt(Sq(s.X1 - s.X0) + Sq(s.Y1 - s.Y0));
                if (len < 1)
                    continue;
                double ux = (s.X1 - s.X0) / len, uy = (s.Y1 - s.Y0) / len;
                foreach (var k in jointCells.Query(Math.Min(s.X0, s.X1), Math.Min(s.Y0, s.Y1), Math.Max(s.X0, s.X1), Math.Max(s.Y0, s.Y1)))
                {
                    var (jx, jy) = joints[k];
                    var along = (jx - s.X0) * ux + (jy - s.Y0) * uy;
                    if (along <= NodeTolMm || along >= len - NodeTolMm || Math.Abs((jx - s.X0) * -uy + (jy - s.Y0) * ux) > NodeTolMm)
                        continue;
                    cuts[i].Add(along / len);
                }
            }
        }

        var net = new MepNetwork();
        // The first node (in order) within reach, as a scan of every node would find it.
        var nodeCells = new Buckets(NodeTolMm);
        int Node(double x, double y)
        {
            var found = -1;
            foreach (var k in nodeCells.Near(x, y))
            {
                if ((found < 0 || k < found) && Math.Sqrt(Sq(net.Nodes[k].X - x) + Sq(net.Nodes[k].Y - y)) <= NodeTolMm)
                    found = k;
            }

            if (found >= 0)
                return found;
            net.Nodes.Add((x, y));
            nodeCells.Add(net.Nodes.Count - 1, x, y, x, y);
            return net.Nodes.Count - 1;
        }

        var pairs = new HashSet<(int, int)>();
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            var ts = new[] { 0.0 }.Concat(cuts[i].Distinct().OrderBy(t => t)).Concat(new[] { 1.0 }).ToList();
            for (var k = 0; k + 1 < ts.Count; k++)
            {
                var a = Node(s.X0 + (s.X1 - s.X0) * ts[k], s.Y0 + (s.Y1 - s.Y0) * ts[k]);
                var b = Node(s.X0 + (s.X1 - s.X0) * ts[k + 1], s.Y0 + (s.Y1 - s.Y0) * ts[k + 1]);
                if (a != b && pairs.Add((Math.Min(a, b), Math.Max(a, b))))
                {
                    var transition = s.From == TransitionFrom;
                    net.Edges.Add(new MepEdge
                    {
                        A = a, B = b, Size = s.Size ?? default, SizeFrom = transition ? "" : s.From, Layer = s.Layer, DrawnWidthMm = widths[i],
                        IsTransition = transition
                    });
                }
            }
        }

        // Each size label goes to the run nearest to it (after the tees split the drawn lines).
        var edgeCells = new Buckets(CellMm);
        for (var i = 0; i < net.Edges.Count; i++)
        {
            var (x0, y0) = net.Nodes[net.Edges[i].A];
            var (x1, y1) = net.Nodes[net.Edges[i].B];
            var reach = LabelReachMm + net.Edges[i].DrawnWidthMm / 2 + 0.1 * Math.Sqrt(Sq(x1 - x0) + Sq(y1 - y0));
            edgeCells.Add(i, Math.Min(x0, x1) - reach, Math.Min(y0, y1) - reach, Math.Max(x0, x1) + reach, Math.Max(y0, y1) + reach);
        }

        // The label each edge took: how far its width is from the drawn one, then how far it is from the edge.
        var taken = new Dictionary<MepEdge, (double Misfit, double D)>();
        var labelAt = new Dictionary<MepEdge, (double X, double Y)>();
        foreach (var text in textList)
        {
            // A grille's or diffuser's label (EAG+OBD 450x450, SAG 1200x150) gives its face, not the duct's size.
            if (!TryParseSize(text.Value, out var size) || GrilleLabel.IsMatch(text.Value))
                continue;
            var tx = text.CenterX ?? text.X;
            var ty = text.CenterY ?? text.Y;
            MepEdge? best = null;
            var bestD = LabelReachMm;
            foreach (var e in edgeCells.At(tx, ty).Select(i => net.Edges[i]).Where(x => !x.IsTransition))
            {
                // A duct drawn with both sides takes only a label as wide as drawn (not the grille's 450x450 beside it).
                if (e.DrawnWidthMm > 0 && !Fits(size, e.DrawnWidthMm))
                    continue;
                var (x0, y0) = net.Nodes[e.A];
                var (x1, y1) = net.Nodes[e.B];
                var len = Math.Sqrt(Sq(x1 - x0) + Sq(y1 - y0));
                var t = ((tx - x0) * (x1 - x0) + (ty - y0) * (y1 - y0)) / (len * len);
                if (t < -0.1 || t > 1.1)
                    continue;
                // From the duct's side when drawn with both (a label beside a wide duct sits far from its axis), and
                // past its end by as far (the label of the duct round the corner is not this one's).
                var across = Math.Max(0, Math.Abs((tx - x0) * -(y1 - y0) / len + (ty - y0) * (x1 - x0) / len) - e.DrawnWidthMm / 2);
                var past = Math.Max(0, Math.Max(-t, t - 1)) * len;
                var d = Math.Sqrt(across * across + past * past);
                if (d >= bestD)
                    continue;
                bestD = d;
                best = e;
            }

            if (best is null)
                continue;
            // Two labels by one duct: the one as wide as drawn, then the nearer.
            var misfit = best.DrawnWidthMm > 0 ? Misfit(size, best.DrawnWidthMm) : 0;
            if (taken.TryGetValue(best, out var had) && (had.Misfit < misfit - 25 || had.Misfit <= misfit + 25 && had.D <= bestD))
                continue;
            taken[best] = (misfit, bestD);
            labelAt[best] = (tx, ty);
            best.Size = size;
            best.SizeFrom = "text";
        }

        SpreadAlongRuns(net, labelAt);

        // Runs with no label continue the run before them: through bends and joints, and straight on through a tee.
        var adjacent = Adjacency(net);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var e in net.Edges.Where(x => x.SizeFrom.Length == 0))
            {
                foreach (var node in new[] { e.A, e.B })
                {
                    var at = adjacent[node].Where(x => x != e).ToList();
                    var other = at.Count == 1 ? at[0] : at.FirstOrDefault(x => Straight(net, e, x, node));
                    if (other is null || other.SizeFrom.Length == 0)
                        continue;
                    e.Size = other.Size;
                    e.SizeFrom = "inherited";
                    changed = true;
                    break;
                }
            }
        }

        foreach (var e in net.Edges.Where(x => x.SizeFrom.Length == 0))
        {
            e.Size = fallback;
            e.SizeFrom = "default";
        }

        // A two-line duct with no label of its own is as wide as drawn (to 50 mm), as deep as the run it continues;
        // continuing a round one (D250), round at the nearest standard diameter.
        foreach (var e in net.Edges.Where(x => x.DrawnWidthMm > 0 && x.SizeFrom != "text"))
        {
            if (e.Size.IsRound)
            {
                var drawnWidth = e.DrawnWidthMm;
                e.Size = new MepSize(0, 0, RoundSizes.OrderBy(d => Math.Abs(d - drawnWidth)).First());
                e.SizeFrom = "drawn";
                continue;
            }

            var depth = e.Size.IsRound ? e.Size.DiameterMm : e.Size.HeightMm;
            e.Size = new MepSize(Math.Max(50, Math.Round(e.DrawnWidthMm / 50) * 50), depth > 0 ? depth : Math.Max(100, fallback.DepthMm), 0);
            e.SizeFrom = "drawn";
        }

        if (axisStart != int.MaxValue || singleLine)
            MergeStraight(net, singleLine);
        Falls(net, textList);
        net.OpeningsSkipped = DropOpenings(net);
        net.FlexSkipped = flexSkipped;
        net.Bends.AddRange(bends);
        return net;
    }

    private static readonly Regex FallLabel = new(@"\bi\s*=\s*(\d+(?:[.,]\d+)?)\s*%", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The fall written on a run (i=1%, i = 0,5%) goes to the run nearest to it, then on through plain joints (a node
    /// of two runs) to the runs after it that have none of their own.
    /// </summary>
    private static void Falls(MepNetwork net, List<CadText> texts)
    {
        foreach (var text in texts)
        {
            var m = FallLabel.Match(text.Value);
            if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var percent) || percent <= 0 || percent > 20)
                continue;
            var tx = text.CenterX ?? text.X;
            var ty = text.CenterY ?? text.Y;
            MepEdge? best = null;
            var bestD = LabelReachMm;
            foreach (var e in net.Edges.Where(x => !x.IsTransition))
            {
                var (x0, y0) = net.Nodes[e.A];
                var (x1, y1) = net.Nodes[e.B];
                var len = Math.Sqrt(Sq(x1 - x0) + Sq(y1 - y0));
                if (len < 1)
                    continue;
                var t = Math.Max(0, Math.Min(1, ((tx - x0) * (x1 - x0) + (ty - y0) * (y1 - y0)) / (len * len)));
                var d = Math.Max(0, Math.Sqrt(Sq(x0 + (x1 - x0) * t - tx) + Sq(y0 + (y1 - y0) * t - ty)) - e.DrawnWidthMm / 2);
                if (d >= bestD)
                    continue;
                bestD = d;
                best = e;
            }

            if (best is not null)
                best.SlopePercent = percent;
        }

        var adjacent = Adjacency(net);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var e in net.Edges.Where(x => x.SlopePercent is null))
            {
                foreach (var node in new[] { e.A, e.B })
                {
                    var at = adjacent[node].Where(x => x != e).ToList();
                    if (at.Count != 1 || at[0].SlopePercent is not { } carried)
                        continue;
                    e.SlopePercent = carried;
                    changed = true;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// How far each node a network reaches stands above <paramref name="outlet"/> (mm): along the shortest way there,
    /// each run falling by its own written fall, else <paramref name="slopePercent"/>.
    /// </summary>
    public static Dictionary<int, double> RisesFrom(MepNetwork net, int outlet, double slopePercent)
    {
        var adjacent = Adjacency(net);
        var dist = new Dictionary<int, double> { [outlet] = 0 };
        var rise = new Dictionary<int, double> { [outlet] = 0 };
        var open = new SortedSet<(double D, int N)> { (0, outlet) };
        while (open.Count > 0)
        {
            var (dn, n) = open.Min;
            open.Remove(open.Min);
            foreach (var e in adjacent[n])
            {
                var m = e.A == n ? e.B : e.A;
                var len = Length(net, e);
                var d = dn + len;
                if (dist.TryGetValue(m, out var known))
                {
                    if (known <= d)
                        continue;
                    open.Remove((known, m));
                }

                dist[m] = d;
                rise[m] = rise[n] + (e.SlopePercent ?? slopePercent) / 100 * len;
                open.Add((d, m));
            }
        }

        return rise;
    }

    /// <summary>
    /// Along each straight two-line run (pieces in line, as wide as each other), a piece with no label of its own takes
    /// the size of the label nearest to it on that run: the size changes at the joint between two labels.
    /// </summary>
    private static void SpreadAlongRuns(MepNetwork net, Dictionary<MepEdge, (double X, double Y)> labelAt)
    {
        var adjacent = Adjacency(net);
        var run = new Dictionary<MepEdge, int>();
        var runs = new List<List<MepEdge>>();
        foreach (var start in net.Edges.Where(e => e.DrawnWidthMm > 0 && !e.IsTransition))
        {
            if (run.ContainsKey(start))
                continue;
            var members = new List<MepEdge>();
            var stack = new Stack<MepEdge>();
            stack.Push(start);
            run[start] = runs.Count;
            while (stack.Count > 0)
            {
                var e = stack.Pop();
                members.Add(e);
                foreach (var node in new[] { e.A, e.B })
                {
                    foreach (var x in adjacent[node])
                    {
                        if (x == e || run.ContainsKey(x) || x.IsTransition || x.DrawnWidthMm <= 0 ||
                            Math.Abs(x.DrawnWidthMm - e.DrawnWidthMm) > 25 || !Straight(net, e, x, node))
                            continue;
                        run[x] = runs.Count;
                        stack.Push(x);
                    }
                }
            }

            runs.Add(members);
        }

        foreach (var members in runs)
        {
            var labelled = members.Where(e => e.SizeFrom == "text" && labelAt.ContainsKey(e)).ToList();
            if (labelled.Count == 0)
                continue;
            foreach (var e in members.Where(e => e.SizeFrom.Length == 0))
            {
                var (ax, ay) = net.Nodes[e.A];
                var (bx, by) = net.Nodes[e.B];
                double mx = (ax + bx) / 2, my = (ay + by) / 2;
                var nearest = labelled.OrderBy(l => Sq(labelAt[l].X - mx) + Sq(labelAt[l].Y - my)).First();
                e.Size = nearest.Size;
                e.SizeFrom = "inherited";
            }
        }
    }

    /// <summary>Pieces of one straight two-line run with one size made one edge again (the run cut at its joints).</summary>
    private static void MergeStraight(MepNetwork net, bool singleLine = false)
    {
        var merged = true;
        while (merged)
        {
            merged = false;
            var adjacent = Adjacency(net);
            for (var n = 0; n < net.Nodes.Count && !merged; n++)
            {
                if (adjacent[n].Count != 2)
                    continue;
                var (a, b) = (adjacent[n][0], adjacent[n][1]);
                if (a.IsTransition || b.IsTransition || !singleLine && (a.DrawnWidthMm <= 0 || b.DrawnWidthMm <= 0) ||
                    !a.Size.Equals(b.Size) || !Straight(net, a, b, n))
                    continue;
                var far = b.A == n ? b.B : b.A;
                if (a.A == n)
                    a.A = far;
                else
                    a.B = far;
                if (a.SizeFrom != "text" && b.SizeFrom == "text")
                    a.SizeFrom = "text";
                net.Edges.Remove(b);
                merged = true;
            }
        }
    }

    private const string TransitionFrom = "transition";

    /// <summary>Gap between dashes (and the dots between them) of one dashed line, joined back.</summary>
    public const double DashGapMm = 400;

    /// <summary>
    /// Collinear strokes of one layer with gaps up to <see cref="DashGapMm"/> joined into one line: a dash-dot
    /// line read as its dashes. Strokes meeting end to end stay apart; arcs stay as they are.
    /// </summary>
    private static List<(double X0, double Y0, double X1, double Y1, MepSize? Size, string From, string Layer, bool Arc)> Undash(
        List<(double X0, double Y0, double X1, double Y1, MepSize? Size, string From, string Layer, bool Arc)> segs)
    {
        var result = segs.Where(x => x.Arc).ToList();
        var lines = segs.Where(x => !x.Arc && Math.Sqrt(Sq(x.X1 - x.X0) + Sq(x.Y1 - x.Y0)) > 1)
            .Select(x =>
            {
                // Direction in [-45°, 135°): a line and its reverse alike.
                var a = Math.Atan2(x.Y1 - x.Y0, x.X1 - x.X0);
                while (a < -Math.PI / 4) a += Math.PI;
                while (a >= 3 * Math.PI / 4) a -= Math.PI;
                var c = -Math.Sin(a) * x.X0 + Math.Cos(a) * x.Y0;
                return (Seg: x, A: a, C: c);
            })
            .OrderBy(x => x.Seg.Layer, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.A).ToList();
        var k = 0;
        while (k < lines.Count)
        {
            // Same layer, same direction (within 0.3°)...
            var start = k;
            while (k + 1 < lines.Count && string.Equals(lines[k + 1].Seg.Layer, lines[start].Seg.Layer, StringComparison.OrdinalIgnoreCase) &&
                   lines[k + 1].A - lines[k].A < 0.005)
                k++;
            var group = lines.GetRange(start, k - start + 1).OrderBy(x => x.C).ToList();
            k++;
            // ... and on one line (within 3 mm across: a dash lies on its line; a line 25 mm off is another).
            var g = 0;
            while (g < group.Count)
            {
                var from = g;
                while (g + 1 < group.Count && group[g + 1].C - group[from].C < 3)
                    g++;
                var run = group.GetRange(from, g - from + 1);
                g++;
                // Along the run's direction; ends kept where drawn (a direction averaged over far-off coordinates
                // would move the line).
                var a = run.Average(x => x.A);
                double ux = Math.Cos(a), uy = Math.Sin(a);
                var spans = run.Select(x =>
                {
                    var t0 = x.Seg.X0 * ux + x.Seg.Y0 * uy;
                    var t1 = x.Seg.X1 * ux + x.Seg.Y1 * uy;
                    return t0 <= t1
                        ? (T0: t0, T1: t1, P0: (x.Seg.X0, x.Seg.Y0), P1: (x.Seg.X1, x.Seg.Y1), x.Seg)
                        : (T0: t1, T1: t0, P0: (x.Seg.X1, x.Seg.Y1), P1: (x.Seg.X0, x.Seg.Y0), x.Seg);
                }).OrderBy(x => x.T0).ToList();
                var (lo, hi, p0, p1, first) = spans[0];
                void Emit() => result.Add((p0.Item1, p0.Item2, p1.Item1, p1.Item2, first.Size, first.From, first.Layer, false));
                foreach (var span in spans.Skip(1))
                {
                    // A gap is a dash's; lines meeting end to end (a run starting where another stops) stay apart.
                    if (span.T0 > hi + 1 && span.T0 <= hi + DashGapMm)
                    {
                        if (span.T1 > hi)
                            (hi, p1) = (span.T1, span.P1);
                        continue;
                    }

                    Emit();
                    (lo, hi, p0, p1, first) = span;
                }

                Emit();
            }
        }

        return result;
    }

    /// <summary>
    /// Largest device drawn as a block (grille, diffuser, fan coil, VAV box, a 3000 louver with its damper): a bigger
    /// block draws runs. Past 3000 so a 3000 louver read a hair over it still counts.
    /// </summary>
    public const double MaxDeviceMm = 3200;
    /// <summary>Largest closed outline taken for a device drawn without a block (a grille drawn as a square).</summary>
    private const double MaxOutlineMm = 1500;
    /// <summary>A flex collar drawn on a box's side: at most this deep and this wide.</summary>
    private const double CollarDepthMm = 150;
    private const double CollarMaxMm = 600;

    /// <summary>
    /// The strokes of a duct drawing without its devices: small blocks (grilles, diffusers, fan coils, boxes), small
    /// squarish closed outlines, and the strokes inside those (a grille's cross). Devices are placed by their own tool
    /// and joined with flex; their outlines are not runs. <paramref name="devices"/>: how many were left out.
    /// </summary>
    public static List<CadEntity> WithoutDevices(IEnumerable<CadEntity> entities, out int devices)
    {
        var list = entities.ToList();
        var blocks = new Dictionary<int, (double MinX, double MinY, double MaxX, double MaxY)>();
        foreach (var e in list.Where(x => x.BlockRef != 0))
        {
            foreach (var (x, y) in Points(e))
            {
                blocks[e.BlockRef] = blocks.TryGetValue(e.BlockRef, out var b)
                    ? (Math.Min(b.MinX, x), Math.Min(b.MinY, y), Math.Max(b.MaxX, x), Math.Max(b.MaxY, y))
                    : (x, y, x, y);
            }
        }

        var small = new HashSet<int>(blocks.Where(b => Math.Max(b.Value.MaxX - b.Value.MinX, b.Value.MaxY - b.Value.MinY) <= MaxDeviceMm)
            .Select(b => b.Key));
        var drop = new bool[list.Count];
        var outlines = new List<(double MinX, double MinY, double MaxX, double MaxY)>();
        var closed = list.Select((e, i) => (e, i)).Where(x => x.e is CadPolyline { Closed: true, FromArc: false } p && p.Points.Count >= 4)
            .Select(x =>
            {
                var p = (CadPolyline)x.e;
                return (I: x.i, MinX: p.Points.Min(q => q.X), MinY: p.Points.Min(q => q.Y), MaxX: p.Points.Max(q => q.X), MaxY: p.Points.Max(q => q.Y));
            })
            .ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].BlockRef != 0 && small.Contains(list[i].BlockRef))
                drop[i] = true;
        }

        // Every corner of every stroke, to tell a piece of duct drawn as a box (its corners shared with the bend, the
        // reducer or the box next to it) from a grille.
        var corners = new Buckets(CellMm);
        var cornerList = new List<(double X, double Y, int I)>();
        for (var i = 0; i < list.Count; i++)
        {
            foreach (var (x, y) in Points(list[i]))
            {
                cornerList.Add((x, y, i));
                corners.Add(cornerList.Count - 1, x, y, x, y);
            }
        }

        bool Shared(int self, double x, double y, (double MinX, double MinY, double MaxX, double MaxY) box) =>
            corners.Query(x - 5, y - 5, x + 5, y + 5).Select(k => cornerList[k]).Any(q =>
                q.I != self && Math.Abs(q.X - x) <= 5 && Math.Abs(q.Y - y) <= 5 &&
                Points(list[q.I]).Any(p => p.X < box.MinX - 20 || p.X > box.MaxX + 20 || p.Y < box.MinY - 20 || p.Y > box.MaxY + 20));

        foreach (var c in closed)
        {
            var big = Math.Max(c.MaxX - c.MinX, c.MaxY - c.MinY);
            if (big > MaxOutlineMm)
                continue;
            // A collar (spigot for a flex) on the side of a box drawn bigger: thin, short, outside the box but touching it.
            var thin = Math.Min(c.MaxX - c.MinX, c.MaxY - c.MinY);
            if (thin <= CollarDepthMm && big <= CollarMaxMm && closed.Any(o => o.I != c.I &&
                    (o.MaxX - o.MinX) * (o.MaxY - o.MinY) > 3 * big * thin &&
                    o.MinX <= c.MaxX + 5 && o.MaxX >= c.MinX - 5 && o.MinY <= c.MaxY + 5 && o.MaxY >= c.MinY - 5 &&
                    !(o.MinX <= c.MinX + 5 && o.MaxX >= c.MaxX - 5 && o.MinY <= c.MinY + 5 && o.MaxY >= c.MaxY - 5)))
            {
                drop[c.I] = true;
                outlines.Add((c.MinX, c.MinY, c.MaxX, c.MaxY));
                continue;
            }

            // A piece of duct between two others: most of its corners are theirs too.
            if (((CadPolyline)list[c.I]).Points.Count(q => Shared(c.I, q.X, q.Y, (c.MinX, c.MinY, c.MaxX, c.MaxY))) >= 3)
                continue;
            // Squarish (a grille), or drawn inside a bigger closed outline (a damper inside a duct drawn as a box).
            if (big > 2.5 * Math.Min(c.MaxX - c.MinX, c.MaxY - c.MinY) &&
                !closed.Any(o => o.I != c.I && o.MinX <= c.MinX + 20 && o.MinY <= c.MinY + 20 && o.MaxX >= c.MaxX - 20 && o.MaxY >= c.MaxY - 20 &&
                                 (o.MaxX - o.MinX) * (o.MaxY - o.MinY) > (c.MaxX - c.MinX) * (c.MaxY - c.MinY) * 1.2))
                continue;
            drop[c.I] = true;
            outlines.Add((c.MinX, c.MinY, c.MaxX, c.MaxY));
        }

        devices = small.Count + outlines.Count;
        if (outlines.Count > 0)
        {
            var cells = new Buckets(CellMm);
            for (var k = 0; k < outlines.Count; k++)
                cells.Add(k, outlines[k].MinX, outlines[k].MinY, outlines[k].MaxX, outlines[k].MaxY);
            for (var i = 0; i < list.Count; i++)
            {
                if (drop[i])
                    continue;
                var pts = Points(list[i]).ToList();
                if (pts.Count == 0)
                    continue;
                drop[i] = cells.At(pts[0].X, pts[0].Y).Select(k => outlines[k])
                    .Any(o => pts.All(q => q.X >= o.MinX - 20 && q.X <= o.MaxX + 20 && q.Y >= o.MinY - 20 && q.Y <= o.MaxY + 20));
            }
        }

        return list.Where((_, i) => !drop[i]).ToList();
    }

    private static IEnumerable<(double X, double Y)> Points(CadEntity e) => e switch
    {
        CadLine l => new[] { (l.X0, l.Y0), (l.X1, l.Y1) },
        CadPolyline p => p.Points.Select(q => (q.X, q.Y)),
        CadCircle c => new[] { (c.Cx - c.Radius, c.Cy - c.Radius), (c.Cx + c.Radius, c.Cy + c.Radius) },
        _ => Enumerable.Empty<(double, double)>()
    };

    /// <summary>A stroke being worked on: its ends, and once paired, the width between the two sides.</summary>
    private sealed class Seg
    {
        public Seg(double x0, double y0, double x1, double y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public double X0, Y0, X1, Y1;
        public MepSize? Size;
        public string From = "";
        public string Layer = "";
        public bool Arc;
        public double Width;
        /// <summary>An off-centre reducer between two axis ends (see <see cref="MepEdge.IsTransition"/>).</summary>
        public bool Transition;
        public double Length => Math.Sqrt(Sq(X1 - X0) + Sq(Y1 - Y0));
        public (double X, double Y) U => ((X1 - X0) / Length, (Y1 - Y0) / Length);
    }

    public const double MinDuctWidthMm = 80;
    public const double MaxDuctWidthMm = 2500;
    /// <summary>sin 1.5°: sides of one duct are drawn parallel.</summary>
    private const double ParallelSin = 0.026;
    private const double MinSideMm = 200;

    /// <summary>
    /// The axes (not yet joined) of the ducts of one layer drawn with both sides, or null when the layer is drawn
    /// single-line. Bends drawn round are left out (the axes run on to the corner), and so are the joints and fitting
    /// ends drawn across a duct from side to side. Unless <paramref name="sure"/> (a duct layer), the layer counts as
    /// two-line when most of its length pairs and the labels inside the bands (when there are a few) mostly agree with
    /// their widths.
    /// </summary>
    private static List<Seg>? DoubleLines(List<Seg> strokes, List<Seg> fine, List<(double X, double Y, MepSize Size)> labels, bool sure,
        ref int flexSkipped, List<(double X, double Y)> joints)
    {
        var segs = strokes.Where(s => !s.Arc && s.Length >= MinSideMm).ToList();
        // A hatch's pattern lines (the cross-hatch of a hopper, a filter) are not sides.
        var hatch = HatchLines(segs);
        if (hatch.Count > 0)
            segs = segs.Where((_, i) => !hatch.Contains(i)).ToList();
        if (segs.Count < 2)
            return sure ? new List<Seg>() : null;
        var labelCells = new Buckets(CellMm);
        for (var k = 0; k < labels.Count; k++)
            labelCells.Add(k, labels[k].X, labels[k].Y, labels[k].X, labels[k].Y);
        // The centerline drawn between the sides is not a side.
        var centers = CenterLines(segs, labelCells, labels);
        if (centers.Count > 0)
            segs = segs.Where((_, i) => !centers.Contains(i)).ToList();
        // Pieces drawn as boxes share their end faces (the box's side over the bend's or the next box's end): drawn
        // twice, a joint, not a side.
        var faces = Faces(segs);
        // So is the line across a round bend's mouth, from the end of its inner arc to the end of its outer one (square
        // to both; a straight side between two bends runs on along them).
        var arcEnds = new Buckets(CellMm);
        var arcPoints = fine.Where(s => s.Arc)
            .SelectMany(s => new[] { (X: s.X0, Y: s.Y0, s.U), (X: s.X1, Y: s.Y1, s.U) }).ToList();
        for (var k = 0; k < arcPoints.Count; k++)
            arcEnds.Add(k, arcPoints[k].X, arcPoints[k].Y, arcPoints[k].X, arcPoints[k].Y);
        bool Mouth(Seg a, double x, double y) => arcEnds.Query(x - 5, y - 5, x + 5, y + 5)
            .Any(k => Math.Abs(arcPoints[k].X - x) <= 5 && Math.Abs(arcPoints[k].Y - y) <= 5 &&
                      Math.Abs(arcPoints[k].U.X * a.U.X + arcPoints[k].U.Y * a.U.Y) < 0.5);
        for (var i = 0; i < segs.Count; i++)
        {
            if (segs[i].Length <= MaxDuctWidthMm + 50 && Mouth(segs[i], segs[i].X0, segs[i].Y0) && Mouth(segs[i], segs[i].X1, segs[i].Y1))
                faces.Add(i);
        }

        var faceList = faces.Select(k => segs[k]).ToList();
        if (faces.Count > 0)
            segs = segs.Where((_, i) => !faces.Contains(i)).ToList();
        var strokeCells = new Buckets(CellMm);
        for (var k = 0; k < fine.Count; k++)
            strokeCells.Add(k, Math.Min(fine[k].X0, fine[k].X1), Math.Min(fine[k].Y0, fine[k].Y1), Math.Max(fine[k].X0, fine[k].X1), Math.Max(fine[k].Y0, fine[k].Y1));
        bool Glued(Seg a, Seg b) => Hanging(fine, strokeCells, a, b) || Hanging(fine, strokeCells, b, a);
        var pairs = SidePairs(segs, labelCells, labels, Glued);
        var across = AcrossPairs(segs, pairs);
        // Joints (a line across the duct, a face two boxes share): where a run may change size.
        joints.AddRange(across.Select(k => ((segs[k].X0 + segs[k].X1) / 2, (segs[k].Y0 + segs[k].Y1) / 2)));
        joints.AddRange(faceList.Select(f => ((f.X0 + f.X1) / 2, (f.Y0 + f.Y1) / 2)));
        if (across.Count > 0)
        {
            segs = segs.Where((_, i) => !across.Contains(i)).ToList();
            pairs = SidePairs(segs, labelCells, labels, Glued);
        }

        var total = segs.Sum(x => x.Length);
        var paired = pairs.SelectMany(p => new[] { p.I, p.J }).Distinct().Sum(i => segs[i].Length);
        int labelled = 0, agree = 0;
        foreach (var (i, _, d, s0, s1) in pairs)
        {
            if (LabelsIn(labelCells, labels, segs[i], d, s0, s1, out var ok) > 0)
            {
                labelled++;
                if (ok > 0)
                    agree++;
            }
        }

        // Single-line drawings have parallel runs too: two-line needs most length paired, and the labels inside the
        // bands (when there are a few) mostly as wide as their band. Ducts (sure) are drawn with both sides anyway.
        Trace?.Invoke($"two-line {strokes.FirstOrDefault()?.Layer}: {segs.Count} sides, paired {paired:0}/{total:0}, labels {agree}/{labelled}");
        if (!sure && (total <= 0 || paired < 0.5 * total || (labelled >= 3 ? agree < 0.5 * labelled : paired < 0.7 * total)))
            return null;

        var fineCells = new Buckets(CellMm);
        for (var k = 0; k < fine.Count; k++)
            fineCells.Add(k, (fine[k].X0 + fine[k].X1) / 2, (fine[k].Y0 + fine[k].Y1) / 2, (fine[k].X0 + fine[k].X1) / 2, (fine[k].Y0 + fine[k].Y1) / 2);
        var axes = new List<Seg>();
        bool Capped(Seg a, double d, double s)
        {
            var (ux, uy) = a.U;
            double px = a.X0 + ux * s, py = a.Y0 + uy * s, qx = px - uy * d, qy = py + ux * d;
            bool Near(double x0, double y0, double x1, double y1) => Math.Abs(x0 - x1) <= 10 && Math.Abs(y0 - y1) <= 10;
            return faceList.Any(f => Near(f.X0, f.Y0, px, py) && Near(f.X1, f.Y1, qx, qy) || Near(f.X1, f.Y1, px, py) && Near(f.X0, f.Y0, qx, qy));
        }

        foreach (var (i, j, d, s0, s1) in pairs)
        {
            // Shorter than its width: a scrap of symbol between two lines (a damper's or a fan box's), not a run;
            // unless a joint face shared with the pieces either side closes both ends (a short box between two bends).
            var stub = s1 - s0 >= 100 && Capped(segs[i], d, s0) && Capped(segs[i], d, s1);
            if (s1 - s0 < Math.Max(MinSideMm, Math.Abs(d)) && !stub)
                continue;
            // The two bases of a transition (a hopper, a reducer drawn as a trapezoid): slanted legs join their ends.
            if (Legs(fine, strokeCells, segs[i], segs[j]))
                continue;
            // Strokes across the band every few cm: a flex duct's corrugations, left to the flex tool.
            // Strokes across the band every few cm: a flex duct's corrugations. That stretch is left to the flex tool,
            // the duct on either side of it kept.
            var stretches = new List<(double From, double To)> { (s0, s1) };
            var at = Crossings(fine, fineCells, segs[i], d, s0, s1);
            if (at.Count >= 10)
            {
                var flex = Dense(at);
                if (flex is { } f)
                {
                    flexSkipped++;
                    stretches = new List<(double From, double To)> { (s0, f.From), (f.To, s1) };
                }
            }

            var a = segs[i];
            var other = segs[j];
            var (ux, uy) = a.U;
            double nx = -uy * d / 2, ny = ux * d / 2;
            foreach (var (from, to) in stretches.Where(x => stub || x.To - x.From >= Math.Max(MinSideMm, Math.Abs(d))))
            {
                axes.Add(new Seg(a.X0 + ux * from + nx, a.Y0 + uy * from + ny, a.X0 + ux * to + nx, a.Y0 + uy * to + ny)
                {
                    Width = Math.Abs(d),
                    Size = a.Size ?? other.Size,
                    From = a.Size is not null ? a.From : other.From,
                    Layer = a.Layer
                });
            }
        }

        return axes;
    }

    /// <summary>
    /// Line b, shorter, hangs off the middle of line a: each end joined to a by a stroke landing between a's ends (a
    /// branch's shoe or a tap drawn on the duct's side), so the two are not the sides of one duct.
    /// </summary>
    private static bool Hanging(List<Seg> fine, Buckets cells, Seg a, Seg b)
    {
        if (b.Length >= a.Length)
            return false;
        var (ux, uy) = a.U;
        bool Linked(double px, double py)
        {
            foreach (var k in cells.At(px, py))
            {
                var f = fine[k];
                if (Math.Abs(f.U.X * uy - f.U.Y * ux) < 0.2)
                    continue;
                // Through b's end (a leg may run on as the branch's side), one end on a short of a's ends.
                if (ToSeg(f, px, py) > NodeTolMm)
                    continue;
                foreach (var (ox, oy) in new[] { (f.X0, f.Y0), (f.X1, f.Y1) })
                {
                    var t = (ox - a.X0) * ux + (oy - a.Y0) * uy;
                    var off = Math.Abs((ox - a.X0) * -uy + (oy - a.Y0) * ux);
                    if (off <= NodeTolMm && t > NodeTolMm && t < a.Length - NodeTolMm)
                        return true;
                }
            }

            return false;
        }

        return Linked(b.X0, b.Y0) && Linked(b.X1, b.Y1);
    }

    /// <summary>Both pairs of ends of lines a and b joined by a slanted stroke: a trapezoid, not a duct.</summary>
    private static bool Legs(List<Seg> fine, Buckets cells, Seg a, Seg b)
    {
        bool Near((double X, double Y) p, double x, double y) => Math.Abs(p.X - x) <= NodeTolMm && Math.Abs(p.Y - y) <= NodeTolMm;
        bool Leg((double X, double Y) p, (double X, double Y) q)
        {
            foreach (var k in cells.At(p.X, p.Y))
            {
                var f = fine[k];
                if (Math.Abs(f.U.X * a.U.X + f.U.Y * a.U.Y) < 0.2 || Math.Abs(f.U.X * a.U.X + f.U.Y * a.U.Y) > 0.97)
                    continue;
                if (Near(p, f.X0, f.Y0) && Near(q, f.X1, f.Y1) || Near(p, f.X1, f.Y1) && Near(q, f.X0, f.Y0))
                    return true;
            }

            return false;
        }

        // b's ends in a's direction: the one at a's start first.
        var flip = b.U.X * a.U.X + b.U.Y * a.U.Y < 0;
        (double X, double Y) b0 = flip ? (b.X1, b.Y1) : (b.X0, b.Y0), b1 = flip ? (b.X0, b.Y0) : (b.X1, b.Y1);
        return Leg((a.X0, a.Y0), b0) && Leg((a.X1, a.Y1), b1);
    }

    /// <summary>Strokes not along side line <paramref name="a"/> whose middle lies inside its band out to offset d.</summary>
    /// <summary>The longest stretch where crossings come at least every 100 mm, at least 10 of them; null if none.</summary>
    private static (double From, double To)? Dense(List<double> at)
    {
        at.Sort();
        (double From, double To)? best = null;
        var start = 0;
        for (var k = 1; k <= at.Count; k++)
        {
            if (k < at.Count && at[k] - at[k - 1] <= 100)
                continue;
            if (k - start >= 10 && (best is null || at[k - 1] - at[start] > best.Value.To - best.Value.From))
                best = (at[start], at[k - 1]);
            start = k;
        }

        return best;
    }

    private static List<double> Crossings(List<Seg> fine, Buckets cells, Seg a, double d, double s0, double s1)
    {
        var (ux, uy) = a.U;
        double nx = -uy, ny = ux;
        double lo = Math.Min(0, d) + 0.1 * Math.Abs(d), hi = Math.Max(0, d) - 0.1 * Math.Abs(d);
        var found = new List<double>();
        foreach (var k in cells.Query(Math.Min(a.X0, a.X1) - MaxDuctWidthMm, Math.Min(a.Y0, a.Y1) - MaxDuctWidthMm,
                     Math.Max(a.X0, a.X1) + MaxDuctWidthMm, Math.Max(a.Y0, a.Y1) + MaxDuctWidthMm))
        {
            var f = fine[k];
            if (Math.Abs(ux * f.U.Y - uy * f.U.X) < 0.26)
                continue;
            double mx = (f.X0 + f.X1) / 2 - a.X0, my = (f.Y0 + f.Y1) / 2 - a.Y0;
            var along = mx * ux + my * uy;
            var across = mx * nx + my * ny;
            if (along >= s0 && along <= s1 && across >= lo && across <= hi)
                found.Add(along);
        }

        return found;
    }

    /// <summary>Upper case without Vietnamese marks (ỐNG MỀM → ONG MEM).</summary>
    private static string Strip(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in text.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c == 'đ' || c == 'Đ' ? 'D' : c);
        }

        return sb.ToString().ToUpperInvariant();
    }

    private static readonly double[] RoundSizes = { 80, 100, 125, 150, 160, 200, 250, 300, 315, 350, 400, 450, 500, 550, 600, 650, 700, 800, 900, 1000, 1250 };

    // Also a box's or equipment's three sizes (W D H: 850X400X300, 500X500X800): not a duct's section.
    private static readonly Regex GrilleLabel = new(@"\b[A-Z]{0,2}AG\b|GRILLE|LOUVER|LOUVRE|DIFFUSER|\d\s*[xX×*]\s*\d+\s*[xX×*]\s*\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>How far a label's width (the nearer of its two sides, or its diameter) is from the drawn width.</summary>
    private static double Misfit(MepSize size, double w) =>
        size.IsRound ? Math.Abs(size.DiameterMm - w) : Math.Min(Math.Abs(size.WidthMm - w), Math.Abs(size.HeightMm - w));

    /// <summary>A label as wide as a duct drawn <paramref name="w"/> mm across: W or H of a rectangle, D of a round one.</summary>
    private static bool Fits(MepSize size, double w)
    {
        var tol = Math.Max(60, 0.25 * w);
        return size.IsRound ? Math.Abs(size.DiameterMm - w) <= tol
            : Math.Abs(size.WidthMm - w) <= tol || Math.Abs(size.HeightMm - w) <= tol;
    }

    /// <summary>Size labels in the band of side line <paramref name="a"/> out to offset d (along the overlap, a little past it).</summary>
    private static int LabelsIn(Buckets labelCells, List<(double X, double Y, MepSize Size)> labels, Seg a, double d, double s0, double s1, out int agreeing)
    {
        agreeing = 0;
        var (ux, uy) = a.U;
        double nx = -uy, ny = ux;
        var found = 0;
        var lo = Math.Min(0, d);
        var hi = Math.Max(0, d);
        foreach (var k in labelCells.Query(Math.Min(a.X0, a.X1) - MaxDuctWidthMm, Math.Min(a.Y0, a.Y1) - MaxDuctWidthMm,
                     Math.Max(a.X0, a.X1) + MaxDuctWidthMm, Math.Max(a.Y0, a.Y1) + MaxDuctWidthMm))
        {
            var (lx, ly, size) = labels[k];
            var along = (lx - a.X0) * ux + (ly - a.Y0) * uy;
            var across = (lx - a.X0) * nx + (ly - a.Y0) * ny;
            if (along < s0 - 300 || along > s1 + 300 || across < lo || across > hi)
                continue;
            found++;
            if (Fits(size, Math.Abs(d)))
                agreeing++;
        }

        return found;
    }

    /// <summary>
    /// The two sides of each duct: (line, partner, signed offset of the partner, overlap along the line). A line pairs
    /// with the nearest parallel line 80–2500 mm off along each stretch, only when it is the nearest for that line too.
    /// A line is the side of one duct: with partners on both sides, a side whose partners all pair the other way is
    /// taken; still open, the side whose band holds a label as wide as the band wins, else the nearer one (two ducts
    /// side by side are not one wide duct, nor is the gap between them).
    /// </summary>
    private static List<(int I, int J, double D, double S0, double S1)> SidePairs(
        List<Seg> segs, Buckets labelCells, List<(double X, double Y, MepSize Size)> labels, Func<Seg, Seg, bool> glued)
    {
        var cells = new Buckets(CellMm);
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            cells.Add(i, Math.Min(s.X0, s.X1) - MaxDuctWidthMm, Math.Min(s.Y0, s.Y1) - MaxDuctWidthMm,
                Math.Max(s.X0, s.X1) + MaxDuctWidthMm, Math.Max(s.Y0, s.Y1) + MaxDuctWidthMm);
        }

        var near = new List<(int J, double D, double S0, double S1)>[segs.Count];
        for (var i = 0; i < segs.Count; i++)
        {
            var a = segs[i];
            var (ux, uy) = a.U;
            double nx = -uy, ny = ux;
            var all = new List<(int J, double D, double S0, double S1)>();
            foreach (var j in cells.Query(Math.Min(a.X0, a.X1), Math.Min(a.Y0, a.Y1), Math.Max(a.X0, a.X1), Math.Max(a.Y0, a.Y1)))
            {
                if (j == i)
                    continue;
                var b = segs[j];
                var (vx, vy) = b.U;
                if (Math.Abs(ux * vy - uy * vx) > ParallelSin)
                    continue;
                var d = ((b.X0 + b.X1) / 2 - a.X0) * nx + ((b.Y0 + b.Y1) / 2 - a.Y0) * ny;
                if (Math.Abs(d) < MinDuctWidthMm || Math.Abs(d) > MaxDuctWidthMm)
                    continue;
                var t0 = (b.X0 - a.X0) * ux + (b.Y0 - a.Y0) * uy;
                var t1 = (b.X1 - a.X0) * ux + (b.Y1 - a.Y0) * uy;
                var s0 = Math.Max(0, Math.Min(t0, t1));
                var s1 = Math.Min(a.Length, Math.Max(t0, t1));
                if (s1 - s0 < Math.Max(MinSideMm, 0.3 * Math.Min(a.Length, b.Length)) || glued(a, b))
                    continue;
                all.Add((j, d, s0, s1));
            }

            // A line farther off, behind a nearer one along most of its stretch, is another duct's (a short line
            // nearer, a damper drawn inside the duct, does not hide the duct's other side).
            near[i] = all.Where(c => !all.Any(o => Math.Sign(o.D) == Math.Sign(c.D) && Math.Abs(o.D) < Math.Abs(c.D) - 20 &&
                                                   Math.Min(o.S1, c.S1) - Math.Max(o.S0, c.S0) > 0.5 * (c.S1 - c.S0)))
                .ToList();
        }

        // Which way line j sees line i (+1 on its left), when i is among j's nearest.
        int Facing(int j, int i)
        {
            foreach (var c in near[j])
            {
                if (c.J == i)
                    return Math.Sign(c.D);
            }

            return 0;
        }

        var cand = new List<(int J, double D, double S0, double S1)>[segs.Count];
        var side = new int[segs.Count]; // +1 left, -1 right, 0 none, 2 both still open
        for (var i = 0; i < segs.Count; i++)
        {
            var me = i;
            cand[i] = near[i].Where(c => Facing(c.J, me) != 0).ToList();
            var left = cand[i].Any(c => c.D > 0);
            var right = cand[i].Any(c => c.D < 0);
            side[i] = left && right ? 2 : left ? 1 : right ? -1 : 0;
        }

        // The side whose band holds a label as wide as the band (0: neither or both), for lines open both ways.
        var labelled = new int[segs.Count];
        for (var i = 0; i < segs.Count; i++)
        {
            if (side[i] != 2)
                continue;
            var left = cand[i].Where(c => c.D > 0).OrderBy(c => c.D).First();
            var right = cand[i].Where(c => c.D < 0).OrderBy(c => -c.D).First();
            LabelsIn(labelCells, labels, segs[i], left.D, left.S0, left.S1, out var leftAgree);
            LabelsIn(labelCells, labels, segs[i], right.D, right.S0, right.S1, out var rightAgree);
            labelled[i] = (leftAgree > 0) == (rightAgree > 0) ? 0 : leftAgree > 0 ? 1 : -1;
        }

        // Settled one at a time: a side whose partners all pair the other way is taken; then the side whose partners
        // run along far more of the line (a short mark or a branch's side outside a long duct side is not its pair);
        // then the side of a partner that has no other choice (the gap between two ducts has no line needing it);
        // then the labelled side (labels are often written beside the duct, in the gap, so they come last).
        var open = Enumerable.Range(0, segs.Count).Where(i => side[i] == 2).ToList();
        while (open.Count > 0)
        {
            var settled = false;
            for (var pass = 0; pass < 4 && !settled; pass++)
            {
                foreach (var i in open)
                {
                    if (side[i] != 2)
                        continue;
                    var me = i;
                    bool Taken(int s) => cand[me].Where(c => Math.Sign(c.D) == s).All(c => side[c.J] != 2 && side[c.J] != Facing(c.J, me));
                    bool Needed(int s) => cand[me].Any(c => Math.Sign(c.D) == s && side[c.J] == Facing(c.J, me));
                    double Along(int s) => cand[me].Where(c => Math.Sign(c.D) == s && (side[c.J] == 2 || side[c.J] == Facing(c.J, me)))
                        .Sum(c => c.S1 - c.S0);
                    var pick = pass switch
                    {
                        0 => Taken(1) ? -1 : Taken(-1) ? 1 : 0,
                        1 => Along(1) >= 2 * Along(-1) ? 1 : Along(-1) >= 2 * Along(1) ? -1 : 0,
                        2 => Needed(1) == Needed(-1) ? 0 : Needed(1) ? 1 : -1,
                        _ => labelled[i]
                    };
                    if (pick == 0)
                        continue;
                    side[i] = pick;
                    settled = true;
                }
            }

            if (!settled)
            {
                // Nothing to go by: the nearer side, for the first line still open.
                var i = open[0];
                var left = cand[i].Where(c => c.D > 0).Min(c => c.D);
                var right = cand[i].Where(c => c.D < 0).Min(c => -c.D);
                side[i] = left <= right ? 1 : -1;
            }

            open.RemoveAll(i => side[i] != 2);
        }

        var pairs = new List<(int I, int J, double D, double S0, double S1)>();
        for (var i = 0; i < segs.Count; i++)
        {            foreach (var c in cand[i])
            {
                if (c.J > i && Math.Sign(c.D) == side[i] && side[c.J] == Facing(c.J, i))
                    pairs.Add((i, c.J, c.D, c.S0, c.S1));
            }
        }

        return pairs;
    }

    /// <summary>Lines drawn across a duct from one of its sides to the other: joints, fitting ends, damper blades.</summary>
    private static HashSet<int> AcrossPairs(List<Seg> segs, List<(int I, int J, double D, double S0, double S1)> pairs)
    {
        // Only pairs longer than wide: the short ends of a duct drawn as a box are not a duct the long sides cross.
        var sides = new HashSet<(int, int)>(pairs.Where(p => p.S1 - p.S0 >= Math.Abs(p.D)).Select(p => (Math.Min(p.I, p.J), Math.Max(p.I, p.J))));
        var cells = new Buckets(CellMm);
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            cells.Add(i, Math.Min(s.X0, s.X1) - NodeTolMm, Math.Min(s.Y0, s.Y1) - NodeTolMm,
                Math.Max(s.X0, s.X1) + NodeTolMm, Math.Max(s.Y0, s.Y1) + NodeTolMm);
        }

        var across = new HashSet<int>();
        for (var k = 0; k < segs.Count; k++)
        {
            var a = segs[k];
            if (a.Length > MaxDuctWidthMm + NodeTolMm)
                continue;
            var me = k;
            List<int> On(double x, double y) => cells.At(x, y)
                .Where(j => j != me && Math.Abs(a.U.X * segs[j].U.Y - a.U.Y * segs[j].U.X) > 0.9 && ToSeg(segs[j], x, y) <= NodeTolMm)
                .ToList();
            var atStart = On(a.X0, a.Y0);
            if (atStart.Count == 0)
                continue;
            var atEnd = On(a.X1, a.Y1);
            if (atStart.Any(i => atEnd.Any(j => sides.Contains((Math.Min(i, j), Math.Max(i, j))))))
                across.Add(k);
        }

        return across;
    }

    /// <summary>Widest spacing of a hatch pattern's lines taken for one (ANSI37 at scale 50: 159 mm).</summary>
    private const double HatchSpacingMm = 200;

    /// <summary>
    /// Lines with a parallel line alongside at about the same spacing on each side, closer than
    /// <see cref="HatchSpacingMm"/>: the inner lines of a hatch pattern.
    /// </summary>
    /// <summary>
    /// Each straight line of axes (one run drawn as several boxes is one line) turned about its middle, half a degree
    /// at most, to a whole number of degrees from the longest axis: the bends between them come out in whole degrees,
    /// which elbow families build (Revit finds no elbow for a bend of 58.37° that it makes at 57° or 58°). The axes of
    /// one line stay on one line.
    /// </summary>
    private static void WholeDegrees(List<Seg> axes)
    {
        var live = axes.Where(a => a.Length >= 1).ToList();
        var longest = live.OrderByDescending(a => a.Length).FirstOrDefault();
        if (longest is null)
            return;
        var baseAngle = Math.Atan2(longest.U.Y, longest.U.X);
        const double degree = Math.PI / 180;
        // Direction in [0, π) and offset of the line through each axis.
        (double Angle, double Offset) Line(Seg a)
        {
            var angle = Math.Atan2(a.U.Y, a.U.X);
            if (angle < 0)
                angle += Math.PI;
            if (angle >= Math.PI)
                angle -= Math.PI;
            return (angle, -Math.Sin(angle) * (a.X0 + a.X1) / 2 + Math.Cos(angle) * (a.Y0 + a.Y1) / 2);
        }

        var lines = new List<List<Seg>>();
        foreach (var a in live)
        {
            var (angle, offset) = Line(a);
            var found = lines.FirstOrDefault(g =>
            {
                var (ga, go) = Line(g[0]);
                var turn = Math.Abs(angle - ga);
                turn = Math.Min(turn, Math.PI - turn);
                return turn < 0.01 && Math.Abs(offset - (Math.Abs(angle - ga) > Math.PI / 2 ? -go : go)) < 30;
            });
            if (found is null)
                lines.Add(new List<Seg> { a });
            else
                found.Add(a);
        }

        foreach (var line in lines)
        {
            var first = line[0];
            var (ux, uy) = first.U;
            // The line's direction, length-weighted, and its middle.
            double sx = 0, sy = 0, cx = 0, cy = 0, total = 0;
            foreach (var a in line)
            {
                var (ax, ay) = a.U;
                var sign = ax * ux + ay * uy < 0 ? -1 : 1;
                sx += sign * ax * a.Length;
                sy += sign * ay * a.Length;
                cx += (a.X0 + a.X1) / 2 * a.Length;
                cy += (a.Y0 + a.Y1) / 2 * a.Length;
                total += a.Length;
            }

            var angle = Math.Atan2(sy, sx);
            (cx, cy) = (cx / total, cy / total);
            var whole = baseAngle + Math.Round((angle - baseAngle) / degree) * degree;
            double ox = Math.Cos(angle), oy = Math.Sin(angle), nx = Math.Cos(whole), ny = Math.Sin(whole);
            foreach (var a in line)
            {
                var t0 = (a.X0 - cx) * ox + (a.Y0 - cy) * oy;
                var t1 = (a.X1 - cx) * ox + (a.Y1 - cy) * oy;
                (a.X0, a.Y0, a.X1, a.Y1) = (cx + nx * t0, cy + ny * t0, cx + nx * t1, cy + ny * t1);
            }
        }
    }

    /// <summary>
    /// Lines up to a duct's width drawn twice over (in line within 3 mm, most of the shorter covered): the face two
    /// pieces drawn as boxes share.
    /// </summary>
    private static HashSet<int> Faces(List<Seg> segs)
    {
        var found = new HashSet<int>();
        var cells = new Buckets(CellMm);
        for (var i = 0; i < segs.Count; i++)
        {
            if (segs[i].Length <= MaxDuctWidthMm + 50)
                cells.Add(i, Math.Min(segs[i].X0, segs[i].X1), Math.Min(segs[i].Y0, segs[i].Y1), Math.Max(segs[i].X0, segs[i].X1), Math.Max(segs[i].Y0, segs[i].Y1));
        }

        for (var i = 0; i < segs.Count; i++)
        {
            var a = segs[i];
            if (a.Length > MaxDuctWidthMm + 50)
                continue;
            var (ux, uy) = a.U;
            foreach (var j in cells.Query(Math.Min(a.X0, a.X1), Math.Min(a.Y0, a.Y1), Math.Max(a.X0, a.X1), Math.Max(a.Y0, a.Y1)))
            {
                if (j <= i)
                    continue;
                var b = segs[j];
                if (Math.Abs(ux * b.U.Y - uy * b.U.X) > ParallelSin)
                    continue;
                if (Math.Abs((b.X0 - a.X0) * -uy + (b.Y0 - a.Y0) * ux) > 3)
                    continue;
                double t0 = (b.X0 - a.X0) * ux + (b.Y0 - a.Y0) * uy, t1 = (b.X1 - a.X0) * ux + (b.Y1 - a.Y0) * uy;
                var overlap = Math.Min(a.Length, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1));
                if (overlap < 0.8 * Math.Min(a.Length, b.Length))
                    continue;
                found.Add(i);
                found.Add(j);
            }
        }

        return found;
    }

    private static HashSet<int> HatchLines(List<Seg> segs)
    {
        var cells = new Buckets(CellMm);
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            cells.Add(i, Math.Min(s.X0, s.X1) - HatchSpacingMm, Math.Min(s.Y0, s.Y1) - HatchSpacingMm,
                Math.Max(s.X0, s.X1) + HatchSpacingMm, Math.Max(s.Y0, s.Y1) + HatchSpacingMm);
        }

        var hatch = new HashSet<int>();
        for (var i = 0; i < segs.Count; i++)
        {
            var a = segs[i];
            var (ux, uy) = a.U;
            double nx = -uy, ny = ux;
            double left = double.MaxValue, right = double.MaxValue;
            foreach (var j in cells.Query(Math.Min(a.X0, a.X1), Math.Min(a.Y0, a.Y1), Math.Max(a.X0, a.X1), Math.Max(a.Y0, a.Y1)))
            {
                if (j == i)
                    continue;
                var b = segs[j];
                if (Math.Abs(ux * b.U.Y - uy * b.U.X) > ParallelSin)
                    continue;
                var d = ((b.X0 + b.X1) / 2 - a.X0) * nx + ((b.Y0 + b.Y1) / 2 - a.Y0) * ny;
                if (Math.Abs(d) < 5 || Math.Abs(d) > HatchSpacingMm)
                    continue;
                var t0 = (b.X0 - a.X0) * ux + (b.Y0 - a.Y0) * uy;
                var t1 = (b.X1 - a.X0) * ux + (b.Y1 - a.Y0) * uy;
                if (Math.Min(a.Length, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1)) < 0.3 * Math.Min(a.Length, b.Length))
                    continue;
                if (d > 0)
                    left = Math.Min(left, d);
                else
                    right = Math.Min(right, -d);
            }

            if (left < double.MaxValue && right < double.MaxValue && Math.Abs(left - right) <= 0.2 * Math.Max(left, right))
                hatch.Add(i);
        }

        return hatch;
    }

    /// <summary>
    /// Lines drawn halfway between two parallel lines: a duct's centerline. Not when a line runs on at the same
    /// spacing beyond either side, nor when a label nearby is as wide as the spacing (ducts in a row with gaps as
    /// wide as they are: the middle line is a side).
    /// </summary>
    private static HashSet<int> CenterLines(List<Seg> segs, Buckets labelCells, List<(double X, double Y, MepSize Size)> labels)
    {
        var cells = new Buckets(CellMm);
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            cells.Add(i, Math.Min(s.X0, s.X1) - MaxDuctWidthMm, Math.Min(s.Y0, s.Y1) - MaxDuctWidthMm,
                Math.Max(s.X0, s.X1) + MaxDuctWidthMm, Math.Max(s.Y0, s.Y1) + MaxDuctWidthMm);
        }

        var centers = new HashSet<int>();
        for (var i = 0; i < segs.Count; i++)
        {
            var a = segs[i];
            var (ux, uy) = a.U;
            double nx = -uy, ny = ux;
            var near = new List<double>();
            foreach (var j in cells.Query(Math.Min(a.X0, a.X1), Math.Min(a.Y0, a.Y1), Math.Max(a.X0, a.X1), Math.Max(a.Y0, a.Y1)))
            {
                if (j == i)
                    continue;
                var b = segs[j];
                if (Math.Abs(ux * b.U.Y - uy * b.U.X) > ParallelSin)
                    continue;
                var d = ((b.X0 + b.X1) / 2 - a.X0) * nx + ((b.Y0 + b.Y1) / 2 - a.Y0) * ny;
                if (Math.Abs(d) < MinDuctWidthMm / 2 || Math.Abs(d) > MaxDuctWidthMm)
                    continue;
                var t0 = (b.X0 - a.X0) * ux + (b.Y0 - a.Y0) * uy;
                var t1 = (b.X1 - a.X0) * ux + (b.Y1 - a.Y0) * uy;
                // The sides run along most of the centerline (a short line beside a long side, a branch, does not count).
                if (Math.Min(a.Length, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1)) >= 0.5 * a.Length)
                    near.Add(d);
            }

            bool At(double d) => near.Any(x => Math.Abs(x - d) <= Math.Max(10, 0.05 * Math.Abs(d)));
            foreach (var d in near.Where(x => x > 0 && x <= MaxDuctWidthMm / 2).OrderBy(x => x))
            {
                // Nothing runs between a centerline and its sides (a side between two other ducts' sides is midway too).
                if (!At(-d) || At(2 * d) || At(-2 * d) || near.Any(x => Math.Abs(x) < d - 20))
                    continue;
                // A label nearby as wide as the spacing (not twice it): ducts side by side, this line a side.
                var reach = 2 * d + LabelReachMm;
                if (labelCells.Query(Math.Min(a.X0, a.X1) - reach, Math.Min(a.Y0, a.Y1) - reach, Math.Max(a.X0, a.X1) + reach, Math.Max(a.Y0, a.Y1) + reach)
                    .Any(k => Fits(labels[k].Size, d) && !Fits(labels[k].Size, 2 * d) && ToSeg(a, labels[k].X, labels[k].Y) <= reach))
                    break;
                centers.Add(i);
                break;
            }
        }

        return centers;
    }

    private static double ToSeg(Seg s, double x, double y)
    {
        var t = Math.Max(0, Math.Min(s.Length, (x - s.X0) * s.U.X + (y - s.Y0) * s.U.Y));
        return Math.Sqrt(Sq(s.X0 + s.U.X * t - x) + Sq(s.Y0 + s.U.Y * t - y));
    }

    /// <summary>
    /// Axes stop where the drawn sides stop, short of the bend, tee or reducer drawn between them. A loose end runs on
    /// to the first axis it meets within reach (1.5 × the wider duct + 300 mm): across another axis's line it stops
    /// there (a tee on its body; past that axis's end, both meet there — a bend); straight on into a parallel axis
    /// (a reducer), both ends meet halfway.
    /// </summary>
    private static void JoinAxisEnds(List<Seg> axes, Func<double, double, double, bool> roundNear, List<(double X, double Y, double RadiusMm)> bends)
    {
        var cells = new Buckets(CellMm);
        for (var i = 0; i < axes.Count; i++)
        {
            var s = axes[i];
            var pad = 1.5 * MaxDuctWidthMm + 300;
            cells.Add(i, Math.Min(s.X0, s.X1) - pad, Math.Min(s.Y0, s.Y1) - pad, Math.Max(s.X0, s.X1) + pad, Math.Max(s.Y0, s.Y1) + pad);
        }

        (double X, double Y) End(Seg s, int end) => end == 0 ? (s.X0, s.Y0) : (s.X1, s.Y1);
        void Set(Seg s, int end, double x, double y)
        {
            if (end == 0)
            {
                s.X0 = x;
                s.Y0 = y;
            }
            else
            {
                s.X1 = x;
                s.Y1 = y;
            }
        }

        bool Touches(int i, double x, double y)
        {
            foreach (var j in cells.At(x, y))
            {
                if (j == i)
                    continue;
                var b = axes[j];
                var len = b.Length;
                var t = Math.Max(0, Math.Min(len, (x - b.X0) * b.U.X + (y - b.Y0) * b.U.Y));
                if (Math.Sqrt(Sq(b.X0 + b.U.X * t - x) + Sq(b.Y0 + b.U.Y * t - y)) <= NodeTolMm)
                    return true;
            }

            return false;
        }

        // Near joins first (an elbow's ends are about its width from the corner), then the farther ones.
        foreach (var scale in new[] { 0.75, 1.5 })
        {
            for (var i = 0; i < axes.Count; i++)
            {
                for (var end = 0; end < 2; end++)
                {
                    var a = axes[i];
                    if (a.Length < 1)
                        continue;
                    var (ex, ey) = End(a, end);
                    if (Touches(i, ex, ey))
                        continue;
                    var (ux, uy) = a.U;
                    if (end == 0)
                        (ux, uy) = (-ux, -uy);
                    double nx = -uy, ny = ux;
                    var bestT = double.MaxValue;
                    Action? join = null;
                    foreach (var j in cells.At(ex, ey))
                    {
                        if (j == i)
                            continue;
                        var b = axes[j];
                        var reach = scale * Math.Max(a.Width, b.Width) + 300;
                        var (vx, vy) = b.U;
                        var cross = ux * vy - uy * vx;
                        if (Math.Abs(cross) <= ParallelSin)
                        {
                            // Straight on: the near end of the other axis ahead, about in line, facing back.
                            for (var fe = 0; fe < 2; fe++)
                            {
                                var (fx, fy) = End(b, fe);
                                var g = (fx - ex) * ux + (fy - ey) * uy;
                                var lateral = Math.Abs((fx - ex) * nx + (fy - ey) * ny);
                                var facing = (fe == 0 ? 1 : -1) * (vx * ux + vy * uy);
                                if (g < -NodeTolMm || g > reach || g >= bestT || lateral > Math.Max(a.Width, b.Width) / 2 + 50 || facing < 0.9)
                                    continue;
                                bestT = g;
                                // A reducer drawn off-centre (one side flat): both runs stay on their own lines, a
                                // transition between their ends where drawn.
                                if (lateral > 30 && Math.Abs(a.Width - b.Width) > 30 && g > NodeTolMm)
                                {
                                    var (sx, sy) = (ex, ey);
                                    join = () =>
                                    {
                                        axes.Add(new Seg(sx, sy, fx, fy) { Transition = true, Layer = a.Layer });
                                        cells.Add(axes.Count - 1, Math.Min(sx, fx), Math.Min(sy, fy), Math.Max(sx, fx), Math.Max(sy, fy));
                                    };
                                    continue;
                                }

                                // An end already joined to something stays put; this end runs to it.
                                var (mx, my) = Touches(j, fx, fy) ? (fx, fy) : ((ex + fx) / 2, (ey + fy) / 2);
                                var other = b;
                                var otherEnd = fe;
                                var endHere = end;
                                join = () =>
                                {
                                    Set(a, endHere, mx, my);
                                    Set(other, otherEnd, mx, my);
                                };
                            }

                            continue;
                        }

                        if (Math.Abs(cross) < 0.34)
                            continue; // shallower than 20°: not a bend or a tee
                        // Where this end's line crosses the other axis's line.
                        var t = ((b.X0 - ex) * vy - (b.Y0 - ey) * vx) / cross;
                        if (t < -NodeTolMm || t > reach || t >= bestT)
                            continue;
                        var (xx, xy) = (ex + ux * t, ey + uy * t);
                        var sAlong = (xx - b.X0) * vx + (xy - b.Y0) * vy;
                        var len = b.Length;
                        if (sAlong < -reach || sAlong > len + reach)
                            continue;
                        // A bend only with a loose end: an end already joined (another bend's corner) is not met again.
                        if (sAlong < 0 && Touches(j, b.X0, b.Y0) || sAlong > len && Touches(j, b.X1, b.Y1))
                            continue;
                        // Counting what the other axis would be drawn on: a tee on a body met at the same point as the
                        // end of the axis in line with it is that tee, not a bend pulling that axis over the first.
                        var score = t + (sAlong < 0 ? -sAlong : sAlong > len ? sAlong - len : 0);
                        if (score >= bestT)
                            continue;
                        bestT = score;
                        var target = b;
                        var endNow = end;
                        var width = Math.Max(a.Width, b.Width);
                        var (dirX, dirY) = (ux, uy);
                        join = () =>
                        {
                            Set(a, endNow, xx, xy);
                            // Past the other axis's end: a bend, both run to the corner. On its body: a tee.
                            if (sAlong < 0)
                                Set(target, 0, xx, xy);
                            else if (sAlong > len)
                                Set(target, 1, xx, xy);
                            else
                                return;
                            // Drawn round, the axes stopped where the arcs start: that far from the corner is
                            // R tan(turn / 2), R the radius at the duct's centre.
                            var other = sAlong < 0 ? -sAlong : sAlong - len;
                            var (wx, wy) = sAlong < 0 ? (vx, vy) : (-vx, -vy);
                            var turn = Math.Acos(Math.Max(-1, Math.Min(1, dirX * wx + dirY * wy)));
                            var reach = (t + other) / 2;
                            if (turn > 0.1 && reach > 1 && roundNear(xx, xy, reach + width))
                                bends.Add((xx, xy, reach / Math.Tan(turn / 2)));
                        };
                    }

                    join?.Invoke();
                }
            }
        }

        // Ends still loose and facing each other (an offset drawn as two round bends, the gap of a damper left out):
        // a run between them, nearest pairs first.
        var loose = new List<(int I, double X, double Y, double Ux, double Uy)>();
        var looseCells = new Buckets(CellMm);
        for (var i = 0; i < axes.Count; i++)
        {
            for (var end = 0; end < 2; end++)
            {
                var a = axes[i];
                if (a.Length < 1)
                    continue;
                var (ex, ey) = End(a, end);
                if (Touches(i, ex, ey))
                    continue;
                var (ux, uy) = a.U;
                loose.Add(end == 1 ? (i, ex, ey, ux, uy) : (i, ex, ey, -ux, -uy));
                looseCells.Add(loose.Count - 1, ex, ey, ex, ey);
            }
        }

        var links = new List<(double D, int A, int B)>();
        for (var k = 0; k < loose.Count; k++)
        {
            var a = loose[k];
            var wa = axes[a.I].Width;
            var pad = 2.5 * MaxDuctWidthMm + 300;
            foreach (var m in looseCells.Query(a.X - pad, a.Y - pad, a.X + pad, a.Y + pad))
            {
                if (m <= k || loose[m].I == a.I)
                    continue;
                var b = loose[m];
                var wb = axes[b.I].Width;
                var d = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
                if (d < 1 || d > 2.5 * Math.Max(wa, wb) + 300 || Math.Min(wa, wb) < 0.6 * Math.Max(wa, wb))
                    continue;
                double vx = (b.X - a.X) / d, vy = (b.Y - a.Y) / d;
                if (a.Ux * vx + a.Uy * vy < 0.5 || -(b.Ux * vx + b.Uy * vy) < 0.5)
                    continue;
                links.Add((d, k, m));
            }
        }

        var used = new HashSet<int>();
        foreach (var (_, ka, kb) in links.OrderBy(l => l.D))
        {
            if (used.Contains(ka) || used.Contains(kb))
                continue;
            used.Add(ka);
            used.Add(kb);
            var (a, b) = (loose[ka], loose[kb]);
            axes.Add(new Seg(a.X, a.Y, b.X, b.Y) { Width = (axes[a.I].Width + axes[b.I].Width) / 2, Layer = axes[a.I].Layer });
        }

        // A short piece (under twice its width, or 600 mm) joined to nothing at either end is a box between two lines (a
        // filter, a fan casing) or a scrap of hatch, not a run.
        var final = new Buckets(CellMm);
        for (var i = 0; i < axes.Count; i++)
        {
            var s = axes[i];
            final.Add(i, Math.Min(s.X0, s.X1) - NodeTolMm, Math.Min(s.Y0, s.Y1) - NodeTolMm, Math.Max(s.X0, s.X1) + NodeTolMm, Math.Max(s.Y0, s.Y1) + NodeTolMm);
        }

        bool Joined(int i, double x, double y) => final.At(x, y).Any(j => j != i && axes[j].Length >= 1 && ToSeg(axes[j], x, y) <= NodeTolMm);
        var lone = new HashSet<int>(Enumerable.Range(0, axes.Count)
            .Where(i => axes[i].Length < Math.Max(2 * axes[i].Width, 600) && !Joined(i, axes[i].X0, axes[i].Y0) && !Joined(i, axes[i].X1, axes[i].Y1)));
        if (lone.Count > 0)
        {
            var keep = axes.Where((_, i) => !lone.Contains(i)).ToList();
            axes.Clear();
            axes.AddRange(keep);
        }
    }

    /// <summary>
    /// Level runs along one edge under the structure. <paramref name="undersides"/> gives, every
    /// <paramref name="stepMm"/> from A, the lowest beam or slab bottom above (absolute mm). The centerline sits
    /// <paramref name="clearanceMm"/> plus half the network's deepest section under it, to 5 mm; where a beam comes
    /// lower the run drops under it, starting <paramref name="leadMm"/> before its face so the bend clears it.
    /// Runs shorter than <paramref name="minRunMm"/> join the lower neighbour (no up-and-down on a short span).
    /// </summary>
    public static List<MepPiece> Profile(
        IReadOnlyList<double> undersides, double stepMm, double lengthMm, double depthMm, double clearanceMm,
        double leadMm = 150, double minRunMm = 600)
    {
        var z = undersides.Select(u => Math.Floor((u - clearanceMm - depthMm / 2) / 5) * 5).ToList();
        var runs = new List<(double From, double To, double Z)>();
        for (var i = 0; i < z.Count; i++)
        {
            var at = Math.Min(i * stepMm, lengthMm);
            if (runs.Count > 0 && Math.Abs(runs[runs.Count - 1].Z - z[i]) < 1)
                runs[runs.Count - 1] = (runs[runs.Count - 1].From, at, runs[runs.Count - 1].Z);
            else
                runs.Add((at, at, z[i]));
        }

        // Each change sits midway between samples; a lower run takes the lead on its sides.
        for (var i = 0; i + 1 < runs.Count; i++)
        {
            var mid = (runs[i].To + runs[i + 1].From) / 2;
            var cut = runs[i].Z < runs[i + 1].Z ? mid + leadMm : mid - leadMm;
            runs[i] = (runs[i].From, cut, runs[i].Z);
            runs[i + 1] = (cut, runs[i + 1].To, runs[i + 1].Z);
        }

        if (runs.Count > 0)
        {
            runs[0] = (0, runs[0].To, runs[0].Z);
            runs[runs.Count - 1] = (runs[runs.Count - 1].From, lengthMm, runs[runs.Count - 1].Z);
        }

        // A short high run between two drops comes down to its lower neighbour (a short dip under a beam stays).
        var changed = true;
        while (changed && runs.Count > 1)
        {
            changed = false;
            for (var i = 0; i < runs.Count; i++)
            {
                if (runs[i].To - runs[i].From >= minRunMm)
                    continue;
                var left = i > 0 ? runs[i - 1].Z : double.MaxValue;
                var right = i + 1 < runs.Count ? runs[i + 1].Z : double.MaxValue;
                var lower = Math.Min(left, right);
                if (lower >= runs[i].Z)
                    continue;
                runs[i] = (runs[i].From, runs[i].To, lower);
                changed = true;
            }

            // Neighbours at one height become one run.
            for (var i = 0; i + 1 < runs.Count; i++)
            {
                if (Math.Abs(runs[i].Z - runs[i + 1].Z) >= 1)
                    continue;
                runs[i] = (runs[i].From, runs[i + 1].To, runs[i].Z);
                runs.RemoveAt(i + 1);
                i--;
            }
        }

        return runs.Select(r => new MepPiece(r.From, r.To, r.Z)).ToList();
    }

    /// <summary>
    /// Puts one end of an edge at the node's elevation (the lowest of the runs meeting there): a short end run just
    /// takes it, a long one gets a drop <paramref name="leadMm"/> from the node.
    /// </summary>
    public static List<MepPiece> SetEnd(List<MepPiece> pieces, bool atStart, double z, double leadMm = 300)
    {
        if (pieces.Count == 0)
            return pieces;
        var result = new List<MepPiece>(pieces);
        var i = atStart ? 0 : result.Count - 1;
        var p = result[i];
        if (Math.Abs(p.Z - z) < 1)
            return result;
        if (p.To - p.From <= leadMm * 2)
            result[i] = new MepPiece(p.From, p.To, z);
        else if (atStart)
        {
            result[i] = new MepPiece(p.From + leadMm, p.To, p.Z);
            result.Insert(0, new MepPiece(p.From, p.From + leadMm, z));
        }
        else
        {
            result[i] = new MepPiece(p.From, p.To - leadMm, p.Z);
            result.Add(new MepPiece(p.To - leadMm, p.To, z));
        }

        // Neighbours at one height become one run.
        for (var k = 0; k + 1 < result.Count; k++)
        {
            if (Math.Abs(result[k].Z - result[k + 1].Z) >= 1)
                continue;
            result[k] = new MepPiece(result[k].From, result[k + 1].To, result[k].Z);
            result.RemoveAt(k + 1);
            k--;
        }

        return result;
    }

    /// <summary>Distance along the network from <paramref name="outlet"/> to every node it reaches (mm).</summary>
    public static Dictionary<int, double> DistancesFrom(MepNetwork net, int outlet)
    {
        var adjacent = Adjacency(net);
        var dist = new Dictionary<int, double> { [outlet] = 0 };
        var open = new SortedSet<(double D, int N)> { (0, outlet) };
        while (open.Count > 0)
        {
            var (dn, n) = open.Min;
            open.Remove(open.Min);
            foreach (var e in adjacent[n])
            {
                var m = e.A == n ? e.B : e.A;
                var d = dn + Length(net, e);
                if (dist.TryGetValue(m, out var known))
                {
                    if (known <= d)
                        continue;
                    open.Remove((known, m));
                }

                dist[m] = d;
                open.Add((d, m));
            }
        }

        return dist;
    }

    /// <summary>The runs at each node, in network order.</summary>
    private static List<MepEdge>[] Adjacency(MepNetwork net)
    {
        var at = net.Nodes.Select(_ => new List<MepEdge>()).ToArray();
        foreach (var e in net.Edges)
        {
            at[e.A].Add(e);
            at[e.B].Add(e);
        }

        return at;
    }

    /// <summary>Plan cells holding indexes of things whose box overlaps them (box query by cell, not by scan).</summary>
    private sealed class Buckets
    {
        private readonly double _cell;
        private readonly Dictionary<(long, long), List<int>> _cells = new();
        /// <summary>Boxes over too many cells (a stray stroke far off the drawing): checked by every query.</summary>
        private readonly List<int> _big = new();

        public Buckets(double cell) => _cell = cell;

        private long C(double v) => (long)Math.Floor(v / _cell);

        public void Add(int index, double minX, double minY, double maxX, double maxY)
        {
            if ((C(maxX) - C(minX) + 1.0) * (C(maxY) - C(minY) + 1.0) > 4096)
            {
                _big.Add(index);
                return;
            }

            for (var ix = C(minX); ix <= C(maxX); ix++)
            for (var iy = C(minY); iy <= C(maxY); iy++)
            {
                if (!_cells.TryGetValue((ix, iy), out var list))
                    _cells[(ix, iy)] = list = new List<int>();
                list.Add(index);
            }
        }

        /// <summary>Everything whose box covers the point's cell, in the order added.</summary>
        public IEnumerable<int> At(double x, double y)
        {
            IEnumerable<int> list = _cells.TryGetValue((C(x), C(y)), out var inCell) ? inCell : Enumerable.Empty<int>();
            return _big.Count == 0 ? list : list.Concat(_big).OrderBy(i => i);
        }

        /// <summary>Everything whose box meets the given box, each once, in the order added.</summary>
        public List<int> Query(double minX, double minY, double maxX, double maxY)
        {
            var found = new HashSet<int>(_big);
            if ((C(maxX) - C(minX) + 1.0) * (C(maxY) - C(minY) + 1.0) <= 4096)
            {
                for (var ix = C(minX); ix <= C(maxX); ix++)
                for (var iy = C(minY); iy <= C(maxY); iy++)
                {
                    if (_cells.TryGetValue((ix, iy), out var list))
                        found.UnionWith(list);
                }
            }
            else
            {
                foreach (var list in _cells.Values)
                    found.UnionWith(list);
            }

            var sorted = found.ToList();
            sorted.Sort();
            return sorted;
        }

        /// <summary>Everything in the point's cell and the 8 around it (points added with a cell as large as the reach).</summary>
        public IEnumerable<int> Near(double x, double y)
        {
            foreach (var i in _big)
                yield return i;
            var cx = C(x);
            var cy = C(y);
            for (var ix = cx - 1; ix <= cx + 1; ix++)
            for (var iy = cy - 1; iy <= cy + 1; iy++)
            {
                if (_cells.TryGetValue((ix, iy), out var list))
                {
                    foreach (var i in list)
                        yield return i;
                }
            }
        }
    }

    private const double CellMm = 3000;

    /// <summary>
    /// The outlet of a drainage network with no outlet picked: the open end of its largest run (the main leaves the
    /// floor there); ties go to the end farthest from the network's other ends.
    /// </summary>
    public static int DefaultOutlet(MepNetwork net, IReadOnlyCollection<int> nodes)
    {
        var adjacent = Adjacency(net);
        var ends = nodes.Where(n => adjacent[n].Count == 1).ToList();
        if (ends.Count == 0)
            return nodes.First();
        return ends
            .OrderByDescending(n => adjacent[n][0].Size.DepthMm)
            .ThenByDescending(n => DistancesFrom(net, n).Values.Max())
            .First();
    }

    /// <summary>
    /// Drainage falls toward the outlet: centerline at each node = outlet + slope × distance along the pipe. With a
    /// structure above, the outlet is set as high as it can go: every point of every run (sampled each
    /// <paramref name="stepMm"/>) stays <paramref name="clearanceMm"/> under the beam or slab over it.
    /// <paramref name="underside"/> gives that underside at a plan point (mm), or null where there is none.
    /// </summary>
    public static double OutletUnderStructure(
        MepNetwork net, int outlet, double slope, Func<double, double, double?> underside, double clearanceMm,
        double stepMm, double fallbackMm)
    {
        var dist = RisesFrom(net, outlet, slope * 100);
        var best = double.MaxValue;
        foreach (var e in net.Edges.Where(x => dist.ContainsKey(x.A) && dist.ContainsKey(x.B)))
        {
            var (ax, ay) = net.Nodes[e.A];
            var (bx, by) = net.Nodes[e.B];
            var len = Length(net, e);
            for (var s = 0.0; ; s += stepMm)
            {
                var t = Math.Min(s, len) / len;
                if (underside(ax + (bx - ax) * t, ay + (by - ay) * t) is double u)
                {
                    var rise = dist[e.A] + (dist[e.B] - dist[e.A]) * t;
                    best = Math.Min(best, u - clearanceMm - e.Size.DepthMm / 2 - rise);
                }

                if (s >= len)
                    break;
            }
        }

        return best == double.MaxValue ? fallbackMm : Math.Floor(best / 5) * 5;
    }

    public static double Length(MepNetwork net, MepEdge e)
    {
        var (ax, ay) = net.Nodes[e.A];
        var (bx, by) = net.Nodes[e.B];
        return Math.Sqrt(Sq(bx - ax) + Sq(by - ay));
    }

    /// <summary>Connected parts of the network: the nodes of each.</summary>
    public static List<List<int>> Parts(MepNetwork net)
    {
        var parent = Enumerable.Range(0, net.Nodes.Count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }

        var used = new bool[net.Nodes.Count];
        foreach (var e in net.Edges)
        {
            parent[Find(e.A)] = Find(e.B);
            used[e.A] = used[e.B] = true;
        }

        return Enumerable.Range(0, net.Nodes.Count)
            .Where(n => used[n])
            .GroupBy(Find)
            .Select(g => g.ToList())
            .ToList();
    }

    /// <summary>
    /// The side a shoe is drawn on where a branch leaves a main: the slanted line from the main's side, out past the
    /// branch's side, back to that side up the branch. +1 when it lies along (<paramref name="mainX"/>,
    /// <paramref name="mainY"/>) from the branch's axis, -1 the other way, 0 when none is drawn (or both).
    /// </summary>
    /// <param name="nodeX">Where the branch's axis meets the main's (mm).</param>
    /// <param name="branchX">Unit plan direction of the branch, away from the main.</param>
    /// <param name="mainX">Unit plan direction of the main.</param>
    public static int ShoeSide(IEnumerable<CadEntity> drawn, double nodeX, double nodeY, double branchX, double branchY,
        double mainX, double mainY, double branchWidthMm, double mainWidthMm)
    {
        double halfB = branchWidthMm / 2, halfM = mainWidthMm / 2;
        var reach = halfM + 2 * branchWidthMm + 300;
        var votes = 0;
        foreach (var e in drawn)
        {
            if (e is CadPolyline { FromArc: true })
                continue;
            var pts = Points(e).ToList();
            var count = e is CadPolyline { Closed: true } ? pts.Count : pts.Count - 1;
            for (var k = 0; k < count; k++)
            {
                var (ax, ay) = pts[k];
                var (bx, by) = pts[(k + 1) % pts.Count];
                // Along the branch (t) and along the main (s), from the node.
                (double T, double S) Local(double x, double y) =>
                    ((x - nodeX) * branchX + (y - nodeY) * branchY, (x - nodeX) * mainX + (y - nodeY) * mainY);
                var p = Local(ax, ay);
                var q = Local(bx, by);
                foreach (var (atMain, onBranch) in new[] { (p, q), (q, p) })
                {
                    if (Math.Abs(atMain.T - halfM) > 30 || Math.Abs(atMain.S) <= halfB + 30 || Math.Abs(atMain.S) > halfB + branchWidthMm + 300 ||
                        Math.Abs(Math.Abs(onBranch.S) - halfB) > 30 || onBranch.T <= halfM + 30 || onBranch.T > reach ||
                        Math.Sign(onBranch.S) != Math.Sign(atMain.S))
                        continue;
                    votes += Math.Sign(atMain.S);
                }
            }
        }

        return Math.Sign(votes);
    }

    private const double OpeningMaxMm = 1000;

    /// <summary>
    /// A two-line part joined to nothing, shorter than <see cref="OpeningMaxMm"/> in all and with no size written on it,
    /// is an opening or louver through a wall drawn on the duct layer, not a duct: left out. Returns how many.
    /// </summary>
    private static int DropOpenings(MepNetwork net)
    {
        var dropped = 0;
        foreach (var part in Parts(net))
        {
            var nodes = new HashSet<int>(part);
            var edges = net.Edges.Where(e => nodes.Contains(e.A)).ToList();
            if (edges.Count == 0 || edges.Any(e => e.DrawnWidthMm <= 0 || e.SizeFrom == "text") ||
                edges.Sum(e => Length(net, e)) >= OpeningMaxMm)
                continue;
            foreach (var e in edges)
                net.Edges.Remove(e);
            dropped++;
        }

        return dropped;
    }

    /// <summary>Two runs leaving a node in opposite directions (the main through a tee).</summary>
    private static bool Straight(MepNetwork net, MepEdge a, MepEdge b, int node)
    {
        (double X, double Y) Away(MepEdge e)
        {
            var far = net.Nodes[e.A == node ? e.B : e.A];
            var (nx, ny) = net.Nodes[node];
            var len = Math.Sqrt(Sq(far.X - nx) + Sq(far.Y - ny));
            return ((far.X - nx) / len, (far.Y - ny) / len);
        }

        var (ax, ay) = Away(a);
        var (bx, by) = Away(b);
        return ax * bx + ay * by < -0.995;
    }

    private static double Sq(double v) => v * v;
}
