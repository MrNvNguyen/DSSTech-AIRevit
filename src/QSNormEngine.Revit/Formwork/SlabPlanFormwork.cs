using Autodesk.Revit.DB;
using QSNormEngine.Core.Formwork;

namespace QSNormEngine.Revit.Formwork;

/// <summary>
/// Join-independent slab formwork:
/// Bottom = Σ outer sketch loops − openings ≥1 m² − union(beam/column/wall plan under soffit).
///   Does not use HOST_AREA (join-dependent). Multi-boundary floors sum all outer loops.
/// Side = free outer perimeter × thickness + opening faces ≥1 m².
/// Free perimeter excludes edges that abut another floor at the same level.
/// </summary>
internal static class SlabPlanFormwork
{
    private const double ExtrudeHtFt = 1.0;
    private const double EdgeAbutTolMm = 30;

    public readonly record struct Result(
        double BottomM2,
        double SideM2,
        double SupportDeductM2,
        double OuterAreaM2,
        double OuterPerimeterM,
        double OuterSideM2,
        double OpeningSideM2,
        int SmallOpenings,
        int LargeOpenings,
        bool UsedSketch);

    public static Result? TryCompute(
        Document doc,
        Floor floor,
        FormworkRuleSet ruleSet,
        IReadOnlyList<SolidEntry>? _ = null)
    {
        if (!TrySketchMetrics(floor, out var outerAreaM2, out var outerPeriM, out var outerEdges, out var sketchOpenings))
            return null;

        var threshold = ruleSet.Settings.SlabOpeningThresholdM2 > 0
            ? ruleSet.Settings.SlabOpeningThresholdM2
            : 1.0;
        var thicknessM = ResolveThicknessM(floor);

        // Openings: hosted first, then inner sketch loops.
        var openings = new List<(double AreaM2, double PeriM, double Cx, double Cy)>();
        AppendHostedOpenings(doc, floor, openings);
        foreach (var sk in sketchOpenings)
        {
            if (IsNearDuplicateOpening(openings, sk.AreaM2, sk.PeriM, sk.Cx, sk.Cy))
                continue;
            openings.Add(sk);
        }

        double openingSideM2 = 0;
        double largeOpenAreaM2 = 0;
        var small = 0;
        var large = 0;
        foreach (var (area, peri, _, _) in openings)
        {
            if (IsLargeOpening(area, threshold))
            {
                largeOpenAreaM2 += area;
                var periUse = peri > 1e-6 ? peri : EstimatedSquarePeriM(area);
                openingSideM2 += periUse * thicknessM;
                large++;
            }
            else
            {
                small++;
            }
        }

        // Bottom: sketch only (join-independent) — never HOST_AREA.
        var bottom = Math.Max(0, outerAreaM2 - largeOpenAreaM2);
        var reportedOuter = outerAreaM2;

        var pad = FormworkUnits.MmToFeet(ruleSet.Settings.ProbeEpsilonMm);
        var zTol = FormworkUnits.MmToFeet(Math.Max(ruleSet.Settings.ProbeEpsilonMm, 80));
        var slabSoffitZ = ResolveSlabSoffitZ(floor);
        var slabTopZ = ResolveSlabTopZ(floor, slabSoffitZ, thicknessM);

        // Always subtract beam/column/wall footprint union under soffit (join-independent).
        // Flag defaults true; set false only to debug gross sketch without support deduct.
        double supportDeduct = 0;
        if (ruleSet.Settings.SlabDeductSupportContact)
        {
            supportDeduct = ComputeSupportUnionAreaM2(
                doc, floor, slabSoffitZ, slabTopZ, pad, zTol);
            supportDeduct = Math.Min(supportDeduct, bottom);
            bottom = Math.Max(0, bottom - supportDeduct);
        }

        // Free outer perimeter: drop edges that abut other floors at this level.
        var abutTolFt = FormworkUnits.MmToFeet(EdgeAbutTolMm);
        var sharedM = SharedBoundaryWithNeighborFloorsM(
            doc, floor, outerEdges, slabSoffitZ, zTol, abutTolFt);
        var freePeriM = Math.Max(0, outerPeriM - sharedM);
        var outerSideM2 = freePeriM * thicknessM;
        var side = outerSideM2 + openingSideM2;

        return new Result(
            FormworkUnits.RoundM2(bottom),
            FormworkUnits.RoundM2(side),
            FormworkUnits.RoundM2(supportDeduct),
            FormworkUnits.RoundM2(reportedOuter),
            FormworkUnits.RoundM2(freePeriM),
            FormworkUnits.RoundM2(outerSideM2),
            FormworkUnits.RoundM2(openingSideM2),
            small,
            large,
            true);
    }

