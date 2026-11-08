using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>Shared geometry for the Combine tools: MEP sizes, solids, structure above, detours.</summary>
internal static class MepCombine
{
    /// <summary>Room for the elbows each side of an obstacle, past half the run's size.</summary>
    private const double ElbowRoomMm = 150;
    private const double MinPieceMm = 100;

    /// <summary>The run's system (thoát nước, cấp nước, PCCC… not just "pipe"), else its category.</summary>
    public static string Kind(Element e) =>
        CombineProfileStore.Identify(e) is var (s, code) && s != Dss.Cad2Bim.Core.MepSystemCode.Unknown
            ? Dss.Cad2Bim.Core.MepSystemClassifier.Label(s, code)
            : e switch
            {
                Pipe => "Ống nước (chưa rõ hệ)",
                _ => e.Category?.Name ?? "?"
            };

    /// <summary>Section height of a run (diameter when round), internal units.</summary>
    public static double Depth(MEPCurve c)
    {
        foreach (var bip in new[]
                 {
                     BuiltInParameter.RBS_PIPE_OUTER_DIAMETER, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM,
                     BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM,
                     BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM
                 })
        {
            if (c.get_Parameter(bip) is { StorageType: StorageType.Double } p && p.AsDouble() > 0)
                return p.AsDouble();
        }

        return UnitConv.MmToInternal(100);
    }

    public static double Width(MEPCurve c)
    {
        foreach (var bip in new[]
                 {
                     BuiltInParameter.RBS_PIPE_OUTER_DIAMETER, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM,
                     BuiltInParameter.RBS_CURVE_WIDTH_PARAM, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM,
                     BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM
                 })
        {
            if (c.get_Parameter(bip) is { StorageType: StorageType.Double } p && p.AsDouble() > 0)
                return p.AsDouble();
        }

        return UnitConv.MmToInternal(100);
    }

    public static IEnumerable<Solid> Solids(Element e, Transform? toHost = null)
    {
        var options = new Options { DetailLevel = ViewDetailLevel.Fine };
        foreach (var s in Walk(e.get_Geometry(options)))
            yield return toHost is null || toHost.IsIdentity ? s : SolidUtils.CreateTransformed(s, toHost);
    }

    private static IEnumerable<Solid> Walk(GeometryElement? g)
    {
        if (g is null)
            yield break;
        foreach (var obj in g)
        {
            if (obj is Solid { Volume: > 1e-9 } solid)
                yield return solid;
            else if (obj is GeometryInstance gi)
            {
                foreach (var inner in Walk(gi.GetInstanceGeometry()))
                    yield return inner;
            }
        }
    }

