using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.Cad;

internal static class ImportInstanceGeometryReader
{
    public static string? TryGetLayerFromReference(ImportInstance import, Reference reference)
    {
        try
        {
            var obj = import.GetGeometryObjectFromReference(reference);
            return obj is null ? null : LayerName(obj, import.Document);
        }
        catch
        {
            return null;
        }
    }

    public static CadGeometry Read(ImportInstance import, View? view)
    {
        var geom = new CadGeometry
        {
            SourcePath = import.Name ?? "",
            InsUnits = 4 // Revit stores model units; CAD link is expected mm per spec. Unknown CAD INSUNITS → fail later if DXF parse overrides.
        };

        // No Options.View: a view only returns the CAD tessellation already drawn,
        // so the first command misses outlines that appear on the second run.
        var opt = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = true
        };

        var elementGeom = import.get_Geometry(opt);
        if (elementGeom is not null)
        {
            var link = import.GetTotalTransform();
            foreach (var obj in elementGeom)
            {
                if (obj is GeometryInstance gi)
                {
                    foreach (var child in gi.GetSymbolGeometry())
                        Walk(child, link, geom, import.Document);
                }
                else
                    Walk(obj, Transform.Identity, geom, import.Document);
            }
        }

        // Prefer DXF parse when external path is available (true INSUNITS).
        try
        {
            var typeId = import.GetTypeId();
            var type = import.Document.GetElement(typeId) as CADLinkType;
            var ext = type?.GetExternalFileReference();
            if (ext != null)
            {
                var path = ModelPathUtils.ConvertModelPathToUserVisiblePath(ext.GetPath());
                if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) &&
                    System.IO.File.Exists(path))
                {
                    var fromFile = DxfParser.Parse(path);
                    if (fromFile.Entities.Count > 0)
                        return fromFile;
                }
            }
        }
        catch
        {
            // keep geometry-from-Revit
        }

        CadLinkText.Append(import, view, geom);
        return geom;
    }

    private static void Walk(GeometryObject obj, Transform transform, CadGeometry geom, Document doc)
    {
        switch (obj)
        {
            case GeometryInstance gi:
                var t = transform.Multiply(gi.Transform);
                foreach (var child in gi.GetSymbolGeometry())
                    Walk(child, t, geom, doc);
                break;
            case Line line:
                if (!line.IsBound)
                    break;
                var p0 = transform.OfPoint(line.GetEndPoint(0));
                var p1 = transform.OfPoint(line.GetEndPoint(1));
                geom.Entities.Add(new CadLine
                {
                    Layer = LayerName(obj, doc),
                    X0 = UnitConv.ToMm(p0.X),
                    Y0 = UnitConv.ToMm(p0.Y),
                    X1 = UnitConv.ToMm(p1.X),
                    Y1 = UnitConv.ToMm(p1.Y)
                });
                break;
            case Arc arc:
            {
                var layer = LayerName(obj, doc);
                if (!arc.IsBound)
                {
                    var center = transform.OfPoint(arc.Center);
                    geom.Entities.Add(new CadCircle
                    {
                        Layer = layer,
                        Cx = UnitConv.ToMm(center.X),
                        Cy = UnitConv.ToMm(center.Y),
                        Radius = UnitConv.ToMm(arc.Radius)
                    });
                    break;
                }

                var polyArc = new CadPolyline { Layer = layer, Closed = false };
                var n = 8;
                for (var i = 0; i <= n; i++)
                {
                    var param = arc.GetEndParameter(0) + (arc.GetEndParameter(1) - arc.GetEndParameter(0)) * i / n;
                    var p = transform.OfPoint(arc.Evaluate(param, false));
                    polyArc.Points.Add(new CadPoint2(UnitConv.ToMm(p.X), UnitConv.ToMm(p.Y)));
                }
                geom.Entities.Add(polyArc);
                break;
            }
            case PolyLine poly:
                var polyOut = new CadPolyline { Layer = LayerName(obj, doc) };
                var coords = poly.GetCoordinates();
                foreach (var c in coords)
                {
                    var p = transform.OfPoint(c);
                    polyOut.Points.Add(new CadPoint2(UnitConv.ToMm(p.X), UnitConv.ToMm(p.Y)));
                }
                if (polyOut.Points.Count >= 3)
                {
                    var a = polyOut.Points[0];
                    var b = polyOut.Points[polyOut.Points.Count - 1];
                    polyOut.Closed = Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1;
                }
                geom.Entities.Add(polyOut);
                break;
            case Solid solid:
                AddBoundingRect(solid.GetBoundingBox(), transform, geom, LayerName(obj, doc));
                break;
            case Mesh mesh:
                AddMeshBoundingRect(mesh, transform, geom, LayerName(obj, doc));
                break;
        }
    }

    private static void AddMeshBoundingRect(Mesh mesh, Transform transform, CadGeometry geom, string layer)
    {
        if (mesh.NumTriangles == 0)
            return;
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        for (var i = 0; i < mesh.NumTriangles; i++)
        {
            var tri = mesh.get_Triangle(i);
            for (var v = 0; v < 3; v++)
            {
                var p = transform.OfPoint(tri.get_Vertex(v));
                var x = UnitConv.ToMm(p.X);
                var y = UnitConv.ToMm(p.Y);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        AddRectPoly(minX, minY, maxX, maxY, geom, layer);
    }

    private static void AddBoundingRect(BoundingBoxXYZ? bb, Transform transform, CadGeometry geom, string layer)
    {
        if (bb is null)
            return;
        var c0 = transform.OfPoint(bb.Min);
        var c1 = transform.OfPoint(bb.Max);
        var minX = UnitConv.ToMm(Math.Min(c0.X, c1.X));
        var minY = UnitConv.ToMm(Math.Min(c0.Y, c1.Y));
        var maxX = UnitConv.ToMm(Math.Max(c0.X, c1.X));
        var maxY = UnitConv.ToMm(Math.Max(c0.Y, c1.Y));
        AddRectPoly(minX, minY, maxX, maxY, geom, layer);
    }

    private static void AddRectPoly(double minX, double minY, double maxX, double maxY, CadGeometry geom, string layer)
    {
        if (maxX - minX < 1 || maxY - minY < 1)
            return;
        var poly = new CadPolyline { Layer = layer, Closed = true };
        poly.Points.Add(new CadPoint2(minX, minY));
        poly.Points.Add(new CadPoint2(maxX, minY));
        poly.Points.Add(new CadPoint2(maxX, maxY));
        poly.Points.Add(new CadPoint2(minX, maxY));
        geom.Entities.Add(poly);
    }

    private static string LayerName(GeometryObject obj, Document doc)
    {
        try
        {
            if (obj.GraphicsStyleId != null && obj.GraphicsStyleId != ElementId.InvalidElementId)
            {
                if (doc.GetElement(obj.GraphicsStyleId) is GraphicsStyle gs && gs.GraphicsStyleCategory != null)
                    return gs.GraphicsStyleCategory.Name;
            }
        }
        catch
        {
            // ignore
        }

        return "0";
    }
}
