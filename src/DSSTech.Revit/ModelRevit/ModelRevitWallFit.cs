using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Tool «Khớp kết cấu»: the selected rough walls, or every rough wall of the plan's level, are cut at the columns and
/// structural walls and topped at the beam or slab underside (structure in this model and in linked models).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ModelWallFitCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        var doc = uidoc?.Document;
        if (doc is null || uidoc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        var selected = uidoc.Selection.GetElementIds().Where(id => doc.GetElement(id) is Wall).ToList();
        List<ElementId> ids;
        string scope;
        if (selected.Count > 0)
        {
            ids = selected;
            scope = $"{selected.Count} tường đang chọn";
        }
        else
        {
            if (!ModelRevitLevelContext.TryResolve(doc.ActiveView, doc, out var level, out _, out var levelErr))
            {
                TaskDialog.Show("DSSTech — Khớp kết cấu", levelErr + "\nHoặc chọn tường trước rồi gọi lệnh.");
                return Result.Cancelled;
            }

            ids = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Where(w => w.LevelId == level.Id)
                .Select(w => w.Id)
                .ToList();
            scope = $"tường thô của level {level.Name}";
        }

        var fit = ModelRevitWallFit.Fit(doc, ids);
        var text = fit.Solids == 0
            ? $"{scope}: không thấy cột/vách/dầm/sàn kết cấu (model hoặc file link) để cắt và hạ đỉnh tường."
            : $"{scope} (bỏ qua vách):\n" +
              $"Cắt tại mặt cột/vách: {fit.Trimmed} tường; tách thêm {fit.Split} đoạn (cột giữa tường); " +
              $"xoá {fit.Deleted} tường nằm trọn trong kết cấu.\n" +
              $"Hạ đỉnh tới đáy dầm/sàn phía trên: {fit.Lowered} tường." +
              (fit.Uneven > 0 ? $"\n{fit.Uneven} tường có đáy dầm/sàn không đều dọc tường: lấy mức gặp trên phần lớn chiều dài." : "") +
              "\nCột có cửa ở cả hai phía: chỉ cắt hai đầu tường (cửa không chuyển sang đoạn mới được).";
        TaskDialog.Show("DSSTech — Khớp kết cấu", text);
        return Result.Succeeded;
    }
}

/// <summary>
/// Fits rough (architectural) walls to the structure, in this model or in linked models: the wall stops at the
/// faces of the columns and structural walls it runs into, and its top comes down to the underside of the beam or
/// slab above it.
/// </summary>
internal static class ModelRevitWallFit
{
    private const double MinPieceMm = 50;
    private const double SampleStepMm = 500;
    /// <summary>The wall is cut at this height above its base (below any beam, above skirting).</summary>
    private const double CutHeightMm = 1000;
    /// <summary>Beams and slabs are looked for up to this far above the wall's top level.</summary>
    private const double SearchAboveMm = 3000;

    public sealed class Report
    {
        public int Trimmed { get; set; }
        public int Split { get; set; }
        public int Deleted { get; set; }
        public int Lowered { get; set; }
        public int Uneven { get; set; }
        public int Solids { get; set; }
    }

    internal sealed record Piece(Solid Solid, BoundingBoxXYZ Box);

    /// <summary>Beams and slabs of this model and of linked models, as solids in this model's frame.</summary>
    internal static List<Piece> Overhead(Document doc)
    {
        var vertical = new List<Piece>();
        var horizontal = new List<Piece>();
        Collect(doc, Transform.Identity, linked: false, vertical, horizontal);
        foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            if (link.GetLinkDocument() is { } linkDoc)
                Collect(linkDoc, link.GetTotalTransform(), linked: true, new List<Piece>(), horizontal);
        }

