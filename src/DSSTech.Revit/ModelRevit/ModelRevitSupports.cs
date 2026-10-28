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

    public static List<SectionFootprint> Collect(Document doc, Level level, double offsetMm, out int columns, out int walls)
    {
        columns = 0;
        walls = 0;
        var result = new List<SectionFootprint>();
        var zone = level.Elevation + UnitConv.MmToInternal(offsetMm - SupportZoneMm);

        foreach (var column in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType()
                     .OfType<FamilyInstance>())
        {
            if (!InZone(column, zone))
                continue;
            var axis = column.GetTransform().BasisX;
            if (TryFootprint(column, axis.X, axis.Y, out var footprint))
            {
                result.Add(footprint);
                columns++;
            }
        }

        foreach (var wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
        {
            if (!IsStructural(wall) || wall.Location is not LocationCurve { Curve: Line line } || !InZone(wall, zone))
                continue;
            var dir = line.Direction;
            if (TryFootprint(wall, dir.X, dir.Y, out var footprint))
            {
                result.Add(footprint);
                walls++;
            }
        }

        return result;
    }

    private static bool InZone(Element element, double z)
    {
        var box = element.get_BoundingBox(null);
        return box is not null && box.Min.Z <= z && box.Max.Z >= z;
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
