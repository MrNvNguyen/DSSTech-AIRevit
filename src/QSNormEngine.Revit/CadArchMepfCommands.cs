using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;

namespace QSNormEngine.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class CadArchCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CadDisciplineRunner.Run(commandData, "cad-layer-map.arch.json", "CAD Kiến trúc", ArchIntentEngine.Plan);
}

[Transaction(TransactionMode.Manual)]
public sealed class CadMepfCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CadDisciplineRunner.Run(commandData, "cad-layer-map.mepf.json", "CAD MEPF", MepfIntentEngine.Plan);
}

[Transaction(TransactionMode.Manual)]
public sealed class CadLinkHelpCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show("DSSTech — Link mặt bằng",
            "1. Insert → Link CAD (Origin-to-Origin hoặc Shared Coordinates).\n" +
            "2. Gán đúng Level của tầng.\n" +
            "3. Chọn ImportInstance → CAD Preflight → Kết cấu / Kiến trúc / MEPF.\n\n" +
            "Spec: docs/CAD_INPUT_SPEC.md (mm, INSUNITS=4, polyline đóng, không nổ block).");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CadDatumCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CadDisciplineRunner.Run(
            commandData,
            "cad-layer-map.str.json",
            "CAD Trục & tầng",
            (geom, map, levelName) =>
            {
                // Datum from G-GRID lines + text; levels usually from MCP/CSV — emit grid ops only.
                var ops = new List<Dss.McpContracts.CreateOperation>();
                foreach (var entity in geom.Entities)
                {
                    if (!string.Equals(entity.Layer, "G-GRID", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (entity is not CadLine line)
                        continue;
                    var name = geom.Entities.OfType<CadText>()
                        .OrderBy(t => Dist2(t.X, t.Y, (line.X0 + line.X1) / 2, (line.Y0 + line.Y1) / 2))
                        .Select(t => t.Value)
                        .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "G";
                    var json = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        name,
                        startMm = new[] { line.X0, line.Y0, 0 },
                        endMm = new[] { line.X1, line.Y1, 0 }
                    });
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    ops.Add(new Dss.McpContracts.CreateOperation
                    {
                        Category = "Grids",
                        Fields = doc.RootElement.Clone(),
                        SkipIfExists = true,
                        Units = "mm"
                    });
                }

                return ops;
            });

    private static double Dist2(double x0, double y0, double x1, double y1)
    {
        var dx = x0 - x1;
        var dy = y0 - y1;
        return dx * dx + dy * dy;
    }
}
