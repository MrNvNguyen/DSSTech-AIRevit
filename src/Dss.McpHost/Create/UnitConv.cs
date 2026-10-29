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

    /// <summary>5 mm stations (xxx0 / xxx5). Float dust within 0.01 mm coalesces onto the station.</summary>
    public const double SnapStepMm = 5.0;

    /// <summary>219.761→220, 49.761→50, 699.99999998→700, 500.007→500.</summary>
    public static double Round5Mm(double mm) =>
        Math.Round(mm / SnapStepMm, MidpointRounding.AwayFromZero) * SnapStepMm;

    public static XYZ XyzFromMm(double[] mm) =>
        new(
            MmToInternal(RoundWholeMm(mm[0])),
            MmToInternal(RoundWholeMm(mm[1])),
            MmToInternal(RoundWholeMm(mm.Length > 2 ? mm[2] : 0)));
}
