using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FormworkSpike.AddIn;

[Transaction(TransactionMode.Manual)]
public sealed class FormworkSpikeCommand : IExternalCommand
{
    private const double VolumeToleranceFt3 = 1e-9;
    private const double EpsilonMm = 5.0;
    private static readonly int[] BatchSizes = [100, 1000, 10000];

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // READ-ONLY spike: no SetParameter, no model write-back.
        try
        {
            var uiApp = commandData.Application;
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc is null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uidoc.Document;
            var repoRoot = RepoRootLocator.Find();
            var evidenceDir = Path.Combine(repoRoot, "docs", "evidence", "formwork-spike", "N0-FORMWORK-SPIKE");
            Directory.CreateDirectory(evidenceDir);

            var fixture = FixtureLoader.TryLoad(Path.Combine(repoRoot, "tools", "formwork-spike", "fixtures", "hand-calc-beam-column.yaml"));
            var selectionIds = uidoc.Selection.GetElementIds().ToList();
            var structural = CollectStructuralElements(doc);
            var options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = true,
                IncludeNonVisibleObjects = false
            };

            var payload = new SpikeResult
            {
                Schema = "formwork-spike/1",
                Campaign = "N0-FORMWORK-SPIKE",
                WaveId = "N0-FORMWORK-SPIKE-ATTEMPT-1",
                Authority = "DIAGNOSTIC",
                RunClass = "PRODUCT_FAILURE_RUN",
                AttemptOutcome = "NOT_MEASURED",
                BehaviorChange = false,
                VerifySha = Environment.GetEnvironmentVariable("QS_GIT_SHA") ?? "unknown",
                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                RevitHostYear = uiApp.Application.VersionNumber,
                RevitBuild = uiApp.Application.VersionBuild,
                ModelTitle = doc.Title,
                StructuralElementCount = structural.Count,
                GeometryTiming = MeasureGeometryTiming(structural, options),
                BooleanTiming = MeasureBooleanTiming(doc, structural, options),
                ProbeEpsilon = MeasureProbeEpsilon(doc, selectionIds, fixture, options)
            };
            ClassifyRun(payload);