    /// <summary>World box of solids (axis aligned).</summary>
    public static (XYZ Min, XYZ Max)? Box(IEnumerable<Solid> solids)
    {
        XYZ? min = null, max = null;
        foreach (var s in solids)
        {
            var bb = s.GetBoundingBox();
            foreach (var c in Corners(bb.Min, bb.Max))
            {
                var p = bb.Transform.OfPoint(c);
                min = min is null ? p : new XYZ(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = max is null ? p : new XYZ(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
        }

        return min is null ? null : (min, max!);
    }

    public static IEnumerable<XYZ> Corners(XYZ a, XYZ b)
    {
        foreach (var x in new[] { a.X, b.X })
        foreach (var y in new[] { a.Y, b.Y })
        foreach (var z in new[] { a.Z, b.Z })
            yield return new XYZ(x, y, z);
    }

    /// <summary>
    /// The run goes under (or over) each obstacle on its way: two vertical pieces and four elbows around the
    /// obstacle's extent along the run, clear of it by <paramref name="clearance"/>. Obstacles close together share
    /// one detour. Returns how many detours were made; reasons for the ones skipped go to <paramref name="notes"/>.
    /// </summary>
    public static int Detour(Document doc, MEPCurve run, IEnumerable<(XYZ Min, XYZ Max)> obstacles, bool under, double clearance,
        List<string> notes) =>
        Detour(doc, run, obstacles.Select(o => (o.Min, o.Max, clearance)), under, 90, notes);

    /// <summary>
    /// As above, each obstacle with its own clearance (the pair's), the legs bent by <paramref name="angleDeg"/>
    /// (90: straight down and up; 45: sloped legs with 45° elbows).
    /// </summary>
    public static int Detour(Document doc, MEPCurve run, IEnumerable<(XYZ Min, XYZ Max, double Clearance)> obstacles, bool under,
        double angleDeg, List<string> notes)
    {
        if (run.Location is not LocationCurve { Curve: Line line })
            return 0;
        var a = line.GetEndPoint(0);
        var b = line.GetEndPoint(1);
        if (Math.Abs(a.Z - b.Z) > UnitConv.MmToInternal(1))
        {
            notes.Add($"{Kind(run)} {run.Id.Value}: ống dốc, không đổi tuyến tự động");
            return 0;
        }

        var u = new XYZ(b.X - a.X, b.Y - a.Y, 0).Normalize();
        var length = new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength();
        var depth = Depth(run);
        var half = Math.Max(depth, Width(run)) / 2;
        var spans = new List<(double S0, double S1, double Z)>();
        foreach (var (min, max, clearance) in obstacles)
        {
            var along = Corners(min, max).Select(p => (p - a).DotProduct(u)).ToList();
            // Room for the elbows, and the vertical legs kept the pair's clearance off the obstacle's sides.
            var lead = half + Math.Max(UnitConv.MmToInternal(ElbowRoomMm), clearance);
            var s0 = along.Min() - lead;
            var s1 = along.Max() + lead;
            if (s1 < 0 || s0 > length)
                continue;
            var z = under ? min.Z - clearance - depth / 2 : max.Z + clearance + depth / 2;
            spans.Add((s0, s1, z));
        }

        // Overlapping detours become one, at the deepest (or highest) level any of them needs.
        var merged = new List<(double S0, double S1, double Z)>();
        foreach (var s in spans.OrderBy(x => x.S0))
        {
            if (merged.Count > 0 && s.S0 <= merged[merged.Count - 1].S1)
            {
                var m = merged[merged.Count - 1];
                merged[merged.Count - 1] = (m.S0, Math.Max(m.S1, s.S1), under ? Math.Min(m.Z, s.Z) : Math.Max(m.Z, s.Z));
            }
            else
                merged.Add(s);
        }

        var made = 0;
        var current = run;
        var start = 0.0;
        var minPiece = UnitConv.MmToInternal(MinPieceMm);
        foreach (var (m0, m1, z) in merged)
        {
            // Sloped legs reach out by as much run as they drop (45°), so the level part still clears the obstacle.
            var leg = UnitConv.MmToInternal(Dss.Cad2Bim.Core.CombineAnalyzer.LegRun(angleDeg, UnitConv.ToMm(Math.Abs(z - a.Z))));
            var s0 = m0 - leg;
            var s1 = m1 + leg;
            if (s0 - start < minPiece || length - s1 < minPiece)
            {
                notes.Add($"{Kind(run)} {run.Id.Value}: vật cản sát đầu ống, cần sửa tay");
                continue;
            }

            if (under ? z >= a.Z - UnitConv.MmToInternal(1) : z <= a.Z + UnitConv.MmToInternal(1))
                continue;
            current = DetourOnce(doc, current, a, u, s0, s1, leg, a.Z, z, b, notes) ?? current;
            start = s1;
            made++;
        }

        return made;
    }

    private static MEPCurve? DetourOnce(Document doc, MEPCurve run, XYZ origin, XYZ u, double s0, double s1, double leg, double z, double z2,
        XYZ end, List<string> notes)
    {
        XYZ At(double s, double zz) => new(origin.X + u.X * s, origin.Y + u.Y * s, zz);
        var lc = (LocationCurve)run.Location;
        var startPoint = ((Line)lc.Curve).GetEndPoint(0);
        var p1 = At(s0, z);
        var p1Low = At(s0 + leg, z2);
        var p2Low = At(s1 - leg, z2);
        var p2 = At(s1, z);

        // What the far end was joined to: the tail takes it over.
        var farEnd = MepFromCad.End(run, end);
        var joined = farEnd.AllRefs.Cast<Connector>().Where(c => c.Owner.Id != run.Id).ToList();
        foreach (var c in joined)
            farEnd.DisconnectFrom(c);

        lc.Curve = Line.CreateBound(startPoint, p1);
        MEPCurve Copy(XYZ from, XYZ to)
        {
            var id = ElementTransformUtils.CopyElement(doc, run.Id, XYZ.Zero).First();
            var copy = (MEPCurve)doc.GetElement(id);
            ((LocationCurve)copy.Location).Curve = Line.CreateBound(from, to);
            return copy;
        }

        var down = Copy(p1, p1Low);
        var low = Copy(p1Low, p2Low);
        var up = Copy(p2Low, p2);
        var tail = Copy(p2, end);
        doc.Regenerate();
        foreach (var c in joined)
        {
            try
            {
                MepFromCad.End(tail, end).ConnectTo(c);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                notes.Add($"{Kind(run)} {run.Id.Value}: nối lại đầu ống: {ex.Message}");
            }
        }

        var failures = new List<string>();
        MepFromCad.Elbow(doc, MepFromCad.End(run, p1), MepFromCad.End(down, p1), failures);
        MepFromCad.Elbow(doc, MepFromCad.End(down, p1Low), MepFromCad.End(low, p1Low), failures);
        MepFromCad.Elbow(doc, MepFromCad.End(low, p2Low), MepFromCad.End(up, p2Low), failures);
        MepFromCad.Elbow(doc, MepFromCad.End(up, p2), MepFromCad.End(tail, p2), failures);
        notes.AddRange(failures.Select(f => $"{Kind(run)} {run.Id.Value}: {f}"));
        return tail;
    }

    /// <summary>MEP elements joined to each other through their connectors, within <paramref name="pool"/>.</summary>
    public static List<List<Element>> Networks(Document doc, IReadOnlyCollection<Element> pool)
    {
        var ids = pool.Select(e => e.Id).ToHashSet();
        var seen = new HashSet<ElementId>();
        var result = new List<List<Element>>();
        foreach (var e in pool)
        {
            if (!seen.Add(e.Id))
                continue;
            var net = new List<Element>();
            var open = new Stack<Element>();
            open.Push(e);
            while (open.Count > 0)
            {
                var x = open.Pop();
                net.Add(x);
                foreach (var c in Connectors(x))
                {
                    foreach (Connector r in c.AllRefs)
                    {
                        var o = r.Owner;
                        if (o is null || !ids.Contains(o.Id) || !seen.Add(o.Id))
                            continue;
                        open.Push(o);
                    }
                }
            }

            result.Add(net);
        }

        return result;
    }

    public static IEnumerable<Connector> Connectors(Element e) => e switch
    {
        MEPCurve c => c.ConnectorManager.Connectors.Cast<Connector>(),
        FamilyInstance fi when fi.MEPModel?.ConnectorManager is { } cm => cm.Connectors.Cast<Connector>(),
        _ => Enumerable.Empty<Connector>()
    };

    public static string Summary(IEnumerable<string> notes, int max = 12)
    {
        var list = notes.ToList();
        if (list.Count == 0)
            return "";
        var sb = new StringBuilder();
        sb.AppendLine($"Cần xem lại ({list.Count}):");
        foreach (var n in list.Take(max))
            sb.AppendLine("  " + n);
        if (list.Count > max)
            sb.AppendLine($"  … xem log ({list.Count - max} dòng nữa)");
        foreach (var n in list)
            DssTechLog.Write("combine: " + n);
        return sb.ToString();
    }
}
