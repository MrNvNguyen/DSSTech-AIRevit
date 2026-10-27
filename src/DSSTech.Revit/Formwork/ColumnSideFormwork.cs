using Autodesk.Revit.DB;
using DSSTech.Core.Formwork;

namespace DSSTech.Revit.Formwork;

/// <summary>
/// Vertical member side formwork (column or structural wall), join-independent.
/// Measurement adapted from OOG FormworkUtils / FaceUtil:
///   Side = Σ vertical solid face areas
///        − Σ coplanar face-overlap with neighbors (Found / Beam / Wall / Col)
///        − peri × h_slab at column head only (slab has no vertical coplanar face)
/// Beams: only slab-supporting (top near slab soffit). Mid-storey dầm giằng not deducted.
/// Free-top necks: no face overlap → no false beam/slab deduct.
/// </summary>
internal static class ColumnSideFormwork
{
    private const double MinM = 1e-6;
    private const double SlabSupportGapMm = 250;

    public readonly record struct Result(
        double SideM2,
        double GrossM2,
        double DeductM2,
        double DeductBeamM2,
        double DeductSlabM2,
        double HeightM,
        double PerimeterM,
        int BeamCount,
        int BeamFaceCount,
        int SlabCount);

    public static Result? TryCompute(Document doc, Element column, FormworkRuleSet ruleSet)
    {
        var bb = column.get_BoundingBox(null);
        if (bb is null) return null;

        var solid = FaceOverlapUtil.TryGetMergedSolid(column);
        if (solid is null) return null;

        var vertTol = ruleSet.Settings.VerticalToleranceDeg > 0
            ? ruleSet.Settings.VerticalToleranceDeg
            : 15.0;
        var sideFaces = FaceOverlapUtil.SideFaces(solid, vertTol);
        if (sideFaces.Count == 0) return null;

        var grossFt2 = FaceOverlapUtil.SumFaceAreasFt2(sideFaces);
        if (grossFt2 <= MinM) return null;

        var colLoFt = bb.Min.Z;
        var colHiFt = bb.Max.Z;
        // Never inflate past modelled solid (free-top necks).
        colHiFt = Math.Min(colHiFt, bb.Max.Z);
        colLoFt = Math.Max(colLoFt, bb.Min.Z);
        var heightM = FormworkUnits.FeetToM(colHiFt - colLoFt);
        if (heightM < MinM) return null;

        var (colXm, colYm) = ResolveSectionM(column, bb);
        var peri = 2.0 * (colXm + colYm);
        var pad = FormworkUnits.MmToFeet(Math.Max(ruleSet.Settings.ProbeEpsilonMm, 20));

        // Slab soffits that embed the column head (for beam filter + slab band deduct).
        var slabSoffitsFt = new List<double>();
        var slabBands = new List<(double Lo, double Hi)>();
        CollectHeadSlabEmbeds(doc, bb, colLoFt, colHiFt, pad, slabSoffitsFt, slabBands);

        double deductFoundFt2 = 0;
        double deductBeamFt2 = 0;
        double deductWallFt2 = 0;
        double deductColFt2 = 0;
        var beamCount = 0;
        var beamFaceHits = 0;

        foreach (var neighbor in CollectNeighbors(doc, column, bb, pad))
        {
            var cat = (BuiltInCategory)(neighbor.Category?.Id.Value ?? 0);
            if (!ShouldDeductNeighbor(cat, neighbor, bb, slabSoffitsFt, pad))
                continue;

            var nSolid = FaceOverlapUtil.TryGetMergedSolid(neighbor);
            if (nSolid is null) continue;

            double overlapFt2 = 0;
            var faceHits = 0;
            foreach (var hostFace in sideFaces)
            {
                foreach (Face nf in nSolid.Faces)
                {
                    var (hit, area, failed) = FaceOverlapUtil.OverlappingResult(hostFace, nf);
                    if (failed || !hit || area <= 0) continue;
                    overlapFt2 += area;
                    faceHits++;
                }
            }

            if (overlapFt2 <= 0) continue;

            switch (cat)
            {
                case BuiltInCategory.OST_StructuralFoundation:
                    deductFoundFt2 += overlapFt2;
                    break;
                case BuiltInCategory.OST_StructuralFraming:
                    deductBeamFt2 += overlapFt2;
                    beamCount++;
                    beamFaceHits += faceHits;
                    break;
                case BuiltInCategory.OST_Walls:
                    deductWallFt2 += overlapFt2;
                    break;
                case BuiltInCategory.OST_StructuralColumns:
                    deductColFt2 += overlapFt2;
                    break;
            }
        }

        var deductSlabM2 = peri > MinM
            ? peri * UnionBandHeightM(slabBands)
            : 0;
        var deductBeamM2 = FormworkUnits.SquareFeetToM2(
            deductBeamFt2 + deductFoundFt2 + deductWallFt2 + deductColFt2);
        var grossM2 = FormworkUnits.SquareFeetToM2(grossFt2);
        var deduct = Math.Min(deductBeamM2 + deductSlabM2, grossM2);
        var side = Math.Max(0, grossM2 - deduct);

        return new Result(
            FormworkUnits.RoundM2(side),
            FormworkUnits.RoundM2(grossM2),
            FormworkUnits.RoundM2(deduct),
            FormworkUnits.RoundM2(deductBeamM2),
            FormworkUnits.RoundM2(deductSlabM2),
            FormworkUnits.RoundM2(heightM),
            FormworkUnits.RoundM2(peri),
            beamCount,
            beamFaceHits,
            slabBands.Count);
    }

