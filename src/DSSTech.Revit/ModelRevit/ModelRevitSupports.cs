using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Structural columns and structural walls already in the model that carry beams at a level, as plan rectangles
/// in internal-origin millimetres (the frame CAD link geometry is read in, so no CAD↔Revit inversion is needed).
/// </summary>
internal static class ModelRevitSupports
{
    /// <summary>An element supports the beam when it occupies the zone this far under the beam top.</summary>
    private const double SupportZoneMm = 300;
    /// <summary>Also accept columns whose top sits this far above the beam top (slab thickness / tolerance).</summary>
    private const double SupportZoneAboveMm = 1500;

    public static List<SectionFootprint> Collect(Document doc, Level level, double offsetMm, out int columns, out int walls)
    {
        var columnCount = 0;
        var wallCount = 0;
        var result = new List<SectionFootprint>();
        var beamTop = level.Elevation + UnitConv.MmToInternal(offsetMm);
        var zoneLo = beamTop - UnitConv.MmToInternal(SupportZoneMm);
        var zoneHi = beamTop + UnitConv.MmToInternal(SupportZoneAboveMm);
        var seen = new HashSet<ElementId>();

        void TryAddColumn(FamilyInstance column)
        {
            if (!seen.Add(column.Id))
                return;
            if (!ColumnSupportsLevel(column, level, zoneLo, zoneHi))
                return;
            var axis = column.GetTransform().BasisX;
            if (!TryFootprint(column, axis.X, axis.Y, out var footprint))
                return;
            result.Add(footprint);
            columnCount++;
        }

        foreach (var column in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType()
                     .OfType<FamilyInstance>())
            TryAddColumn(column);

        // Architectural columns often carry beams when the model has no Structural Column category yet.
        foreach (var column in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Columns)
                     .WhereElementIsNotElementType()
                     .OfType<FamilyInstance>())
            TryAddColumn(column);

        foreach (var wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
        {
            if (!IsStructural(wall) || wall.Location is not LocationCurve { Curve: Line line } ||
                !InZone(wall, zoneLo, zoneHi))
                continue;
            var dir = line.Direction;
            if (TryFootprint(wall, dir.X, dir.Y, out var footprint))
            {
                result.Add(footprint);
                wallCount++;
            }
        }

        columns = columnCount;
        walls = wallCount;
        return result;
    }

    /// <summary>
    /// Column supports the beam level when its bbox meets the Z zone, or when Base/Top Level
    /// parameters straddle the beam level (bbox can miss when the column sits on a linked level).
    /// </summary>
    private static bool ColumnSupportsLevel(FamilyInstance column, Level level, double zoneLo, double zoneHi)
    {
        if (InZone(column, zoneLo, zoneHi))
            return true;

        var baseId = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId()
                     ?? column.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_PARAM)?.AsElementId();
        var topId = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId()
                    ?? column.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_PARAM)?.AsElementId();
        if (baseId is null || topId is null)
            return false;
        if (baseId == level.Id || topId == level.Id)
            return true;

        var doc = column.Document;
        var baseLevel = baseId != ElementId.InvalidElementId ? doc.GetElement(baseId) as Level : null;
        var topLevel = topId != ElementId.InvalidElementId ? doc.GetElement(topId) as Level : null;
        if (baseLevel is null || topLevel is null)
            return false;
        var lo = Math.Min(baseLevel.Elevation, topLevel.Elevation);
        var hi = Math.Max(baseLevel.Elevation, topLevel.Elevation);
        return lo <= level.Elevation + 1e-6 && hi >= level.Elevation - 1e-6;
    }

    private static bool InZone(Element element, double zLo, double zHi)
    {
        var box = element.get_BoundingBox(null);
        return box is not null && box.Min.Z <= zHi && box.Max.Z >= zLo;
    }

    private static bool IsStructural(Wall wall)
    {
        if (wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() == 1)
            return true;
        return string.Equals(wall.WallType?.Function.ToString(), "CoreShaft", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rectangle aligned to (ux,uy) enclosing the element's solid edges in plan.</summary>
    private static bool TryFootprint(Element element, double ux, double uy, out SectionFootprint footprint)
    {
        footprint = default;
        var len = Math.Sqrt(ux * ux + uy * uy);
        if (len < 1e-9)
        {
            ux = 1;
            uy = 0;
        }
        else
        {
            ux /= len;
            uy /= len;
        }

        var minA = double.MaxValue;
        var maxA = double.MinValue;
        var minB = double.MaxValue;
        var maxB = double.MinValue;
        void Add(XYZ p)
        {
            var x = UnitConv.ToMm(p.X);
            var y = UnitConv.ToMm(p.Y);
            var a = x * ux + y * uy;
            var b = -x * uy + y * ux;
            minA = Math.Min(minA, a);
            maxA = Math.Max(maxA, a);
            minB = Math.Min(minB, b);
            maxB = Math.Max(maxB, b);
        }

        var geometry = element.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
        if (geometry is not null)
            Walk(geometry, Add);
        if (minA > maxA)
        {
            var box = element.get_BoundingBox(null);
            if (box is null)
                return false;
            Add(box.Min);
            Add(box.Max);
            Add(new XYZ(box.Min.X, box.Max.Y, 0));
            Add(new XYZ(box.Max.X, box.Min.Y, 0));
        }

        if (maxA - minA < 1 || maxB - minB < 1)
            return false;
        var ca = (minA + maxA) / 2;
        var cb = (minB + maxB) / 2;
        footprint = new SectionFootprint(
            ca * ux - cb * uy, ca * uy + cb * ux, maxA - minA, maxB - minB, Math.Atan2(uy, ux) * 180.0 / Math.PI);
        return true;
    }

    private static void Walk(GeometryElement geometry, Action<XYZ> add)
    {
        foreach (var obj in geometry)
        {
            switch (obj)
            {
                case Solid solid when solid.Volume > 1e-9:
                    foreach (Edge edge in solid.Edges)
                    {
                        foreach (var p in edge.Tessellate())
                            add(p);
                    }

                    break;
                case GeometryInstance instance:
                    Walk(instance.GetInstanceGeometry(), add);
                    break;
            }
        }
    }
}
