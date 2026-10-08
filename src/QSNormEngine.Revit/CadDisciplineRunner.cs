using System.IO;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpContracts;
using Dss.McpHost.Create;
using QSNormEngine.Revit.Cad;

namespace QSNormEngine.Revit;

internal static class CadDisciplineRunner
{
    public static Result Run(
        ExternalCommandData commandData,
        string mapFileName,
        string title,
        Func<CadGeometry, LayerMap, string, IReadOnlyList<CreateOperation>> plan)
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
                TaskDialog.Show("DSSTech", "Hãy chọn một CAD link (ImportInstance).");
                return Result.Failed;
            }

            var geom = ImportInstanceGeometryReader.Read(import, uidoc.ActiveView);
            var mapPath = Path.Combine(PluginPaths.Root, "Config", mapFileName);
            if (!File.Exists(mapPath))
            {
                TaskDialog.Show("DSSTech", $"Thiếu map: {mapPath}");
                return Result.Failed;
            }

            var map = LayerMap.Load(mapPath);
            var report = PreflightEngine.Run(geom, new[] { map }, null);
            if (!report.Ok)
            {
                var fail = string.Join("\n", report.Failures.Select(f => $"[{f.Code}] {f.MessageVi}"));
                TaskDialog.Show("DSSTech — Preflight FAIL", fail + "\n\nKhông tạo đối tượng.");
                return Result.Failed;
            }

            var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();
            var levelName = level?.Name ?? "Level 1";
            var ops = plan(geom, map, levelName).ToList();
            if (ops.Count == 0)
            {
                TaskDialog.Show("DSSTech", "Không suy ra thao tác từ CAD (kiểm tra layer map).");
                return Result.Failed;
            }

            var preview = new StringBuilder();
            preview.AppendLine($"{title} — {ops.Count} thao tác (Level={levelName})");
            foreach (var g in ops.GroupBy(o => o.Category))
                preview.AppendLine($"  {g.Key}: {g.Count()}");
            preview.AppendLine("Preview → Confirm. Undo một lần hoàn tác cả batch.");

            var td = new TaskDialog("DSSTech — " + title)
            {
                MainInstruction = "Confirm create_batch?",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (td.Show() != TaskDialogResult.Yes)
                return Result.Cancelled;

            var batchArgs = JsonSerializer.SerializeToElement(new { dryRun = false, operations = ops });
            var result = CreateBatchTool.Execute(commandData.Application, batchArgs, McpPermission.ReadCreate);
            TaskDialog.Show("DSSTech", result.Ok ? result.Data.GetRawText() : $"{result.ErrorCode}: {result.Message}");
            return result.Ok ? Result.Succeeded : Result.Failed;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("DSSTech", ex.Message);
            return Result.Failed;
        }
    }
}
