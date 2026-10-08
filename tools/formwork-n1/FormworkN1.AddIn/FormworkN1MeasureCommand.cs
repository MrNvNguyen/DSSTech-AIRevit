using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QSNormEngine.Core.Formwork;
using QSNormEngine.Revit.Formwork;

namespace FormworkN1.AddIn;

[Transaction(TransactionMode.Manual)]
public sealed class FormworkN1MeasureCommand : IExternalCommand
{
    private const double MaxRelErrorPct = 1.0;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
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
            var evidenceDir = Path.Combine(repoRoot, "docs", "evidence", "formwork-n1", "N1-FORMWORK-GEOMETRY");
            Directory.CreateDirectory(evidenceDir);

            var aprioriPath = Path.Combine(repoRoot, "docs", "evidence", "formwork-n1", "apriori-hand-calc.yaml");
            var apriori = AprioriLoader.TryLoad(aprioriPath);
            var filterIds = apriori?.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.ElementId) && !e.ElementId.StartsWith("PENDING", StringComparison.OrdinalIgnoreCase))
                .Select(e => new ElementId(long.Parse(e.ElementId, CultureInfo.InvariantCulture)))
                .ToList();

            var rulesPath = FormworkRulesPath.Resolve(repoRoot);
            var ruleSet = FormworkRuleSetLoader.Load(rulesPath);
            var rulesSha = FormworkRuleSetLoader.Sha256Hex(rulesPath);
            var rulesRel = Path.GetRelativePath(repoRoot, rulesPath).Replace('\\', '/');

            var batch = FormworkCalculator.Compute(
                doc, ruleSet, filterIds is { Count: > 0 } ? filterIds : null, rulesRel, rulesSha);

            var comparisons = BuildComparisons(apriori, batch);
            var payload = new N1Result
            {
                Schema = "formwork-n1/1",
                Campaign = "N1-FORMWORK-GEOMETRY",
                WaveId = "N1-FORMWORK-GEOMETRY-ATTEMPT-1",
                Authority = "PRODUCT",
                BehaviorChange = true,
                VerifySha = Environment.GetEnvironmentVariable("QS_GIT_SHA") ?? "unknown",
                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                RevitHostYear = uiApp.Application.VersionNumber,
                RevitBuild = uiApp.Application.VersionBuild,
                ModelTitle = doc.Title,
                EpsilonMm = batch.EpsilonMm,
                ElementsProcessed = batch.ElementsProcessed,
                NeighborPairs = batch.NeighborPairs,
                RulesPath = batch.RulesPath,
                RulesSha256 = batch.RulesSha256,
                GroundContactAutoDetect = batch.GroundContactAutoDetect,
                AprioriPath = Path.GetRelativePath(repoRoot, aprioriPath).Replace('\\', '/'),
                AprioriSlabPending = apriori?.Elements.Any(e =>
                    e.Role.Equals("slab", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(e.ElementId) || e.ElementId.StartsWith("PENDING", StringComparison.OrdinalIgnoreCase) ||
                     e.HandTotalM2 is null)) ?? true,
                Elements = batch.Elements.Select(ToElementPayload).ToList(),
                Comparisons = comparisons
            };
            ClassifyRun(payload);

            var resultPath = Path.Combine(evidenceDir, "result.json");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(payload, N1Json.Options), new UTF8Encoding(false));

            TaskDialog.Show(
                "Formwork N1 Measure",
                $"Elements: {batch.ElementsProcessed}\n" +
                $"Neighbor pairs: {batch.NeighborPairs}\n" +
                $"Run class: {payload.RunClass}\n" +
                $"Outcome: {payload.AttemptOutcome}\n" +
                $"Wrote:\n{resultPath}");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.ToString();
            return Result.Failed;
        }
    }

    private static List<ComparisonPayload> BuildComparisons(AprioriData? apriori, FormworkBatchResult batch)
    {
        var byId = batch.Elements.ToDictionary(e => e.ElementId, StringComparer.Ordinal);
        var comparisons = new List<ComparisonPayload>();

        if (apriori is null)
        {
            comparisons.Add(new ComparisonPayload
            {
                Role = "all",
                Status = "NOT_MEASURED",
                Reason = "Missing apriori-hand-calc.yaml"
            });
            return comparisons;
        }

        foreach (var expected in apriori.Elements)
        {
            if (string.IsNullOrWhiteSpace(expected.ElementId) ||
                expected.ElementId.StartsWith("PENDING", StringComparison.OrdinalIgnoreCase) ||
                expected.HandTotalM2 is null)
            {
                comparisons.Add(new ComparisonPayload
                {
                    Role = expected.Role,
                    ElementId = expected.ElementId,
                    HandTotalM2 = expected.HandTotalM2,
                    Status = "NOT_MEASURED",
                    Reason = "A-priori hand calc incomplete (slab pending Revit query)."
                });
                continue;
            }

            if (!byId.TryGetValue(expected.ElementId, out var actual))
            {
                comparisons.Add(new ComparisonPayload
                {
                    Role = expected.Role,
                    ElementId = expected.ElementId,
                    HandTotalM2 = expected.HandTotalM2,
                    Status = "NOT_MEASURED",
                    Reason = "Element not found in FormworkCalculator output."
                });
                continue;
            }

            var errPct = Math.Abs(actual.TotalAreaM2 - expected.HandTotalM2.Value) / expected.HandTotalM2.Value * 100.0;
            comparisons.Add(new ComparisonPayload
            {
                Role = expected.Role,
                ElementId = expected.ElementId,
                HandTotalM2 = expected.HandTotalM2,
                ComputedTotalM2 = actual.TotalAreaM2,
                ErrorPct = errPct,
                Status = errPct <= MaxRelErrorPct ? "PASS" : "FAIL",
                GrossAreaM2 = actual.GrossAreaM2,
                DeductAreaM2 = actual.DeductAreaM2
            });
        }

        return comparisons;
    }

    private static void ClassifyRun(N1Result payload)
    {
        var measured = payload.Comparisons.Where(c => c.Status is "PASS" or "FAIL").ToList();
        if (measured.Count == 0 || payload.AprioriSlabPending)
        {
            payload.RunClass = "INVALID_ENVIRONMENT_RUN";
            payload.AttemptOutcome = "NOT_MEASURED";
            payload.ClassificationReason = payload.AprioriSlabPending
                ? "Slab a-priori hand calc pending — complete apriori-hand-calc.yaml before bar adjudication."
                : "No comparisons measured against a-priori hand calc.";
            return;
        }

        if (measured.Any(c => c.Status == "FAIL"))
        {
            payload.RunClass = "PRODUCT_FAILURE_RUN";
            payload.AttemptOutcome = "FALSIFIED";
            payload.ClassificationReason = "One or more elements exceed 1% vs a-priori hand calc.";
            return;
        }

        payload.RunClass = "PRODUCT_PASS_RUN";
        payload.AttemptOutcome = "PASS";
        payload.ClassificationReason = "All measured elements within 1% of a-priori hand calc.";
    }

    private static ElementPayload ToElementPayload(FormworkResult result) => new()
    {
        ElementId = result.ElementId,
        Role = result.Role.ToString(),
        Kind = result.Kind,
        SegmentIndex = result.SegmentIndex,
        SegmentCount = result.SegmentCount,
        ClipBottomZM = result.ClipBottomZM,
        ClipTopZM = result.ClipTopZM,
        SegmentHeightM = result.SegmentHeightM,
        BboxHeightM = result.BboxHeightM,
        ClipHeightApplied = result.ClipHeightApplied,
        SideAreaM2 = result.SideAreaM2,
        BottomAreaM2 = result.BottomAreaM2,
        TopAreaM2 = result.TopAreaM2,
        GrossAreaM2 = result.GrossAreaM2,
        DeductAreaM2 = result.DeductAreaM2,
        TotalAreaM2 = result.TotalAreaM2,
        GroundContactDropBottom = result.GroundContactDropBottom,
        Status = result.Status
    };
}

