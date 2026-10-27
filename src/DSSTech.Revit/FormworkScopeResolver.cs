using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DSSTech.Revit;

internal enum FormworkScopeMode
{
    Selection,
    ActiveView,
    WholeModel
}

internal static class FormworkScopeResolver
{
    private const int WholeModelWarnThreshold = 500;

    public static IReadOnlyList<ElementId> Resolve(Document doc, UIDocument uidoc, FormworkScopeMode mode)
    {
        IEnumerable<Element> source = mode switch
        {
            FormworkScopeMode.Selection => uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e is not null)!,
            FormworkScopeMode.ActiveView => new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElements(),
            FormworkScopeMode.WholeModel => new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements(),
            _ => []
        };

        var ids = source
            .Where(IsFormworkCandidate)
            .Select(e => e.Id)
            .Distinct()
            .ToList();

        if (mode == FormworkScopeMode.WholeModel && ids.Count > WholeModelWarnThreshold)
        {
            var td = new TaskDialog("DSSTech — Formwork Takeoff")
            {
                MainInstruction = $"Whole model có {ids.Count} cấu kiện formwork.",
                MainContent = "Tiếp tục có thể chậm. Bạn muốn chạy không?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (td.Show() != TaskDialogResult.Yes)
                return [];
        }

        return ids;
    }

    public static bool IsFormworkCandidate(Element e)
    {
        if (e?.Category is null) return false;
        var cat = e.Category.BuiltInCategory;
        if (cat == BuiltInCategory.OST_Walls)
            return IsStructuralWall(e);

        return cat is BuiltInCategory.OST_StructuralColumns
            or BuiltInCategory.OST_StructuralFraming
            or BuiltInCategory.OST_Floors
            or BuiltInCategory.OST_StructuralFoundation;
    }

    private static bool IsStructuralWall(Element e)
    {
        if (e is not Wall)
            return false;
        try
        {
            var p = e.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
            return p is not null && p.AsInteger() == 1;
        }
        catch
        {
            return false;
        }
    }
}
