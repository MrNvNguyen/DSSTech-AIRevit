using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSSTech.Core;

namespace DSSTech.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class NormSelectorCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // Manual: ResultsWindow may write QS_* after explicit user confirm (fill / write-back).
        // Analysis itself does not auto-write.
        try
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;
            var ids = uidoc.Selection.GetElementIds().ToList();
            if (ids.Count == 0)
            {
                TaskDialog.Show("DSSTech", "Hãy chọn một hoặc nhiều đối tượng Revit trước khi chạy Norm Selector.");
                return Result.Cancelled;
            }

            if (!File.Exists(PluginPaths.RuntimeData))
            {
                TaskDialog.Show("DSSTech", $"Thiếu runtime database:\n{PluginPaths.RuntimeData}");
                return Result.Failed;
            }

            var data = RuntimeDataLoader.Load(PluginPaths.RuntimeData);
            var config = RuntimeDataLoader.LoadConfig(PluginPaths.Config);
            var engine = new NormSelectionEngine(data, config);
            var factory = new RevitElementFactsFactory(doc);
            var results = new List<NormSelectionResult>();
            foreach (var id in ids)
            {
                var element = doc.GetElement(id);
                if (element?.Category is null) continue;
                var facts = factory.Create(element);
                results.Add(engine.Select(facts));
            }

            var window = new ResultsWindow(results, doc);
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
