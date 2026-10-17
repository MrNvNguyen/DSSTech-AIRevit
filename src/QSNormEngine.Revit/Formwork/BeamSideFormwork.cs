using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit.Formwork;

/// <summary>
/// Join-independent beam formwork.
/// Model may be column-to-column or continuous through columns — same rule:
///   Bottom = b×L − Σ (along × b) at every intersecting column/wall
///   Side   = 2×h×L − slab embed (2×along×h_slab) − Σ (2×along×stem) at column/wall
///            + free end faces (b×stem) only when a tip has no column/wall
/// Side contacts are column / wall / slab only — not crossing beams.
/// Do not use Cut Length together with column deducts.
/// </summary>
internal static class BeamSideFormwork
{
    private const double MinM = 1e-6;

    public readonly record struct Result(
        double SideM2,
        double BottomM2,
        double GrossSideM2,
        double GrossBottomM2,
        double DeductSideM2,
        double DeductBottomM2,
        double LengthM,
        double WidthM,
        double HeightM,
        int FreeEnds,
        int ColumnContacts,
        int WallContacts,
        int SlabContacts,
        int BeamContacts);

    public static Result? TryCompute(Document doc, Element beam, FormworkRuleSet ruleSet)
    {
        var bb = beam.get_BoundingBox(null);
        if (bb is null) return null;

        var (widthM, heightM) = ResolveBeamSectionM(beam, bb);
        if (widthM < MinM || heightM < MinM) return null;

        var lengthM = ResolveLengthM(beam, bb);
        if (lengthM < MinM) return null;

        var alongX = BeamSpansAlongX(beam, bb);
        var pad = FormworkUnits.MmToFeet(Math.Max(ruleSet.Settings.ProbeEpsilonMm, 20));
        var widthEpsM = Math.Max(ruleSet.Settings.ProbeEpsilonMm, 5) / 1000.0;

        // Gross cheeks only — end mút added later if tip is free.
        var grossSide = 2.0 * heightM * lengthM;
        var grossBottom = widthM * lengthM;

        double deductSide = 0;
        double deductBottom = 0;
        var colN = 0;
        var wallN = 0;
        var slabN = 0;
        double slabEmbedM = 0;

        ResolveEndPoints(beam, bb, alongX, out var startPt, out var endPt);

        // --- Slab embed → both cheeks lose h_slab along overlap ---
        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Floors)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            var slabBb = el.get_BoundingBox(null);
            if (slabBb is null) continue;
            if (!FootprintsOverlap(bb, slabBb, pad)) continue;

            var zLo = Math.Max(bb.Min.Z, slabBb.Min.Z);
            var zHi = Math.Min(bb.Max.Z, slabBb.Max.Z);
            if (zHi - zLo <= pad) continue;
            if (slabBb.Min.Z < bb.Min.Z + FormworkUnits.MmToFeet(heightM * 1000.0) * 0.5)
                continue;

            var embedM = FormworkUnits.FeetToM(zHi - zLo);
            var alongM = OverlapAlongBeamM(bb, slabBb, alongX);
            if (alongM < MinM || embedM < MinM) continue;

