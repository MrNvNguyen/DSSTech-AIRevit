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
    /// <summary>
    /// Elevation written on the run (H=2800, BOT 2600): mm above the level, measured as the drawing's note says
    /// (bottom unless told otherwise); null where none is written or carried on.
    /// </summary>
    public double? ElevationMm { get; set; }
    /// <summary>Flow written on the run (0.90 L/S): litres per second; null where none is written or carried on.</summary>
    public double? FlowLps { get; set; }
}

public sealed class MepNetwork
{
    public List<(double X, double Y)> Nodes { get; } = new();
    public List<MepEdge> Edges { get; } = new();
    /// <summary>Flex ducts drawn on the picked layers, left out (joined later by the flex tool).</summary>
    public int FlexSkipped { get; set; }
    /// <summary>Short two-line boxes standing alone with no size written (openings, louvers through a wall), left out.</summary>
    public int OpeningsSkipped { get; set; }
    /// <summary>Short ladder pieces standing alone with nothing written by them (drawn in a fitting), left out.</summary>
    public int LadderPiecesSkipped { get; set; }
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

    /// <summary>The xref a layer comes from ("XR TANG HAM$0$XR-TXTN T HAM$0$Text" → "XR TANG HAM$0$XR-TXTN T HAM"); "" for the drawing's own.</summary>
    private static string XrefOf(string? layer)
    {
        var at = (layer ?? "").LastIndexOf("$0$", StringComparison.Ordinal);
        return at < 0 ? "" : layer!.Substring(0, at);
    }

    /// <summary>Round sizes one or two steps apart (75 / 90, 110 / 140), not a branch's (DN32 by a DN100).</summary>
    private static bool NearSize(MepSize a, MepSize b) =>
        a.IsRound && b.IsRound && a.DiameterMm > 0 && b.DiameterMm > 0 &&
        Math.Min(a.DiameterMm, b.DiameterMm) / Math.Max(a.DiameterMm, b.DiameterMm) >= 0.6;
    /// <summary>Reading a whole link: shorter strokes are arrows, ticks and symbols, not runs.</summary>
    private const double MinRunInLinkMm = 150;