        return horizontal;
    }

    /// <summary>Lowest bottom of a beam or slab above the point, between <paramref name="fromZ"/> and <paramref name="toZ"/> (internal units).</summary>
    internal static double? Underside(List<Piece> overhead, double x, double y, double fromZ, double toZ)
    {
        var ray = Line.CreateBound(new XYZ(x, y, fromZ), new XYZ(x, y, toZ));
        double? lowest = null;
        foreach (var piece in overhead)
        {
            if (!Overlaps(piece.Box, ray))
                continue;
            var hit = piece.Solid.IntersectWithCurve(ray,
                new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside });
            for (var k = 0; k < hit.SegmentCount; k++)
            {
                var seg = hit.GetCurveSegment(k);
                var bottom = Math.Min(seg.GetEndPoint(0).Z, seg.GetEndPoint(1).Z);
                if (lowest is null || bottom < lowest)
                    lowest = bottom;
            }
        }

        return lowest;
    }

    public static Report Fit(Document doc, IEnumerable<ElementId> ids)
    {
        var report = new Report();
        var walls = ids.Select(doc.GetElement).OfType<Wall>()
            .Where(w => w.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() != 1 &&
                        w.Location is LocationCurve { Curve: Line })
            .Distinct()
            .ToList();
        if (walls.Count == 0)
            return report;

        var vertical = new List<Piece>();
        var horizontal = new List<Piece>();
        Collect(doc, Transform.Identity, linked: false, vertical, horizontal);
        foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            if (link.GetLinkDocument() is { } linkDoc)
                Collect(linkDoc, link.GetTotalTransform(), linked: true, vertical, horizontal);
        }

        report.Solids = vertical.Count + horizontal.Count;
        DssTechLog.Write($"wall fit: walls={walls.Count} vertical={vertical.Count} horizontal={horizontal.Count}");
        if (report.Solids == 0)
            return report;

        using var tx = new Transaction(doc, "DSSTech fit walls to structure");
        tx.Start();
        foreach (var wall in walls)
        {
            try
            {
                if (!Cut(doc, wall, vertical, report))
                    continue;
                doc.Regenerate();
                Lower(doc, wall, horizontal, report);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or InvalidOperationException)
            {
                DssTechLog.Write($"wall fit {wall.Id.Value}: {ex.Message}");
            }
        }

        tx.Commit();
        return report;
    }

    /// <summary>Columns and structural walls (vertical), beams and floors (horizontal), as solids in this model's frame.</summary>
    private static void Collect(Document source, Transform toHost, bool linked, List<Piece> vertical, List<Piece> horizontal)
    {
        void Add(Element e, List<Piece> into)
        {
            var options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
            foreach (var solid in Solids(e.get_Geometry(options)))
            {
                var placed = toHost.IsIdentity ? solid : SolidUtils.CreateTransformed(solid, toHost);
                var box = placed.GetBoundingBox();
                var world = new BoundingBoxXYZ
                {
                    Min = box.Transform.OfPoint(box.Min),
                    Max = box.Transform.OfPoint(box.Max)
                };
                var min = new XYZ(Math.Min(world.Min.X, world.Max.X), Math.Min(world.Min.Y, world.Max.Y), Math.Min(world.Min.Z, world.Max.Z));
                var max = new XYZ(Math.Max(world.Min.X, world.Max.X), Math.Max(world.Min.Y, world.Max.Y), Math.Max(world.Min.Z, world.Max.Z));
                into.Add(new Piece(placed, new BoundingBoxXYZ { Min = min, Max = max }));
            }
        }

        foreach (var bic in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns })
        {
            foreach (var e in new FilteredElementCollector(source).OfCategory(bic).WhereElementIsNotElementType())
                Add(e, vertical);
        }

        foreach (var wall in new FilteredElementCollector(source).OfClass(typeof(Wall)).Cast<Wall>())
        {
            // A structural model's walls are all structure; in this model only the structural ones are.
            if (linked || wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() == 1)
                Add(wall, vertical);
        }

        foreach (var bic in new[] { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Floors })
        {
            foreach (var e in new FilteredElementCollector(source).OfCategory(bic).WhereElementIsNotElementType())
                Add(e, horizontal);
        }
    }

    private static IEnumerable<Solid> Solids(GeometryElement? element)
    {
        if (element is null)
            yield break;
        foreach (var obj in element)
        {
            if (obj is Solid { Volume: > 1e-9 } solid)
                yield return solid;
            else if (obj is GeometryInstance gi)
            {
                foreach (var inner in Solids(gi.GetInstanceGeometry()))
                    yield return inner;
            }
        }
    }

    /// <summary>
    /// Cuts the wall at the structure it runs into. Ends inside a column or wall pull back to its face; a column in
    /// the middle splits the wall, unless doors or windows sit on both sides (then only the ends are trimmed).
    /// Returns false when the wall was deleted.
    /// </summary>
    private static bool Cut(Document doc, Wall wall, List<Piece> vertical, Report report)
    {
        var lc = (LocationCurve)wall.Location;
        var line = (Line)lc.Curve;
        var a = line.GetEndPoint(0);
        var b = line.GetEndPoint(1);
        var u = (b - a).Normalize();
        var len = line.Length;
        var z = BaseZ(doc, wall) + UnitConv.MmToInternal(CutHeightMm);
        var probe = Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z));
        var inside = new List<(double From, double To)>();
        foreach (var piece in vertical)
        {
            if (!Overlaps(piece.Box, probe))
                continue;
            var hit = piece.Solid.IntersectWithCurve(probe,
                new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside });
            for (var i = 0; i < hit.SegmentCount; i++)
            {
                var seg = hit.GetCurveSegment(i);
                var s0 = (seg.GetEndPoint(0) - probe.GetEndPoint(0)).DotProduct(u);
                var s1 = (seg.GetEndPoint(1) - probe.GetEndPoint(0)).DotProduct(u);
                inside.Add((Math.Min(s0, s1), Math.Max(s0, s1)));
            }
        }

        if (inside.Count == 0)
            return true;
        var min = UnitConv.MmToInternal(MinPieceMm);
        var pieces = new List<(double From, double To)>();
        var at = 0.0;
        foreach (var cut in inside.OrderBy(c => c.From))
        {
            if (cut.From > at + min)
                pieces.Add((at, cut.From));
            at = Math.Max(at, cut.To);
        }

        if (len > at + min)
            pieces.Add((at, len));

        var inserts = wall.FindInserts(true, false, false, false)
            .Select(doc.GetElement).OfType<FamilyInstance>()
            .Select(fi => fi.Location is LocationPoint p ? (p.Point - a).DotProduct(u) : double.NaN)
            .Where(s => !double.IsNaN(s))
            .ToList();
        if (pieces.Count == 0)
        {
            if (inserts.Count > 0)
                return true;
            // The whole wall lies inside structure: a duplicate of a structural wall.
            DssTechLog.Write($"wall fit {wall.Id.Value}: inside structure, deleted");
            doc.Delete(wall.Id);
            report.Deleted++;
            return false;
        }

        var holding = pieces.Where(p => inserts.Any(s => s >= p.From && s <= p.To)).ToList();
        if (holding.Count > 1)
        {
            // Doors on both sides of a column: keep one wall, only its ends come back to the structure.
            pieces = new List<(double, double)> { (pieces[0].From, pieces[pieces.Count - 1].To) };
            holding = pieces;
        }

        var keep = holding.Count == 1 ? holding[0] : pieces.OrderByDescending(p => p.To - p.From).First();
        if (keep.From > 1e-6 || keep.To < len - 1e-6)
        {
            lc.Curve = Line.CreateBound(a + u * keep.From, a + u * keep.To);
            report.Trimmed++;
        }

        foreach (var p in pieces.Where(p => p != keep))
        {
            var copy = Wall.Create(doc, Line.CreateBound(a + u * p.From, a + u * p.To), wall.WallType.Id, wall.LevelId,
                UnitConv.MmToInternal(3000), 0, wall.Flipped, false);
            foreach (var bip in new[]
                     {
                         BuiltInParameter.WALL_KEY_REF_PARAM, BuiltInParameter.WALL_BASE_OFFSET,
                         BuiltInParameter.WALL_HEIGHT_TYPE, BuiltInParameter.WALL_TOP_OFFSET,
                         BuiltInParameter.WALL_USER_HEIGHT_PARAM
                     })
                CopyParam(wall, copy, bip);
            report.Split++;
        }

        return true;
    }

    /// <summary>
    /// Top of the wall down to the underside of the beam or slab above it: the underside that covers most of the
    /// wall's length, so a crossing beam does not pull the whole wall down.
    /// </summary>
    private static void Lower(Document doc, Wall wall, List<Piece> horizontal, Report report)
    {
        var line = (Line)((LocationCurve)wall.Location).Curve;
        var baseZ = BaseZ(doc, wall);
        var topLevel = doc.GetElement(wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() ?? ElementId.InvalidElementId) as Level;
        var currentTop = baseZ + (wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0);
        var searchTop = (topLevel?.Elevation ?? currentTop) + UnitConv.MmToInternal(SearchAboveMm);
        var from = baseZ + UnitConv.MmToInternal(CutHeightMm);
        var len = line.Length;
        var step = UnitConv.MmToInternal(SampleStepMm);
        var count = Math.Max(2, (int)Math.Ceiling(len / step) + 1);
        var hits = new List<double>();
        for (var i = 0; i < count; i++)
        {
            var t = Math.Min(Math.Max(i * len / (count - 1), UnitConv.MmToInternal(50)), len - UnitConv.MmToInternal(50));
            var p = line.Evaluate(t / len, true);
            var ray = Line.CreateBound(new XYZ(p.X, p.Y, from), new XYZ(p.X, p.Y, searchTop));
            double? lowest = null;
            foreach (var piece in horizontal)
            {
                if (!Overlaps(piece.Box, ray))
                    continue;
                var hit = piece.Solid.IntersectWithCurve(ray,
                    new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside });
                for (var k = 0; k < hit.SegmentCount; k++)
                {
                    var seg = hit.GetCurveSegment(k);
                    var bottom = Math.Min(seg.GetEndPoint(0).Z, seg.GetEndPoint(1).Z);
                    if (lowest is null || bottom < lowest)
                        lowest = bottom;
                }
            }

            if (lowest is double l)
                hits.Add(l);
        }

        if (hits.Count == 0)
            return;
        // Undersides to 5 mm; the one met along most of the wall wins.
        var groups = hits.GroupBy(h => UnitConv.Round5Mm(UnitConv.ToMm(h))).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        var underside = UnitConv.MmToInternal(groups[0].Key);
        if (groups.Count > 1 && groups.Max(g => g.Key) - groups.Min(g => g.Key) > 50)
            report.Uneven++;
        if (topLevel is not null)
        {
            var offset = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
            var want = underside - topLevel.Elevation;
            if (offset is null || offset.IsReadOnly || Math.Abs(offset.AsDouble() - want) < UnitConv.MmToInternal(1))
                return;
            offset.Set(want);
        }
        else
        {
            var height = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            var want = underside - baseZ;
            if (height is null || height.IsReadOnly || want <= 0 || Math.Abs(height.AsDouble() - want) < UnitConv.MmToInternal(1))
                return;
            height.Set(want);
        }

        report.Lowered++;
    }

    private static double BaseZ(Document doc, Wall wall)
    {
        var level = doc.GetElement(wall.LevelId) as Level;
        return (level?.Elevation ?? 0) + (wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0);
    }

    private static bool Overlaps(BoundingBoxXYZ box, Line line)
    {
        var a = line.GetEndPoint(0);
        var b = line.GetEndPoint(1);
        return Math.Max(a.X, b.X) >= box.Min.X && Math.Min(a.X, b.X) <= box.Max.X &&
               Math.Max(a.Y, b.Y) >= box.Min.Y && Math.Min(a.Y, b.Y) <= box.Max.Y &&
               Math.Max(a.Z, b.Z) >= box.Min.Z && Math.Min(a.Z, b.Z) <= box.Max.Z;
    }

    private static void CopyParam(Wall from, Wall to, BuiltInParameter bip)
    {
        var src = from.get_Parameter(bip);
        var dst = to.get_Parameter(bip);
        if (src is null || dst is null || dst.IsReadOnly)
            return;
        switch (src.StorageType)
        {
            case StorageType.Double:
                dst.Set(src.AsDouble());
                break;
            case StorageType.Integer:
                dst.Set(src.AsInteger());
                break;
            case StorageType.ElementId:
                dst.Set(src.AsElementId());
                break;
        }
    }
}
