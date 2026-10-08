using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit.Formwork;

/// <summary>
/// Unified formwork — OOG FormworkUtils + FaceUtil
/// (<c>docs/formwork_logic_code.html</c>) for Foundation / Column / Wall / Beam / Slab:
///   Gross = Σ included planar face areas
///   Deduct = Σ coplanar face-overlap with solid-intersecting neighbors
///   (per host face capped at face.Area — avoids double-count at crossings)
/// Face pairing (physical formwork contact):
///   Side ↔ Side · Bottom ↔ Top
///   (skip Bottom↔Bottom beam/slab — false soffit overlaps at same elevation)
/// </summary>
internal static class FaceOverlapFormwork
{
    private static readonly BuiltInCategory[] NeighborCategories =
    [
        BuiltInCategory.OST_StructuralFoundation,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Floors
    ];

    public readonly record struct Result(
        double SideM2,
        double BottomM2,
        double GrossSideM2,
        double GrossBottomM2,
        double DeductSideM2,
        double DeductBottomM2,
        double SubFoundM2,
        double SubBeamM2,
        double SubSlabM2,
        double SubColM2,
        double SubWallM2,
        int NeighborHits,
        int FailedOverlaps,
        string Reason);

    public sealed class SolidCache
    {
        private readonly Dictionary<long, Solid?> _byId = new();

        public Solid? Get(Element el)
        {
            var id = el.Id.Value;
            if (_byId.TryGetValue(id, out var cached))
                return cached;
            var solid = FaceOverlapUtil.TryGetMergedSolid(el);
            _byId[id] = solid;
            return solid;
        }

        public void Put(ElementId id, Solid solid) => _byId[id.Value] = solid;
    }

    public static Result? TryCompute(
        Document doc,
        SolidEntry entry,
        FormworkRuleSet ruleSet,
        SolidCache cache)
    {
        var included = IncludedFaces(entry);
        if (included.Count == 0)
            return null;

        var hostSolid = cache.Get(entry.Element);
        if (hostSolid is null || hostSolid.Faces.Size == 0)
            return null;

        var vertTol = ruleSet.Settings.VerticalToleranceDeg > 0
            ? ruleSet.Settings.VerticalToleranceDeg
            : 15.0;
        var pad = FormworkUnits.MmToFeet(Math.Max(ruleSet.Settings.ProbeEpsilonMm, 20));

        var hostFaces = new List<(Face Face, FormworkFaceRole Role, double AreaFt2)>();
        double grossSideFt2 = 0;
        double grossBottomFt2 = 0;
        foreach (Face face in hostSolid.Faces)
        {
            if (face is not PlanarFace) continue;
            var role = FaceClassifier.Classify(face, vertTol);
            if (!included.Contains(role)) continue;
            var area = face.Area;
            if (area <= 1e-12) continue;
            hostFaces.Add((face, role, area));
            if (role == FormworkFaceRole.Side)
                grossSideFt2 += area;
            else if (role == FormworkFaceRole.Bottom)
                grossBottomFt2 += area;
        }

        if (hostFaces.Count == 0)
            return null;

        var neighbors = CollectIntersectingNeighbors(doc, entry, hostSolid, pad).ToList();
        var ignore = entry.Rule?.IgnoreContactWith ?? [];

        double deductSideFt2 = 0;
        double deductBottomFt2 = 0;
        double subFound = 0, subBeam = 0, subSlab = 0, subCol = 0, subWall = 0;
        var hits = 0;
        var failed = 0;

        // Per host face: max overlap from any single neighbor face, summed across neighbors
        // then capped at face.Area (OOG subtracts each hit; cap prevents crossing double-count).
        foreach (var (hostFace, hostRole, hostAreaFt2) in hostFaces)
        {
            double faceDeductFt2 = 0;
            foreach (var neighbor in neighbors)
            {
                if (ShouldIgnore(neighbor, ignore))
                    continue;

                var nSolid = cache.Get(neighbor);
                if (nSolid is null) continue;

                double bestVsNeighbor = 0;
                var anyHit = false;
                foreach (Face nf in nSolid.Faces)
                {
                    if (nf is not PlanarFace) continue;
                    var nRole = FaceClassifier.Classify(nf, vertTol);
                    if (!FacesCanContact(hostRole, nRole, entry.Role, NeighborRole(neighbor)))
                        continue;

                    var (overlap, areaFt2, fail) = FaceOverlapUtil.OverlappingResult(hostFace, nf);
                    if (fail)
                    {
                        failed++;
                        continue;
                    }

                    if (!overlap || areaFt2 <= 0) continue;
                    anyHit = true;
                    // One neighbor: take max fragment (avoid multi-face fragmentation on same contact).
                    if (areaFt2 > bestVsNeighbor)
                        bestVsNeighbor = areaFt2;
                }

                if (!anyHit || bestVsNeighbor <= 0) continue;
                faceDeductFt2 += bestVsNeighbor;
                hits++;
                AssignCategory(neighbor, FormworkUnits.SquareFeetToM2(bestVsNeighbor),
                    ref subFound, ref subBeam, ref subSlab, ref subCol, ref subWall);
            }

            faceDeductFt2 = Math.Min(faceDeductFt2, hostAreaFt2);
            if (hostRole == FormworkFaceRole.Bottom)
                deductBottomFt2 += faceDeductFt2;
            else
                deductSideFt2 += faceDeductFt2;
        }

        var grossSide = FormworkUnits.SquareFeetToM2(grossSideFt2);
        var grossBottom = FormworkUnits.SquareFeetToM2(grossBottomFt2);
        var deductSide = Math.Min(FormworkUnits.SquareFeetToM2(deductSideFt2), grossSide);
        var deductBottom = Math.Min(FormworkUnits.SquareFeetToM2(deductBottomFt2), grossBottom);
        var side = Math.Max(0, grossSide - deductSide);
        var bottom = Math.Max(0, grossBottom - deductBottom);

        if (entry.GroundContactDropBottom)
            bottom = 0;

        var kind = entry.Rule?.Kind ?? entry.Role.ToString();
        var reason =
            $"{kind} Side {grossSide:0.000}−{deductSide:0.000}→{side:0.000}" +
            (included.Contains(FormworkFaceRole.Bottom)
                ? $" | Bottom {grossBottom:0.000}−{deductBottom:0.000}→{bottom:0.000}"
                : "") +
            $" | sub F{subFound:0.###}/B{subBeam:0.###}/S{subSlab:0.###}/C{subCol:0.###}/W{subWall:0.###}" +
            $" hits={hits}" +
            (failed > 0 ? $" failOverlap={failed}" : "");

        return new Result(
            FormworkUnits.RoundM2(side),
            FormworkUnits.RoundM2(bottom),
            FormworkUnits.RoundM2(grossSide),
            FormworkUnits.RoundM2(grossBottom),
            FormworkUnits.RoundM2(deductSide),
            FormworkUnits.RoundM2(deductBottom),
            FormworkUnits.RoundM2(subFound),
            FormworkUnits.RoundM2(subBeam),
            FormworkUnits.RoundM2(subSlab),
            FormworkUnits.RoundM2(subCol),
            FormworkUnits.RoundM2(subWall),
            hits,
            failed,
            reason);
    }

