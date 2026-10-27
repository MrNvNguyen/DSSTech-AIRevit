using System.IO;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using DSSTech.Core;

namespace DSSTech.Revit;

internal static class BoqExcelExport
{
    public static Result Run(UIApplication app)
    {
        var uidoc = app.ActiveUIDocument;
        if (uidoc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        if (!BoqExtractCommand.TryBuild(uidoc, out var detail, out var inspection, out _, out var error))
        {
            TaskDialog.Show("DSSTech", error);
            return error.StartsWith("Hãy chọn", StringComparison.Ordinal) ? Result.Cancelled : Result.Failed;
        }

        var display = BoqGrouper.Group(detail, BoqGroupingMode.Code);
        var configPath = Path.Combine(PluginPaths.Root, "Config", ElementQuantityExporter.DefaultConfigFileName);
        var config = ElementQuantityExporter.LoadConfig(configPath);
        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = "boq-export.xlsx",
            AddExtension = true
        };
        if (dlg.ShowDialog() != true)
            return Result.Cancelled;

        BoqWorkbookExporter.Export(dlg.FileName, display, inspection, quantityConfig: config);
        TaskDialog.Show("DSSTech", $"Đã xuất Excel:\n{dlg.FileName}");
        return Result.Succeeded;
    }
}
