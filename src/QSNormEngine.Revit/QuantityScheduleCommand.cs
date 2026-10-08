using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QSNormEngine.Revit.Quantity;

namespace QSNormEngine.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class QuantityScheduleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc?.Document is null)
            {
                TaskDialog.Show("DSSTech", "Mở một project Revit trước khi tạo schedule.");
                return Result.Cancelled;
            }

            var doc = uidoc.Document;
            QuantityScheduleResult result;
            using (var tx = new Transaction(doc, "DSSTech quantity schedule"))
            {
                tx.Start();
                result = QuantityScheduleBuilder.CreateOrUpdate(doc);
                tx.Commit();
            }

            var schedule = doc.GetElement(result.ScheduleId) as View;
            if (schedule is not null)
                uidoc.ActiveView = schedule;

            var verb = result.Created ? "Đã tạo" : "Đã cập nhật";
            TaskDialog.Show("DSSTech", $"{verb} schedule:\n{result.Name}");
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