    /// <summary>
    /// OOG contact pairs: vertical↔vertical, soffit↔bearing top.
    /// Skip Bottom↔Bottom between beams/floors (same-elevation soffits overlap falsely).
    /// </summary>
    private static bool FacesCanContact(
        FormworkFaceRole hostRole,
        FormworkFaceRole neighborRole,
        FormworkElementRole hostKind,
        FormworkElementRole neighborKind)
    {
        if (hostRole == FormworkFaceRole.Side && neighborRole == FormworkFaceRole.Side)
            return true;

        if (hostRole == FormworkFaceRole.Bottom && neighborRole == FormworkFaceRole.Top)
            return true;

        // Slab/beam soffit may sit on footing top (Top) — already covered.
        // Rare: footing side vs beam bottom — not coplanar typically.
        if (hostRole == FormworkFaceRole.Bottom && neighborRole == FormworkFaceRole.Bottom)
        {
            // Only allow foundation bottoms as "bearing" when normals oppose (handled by plane test);
            // still skip floor↔floor and beam↔beam soffit-on-soffit.
            if (hostKind is FormworkElementRole.Beam or FormworkElementRole.Slab &&
                neighborKind is FormworkElementRole.Beam or FormworkElementRole.Slab)
                return false;
            return neighborKind == FormworkElementRole.Footing;
        }

        return false;
    }

    private static FormworkElementRole NeighborRole(Element el)
    {
        var cat = el.Category?.Id.Value ?? 0;
        if (cat == (int)BuiltInCategory.OST_StructuralColumns) return FormworkElementRole.Column;
        if (cat == (int)BuiltInCategory.OST_Walls) return FormworkElementRole.Wall;
        if (cat == (int)BuiltInCategory.OST_StructuralFraming) return FormworkElementRole.Beam;
        if (cat == (int)BuiltInCategory.OST_Floors) return FormworkElementRole.Slab;
        if (cat == (int)BuiltInCategory.OST_StructuralFoundation) return FormworkElementRole.Footing;
        return FormworkElementRole.Unknown;
    }

