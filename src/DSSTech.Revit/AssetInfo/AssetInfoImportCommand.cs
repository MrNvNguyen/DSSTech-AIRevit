using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.AssetInfo.Core;
using DSSTech.Revit.License;

namespace DSSTech.Revit.AssetInfo;

/// <summary>
/// Ribbon: DSSTech › Tài sản · Assets › "Tài sản · Asset Data" (registered in App.cs with the <c>Large(...)</c> helper,
/// so it gets the bilingual label, F1 help anchor "tai-san" and the <see cref="LicensedAvailability"/> gate).
/// Manual transaction mode because the window writes TS_* — but only after the user's explicit confirm.
/// Opening the command never writes anything.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class AssetInfoImportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            // The ribbon is already gated; this covers launches from elsewhere (Control Center, journal).
            if (!LicenseService.Require())
                return Result.Cancelled;

            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc?.Document is null)
            {
                TaskDialog.Show("DSSTech", "Hãy mở một mô hình trước.");
                return Result.Cancelled;
            }
            if (uidoc.Document.IsFamilyDocument)
            {
                TaskDialog.Show("DSSTech", "Lệnh dùng cho mô hình dự án, không dùng trong Family Editor.");
                return Result.Cancelled;
            }

            SpecRuleSet rules;
            try
            {
                rules = SpecRuleSet.Load(AssetInfoPaths.Rules);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("DSSTech", $"Không đọc được quy tắc tách thông số:\n{AssetInfoPaths.Rules}\n\n{ex.Message}");
                return Result.Failed;
            }

            var window = new AssetInfoWindow(uidoc, rules);
            _ = new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.ToString();
            DssTechLog.Write($"asset info: {ex}");
            TaskDialog.Show("DSSTech", ex.Message);
            return Result.Failed;
        }
    }
}
