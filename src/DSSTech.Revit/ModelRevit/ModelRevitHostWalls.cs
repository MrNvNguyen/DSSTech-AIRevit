using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Straight walls standing on a level, as plan axes in internal-origin millimetres (the frame the CAD link geometry
/// is read in). Doors and windows of that level are hosted on these.
/// </summary>
internal static class ModelRevitHostWalls
{
    /// <summary>A wall counts for the level when it rises at least this far above it (door head height).</summary>
    private const double MinRiseMm = 1000;

    public static List<HostWallAxis> At(Document doc, string levelName)
    {
        var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.Ordinal));
        var result = new List<HostWallAxis>();
        if (level is null)
            return result;
        var floor = level.Elevation;
        var tol = UnitConv.MmToInternal(10);
        var rise = UnitConv.MmToInternal(MinRiseMm);
        foreach (var wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
        {
            if (wall.Location is not LocationCurve { Curve: Line line } || wall.Width <= 0)
                continue;
            var box = wall.get_BoundingBox(null);
            var onLevel = wall.LevelId == level.Id ||
                          (box is not null && box.Min.Z <= floor + tol && box.Max.Z >= floor + rise);
            if (!onLevel)
                continue;
            var a = line.GetEndPoint(0);
            var b = line.GetEndPoint(1);
            result.Add(new HostWallAxis(
                wall.Id.Value.ToString(),
                UnitConv.ToMm(a.X), UnitConv.ToMm(a.Y), UnitConv.ToMm(b.X), UnitConv.ToMm(b.Y),
                UnitConv.ToMm(wall.Width)));
        }

        DssTechLog.Write($"host walls level={levelName} count={result.Count}");
        return result;
    }
}
