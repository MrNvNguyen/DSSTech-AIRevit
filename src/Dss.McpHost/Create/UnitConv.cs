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

    /// <summary>Whole millimetres only — Revit then shows xxxx.000000000, not float noise.</summary>
    public static double RoundWholeMm(double mm) =>
        Math.Round(mm, MidpointRounding.AwayFromZero);

    public static XYZ XyzFromMm(double[] mm) =>
        new(
            MmToInternal(RoundWholeMm(mm[0])),
            MmToInternal(RoundWholeMm(mm[1])),
            MmToInternal(RoundWholeMm(mm.Length > 2 ? mm[2] : 0)));
}