            var resultPath = Path.Combine(evidenceDir, "result.json");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(payload, SpikeJson.Options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            TaskDialog.Show(
                "Formwork Spike N0",
                $"Structural elements: {structural.Count}\n" +
                $"Boolean fail rate: {FormatRate(payload.BooleanTiming.FailRate)}\n" +
                $"Probe: {payload.ProbeEpsilon.Status}\n" +
                $"Wrote:\n{resultPath}");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.ToString();
            return Result.Failed;
        }
    }

    private static List<Element> CollectStructuralElements(Document doc)
    {
        var categories = new[]
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_StructuralFoundation
        };

        var result = new List<Element>();
        foreach (var cat in categories)
        {
            result.AddRange(new FilteredElementCollector(doc)
                .OfCategory(cat)
                .WhereElementIsNotElementType()
                .ToElements());
        }

        result.AddRange(new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(IsStructuralWall));

        return result
            .GroupBy(e => e.Id.Value)
            .Select(g => g.First())
            .ToList();
    }

    private static bool IsStructuralWall(Element element)
    {
        if (element is not Wall wall) return false;
        // BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM: 0 = NonBearing, 1 = Bearing, 2 = Shear
        var usage = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM)?.AsInteger();
        return usage is not null && usage.Value != 0;
    }

    private static GeometryTimingSection MeasureGeometryTiming(IReadOnlyList<Element> elements, Options options)
    {
        var section = new GeometryTimingSection { BatchSizes = BatchSizes.ToList() };
        foreach (var batchSize in BatchSizes)
        {
            var count = Math.Min(batchSize, elements.Count);
            if (count == 0)
            {
                section.Samples.Add(new GeometryTimingSample
                {
                    BatchSize = batchSize,
                    ElementsTimed = 0,
                    TotalMs = 0,
                    AvgMsPerElement = null,
                    Status = "NOT_MEASURED",
                    Reason = "No structural elements in model."
                });
                continue;
            }

            var sw = Stopwatch.StartNew();
            var solidsFound = 0;
            for (var i = 0; i < count; i++)
            {
                solidsFound += ExtractSolids(elements[i], options).Count;
            }
            sw.Stop();
            section.Samples.Add(new GeometryTimingSample
            {
                BatchSize = batchSize,
                ElementsTimed = count,
                SolidsFound = solidsFound,
                TotalMs = sw.Elapsed.TotalMilliseconds,
                AvgMsPerElement = sw.Elapsed.TotalMilliseconds / count,
                Status = "MEASURED"
            });
        }

        return section;
    }

    private static BooleanTimingSection MeasureBooleanTiming(Document doc, IReadOnlyList<Element> elements, Options options)
    {
        var section = new BooleanTimingSection();
        if (elements.Count < 2)
        {
            section.Status = "NOT_MEASURED";
            section.Reason = "Need at least two structural elements for neighbor boolean timing.";
            return section;
        }

        var solidsById = new Dictionary<long, Solid>();
        var bboxById = new Dictionary<long, BoundingBoxXYZ>();
        foreach (var element in elements)
        {
            var solids = ExtractSolids(element, options);
            var merged = MergeSolids(solids);
            if (merged is null) continue;
            solidsById[element.Id.Value] = merged;
            bboxById[element.Id.Value] = element.get_BoundingBox(null) ?? merged.GetBoundingBox();
        }

        var pairs = new HashSet<(long, long)>();
        var ids = solidsById.Keys.OrderBy(x => x).ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                var a = ids[i];
                var b = ids[j];
                if (!BoundingBoxesIntersect(bboxById[a], bboxById[b])) continue;
                if (AreJoined(doc, a, b) || BoundingBoxesIntersect(bboxById[a], bboxById[b], 0.05))
                    pairs.Add((a, b));
            }
        }

        section.PairsConsidered = pairs.Count;
        if (pairs.Count == 0)
        {
            section.Status = "NOT_MEASURED";
            section.Reason = "No neighbor pairs with intersecting bbox or join.";
            return section;
        }

        var sw = Stopwatch.StartNew();
        foreach (var (a, b) in pairs)
        {
            section.PairsAttempted++;
            if (TryBooleanDifference(solidsById[a], solidsById[b], out var reason))
                section.PairsSucceeded++;
            else
            {
                section.PairsFailed++;
                if (!string.IsNullOrWhiteSpace(reason))
                    section.FailureReasons.Add(reason);
            }
        }
        sw.Stop();

        section.TotalMs = sw.Elapsed.TotalMilliseconds;
        section.AvgMsPerOp = section.PairsAttempted > 0 ? sw.Elapsed.TotalMilliseconds / section.PairsAttempted : null;
        section.FailRate = section.PairsAttempted > 0 ? (double)section.PairsFailed / section.PairsAttempted : null;
        section.Status = "MEASURED";
        return section;
    }

    private static ProbeEpsilonSection MeasureProbeEpsilon(
        Document doc,
        IReadOnlyList<ElementId> selectionIds,
        FixtureData? fixture,
        Options options)
    {
        var section = new ProbeEpsilonSection
        {
            EpsilonMm = EpsilonMm,
            HandAreaM2 = fixture?.HandAreaM2
        };

        var probeIds = ResolveProbeElementIds(doc, selectionIds, fixture, options);
        section.ElementIds = probeIds.Select(id => id.Value.ToString(CultureInfo.InvariantCulture)).ToList();

        if (probeIds.Count != 2)
        {
            section.Status = "NOT_MEASURED";
            section.Reason = probeIds.Count == 0
                ? "Select exactly two joined elements or populate element_ids in hand-calc-beam-column.yaml."
                : $"Probe requires exactly two elements; got {probeIds.Count}.";
            return section;
        }

        var a = doc.GetElement(probeIds[0]);
        var b = doc.GetElement(probeIds[1]);
        if (a is null || b is null)
        {
            section.Status = "NOT_MEASURED";
            section.Reason = "Probe element ids not found in active document.";
            return section;
        }

        var solidA = MergeSolids(ExtractSolids(a, options));
        var solidB = MergeSolids(ExtractSolids(b, options));
        if (solidA is null || solidB is null)
        {
            section.Status = "NOT_MEASURED";
            section.Reason = "Could not extract valid solids for probe pair.";
            return section;
        }

        section.VolumeA_M3 = CubicFeetToM3(solidA.Volume);
        section.VolumeB_M3 = CubicFeetToM3(solidB.Volume);
        section.DifferenceAbSucceeded = TryBoolean(solidA, solidB, BooleanOperationsType.Difference) is not null;
        section.DifferenceBaSucceeded = TryBoolean(solidB, solidA, BooleanOperationsType.Difference) is not null;

        // Joined Revit solids usually abut (no volume overlap). Use face-extrusion probe.
        var epsFt = EpsilonMm / 304.8;
        var contactFt2 = EstimateContactByFaceProbe(solidA, solidB, epsFt);
        if (contactFt2 is null || contactFt2 <= VolumeToleranceFt3)
            contactFt2 = EstimateContactByFaceProbe(solidB, solidA, epsFt);

        if (contactFt2 is null || contactFt2 <= VolumeToleranceFt3)
        {
            // Selection may be non-touching; try an auto-discovered joined column+beam once.
            var auto = FindJoinedColumnBeamPair(doc, options);
            if (auto is { Count: 2 } &&
                !(auto[0] == probeIds[0] && auto[1] == probeIds[1]) &&
                !(auto[0] == probeIds[1] && auto[1] == probeIds[0]))
            {
                var a2 = doc.GetElement(auto[0]);
                var b2 = doc.GetElement(auto[1]);
                var s2a = a2 is null ? null : MergeSolids(ExtractSolids(a2, options));
                var s2b = b2 is null ? null : MergeSolids(ExtractSolids(b2, options));
                if (s2a is not null && s2b is not null)
                {
                    contactFt2 = EstimateContactByFaceProbe(s2a, s2b, epsFt)
                                 ?? EstimateContactByFaceProbe(s2b, s2a, epsFt);
                    if (contactFt2 is > VolumeToleranceFt3)
                    {
                        section.ElementIds = auto.Select(id => id.Value.ToString(CultureInfo.InvariantCulture)).ToList();
                        section.VolumeA_M3 = CubicFeetToM3(s2a.Volume);
                        section.VolumeB_M3 = CubicFeetToM3(s2b.Volume);
                        section.DifferenceAbSucceeded = TryBoolean(s2a, s2b, BooleanOperationsType.Difference) is not null;
                        section.DifferenceBaSucceeded = TryBoolean(s2b, s2a, BooleanOperationsType.Difference) is not null;
                    }
                }
            }
        }

        if (contactFt2 is null || contactFt2 <= VolumeToleranceFt3)
        {
            section.Status = "NOT_MEASURED";
            section.Reason =
                "Face-extrusion probe found no contact. Clear selection or select a joined column+beam, then re-run.";
            section.ComputedAreaM2 = 0;
            return section;
        }

        section.ComputedAreaM2 = CubicFeetToM2(contactFt2.Value);
        if (section.HandAreaM2 is > 0)
            section.ErrorPct = Math.Abs(section.ComputedAreaM2.Value - section.HandAreaM2.Value) / section.HandAreaM2.Value * 100.0;

        section.Status = "MEASURED";
        section.Method = "face_extrusion_difference";
        return section;
    }

    /// <summary>
    /// Extrude each planar face by eps into space; volume lost after Difference with neighbor ≈ contact area × eps.
    /// </summary>
    private static double? EstimateContactByFaceProbe(Solid host, Solid neighbor, double epsFt)
    {
        double bestFt2 = 0;
        foreach (Face face in host.Faces)
        {
            try
            {
                if (face is not PlanarFace planar) continue;
                var loops = planar.GetEdgesAsCurveLoops();
                if (loops is null || loops.Count == 0) continue;

                var probe = GeometryCreationUtilities.CreateExtrusionGeometry(loops, planar.FaceNormal, epsFt);
                if (probe is null || probe.Volume <= VolumeToleranceFt3) continue;

                var after = TryBoolean(probe, neighbor, BooleanOperationsType.Difference);
                if (after is null) continue;

                var lost = probe.Volume - after.Volume;
                if (lost <= VolumeToleranceFt3) continue;

                var areaFt2 = lost / epsFt;
                if (areaFt2 > bestFt2)
                    bestFt2 = areaFt2;
            }
            catch
            {
                // Skip non-extrudable faces.
            }
        }

        return bestFt2 > 0 ? bestFt2 : null;
    }

    /// <summary>
    /// Gate: boolean fail &lt; 5% at measured batch + probe error ≤ 2% when measured.
    /// Incomplete metrics → NOT_MEASURED (does not claim product PASS).
    /// </summary>
    private static void ClassifyRun(SpikeResult payload)
    {
        var booleanMeasured = string.Equals(payload.BooleanTiming.Status, "MEASURED", StringComparison.OrdinalIgnoreCase)
                              && payload.BooleanTiming.FailRate is not null;
        var probeMeasured = string.Equals(payload.ProbeEpsilon.Status, "MEASURED", StringComparison.OrdinalIgnoreCase)
                            && payload.ProbeEpsilon.ErrorPct is not null;
        var geomMeasured = payload.GeometryTiming.Samples.Any(s =>
            string.Equals(s.Status, "MEASURED", StringComparison.OrdinalIgnoreCase) && s.BatchSize == 100);

        if (!booleanMeasured || !probeMeasured || !geomMeasured)
        {
            payload.RunClass = "INVALID_ENVIRONMENT_RUN";
            payload.AttemptOutcome = "NOT_MEASURED";
            payload.ClassificationReason = "Missing required measured bars (boolean N-pairs, probe error, or geometry N=100).";
            return;
        }

        var failRate = payload.BooleanTiming.FailRate!.Value;
        var probeErr = payload.ProbeEpsilon.ErrorPct!.Value;
        if (failRate >= 0.05 || probeErr > 2.0)
        {
            payload.RunClass = "PRODUCT_FAILURE_RUN";
            payload.AttemptOutcome = "FALSIFIED";
            payload.ClassificationReason =
                $"Gate fail: boolean_fail_rate={failRate:P2}, probe_error_pct={probeErr:F2} (bars: fail&lt;5%, probe≤2%).";
            return;
        }

        payload.RunClass = "PRODUCT_PASS_RUN";
        payload.AttemptOutcome = "PASS";
        payload.ClassificationReason =
            $"Bars met: boolean_fail_rate={failRate:P2}, probe_error_pct={probeErr:F2}.";
    }

    private static List<ElementId> ResolveProbeElementIds(
        Document doc,
        IReadOnlyList<ElementId> selectionIds,
        FixtureData? fixture,
        Options options)
    {
        if (selectionIds.Count == 2)
            return selectionIds.ToList();

        if (fixture?.ElementIds is { Count: 2 })
        {
            return fixture.ElementIds
                .Select(id => new ElementId(long.Parse(id, CultureInfo.InvariantCulture)))
                .ToList();
        }

        return FindJoinedColumnBeamPair(doc, options) ?? [];
    }

    private static List<ElementId>? FindJoinedColumnBeamPair(Document doc, Options options)
    {
        var columns = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType()
            .ToElements();
        var beams = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .ToElements();

        foreach (var column in columns)
        {
            foreach (var beam in beams)
            {
                try
                {
                    if (JoinGeometryUtils.AreElementsJoined(doc, column, beam))
                        return [column.Id, beam.Id];
                }
                catch
                {
                    // Ignore join query failures.
                }

                var sa = MergeSolids(ExtractSolids(column, options));
                var sb = MergeSolids(ExtractSolids(beam, options));
                if (sa is null || sb is null) continue;
                var bbA = sa.GetBoundingBox();
                var bbB = sb.GetBoundingBox();
                if (!BoundingBoxesIntersect(bbA, bbB, 0.02)) continue;
                var contact = EstimateContactByFaceProbe(sa, sb, EpsilonMm / 304.8);
                if (contact is > VolumeToleranceFt3)
                    return [column.Id, beam.Id];
            }
        }

        return null;
    }

    private static List<Solid> ExtractSolids(Element element, Options options)
    {
        var geometry = element.get_Geometry(options);
        if (geometry is null) return [];
        var solids = new List<Solid>();
        CollectSolids(geometry, solids);
        return solids.Where(s => s.Volume > VolumeToleranceFt3).ToList();
    }

    private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
    {
        foreach (var obj in geometry)
        {
            switch (obj)
            {
                case Solid solid when solid.Volume > VolumeToleranceFt3:
                    solids.Add(solid);
                    break;
                case GeometryInstance instance:
                    var instGeom = instance.GetInstanceGeometry();
                    if (instGeom is not null) CollectSolids(instGeom, solids);
                    break;
            }
        }
    }

    private static Solid? MergeSolids(IReadOnlyList<Solid> solids)
    {
        Solid? merged = null;
        foreach (var solid in solids)
        {
            if (merged is null)
            {
                merged = solid;
                continue;
            }

            merged = TryBoolean(merged, solid, BooleanOperationsType.Union) ?? merged;
        }

        return merged is { Volume: > VolumeToleranceFt3 } ? merged : null;
    }

    private static bool TryBooleanDifference(Solid a, Solid b, out string? reason)
    {
        reason = null;
        try
        {
            var result = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, BooleanOperationsType.Difference);
            if (result.Volume <= VolumeToleranceFt3)
            {
                reason = "Difference returned empty solid.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name;
            return false;
        }
    }

    private static Solid? TryBoolean(Solid a, Solid b, BooleanOperationsType op)
    {
        try
        {
            var result = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, op);
            return result.Volume > VolumeToleranceFt3 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool AreJoined(Document doc, long idA, long idB)
    {
        var a = doc.GetElement(new ElementId(idA));
        var b = doc.GetElement(new ElementId(idB));
        if (a is null || b is null) return false;

        try
        {
            var joinedA = JoinGeometryUtils.GetJoinedElements(doc, a);
            if (joinedA.Any(x => x.Value == idB)) return true;
            var joinedB = JoinGeometryUtils.GetJoinedElements(doc, b);
            return joinedB.Any(x => x.Value == idA);
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

    private static double CubicFeetToM3(double ft3) => ft3 * 0.028316846592;
    private static double CubicFeetToM2(double ft2Proxy) => ft2Proxy * 0.09290304;

    private static string FormatRate(double? rate) =>
        rate is null ? "n/a" : $"{rate.Value * 100:F1}%";
}

internal static class RepoRootLocator
{
    public static string Find()
    {
        var env = Environment.GetEnvironmentVariable("QS_NORMENGINE_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && IsRepoRoot(env))
            return Path.GetFullPath(env);

        var marker = Path.Combine(AppContext.BaseDirectory, "repo-root.txt");
        if (File.Exists(marker))
        {
            var line = File.ReadAllLines(marker)
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(line) && IsRepoRoot(line))
                return Path.GetFullPath(line);
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (IsRepoRoot(dir.FullName))
                return dir.FullName;
            dir = dir.Parent;
        }

        // Dev machine fallback used by this campaign.
        const string known = @"D:\DEV\QSNormEngine_Revit";
        if (IsRepoRoot(known))
            return known;

        throw new InvalidOperationException(
            "Could not locate QSNormEngine repo root. Re-run tools/formwork-spike/run-spike.ps1 -Install " +
            "or set QS_NORMENGINE_ROOT.");
    }

    private static bool IsRepoRoot(string path) =>
        Directory.Exists(Path.Combine(path, "docs", "evidence")) &&
        File.Exists(Path.Combine(path, "tools", "formwork-spike", "README.md"));
}

internal sealed class FixtureData
{
    public List<string> ElementIds { get; set; } = [];
    public double? HandAreaM2 { get; set; }
}

internal static class FixtureLoader
{
    public static FixtureData? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;

        var data = new FixtureData();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("hand_area_m2:", StringComparison.Ordinal))
            {
                var token = line["hand_area_m2:".Length..].Trim();
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var area))
                    data.HandAreaM2 = area;
            }
            else if (line.StartsWith("- id:", StringComparison.Ordinal))
            {
                var token = line["- id:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(token))
                    data.ElementIds.Add(token);
            }
            else if (line.StartsWith("element_ids:", StringComparison.Ordinal) && line.Contains('['))
            {
                // empty yaml list — ignore
            }
        }

        return data;
    }
}

