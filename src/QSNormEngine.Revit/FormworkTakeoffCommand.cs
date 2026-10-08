using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QSNormEngine.Core.Formwork;
using QSNormEngine.Revit.Formwork;

namespace QSNormEngine.Revit;

/// <summary>
/// Product Formwork Takeoff — analyze selection/view/model, show grid, write QS_Formwork* only after Ghi.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class FormworkTakeoffCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uiApp = commandData.Application;
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc is null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uidoc.Document;
            var mode = FormworkScopeDialog.ShowModal(uiApp.MainWindowHandle);
            if (mode is null)
                return Result.Cancelled;

            var filter = FormworkScopeResolver.Resolve(doc, uidoc, mode.Value);
            if (filter.Count == 0)
            {
                TaskDialog.Show(
                    "DSSTech",
                    mode == FormworkScopeMode.Selection
                        ? "Không có cấu kiện formwork trong selection (cột / vách / dầm / sàn)."
                        : "Không có cấu kiện formwork trong phạm vi đã chọn.");
                return Result.Cancelled;
            }

            var rulesPath = PluginPaths.FormworkRules;
            FormworkRuleSet ruleSet;
            string? rulesSha = null;
            if (File.Exists(rulesPath))
            {
                ruleSet = FormworkRuleSetLoader.Load(rulesPath);
                rulesSha = FormworkRuleSetLoader.Sha256Hex(rulesPath);
            }
            else
            {
                ruleSet = FormworkRuleSetLoader.CreateN1ParityDefaults();
                TaskDialog.Show(
                    "DSSTech",
                    $"Không tìm thấy formwork-rules.json:\n{rulesPath}\nDùng N1-parity defaults.");
            }

            var batch = FormworkCalculator.Compute(doc, ruleSet, filter, rulesPath, rulesSha);
            var rows = FormworkTakeoffRowAggregator.FromBatch(batch);
            if (rows.Count == 0)
            {
                TaskDialog.Show("DSSTech", "Formwork calculator không trả về dòng nào.");
                return Result.Cancelled;
            }

            var window = new FormworkResultsWindow(rows, doc, batch);
            new System.Windows.Interop.WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
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
