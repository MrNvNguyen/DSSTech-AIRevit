namespace DSSTech.Core.Formwork;

/// <summary>
/// Pure formula helpers for N6 Layer A (no Revit geometry).
/// Matches docs/FORMWORK_RULES.md — not a substitute for in-Revit takeoff.
/// </summary>
public static class FormworkFormula
{
    /// <summary>Column / structural wall side area before contact deducts: peri × H.</summary>
    public static double ColumnOrWallSide(double sectionB, double sectionH, double heightM)
    {
        if (sectionB <= 0 || sectionH <= 0 || heightM <= 0) return 0;
        var peri = 2.0 * (sectionB + sectionH);
        return peri * heightM;
    }

    /// <summary>Beam bottom before column/wall deducts: b × L.</summary>
    public static double BeamBottom(double widthB, double lengthL) =>
        widthB > 0 && lengthL > 0 ? widthB * lengthL : 0;

    /// <summary>Beam side before deducts: 2 × h × L (stem faces only, no ends).</summary>
    public static double BeamSideStem(double heightH, double lengthL) =>
        heightH > 0 && lengthL > 0 ? 2.0 * heightH * lengthL : 0;

    /// <summary>Slab bottom gross (openings / contacts applied by caller).</summary>
    public static double SlabBottomGross(double areaM2) => areaM2 > 0 ? areaM2 : 0;

    /// <summary>Slab edge formwork: perimeter × thickness.</summary>
    public static double SlabSide(double perimeterM, double thicknessM) =>
        perimeterM > 0 && thicknessM > 0 ? perimeterM * thicknessM : 0;
}