    /// <summary>
    /// Network from single lines. <paramref name="pickedLayer"/> null: every line of the CAD link (one link per system,
    /// whatever the consultant's layer names); otherwise the picked layer and the layers differing from it only by
    /// size. Size of each run: the size label written beside it (600x400, Ø200, D250, DN100), else the size in its
    /// layer name, else the size of the run it continues, else <paramref name="fallback"/>.
    /// <paramref name="doubleLine"/> (reading a whole link): true, ducts drawn with both sides (every layer read as
    /// pairs of sides); false, single lines only (pipes, trays); null, each layer read as it looks.
    /// <paramref name="busway"/>: single-line busways, which take only the heights written for busways (and notes
    /// naming no kind of run), never a tray's or a duct's.
    /// </summary>
    public static MepNetwork Build(
        IEnumerable<CadEntity> entities, string? pickedLayer, MepSize fallback, IEnumerable<CadText>? texts = null, bool? doubleLine = null,
        bool busway = false)
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
        // Ladder trays as drawn: rungs of two trays side by side are in line, not one line's dashes.
        var ladders = false;
        var drawn = doubleLine is null
            ? segs.GroupBy(x => x.Layer, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => new Seg(x.X0, x.Y0, x.X1, x.Y1) { Arc = x.Arc, Layer = x.Layer }).Where(x => x.Length > 1).ToList(), StringComparer.OrdinalIgnoreCase)
            : null;
        // A whole link read with its linetypes comes as dashes: each line's dashes are one line again.
        if (family is null && doubleLine != false)
            segs = Undash(segs);
        // Every stroke, the short ones too (a flex duct's corrugations), by layer: what lies between two sides.
        var fine = segs.GroupBy(x => x.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => new Seg(x.X0, x.Y0, x.X1, x.Y1) { Arc = x.Arc }).Where(x => x.Length > 1).ToList(), StringComparer.OrdinalIgnoreCase);
        segs = segs.Where(x => Math.Sqrt(Sq(x.X1 - x.X0) + Sq(x.Y1 - x.Y0)) > minRun).ToList();
        var textList = WithStackedSizes((texts ?? Enumerable.Empty<CadText>()).ToList());
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

                // Trays (each layer read as it looks): a ladder drawn with its rails and rungs first.
                var found = drawn is not null && drawn.TryGetValue(layer.Key, out var ladder) ? LadderAxes(ladder) : null;
                ladders |= found is not null;
                found ??= DoubleLines(layer.Select(x => new Seg(x.X0, x.Y0, x.X1, x.Y1) { Size = x.Size, From = x.From, Layer = x.Layer, Arc = x.Arc }).ToList(),
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
        var xrefs = new HashSet<string>(net.Edges.Select(e => XrefOf(e.Layer))) { "" };
        foreach (var text in textList)
        {
            // A grille's or diffuser's label (EAG+OBD 450x450, SAG 1200x150) gives its face, not the duct's size.
            if (!TryParseSize(text.Value, out var size) || GrilleLabel.IsMatch(text.Value))
                continue;
            // A size in an xref the pipes are not drawn in (another discipline's drawing: "XR TANG HAM$0$…$0$VNBT-FF-TEXT"
            // D65 of a fire main) is not these pipes' label. Pipes drawn in an xref take labels from that xref.
            if (!xrefs.Contains(XrefOf(text.Layer)))
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
                // The label written on the drawing wins over the size a layer names (pipes drawn on the wrong layer:
                // D75 on "P - TR D90", D110 on "P - TX D140"). Only a label far off the layer's size (a branch's Dn40
                // written beside an SD-D150 main whose branch is not picked) is another pipe's.
                if (e.SizeFrom == "layer" && !e.Size.Equals(size) && !NearSize(size, e.Size))
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
        // A label wins over the layer along its whole run: the pieces of the same layer after it (through bends and
        // joints, straight on through a tee) that took the layer's size take the label's, up to another label.
        var adjacent = Adjacency(net);
        foreach (var labelled in labelAt.Keys.Where(e => e.SizeFrom == "text"))
        {
            var queue = new Queue<MepEdge>();
            queue.Enqueue(labelled);
            while (queue.Count > 0)
            {
                var e = queue.Dequeue();
                foreach (var node in new[] { e.A, e.B })
                {
                    var at = adjacent[node].Where(x => x != e).ToList();
                    foreach (var x in at.Count == 1 ? at : at.Where(x => Straight(net, e, x, node)))
                    {
                        if (x.SizeFrom != "layer" || x.Layer != labelled.Layer || x.Size.Equals(labelled.Size) || x.IsTransition)
                            continue;
                        x.Size = labelled.Size;
                        x.SizeFrom = "inherited";
                        queue.Enqueue(x);
                    }
                }
            }
        }

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
        Flows(net, textList);
        Elevations(net, textList, doubleLine, busway);
        net.OpeningsSkipped = DropOpenings(net);
        if (ladders)
            net.LadderPiecesSkipped = DropOpenings(net, LadderPieceMaxMm);
        if (doubleLine is null)
            JoinCrossings(net);
        net.FlexSkipped = flexSkipped;
        net.Bends.AddRange(bends);
        return net;
    }

    /// <summary>
    /// H=2800, H = 2600 mm, BOT 2600, H=+2.80 (m); not a pump's head (H=15M). A bare "H 200" is not one: H needs "=",
    /// ":" or "+" (BỆ MÁY H 200 is a plinth's height). FFL=+3.0M / FFL=+3M: a level above the floor, so M is metres.
    /// </summary>
    private static readonly Regex ElevationLabel = new(
        @"(?<![\p{L}\d])(?:H\s*(?:[=:]\s*\+?|\+)|(?:BOT|BOD)\s*[=:]?\s*\+?|(FFL)\s*(?:[=:]\s*\+?|\+))\s*(\d+(?:[.,]\d+)?)\s*(MM|M)?(?![\p{L}\d])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A pipe bottom written on a drain: BOP=-0.57, BOP = -570, BOD=-0.49 (bottom of pipe, from the level).</summary>
    private static readonly Regex BottomLabel = new(
        @"(?<![\p{L}\d])(?:BOP|BOD|BOT|IL|INV)\s*[=:]?\s*([+-]?\s*\d+(?:[.,]\d+)?)\s*(MM|M)?(?![\p{L}\d])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The pipe bottom a note gives (mm from the level): BOP=-0.57 → -570, BOP=-570 → -570.</summary>
    public static bool TryParseBottom(string? text, out double mm)
    {
        mm = 0;
        var m = text is null ? Match.Empty : BottomLabel.Match(text);
        if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(" ", "").Replace(',', '.'),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return false;
        var unit = m.Groups[2].Value.ToUpperInvariant();
        var metres = unit == "M" || unit != "MM" && Math.Abs(v) < 30;
        mm = metres ? v * 1000 : v;
        return true;
    }

    /// <summary>
    /// The pipe bottoms written at a drain network's open ends (BOP=-0.50 by the end of the run): each note within
    /// <paramref name="reachMm"/> of the pipe, its foot on the pipe within <paramref name="endMm"/> (along it) of an open
    /// end, gives that end. Notes along the middle of a run are not used: the ends fix it, the fall does the rest.
    /// </summary>
    public static Dictionary<int, (double BottomMm, string Text)> EndBottoms(
        MepNetwork net, IReadOnlyCollection<int> part, IEnumerable<CadText> texts, double reachMm = 2000, double endMm = 2500)
    {
        var adjacent = Adjacency(net);
        var inPart = new HashSet<int>(part);
        var edges = net.Edges.Where(e => inPart.Contains(e.A) && !e.IsTransition).ToList();
        var ends = part.Where(n => adjacent[n].Count == 1).ToList();
        var found = new Dictionary<int, (double BottomMm, string Text, double D)>();
        var all = texts.ToList();
        // Sleeve notes with a size (D140 CHỜ XUYÊN MÓNG): a BOP written under one is that sleeve's.
        var sleeves = all.Select(s => (Text: DevicePlanner.Plain(s.Value), X: s.CenterX ?? s.X, Y: s.CenterY ?? s.Y))
            .Where(s => SleeveNote.IsMatch(s.Text) && SizeIn(s.Text) is not null).ToList();
        foreach (var t in all)
        {
            if (!TryParseBottom(Regex.Replace(t.Value ?? "", @"\\[A-Za-z][^;\\]*;|[{}]", ""), out var mm))
                continue;
            var tx = t.CenterX ?? t.X;
            var ty = t.CenterY ?? t.Y;
            // The sleeve line stands over its BOP line; a long sleeve note's middle is ~2-3 m from a short BOP's.
            var pairedSize = sleeves.Where(s => Math.Sqrt(Sq(s.X - tx) + Sq(s.Y - ty)) <= 3000)
                .OrderBy(s => Sq(s.X - tx) + Sq(s.Y - ty)).Select(s => SizeIn(s.Text)).FirstOrDefault();
            // The foot of the note on the nearest run of this network.
            var (best, bd, fx, fy) = ((MepEdge?)null, reachMm, 0.0, 0.0);
            foreach (var e in edges)
            {
                var (x0, y0) = net.Nodes[e.A];
                var (x1, y1) = net.Nodes[e.B];
                var l2 = Sq(x1 - x0) + Sq(y1 - y0);
                var u = l2 < 1 ? 0 : Math.Max(0, Math.Min(1, ((tx - x0) * (x1 - x0) + (ty - y0) * (y1 - y0)) / l2));
                var (px, py) = (x0 + (x1 - x0) * u, y0 + (y1 - y0) * u);
                var d = Math.Sqrt(Sq(px - tx) + Sq(py - ty));
                if (d < bd)
                    (best, bd, fx, fy) = (e, d, px, py);
            }

            if (best is null || pairedSize is double ps && Math.Abs(ps - best.Size.DiameterMm) >= 1)
                continue;
            // The open end this foot lies near, along the pipe.
            foreach (var n in ends)
            {
                var along = DistancesFrom(net, n);
                var a = Math.Min(
                    (along.TryGetValue(best.A, out var da) ? da : double.MaxValue) + Math.Sqrt(Sq(fx - net.Nodes[best.A].X) + Sq(fy - net.Nodes[best.A].Y)),
                    (along.TryGetValue(best.B, out var db) ? db : double.MaxValue) + Math.Sqrt(Sq(fx - net.Nodes[best.B].X) + Sq(fy - net.Nodes[best.B].Y)));
                if (a > endMm || found.TryGetValue(n, out var had) && had.D <= a + bd)
                    continue;
                found[n] = (mm, t.Value ?? "", a + bd);
            }
        }

        return found.ToDictionary(kv => kv.Key, kv => (kv.Value.BottomMm, kv.Value.Text));
    }

    /// <summary>Heights of building parts, not of a run: plinths, pads, doors, tanks, pits.</summary>
    private static readonly Regex NotRunHeight = new(
        @"(?<!\p{L})(?:BỆ|BE MAY|ĐẾ|PAD|PLINTH|CỬA|BỂ|HỐ|SILL)(?!\p{L})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The elevation a note gives (mm): H=2800 → 2800, H=+2.80 → 2800; false for anything else.</summary>
    public static bool TryParseElevation(string? text, out double mm)
    {
        mm = 0;
        if (text is null || NotRunHeight.IsMatch(text))
            return false;
        foreach (Match m in ElevationLabel.Matches(text))
        {
            var raw = m.Groups[2].Value.Replace(',', '.');
            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                continue;
            var unit = m.Groups[3].Value.ToUpperInvariant();
            var metres = (raw.Contains(".") || m.Groups[1].Success && unit == "M") && v < 30;
            // H=15M, H=100M: a pump's head, not a height.
            if (unit == "M" && !metres)
                continue;
            var value = metres ? v * 1000 : v;
            if (value < 100 || value > 15000)
                continue;
            mm = value;
            return true;
        }

        return false;
    }

    private static readonly Regex TrayWords = new(
        @"THANG\s*CÁP|MÁNG\s*CÁP|THANG\s*CAP|MANG\s*CAP|TRUNKING|CABLE\s*(?:TRAY|LADDER)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BuswayWords = new(
        @"BUS\s*WAY|BUSDUCT|BUS\s*DUCT|THANH\s*CÁI|THANH\s*CAI|THANH\s*DẪN|THANH\s*DAN",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DuctWords = new(
        @"ỐNG\s*GIÓ|ONG\s*GIO|\bDUCT\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// A busway's strokes: open lines and polylines outside blocks (equipment boxes, panels and symbols are closed or
    /// blocks). A run is drawn wide (global width) or long; a busway drawn without width is still one, so width alone
    /// does not decide. Short strokes count only where they meet the end of a run (a jog between two runs).
    /// </summary>
    public static List<CadEntity> BuswayStrokes(IEnumerable<CadEntity> lines, double minRunMm = 1000, double touchMm = 50)
    {
        var open = lines.Where(e => e.BlockRef == 0 && (e is CadLine || e is CadPolyline { Closed: false, Points.Count: >= 2 }))
            .ToList();
        static (CadPoint2 A, CadPoint2 B, double Len) Ends(CadEntity e)
        {
            if (e is CadLine l)
                return (new CadPoint2(l.X0, l.Y0), new CadPoint2(l.X1, l.Y1), Math.Sqrt((l.X1 - l.X0) * (l.X1 - l.X0) + (l.Y1 - l.Y0) * (l.Y1 - l.Y0)));
            var p = ((CadPolyline)e).Points;
            var len = 0.0;
            for (var i = 0; i + 1 < p.Count; i++)
                len += Math.Sqrt((p[i + 1].X - p[i].X) * (p[i + 1].X - p[i].X) + (p[i + 1].Y - p[i].Y) * (p[i + 1].Y - p[i].Y));
            return (p[0], p[p.Count - 1], len);
        }

        var ends = open.ToDictionary(e => e, Ends);
        var kept = open.Where(e => e is CadPolyline { WidthMm: > 0 } || ends[e].Len >= minRunMm).ToList();
        var rest = open.Except(kept).ToList();
        bool Near(CadPoint2 a, CadPoint2 b) => Math.Abs(a.X - b.X) <= touchMm && Math.Abs(a.Y - b.Y) <= touchMm;
        for (var added = true; added && rest.Count > 0;)
        {
            var join = rest.Where(s => kept.Any(k =>
                Near(ends[s].A, ends[k].A) || Near(ends[s].A, ends[k].B) || Near(ends[s].B, ends[k].A) || Near(ends[s].B, ends[k].B))).ToList();
            added = join.Count > 0;
            kept.AddRange(join);
            rest = rest.Except(join).ToList();
        }

        return kept;
    }

    /// <summary>
    /// Busways broken where a tray is drawn over them (the ladder's block hides the busway, nothing is drawn under it):
    /// two open ends on one line (≤ <paramref name="maxOffsetMm"/> aside), each pointing at the other, at most
    /// <paramref name="maxGapMm"/> apart, are joined by a straight stroke along the first run's line. Each end is
    /// joined once, to its nearest match.
    /// </summary>
    public static List<CadLine> BuswayBridges(IReadOnlyList<CadEntity> strokes, double maxGapMm = 6000, double maxOffsetMm = 100)
    {
        // Every open end: where it is and the way it points (out of its run).
        var ends = new List<(CadPoint2 P, double Dx, double Dy, string Layer, int Stroke)>();
        void End(CadPoint2 p, CadPoint2 inner, string layer, int stroke)
        {
            var dx = p.X - inner.X;
            var dy = p.Y - inner.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 1)
                ends.Add((p, dx / len, dy / len, layer, stroke));
        }

        for (var s = 0; s < strokes.Count; s++)
        {
            switch (strokes[s])
            {
                case CadLine l:
                    End(new CadPoint2(l.X0, l.Y0), new CadPoint2(l.X1, l.Y1), l.Layer, s);
                    End(new CadPoint2(l.X1, l.Y1), new CadPoint2(l.X0, l.Y0), l.Layer, s);
                    break;
                case CadPolyline { Closed: false, Points.Count: >= 2 } pl:
                    End(pl.Points[0], pl.Points[1], pl.Layer, s);
                    End(pl.Points[pl.Points.Count - 1], pl.Points[pl.Points.Count - 2], pl.Layer, s);
                    break;
            }
        }

        // An end touching another stroke is not open.
        bool Touches(int i) => ends.Where((_, j) => j != i).Any(o =>
            Math.Abs(o.P.X - ends[i].P.X) <= 50 && Math.Abs(o.P.Y - ends[i].P.Y) <= 50);
        var open = Enumerable.Range(0, ends.Count).Where(i => !Touches(i)).ToList();

        var pairs = new List<(int A, int B, double Gap)>();
        foreach (var a in open)
        {
            foreach (var b in open.Where(b => b > a && ends[b].Stroke != ends[a].Stroke))
            {
                var (pa, ax, ay, _, _) = ends[a];
                var (pb, bx, by, _, _) = ends[b];
                // Pointing at each other along one line.
                if (ax * bx + ay * by > -0.995)
                    continue;
                var along = (pb.X - pa.X) * ax + (pb.Y - pa.Y) * ay;
                var aside = Math.Abs(-(pb.X - pa.X) * ay + (pb.Y - pa.Y) * ax);
                if (along > 1 && along <= maxGapMm && aside <= maxOffsetMm)
                    pairs.Add((a, b, along));
            }
        }

        var used = new HashSet<int>();
        var bridges = new List<CadLine>();
        foreach (var (a, b, _) in pairs.OrderBy(x => x.Gap))
        {
            if (used.Contains(a) || used.Contains(b))
                continue;
            used.Add(a);
            used.Add(b);
            var (pa, ax, ay, layer, _) = ends[a];
            var pb = ends[b].P;
            var along = (pb.X - pa.X) * ax + (pb.Y - pa.Y) * ay;
            // Along the first run's line, ending square across from the other end (the planner's snap joins them).
            bridges.Add(new CadLine { Layer = layer, X0 = pa.X, Y0 = pa.Y, X1 = pa.X + ax * along, Y1 = pa.Y + ay * along });
        }

        return bridges;
    }

    /// <summary>The height written most often among a network's runs (null when none is written).</summary>
    public static double? MostWritten(IEnumerable<double?> heights) =>
        heights.Where(z => z is not null)
            .GroupBy(z => z!.Value)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => (double?)g.Key)
            .FirstOrDefault();

    /// <summary>
    /// A busway network's one centre height: the CAD's, else the typed one, lowered whole (to 5 mm) where the lowest
    /// beam or slab over it leaves less than the clearance above its top. Never a profile that dodges beams.
    /// </summary>
    public static double BuswayHeight(double? cadCenter, double manualCenter, double? lowestUnderside, double clearanceMm, double depthMm)
    {
        if (cadCenter is double cad)
            return cad;
        if (lowestUnderside is double u && u - clearanceMm < manualCenter + depthMm / 2)
            return Math.Floor((u - clearanceMm - depthMm / 2) / 5) * 5;
        return manualCenter;
    }

    /// <summary>
    /// Whether a note may speak of runs read as <paramref name="doubleLine"/> (true duct, null tray, false pipe):
    /// a note naming trays is not a duct's or a pipe's, one naming ducts is not a tray's or a pipe's. A note naming
    /// busways is only a busway's (<paramref name="busway"/>), and a busway takes no tray's or duct's note.
    /// </summary>
    public static bool OfKind(string text, bool? doubleLine, bool busway = false)
    {
        if (BuswayWords.IsMatch(text))
            return busway;
        if (busway)
            return !TrayWords.IsMatch(text) && !DuctWords.IsMatch(text);
        if (TrayWords.IsMatch(text))
            return doubleLine is null;
        if (DuctWords.IsMatch(text))
            return doubleLine == true;
        return true;
    }

    /// <summary>A run of the size a note names: its width (or diameter) within 10 % or 20 mm, as labelled or as drawn.</summary>
    private static bool Fits(MepSize said, MepEdge e)
    {
        var want = said.IsRound ? said.DiameterMm : said.WidthMm;
        bool Near(double have) => have > 0 && Math.Abs(have - want) <= Math.Max(20, want * 0.1);
        return Near(e.Size.IsRound ? e.Size.DiameterMm : e.Size.WidthMm) || Near(e.DrawnWidthMm);
    }

    /// <summary>A note an MEP run may take a size, a fall or an elevation from.</summary>
    public static bool IsNote(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text!.Length <= 120 &&
        (Rect.IsMatch(text) || Round.IsMatch(text) || FallLabel.IsMatch(text) || TryParseElevation(text, out _));

    /// <summary>Notes this close together are one stack, written one over the other.</summary>
    private const double StackMm = 1200;
    /// <summary>Runs side by side a stack of notes speaks of: each this much farther than the one before, at most.</summary>
    private const double StackRunMm = 2500;

    /// <summary>
    /// The elevation written on a run goes to the run nearest to it. Notes stacked by two or more runs side by side
    /// (THANG CÁP 1200X100, H=3100 over THANG CÁP 1200X100, H=2800 by two trays) go in order: the note nearest the
    /// top (or left) to the run nearest the top (or left), sizes with them. Runs with none carry on the elevation of
    /// the run before them, through bends and joints and straight on through tees, then into branches.
    /// A note naming another kind of run (THANG CÁP by a duct, from an xref) or another size is not this run's.
    /// </summary>
    private static void Elevations(MepNetwork net, List<CadText> texts, bool? doubleLine, bool busway = false)
    {
        var notes = new List<(double X, double Y, double Mm, CadText Text)>();
        foreach (var t in texts)
        {
            if (TryParseElevation(t.Value, out var mm) && OfKind(t.Value, doubleLine, busway))
                notes.Add((t.CenterX ?? t.X, t.CenterY ?? t.Y, mm, t));
        }

        if (notes.Count == 0)
            return;
        var all = net.Edges.Where(e => !e.IsTransition && Length(net, e) >= 1).ToList();
        if (all.Count == 0)
            return;
        // Runs of the size a note names (any run when it names none).
        List<MepEdge> For(IEnumerable<(double X, double Y, double Mm, CadText Text)> said)
        {
            var sizes = said.Select(n => TryParseSize(n.Text.Value, out var s) ? s : (MepSize?)null).ToList();
            return sizes.Any(s => s is null) ? all : all.Where(e => sizes.Any(s => Fits(s!.Value, e))).ToList();
        }

        var edges = all;
        if (edges.Count == 0)
            return;
        // Distance from a note to a run, past its side and its ends.
        double Gap(MepEdge e, double x, double y)
        {
            var (x0, y0) = net.Nodes[e.A];
            var (x1, y1) = net.Nodes[e.B];
            var len = Math.Sqrt(Sq(x1 - x0) + Sq(y1 - y0));
            var t = ((x - x0) * (x1 - x0) + (y - y0) * (y1 - y0)) / (len * len);
            var across = Math.Max(0, Math.Abs((x - x0) * -(y1 - y0) / len + (y - y0) * (x1 - x0) / len) - e.DrawnWidthMm / 2);
            var past = Math.Max(0, Math.Max(-t, t - 1)) * len;
            return Math.Sqrt(across * across + past * past);
        }

        void Nearest((double X, double Y, double Mm, CadText Text) n)
        {
            var only = For(new[] { n }).Select(e => (E: e, D: Gap(e, n.X, n.Y))).OrderBy(x => x.D).FirstOrDefault();
            if (only.E is not null && only.D <= LabelReachMm)
                only.E.ElevationMm = n.Mm;
        }

        void Label(MepEdge e, string text, double mm)
        {
            e.ElevationMm = mm;
            if (TryParseSize(text, out var size) && e.SizeFrom != "layer")
            {
                e.Size = size;
                e.SizeFrom = "text";
            }
        }

        // Two notes by two runs at different heights: which went where, to check against the runs' corners.
        var pairs = new List<(MepEdge First, MepEdge Second, string TextA, double MmA, string TextB, double MmB)>();

        // Stacks: notes one over the other.
        var stackOf =Enumerable.Range(0, notes.Count).ToArray();
        int Root(int i) => stackOf[i] == i ? i : stackOf[i] = Root(stackOf[i]);
        for (var i = 0; i < notes.Count; i++)
        for (var j = i + 1; j < notes.Count; j++)
        {
            if (Math.Sqrt(Sq(notes[i].X - notes[j].X) + Sq(notes[i].Y - notes[j].Y)) <= StackMm)
                stackOf[Root(i)] = Root(j);
        }

        foreach (var stack in Enumerable.Range(0, notes.Count).GroupBy(Root).Select(g => g.Select(i => notes[i]).ToList()))
        {
            if (stack.Count == 1)
            {
                Nearest(stack[0]);
                continue;
            }

            double cx = stack.Average(n => n.X), cy = stack.Average(n => n.Y);
            // The runs beside the stack: in reach of it, the farther ones a run's width or so beyond the nearest.
            var nearest = For(stack).Select(e => (E: e, D: Gap(e, cx, cy))).OrderBy(x => x.D).ToList();
            if (nearest.Count == 0 || nearest[0].D > LabelReachMm + StackMm)
                continue;
            var limit = nearest[0].D + (stack.Count - 1) * StackRunMm;
            nearest = nearest.Where(x => x.D <= limit).ToList();
            // As many runs as notes, in line with the nearest one, each a separate run beside the others.
            var first = nearest[0].E;
            var (ax, ay) = net.Nodes[first.A];
            var (bx, by) = net.Nodes[first.B];
            var l0 = Math.Sqrt(Sq(bx - ax) + Sq(by - ay));
            double ux = (bx - ax) / l0, uy = (by - ay) / l0;
            // Across: the normal pointing up (left for a run up the sheet), so the first is the top one.
            double nx = -uy, ny = ux;
            if (ny < -1e-9 || (Math.Abs(ny) <= 1e-9 && nx > 0))
                (nx, ny) = (-nx, -ny);
            double Across(double x, double y) => (x - ax) * nx + (y - ay) * ny;
            var runs = new List<(MepEdge E, double At)>();
            foreach (var (e, _) in nearest)
            {
                var (ex0, ey0) = net.Nodes[e.A];
                var (ex1, ey1) = net.Nodes[e.B];
                var le = Math.Sqrt(Sq(ex1 - ex0) + Sq(ey1 - ey0));
                if (Math.Abs(ux * (ey1 - ey0) / le - uy * (ex1 - ex0) / le) > ParallelSin)
                    continue;
                var at = Across((ex0 + ex1) / 2, (ey0 + ey1) / 2);
                if (runs.Any(r => Math.Abs(r.At - at) < 1))
                    continue;
                runs.Add((e, at));
                if (runs.Count == stack.Count)
                    break;
            }

            if (runs.Count != stack.Count)
            {
                // Can't tell which is which: each note to its nearest run.
                foreach (var n in stack)
                    Nearest(n);
                continue;
            }

            var ordered = runs.OrderByDescending(r => r.At).ToList();
            var labels = stack.OrderByDescending(n => Across(n.X, n.Y)).ToList();
            for (var k = 0; k < labels.Count; k++)
                Label(ordered[k].E, labels[k].Text.Value, labels[k].Mm);
            if (labels.Count == 2 && Math.Abs(labels[0].Mm - labels[1].Mm) > 1)
                pairs.Add((ordered[0].E, ordered[1].E, labels[0].Text.Value, labels[0].Mm, labels[1].Text.Value, labels[1].Mm));
        }

        KeepPairsContinuous(net, pairs, Label);

        var adjacent = Adjacency(net);
        foreach (var anyWay in new[] { false, true })
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var e in net.Edges.Where(x => x.ElevationMm is null && !x.IsTransition))
                {
                    foreach (var node in new[] { e.A, e.B })
                    {
                        var other = adjacent[node].FirstOrDefault(x => x != e && x.ElevationMm is not null &&
                                                                       (anyWay || adjacent[node].Count == 2 || Straight(net, e, x, node)));
                        if (other is null)
                            continue;
                        e.ElevationMm = other.ElevationMm;
                        changed = true;
                        break;
                    }
                }
            }
        }

        foreach (var t in net.Edges.Where(x => x.IsTransition))
            t.ElevationMm = adjacent[t.A].Concat(adjacent[t.B]).FirstOrDefault(x => x != t && x.ElevationMm is not null)?.ElevationMm;
    }

    /// <summary>
    /// Pairs of runs side by side at two heights, labelled by stacked notes, keep their heights round corners: a tray
    /// does not swap height with its neighbour at a bend. Where two pairs' note order says otherwise (2800 on the inner
    /// tray along one stretch, on the outer one round the corner), the order that keeps every run continuous through
    /// its bends and joints wins, the one most pairs read as written.
    /// </summary>
    private static void KeepPairsContinuous(MepNetwork net,
        List<(MepEdge First, MepEdge Second, string TextA, double MmA, string TextB, double MmB)> pairs,
        Action<MepEdge, string, double> label)
    {
        if (pairs.Count < 2 || pairs.Count > 16)
            return;
        // Runs one after the other through plain joints (a node of two runs) are one run.
        var degree = new int[net.Nodes.Count];
        foreach (var e in net.Edges)
        {
            degree[e.A]++;
            degree[e.B]++;
        }

        var root = Enumerable.Range(0, net.Edges.Count).ToArray();
        int Find(int i) => root[i] == i ? i : root[i] = Find(root[i]);
        var firstAt = new Dictionary<int, int>();
        for (var i = 0; i < net.Edges.Count; i++)
        {
            foreach (var node in new[] { net.Edges[i].A, net.Edges[i].B })
            {
                if (degree[node] != 2 || net.Edges[i].IsTransition)
                    continue;
                if (firstAt.TryGetValue(node, out var other))
                    root[Find(i)] = Find(other);
                else
                    firstAt[node] = i;
            }
        }

        var index = net.Edges.Select((e, i) => (e, i)).ToDictionary(x => x.e, x => x.i);
        int Chain(MepEdge e) => Find(index[e]);
        // Height of each run of pair p, swapped or not.
        double Height(int p, bool second, bool swapped) =>
            second != swapped ? pairs[p].MmB : pairs[p].MmA;
        var best = -1;
        var bestSwaps = int.MaxValue;
        for (var mask = 0; mask < 1 << pairs.Count; mask++)
        {
            var at = new Dictionary<int, double>();
            var ok = true;
            for (var p = 0; p < pairs.Count && ok; p++)
            {
                var swapped = (mask >> p & 1) == 1;
                foreach (var second in new[] { false, true })
                {
                    var chain = Chain(second ? pairs[p].Second : pairs[p].First);
                    var h = Height(p, second, swapped);
                    if (at.TryGetValue(chain, out var seen) && Math.Abs(seen - h) > 1)
                        ok = false;
                    at[chain] = h;
                }
            }

            var swaps = Enumerable.Range(0, pairs.Count).Count(p => (mask >> p & 1) == 1);
            if (ok && swaps < bestSwaps)
                (best, bestSwaps) = (mask, swaps);
        }

        // No order keeps them all continuous, or the notes already do: as written. A tie (half the pairs swapped)
        // can't be told either way: as written too.
        if (best <= 0 || bestSwaps * 2 >= pairs.Count)
            return;
        for (var p = 0; p < pairs.Count; p++)
        {
            if ((best >> p & 1) == 0)
                continue;
            label(pairs[p].First, pairs[p].TextB, pairs[p].MmB);
            label(pairs[p].Second, pairs[p].TextA, pairs[p].MmA);
        }
    }

    /// <summary>
    /// Trays crossing on plan at one height (or one with none written) meet there: the fitting drawn at the crossing
    /// is dropped with the other short strokes, so the two runs only cross. A free end just past the other tray (by
    /// up to that tray's width) stops on it as a tee; runs carrying on past each other are cut into a cross. Trays at
    /// different heights pass one over the other and stay apart.
    /// </summary>
    private static void JoinCrossings(MepNetwork net)
    {
        for (var guard = 0; guard < 1000; guard++)
        {
            var found = false;
            var degree = new int[net.Nodes.Count];
            foreach (var e in net.Edges)
            {
                degree[e.A]++;
                degree[e.B]++;
            }

            for (var i = 0; i < net.Edges.Count && !found; i++)
            for (var j = i + 1; j < net.Edges.Count && !found; j++)
            {
                MepEdge e = net.Edges[i], f = net.Edges[j];
                if (e.IsTransition || f.IsTransition || e.A == f.A || e.A == f.B || e.B == f.A || e.B == f.B)
                    continue;
                if (e.ElevationMm is double he && f.ElevationMm is double hf && Math.Abs(he - hf) > 1)
                    continue;
                if (!Crossing(net, e, f, out var x, out var y))
                    continue;
                found = true;
                var at = net.Nodes.Count;
                net.Nodes.Add((x, y));
                Array.Resize(ref degree, net.Nodes.Count);
                Meet(e, f);
                Meet(f, e);
                e.ElevationMm ??= f.ElevationMm;
                f.ElevationMm ??= e.ElevationMm;

                void Meet(MepEdge run, MepEdge across)
                {
                    var reach = Math.Max(300, across.DrawnWidthMm);
                    foreach (var atStart in new[] { true, false })
                    {
                        var end = atStart ? run.A : run.B;
                        var (ex, ey) = net.Nodes[end];
                        if (degree[end] != 1 || Math.Sqrt(Sq(ex - x) + Sq(ey - y)) > reach)
                            continue;
                        if (atStart)
                            run.A = at;
                        else
                            run.B = at;
                        return;
                    }

                    net.Edges.Add(new MepEdge
                    {
                        A = at, B = run.B, Size = run.Size, Layer = run.Layer, SizeFrom = run.SizeFrom,
                        DrawnWidthMm = run.DrawnWidthMm, SlopePercent = run.SlopePercent, ElevationMm = run.ElevationMm
                    });
                    run.B = at;
                }
            }

            if (!found)
                return;
        }
    }

    /// <summary>Where two runs cross, more than 1 mm inside each.</summary>
    private static bool Crossing(MepNetwork net, MepEdge e, MepEdge f, out double x, out double y)
    {
        x = y = 0;
        var (x1, y1) = net.Nodes[e.A];
        var (x2, y2) = net.Nodes[e.B];
        var (x3, y3) = net.Nodes[f.A];
        var (x4, y4) = net.Nodes[f.B];
        var d = (x2 - x1) * (y4 - y3) - (y2 - y1) * (x4 - x3);
        double l1 = Math.Sqrt(Sq(x2 - x1) + Sq(y2 - y1)), l2 = Math.Sqrt(Sq(x4 - x3) + Sq(y4 - y3));
        if (l1 < 1 || l2 < 1 || Math.Abs(d) < 1e-6 * l1 * l2)
            return false;
        var t = ((x3 - x1) * (y4 - y3) - (y3 - y1) * (x4 - x3)) / d;
        var u = ((x3 - x1) * (y2 - y1) - (y3 - y1) * (x2 - x1)) / d;
        if (t * l1 <= 1 || (1 - t) * l1 <= 1 || u * l2 <= 1 || (1 - u) * l2 <= 1)
            return false;
        x = x1 + t * (x2 - x1);
        y = y1 + t * (y2 - y1);
        return true;
    }

    private static readonly Regex FallLabel = new(@"\bi\s*=\s*(\d+(?:[.,]\d+)?)\s*%", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The fall in a run's own label: size, fall, length (D110-1%, D200-0.5%-24.4M, D75-2%%%-5.4M).</summary>
    private static readonly Regex SizeFallLabel = new(@"(?:^|[^A-Za-z])(?:DN|D|Ø|%%C)\s*\d+(?:[.,]\d+)?\s*-\s*(\d+(?:[.,]\d+)?)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The fall written on a run goes to the run nearest to it, then on through plain joints (a node of two runs) to
    /// the runs after it that have none of their own. The run's own label (D110-1%) wins over a lone i=1%; an i=..%
    /// on an xref's layer (the architect's floor falls, "XR …$0$…") is not a pipe's.
    /// </summary>
    private static void Falls(MepNetwork net, List<CadText> texts)
    {
        var falls = texts.Select(t => (Text: t, Own: SizeFallLabel.Match(t.Value), Lone: FallLabel.Match(t.Value)))
            .Where(x => x.Own.Success || (x.Lone.Success && x.Text.Layer.IndexOf("$0$", StringComparison.Ordinal) < 0))
            .OrderBy(x => x.Own.Success ? 1 : 0)
            .ToList();
        var labelled = new Dictionary<MepEdge, double>();
        foreach (var (text, own, lone) in falls)
        {
            var m = own.Success ? own : lone;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
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

            if (best is null)
                continue;
            best.SlopePercent = percent;
            if (own.Success)
                labelled[best] = percent;
        }

        // The run's own label (D200-0.5%-23.6M) names the whole straight run of that size, through the tees branches
        // join it at: each piece straight on from the labelled one, of its size, takes the fall, up to a piece with a
        // label of its own.
        foreach (var pair in labelled)
            foreach (var piece in StraightRun(net, pair.Key).Where(x => !labelled.ContainsKey(x)))
                piece.SlopePercent = pair.Value;

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
    /// A size stacked over a flow ("DN32" over "0.80 L/S", the pipe on the flow's side): the size is also read at the
    /// flow's place, so it reaches the pipe the flow sits by (the DN line alone is often beyond label reach).
    /// </summary>
    internal static List<CadText> WithStackedSizes(List<CadText> texts)
    {
        var flows = texts.Where(t => FlowLabel.IsMatch(t.Value) && !TryParseSize(t.Value, out _)).ToList();
        if (flows.Count == 0)
            return texts;
        var added = new List<CadText>();
        foreach (var t in texts)
        {
            if (!TryParseSize(t.Value, out _) || FlowLabel.IsMatch(t.Value))
                continue;
            var (x, y) = (t.CenterX ?? t.X, t.CenterY ?? t.Y);
            var flow = flows.Select(f => (F: f, D: Math.Sqrt(Sq((f.CenterX ?? f.X) - x) + Sq((f.CenterY ?? f.Y) - y))))
                .Where(f => f.D <= 1000).OrderBy(f => f.D).Select(f => f.F).FirstOrDefault();
            if (flow is not null)
                added.Add(new CadText { Layer = t.Layer, Value = t.Value, X = flow.X, Y = flow.Y, CenterX = flow.CenterX, CenterY = flow.CenterY, RotationDeg = t.RotationDeg });
        }

        return added.Count == 0 ? texts : texts.Concat(added).ToList();
    }

    /// <summary>A flow written on a pipe: 0.90 L/S, 1,6 l/s, Q=0.45L/S.</summary>
    private static readonly Regex FlowLabel = new(@"(\d+(?:[.,]\d+)?)\s*L\s*/\s*S\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The flow written on a run (stacked under its DN, "0.90 L/S") goes to the run nearest to it within label reach,
    /// then along the straight run of that size up to a piece with a flow of its own (Revit's Additional Flow).
    /// </summary>
    private static void Flows(MepNetwork net, List<CadText> texts)
    {
        var labelled = new Dictionary<MepEdge, double>();
        foreach (var text in texts)
        {
            var m = FlowLabel.Match(text.Value);
            if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lps) || lps <= 0)
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

            if (best is null)
                continue;
            best.FlowLps = lps;
            labelled[best] = lps;
        }

        foreach (var pair in labelled)
            foreach (var piece in StraightRun(net, pair.Key).Where(x => !labelled.ContainsKey(x) && x.FlowLps is null))
                piece.FlowLps = pair.Value;
        FlowsThroughBends(net);
    }

    /// <summary>
    /// Pipes drawn with both walls (a chilled water plan at large scale) as single lines, so they route like any pipe:
    /// each wall paired with its mutually nearest parallel wall (8–400 mm apart, overlapping) gives the centreline over
    /// their overlap; unpaired strokes (end caps, fitting outlines) are dropped. <paramref name="twoLine"/>: most of the
    /// length pairs, so the drawing is two-line; otherwise the strokes are returned as drawn.
    /// </summary>
    public static List<CadEntity> PipeCenterlines(IEnumerable<CadEntity> strokes, out bool twoLine) =>
        PipeCenterlines(strokes, out twoLine, out _);

    /// <summary>As above, with each centreline's wall-to-wall width (the pipe's outside diameter as drawn).</summary>
    public static List<CadEntity> PipeCenterlines(IEnumerable<CadEntity> strokes, out bool twoLine, out List<(CadLine Axis, double WidthMm)> widths)
    {
        widths = new List<(CadLine Axis, double WidthMm)>();
        var all = strokes.ToList();
        var segs = new List<(double X0, double Y0, double X1, double Y1, string Layer)>();
        foreach (var e in all)
        {
            if (e is CadLine l)
                segs.Add((l.X0, l.Y0, l.X1, l.Y1, l.Layer));
            else if (e is CadPolyline { FromArc: false } poly && (poly.Bulges is null || poly.Bulges.All(x => Math.Abs(x) < 1e-6)))
            {
                var n = poly.Points.Count;
                for (var i = 0; i < (poly.Closed ? n : n - 1); i++)
                    segs.Add((poly.Points[i].X, poly.Points[i].Y, poly.Points[(i + 1) % n].X, poly.Points[(i + 1) % n].Y, poly.Layer));
            }
        }

        segs = segs.Where(x => Math.Sqrt(Sq(x.X1 - x.X0) + Sq(x.Y1 - x.Y0)) >= 50).ToList();
        const double MinGap = 8, MaxGap = 400;
        var cells = new Buckets(1000);
        for (var k = 0; k < segs.Count; k++)
            cells.Add(k, Math.Min(segs[k].X0, segs[k].X1), Math.Min(segs[k].Y0, segs[k].Y1), Math.Max(segs[k].X0, segs[k].X1), Math.Max(segs[k].Y0, segs[k].Y1));

        // A parallel stroke of the same layer facing this one: its signed distance and the stretch along this one.
        (bool Ok, double D, double S0, double S1) Facing(int i, int j)
        {
            var a = segs[i];
            var c = segs[j];
            var len = Math.Sqrt(Sq(a.X1 - a.X0) + Sq(a.Y1 - a.Y0));
            double ux = (a.X1 - a.X0) / len, uy = (a.Y1 - a.Y0) / len;
            var lc = Math.Sqrt(Sq(c.X1 - c.X0) + Sq(c.Y1 - c.Y0));
            double vx = (c.X1 - c.X0) / lc, vy = (c.Y1 - c.Y0) / lc;
            if (Math.Abs(ux * vy - uy * vx) > 0.02 || !string.Equals(a.Layer, c.Layer, StringComparison.OrdinalIgnoreCase))
                return (false, 0, 0, 0);
            var d = (c.X0 - a.X0) * -uy + (c.Y0 - a.Y0) * ux;
            double t0 = (c.X0 - a.X0) * ux + (c.Y0 - a.Y0) * uy, t1 = (c.X1 - a.X0) * ux + (c.Y1 - a.Y0) * uy;
            double s0 = Math.Max(0, Math.Min(t0, t1)), s1 = Math.Min(len, Math.Max(t0, t1));
            if (Math.Abs(d) < MinGap || Math.Abs(d) > MaxGap || s1 - s0 < Math.Max(30, Math.Abs(d)))
                return (false, 0, 0, 0);
            return (true, d, s0, s1);
        }

        var nearest = new double[segs.Count];
        var facing = new List<(int J, double D, double S0, double S1)>[segs.Count];
        for (var i = 0; i < segs.Count; i++)
        {
            facing[i] = new List<(int, double, double, double)>();
            var a = segs[i];
            foreach (var j in cells.Query(Math.Min(a.X0, a.X1) - MaxGap, Math.Min(a.Y0, a.Y1) - MaxGap, Math.Max(a.X0, a.X1) + MaxGap, Math.Max(a.Y0, a.Y1) + MaxGap).Distinct())
            {
                if (j == i)
                    continue;
                var (ok, d, s0, s1) = Facing(i, j);
                if (ok)
                    facing[i].Add((j, d, s0, s1));
            }

            nearest[i] = facing[i].Count == 0 ? double.MaxValue : facing[i].Min(f => Math.Abs(f.D));
        }

        var axes = new List<CadEntity>();
        double total = segs.Sum(x => Math.Sqrt(Sq(x.X1 - x.X0) + Sq(x.Y1 - x.Y0))), paired = 0;
        for (var i = 0; i < segs.Count; i++)
        {
            foreach (var (j, d, s0, s1) in facing[i])
            {
                // Mutually nearest walls: a pipe's two walls, not one wall of each of two pipes side by side.
                if (Math.Abs(Math.Abs(d) - nearest[i]) > 2 || Math.Abs(Math.Abs(d) - nearest[j]) > 2)
                    continue;
                paired += s1 - s0;
                if (j < i)
                    continue;
                var a = segs[i];
                var len = Math.Sqrt(Sq(a.X1 - a.X0) + Sq(a.Y1 - a.Y0));
                double ux = (a.X1 - a.X0) / len, uy = (a.Y1 - a.Y0) / len, nx = -uy * d / 2, ny = ux * d / 2;
                var axis = new CadLine
                {
                    Layer = a.Layer,
                    X0 = a.X0 + ux * s0 + nx,
                    Y0 = a.Y0 + uy * s0 + ny,
                    X1 = a.X0 + ux * s1 + nx,
                    Y1 = a.Y0 + uy * s1 + ny
                };
                axes.Add(axis);
                widths.Add((axis, Math.Abs(d)));
            }
        }

        twoLine = total > 0 && paired >= 0.6 * total;
        if (twoLine)
            JoinPipeAxes(widths);
        return twoLine ? axes : all;
    }

    /// <summary>
    /// Centrelines of two-line pipes meet where the drawing joins them, beyond the 300 mm the router bridges: a bend
    /// drawn round stops both walls a bend radius short of the corner (≈ 1.5 DN, 450 mm on a DN300), and a valve or a
    /// coupling drawn on a run breaks it. Free ends facing each other on one line (same width) are closed across the gap
    /// (up to 3 × the width, at least 600 mm); two ends whose lines cross just ahead of both (within 2.5 × the width, at
    /// least 300 mm) are drawn on to that corner. Each end joins once, nearest first.
    /// </summary>
    private static void JoinPipeAxes(List<(CadLine Axis, double WidthMm)> axes)
    {
        (double X, double Y) End(int i, int k) => k == 0 ? (axes[i].Axis.X0, axes[i].Axis.Y0) : (axes[i].Axis.X1, axes[i].Axis.Y1);
        (double X, double Y) Out(int i, int k)
        {
            var a = axes[i].Axis;
            var len = Math.Sqrt(Sq(a.X1 - a.X0) + Sq(a.Y1 - a.Y0));
            return k == 1 ? ((a.X1 - a.X0) / len, (a.Y1 - a.Y0) / len) : ((a.X0 - a.X1) / len, (a.Y0 - a.Y1) / len);
        }

        var cells = new Buckets(1000);
        var ends = new List<(int I, int K)>();
        for (var i = 0; i < axes.Count; i++)
            for (var k = 0; k < 2; k++)
            {
                var (x, y) = End(i, k);
                cells.Add(ends.Count, x, y, x, y);
                ends.Add((i, k));
            }

        var options = new List<(double Cost, int A, int B, double X, double Y)>();
        for (var a = 0; a < ends.Count; a++)
        {
            var (i, ki) = ends[a];
            var (px, py) = End(i, ki);
            var (ux, uy) = Out(i, ki);
            var wi = axes[i].WidthMm;
            const double far = 1200;
            foreach (var b in cells.Query(px - far, py - far, px + far, py + far).Distinct())
            {
                var (j, kj) = ends[b];
                if (b <= a || j == i)
                    continue;
                var (qx, qy) = End(j, kj);
                var (vx, vy) = Out(j, kj);
                var w = Math.Max(wi, axes[j].WidthMm);
                var cross = ux * vy - uy * vx;
                if (Math.Abs(cross) < 0.02)
                {
                    // On one line, facing each other, the same pipe: close the gap.
                    var gap = (qx - px) * ux + (qy - py) * uy;
                    var side = Math.Abs((qx - px) * -uy + (qy - py) * ux);
                    if (ux * vx + uy * vy > -0.99 || gap < 0 || gap > Math.Max(600, 3 * w) || side > Math.Max(5, w / 4)
                        || Math.Abs(wi - axes[j].WidthMm) > 0.12 * w)
                        continue;
                    options.Add((gap, a, b, (px + qx) / 2, (py + qy) / 2));
                    continue;
                }

                // A corner: the two lines cross just ahead of both ends.
                var t = ((qx - px) * vy - (qy - py) * vx) / cross;
                var xx = px + ux * t;
                var xy = py + uy * t;
                var tj = (xx - qx) * vx + (xy - qy) * vy;
                var reach = Math.Max(300, 2.5 * w);
                if (t < -w / 2 || tj < -w / 2 || t > reach || tj > reach)
                    continue;
                options.Add((Math.Max(t, 0) + Math.Max(tj, 0), a, b, xx, xy));
            }
        }

        var used = new HashSet<int>();
        foreach (var (_, a, b, x, y) in options.OrderBy(o => o.Cost))
        {
            if (used.Contains(a) || used.Contains(b))
                continue;
            used.Add(a);
            used.Add(b);
            foreach (var e in new[] { ends[a], ends[b] })
            {
                var line = axes[e.I].Axis;
                if (e.K == 0)
                {
                    line.X0 = x;
                    line.Y0 = y;
                }
                else
                {
                    line.X1 = x;
                    line.Y1 = y;
                }
            }
        }
    }

    /// <summary>
    /// A flow carried on through bends (a node of two runs of one size): the short leg of a branch takes the flow
    /// written on its long leg.
    /// </summary>
    private static void FlowsThroughBends(MepNetwork net)
    {
        var adjacent = Adjacency(net);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var e in net.Edges.Where(x => x.FlowLps is null && !x.IsTransition))
            {
                foreach (var node in new[] { e.A, e.B })
                {
                    var at = adjacent[node].Where(x => x != e).ToList();
                    if (at.Count != 1 || at[0].FlowLps is not { } carried || !at[0].Size.Equals(e.Size))
                        continue;
                    e.FlowLps = carried;
                    changed = true;
                    break;
                }
            }
        }
    }

    /// <summary>Steel pipe outside diameters (mm) by DN: what two walls drawn this far apart are.</summary>
    private static readonly (int Dn, double Od)[] SteelOd =
    {
        (15, 21.3), (20, 26.7), (25, 33.4), (32, 42.2), (40, 48.3), (50, 60.3), (65, 73.0), (80, 88.9), (100, 114.3),
        (125, 141.3), (150, 168.3), (200, 219.1), (250, 273.0), (300, 323.9), (350, 355.6), (400, 406.4)
    };

    /// <summary>The DN whose outside diameter two walls this far apart draw (within 12 %), else null.</summary>
    public static int? DnForOutside(double widthMm)
    {
        var best = SteelOd.OrderBy(x => Math.Abs(Math.Log(widthMm / x.Od))).First();
        return Math.Abs(widthMm / best.Od - 1) <= 0.12 ? best.Dn : null;
    }

    /// <summary>
    /// Pipes drawn with both walls: the walls say the size. Each run lying on a centreline takes the DN of its
    /// wall-to-wall width (DN25 for 33.4 mm, DN300 for 324 mm), whatever label is nearest; then each flow ("0.33 L/S",
    /// with the "DN25" stacked over it) goes to the nearest run of that DN, not merely the nearest run, and on along its
    /// straight run. A flow whose DN no run near it has is left out rather than put on the wrong pipe.
    /// </summary>
    public static (int Sized, int Flows) SizeByWalls(MepNetwork net, List<(CadLine Axis, double WidthMm)> widths, IEnumerable<CadText> texts)
    {
        var sized = 0;
        foreach (var e in net.Edges.Where(x => !x.IsTransition))
        {
            var (ax, ay) = net.Nodes[e.A];
            var (bx, by) = net.Nodes[e.B];
            var len = Math.Sqrt(Sq(bx - ax) + Sq(by - ay));
            if (len < 1)
                continue;
            double mx = (ax + bx) / 2, my = (ay + by) / 2, ux = (bx - ax) / len, uy = (by - ay) / len;
            foreach (var (axis, w) in widths)
            {
                var al = Math.Sqrt(Sq(axis.X1 - axis.X0) + Sq(axis.Y1 - axis.Y0));
                if (al < 1)
                    continue;
                double vx = (axis.X1 - axis.X0) / al, vy = (axis.Y1 - axis.Y0) / al;
                if (Math.Abs(ux * vy - uy * vx) > 0.05)
                    continue;
                var t = ((mx - axis.X0) * vx + (my - axis.Y0) * vy) / al;
                if (t < -0.01 || t > 1.01 || Math.Abs((mx - axis.X0) * -vy + (my - axis.Y0) * vx) > Math.Max(5, w / 4))
                    continue;
                e.DrawnWidthMm = w;
                if (DnForOutside(w) is int dn && TryParseSize($"DN{dn}", out var size))
                {
                    e.Size = size;
                    e.SizeFrom = "drawn";
                    sized++;
                }

                break;
            }
        }

        // Flows again, each to a run of its own DN.
        var list = texts.ToList();
        var sizeTexts = list.Where(t => TryParseSize(t.Value, out _) && !FlowLabel.IsMatch(t.Value)).ToList();
        foreach (var e in net.Edges)
            e.FlowLps = null;
        var labelled = new Dictionary<MepEdge, double>();
        foreach (var text in list)
        {
            var m = FlowLabel.Match(text.Value);
            if (!m.Success || TryParseSize(text.Value, out _) || !double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lps) || lps <= 0)
                continue;
            var tx = text.CenterX ?? text.X;
            var ty = text.CenterY ?? text.Y;
            var dnText = sizeTexts.Select(t => (T: t, D: Math.Sqrt(Sq((t.CenterX ?? t.X) - tx) + Sq((t.CenterY ?? t.Y) - ty))))
                .Where(x => x.D <= 1000).OrderBy(x => x.D).Select(x => x.T).FirstOrDefault();
            MepSize? want = dnText is not null && TryParseSize(dnText.Value, out var ws) ? ws : null;
            if (dnText is not null)
            {
                // The tag's middle (between "DN50" and "2.45 L/S") says which side of a tee it stands on.
                tx = (tx + (dnText.CenterX ?? dnText.X)) / 2;
                ty = (ty + (dnText.CenterY ?? dnText.Y)) / 2;
            }
            MepEdge? best = null;
            var bestD = want is null ? LabelReachMm : 2 * LabelReachMm;
            foreach (var e in net.Edges.Where(x => !x.IsTransition))
            {
                if (want is { } wz && (e.SizeFrom != "drawn" || Math.Abs(e.Size.DiameterMm - wz.DiameterMm) > 0.5))
                    continue;
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

            if (best is null)
                continue;
            best.FlowLps = lps;
            labelled[best] = lps;
        }

        foreach (var pair in labelled)
            foreach (var piece in StraightRun(net, pair.Key).Where(x => !labelled.ContainsKey(x) && x.FlowLps is null))
                piece.FlowLps = pair.Value;
        FlowsThroughBends(net);
        return (sized, net.Edges.Count(e => e.FlowLps is not null));
    }

    /// <summary>
    /// The pieces straight on from <paramref name="start"/> both ways (within 2°, same size and layer), through any
    /// node: a straight run split where branches join it. <paramref name="start"/> itself is not returned.
    /// </summary>
    private static List<MepEdge> StraightRun(MepNetwork net, MepEdge start)
    {
        var adjacent = Adjacency(net);
        var run = new List<MepEdge>();
        var seen = new HashSet<MepEdge> { start };
        foreach (var from in new[] { start.A, start.B })
        {
            var (edge, node) = (start, from);
            while (true)
            {
                var back = edge.A == node ? edge.B : edge.A;
                var (dx, dy) = (net.Nodes[node].X - net.Nodes[back].X, net.Nodes[node].Y - net.Nodes[back].Y);
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1)
                    break;
                MepEdge? next = null;
                foreach (var e in adjacent[node])
                {
                    if (seen.Contains(e) || e.IsTransition || !e.Size.Equals(start.Size) || e.Layer != start.Layer)
                        continue;
                    var far = e.A == node ? e.B : e.A;
                    var (ex, ey) = (net.Nodes[far].X - net.Nodes[node].X, net.Nodes[far].Y - net.Nodes[node].Y);
                    var el = Math.Sqrt(ex * ex + ey * ey);
                    if (el >= 1 && (dx * ex + dy * ey) / (len * el) > Math.Cos(2 * Math.PI / 180))
                    {
                        next = e;
                        break;
                    }
                }

                if (next is null)
                    break;
                seen.Add(next);
                run.Add(next);
                (edge, node) = (next, next.A == node ? next.B : next.A);
            }
        }

        return run;
    }

    /// <summary>
    /// How far each node a network reaches stands above <paramref name="outlet"/> (mm): along the shortest way there,
    /// each run falling by its own written fall, else <paramref name="slopePercent"/>.
    /// </summary>
    public static Dictionary<int, double> RisesFrom(MepNetwork net, int outlet, double slopePercent) =>
        RisesFrom(net, new[] { outlet }, slopePercent);

    /// <summary>
    /// As above for a network that drains to several outlets (one to each septic tank or manhole): each node falls
    /// toward the outlet nearest to it along the pipe.
    /// </summary>
    public static Dictionary<int, double> RisesFrom(MepNetwork net, IReadOnlyCollection<int> outlets, double slopePercent)
    {
        var adjacent = Adjacency(net);
        var dist = outlets.Distinct().ToDictionary(o => o, _ => 0.0);
        var rise = outlets.Distinct().ToDictionary(o => o, _ => 0.0);
        var open = new SortedSet<(double D, int N)>(dist.Keys.Select(o => (0.0, o)));
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
            // A louver's or fan's hood (four corners, not square) standing on its own: a reducer drawn as a box shares
            // its corners with the ducts either side.
            if (big <= MaxDeviceMm && !Square((CadPolyline)list[c.I]) &&
                ((CadPolyline)list[c.I]).Points.Count(q => Shared(c.I, q.X, q.Y, (c.MinX, c.MinY, c.MaxX, c.MaxY))) < 2)
            {
                drop[c.I] = true;
                outlines.Add((c.MinX, c.MinY, c.MaxX, c.MaxY));
                continue;
            }

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

    /// <summary>
    /// A closed outline of four corners all square (a box), or of any other count (left to the other tests); false
    /// only for a four-cornered one with a corner off square (a trapezoid: a hood, a taper).
    /// </summary>
    private static bool Square(CadPolyline p)
    {
        var pts = new List<(double X, double Y)>();
        foreach (var q in p.Points)
        {
            if (pts.Count == 0 || Math.Abs(pts[pts.Count - 1].X - q.X) + Math.Abs(pts[pts.Count - 1].Y - q.Y) > 5)
                pts.Add((q.X, q.Y));
        }

        if (pts.Count > 1 && Math.Abs(pts[0].X - pts[pts.Count - 1].X) + Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) <= 5)
            pts.RemoveAt(pts.Count - 1);
        if (pts.Count != 4)
            return true;
        for (var k = 0; k < 4; k++)
        {
            var (ax, ay) = pts[(k + 3) % 4];
            var (bx, by) = pts[k];
            var (cx, cy) = pts[(k + 1) % 4];
            double ux = bx - ax, uy = by - ay, vx = cx - bx, vy = cy - by;
            var lu = Math.Sqrt(ux * ux + uy * uy);
            var lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1 || lv < 1 || Math.Abs(ux * vx + uy * vy) / (lu * lv) > 0.05)
                return false;
        }

        return true;
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

        bool ArcAt(double x, double y) => arcEnds.Query(x - 5, y - 5, x + 5, y + 5)
            .Any(k => Math.Abs(arcPoints[k].X - x) <= 5 && Math.Abs(arcPoints[k].Y - y) <= 5);
        int ArcEnds(Seg a) => (ArcAt(a.X0, a.Y0) ? 1 : 0) + (ArcAt(a.X1, a.Y1) ? 1 : 0);

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
            // Two rungs of a flex duct drawn bumpy (a rung's ends on the corrugation's arcs; the end rung's on the short
            // lines closing the flex): short, narrow, not a run.
            if (s1 - s0 <= 3 * MinSideMm && Math.Abs(d) <= 150 && Math.Max(ArcEnds(segs[i]), ArcEnds(segs[j])) == 2)
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

    /// <summary>Largest rail drawn as a strip (both edges of the rail): wider is another tray beside it.</summary>
    private const double MaxRailMm = 80;
    /// <summary>Shortest straight piece of ladder read (shorter: the strokes of a tee or a bend drawn between pieces).</summary>
    private const double MinLadderMm = 600;
    /// <summary>Farthest apart two rungs of one ladder are drawn (ladders: 250–450 mm).</summary>
    private const double MaxRungGapMm = 700;
    /// <summary>A rung's end on a rail: this close to the rail's line.</summary>
    private const double RungReachMm = 30;

    /// <summary>
    /// Collinear strokes (same direction within 0.3°, on one line within 3 mm) with gaps up to <paramref name="gapMm"/>
    /// joined into one line each.
    /// </summary>
    private static List<Seg> Merged(IEnumerable<Seg> strokes, double gapMm)
    {
        var lines = strokes.Where(x => !x.Arc && x.Length > 1).Select(x =>
        {
            var a = Math.Atan2(x.Y1 - x.Y0, x.X1 - x.X0);
            while (a < -Math.PI / 4) a += Math.PI;
            while (a >= 3 * Math.PI / 4) a -= Math.PI;
            return (Seg: x, A: a, C: -Math.Sin(a) * x.X0 + Math.Cos(a) * x.Y0);
        }).OrderBy(x => x.A).ToList();
        var result = new List<Seg>();
        var k = 0;
        while (k < lines.Count)
        {
            var start = k;
            while (k + 1 < lines.Count && lines[k + 1].A - lines[k].A < 0.005)
                k++;
            var group = lines.GetRange(start, k - start + 1).OrderBy(x => x.C).ToList();
            k++;
            var g = 0;
            while (g < group.Count)
            {
                var from = g;
                while (g + 1 < group.Count && group[g + 1].C - group[g].C < 3)
                    g++;
                var run = group.GetRange(from, g - from + 1);
                g++;
                var a = run.Average(x => x.A);
                double ux = Math.Cos(a), uy = Math.Sin(a);
                var spans = run.Select(x =>
                {
                    var t0 = x.Seg.X0 * ux + x.Seg.Y0 * uy;
                    var t1 = x.Seg.X1 * ux + x.Seg.Y1 * uy;
                    return t0 <= t1 ? (T0: t0, T1: t1, P0: (x.Seg.X0, x.Seg.Y0), P1: (x.Seg.X1, x.Seg.Y1), x.Seg)
                        : (T0: t1, T1: t0, P0: (x.Seg.X1, x.Seg.Y1), P1: (x.Seg.X0, x.Seg.Y0), x.Seg);
                }).OrderBy(x => x.T0).ToList();
                var (_, hi, p0, p1, first) = spans[0];
                void Emit() => result.Add(new Seg(p0.Item1, p0.Item2, p1.Item1, p1.Item2) { Layer = first.Layer, Size = first.Size, From = first.From });
                foreach (var span in spans.Skip(1))
                {
                    if (span.T0 <= hi + gapMm)
                    {
                        if (span.T1 > hi)
                            (hi, p1) = (span.T1, span.P1);
                        continue;
                    }

                    Emit();
                    (_, hi, p0, p1, first) = span;
                }

                Emit();
            }
        }

        return result;
    }

    /// <summary>
    /// Ladder trays drawn as they look (two rails, each a strip of two lines, and rungs across every few hundred mm):
    /// the axis of each straight piece and its width over the rails' outer edges, or null when the layer has no
    /// ladder. Two rails belong to one tray where rungs run from one to the other (two trays side by side share none);
    /// the rungs, the fittings drawn between the pieces and the strokes of other things on the layer are left out.
    /// </summary>
    private static List<Seg>? LadderAxes(List<Seg> fine)
    {
        var lines = Merged(fine, 50);
        var cells = new Buckets(CellMm);
        for (var i = 0; i < lines.Count; i++)
        {
            var s = lines[i];
            cells.Add(i, Math.Min(s.X0, s.X1) - RungReachMm, Math.Min(s.Y0, s.Y1) - RungReachMm,
                Math.Max(s.X0, s.X1) + RungReachMm, Math.Max(s.Y0, s.Y1) + RungReachMm);
        }

        // The line a point lies on (the nearest within reach, along its length), square-ish to the stroke ending there.
        int? On(Seg stroke, double x, double y, int other)
        {
            int? found = null;
            var best = RungReachMm;
            foreach (var j in cells.At(x, y))
            {
                var l = lines[j];
                if (j == other || l.Length < 2 * RungReachMm || Math.Abs(stroke.U.X * l.U.Y - stroke.U.Y * l.U.X) < 0.7)
                    continue;
                var t = (x - l.X0) * l.U.X + (y - l.Y0) * l.U.Y;
                if (t < -RungReachMm || t > l.Length + RungReachMm)
                    continue;
                var off = Math.Abs((x - l.X0) * -l.U.Y + (y - l.Y0) * l.U.X);
                if (off <= best)
                    (found, best) = (j, off);
            }

            return found;
        }

        // Rungs: strokes from one line to another parallel one (the two rails' inner edges).
        var rungs = new Dictionary<(int, int), List<double>>();
        foreach (var s in fine.Where(s => !s.Arc && s.Length >= 80 && s.Length <= MaxDuctWidthMm))
        {
            if (On(s, s.X0, s.Y0, -1) is not int a || On(s, s.X1, s.Y1, a) is not int b)
                continue;
            if (Math.Abs(lines[a].U.X * lines[b].U.Y - lines[a].U.Y * lines[b].U.X) > ParallelSin)
                continue;
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (!rungs.TryGetValue(key, out var at))
                rungs[key] = at = new List<double>();
            var rail = lines[key.Item1];
            at.Add((s.X0 - rail.X0) * rail.U.X + (s.Y0 - rail.Y0) * rail.U.Y);
        }

        var axes = new List<Seg>();
        foreach (var pair in rungs)
        {
            if (pair.Value.Count < 2)
                continue;
            var (a, b) = pair.Key;
            var la = lines[a];
            var lb = lines[b];
            var (ux, uy) = la.U;
            double nx = -uy, ny = ux;
            // Inner edges: b on the +n side of a at distance d.
            var d = (lb.X0 - la.X0) * nx + (lb.Y0 - la.Y0) * ny;
            if (d < 0)
                (nx, ny, d) = (-nx, -ny, -d);
            if (d < MinDuctWidthMm / 2 || d > MaxDuctWidthMm)
                continue;
            // Where both rails run.
            var b0 = (lb.X0 - la.X0) * ux + (lb.Y0 - la.Y0) * uy;
            var b1 = (lb.X1 - la.X0) * ux + (lb.Y1 - la.Y0) * uy;
            var lo = Math.Max(0, Math.Min(b0, b1));
            var hi = Math.Min(la.Length, Math.Max(b0, b1));
            // A straight piece, not the rung-like strokes of a fitting drawn between pieces.
            if (hi - lo < Math.Max(MinLadderMm, d))
                continue;
            // Rungs all along it, a few hundred mm apart: two strokes of a fitting meeting the same lines are not.
            var along = pair.Value.Where(t => t >= lo - RungReachMm && t <= hi + RungReachMm).OrderBy(t => t).ToList();
            if (along.Count < 2 || along[0] - lo > MaxRungGapMm || hi - along[along.Count - 1] > MaxRungGapMm ||
                along.Zip(along.Skip(1), (p, q) => q - p).Any(g => g > MaxRungGapMm))
                continue;

            // Each rail's outer edge: the line beside its inner edge, away from the other rail, along the piece.
            double Outer(Seg inner, double away)
            {
                var best = 0.0;
                var mid = (lo + hi) / 2;
                double mx = la.X0 + ux * mid + nx * (away > 0 ? d : 0), my = la.Y0 + uy * mid + ny * (away > 0 ? d : 0);
                foreach (var j in cells.Query(mx - MaxRailMm, my - MaxRailMm, mx + MaxRailMm, my + MaxRailMm))
                {
                    var l = lines[j];
                    if (l == inner || Math.Abs(ux * l.U.Y - uy * l.U.X) > ParallelSin)
                        continue;
                    var off = ((l.X0 - inner.X0) * nx + (l.Y0 - inner.Y0) * ny) * Math.Sign(away);
                    if (off < 5 || off > MaxRailMm)
                        continue;
                    var t0 = (l.X0 - la.X0) * ux + (l.Y0 - la.Y0) * uy;
                    var t1 = (l.X1 - la.X0) * ux + (l.Y1 - la.Y0) * uy;
                    if (Math.Min(hi, Math.Max(t0, t1)) - Math.Max(lo, Math.Min(t0, t1)) < 0.5 * (hi - lo))
                        continue;
                    best = Math.Max(best, off);
                }

                return best;
            }

            var wa = Outer(la, -1);
            var wb = Outer(lb, 1);
            var mid0 = d / 2 + (wb - wa) / 2;
            axes.Add(new Seg(la.X0 + ux * lo + nx * mid0, la.Y0 + uy * lo + ny * mid0, la.X0 + ux * hi + nx * mid0, la.Y0 + uy * hi + ny * mid0)
            {
                Layer = la.Layer,
                Width = d + wa + wb
            });
        }

        // A fitting drawn between pieces holds rung-like strokes of its own: a piece lying mostly over a longer one in
        // line with it, already read, is one of those (a tray crossing over another at its own height is not).
        var kept = new List<Seg>();
        bool Inside(Seg r, double x, double y)
        {
            var t = (x - r.X0) * r.U.X + (y - r.Y0) * r.U.Y;
            return t >= 0 && t <= r.Length && Math.Abs((x - r.X0) * -r.U.Y + (y - r.Y0) * r.U.X) <= r.Width / 2;
        }

        foreach (var a in axes.OrderByDescending(x => x.Length))
        {
            var near = kept.Where(k => Math.Abs(a.U.X * k.U.Y - a.U.Y * k.U.X) < 0.17 && Math.Max(Math.Min(a.X0, a.X1), Math.Min(k.X0, k.X1)) - (a.Width + k.Width) / 2 <= Math.Min(Math.Max(a.X0, a.X1), Math.Max(k.X0, k.X1)) &&
                                       Math.Max(Math.Min(a.Y0, a.Y1), Math.Min(k.Y0, k.Y1)) - (a.Width + k.Width) / 2 <= Math.Min(Math.Max(a.Y0, a.Y1), Math.Max(k.Y0, k.Y1))).ToList();
            int all = 0, covered = 0;
            for (var s = 0.0; s <= a.Length; s += 100)
            for (var f = -0.25; f <= 0.25; f += 0.25)
            {
                var x = a.X0 + a.U.X * s - a.U.Y * f * a.Width;
                var y = a.Y0 + a.U.Y * s + a.U.X * f * a.Width;
                all++;
                if (near.Any(k => Inside(k, x, y)))
                    covered++;
            }

            if (covered <= 0.3 * all)
                kept.Add(a);
        }

        // A short piece across a wider one, inside it end to end: the rungs of a crossing or a tee drawn there.
        kept = kept.Where(a => !kept.Any(k => k != a && k.Length > a.Length && a.Length <= 1.5 * k.Width &&
                                              Inside(k, a.X0, a.Y0) && Inside(k, a.X1, a.Y1) && Inside(k, (a.X0 + a.X1) / 2, (a.Y0 + a.Y1) / 2)))
            .ToList();

        return kept.Count == 0 ? null : kept;
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
    /// The two runs that are the main through a junction: of the pairs going straight on, the widest (the smaller of
    /// the two sizes); a cross has two straight pairs, and the branches (DN32 up, DN50 down) are not the DN150 main.
    /// Without a straight pair, the most opposite. Directions point away from the node.
    /// </summary>
    public static (int, int) MainPair(IReadOnlyList<(double X, double Y, double Z, double Across)> ends)
    {
        var best = (0, 1);
        var (bestStraight, bestAcross, bestDot) = (false, double.MinValue, double.MaxValue);
        for (var i = 0; i < ends.Count; i++)
        for (var j = i + 1; j < ends.Count; j++)
        {
            var (a, b) = (ends[i], ends[j]);
            var dot = (a.X * b.X + a.Y * b.Y + a.Z * b.Z) /
                      Math.Max(1e-9, Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z) * Math.Sqrt(b.X * b.X + b.Y * b.Y + b.Z * b.Z));
            var straight = dot < -0.999;
            var across = Math.Min(a.Across, b.Across);
            var better = straight != bestStraight ? straight
                : straight ? across > bestAcross + 1e-6 || (Math.Abs(across - bestAcross) <= 1e-6 && dot < bestDot)
                : dot < bestDot;
            if (better)
            {
                best = (i, j);
                (bestStraight, bestAcross, bestDot) = (straight, across, dot);
            }
        }

        return best;
    }

    /// <summary>
    /// A node sits at the lowest end of the runs meeting there: a higher run drops to it near its end (a riser and two
    /// elbows), as where two runs written at different heights meet. A run wider than every run at that lowest height
    /// stays up: Revit cannot tee a wide branch into a narrower main, so the drop would be left floating (a 1200 ladder
    /// at 3100 ending on a 200 tray at 2600). With <paramref name="only"/>, just the nodes one of those edges touches.
    /// </summary>
    public static void MeetAtNodes(MepNetwork net, List<List<int>> edgesAt, List<List<MepPiece>> profiles, bool[]? only = null)
    {
        double Wide(MepEdge e) => e.Size.IsRound ? e.Size.DiameterMm : e.Size.WidthMm;
        for (var n = 0; n < net.Nodes.Count; n++)
        {
            var ends = new List<(int Edge, bool AtStart, double Z)>();
            foreach (var i in edgesAt[n])
            {
                if (profiles[i].Count == 0)
                    continue;
                if (net.Edges[i].A == n)
                    ends.Add((i, true, profiles[i][0].Z));
                if (net.Edges[i].B == n)
                    ends.Add((i, false, profiles[i][profiles[i].Count - 1].Z));
            }

            if (ends.Count < 2 || only is not null && !ends.Any(x => only[x.Edge]))
                continue;
            var z = ends.Min(x => x.Z);
            var lowWidth = ends.Where(x => x.Z <= z + 1).Max(x => Wide(net.Edges[x.Edge]));
            foreach (var (edge, atStart, endZ) in ends)
            {
                if (endZ > z + 1 && Wide(net.Edges[edge]) > lowWidth + 1)
                    continue;
                profiles[edge] = SetEnd(profiles[edge], atStart, z);
            }
        }
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

    // "BE" without its accents: BỂ (tank), not BỆ MÁY (a plinth) or BÊ TÔNG.
    private static readonly Regex TankNote = new(@"\b(BE(?!\s+(MAY|TONG)\b)|PHOT|TU HOAI|HO GA|GA|TACH MO|HO THU|SEPTIC|MANHOLE|ONG RA|XUYEN MONG|CHO XUYEN)\b");

    /// <summary>
    /// Which way a drainage network read from the drawing flows. Every network meets its riser (the service shaft) at
    /// one open end (<see cref="DrainHead"/>). On the upper floors the water runs from the WCs and floor drains (every
    /// other open end) to that riser: the riser end is the one outlet. On the lowest floor
    /// (<paramref name="lowestFloor"/>, or a tank / manhole note within 5 m of another open end) it runs from the riser
    /// foot out to the manholes and septic tanks: every other open end is an outlet. <paramref name="riserPick"/>, when
    /// given, is the riser end picked.
    /// </summary>
    public static (List<int> Outlets, int Riser, bool FromRiser, string Why) DrainFlow(
        MepNetwork net, IReadOnlyCollection<int> part, IReadOnlyCollection<CadText> texts,
        IEnumerable<(double X, double Y)> otherEnds, bool lowestFloor, int? riserPick = null)
    {
        var (riser, why) = riserPick is int picked ? (picked, "trục đã pick") : DrainHead(net, part, texts, otherEnds);
        var adjacent = Adjacency(net);
        var rest = part.Where(n => n != riser && adjacent[n].Count == 1).ToList();
        if (rest.Count == 0)
            return (new List<int> { riser }, riser, false, why);
        var tanks = texts.Where(t => TankNote.IsMatch(DevicePlanner.Plain(t.Value))).ToList();
        double D(int n, CadText t) => Math.Sqrt(Sq(net.Nodes[n].X - t.X) + Sq(net.Nodes[n].Y - t.Y));
        var nearTank = rest.Any(n => tanks.Any(t => D(n, t) <= 5000));
        if (!lowestFloor && !nearTank)
            return (new List<int> { riser }, riser, false, why + "; tầng trên: WC / phễu thu chảy về trục");
        // Lowest floor: several risers (TKT28, TG3, TKT29…) and floor drains (THOÁT SÀN) come down onto one collector,
        // and it leaves the building at ONE place: never two exits with the water parting at a junction between. Riser
        // feet and floor-drain ends are high ends. The exit is the other end nearest a septic tank / manhole / pump
        // pit note (BỂ PHỐT, HỐ GA, HỐ BƠM; not a clean-water tank BỂ NƯỚC); else one by a foundation sleeve note of
        // its own size (D200 CHỜ XUYÊN MÓNG: a D140 sleeve is another pipe's); else the end of the largest pipe,
        // farthest along the pipe from the riser.
        var plain = texts.Select(t => (Text: DevicePlanner.Plain(t.Value), Note: t)).ToList();
        var heads = plain.Where(t => RiserNote.IsMatch(t.Text) || HeadNote.IsMatch(t.Text)).Select(t => t.Note).ToList();
        var exits = plain.Where(t => ExitNote.IsMatch(t.Text)).Select(t => t.Note).ToList();
        var sleeves = plain.Where(t => SleeveNote.IsMatch(t.Text)).ToList();
        // The riser read above may itself be the exit (no riser note at it): every end is weighed.
        var allEnds = part.Where(n => adjacent[n].Count == 1).ToList();
        var high = new HashSet<int>(allEnds.Where(n => heads.Any(t => D(n, t) <= (HeadNote.IsMatch(DevicePlanner.Plain(t.Value)) ? 3500 : 1500))));
        if (riserPick is int pickedRiser)
            high.Add(pickedRiser);
        var candidates = allEnds.Where(n => !high.Contains(n)).ToList();
        if (candidates.Count == 0)
            candidates = rest;
        var along = DistancesFrom(net, riser);
        double Sleeve(int n)
        {
            var size = adjacent[n][0].Size.DepthMm;
            return sleeves.Where(s => SizeIn(s.Text) is not { } d || Math.Abs(d - size) < 1)
                .Select(s => D(n, s.Note)).DefaultIfEmpty(double.MaxValue).Min();
        }

        var scored = candidates.Select(n => (Node: n,
                Exit: exits.Select(t => D(n, t)).DefaultIfEmpty(double.MaxValue).Min(),
                Sleeve: Sleeve(n),
                Depth: adjacent[n][0].Size.DepthMm,
                Along: along.TryGetValue(n, out var a) ? a : 0))
            .OrderByDescending(s => s.Exit <= 10000)
            .ThenBy(s => s.Exit <= 10000 ? Math.Round(s.Exit / 500) : 0)
            .ThenByDescending(s => s.Sleeve <= 5000)
            .ThenByDescending(s => s.Depth)
            .ThenByDescending(s => s.Along)
            .ToList();
        var exit = scored[0];
        var how = exit.Exit <= 10000 ? "cạnh chữ bể / hố ga / hố bơm"
            : exit.Sleeve <= 5000 ? "cạnh chữ chờ xuyên móng cùng cỡ ống"
            : "đầu ống lớn nhất, xa trục nhất";
        return (new List<int> { exit.Node }, riser, true,
            why + $"; tầng dưới cùng: {high.Count} đầu trục / thoát sàn là điểm cao, 1 điểm xả {how}");
    }

    /// <summary>Where a lowest-floor drain leaves: a septic tank, manhole, pump pit, treatment plant; not a clean-water tank.</summary>
    private static readonly Regex ExitNote = new(
        @"\b(BE(?!\s+(MAY|TONG|NUOC|NGAM NUOC|PCCC|CAP)\b)|PHOT|TU HOAI|HO GA|GA|TACH MO|HO THU|HO BOM|HO PIT|TRAM XU LY|SEPTIC|MANHOLE|ONG RA)\b");

    /// <summary>A foundation sleeve left for a pipe (D200 CHỜ XUYÊN MÓNG).</summary>
    private static readonly Regex SleeveNote = new(@"\b(XUYEN MONG|CHO XUYEN|XUYEN VACH|XUYEN TUONG)\b");

    /// <summary>The start of a drain branch: a floor drain or fixture (THOÁT SÀN, PHỄU THU, FD, WC).</summary>
    private static readonly Regex HeadNote = new(@"\b(THOAT SAN|PHEU|FD|WC|LAVABO)\b");

    private static double? SizeIn(string text)
    {
        var m = Regex.Match(text, @"\b(?:D|DN|O)\s*(\d{2,3})\b");
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static readonly Regex VentWords = new(@"\b(VENT|VP|THONG HOI|TH|HX|HR|THBP|THG)\b");

    /// <summary>
    /// A vent run (thông hơi): the Revit system is classed Vent, or its name / abbreviation, or every layer drawn
    /// on, says so (P-VP-Vent, Thông Hơi, "P - TH D110", HX / HR).
    /// </summary>
    public static bool IsVent(bool classedVent, string systemName, string systemAbbr, IEnumerable<string> layers)
    {
        if (classedVent || VentWords.IsMatch(DevicePlanner.Plain(systemName)) || VentWords.IsMatch(DevicePlanner.Plain(systemAbbr)))
            return true;
        var list = layers.ToList();
        return list.Count > 0 && list.All(l => VentWords.IsMatch(DevicePlanner.Plain(l.Replace('-', ' ').Replace('_', ' '))));
    }

    /// <summary>
    /// A vent rises the other way from a drain: low at the branch ends (WC, floor drain) and highest at the riser it
    /// climbs to. <paramref name="drain"/> are the runs' ends as a drain falling to the riser; the result turns each
    /// height over (highest - height), so the riser end is the top and every run keeps its own fall.
    /// </summary>
    public static Dictionary<MepEdge, (double A, double B)> VentRises(Dictionary<MepEdge, (double A, double B)> drain)
    {
        if (drain.Count == 0)
            return drain;
        var top = drain.Values.Max(r => Math.Max(r.A, r.B));
        return drain.ToDictionary(kv => kv.Key, kv => (top - kv.Value.A, top - kv.Value.B));
    }

    /// <summary>A riser's note: TR, OD, ỐNG ĐỨNG, TRỤC, a shaft (TKT29) or a collected riser (TG3).</summary>
    private static readonly Regex RiserNote = new(@"\b(TR|OD|ONG DUNG|TRUC|RISER|STACK|TKT\d*|TG\d*)\b");

    /// <summary>
    /// The open end of a drainage network at its riser: one with a riser beside it (a "TR" / "OD" / "ỐNG ĐỨNG" note
    /// within 1.5 m, or another network's open end within 1 m: risers dropping side by side); among those (or all ends
    /// if none) the end of the largest pipe, then the one farthest from any tank / manhole note, then the one farthest
    /// along the pipe from the others. <paramref name="otherEnds"/> are the open ends of the other networks. Returns
    /// the node and why it was chosen.
    /// </summary>
    public static (int Node, string Why) DrainHead(
        MepNetwork net, IReadOnlyCollection<int> part, IEnumerable<CadText> texts, IEnumerable<(double X, double Y)> otherEnds)
    {
        var adjacent = Adjacency(net);
        var ends = part.Where(n => adjacent[n].Count == 1).ToList();
        if (ends.Count == 0)
            return (part.First(), "mạng khép kín, không có đầu hở");
        if (ends.Count == 1)
            return (ends[0], "đầu hở duy nhất");
        var notes = texts.Select(t => (Text: DevicePlanner.Plain(t.Value), t.X, t.Y)).Where(t => t.Text.Length > 0).ToList();
        var riser = RiserNote;
        var outlet = TankNote;
        var others = otherEnds.ToList();
        double D(int n, double x, double y) => Math.Sqrt(Sq(net.Nodes[n].X - x) + Sq(net.Nodes[n].Y - y));
        var scored = ends.Select(n =>
        {
            var riserNote = notes.Where(t => riser.IsMatch(t.Text) && D(n, t.X, t.Y) <= 1500).OrderBy(t => D(n, t.X, t.Y))
                .Select(t => t.Text).FirstOrDefault();
            var beside = others.Any(o => D(n, o.X, o.Y) <= 1000);
            var toTank = notes.Where(t => outlet.IsMatch(t.Text)).Select(t => D(n, t.X, t.Y)).DefaultIfEmpty(double.MaxValue).Min();
            var spread = DistancesFrom(net, n).Where(kv => ends.Contains(kv.Key)).Sum(kv => kv.Value);
            return (Node: n, Riser: riserNote is not null || beside, Note: riserNote, Beside: beside, Depth: adjacent[n][0].Size.DepthMm,
                ToTank: Math.Min(toTank, 30000), Spread: spread);
        }).OrderByDescending(s => s.Riser).ThenByDescending(s => s.Depth).ThenByDescending(s => Math.Round(s.ToTank / 500))
            .ThenByDescending(s => s.Spread).ToList();
        var head = scored[0];
        var why = head.Note is not null ? $"chữ \"{head.Note}\" cạnh đầu ống"
            : head.Beside ? "đầu ống cạnh đầu ống của mạng khác (ống đứng)"
            : scored.Count > 1 && scored[1].Depth < head.Depth ? $"đầu ống lớn nhất (Ø{head.Depth:0})"
            : head.ToTank < 30000 ? "đầu hở xa chữ bể / hố ga nhất"
            : "đầu hở xa các đầu khác nhất";
        return (head.Node, why);
    }

    /// <summary>
    /// Drainage falls toward the outlet: centerline at each node = outlet + slope × distance along the pipe. With a
    /// structure above, the outlet is set as high as it can go: every point of every run (sampled each
    /// <paramref name="stepMm"/>) stays <paramref name="clearanceMm"/> under the beam or slab over it.
    /// <paramref name="underside"/> gives that underside at a plan point (mm), or null where there is none.
    /// </summary>
    public static double OutletUnderStructure(
        MepNetwork net, int outlet, double slope, Func<double, double, double?> underside, double clearanceMm,
        double stepMm, double fallbackMm) =>
        OutletUnderStructure(net, new[] { outlet }, slope, underside, clearanceMm, stepMm, fallbackMm);

    /// <summary>
    /// Each run's two ends, above the lowest outlet: every run straight at its own drawn fall, every joint at one height,
    /// no run broken. Each run flows one way: a run on the way from a start (an open end not picked as an outlet) to the
    /// outlet nearest that start follows that way; any other run flows away from the start nearer to it (never back
    /// toward a start); with no start, toward the outlet nearer along the pipe. Heights then follow run by run from one outlet, each run its fall lower downstream, so outlets may
    /// stand at different heights, and where two outlets' reaches meet the high point is a node (water parts there),
    /// never a peak mid-run. A run closing a loop whose ends do not fit its fall is returned in <c>OffFall</c>.
    /// </summary>
    public static (Dictionary<MepEdge, (double A, double B)> Ends, List<MepEdge> OffFall) RunRises(
        MepNetwork net, IReadOnlyCollection<int> outlets, double slopePercent)
    {
        var rises = RisesFrom(net, outlets, slopePercent);
        var ends = new Dictionary<MepEdge, (double A, double B)>();
        var offFall = new List<MepEdge>();
        var at = new Dictionary<int, List<MepEdge>>();
        foreach (var e in net.Edges.Where(x => rises.ContainsKey(x.A) && rises.ContainsKey(x.B)))
        {
            foreach (var n in new[] { e.A, e.B })
            {
                if (!at.TryGetValue(n, out var list))
                    at[n] = list = new List<MepEdge>();
                list.Add(e);
            }
        }

        double Fall(MepEdge e) => (e.SlopePercent ?? slopePercent) / 100 * Length(net, e);
        var (_, toOutlet) = Nearest(net, outlets);
        var starts = at.Where(kv => kv.Value.Count == 1 && !outlets.Contains(kv.Key)).Select(kv => kv.Key).ToList();
        var (fromStart, _) = Nearest(net, starts);
        // Runs carrying a start's water to its outlet: upper end the one further from the outlet.
        var carried = new Dictionary<MepEdge, bool>();
        foreach (var s0 in starts)
        {
            for (var n = s0; toOutlet.TryGetValue(n, out var down) && !carried.ContainsKey(down);)
            {
                carried[down] = down.A == n;
                n = down.A == n ? down.B : down.A;
            }
        }

        bool AUpper(MepEdge e)
        {
            if (carried.TryGetValue(e, out var a))
                return a;
            if (fromStart.TryGetValue(e.A, out var da) && fromStart.TryGetValue(e.B, out var db) && Math.Abs(da - db) > 1)
                return da < db;
            return rises[e.A] > rises[e.B] || (rises[e.A] == rises[e.B] && e.A > e.B);
        }
        var height = new Dictionary<int, double>();
        var done = new HashSet<MepEdge>();
        foreach (var start in outlets.Where(at.ContainsKey).Concat(at.Keys))
        {
            if (height.ContainsKey(start))
                continue;
            var component = new List<int> { start };
            height[start] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var e in at[u])
                {
                    if (!done.Add(e))
                        continue;
                    var v = e.A == u ? e.B : e.A;
                    var fall = Fall(e);
                    var uUpper = AUpper(e) == (e.A == u);
                    var hv = height[u] + (uUpper ? -fall : fall);
                    if (height.TryGetValue(v, out var known))
                    {
                        if (Math.Abs(known - hv) > 0.5)
                            offFall.Add(e);
                        continue;
                    }

                    height[v] = hv;
                    component.Add(v);
                    queue.Enqueue(v);
                }
            }

            // Above the lowest point of the network (an outlet).
            var low = component.Min(n => height[n]);
            foreach (var n in component)
                height[n] -= low;
        }

        foreach (var e in done)
            ends[e] = (height[e.A], height[e.B]);
        return (ends, offFall);
    }

    /// <summary>Distance along the pipe from the nearest of <paramref name="sources"/>, and the run each node takes toward it.</summary>
    private static (Dictionary<int, double> Dist, Dictionary<int, MepEdge> Toward) Nearest(MepNetwork net, IEnumerable<int> sources)
    {
        var adjacent = Adjacency(net);
        var dist = sources.Distinct().ToDictionary(o => o, _ => 0.0);
        var toward = new Dictionary<int, MepEdge>();
        var open = new SortedSet<(double D, int N)>(dist.Keys.Select(o => (0.0, o)));
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
                toward[m] = e;
                open.Add((d, m));
            }
        }

        return (dist, toward);
    }

    /// <summary>
    /// A drain's head: the node standing highest above its outlets (the far end of the longest fall). Its height is the
    /// one fixed (under the beam over it); every other follows down the fall to the outlets.
    /// </summary>
    public static int HeadNode(Dictionary<int, double> rises) =>
        rises.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;

    /// <summary>As above for a network with several outlets, all at the one height returned.</summary>
    public static double OutletUnderStructure(
        MepNetwork net, IReadOnlyCollection<int> outlets, double slope, Func<double, double, double?> underside, double clearanceMm,
        double stepMm, double fallbackMm)
    {
        var dist = RisesFrom(net, outlets, slope * 100);
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

    /// <summary>
    /// Drainage drawn with square corners, made to drain: where a branch meets its main at a right angle, it turns
    /// 45° short of the main and joins it a little downstream through a 45° wye; a right-angle bend becomes two 45°
    /// bends. Offsets are <paramref name="minOffsetMm"/> or 1.5 × the pipe, whichever is more. Flow runs toward the
    /// nearest of <paramref name="outlets"/>. Junctions too tight for it stay square and are counted as skipped.
    /// </summary>
    public static (int Wyes, int Bends, int Skipped) DrainFittings(MepNetwork net, IEnumerable<int> outlets, double minOffsetMm = 150)
    {
        var dist = new Dictionary<int, double>();
        foreach (var o in outlets)
        {
            foreach (var kv in DistancesFrom(net, o))
            {
                if (!dist.TryGetValue(kv.Key, out var d) || kv.Value < d)
                    dist[kv.Key] = kv.Value;
            }
        }

        (double X, double Y) Dir(int from, MepEdge e)
        {
            var to = e.A == from ? e.B : e.A;
            var (ax, ay) = net.Nodes[from];
            var (bx, by) = net.Nodes[to];
            var l = Math.Sqrt(Sq(bx - ax) + Sq(by - ay));
            return l < 1e-6 ? (0, 0) : ((bx - ax) / l, (by - ay) / l);
        }

        int Add(double x, double y)
        {
            net.Nodes.Add((x, y));
            return net.Nodes.Count - 1;
        }

        void Move(MepEdge e, int from, int to)
        {
            if (e.A == from)
                e.A = to;
            else
                e.B = to;
        }

        static MepEdge Like(MepEdge e, int a, int b) => new()
        {
            A = a, B = b, Size = e.Size, Layer = e.Layer, SizeFrom = e.SizeFrom, SlopePercent = e.SlopePercent
        };

        static double Offset(double depthMm, double min) => Math.Max(min, Math.Ceiling(1.5 * depthMm / 10) * 10);

        int wyes = 0, bends = 0, skipped = 0;
        var original = net.Nodes.Count;
        for (var n = 0; n < original; n++)
        {
            var at = net.Edges.Where(e => e.A == n || e.B == n).ToList();
            if (at.Any(e => e.IsTransition) || !dist.ContainsKey(n))
                continue;
            if (at.Count == 3)
            {
                // The two most opposite runs are the main; the third is the branch.
                var pairs = new[] { (0, 1, 2), (0, 2, 1), (1, 2, 0) };
                var (i, j, k) = pairs.OrderBy(p =>
                {
                    var (ax, ay) = Dir(n, at[p.Item1]);
                    var (bx, by) = Dir(n, at[p.Item2]);
                    return ax * bx + ay * by;
                }).First();
                var (m1x, m1y) = Dir(n, at[i]);
                var (m2x, m2y) = Dir(n, at[j]);
                var (bdx, bdy) = Dir(n, at[k]);
                if (m1x * m2x + m1y * m2y > -0.99 || Math.Abs(bdx * m1x + bdy * m1y) > 0.17)
                    continue;
                var branch = at[k];
                var branchEnd = branch.A == n ? branch.B : branch.A;
                int End(MepEdge e) => e.A == n ? e.B : e.A;
                if (!dist.TryGetValue(End(at[i]), out var di) || !dist.TryGetValue(End(at[j]), out var dj) ||
                    !dist.TryGetValue(branchEnd, out var db) || db < dist[n])
                {
                    // The branch carries the flow away (two mains meeting head-on into it): left square.
                    skipped++;
                    continue;
                }

                var down = di < dj ? at[i] : at[j];
                var (fx, fy) = di < dj ? (m1x, m1y) : (m2x, m2y);
                var d = Offset(branch.Size.DepthMm, minOffsetMm);
                if (Length(net, branch) < d + 100 || Length(net, down) < d + 100)
                {
                    skipped++;
                    continue;
                }

                var (nx, ny) = net.Nodes[n];
                var nb = Add(nx + bdx * d, ny + bdy * d);
                var nj = Add(nx + fx * d, ny + fy * d);
                Move(branch, n, nb);
                Move(down, n, nj);
                net.Edges.Add(Like(down, n, nj));
                net.Edges.Add(Like(branch, nb, nj));
                wyes++;
            }
            else if (at.Count == 2)
            {
                var (ax, ay) = Dir(n, at[0]);
                var (bx, by) = Dir(n, at[1]);
                if (Math.Abs(ax * bx + ay * by) > 0.17)
                    continue;
                var big = at[0].Size.DepthMm >= at[1].Size.DepthMm ? at[0] : at[1];
                var d = Offset(big.Size.DepthMm, minOffsetMm);
                if (Length(net, at[0]) < d + 100 || Length(net, at[1]) < d + 100)
                {
                    skipped++;
                    continue;
                }

                var (nx, ny) = net.Nodes[n];
                var c0 = Add(nx + ax * d, ny + ay * d);
                var c1 = Add(nx + bx * d, ny + by * d);
                Move(at[0], n, c0);
                Move(at[1], n, c1);
                // The cut corner runs at the size and fall of the larger pipe.
                net.Edges.Add(Like(big, c0, c1));
                bends++;
            }
        }

        return (wyes, bends, skipped);
    }

    /// <summary>
    /// Sprinkler pipes: where a straight run steps down a size the drawing puts the change at the head or at the branch
    /// tee; the reducer goes <paramref name="pastMm"/> on into the smaller run (the head or the branch comes off the
    /// larger pipe, then it reduces). A node of just those two runs (a head) moves; at a tee the smaller main is split
    /// there and the piece up to the tee takes the larger size. A smaller run too short to leave
    /// <paramref name="keepMm"/> stays. Returns the reducers moved; <paramref name="at"/> gets the nodes they are now at
    /// (where the reducing starts: the reducer's large end goes there).
    /// </summary>
    public static int ReducersPast(MepNetwork net, double pastMm = 150, double keepMm = 100, List<int>? reducers = null)
    {
        var at = net.Nodes.Select(_ => new List<MepEdge>()).ToList();
        foreach (var e in net.Edges)
        {
            at[e.A].Add(e);
            at[e.B].Add(e);
        }

        double Across(MepSize s) => s.IsRound ? s.DiameterMm : s.WidthMm;
        var moved = 0;
        var original = net.Nodes.Count;
        for (var n = 0; n < original; n++)
        {
            if (at[n].Count is 3 or 4 && !at[n].Any(e => e.IsTransition) && AtTee(n))
            {
                reducers?.Add(net.Nodes.Count - 1);
                moved++;
                continue;
            }

            if (at[n].Count != 2 || at[n].Any(e => e.IsTransition))
                continue;
            var (e1, e2) = (at[n][0], at[n][1]);
            if (Math.Abs(Across(e1.Size) - Across(e2.Size)) < 1)
                continue;
            var small = Across(e1.Size) < Across(e2.Size) ? e1 : e2;
            var big = small == e1 ? e2 : e1;
            var (x, y) = net.Nodes[n];
            var (sx, sy) = net.Nodes[small.A == n ? small.B : small.A];
            var (bx, by) = net.Nodes[big.A == n ? big.B : big.A];
            var (ls, lb) = (Math.Sqrt(Sq(sx - x) + Sq(sy - y)), Math.Sqrt(Sq(bx - x) + Sq(by - y)));
            if (ls < pastMm + keepMm || lb < 1)
                continue;
            // Straight on only: a corner keeps its reducer at the elbow.
            var dot = ((sx - x) * (bx - x) + (sy - y) * (by - y)) / (ls * lb);
            if (dot > -0.999)
                continue;
            net.Nodes[n] = (x + (sx - x) / ls * pastMm, y + (sy - y) / ls * pastMm);
            reducers?.Add(n);
            moved++;
        }

        return moved;

        // A main stepping down at a branch tee: the smaller main split pastMm on, the piece to the tee the larger size.
        bool AtTee(int n)
        {
            var (x, y) = net.Nodes[n];
            (MepEdge Edge, double X, double Y, double L) Out(MepEdge e)
            {
                var (fx, fy) = net.Nodes[e.A == n ? e.B : e.A];
                var l = Math.Sqrt(Sq(fx - x) + Sq(fy - y));
                return l < 1e-9 ? (e, 0, 0, 0) : (e, (fx - x) / l, (fy - y) / l, l);
            }

            var outs = at[n].Select(Out).ToList();
            var main = (from i in Enumerable.Range(0, outs.Count) from j in Enumerable.Range(i + 1, outs.Count - 1 - i)
                let a = outs[i] let b = outs[j]
                where a.L > 0 && b.L > 0 && a.X * b.X + a.Y * b.Y < -0.999 && Math.Abs(Across(a.Edge.Size) - Across(b.Edge.Size)) >= 1
                select (a, b)).ToList();
            if (main.Count != 1)
                return false;
            var (p, q) = main[0];
            var (small, big) = Across(p.Edge.Size) < Across(q.Edge.Size) ? (p, q) : (q, p);
            // The branches no wider than the smaller main (a wider one is the main turning, not a branch off it).
            if (outs.Where(o => o.Edge != small.Edge && o.Edge != big.Edge).Any(o => Across(o.Edge.Size) > Across(small.Edge.Size) + 1)
                || small.L < pastMm + keepMm)
                return false;
            net.Nodes.Add((x + small.X * pastMm, y + small.Y * pastMm));
            var cut = net.Nodes.Count - 1;
            var e = small.Edge;
            if (e.A == n)
                e.A = cut;
            else
                e.B = cut;
            net.Edges.Add(new MepEdge
            {
                A = n, B = cut, Size = big.Edge.Size, SizeFrom = big.Edge.SizeFrom, DrawnWidthMm = big.Edge.DrawnWidthMm,
                Layer = e.Layer, SlopePercent = e.SlopePercent, ElevationMm = e.ElevationMm
            });
            return true;
        }
    }

    /// <summary>
    /// Runs drawn a few degrees off square, which no fitting joins (a transition needs the runs in line, an elbow a
    /// listed angle): a branch off a straight main, up to <paramref name="maxDeg"/> off square, has its tee slid along
    /// the main until it is square (the main stays straight, the branch's far end stays); a corner up to that far off
    /// 90° with a free end (nothing beyond) has that run turned about the corner to 90°. Returns the tees slid and the
    /// corners squared.
    /// </summary>
    public static (int Tees, int Corners) SquareUp(MepNetwork net, double maxDeg = 8, double keepMm = 100)
    {
        var at = net.Nodes.Select(_ => new List<MepEdge>()).ToList();
        foreach (var e in net.Edges)
        {
            at[e.A].Add(e);
            at[e.B].Add(e);
        }

        int Far(MepEdge e, int n) => e.A == n ? e.B : e.A;
        (double X, double Y, double L) Dir(int n, int f)
        {
            var (dx, dy) = (net.Nodes[f].X - net.Nodes[n].X, net.Nodes[f].Y - net.Nodes[n].Y);
            var l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-9 ? (0, 0, 0) : (dx / l, dy / l, l);
        }

        var (lo, hi) = (Math.Sin(0.1 * Math.PI / 180), Math.Sin(maxDeg * Math.PI / 180));
        int tees = 0, corners = 0;
        var count = net.Nodes.Count;
        for (var n = 0; n < count; n++)
        {
            var edges = at[n].Where(e => !e.IsTransition).ToList();
            if (edges.Count is 3 or 4)
            {
                // The main: the one pair straight on; the branches: the rest.
                var pair = (from i in Enumerable.Range(0, edges.Count) from j in Enumerable.Range(i + 1, edges.Count - 1 - i)
                    let a = Dir(n, Far(edges[i], n)) let b = Dir(n, Far(edges[j], n))
                    where a.L > 0 && b.L > 0 && a.X * b.X + a.Y * b.Y < -0.999
                    select (i, j, a, b)).ToList();
                // Two pairs straight on (a cross): the main is the wider pair.
                double Across(MepSize z) => z.IsRound ? z.DiameterMm : z.WidthMm;
                pair = pair.OrderByDescending(q => Math.Min(Across(edges[q.i].Size), Across(edges[q.j].Size))).ToList();
                if (pair.Count == 0 || (pair.Count > 1 && Math.Min(Across(edges[pair[0].i].Size), Across(edges[pair[0].j].Size)) <=
                        Math.Min(Across(edges[pair[1].i].Size), Across(edges[pair[1].j].Size))))
                    continue;
                var (i1, j1, m1, m2) = pair[0];
                var branches = edges.Where((_, k) => k != i1 && k != j1)
                    .Select(e => (Edge: e, U: Dir(n, Far(e, n)))).Select(b => (b.Edge, b.U, Cos: b.U.X * m1.X + b.U.Y * m1.Y)).ToList();
                var askew = branches.Where(b => b.U.L >= 1 && Math.Abs(b.Cos) >= lo && Math.Abs(b.Cos) <= hi).ToList();
                // One branch askew, any other square (a cross keeps its square branch where it is).
                if (askew.Count != 1 || branches.Count - 1 != branches.Count(b => Math.Abs(b.Cos) < lo))
                    continue;
                // Slide along the main by the branch's drift along it; both main runs keep their length beyond.
                var drift = askew[0].U.L * askew[0].Cos;
                if (m1.L - drift < keepMm || m2.L + drift < keepMm)
                    continue;
                var (x, y) = net.Nodes[n];
                var slid = (x + m1.X * drift, y + m1.Y * drift);
                if (branches.Count == 1)
                    net.Nodes[n] = slid;
                else
                {
                    // A cross: the askew branch gets a tee of its own along the main, the main split there.
                    var main = drift > 0 ? edges[i1] : edges[j1];
                    net.Nodes.Add(slid);
                    var t = net.Nodes.Count - 1;
                    net.Edges.Add(new MepEdge
                    {
                        A = n, B = t, Size = main.Size, Layer = main.Layer, SizeFrom = main.SizeFrom,
                        SlopePercent = main.SlopePercent, ElevationMm = main.ElevationMm
                    });
                    if (main.A == n)
                        main.A = t;
                    else
                        main.B = t;
                    if (askew[0].Edge.A == n)
                        askew[0].Edge.A = t;
                    else
                        askew[0].Edge.B = t;
                }

                tees++;
            }
            else if (edges.Count == 2)
            {
                var (a, b) = (edges[0], edges[1]);
                var (fa, fb) = (Far(a, n), Far(b, n));
                var (da, db) = (Dir(n, fa), Dir(n, fb));
                var cos = da.X * db.X + da.Y * db.Y;
                if (da.L < 1 || db.L < 1 || Math.Abs(cos) < lo || Math.Abs(cos) > hi)
                    continue;
                // The free run (its far end joins nothing more; the shorter when both are) turns, the other stays.
                var freeA = at[fa].Count == 1;
                var freeB = at[fb].Count == 1;
                if (!freeA && !freeB)
                    continue;
                var turnA = freeA && (!freeB || da.L <= db.L);
                var (turn, keep, far) = turnA ? (da, db, fa) : (db, da, fb);
                // Square to the kept run, on the side the free run already leans.
                var (px, py) = (-keep.Y, keep.X);
                if (px * turn.X + py * turn.Y < 0)
                    (px, py) = (-px, -py);
                var (x, y) = net.Nodes[n];
                net.Nodes[far] = (x + px * turn.L, y + py * turn.L);
                corners++;
            }
        }

        return (tees, corners);
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
    /// <summary>A ladder piece standing alone shorter than this, with nothing written by it: part of a fitting (a drop at a tee).</summary>
    private const double LadderPieceMaxMm = 2000;

    private static int DropOpenings(MepNetwork net, double maxMm = OpeningMaxMm)
    {
        var dropped = 0;
        foreach (var part in Parts(net))
        {
            var nodes = new HashSet<int>(part);
            var edges = net.Edges.Where(e => nodes.Contains(e.A)).ToList();
            if (edges.Count == 0 || edges.Any(e => e.DrawnWidthMm <= 0 || e.SizeFrom == "text") ||
                edges.Sum(e => Length(net, e)) >= maxMm)
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
