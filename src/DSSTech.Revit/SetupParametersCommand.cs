using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DSSTech.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class SetupParametersCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            var definitionPath = Path.Combine(PluginPaths.Root, "Config", "QS_SharedParameters.txt");
            if (!File.Exists(definitionPath))
            {
                TaskDialog.Show("DSSTech", $"Không tìm thấy file Shared Parameter:\n{definitionPath}");
                return Result.Failed;
            }

            var result = SharedParameterSetup.BindFromFile(doc, definitionPath);
            var failed = result.StartsWith("Không", StringComparison.OrdinalIgnoreCase) ||
                         result.StartsWith("Thiếu", StringComparison.OrdinalIgnoreCase) ||
                         result.Contains("readParamDatabase", StringComparison.OrdinalIgnoreCase);
            TaskDialog.Show("DSSTech", result);
            return failed ? Result.Failed : Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.ToString();
            TaskDialog.Show("DSSTech", ex.Message);
            return Result.Failed;
        }
    }
}
