using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>Room separation lines closing the open passages between walls, so rooms placed from CAD names are bounded.</summary>
internal static class ModelRevitRoomBounds
{
    public static int CloseGaps(Document doc, View view, string levelName, IReadOnlyList<HostWallAxis> walls)
    {
        var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.Ordinal));
        if (level is null || view is not ViewPlan)
            return 0;
        var gaps = RoomPlanner.Gaps(walls);
        if (gaps.Count == 0)
            return 0;

        // Separation lines from an earlier run stay; only gaps not yet closed get one.
        var existing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RoomSeparationLines)
            .WhereElementIsNotElementType()
            .OfType<CurveElement>()
            .Select(c => c.GeometryCurve)
            .OfType<Line>()
            .ToList();
        var tol = UnitConv.MmToInternal(20);
        var curves = new CurveArray();
        using var tx = new Transaction(doc, "DSSTech room separation");
        tx.Start();
        var plane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation)));
        foreach (var (x0, y0, x1, y1) in gaps)
        {
            var a = new XYZ(UnitConv.MmToInternal(x0), UnitConv.MmToInternal(y0), level.Elevation);
            var b = new XYZ(UnitConv.MmToInternal(x1), UnitConv.MmToInternal(y1), level.Elevation);
            if (a.DistanceTo(b) < tol || existing.Any(e => Near(e, a, b, tol)))
                continue;
            curves.Append(Line.CreateBound(a, b));
        }

        if (curves.Size == 0)
        {
            tx.RollBack();
            return 0;
        }

        doc.Create.NewRoomBoundaryLines(plane, curves, view);
        tx.Commit();
        DssTechLog.Write($"room separation lines: {curves.Size}");
        return curves.Size;
    }

    private static bool Near(Line e, XYZ a, XYZ b, double tol)
    {
        var e0 = e.GetEndPoint(0);
        var e1 = e.GetEndPoint(1);
        bool Same(XYZ p, XYZ q) => Math.Abs(p.X - q.X) <= tol && Math.Abs(p.Y - q.Y) <= tol;
        return (Same(e0, a) && Same(e1, b)) || (Same(e0, b) && Same(e1, a));
    }
}