    private static void CollectHeadSlabEmbeds(
        Document doc,
        BoundingBoxXYZ colBb,
        double colLoFt,
        double colHiFt,
        double pad,
        List<double> soffitsOut,
        List<(double Lo, double Hi)> bandsOut)
    {
        var candidates = new List<(double SoffitFt, double ThickFt)>();
        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Floors)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            var slabBb = el.get_BoundingBox(null);
            if (slabBb is null) continue;
            if (!FootprintsOverlap(colBb, slabBb, pad)) continue;

            var thickFt = ResolveSlabThicknessFt(el, slabBb);
            if (thickFt < pad) continue;
            var soffitFt = ResolveSlabSoffitFt(el, slabBb, thickFt, pad);
            var slabTopFt = soffitFt + thickFt;

            // Head embed only — solid top inside slab thickness.
            if (colHiFt < soffitFt - pad || colHiFt > slabTopFt + pad)
                continue;
            var zLo = Math.Max(colLoFt, soffitFt);
            var zHi = Math.Min(colHiFt, slabTopFt);
            if (zHi - zLo <= pad * 0.5) continue;

            candidates.Add((soffitFt, thickFt));
        }

        var clusterTol = FormworkUnits.MmToFeet(100);
        var ordered = candidates.OrderBy(c => c.SoffitFt).ToList();
        var i = 0;
        while (i < ordered.Count)
        {
            var soffit = ordered[i].SoffitFt;
            var thick = ordered[i].ThickFt;
            var j = i + 1;
            while (j < ordered.Count && ordered[j].SoffitFt - soffit <= clusterTol)
            {
                thick = Math.Max(thick, ordered[j].ThickFt);
                j++;
            }

            soffitsOut.Add(soffit);
            var zLo = Math.Max(colLoFt, soffit);
            var zHi = Math.Min(colHiFt, soffit + thick);
            if (zHi - zLo > 1e-9)
                bandsOut.Add((zLo, zHi));
            i = j;
        }
    }

    private static bool ShouldDeductNeighbor(
        BuiltInCategory cat,
        Element neighbor,
        BoundingBoxXYZ colBb,
        IReadOnlyList<double> slabSoffitsFt,
        double pad)
    {
        return cat switch
        {
            BuiltInCategory.OST_StructuralFoundation => true,
            BuiltInCategory.OST_Walls => true,
            BuiltInCategory.OST_StructuralColumns => true,
            BuiltInCategory.OST_StructuralFraming =>
                IsSlabSupportingBeam(neighbor, slabSoffitsFt, pad),
            _ => false
        };
    }

    /// <summary>
    /// Floor beam under a slab soffit that embeds this column. Grade / mid-storey
    /// linking beams return false — those faces still need formwork.
    /// </summary>
    private static bool IsSlabSupportingBeam(
        Element beam,
        IReadOnlyList<double> slabSoffitsFt,
        double pad)
    {
        if (slabSoffitsFt.Count == 0) return false;
        var beamBb = beam.get_BoundingBox(null);
        if (beamBb is null) return false;

        var beamTopFt = beamBb.Max.Z;
        var beamLoFt = beamBb.Min.Z;
        var gapFt = FormworkUnits.MmToFeet(SlabSupportGapMm);

        if (slabSoffitsFt.Any(z => z >= beamLoFt - pad && z <= beamTopFt + pad))
            return true;
        return slabSoffitsFt.Any(z => z > beamTopFt - pad && z <= beamTopFt + gapFt);
    }

    private static IEnumerable<Element> CollectNeighbors(
        Document doc,
        Element self,
        BoundingBoxXYZ bb,
        double pad)
    {
        var cats = new[]
        {
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_StructuralColumns
        };

        var seen = new HashSet<long> { self.Id.Value };
        foreach (var cat in cats)
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
                // Z overlap with solid.
                if (ob.Max.Z < bb.Min.Z - pad || ob.Min.Z > bb.Max.Z + pad)
                    continue;
                yield return el;
            }
        }
    }

    private static double ResolveSlabSoffitFt(
        Element floor,
        BoundingBoxXYZ bb,
        double thickFt,
        double pad)
    {
        try
        {
            var elev = floor.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM)
                       ?? floor.LookupParameter("Elevation at Bottom");
            if (elev is not null && elev.HasValue)
            {
                var z = elev.AsDouble();
                if (z >= bb.Min.Z - pad && z <= bb.Max.Z + pad)
                    return z;
            }
        }
        catch
        {
            // fall through
        }

        var span = bb.Max.Z - bb.Min.Z;
        if (span > thickFt * 2.5 + pad)
            return bb.Max.Z - thickFt;
        return bb.Min.Z;
    }

    private static double ResolveSlabThicknessFt(Element floor, BoundingBoxXYZ bb)
    {
        const double maxThickMm = 400;
        try
        {
            var p = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
            if (p is not null && p.HasValue && p.AsDouble() > 1e-9)
                return Math.Min(p.AsDouble(), FormworkUnits.MmToFeet(maxThickMm));

            if (floor is Floor f)
            {
                var type = f.Document.GetElement(f.GetTypeId()) as FloorType;
                var w = type?.GetCompoundStructure()?.GetWidth() ?? 0;
                if (w > 1e-9)
                    return Math.Min(w, FormworkUnits.MmToFeet(maxThickMm));
            }
        }
        catch
        {
            // fall through
        }

        var span = bb.Max.Z - bb.Min.Z;
        var raw = span > 1e-9 && span <= FormworkUnits.MmToFeet(500)
            ? span
            : FormworkUnits.MmToFeet(150);
        return Math.Min(raw, FormworkUnits.MmToFeet(maxThickMm));
    }

    private static double UnionBandHeightM(List<(double Lo, double Hi)> bands)
    {
        if (bands.Count == 0) return 0;
        var ordered = bands.OrderBy(b => b.Lo).ToList();
        double lo = ordered[0].Lo;
        double hi = ordered[0].Hi;
        double sumFt = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            var (a, b) = ordered[i];
            if (a <= hi)
            {
                hi = Math.Max(hi, b);
                continue;
            }

            sumFt += Math.Max(0, hi - lo);
            lo = a;
            hi = b;
        }

        sumFt += Math.Max(0, hi - lo);
        return FormworkUnits.FeetToM(sumFt);
    }

    private static (double X, double Y) ResolveSectionM(Element e, BoundingBoxXYZ bb)
    {
        var dx = FormworkUnits.FeetToM(bb.Max.X - bb.Min.X);
        var dy = FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y);

        try
        {
            if (e is Wall wall)
            {
                var thickFt = wall.WallType?.Width ?? 0;
                var lenFt = 0.0;
                if (wall.Location is LocationCurve lc)
                    lenFt = lc.Curve.Length;
                if (thickFt > 1e-9 && lenFt > 1e-9)
                {
                    var thickM = FormworkUnits.FeetToM(thickFt);
                    var lenM = FormworkUnits.FeetToM(lenFt);
                    return dx >= dy ? (lenM, thickM) : (thickM, lenM);
                }
            }
        }
        catch
        {
            // fall through
        }

        return (dx, dy);
    }

    private static bool FootprintsOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b, double pad) =>
        a.Min.X - pad <= b.Max.X + pad &&
        a.Max.X + pad >= b.Min.X - pad &&
        a.Min.Y - pad <= b.Max.Y + pad &&
        a.Max.Y + pad >= b.Min.Y - pad;
}
