using Autodesk.Revit.DB;
using DSSTech.Core.Formwork;

namespace DSSTech.Revit.Formwork;

internal static class ContactResolver
{
    private const double VolumeToleranceFt3 = 1e-9;

    public readonly record struct FaceContact(double AreaM2, FormworkFaceRole Face);

    public static FaceContact ResolvePairDeduction(
        SolidEntry loser,
        SolidEntry winner,
        double epsFt,
        IReadOnlyCollection<FormworkFaceRole> includedFaces)
    {
        // Prefer probing the loser's formwork faces (e.g. slab Bottom vs beam/column).
        var hit = EstimateContactByFaceProbe(loser.Solid, winner.Solid, epsFt, includedFaces);
        if (hit is null || hit.Value.AreaFt2 <= VolumeToleranceFt3)
        {
            var fallback = EstimateContactByFaceProbe(winner.Solid, loser.Solid, epsFt, null);
            if (fallback is null || fallback.Value.AreaFt2 <= VolumeToleranceFt3)
                return new FaceContact(0, FormworkFaceRole.Excluded);

            // Attribute to loser's most likely contact face.
            var face = includedFaces.Contains(FormworkFaceRole.Bottom)
                ? FormworkFaceRole.Bottom
                : includedFaces.Contains(FormworkFaceRole.Side)
                    ? FormworkFaceRole.Side
                    : FormworkFaceRole.Excluded;
            return new FaceContact(FormworkUnits.SquareFeetToM2(fallback.Value.AreaFt2), face);
        }

        return new FaceContact(
            FormworkUnits.SquareFeetToM2(hit.Value.AreaFt2),
            hit.Value.Face);
    }

    /// <summary>
    /// Extrude each included planar face by eps; volume lost after Difference with neighbor ≈ contact area × eps.
    /// </summary>
    internal static (double AreaFt2, FormworkFaceRole Face)? EstimateContactByFaceProbe(
        Solid host,
        Solid neighbor,
        double epsFt,
        IReadOnlyCollection<FormworkFaceRole>? includedFaces)
    {
        double bestFt2 = 0;
        var bestFace = FormworkFaceRole.Excluded;
        foreach (Face face in host.Faces)
        {
            try
            {
                if (face is not PlanarFace planar) continue;
                var role = FaceClassifier.Classify(planar);
                if (includedFaces is not null && !includedFaces.Contains(role))
                    continue;

                var loops = planar.GetEdgesAsCurveLoops();
                if (loops is null || loops.Count == 0) continue;

                var probe = GeometryCreationUtilities.CreateExtrusionGeometry(loops, planar.FaceNormal, epsFt);
                if (probe is null || probe.Volume <= VolumeToleranceFt3) continue;

                var after = SolidProvider.TryBoolean(probe, neighbor, BooleanOperationsType.Difference);
                if (after is null) continue;

                var lost = probe.Volume - after.Volume;
                if (lost <= VolumeToleranceFt3) continue;

                var areaFt2 = lost / epsFt;
                if (areaFt2 > bestFt2)
                {
                    bestFt2 = areaFt2;
                    bestFace = role;
                }
            }
            catch
            {
                // Skip non-extrudable faces.
            }
        }

        return bestFt2 > 0 ? (bestFt2, bestFace) : null;
    }
}

internal static class FormworkUnits
{
    private const double SquareFeetToM2Factor = 0.09290304;
    private const double FeetToMFactor = 0.3048;

    public static double SquareFeetToM2(double ft2) => ft2 * SquareFeetToM2Factor;

    public static double FeetToM(double ft) => ft * FeetToMFactor;

    public static double MmToFeet(double mm) => mm / 304.8;

    /// <summary>Formwork areas written/displayed in m² to 3 decimal places.</summary>
    public static double RoundM2(double m2) =>
        Math.Round(m2, 3, MidpointRounding.AwayFromZero);
}
