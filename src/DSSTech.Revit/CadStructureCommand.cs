using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;

namespace DSSTech.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class CadStructureCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CadDisciplineRunner.Run(commandData, "cad-layer-map.str.json", "CAD Kết cấu", StructureIntentEngine.Plan);
}
