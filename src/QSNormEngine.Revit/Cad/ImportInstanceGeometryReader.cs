using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace QSNormEngine.Revit.Cad;

internal static class ImportInstanceGeometryReader
{
    public static CadGeometry Read(ImportInstance import, View? view)
    {
        var geom = new CadGeometry
        {
            SourcePath = import.Name ?? "",
            InsUnits = 4 // Revit stores model units; CAD link is expected mm per spec. Unknown CAD INSUNITS → fail later if DXF parse overrides.
        };

        var opt = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false
        };
        if (view is not null)
            opt.View = view;

        var elementGeom = import.get_Geometry(opt);
        if (elementGeom is null)
            return geom;

        foreach (var obj in elementGeom)
            Walk(obj, Transform.Identity, geom, import.Document);

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

        return geom;
    }

    private static void Walk(GeometryObject obj, Transform transform, CadGeometry geom, Document doc)
    {
        switch (obj)
        {
            case GeometryInstance gi:
                var t = transform.Multiply(gi.Transform);
                foreach (var child in gi.GetInstanceGeometry())
                    Walk(child, t, geom, doc);
                break;
            case Line line:
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
                var polyArc = new CadPolyline { Layer = LayerName(obj, doc), Closed = false };
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
        }
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