internal sealed class SpikeResult
{
    public string Schema { get; set; } = "";
    public string Campaign { get; set; } = "";
    public string WaveId { get; set; } = "";
    public string Authority { get; set; } = "";
    public string RunClass { get; set; } = "";
    public string AttemptOutcome { get; set; } = "";
    public string? ClassificationReason { get; set; }
    public bool BehaviorChange { get; set; }
    public string VerifySha { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
    public string? RevitHostYear { get; set; }
    public string? RevitBuild { get; set; }
    public string ModelTitle { get; set; } = "";
    public int StructuralElementCount { get; set; }
    public GeometryTimingSection GeometryTiming { get; set; } = new();
    public BooleanTimingSection BooleanTiming { get; set; } = new();
    public ProbeEpsilonSection ProbeEpsilon { get; set; } = new();
}

internal sealed class GeometryTimingSection
{
    public List<int> BatchSizes { get; set; } = [];
    public List<GeometryTimingSample> Samples { get; set; } = [];
}

internal sealed class GeometryTimingSample
{
    public int BatchSize { get; set; }
    public int ElementsTimed { get; set; }
    public int SolidsFound { get; set; }
    public double TotalMs { get; set; }
    public double? AvgMsPerElement { get; set; }
    public string Status { get; set; } = "MEASURED";
    public string? Reason { get; set; }
}

internal sealed class BooleanTimingSection
{
    public string Status { get; set; } = "MEASURED";
    public string? Reason { get; set; }
    public int PairsConsidered { get; set; }
    public int PairsAttempted { get; set; }
    public int PairsSucceeded { get; set; }
    public int PairsFailed { get; set; }
    public double? FailRate { get; set; }
    public double TotalMs { get; set; }
    public double? AvgMsPerOp { get; set; }
    public List<string> FailureReasons { get; set; } = [];
}

internal sealed class ProbeEpsilonSection
{
    public string Status { get; set; } = "NOT_MEASURED";
    public string? Reason { get; set; }
    public string? Method { get; set; }
    public double EpsilonMm { get; set; }
    public List<string> ElementIds { get; set; } = [];
    public double? HandAreaM2 { get; set; }
    public double? ComputedAreaM2 { get; set; }
    public double? ErrorPct { get; set; }
    public double? VolumeA_M3 { get; set; }
    public double? VolumeB_M3 { get; set; }
    public bool DifferenceAbSucceeded { get; set; }
    public bool DifferenceBaSucceeded { get; set; }
}

internal static class SpikeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
