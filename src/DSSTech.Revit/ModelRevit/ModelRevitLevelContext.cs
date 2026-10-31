using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;

namespace DSSTech.Revit.ModelRevit;

internal static class ModelRevitLevelContext
{
    private const double ElevationTolFt = 0.01;

    public static bool TryResolve(View view, Document doc, out Level baseLevel, out Level topLevel, out string errorVi)
    {
        baseLevel = null!;
        topLevel = null!;
        errorVi = "";

        if (!IsModelRevitPlanView(view))
        {
            errorVi =
                "Mở mặt bằng tầng (Floor Plan hoặc Structural Plan) rồi gọi lại ModelRevit.\n" +
                "View hiện tại không phải mặt bằng (3D, mặt cắt, elevation, sheet, …).";
            return false;
        }

        var gen = view.GenLevel;
        if (gen is null)
        {
            errorVi = "View mặt bằng không gắn Level. Kiểm tra template dự án.";
            return false;
        }

        baseLevel = gen;
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        // Only columns and walls run up to the level above; beams, floors and the rest stay on the plan's level.
        topLevel = levels.FirstOrDefault(l => l.Elevation > gen.Elevation + ElevationTolFt) ?? gen;
        return true;
    }

    public static bool NeedsTopLevel(ModelRevitPartKind part) =>
        part is ModelRevitPartKind.Column or ModelRevitPartKind.StructuralWall or ModelRevitPartKind.ArchitecturalWall;

    public static string NoUpperLevelMessage(Level level) =>
        $"Level «{level.Name}» của mặt bằng đang mở không có Level kế tiếp phía trên.\n" +
        "Thêm Level tầng trên trong model rồi gọi lại ModelRevit.";

    private static bool IsModelRevitPlanView(View view) =>
        view.ViewType is ViewType.FloorPlan or ViewType.EngineeringPlan;
}