            var hEmb = Math.Min(embedM, heightM);
            deductSide += 2.0 * alongM * hEmb;
            slabEmbedM = Math.Max(slabEmbedM, hEmb);
            slabN++;
        }

        var stemM = Math.Max(0, heightM - slabEmbedM);

        // --- Every intersecting column/wall (end or through): bottom + both cheeks ---
        ApplyColumnWallDeducts(
            doc, BuiltInCategory.OST_StructuralColumns, beam.Id, bb, alongX,
            startPt, endPt, pad, widthEpsM, widthM, stemM, lengthM,
            ref deductSide, ref deductBottom, ref colN,
            out var startOnCol, out var endOnCol);

        ApplyColumnWallDeducts(
            doc, BuiltInCategory.OST_Walls, beam.Id, bb, alongX,
            startPt, endPt, pad, widthEpsM, widthM, stemM, lengthM,
            ref deductSide, ref deductBottom, ref wallN,
            out var startOnWall, out var endOnWall);

        var startSupported = startOnCol || startOnWall;
        var endSupported = endOnCol || endOnWall;

        // Crossing girder at tip also supports the end (no free mút).
        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralFraming)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (el.Id == beam.Id) continue;
            var otherBb = el.get_BoundingBox(null);
            if (otherBb is null) continue;
            if (!FootprintsOverlap(bb, otherBb, pad)) continue;

            var zLo = Math.Max(bb.Min.Z, otherBb.Min.Z);
            var zHi = Math.Min(bb.Max.Z, otherBb.Max.Z);
            if (zHi - zLo <= pad) continue;
            if (BeamSpansAlongX(el, otherBb) == alongX)
                continue;

            if (PointNearEnd(startPt, otherBb, alongX, pad))
                startSupported = true;
            if (PointNearEnd(endPt, otherBb, alongX, pad))
                endSupported = true;
        }

        // Free / console tips: add end face into Side (b×stem).
        var freeEnds = (startSupported ? 0 : 1) + (endSupported ? 0 : 1);
        if (IsCantileverUsage(beam) && freeEnds == 0)
            freeEnds = 1;

        if (freeEnds > 0)
        {
            var endH = stemM > MinM ? stemM : heightM;
            grossSide += freeEnds * widthM * endH;
        }

        // Crossing beams are not deducted here — Side/Bottom only lose
        // column, wall, and slab contacts (PO: trừ cột/vách/sàn giao cắt).

        deductSide = Math.Min(deductSide, grossSide);
        deductBottom = Math.Min(deductBottom, grossBottom);
        var side = Math.Max(0, grossSide - deductSide);
        var bottom = Math.Max(0, grossBottom - deductBottom);

        return new Result(
            FormworkUnits.RoundM2(side),
            FormworkUnits.RoundM2(bottom),
            FormworkUnits.RoundM2(grossSide),
            FormworkUnits.RoundM2(grossBottom),
            FormworkUnits.RoundM2(deductSide),
            FormworkUnits.RoundM2(deductBottom),
            FormworkUnits.RoundM2(lengthM),
            FormworkUnits.RoundM2(widthM),
            FormworkUnits.RoundM2(heightM),
            freeEnds,
            colN,
            wallN,
            slabN,
            0);
    }

    /// <summary>
    /// Deduct every column/wall that intersects the beam in plan + Z —
    /// same for end bearing and mid-span through. Bottom + both cheeks × stem.
    /// </summary>
    private static void ApplyColumnWallDeducts(
        Document doc,
        BuiltInCategory cat,
        ElementId selfId,
        BoundingBoxXYZ bb,
        bool alongX,
        XYZ startPt,
        XYZ endPt,
        double pad,
        double widthEpsM,
        double widthM,
        double stemM,
        double lengthM,
        ref double deductSide,
        ref double deductBottom,
        ref int contactN,
        out bool startSupported,
        out bool endSupported)
    {
        startSupported = false;
        endSupported = false;

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(cat)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (el.Id == selfId) continue;
            var otherBb = el.get_BoundingBox(null);
            if (otherBb is null) continue;
            if (!FootprintsOverlap(bb, otherBb, pad)) continue;

            // Must share vertical band with the beam (through or bearing).
            var zLo = Math.Max(bb.Min.Z, otherBb.Min.Z);
            var zHi = Math.Min(bb.Max.Z, otherBb.Max.Z);
            if (zHi - zLo <= pad * 0.5) continue;

            var alongM = OverlapAlongBeamM(bb, otherBb, alongX);
            var acrossM = OverlapAcrossBeamM(bb, otherBb, alongX);
            if (alongM < widthEpsM || acrossM < widthEpsM) continue;

            if (PointNearEnd(startPt, otherBb, alongX, pad))
                startSupported = true;
            if (PointNearEnd(endPt, otherBb, alongX, pad))
                endSupported = true;

            var w = Math.Min(acrossM, widthM);
            var along = Math.Min(alongM, lengthM);

            deductBottom += along * w;
            if (stemM > MinM)
                deductSide += 2.0 * along * stemM;

            contactN++;
        }
    }

    private static void ResolveEndPoints(
        Element beam,
        BoundingBoxXYZ bb,
        bool alongX,
        out XYZ start,
        out XYZ end)
    {
        if (beam.Location is LocationCurve lc)
        {
            start = lc.Curve.GetEndPoint(0);
            end = lc.Curve.GetEndPoint(1);
            return;
        }

        var midY = (bb.Min.Y + bb.Max.Y) * 0.5;
        var midX = (bb.Min.X + bb.Max.X) * 0.5;
        var midZ = (bb.Min.Z + bb.Max.Z) * 0.5;
        if (alongX)
        {
            start = new XYZ(bb.Min.X, midY, midZ);
            end = new XYZ(bb.Max.X, midY, midZ);
        }
        else
        {
            start = new XYZ(midX, bb.Min.Y, midZ);
            end = new XYZ(midX, bb.Max.Y, midZ);
        }
    }

    private static bool PointNearEnd(XYZ endPt, BoundingBoxXYZ supportBb, bool alongX, double pad)
    {
        var tipPad = pad + FormworkUnits.MmToFeet(50);
        if (alongX)
        {
            return endPt.X >= supportBb.Min.X - tipPad &&
                   endPt.X <= supportBb.Max.X + tipPad &&
                   endPt.Y >= supportBb.Min.Y - tipPad &&
                   endPt.Y <= supportBb.Max.Y + tipPad;
        }

        return endPt.Y >= supportBb.Min.Y - tipPad &&
               endPt.Y <= supportBb.Max.Y + tipPad &&
               endPt.X >= supportBb.Min.X - tipPad &&
               endPt.X <= supportBb.Max.X + tipPad;
    }

    private static bool IsCantileverUsage(Element beam)
    {
        try
        {
            var p = beam.get_Parameter(BuiltInParameter.INSTANCE_STRUCT_USAGE_PARAM);
            if (p is null || p.StorageType != StorageType.Integer)
                return false;
            return p.AsInteger() == 5 ||
                   (p.AsValueString()?.Contains("Cantilever", StringComparison.OrdinalIgnoreCase) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private static double OverlapAlongBeamM(BoundingBoxXYZ beamBb, BoundingBoxXYZ otherBb, bool alongX)
    {
        if (alongX)
        {
            var lo = Math.Max(beamBb.Min.X, otherBb.Min.X);
            var hi = Math.Min(beamBb.Max.X, otherBb.Max.X);
            return FormworkUnits.FeetToM(Math.Max(0, hi - lo));
        }

        var loY = Math.Max(beamBb.Min.Y, otherBb.Min.Y);
        var hiY = Math.Min(beamBb.Max.Y, otherBb.Max.Y);
        return FormworkUnits.FeetToM(Math.Max(0, hiY - loY));
    }

    private static double OverlapAcrossBeamM(BoundingBoxXYZ beamBb, BoundingBoxXYZ otherBb, bool alongX)
    {
        if (alongX)
        {
            var lo = Math.Max(beamBb.Min.Y, otherBb.Min.Y);
            var hi = Math.Min(beamBb.Max.Y, otherBb.Max.Y);
            return FormworkUnits.FeetToM(Math.Max(0, hi - lo));
        }

        var loX = Math.Max(beamBb.Min.X, otherBb.Min.X);
        var hiX = Math.Min(beamBb.Max.X, otherBb.Max.X);
        return FormworkUnits.FeetToM(Math.Max(0, hiX - loX));
    }

    private static bool BeamSpansAlongX(Element beam, BoundingBoxXYZ bb)
    {
        if (beam.Location is LocationCurve lc)
        {
            var basis = lc.Curve.ComputeDerivatives(0.5, true).BasisX;
            if (!basis.IsZeroLength())
            {
                var dir = basis.Normalize();
                return Math.Abs(dir.X) >= Math.Abs(dir.Y);
            }
        }

        return bb.Max.X - bb.Min.X >= bb.Max.Y - bb.Min.Y;
    }

    /// <summary>
    /// Full modeled span (Location / Length). Avoid Cut Length — joins shorten it
    /// and would double-count when we also deduct intersecting columns.
    /// </summary>
    private static double ResolveLengthM(Element e, BoundingBoxXYZ bb)
    {
        try
        {
            if (e.Location is LocationCurve lc)
            {
                var curveLen = FormworkUnits.FeetToM(lc.Curve.Length);
                if (curveLen > MinM)
                    return curveLen;
            }

            var len = e.LookupParameter("Length")?.AsDouble();
            if (len is > 1e-9)
                return FormworkUnits.FeetToM(len.Value);
        }
        catch
        {
            // fall through
        }

        var dx = bb.Max.X - bb.Min.X;
        var dy = bb.Max.Y - bb.Min.Y;
        return FormworkUnits.FeetToM(Math.Max(dx, dy));
    }

    private static (double WidthM, double HeightM) ResolveBeamSectionM(Element e, BoundingBoxXYZ bb)
    {
        try
        {
            var symbol = (e as FamilyInstance)?.Symbol;
            var b = FirstPositiveLengthFt(symbol, e, "b", "B", "Width", "width");
            var h = FirstPositiveLengthFt(symbol, e, "h", "H", "Height", "height", "Depth", "d", "D");
            if (b > 1e-9 && h > 1e-9)
                return (FormworkUnits.FeetToM(b), FormworkUnits.FeetToM(h));
        }
        catch
        {
            // fall through
        }

        var dx = FormworkUnits.FeetToM(bb.Max.X - bb.Min.X);
        var dy = FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y);
        var dz = FormworkUnits.FeetToM(bb.Max.Z - bb.Min.Z);
        return (Math.Min(dx, dy), dz);
    }

    private static double FirstPositiveLengthFt(Element? symbol, Element instance, params string[] names)
    {
        foreach (var name in names)
        {
            var p = symbol?.LookupParameter(name) ?? instance.LookupParameter(name);
            if (p is not null && p.HasValue && p.StorageType == StorageType.Double)
            {
                var v = p.AsDouble();
                if (v > 1e-9) return v;
            }
        }

        return 0;
    }

    private static bool FootprintsOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b, double pad) =>
        a.Min.X - pad <= b.Max.X + pad &&
        a.Max.X + pad >= b.Min.X - pad &&
        a.Min.Y - pad <= b.Max.Y + pad &&
        a.Max.Y + pad >= b.Min.Y - pad;
}
