using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;

namespace DSSTech.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class CadArchCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CadDisciplineRunner.Run(commandData, "cad-layer-map.arch.json", "CAD Kiến trúc", ArchIntentEngine.Plan);
}

[Transaction(TransactionMode.Manual)]
public sealed class CadMepfCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CadDisciplineRunner.Run(commandData, "cad-layer-map.mepf.json", "CAD MEPF", MepfIntentEngine.Plan);
}

[Transaction(TransactionMode.Manual)]
public sealed class CadLinkHelpCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show("DSSTech — Link mặt bằng",
            "1. Insert → Link CAD vào Level tầng cần dựng.\n" +
            "2. Move / Rotate link cho khớp tọa độ và góc model (add-in không tự chỉnh link).\n" +
            "3. Mở mặt bằng tầng đó → Dựng mô hình (pick layer + Text/Dimension).\n\n" +
            "Cần dựng hàng loạt có xem trước: dùng nút Dựng theo lô (Kiểm tra CAD → Kết cấu / Kiến trúc / MEPF).");
        return Result.Succeeded;
    }
}

