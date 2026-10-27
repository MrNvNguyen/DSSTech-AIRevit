using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

internal static class ModelRevitGrids
{
    public static List<CadPoint2> Intersections(Document doc, View view)
    {
        var lines = new List<Seg>();
        foreach (var grid in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
        {
            foreach (var curve in BoundLines(grid, view))
            {
                var a = curve.GetEndPoint(0);
                var b = curve.GetEndPoint(1);
                lines.Add(new Seg(UnitConv.ToMm(a.X), UnitConv.ToMm(a.Y), UnitConv.ToMm(b.X), UnitConv.ToMm(b.Y)));
            }
        }

        var points = new List<CadPoint2>();
        for (var i = 0; i < lines.Count; i++)
        {
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (!TryIntersect(lines[i], lines[j], out var x, out var y))
                    continue;
                points.Add(new CadPoint2(x, y));
            }
        }

        return points;
    }

    /// <summary>Grid directions in degrees, folded to (-90, 90]. Parallel grids share one angle.</summary>
    public static List<double> Angles(Document doc, View view)
    {
        var angles = new List<double>();
        foreach (var grid in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
        {
            foreach (var curve in BoundLines(grid, view))
            {
                var a = curve.GetEndPoint(0);
                var b = curve.GetEndPoint(1);
                var deg = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
                while (deg <= -90.0)
                    deg += 180.0;
                while (deg > 90.0)
                    deg -= 180.0;
                var known = false;
                for (var i = 0; i < angles.Count; i++)
                {
                    var delta = Math.Abs(angles[i] - deg);
                    if (delta > 90.0)
                        delta = 180.0 - delta;
                    if (delta <= 1.0)
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                    angles.Add(deg);
            }
        }

        return angles;
    }

    private static IEnumerable<Line> BoundLines(Grid grid, View view)
    {
        foreach (var extent in new[] { DatumExtentType.ViewSpecific, DatumExtentType.Model })
        {
            IList<Curve>? curves = null;
            try
            {
                curves = grid.GetCurvesInView(extent, view);
            }
            catch
            {
                continue;
            }

            if (curves == null)
                continue;

            var any = false;
            foreach (var curve in curves)
            {
                if (curve is Line line && line.IsBound)
                {
                    any = true;
                    yield return line;
                }
            }

            if (any)
                yield break;
        }

        if (grid.Curve is Line unbound)
        {
            var origin = unbound.Origin;
            var dir = unbound.Direction;
            const double halfFeet = 2000;
            yield return Line.CreateBound(origin - dir * halfFeet, origin + dir * halfFeet);
        }
    }

    private static bool TryIntersect(Seg a, Seg b, out double x, out double y)
    {
        x = y = 0;
        var rx = a.X1 - a.X0;
        var ry = a.Y1 - a.Y0;
        var sx = b.X1 - b.X0;
        var sy = b.Y1 - b.Y0;
        var den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1)
            return false;

        var qx = b.X0 - a.X0;
        var qy = b.Y0 - a.Y0;
        var t = (qx * sy - qy * sx) / den;
        var u = (qx * ry - qy * rx) / den;
        if (t < -0.05 || t > 1.05 || u < -0.05 || u > 1.05)
            return false;

        x = a.X0 + t * rx;
        y = a.Y0 + t * ry;
        return true;
    }

    private readonly struct Seg
    {
        public Seg(double x0, double y0, double x1, double y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
    }
}