    private static IEnumerable<Element> CollectIntersectingNeighbors(
        Document doc,
        SolidEntry entry,
        Solid hostSolid,
        double pad)
    {
        var selfId = entry.ElementId.Value;
        var seen = new HashSet<long> { selfId };
        var bb = entry.BoundingBox;

        // Prefer solid intersection (OOG IntersectingElements). Fallback: bbox.
        List<Element>? solidHits = null;
        try
        {
            var solidFilter = new ElementIntersectsSolidFilter(hostSolid);
            solidHits = [];
            foreach (var cat in NeighborCategories)
            {
                foreach (var el in new FilteredElementCollector(doc)
                             .OfCategory(cat)
                             .WhereElementIsNotElementType()
                             .WherePasses(solidFilter)
                             .ToElements())
                {
                    if (!seen.Add(el.Id.Value)) continue;
                    solidHits.Add(el);
                }
            }
        }
        catch
        {
            solidHits = null;
        }

        if (solidHits is { Count: > 0 })
            return solidHits;

        // Bbox fallback when solid filter unsupported / empty.
        seen = new HashSet<long> { selfId };
        var list = new List<Element>();
        foreach (var cat in NeighborCategories)
        {
            foreach (var el in new FilteredElementCollector(doc)
                         .OfCategory(cat)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                if (!seen.Add(el.Id.Value)) continue;
                var ob = el.get_BoundingBox(null);
                if (ob is null) continue;
                if (!FootprintsOverlap(bb, ob, pad)) continue;
                if (ob.Max.Z < bb.Min.Z - pad || ob.Min.Z > bb.Max.Z + pad)
                    continue;
                list.Add(el);
            }
        }

        return list;
    }

    private static bool ShouldIgnore(Element neighbor, IReadOnlyList<string> ignore)
    {
        if (ignore.Count == 0) return false;
        var cat = neighbor.Category?.Id.Value ?? 0;
        foreach (var token in ignore)
        {
            if (token.Equals("Slab", StringComparison.OrdinalIgnoreCase) &&
                cat == (int)BuiltInCategory.OST_Floors)
                return true;
            if (token.Equals("Beam", StringComparison.OrdinalIgnoreCase) &&
                cat == (int)BuiltInCategory.OST_StructuralFraming)
                return true;
            if (token.Equals("Column", StringComparison.OrdinalIgnoreCase) &&
                cat == (int)BuiltInCategory.OST_StructuralColumns)
                return true;
            if (token.Equals("Wall", StringComparison.OrdinalIgnoreCase) &&
                cat == (int)BuiltInCategory.OST_Walls)
                return true;
            if ((token.Equals("Footing", StringComparison.OrdinalIgnoreCase) ||
                 token.Equals("Foundation", StringComparison.OrdinalIgnoreCase)) &&
                cat == (int)BuiltInCategory.OST_StructuralFoundation)
                return true;
        }

        return false;
    }

    private static void AssignCategory(
        Element e,
        double valueM2,
        ref double found,
        ref double beam,
        ref double slab,
        ref double col,
        ref double wall)
    {
        var cat = e.Category?.Id.Value ?? 0;
        if (cat == (int)BuiltInCategory.OST_StructuralFoundation) found += valueM2;
        else if (cat == (int)BuiltInCategory.OST_StructuralFraming) beam += valueM2;
        else if (cat == (int)BuiltInCategory.OST_Floors) slab += valueM2;
        else if (cat == (int)BuiltInCategory.OST_StructuralColumns) col += valueM2;
        else if (cat == (int)BuiltInCategory.OST_Walls) wall += valueM2;
    }

    private static bool FootprintsOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b, double pad) =>
        a.Min.X - pad <= b.Max.X + pad &&
        a.Max.X + pad >= b.Min.X - pad &&
        a.Min.Y - pad <= b.Max.Y + pad &&
        a.Max.Y + pad >= b.Min.Y - pad;

    private static HashSet<FormworkFaceRole> IncludedFaces(SolidEntry entry)
    {
        if (entry.Rule is not null)
            return FormworkRuleMatcher.ParseFaces(entry.Rule);

        return entry.Role switch
        {
            FormworkElementRole.Column => [FormworkFaceRole.Side],
            FormworkElementRole.Wall => [FormworkFaceRole.Side],
            FormworkElementRole.Beam => [FormworkFaceRole.Side, FormworkFaceRole.Bottom],
            FormworkElementRole.Slab => [FormworkFaceRole.Side, FormworkFaceRole.Bottom],
            FormworkElementRole.Footing => [FormworkFaceRole.Side],
            _ => []
        };
    }
}
