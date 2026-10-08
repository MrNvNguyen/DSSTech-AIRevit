using Autodesk.Revit.DB;

namespace Dss.McpHost.Create;

public static class UnitConv
{
    public static double MmToInternal(double mm) =>
        UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

    public static double ToMm(double internalFeet) =>
        UnitUtils.ConvertFromInternalUnits(internalFeet, UnitTypeId.Millimeters);

    public static double ScaleToMm(double value, string? units) =>
        string.Equals(units, "m", StringComparison.OrdinalIgnoreCase) ? value * 1000.0 : value;

    public static XYZ XyzFromMm(double[] mm) =>
        new(MmToInternal(mm[0]), MmToInternal(mm[1]), MmToInternal(mm.Length > 2 ? mm[2] : 0));
}
