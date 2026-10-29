using System.Globalization;
using System.Text;
using System.Text.Json;
using Dss.Cad2Bim.Core;

if (args.Length >= 2 && args[0] == "--replay")
    return Replay(args[1], args.Length >= 4 && args[2] == "--svg" ? args[3] : null);

if (!SectionFootprintSelfTest.Run(out var message))
{
    Console.Error.WriteLine("FAIL: " + message);
    return 1;
}

Console.WriteLine("PASS: " + message);
return 0;

// Re-run the planner on a capture saved by ModelRevit (%LOCALAPPDATA%\DSSTech\beam-capture.json).
static int Replay(string capturePath, string? svgPath)
{
    var capture = PlanCapture.Load(capturePath);
    var geom = capture.Geometry();
    var place = capture.Placement(Console.WriteLine);
    var ops = ModelRevitPlanner.Plan(
        geom, capture.Layer, capture.Part, capture.BaseLevel, capture.TopLevel, capture.Size(),
        equipmentFamilyType: capture.Family, gridPoints: capture.Grids(), columnFamily: capture.Family,
        placement: place, gridAnglesDeg: capture.GridAngles);
    Console.WriteLine($"ops={ops.Count}");
    if (svgPath != null)
    {
        File.WriteAllText(svgPath, Svg(capture, geom, place.Supports, ops));
        Console.WriteLine($"svg={svgPath}");
    }

    return 0;
}

static string Svg(
    PlanCapture capture, CadGeometry geom, IReadOnlyList<SectionFootprint>? supports,
    IReadOnlyList<Dss.McpContracts.CreateOperation> ops)
{
    var inv = CultureInfo.InvariantCulture;
    var layer = geom.Entities.Where(e => string.Equals(e.Layer, capture.Layer, StringComparison.OrdinalIgnoreCase)).ToList();
    var xs = new List<double>();
    var ys = new List<double>();
    foreach (var e in layer)
    {
        if (e is CadLine l) { xs.Add(l.X0); xs.Add(l.X1); ys.Add(l.Y0); ys.Add(l.Y1); }
        if (e is CadPolyline p) foreach (var pt in p.Points) { xs.Add(pt.X); ys.Add(pt.Y); }
    }

    if (xs.Count == 0)
        return "<svg xmlns=\"http://www.w3.org/2000/svg\"/>";
    double minX = xs.Min() - 2000, maxX = xs.Max() + 2000, minY = ys.Min() - 2000, maxY = ys.Max() + 2000;
    string P(double x, double y) => string.Format(inv, "{0:0.#},{1:0.#}", x - minX, maxY - y);
    var sb = new StringBuilder();
    sb.AppendFormat(inv,
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0:0} {1:0}\" style=\"background:#fff\">\n",
        maxX - minX, maxY - minY);
    if (supports != null)
    {
        foreach (var s in supports)
        {
            sb.AppendFormat(inv,
                "<rect x=\"{0:0.#}\" y=\"{1:0.#}\" width=\"{2:0.#}\" height=\"{3:0.#}\" fill=\"#9ecbff\" stroke=\"#1f6feb\" stroke-width=\"10\" " +
                "transform=\"rotate({4:0.###} {5})\"/>\n",
                s.CenterX - s.WidthMm / 2 - minX, maxY - s.CenterY - s.DepthMm / 2, s.WidthMm, s.DepthMm,
                -s.RotationDeg, P(s.CenterX, s.CenterY).Replace(',', ' '));
        }
    }

    foreach (var op in ops)
    {
        var f = op.Fields;
        if (!f.TryGetProperty("startMm", out var st) || !f.TryGetProperty("endMm", out var en))
            continue;
        double sx = st[0].GetDouble(), sy = st[1].GetDouble(), ex = en[0].GetDouble(), ey = en[1].GetDouble();
        var w = f.TryGetProperty("widthMm", out var wEl) && wEl.ValueKind == JsonValueKind.Number ? wEl.GetDouble() : 200;
        var len = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
        if (len < 1)
            continue;
        var nx = -(ey - sy) / len * w / 2;
        var ny = (ex - sx) / len * w / 2;
        sb.AppendFormat(inv, "<polygon points=\"{0} {1} {2} {3}\" fill=\"#ff000033\" stroke=\"#d00\" stroke-width=\"8\"/>\n",
            P(sx + nx, sy + ny), P(ex + nx, ey + ny), P(ex - nx, ey - ny), P(sx - nx, sy - ny));
        sb.AppendFormat(inv, "<line x1=\"{0:0.#}\" y1=\"{1:0.#}\" x2=\"{2:0.#}\" y2=\"{3:0.#}\" stroke=\"#d00\" stroke-width=\"6\" stroke-dasharray=\"60 40\"/>\n",
            sx - minX, maxY - sy, ex - minX, maxY - ey);
        var mark = f.TryGetProperty("mark", out var m) ? m.GetString() : "";
        sb.AppendFormat(inv, "<text x=\"{0:0.#}\" y=\"{1:0.#}\" font-size=\"180\" fill=\"#d00\">{2}</text>\n",
            (sx + ex) / 2 - minX, maxY - (sy + ey) / 2, System.Security.SecurityElement.Escape(mark ?? ""));
    }

    foreach (var e in layer)
    {
        if (e is CadLine l)
            sb.AppendFormat(inv, "<line x1=\"{0:0.#}\" y1=\"{1:0.#}\" x2=\"{2:0.#}\" y2=\"{3:0.#}\" stroke=\"#000\" stroke-width=\"6\"/>\n",
                l.X0 - minX, maxY - l.Y0, l.X1 - minX, maxY - l.Y1);
        else if (e is CadPolyline p && p.Points.Count > 1)
            sb.AppendFormat(inv, "<{0} points=\"{1}\" fill=\"none\" stroke=\"#000\" stroke-width=\"6\"/>\n",
                p.Closed ? "polygon" : "polyline", string.Join(" ", p.Points.Select(pt => P(pt.X, pt.Y))));
    }

    foreach (var t in geom.Entities.OfType<CadText>())
    {
        if (!BeamLabelParser.TryParse(t.Value, out _))
            continue;
        double tx = t.CenterX ?? t.X, ty = t.CenterY ?? t.Y;
        if (tx < minX || tx > maxX || ty < minY || ty > maxY)
            continue;
        sb.AppendFormat(inv, "<text x=\"{0:0.#}\" y=\"{1:0.#}\" font-size=\"200\" fill=\"#0a7\" transform=\"rotate({2:0.#} {3})\">{4}</text>\n",
            tx - minX, maxY - ty, -t.RotationDeg, P(tx, ty).Replace(',', ' '), System.Security.SecurityElement.Escape(t.Value));
    }

    sb.Append("</svg>\n");
    return sb.ToString();
}