    /// <summary>
    /// Union of beam / column / wall footprints under the slab soffit, clipped to sketch.
    /// Boolean union only — additive sum over-counts beam crossings and must not win.
    /// </summary>
    private static double ComputeSupportUnionAreaM2(
        Document doc,
        Floor floor,
        double slabSoffitZ,
        double slabTopZ,
        double pad,
        double zTol)
    {
        if (!TrySlabFootprintSolid(floor, out var slabSolid) || slabSolid is null)
            return FallbackAdditiveSupportM2(doc, floor, slabSoffitZ, slabTopZ, pad, zTol);

        Solid? coverage = null;
        var seen = new HashSet<long>();

        void Consider(Element el, Solid? solid)
        {
            if (solid is null) return;
            if (!seen.Add(el.Id.Value)) return;
            AddSlabSupportCoverage(slabSolid, solid, ref coverage);
        }

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralFraming)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (TryBeamFootprintSolid(el, slabSoffitZ, slabTopZ, zTol, out var solid))
                Consider(el, solid);
        }

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (TryColumnFootprintSolid(el, slabSoffitZ, slabTopZ, pad, zTol, out var solid))
                Consider(el, solid);
        }

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Walls)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (TryWallFootprintSolid(el, slabSoffitZ, slabTopZ, zTol, out var solid))
                Consider(el, solid);
        }

        if (coverage is not null && coverage.Volume > 1e-12)
            return FormworkUnits.SquareFeetToM2(coverage.Volume / ExtrudeHtFt);

        // Boolean failed entirely — last resort (may slightly over-count crossings).
        return FallbackAdditiveSupportM2(doc, floor, slabSoffitZ, slabTopZ, pad, zTol);
    }

    private static double FallbackAdditiveSupportM2(
        Document doc,
        Floor floor,
        double slabSoffitZ,
        double slabTopZ,
        double pad,
        double zTol)
    {
        var slabXy = ResolveSlabXyBounds(floor);
        if (slabXy is null) return 0;

        double sum = 0;
        var seen = new HashSet<long>();
        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralFraming)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (!seen.Add(el.Id.Value)) continue;
            if (TryBeamSupportContact(el, slabXy, slabSoffitZ, slabTopZ, zTol, out var a))
                sum += a;
        }

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (!seen.Add(el.Id.Value)) continue;
            if (TryColumnSupportContact(el, slabXy, slabSoffitZ, slabTopZ, pad, zTol, out var a))
                sum += a;
        }

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Walls)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (!seen.Add(el.Id.Value)) continue;
            if (TryWallSupportContact(el, slabXy, slabSoffitZ, slabTopZ, zTol, out var a))
                sum += a;
        }

        return sum;
    }

    private static void AddSlabSupportCoverage(Solid slabSolid, Solid supportSolid, ref Solid? coverage)
    {
        Solid? piece;
        try
        {
            piece = BooleanOperationsUtils.ExecuteBooleanOperation(
                slabSolid, supportSolid, BooleanOperationsType.Intersect);
        }
        catch
        {
            return;
        }

        if (piece is null || piece.Volume <= 1e-12)
            return;

        coverage = UnionSolids(coverage, piece);
    }

    private static Solid? UnionSolids(Solid? existing, Solid add)
    {
        if (existing is null)
            return add;
        try
        {
            return BooleanOperationsUtils.ExecuteBooleanOperation(
                existing, add, BooleanOperationsType.Union);
        }
        catch
        {
            try
            {
                var overlap = BooleanOperationsUtils.ExecuteBooleanOperation(
                    existing, add, BooleanOperationsType.Intersect);
                if (overlap is null || overlap.Volume <= 1e-12)
                {
                    return BooleanOperationsUtils.ExecuteBooleanOperation(
                        existing, add, BooleanOperationsType.Union);
                }

                var diff = BooleanOperationsUtils.ExecuteBooleanOperation(
                    add, overlap, BooleanOperationsType.Difference);
                if (diff is null || diff.Volume <= 1e-12)
                    return existing;

                return BooleanOperationsUtils.ExecuteBooleanOperation(
                    existing, diff, BooleanOperationsType.Union);
            }
            catch
            {
                return existing;
            }
        }
    }

    private static bool TrySlabFootprintSolid(Floor floor, out Solid? solid)
    {
        solid = null;
        try
        {
            var sketchId = floor.SketchId;
            if (sketchId == ElementId.InvalidElementId) return false;
            if (floor.Document.GetElement(sketchId) is not Sketch sketch) return false;
            var profile = sketch.Profile;
            if (profile is null || profile.Size == 0) return false;

            // All outer loops (CCW); ignore CW openings. Multi-boundary → union.
            var outerLoops = new List<(CurveLoop Loop, double AreaFt2, bool Ccw)>();
            foreach (CurveArray arr in profile)
            {
                if (!TryCurveLoop(arr, out var loop, out var areaFt2, out var ccw) || loop is null)
                    continue;
                if (areaFt2 <= 1e-12) continue;
                outerLoops.Add((loop, areaFt2, ccw));
            }

            if (outerLoops.Count == 0) return false;

            var hasCcw = outerLoops.Any(o => o.Ccw);
            var hasCw = outerLoops.Any(o => !o.Ccw);
            List<CurveLoop> loops;
            if (hasCcw && hasCw)
                loops = outerLoops.Where(o => o.Ccw).Select(o => o.Loop).ToList();
            else
                // All same winding (multi-boundary plates) → every loop is outer.
                loops = outerLoops.Select(o => o.Loop).ToList();

            if (loops.Count == 0) return false;

            Solid? union = null;
            foreach (var loop in loops)
            {
                Solid piece;
                try
                {
                    piece = GeometryCreationUtilities.CreateExtrusionGeometry(
                        [loop], XYZ.BasisZ, ExtrudeHtFt);
                }
                catch
                {
                    continue;
                }

                if (piece is null || piece.Volume <= 1e-12) continue;
                union = union is null ? piece : UnionSolids(union, piece);
            }

            solid = union;
            return solid is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBeamFootprintSolid(
        Element beam,
        double slabSoffitZ,
        double slabTopZ,
        double zTol,
        out Solid? solid)
    {
        solid = null;
        if (!SupportTouchesSlabBand(beam, slabSoffitZ, slabTopZ, zTol))
            return false;

        var widthFt = TryBeamWidthFt(beam);
        if (widthFt <= 1e-9) return false;
        if (beam.Location is not LocationCurve lc) return false;

        try
        {
            var curve = lc.Curve;
            var p0 = curve.GetEndPoint(0);
            var p1 = curve.GetEndPoint(1);
            var dir = p1 - p0;
            if (dir.GetLength() < 1e-9) return false;
            dir = dir.Normalize();
            var perp = XYZ.BasisZ.CrossProduct(dir);
            if (perp.GetLength() < 1e-9) return false;
            perp = perp.Normalize();
            var half = widthFt * 0.5;

            var c0 = p0 + perp * half;
            var c1 = p1 + perp * half;
            var c2 = p1 - perp * half;
            var c3 = p0 - perp * half;
            // Flatten to Z=0 for planar extrusion.
            c0 = new XYZ(c0.X, c0.Y, 0);
            c1 = new XYZ(c1.X, c1.Y, 0);
            c2 = new XYZ(c2.X, c2.Y, 0);
            c3 = new XYZ(c3.X, c3.Y, 0);

            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(c0, c1));
            loop.Append(Line.CreateBound(c1, c2));
            loop.Append(Line.CreateBound(c2, c3));
            loop.Append(Line.CreateBound(c3, c0));

            solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                [loop], XYZ.BasisZ, ExtrudeHtFt);
            return solid is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryColumnFootprintSolid(
        Element column,
        double slabSoffitZ,
        double slabTopZ,
        double pad,
        double zTol,
        out Solid? solid)
    {
        solid = null;
        if (!SupportTouchesSlabBand(column, slabSoffitZ, slabTopZ, zTol))
            return false;

        if (!TryColumnSectionFt(column, out var bFt, out var hFt) || bFt <= 1e-9 || hFt <= 1e-9)
            return false;

        XYZ origin;
        if (column.Location is LocationPoint lp)
            origin = lp.Point;
        else
        {
            var bb = column.get_BoundingBox(null);
            if (bb is null) return false;
            origin = (bb.Min + bb.Max) * 0.5;
        }

        try
        {
            // Orient with instance transform if available.
            XYZ xDir = XYZ.BasisX;
            XYZ yDir = XYZ.BasisY;
            if (column is FamilyInstance fi)
            {
                var t = fi.GetTransform();
                xDir = new XYZ(t.BasisX.X, t.BasisX.Y, 0);
                yDir = new XYZ(t.BasisY.X, t.BasisY.Y, 0);
                if (xDir.GetLength() > 1e-9) xDir = xDir.Normalize();
                else xDir = XYZ.BasisX;
                if (yDir.GetLength() > 1e-9) yDir = yDir.Normalize();
                else yDir = XYZ.BasisY;
            }

            var hx = bFt * 0.5;
            var hy = hFt * 0.5;
            var o = new XYZ(origin.X, origin.Y, 0);
            var c0 = o + xDir * hx + yDir * hy;
            var c1 = o - xDir * hx + yDir * hy;
            var c2 = o - xDir * hx - yDir * hy;
            var c3 = o + xDir * hx - yDir * hy;

            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(c0, c1));
            loop.Append(Line.CreateBound(c1, c2));
            loop.Append(Line.CreateBound(c2, c3));
            loop.Append(Line.CreateBound(c3, c0));

            solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                [loop], XYZ.BasisZ, ExtrudeHtFt);
            return solid is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWallFootprintSolid(
        Element wall,
        double slabSoffitZ,
        double slabTopZ,
        double zTol,
        out Solid? solid)
    {
        solid = null;
        if (wall is not Wall w)
            return false;
        if (!SupportTouchesSlabBand(wall, slabSoffitZ, slabTopZ, zTol))
            return false;

        var thickFt = w.WallType?.Width ?? 0;
        if (thickFt <= 1e-9)
            return false;
        if (wall.Location is not LocationCurve lc)
            return false;

        try
        {
            var curve = lc.Curve;
            var p0 = curve.GetEndPoint(0);
            var p1 = curve.GetEndPoint(1);
            var dir = p1 - p0;
            if (dir.GetLength() < 1e-9) return false;
            dir = dir.Normalize();
            var perp = XYZ.BasisZ.CrossProduct(dir);
            if (perp.GetLength() < 1e-9) return false;
            perp = perp.Normalize();
            var half = thickFt * 0.5;

            var c0 = new XYZ((p0 + perp * half).X, (p0 + perp * half).Y, 0);
            var c1 = new XYZ((p1 + perp * half).X, (p1 + perp * half).Y, 0);
            var c2 = new XYZ((p1 - perp * half).X, (p1 - perp * half).Y, 0);
            var c3 = new XYZ((p0 - perp * half).X, (p0 - perp * half).Y, 0);

            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(c0, c1));
            loop.Append(Line.CreateBound(c1, c2));
            loop.Append(Line.CreateBound(c2, c3));
            loop.Append(Line.CreateBound(c3, c0));

            solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                [loop], XYZ.BasisZ, ExtrudeHtFt);
            return solid is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCurveLoop(CurveArray arr, out CurveLoop? loop, out double areaFt2) =>
        TryCurveLoop(arr, out loop, out areaFt2, out _);

    private static bool TryCurveLoop(
        CurveArray arr,
        out CurveLoop? loop,
        out double areaFt2,
        out bool isCcw)
    {
        loop = null;
        areaFt2 = 0;
        isCcw = true;
        try
        {
            var curves = new List<Curve>();
            var pts = new List<XYZ>();
            foreach (Curve c in arr)
            {
                if (c is null) continue;
                // Project to Z=0.
                var p0 = c.GetEndPoint(0);
                var p1 = c.GetEndPoint(1);
                var a = new XYZ(p0.X, p0.Y, 0);
                var b = new XYZ(p1.X, p1.Y, 0);
                if (a.DistanceTo(b) < 1e-9) continue;
                curves.Add(Line.CreateBound(a, b));
                pts.Add(a);
            }

            if (curves.Count < 3 || pts.Count < 3) return false;

            double sum = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % pts.Count];
                sum += p.X * q.Y - q.X * p.Y;
            }

            isCcw = sum > 0;
            areaFt2 = Math.Abs(sum) * 0.5;
            loop = CurveLoop.Create(curves);
            return areaFt2 > 1e-12;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendHostedOpenings(
        Document doc,
        Floor floor,
        List<(double AreaM2, double PeriM, double Cx, double Cy)> openings)
    {
        var seenIds = new HashSet<long>();

        void AddOpening(Element el)
        {
            if (el is null || !seenIds.Add(el.Id.Value)) return;
            if (!TryElementOpeningMetrics(el, out var area, out var peri, out var cx, out var cy) ||
                area <= 1e-9)
                return;
            openings.Add((area, peri, cx, cy));
        }

        // Accept Opening hosted on this floor OR cutting its bbox (Opening-by-face quirks).
        bool IsFloorOpening(Element el)
        {
            if (el is not Opening op) return false;
            if (op.Host?.Id == floor.Id) return true;
            var ob = op.get_BoundingBox(null);
            var sb = floor.get_BoundingBox(null);
            if (ob is null || sb is null) return false;
            if (!FootprintsOverlap(ob, sb, FormworkUnits.MmToFeet(10))) return false;
            // Vertical overlap with slab thickness band.
            return ob.Max.Z >= sb.Min.Z - FormworkUnits.MmToFeet(50) &&
                   ob.Min.Z <= sb.Max.Z + FormworkUnits.MmToFeet(50);
        }

        // 1) Inserts — try both cut flags (Opening by face varies by host).
        foreach (var cutsOnly in new[] { false, true })
        {
            try
            {
                foreach (var id in floor.FindInserts(true, false, false, cutsOnly))
                {
                    var el = doc.GetElement(id);
                    if (el is Opening ||
                        el?.Category?.Id.Value == (int)BuiltInCategory.OST_ShaftOpening)
                        AddOpening(el);
                }
            }
            catch
            {
                // optional
            }
        }

        try
        {
            foreach (var id in floor.FindInserts(true, true, true, true))
            {
                var el = doc.GetElement(id);
                if (el is Opening ||
                    el?.Category?.Id.Value == (int)BuiltInCategory.OST_ShaftOpening)
                    AddOpening(el);
            }
        }
        catch
        {
            // optional
        }

        // 2) Dependent Opening elements.
        try
        {
            foreach (var id in floor.GetDependentElements(new ElementClassFilter(typeof(Opening))))
                AddOpening(doc.GetElement(id));
        }
        catch
        {
            // optional
        }

        // 3) All openings that cut this floor (Host match OR bbox cut).
        try
        {
            foreach (Opening op in new FilteredElementCollector(doc)
                         .OfClass(typeof(Opening))
                         .Cast<Opening>())
            {
                if (IsFloorOpening(op))
                    AddOpening(op);
            }
        }
        catch
        {
            // sketch holes still apply
        }

        // 4) Shaft openings cutting this level.
        try
        {
            foreach (var el in new FilteredElementCollector(doc)
                         .OfCategory(BuiltInCategory.OST_ShaftOpening)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                var bb = el.get_BoundingBox(null);
                var slabBb = floor.get_BoundingBox(null);
                if (bb is null || slabBb is null) continue;
                if (!FootprintsOverlap(bb, slabBb, FormworkUnits.MmToFeet(5)))
                    continue;
                if (bb.Max.Z < slabBb.Min.Z - FormworkUnits.MmToFeet(50) ||
                    bb.Min.Z > slabBb.Max.Z + FormworkUnits.MmToFeet(50))
                    continue;

                if (!seenIds.Add(el.Id.Value)) continue;
                if (!TryElementOpeningMetrics(el, out var area, out var peri, out var cx, out var cy) ||
                    area <= 1e-9)
                {
                    var w = FormworkUnits.FeetToM(bb.Max.X - bb.Min.X);
                    var d = FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y);
                    area = Math.Abs(w * d);
                    peri = 2.0 * (Math.Abs(w) + Math.Abs(d));
                    cx = FormworkUnits.FeetToM((bb.Min.X + bb.Max.X) * 0.5);
                    cy = FormworkUnits.FeetToM((bb.Min.Y + bb.Max.Y) * 0.5);
                }

                if (area > 1e-9)
                    openings.Add((area, peri, cx, cy));
            }
        }
        catch
        {
            // optional
        }
    }

    private static double EstimatedSquarePeriM(double areaM2) =>
        areaM2 > 1e-9 ? 4.0 * Math.Sqrt(areaM2) : 0;

    /// <summary>≥ threshold with 0.01 m² tolerance (exact 1.0 m² openings must count).</summary>
    private static bool IsLargeOpening(double areaM2, double thresholdM2) =>
        areaM2 + 0.01 >= thresholdM2;

    /// <summary>
    /// Duplicate only when same place (centroid within 150 mm).
    /// Different 1×1 m holes stay separate; sketch loop vs same Opening insert collapses.
    /// </summary>
    private static bool IsNearDuplicateOpening(
        List<(double AreaM2, double PeriM, double Cx, double Cy)> seen,
        double area,
        double peri,
        double cx,
        double cy)
    {
        // No reliable position — never collapse (avoids wiping 3 identical 1 m² holes).
        if (Math.Abs(cx) < 1e-9 && Math.Abs(cy) < 1e-9)
            return false;

        const double posTolM = 0.15;
        foreach (var (a, _, x, y) in seen)
        {
            if (Math.Abs(x) < 1e-9 && Math.Abs(y) < 1e-9)
                continue;
            var dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (dist <= posTolM && Math.Abs(a - area) <= 0.05)
                return true;
        }

        return false;
    }

    private static bool TryElementOpeningMetrics(
        Element el,
        out double areaM2,
        out double periM,
        out double cx,
        out double cy)
    {
        areaM2 = 0;
        periM = 0;
        cx = 0;
        cy = 0;
        if (el is Opening opening)
            return TryOpeningMetrics(opening, out areaM2, out periM, out cx, out cy);

        if (!TryOpeningFootprintFromGeometry(el, out areaM2, out periM, out cx, out cy))
        {
            var bb = el.get_BoundingBox(null);
            if (bb is null) return false;
            var w = FormworkUnits.FeetToM(bb.Max.X - bb.Min.X);
            var d = FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y);
            areaM2 = Math.Abs(w * d);
            periM = 2.0 * (Math.Abs(w) + Math.Abs(d));
            cx = FormworkUnits.FeetToM((bb.Min.X + bb.Max.X) * 0.5);
            cy = FormworkUnits.FeetToM((bb.Min.Y + bb.Max.Y) * 0.5);
        }

        return areaM2 > 0;
    }

    private static bool TryOpeningMetrics(
        Opening op,
        out double areaM2,
        out double periM,
        out double cx,
        out double cy)
    {
        areaM2 = 0;
        periM = 0;
        cx = 0;
        cy = 0;
        try
        {
            var curves = op.BoundaryCurves;
            if (curves is not null && curves.Size > 0)
            {
                var pts = new List<XYZ>();
                foreach (Curve c in curves)
                {
                    if (c is null) continue;
                    periM += FormworkUnits.FeetToM(c.Length);
                    pts.Add(c.GetEndPoint(0));
                }

                if (pts.Count >= 3)
                {
                    double sum = 0;
                    double sx = 0, sy = 0;
                    for (var i = 0; i < pts.Count; i++)
                    {
                        var a = pts[i];
                        var b = pts[(i + 1) % pts.Count];
                        sum += a.X * b.Y - b.X * a.Y;
                        sx += a.X;
                        sy += a.Y;
                    }

                    areaM2 = FormworkUnits.SquareFeetToM2(Math.Abs(sum) * 0.5);
                    cx = FormworkUnits.FeetToM(sx / pts.Count);
                    cy = FormworkUnits.FeetToM(sy / pts.Count);
                    if (areaM2 > 0)
                        return true;
                }
            }

            if (TryOpeningFootprintFromGeometry(op, out areaM2, out periM, out cx, out cy))
                return true;

            var bb = op.get_BoundingBox(null);
            if (bb is null) return false;
            var w = FormworkUnits.FeetToM(bb.Max.X - bb.Min.X);
            var d = FormworkUnits.FeetToM(bb.Max.Y - bb.Min.Y);
            areaM2 = Math.Abs(w * d);
            periM = 2.0 * (Math.Abs(w) + Math.Abs(d));
            cx = FormworkUnits.FeetToM((bb.Min.X + bb.Max.X) * 0.5);
            cy = FormworkUnits.FeetToM((bb.Min.Y + bb.Max.Y) * 0.5);
            return areaM2 > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryOpeningFootprintFromGeometry(
        Element el,
        out double areaM2,
        out double periM,
        out double cx,
        out double cy)
    {
        areaM2 = 0;
        periM = 0;
        cx = 0;
        cy = 0;
        try
        {
            var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
            var geo = el.get_Geometry(opt);
            if (geo is null) return false;

            double bestArea = 0;
            double bestPeri = 0;
            double bestCx = 0, bestCy = 0;
            foreach (var obj in geo)
            {
                if (obj is GeometryInstance gi)
                {
                    var inst = gi.GetInstanceGeometry();
                    if (inst is null) continue;
                    foreach (var o2 in inst)
                    {
                        if (o2 is Solid s2)
                            ConsiderSolid(s2, ref bestArea, ref bestPeri, ref bestCx, ref bestCy);
                    }
                }
                else if (obj is Solid solid)
                {
                    ConsiderSolid(solid, ref bestArea, ref bestPeri, ref bestCx, ref bestCy);
                }
            }

            if (bestArea <= 1e-9) return false;
            areaM2 = bestArea;
            periM = bestPeri > 1e-6 ? bestPeri : EstimatedSquarePeriM(bestArea);
            cx = bestCx;
            cy = bestCy;
            return true;
        }
        catch
        {
            return false;
        }

        static void ConsiderSolid(
            Solid solid,
            ref double bestArea,
            ref double bestPeri,
            ref double bestCx,
            ref double bestCy)
        {
            if (solid is null || solid.Faces.Size == 0 || solid.Volume <= 1e-12)
                return;

            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace pf) continue;
                if (Math.Abs(pf.FaceNormal.Z) < 0.85) continue;
                IList<CurveLoop> loops;
                try
                {
                    loops = pf.GetEdgesAsCurveLoops();
                }
                catch
                {
                    continue;
                }

                if (loops.Count == 0) continue;
                double faceBest = 0;
                double facePeri = 0;
                double faceCx = 0, faceCy = 0;
                foreach (var loop in loops)
                {
                    if (!TryCurveLoopPlanMetrics(loop, out var a, out var p, out var x, out var y) ||
                        a <= 1e-9)
                        continue;
                    if (a > faceBest)
                    {
                        faceBest = a;
                        facePeri = p;
                        faceCx = x;
                        faceCy = y;
                    }
                }

                if (faceBest > bestArea)
                {
                    bestArea = faceBest;
                    bestPeri = facePeri;
                    bestCx = faceCx;
                    bestCy = faceCy;
                }
            }
        }
    }

    private static bool TryCurveLoopPlanMetrics(
        CurveLoop loop,
        out double areaM2,
        out double periM,
        out double cx,
        out double cy)
    {
        areaM2 = 0;
        periM = 0;
        cx = 0;
        cy = 0;
        try
        {
            var pts = new List<XYZ>();
            foreach (Curve c in loop)
            {
                if (c is null) continue;
                periM += FormworkUnits.FeetToM(c.Length);
                var p0 = c.GetEndPoint(0);
                pts.Add(new XYZ(p0.X, p0.Y, 0));
            }

            if (pts.Count < 3) return false;
            double sum = 0;
            double sx = 0, sy = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                sum += a.X * b.Y - b.X * a.Y;
                sx += a.X;
                sy += a.Y;
            }

            areaM2 = FormworkUnits.SquareFeetToM2(Math.Abs(sum) * 0.5);
            cx = FormworkUnits.FeetToM(sx / pts.Count);
            cy = FormworkUnits.FeetToM(sy / pts.Count);
            return areaM2 > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFloorAreaM2(Floor floor, out double areaM2)
    {
        areaM2 = 0;
        try
        {
            var p = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)
                    ?? floor.LookupParameter("Area");
            if (p is null || !p.HasValue) return false;
            areaM2 = FormworkUnits.SquareFeetToM2(p.AsDouble());
            return areaM2 > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFloorPerimeterM(Floor floor, out double periM)
    {
        periM = 0;
        try
        {
            var p = floor.get_Parameter(BuiltInParameter.HOST_PERIMETER_COMPUTED)
                    ?? floor.LookupParameter("Perimeter");
            if (p is null || !p.HasValue) return false;
            periM = FormworkUnits.FeetToM(p.AsDouble());
            return periM > 0;
        }
        catch
        {
            return false;
        }
    }

    private static double TryFloorPerimeterM(Floor floor) =>
        TryFloorPerimeterM(floor, out var peri) ? peri : 0;

    private static bool TryBeamSupportContact(
        Element beam,
        BoundingBoxXYZ slabXy,
        double slabSoffitZ,
        double slabTopZ,
        double zTol,
        out double areaM2)
    {
        areaM2 = 0;
        if (!SupportTouchesSlabBand(beam, slabSoffitZ, slabTopZ, zTol))
            return false;

        var widthM = FormworkUnits.FeetToM(TryBeamWidthFt(beam));
        var lengthM = TryBeamLengthUnderSlabM(beam, slabXy);
        if (widthM <= 1e-9 || lengthM <= 1e-9)
            return false;

        areaM2 = widthM * lengthM;
        return areaM2 > 1e-9;
    }

    private static bool TryColumnSupportContact(
        Element column,
        BoundingBoxXYZ slabXy,
        double slabSoffitZ,
        double slabTopZ,
        double pad,
        double zTol,
        out double areaM2)
    {
        areaM2 = 0;
        if (!SupportTouchesSlabBand(column, slabSoffitZ, slabTopZ, zTol))
            return false;

        if (!TryColumnSectionFt(column, out var bFt, out var hFt))
            return false;

        if (!ColumnFootprintOverlapsSlab(column, slabXy, pad))
            return false;

        areaM2 = FormworkUnits.FeetToM(bFt) * FormworkUnits.FeetToM(hFt);
        return areaM2 > 1e-9;
    }

    private static bool TryWallSupportContact(
        Element wall,
        BoundingBoxXYZ slabXy,
        double slabSoffitZ,
        double slabTopZ,
        double zTol,
        out double areaM2)
    {
        areaM2 = 0;
        if (wall is not Wall w)
            return false;
        if (!SupportTouchesSlabBand(wall, slabSoffitZ, slabTopZ, zTol))
            return false;

        var thickM = FormworkUnits.FeetToM(w.WallType?.Width ?? 0);
        if (thickM <= 1e-9) return false;
        if (wall.Location is not LocationCurve lc) return false;

        var curve = lc.Curve;
        var lenFt = curve.Length;
        var steps = Math.Max(4, (int)Math.Ceiling(lenFt / FormworkUnits.MmToFeet(200)));
        var inside = 0;
        for (var i = 0; i <= steps; i++)
        {
            var p = curve.Evaluate(i / (double)steps, true);
            if (p.X >= slabXy.Min.X && p.X <= slabXy.Max.X &&
                p.Y >= slabXy.Min.Y && p.Y <= slabXy.Max.Y)
                inside++;
        }

        var lengthM = FormworkUnits.FeetToM(lenFt * inside / (double)(steps + 1));
        if (lengthM <= 1e-9) return false;
        areaM2 = thickM * lengthM;
        return areaM2 > 1e-9;
    }

    private static bool ColumnFootprintOverlapsSlab(Element column, BoundingBoxXYZ slabXy, double pad)
    {
        XYZ? p = null;
        if (column.Location is LocationPoint lp)
            p = lp.Point;
        if (p is null)
        {
            var bb = column.get_BoundingBox(null);
            if (bb is null) return false;
            p = (bb.Min + bb.Max) * 0.5;
        }

        return p.X >= slabXy.Min.X - pad && p.X <= slabXy.Max.X + pad &&
               p.Y >= slabXy.Min.Y - pad && p.Y <= slabXy.Max.Y + pad;
    }

    private static double ResolveSlabSoffitZ(Floor floor)
    {
        var p = floor.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM)
                ?? floor.LookupParameter("Elevation at Bottom");
        if (p is not null && p.HasValue)
            return p.AsDouble();

        var level = floor.Document.GetElement(floor.LevelId) as Level;
        var offset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
        var thick = ResolveThicknessM(floor) / 0.3048;
        return (level?.Elevation ?? 0) + offset - thick;
    }

    private static double ResolveSlabTopZ(Floor floor, double soffitZ, double thicknessM) =>
        soffitZ + thicknessM / 0.3048;

    private static BoundingBoxXYZ? ResolveSlabXyBounds(Floor floor)
    {
        try
        {
            var sketchId = floor.SketchId;
            if (sketchId != ElementId.InvalidElementId &&
                floor.Document.GetElement(sketchId) is Sketch sketch &&
                sketch.Profile is { Size: > 0 })
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (CurveArray arr in sketch.Profile)
                {
                    foreach (Curve c in arr)
                    {
                        if (c is null) continue;
                        foreach (var p in new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                        {
                            minX = Math.Min(minX, p.X);
                            minY = Math.Min(minY, p.Y);
                            maxX = Math.Max(maxX, p.X);
                            maxY = Math.Max(maxY, p.Y);
                        }
                    }
                }

                if (minX < maxX && minY < maxY)
                {
                    return new BoundingBoxXYZ
                    {
                        Min = new XYZ(minX, minY, 0),
                        Max = new XYZ(maxX, maxY, 0)
                    };
                }
            }
        }
        catch
        {
            // fall through
        }

        return floor.get_BoundingBox(null);
    }

    private static double ResolveBeamTopZ(Element beam)
    {
        var p = beam.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_TOP)
                ?? beam.LookupParameter("Elevation at Top");
        if (p is not null && p.HasValue)
            return p.AsDouble();

        try
        {
            var refId = beam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId();
            var level = refId is not null ? beam.Document.GetElement(refId) as Level : null;
            var zOff = beam.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.AsDouble() ?? 0;
            var h = TryBeamHeightFt(beam);
            return (level?.Elevation ?? 0) + zOff + (h > 0 ? h : 0);
        }
        catch
        {
            var bb = beam.get_BoundingBox(null);
            return bb?.Max.Z ?? 0;
        }
    }

    private static double ResolveColumnTopZ(Element column)
    {
        var top = column.LookupParameter("Elevation at Top")
                  ?? column.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_TOP);
        if (top is not null && top.HasValue)
            return top.AsDouble();

        try
        {
            if (column is FamilyInstance fi)
            {
                var topLevelId = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId();
                var topLevel = topLevelId is not null ? column.Document.GetElement(topLevelId) as Level : null;
                var topOff = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;
                if (topLevel is not null)
                    return topLevel.Elevation + topOff;
            }
        }
        catch
        {
            // fall through
        }

        var bb = column.get_BoundingBox(null);
        return bb?.Max.Z ?? 0;
    }

    private static double TryBeamHeightFt(Element e)
    {
        try
        {
            var symbol = (e as FamilyInstance)?.Symbol;
            var h = symbol?.LookupParameter("h")?.AsDouble()
                    ?? symbol?.LookupParameter("Height")?.AsDouble();
            return h is > 1e-9 ? h.Value : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static double TryBeamWidthFt(Element e)
    {
        try
        {
            var symbol = (e as FamilyInstance)?.Symbol;
            foreach (var name in new[] { "b", "B", "Width", "width" })
            {
                var p = symbol?.LookupParameter(name) ?? e.LookupParameter(name);
                if (p is not null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 1e-9)
                    return p.AsDouble();
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryColumnSectionFt(Element e, out double bFt, out double hFt)
    {
        bFt = 0;
        hFt = 0;
        try
        {
            if (e is not FamilyInstance fi)
                return false;
            var symbol = fi.Symbol;
            double? b = null;
            double? h = null;
            foreach (var name in new[] { "b", "B", "Width", "width" })
            {
                var p = symbol?.LookupParameter(name) ?? e.LookupParameter(name);
                if (p is not null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 1e-9)
                {
                    b = p.AsDouble();
                    break;
                }
            }
            foreach (var name in new[] { "h", "H", "Depth", "d", "D", "Height", "height" })
            {
                var p = symbol?.LookupParameter(name) ?? e.LookupParameter(name);
                if (p is not null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 1e-9)
                {
                    h = p.AsDouble();
                    break;
                }
            }
            if (b is > 1e-9 && h is > 1e-9)
            {
                bFt = b.Value;
                hFt = h.Value;
                return true;
            }
        }
        catch
        {
            // fall through
        }

        var bb = e.get_BoundingBox(null);
        if (bb is null) return false;
        bFt = bb.Max.X - bb.Min.X;
        hFt = bb.Max.Y - bb.Min.Y;
        return bFt > 1e-9 && hFt > 1e-9;
    }

    private static bool TrySketchMetrics(
        Floor floor,
        out double outerAreaM2,
        out double outerPeriM,
        out List<(XYZ A, XYZ B)> outerEdges,
        out List<(double AreaM2, double PeriM, double Cx, double Cy)> openings)
    {
        outerAreaM2 = 0;
        outerPeriM = 0;
        outerEdges = [];
        openings = [];

        try
        {
            var sketchId = floor.SketchId;
            if (sketchId == ElementId.InvalidElementId)
                return false;
            if (floor.Document.GetElement(sketchId) is not Sketch sketch)
                return false;
            var profile = sketch.Profile;
            if (profile is null || profile.Size == 0)
                return false;

            var outers = new List<(double AreaM2, double PeriM, double Cx, double Cy, List<(XYZ A, XYZ B)> Edges, bool Ccw)>();
            var inners = new List<(double AreaM2, double PeriM, double Cx, double Cy)>();

            foreach (CurveArray arr in profile)
            {
                if (!TryLoopMetricsWithEdges(arr, out var area, out var peri, out var cx, out var cy, out var edges, out var ccw) ||
                    area <= 1e-9)
                    continue;

                // Floor sketch: CCW = outer boundary, CW = opening (Revit convention from above).
                if (ccw)
                    outers.Add((area, peri, cx, cy, edges, true));
                else
                    inners.Add((area, peri, cx, cy));
            }

            // All same winding (multi-boundary plates) → treat every loop as outer.
            if (outers.Count == 0 && inners.Count > 0)
            {
                foreach (CurveArray arr in profile)
                {
                    if (!TryLoopMetricsWithEdges(arr, out var area, out var peri, out var cx, out var cy, out var edges, out _) ||
                        area <= 1e-9)
                        continue;
                    outers.Add((area, peri, cx, cy, edges, true));
                }
                inners.Clear();
            }

            if (outers.Count == 0)
                return false;

            foreach (var o in outers)
            {
                outerAreaM2 += o.AreaM2;
                outerPeriM += o.PeriM;
                outerEdges.AddRange(o.Edges);
            }

            openings.AddRange(inners);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoopMetricsWithEdges(
        CurveArray arr,
        out double areaM2,
        out double periM,
        out double cx,
        out double cy,
        out List<(XYZ A, XYZ B)> edges,
        out bool isCcw)
    {
        areaM2 = 0;
        periM = 0;
        cx = 0;
        cy = 0;
        edges = [];
        isCcw = true;
        var pts = new List<XYZ>();
        foreach (Curve c in arr)
        {
            if (c is null) continue;
            periM += FormworkUnits.FeetToM(c.Length);
            var p0 = c.GetEndPoint(0);
            var p1 = c.GetEndPoint(1);
            edges.Add((p0, p1));
            pts.Add(p0);
        }

        if (pts.Count < 3)
            return false;

        double sum = 0;
        double sx = 0, sy = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
            sx += a.X;
            sy += a.Y;
        }

        isCcw = sum > 0;
        areaM2 = FormworkUnits.SquareFeetToM2(Math.Abs(sum) * 0.5);
        cx = FormworkUnits.FeetToM(sx / pts.Count);
        cy = FormworkUnits.FeetToM(sy / pts.Count);
        return areaM2 > 0;
    }

    /// <summary>
    /// Length of this floor’s outer edges that coincide with another floor at the same level
    /// (abutting plates — no side formwork on the shared joint).
    /// </summary>
    private static double SharedBoundaryWithNeighborFloorsM(
        Document doc,
        Floor floor,
        List<(XYZ A, XYZ B)> outerEdges,
        double slabSoffitZ,
        double zTol,
        double xyTolFt)
    {
        if (outerEdges.Count == 0)
            return 0;

        // Per outer edge: max overlap with any same-level neighbor (no double-count).
        var edgeSharedFt = new double[outerEdges.Count];

        foreach (var el in new FilteredElementCollector(doc)
                     .OfCategory(BuiltInCategory.OST_Floors)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (el.Id == floor.Id || el is not Floor other)
                continue;

            var otherBb = other.get_BoundingBox(null);
            if (otherBb is null) continue;
            if (Math.Abs(otherBb.Min.Z - slabSoffitZ) > zTol * 4)
                continue;

            if (!TrySketchMetrics(other, out _, out _, out var otherEdges, out _))
                continue;
            if (otherEdges.Count == 0)
                continue;

            for (var i = 0; i < outerEdges.Count; i++)
            {
                var (a0, a1) = outerEdges[i];
                var oFt = EdgeOverlapLengthFt(a0, a1, otherEdges, xyTolFt);
                if (oFt > edgeSharedFt[i])
                    edgeSharedFt[i] = oFt;
            }
        }

        double sharedM = 0;
        for (var i = 0; i < outerEdges.Count; i++)
        {
            var (a0, a1) = outerEdges[i];
            var lenFt = Math.Sqrt(
                (a1.X - a0.X) * (a1.X - a0.X) + (a1.Y - a0.Y) * (a1.Y - a0.Y));
            sharedM += FormworkUnits.FeetToM(Math.Min(edgeSharedFt[i], lenFt));
        }

        return sharedM;
    }

    private static double EdgeOverlapLengthFt(
        XYZ a0,
        XYZ a1,
        List<(XYZ A, XYZ B)> others,
        double tolFt)
    {
        var ax = a1.X - a0.X;
        var ay = a1.Y - a0.Y;
        var lenFt = Math.Sqrt(ax * ax + ay * ay);
        if (lenFt < 1e-9)
            return 0;

        double overlapFt = 0;
        foreach (var (b0, b1) in others)
        {
            var o = ColinearSegmentOverlapFt(a0, a1, b0, b1, tolFt);
            if (o > overlapFt)
                overlapFt = o;
        }

        return Math.Min(overlapFt, lenFt);
    }

    private static double ColinearSegmentOverlapFt(
        XYZ a0,
        XYZ a1,
        XYZ b0,
        XYZ b1,
        double tolFt)
    {
        var ax = a1.X - a0.X;
        var ay = a1.Y - a0.Y;
        var aLen = Math.Sqrt(ax * ax + ay * ay);
        if (aLen < 1e-9)
            return 0;
        var ux = ax / aLen;
        var uy = ay / aLen;

        var bx = b1.X - b0.X;
        var by = b1.Y - b0.Y;
        var bLen = Math.Sqrt(bx * bx + by * by);
        if (bLen < 1e-9)
            return 0;

        // Parallel (or anti-parallel) in plan.
        var cross = Math.Abs(ux * (by / bLen) - uy * (bx / bLen));
        if (cross > 0.08)
            return 0;

        // Midpoint of B within tol of line A.
        var mx = (b0.X + b1.X) * 0.5;
        var my = (b0.Y + b1.Y) * 0.5;
        var dx = mx - a0.X;
        var dy = my - a0.Y;
        var dist = Math.Abs(dx * uy - dy * ux);
        if (dist > tolFt)
            return 0;

        // 1D overlap along A's axis.
        double Proj(XYZ p) => (p.X - a0.X) * ux + (p.Y - a0.Y) * uy;
        var t0 = Proj(b0);
        var t1 = Proj(b1);
        var lo = Math.Max(0, Math.Min(t0, t1));
        var hi = Math.Min(aLen, Math.Max(t0, t1));
        return Math.Max(0, hi - lo);
    }

    private static bool TryLoopMetrics(
        CurveArray arr,
        out double areaM2,
        out double periM,
        out double cx,
        out double cy)
    {
        return TryLoopMetricsWithEdges(arr, out areaM2, out periM, out cx, out cy, out _, out _);
    }

    private static double TryBeamLengthUnderSlabM(Element e, BoundingBoxXYZ slabXy)
    {
        try
        {
            if (e.Location is LocationCurve lc)
            {
                var curve = lc.Curve;
                var lenFt = curve.Length;
                var steps = Math.Max(4, (int)Math.Ceiling(lenFt / FormworkUnits.MmToFeet(200)));
                var inside = 0;
                for (var i = 0; i <= steps; i++)
                {
                    var p = curve.Evaluate(i / (double)steps, true);
                    if (p.X >= slabXy.Min.X && p.X <= slabXy.Max.X &&
                        p.Y >= slabXy.Min.Y && p.Y <= slabXy.Max.Y)
                        inside++;
                }

                var ratio = inside / (double)(steps + 1);
                return FormworkUnits.FeetToM(lenFt * ratio);
            }

            var cut = e.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH)?.AsDouble();
            if (cut is > 1e-9)
                return FormworkUnits.FeetToM(cut.Value);
        }
        catch
        {
            // fall through
        }

        return 0;
    }

    private static bool FootprintsOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b, double pad) =>
        a.Min.X - pad <= b.Max.X + pad &&
        a.Max.X + pad >= b.Min.X - pad &&
        a.Min.Y - pad <= b.Max.Y + pad &&
        a.Max.Y + pad >= b.Min.Y - pad;

    /// <summary>
    /// Support touches this slab soffit band:
    /// beam/wall top near soffit; column straddles soffit (or top near soffit).
    /// Uses elevation params first — bbox alone can false-hit adjacent storeys.
    /// </summary>
    private static bool SupportTouchesSlabBand(
        Element support,
        double slabSoffitZ,
        double slabTopZ,
        double zTol)
    {
        var cat = support.Category?.Id.Value ?? 0;
        if (cat == (int)BuiltInCategory.OST_StructuralFraming)
        {
            var topZ = ResolveBeamTopZ(support);
            return topZ >= slabSoffitZ - zTol && topZ <= slabTopZ + zTol;
        }

        if (cat == (int)BuiltInCategory.OST_StructuralColumns)
        {
            var topZ = ResolveColumnTopZ(support);
            var botZ = ResolveColumnBottomZ(support);
            // Top meets soffit, or column passes through slab thickness.
            if (topZ >= slabSoffitZ - zTol && topZ <= slabTopZ + zTol)
                return true;
            return botZ <= slabSoffitZ + zTol && topZ >= slabTopZ - zTol;
        }

        if (cat == (int)BuiltInCategory.OST_Walls && support is Wall wall)
        {
            var bb = wall.get_BoundingBox(null);
            if (bb is null) return false;
            // Wall top near soffit or wall spans through slab.
            return bb.Max.Z >= slabSoffitZ - zTol && bb.Min.Z <= slabTopZ + zTol &&
                   bb.Max.Z >= slabSoffitZ - zTol;
        }

        var box = support.get_BoundingBox(null);
        if (box is null) return false;
        return box.Max.Z >= slabSoffitZ - zTol && box.Min.Z <= slabTopZ + zTol;
    }

    private static double ResolveColumnBottomZ(Element column)
    {
        var bot = column.LookupParameter("Elevation at Bottom")
                  ?? column.get_Parameter(BuiltInParameter.STRUCTURAL_ELEVATION_AT_BOTTOM);
        if (bot is not null && bot.HasValue)
            return bot.AsDouble();

        try
        {
            if (column is FamilyInstance fi)
            {
                var baseLevelId = fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();
                var baseLevel = baseLevelId is not null ? column.Document.GetElement(baseLevelId) as Level : null;
                var baseOff = fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;
                if (baseLevel is not null)
                    return baseLevel.Elevation + baseOff;
            }
        }
        catch
        {
            // fall through
        }

        return column.get_BoundingBox(null)?.Min.Z ?? 0;
    }

    private static double ResolveThicknessM(Floor floor)
    {
        var p = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
        if (p is not null && p.HasValue)
            return FormworkUnits.FeetToM(p.AsDouble());
        var type = floor.Document.GetElement(floor.GetTypeId()) as FloorType;
        var compound = type?.GetCompoundStructure();
        if (compound is not null)
            return FormworkUnits.FeetToM(compound.GetWidth());
        return 0.15;
    }
}
