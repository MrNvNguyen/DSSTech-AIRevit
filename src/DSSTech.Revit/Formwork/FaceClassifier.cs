using Autodesk.Revit.DB;
using DSSTech.Core.Formwork;

namespace DSSTech.Revit.Formwork;

internal static class FaceClassifier
{
    public static FormworkFaceRole Classify(PlanarFace face, double verticalToleranceDeg = 15.0)
    {
        var threshold = Math.Cos(verticalToleranceDeg * Math.PI / 180.0);
        var z = face.FaceNormal.Z;
        if (z >= threshold)
            return FormworkFaceRole.Top;
        if (z <= -threshold)
            return FormworkFaceRole.Bottom;
        return FormworkFaceRole.Side;
    }

    public static FormworkFaceRole Classify(Face face, double verticalToleranceDeg = 15.0) =>
        face is PlanarFace planar ? Classify(planar, verticalToleranceDeg) : FormworkFaceRole.Excluded;
}
