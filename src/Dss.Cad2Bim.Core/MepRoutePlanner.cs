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
    /// <summary>Where the size came from: text (label beside the line), layer, inherited (next run), default.</summary>
    public string SizeFrom { get; set; } = "";
}

public sealed class MepNetwork
{
    public List<(double X, double Y)> Nodes { get; } = new();
    public List<MepEdge> Edges { get; } = new();
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
    /// </summary>
    public static MepNetwork Build(
        IEnumerable<CadEntity> entities, string? pickedLayer, MepSize fallback, IEnumerable<CadText>? texts = null)
    {
        var family = pickedLayer is null ? null : Family(pickedLayer);
        var segs = new List<(double X0, double Y0, double X1, double Y1, MepSize? Size, string From, string Layer)>();
        foreach (var e in entities)
        {
            if (family is not null && Family(e.Layer) != family)
                continue;
            MepSize? size = TryParseSize(e.Layer, out var s) ? s : null;
            var from = size is null ? "" : "layer";
            if (e is CadLine l)
                segs.Add((l.X0, l.Y0, l.X1, l.Y1, size, from, l.Layer));
            else if (e is CadPolyline p)
            {
                for (var i = 0; i + 1 < p.Points.Count; i++)
                    segs.Add((p.Points[i].X, p.Points[i].Y, p.Points[i + 1].X, p.Points[i + 1].Y, size, from, p.Layer));
            }
        }

        var minRun = family is null ? MinRunInLinkMm : NodeTolMm;
        segs = segs.Where(x => Math.Sqrt(Sq(x.X1 - x.X0) + Sq(x.Y1 - x.Y0)) > minRun).ToList();
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
                    net.Edges.Add(new MepEdge { A = a, B = b, Size = s.Size ?? default, SizeFrom = s.From, Layer = s.Layer });
            }
        }

        // Each size label goes to the run nearest to it (after the tees split the drawn lines).
        var edgeCells = new Buckets(CellMm);
        for (var i = 0; i < net.Edges.Count; i++)
        {
            var (x0, y0) = net.Nodes[net.Edges[i].A];
            var (x1, y1) = net.Nodes[net.Edges[i].B];
            var reach = LabelReachMm + 0.1 * Math.Sqrt(Sq(x1 - x0) + Sq(y1 - y0));
            edgeCells.Add(i, Math.Min(x0, x1) - reach, Math.Min(y0, y1) - reach, Math.Max(x0, x1) + reach, Math.Max(y0, y1) + reach);
        }

        foreach (var text in texts ?? Enumerable.Empty<CadText>())
        {
            if (!TryParseSize(text.Value, out var size))
                continue;
            var tx = text.CenterX ?? text.X;
            var ty = text.CenterY ?? text.Y;
            MepEdge? best = null;
            var bestD = LabelReachMm;
            foreach (var e in edgeCells.At(tx, ty).Select(i => net.Edges[i]))
            {
                var (x0, y0) = net.Nodes[e.A];
                var (x1, y1) = net.Nodes[e.B];
                var len = Math.Sqrt(Sq(x1 - x0) + Sq(y1 - y0));
                var t = ((tx - x0) * (x1 - x0) + (ty - y0) * (y1 - y0)) / (len * len);
                if (t < -0.1 || t > 1.1)
                    continue;
                var d = Math.Abs((tx - x0) * -(y1 - y0) / len + (ty - y0) * (x1 - x0) / len);
                if (d >= bestD)
                    continue;
                bestD = d;
                best = e;
            }

            if (best is not null)
            {
                best.Size = size;
                best.SizeFrom = "text";
            }
        }

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

        return net;
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
        var dist = DistancesFrom(net, outlet);
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
                    var rise = slope * (dist[e.A] + (dist[e.B] - dist[e.A]) * t);
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
