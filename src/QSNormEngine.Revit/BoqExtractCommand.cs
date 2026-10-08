using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QSNormEngine.Core;

namespace QSNormEngine.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class BoqExtractCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;
            var ids = uidoc.Selection.GetElementIds().ToList();
            if (ids.Count == 0)
            {
                TaskDialog.Show("DSSTech", "Hãy chọn cấu kiện hoặc dùng filter để chọn trước khi trích BOQ.");
                return Result.Cancelled;
            }

            if (!File.Exists(PluginPaths.RuntimeData))
            {
                TaskDialog.Show("DSSTech", $"Thiếu runtime database:\n{PluginPaths.RuntimeData}");
                return Result.Failed;
            }

            var (_, _, pipeline) = EngineHost.Create();
            var factory = new RevitElementFactsFactory(doc);
            var facts = ids.Select(id => doc.GetElement(id)).Where(e => e?.Category is not null).Select(factory.Create).ToList();
            var detailRows = pipeline.Extract(facts, BoqGroupingMode.Detail).ToList();
            var inspection = BoqInspectionBuilder.Build(BoqGrouper.Group(detailRows, BoqGroupingMode.Code), "code", facts.Count);

            var window = new BoqResultsWindow(detailRows, inspection, facts.Count, doc, uidoc);
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
}