internal static class FormworkRulesPath
{
    public static string Resolve(string repoRoot)
    {
        var env = Environment.GetEnvironmentVariable("QS_FORMWORK_RULES_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return Path.GetFullPath(env);

        var config = Path.Combine(repoRoot, "Config", "formwork-rules.json");
        if (File.Exists(config))
            return config;

        throw new FileNotFoundException("Missing Config/formwork-rules.json", config);
    }
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
                .FirstOrDefault(x => x.Length > 0 && !x.StartsWith('#'));
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

        const string known = @"D:\DEV\QSNormEngine_Revit";
        if (IsRepoRoot(known))
            return known;

        throw new InvalidOperationException(
            "Could not locate QSNormEngine repo root. Re-run tools/formwork-n1/run-n1-measure.ps1 -Install " +
            "or set QS_NORMENGINE_ROOT.");
    }

    private static bool IsRepoRoot(string path) =>
        Directory.Exists(Path.Combine(path, "docs", "evidence")) &&
        Directory.Exists(Path.Combine(path, "tools", "formwork-n1"));
}

internal sealed class AprioriData
{
    public List<AprioriElement> Elements { get; set; } = [];
}

internal sealed class AprioriElement
{
    public string Role { get; set; } = "";
    public string ElementId { get; set; } = "";
    public int? SegmentIndex { get; set; }
    public double? HandTotalM2 { get; set; }
}

