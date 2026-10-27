using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSSTech.Revit.License;

namespace DSSTech.Revit;

[Transaction(TransactionMode.ReadOnly)]
public sealed class AccountCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var dlg = new AccountWindow();
        dlg.SetOwner(commandData.Application.MainWindowHandle);
        dlg.ShowDialog();
        return Result.Succeeded;
    }
}
