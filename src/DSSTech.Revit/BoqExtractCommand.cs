using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSSTech.Core;

namespace DSSTech.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class BoqExtractCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;
            if (!TryBuild(uidoc, out var detailRows, out var inspection, out var sourceCount, out var error))
            {
                TaskDialog.Show("DSSTech", error);
                return error.StartsWith("Hãy chọn", StringComparison.Ordinal) ? Result.Cancelled : Result.Failed;
            }

            var window = new BoqResultsWindow(detailRows, inspection, sourceCount, doc, uidoc);
            new System.Windows.Interop.WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.ToString();
            TaskDialog.Show("DSSTech", ex.Message);
            return Result.Failed;
        }
    }

    internal static bool TryBuild(
        UIDocument uidoc,
        out List<BoqLineItem> detailRows,
        out List<BoqInspectionRow> inspection,
        out int sourceCount,
        out string error)
    {
        detailRows = new List<BoqLineItem>();
        inspection = new List<BoqInspectionRow>();
        sourceCount = 0;
        error = "";
        var doc = uidoc.Document;
        var ids = uidoc.Selection.GetElementIds().ToList();
        if (ids.Count == 0)
        {
            error = "Hãy chọn cấu kiện hoặc dùng filter để chọn trước khi trích BOQ.";
            return false;
        }

        if (!File.Exists(PluginPaths.RuntimeData))
        {
            error = $"Thiếu runtime database:\n{PluginPaths.RuntimeData}";
            return false;
        }

        var (_, _, pipeline) = EngineHost.Create();
        var factory = new RevitElementFactsFactory(doc);
        var facts = ids.Select(id => doc.GetElement(id)).Where(e => e?.Category is not null).Select(factory.Create).ToList();
        sourceCount = facts.Count;
        detailRows = pipeline.Extract(facts, BoqGroupingMode.Detail).ToList();
        inspection = BoqInspectionBuilder.Build(BoqGrouper.Group(detailRows, BoqGroupingMode.Code), "code", facts.Count);
        return true;
    }
}
