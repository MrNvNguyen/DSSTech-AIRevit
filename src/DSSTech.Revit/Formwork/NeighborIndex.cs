using Autodesk.Revit.DB;

namespace DSSTech.Revit.Formwork;

internal static class NeighborIndex
{
    public static IReadOnlyList<(SolidEntry A, SolidEntry B)> BuildPairs(Document doc, IReadOnlyList<SolidEntry> entries)
    {
        var pairs = new HashSet<(long, long)>();
        var result = new List<(SolidEntry, SolidEntry)>();

        for (var i = 0; i < entries.Count; i++)
        {
            for (var j = i + 1; j < entries.Count; j++)
            {
                var a = entries[i];
                var b = entries[j];
                if (!BoundingBoxesIntersect(a.BoundingBox, b.BoundingBox))
                    continue;

                if (!ArePotentialNeighbors(doc, a, b))
                    continue;

                var key = a.ElementId.Value < b.ElementId.Value
                    ? (a.ElementId.Value, b.ElementId.Value)
                    : (b.ElementId.Value, a.ElementId.Value);
                if (!pairs.Add(key))
                    continue;

                result.Add((a, b));
            }
        }

        return result;
    }

    private static bool ArePotentialNeighbors(Document doc, SolidEntry a, SolidEntry b)
    {
        if (AreJoined(doc, a.Element, b.Element))
            return true;

        if (BoundingBoxesIntersect(a.BoundingBox, b.BoundingBox, 0.05))
            return true;

        return false;
    }

    private static bool AreJoined(Document doc, Element a, Element b)
    {
        try
        {
            if (JoinGeometryUtils.AreElementsJoined(doc, a, b))
                return true;

            var joinedA = JoinGeometryUtils.GetJoinedElements(doc, a);
            if (joinedA.Any(x => x == b.Id))
                return true;

            var joinedB = JoinGeometryUtils.GetJoinedElements(doc, b);
            return joinedB.Any(x => x == a.Id);
        }
        catch
        {
            return false;
        }
    }

    private static bool BoundingBoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b, double padFt = 0)
    {
        return a.Max.X + padFt >= b.Min.X - padFt &&
               a.Min.X - padFt <= b.Max.X + padFt &&
               a.Max.Y + padFt >= b.Min.Y - padFt &&
               a.Min.Y - padFt <= b.Max.Y + padFt &&
               a.Max.Z + padFt >= b.Min.Z - padFt &&
               a.Min.Z - padFt <= b.Max.Z + padFt;
    }
}
