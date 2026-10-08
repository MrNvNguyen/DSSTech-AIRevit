using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using QSNormEngine.Revit.Cad;

namespace QSNormEngine.Revit;

[Transaction(TransactionMode.ReadOnly)]
public sealed class CadPreflightCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc is null)
            {
                TaskDialog.Show("DSSTech", "Mở một project Revit trước.");
                return Result.Failed;
            }

            var import = uidoc!.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<ImportInstance>()
                .FirstOrDefault();
            if (import is null)
            {
                TaskDialog.Show("DSSTech", "Hãy chọn một CAD link / ImportInstance rồi chạy Preflight.");
                return Result.Failed;
            }

            var geom = ImportInstanceGeometryReader.Read(import, uidoc.ActiveView);
            var maps = LoadMaps();
            var gridEnv = BuildGridEnvelope(doc);
            var report = PreflightEngine.Run(geom, maps, gridEnv);

            var sb = new StringBuilder();
            sb.AppendLine(report.Ok ? "Preflight PASS — chưa tạo đối tượng nào." : "Preflight FAIL — không tạo.");
            sb.AppendLine($"Entities: {geom.Entities.Count}; createdElementCount={report.CreatedElementCount}");
            if (geom.InsUnits is not null)
                sb.AppendLine($"INSUNITS={geom.InsUnits}");
            foreach (var kv in report.CountsByKind.OrderBy(k => k.Key))
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
            foreach (var f in report.Failures)
                sb.AppendLine($"FAIL [{f.Code}] {f.MessageVi}");
            foreach (var w in report.Warnings)
                sb.AppendLine($"WARN [{w.Code}] {w.MessageVi}");

            TaskDialog.Show("DSSTech — CAD Preflight", sb.ToString());
            return report.Ok ? Result.Succeeded : Result.Failed;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("DSSTech", ex.Message);
            return Result.Failed;
        }
    }

    private static List<LayerMap> LoadMaps()
    {
        var maps = new List<LayerMap>();
        var root = PluginPaths.Root;
        foreach (var name in new[] { "cad-layer-map.str.json", "cad-layer-map.arch.json", "cad-layer-map.mepf.json" })
        {
            var path = Path.Combine(root, "Config", name);
            if (File.Exists(path))
                maps.Add(LayerMap.Load(path));
        }

        return maps;
    }

    private static CadBBox? BuildGridEnvelope(Document doc)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var grid in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
        {
            if (grid.Curve is not Line line)
                continue;
            var a = line.GetEndPoint(0);
            var b = line.GetEndPoint(1);
            xs.Add(UnitConv.ToMm(a.X)); xs.Add(UnitConv.ToMm(b.X));
            ys.Add(UnitConv.ToMm(a.Y)); ys.Add(UnitConv.ToMm(b.Y));
        }

        if (xs.Count == 0)
            return null;
        return new CadBBox(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }
}