internal static class AprioriLoader
{
    public static AprioriData? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;

        var data = new AprioriData();
        AprioriElement? current = null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("- role:", StringComparison.Ordinal))
            {
                current = new AprioriElement { Role = line["- role:".Length..].Trim() };
                data.Elements.Add(current);
                continue;
            }

            if (current is null) continue;

            if (line.StartsWith("element_id:", StringComparison.Ordinal))
                current.ElementId = StripComment(line["element_id:".Length..]).Trim().Trim('"');
            else if (line.StartsWith("segment_index:", StringComparison.Ordinal))
            {
                var token = StripComment(line["segment_index:".Length..]);
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seg))
                    current.SegmentIndex = seg;
            }
            else if (line.StartsWith("hand_total_m2:", StringComparison.Ordinal))
            {
                var token = StripComment(line["hand_total_m2:".Length..]);
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var area))
                    current.HandTotalM2 = area;
            }
        }

        return data;
    }

    private static string StripComment(string raw)
    {
        var s = raw.Trim();
        var hash = s.IndexOf('#');
        if (hash >= 0) s = s[..hash];
        return s.Trim();
    }
}

internal sealed class N1Result
{
    public string Schema { get; set; } = "";
    public string Campaign { get; set; } = "";
    public string WaveId { get; set; } = "";
    public string Authority { get; set; } = "";
    public string RunClass { get; set; } = "INVALID_ENVIRONMENT_RUN";
    public string AttemptOutcome { get; set; } = "NOT_MEASURED";
    public string? ClassificationReason { get; set; }
    public bool BehaviorChange { get; set; }
    public string VerifySha { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
    public string? RevitHostYear { get; set; }
    public string? RevitBuild { get; set; }
    public string ModelTitle { get; set; } = "";
    public double EpsilonMm { get; set; }
    public int ElementsProcessed { get; set; }
    public int NeighborPairs { get; set; }
    public string? RulesPath { get; set; }
    public string? RulesSha256 { get; set; }
    public bool GroundContactAutoDetect { get; set; }
    public string AprioriPath { get; set; } = "";
    public bool AprioriSlabPending { get; set; }
    public List<ElementPayload> Elements { get; set; } = [];
    public List<ComparisonPayload> Comparisons { get; set; } = [];
}

internal sealed class ElementPayload
{
    public string ElementId { get; set; } = "";
    public string Role { get; set; } = "";
    public string? Kind { get; set; }
    public int? SegmentIndex { get; set; }
    public int? SegmentCount { get; set; }
    public double? ClipBottomZM { get; set; }
    public double? ClipTopZM { get; set; }
    public double? SegmentHeightM { get; set; }
    public double? BboxHeightM { get; set; }
    public bool ClipHeightApplied { get; set; }
    public double SideAreaM2 { get; set; }
    public double BottomAreaM2 { get; set; }
    public double TopAreaM2 { get; set; }
    public double GrossAreaM2 { get; set; }
    public double DeductAreaM2 { get; set; }
    public double TotalAreaM2 { get; set; }
    public bool GroundContactDropBottom { get; set; }
    public string Status { get; set; } = "OK";
}

internal sealed class ComparisonPayload
{
    public string Role { get; set; } = "";
    public string? ElementId { get; set; }
    public double? HandTotalM2 { get; set; }
    public double? ComputedTotalM2 { get; set; }
    public double? GrossAreaM2 { get; set; }
    public double? DeductAreaM2 { get; set; }
    public double? ErrorPct { get; set; }
    public string Status { get; set; } = "NOT_MEASURED";
    public string? Reason { get; set; }
}

internal static class N1Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
