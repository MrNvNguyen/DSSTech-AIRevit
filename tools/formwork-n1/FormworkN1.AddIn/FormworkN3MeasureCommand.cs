using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSSTech.Core.Formwork;
using DSSTech.Revit.Formwork;

namespace FormworkN1.AddIn;

/// <summary>
/// N3 measurement harness — height clip; dual-run clipHeight on/off toggle proof in one click.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class FormworkN3MeasureCommand : IExternalCommand
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
            var evidenceDir = Path.Combine(repoRoot, "docs", "evidence", "formwork-n3", "N3-FORMWORK-CLIP");
            var toggleDir = Path.Combine(repoRoot, "docs", "evidence", "formwork-n3", "rules-toggle");
            Directory.CreateDirectory(evidenceDir);
            Directory.CreateDirectory(toggleDir);

            var aprioriPath = Path.Combine(repoRoot, "docs", "evidence", "formwork-n3", "apriori-hand-calc.yaml");
            var apriori = AprioriLoader.TryLoad(aprioriPath);
            var filterIds = apriori?.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.ElementId) && !e.ElementId.StartsWith("PENDING", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.ElementId)
                .Distinct(StringComparer.Ordinal)
                .Select(id => new ElementId(long.Parse(id, CultureInfo.InvariantCulture)))
                .ToList();

            var rulesPath = FormworkRulesPath.Resolve(repoRoot);
            var ruleSetOn = FormworkRuleSetLoader.Load(rulesPath);
            EnsureColumnClipHeight(ruleSetOn, true);
            var rulesShaOn = FormworkRuleSetLoader.Sha256Hex(rulesPath);
            var rulesRel = Path.GetRelativePath(repoRoot, rulesPath).Replace('\\', '/');

            var filter = filterIds is { Count: > 0 } ? filterIds : null;
            var batchOn = FormworkCalculator.Compute(doc, ruleSetOn, filter, rulesRel, rulesShaOn);

            // Dual-run: clipHeight false via temp JSON (no rebuild) for toggle bar.
            var (batchOff, rulesShaOff) = ComputeWithClipHeightOff(doc, rulesPath, rulesRel, filter);

            var comparisons = BuildComparisons(apriori, batchOn);
            var payload = BuildPayload(uiApp, doc, batchOn, comparisons, aprioriPath, repoRoot);
            ClassifyRun(payload, batchOn);

            var resultPath = Path.Combine(evidenceDir, "result.json");
            var json = JsonSerializer.Serialize(payload, N1Json.Options);
            File.WriteAllText(resultPath, json, new UTF8Encoding(false));

            var payloadOff = BuildPayload(uiApp, doc, batchOff, BuildComparisons(null, batchOff), aprioriPath, repoRoot);
            payloadOff.AttemptOutcome = "TOGGLE_BASELINE";
            payloadOff.RunClass = "PRODUCT_OBSERVE_RUN";
            payloadOff.ClassificationReason = "clipHeight=false dual-run baseline (no apriori bar).";

            var toggledPath = Path.Combine(toggleDir, "toggled-result.json");
            var baselinePath = Path.Combine(toggleDir, "baseline-result.json");
            File.WriteAllText(toggledPath, json, new UTF8Encoding(false));
            File.WriteAllText(baselinePath, JsonSerializer.Serialize(payloadOff, N1Json.Options), new UTF8Encoding(false));

            var compare = BuildCompareSummary(payloadOff, payload, rulesShaOff, rulesShaOn, repoRoot);
            var comparePath = Path.Combine(toggleDir, "compare-summary.json");
            File.WriteAllText(comparePath, compare.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

            var segCount = batchOn.Elements.Count(e => e.ClipHeightApplied);
            var toggleBar = compare["json_toggle_bar"]?.GetValue<string>() ?? "UNKNOWN";
            TaskDialog.Show(
                "Formwork N3 Measure",
                $"Elements/rows: {batchOn.ElementsProcessed}\n" +
                $"Clip segments: {segCount}\n" +
                $"Neighbor pairs: {batchOn.NeighborPairs}\n" +
                $"clipHeight Column: True (dual-run off also written)\n" +
                $"JSON toggle: {toggleBar}\n" +
                $"Rules SHA: {rulesShaOn.Substring(0, Math.Min(12, rulesShaOn.Length))}…\n" +
                $"Run class: {payload.RunClass}\n" +
                $"Outcome: {payload.AttemptOutcome}\n" +
                $"Wrote:\n{resultPath}\n+ rules-toggle/");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.ToString();
            return Result.Failed;
        }
    }

    private static (FormworkBatchResult Batch, string Sha) ComputeWithClipHeightOff(
        Document doc,
        string rulesPath,
        string rulesRel,
        IReadOnlyCollection<ElementId>? filter)
    {
        var node = JsonNode.Parse(File.ReadAllText(rulesPath))
                   ?? throw new InvalidDataException("formwork-rules.json invalid");
        if (node["rules"] is JsonArray rules)
        {
            foreach (var rule in rules)
            {
                if (rule is null) continue;
                var kind = rule["kind"]?.GetValue<string>();
                if (string.Equals(kind, "Column", StringComparison.OrdinalIgnoreCase))
                    rule["clipHeight"] = false;
            }
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "qsnorm-formwork-rules-clip-off.json");
        File.WriteAllText(tempPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        var ruleSetOff = FormworkRuleSetLoader.Load(tempPath);
        EnsureColumnClipHeight(ruleSetOff, false);
        var shaOff = FormworkRuleSetLoader.Sha256Hex(tempPath);
        var batchOff = FormworkCalculator.Compute(
            doc, ruleSetOff, filter, rulesRel + "#clipHeight=false", shaOff);
        return (batchOff, shaOff);
    }

    private static void EnsureColumnClipHeight(FormworkRuleSet ruleSet, bool clipHeight)
    {
        foreach (var rule in ruleSet.Rules)
        {
            if (string.Equals(rule.Kind, "Column", StringComparison.OrdinalIgnoreCase))
                rule.ClipHeight = clipHeight;
        }
    }

    private static JsonObject BuildCompareSummary(
        N1Result baseline,
        N1Result toggled,
        string shaOff,
        string shaOn,
        string repoRoot)
    {
        var byBase = baseline.Elements.ToDictionary(e => e.ElementId, StringComparer.Ordinal);
        var deltas = new JsonArray();
        foreach (var t in toggled.Elements)
        {
            if (!byBase.TryGetValue(t.ElementId, out var b)) continue;
            var dt = t.TotalAreaM2 - b.TotalAreaM2;
            var dClip = t.ClipHeightApplied != b.ClipHeightApplied;
            if (Math.Abs(dt) > 1e-9 || dClip)
            {
                deltas.Add(new JsonObject
                {
                    ["element_id"] = t.ElementId,
                    ["delta_total_area_m2"] = dt,
                    ["baseline_total_m2"] = b.TotalAreaM2,
                    ["toggled_total_m2"] = t.TotalAreaM2,
                    ["baseline_clip_height_applied"] = b.ClipHeightApplied,
                    ["toggled_clip_height_applied"] = t.ClipHeightApplied
                });
            }
        }

        return new JsonObject
        {
            ["schema"] = "formwork-n3-toggle-compare/1",
            ["baseline"] = "docs/evidence/formwork-n3/rules-toggle/baseline-result.json",
            ["toggled"] = "docs/evidence/formwork-n3/rules-toggle/toggled-result.json",
            ["baseline_rules_sha256"] = shaOff,
            ["toggled_rules_sha256"] = shaOn,
            ["sha_changed"] = shaOff != shaOn,
            ["elements_with_delta"] = deltas.Count,
            ["deltas"] = deltas,
            ["json_toggle_bar"] = deltas.Count > 0 ? "PASS" : "FAIL",
            ["repo_root"] = repoRoot
        };
    }

    private static N1Result BuildPayload(
        UIApplication uiApp,
        Document doc,
        FormworkBatchResult batch,
        List<ComparisonPayload> comparisons,
        string aprioriPath,
        string repoRoot) => new()
    {
        Schema = "formwork-n3/1",
        Campaign = "N3-FORMWORK-CLIP",
        WaveId = "N3-FORMWORK-CLIP-ATTEMPT-1",
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
        AprioriPath = File.Exists(aprioriPath)
            ? Path.GetRelativePath(repoRoot, aprioriPath).Replace('\\', '/')
            : "docs/evidence/formwork-n3/apriori-hand-calc.yaml",
        AprioriSlabPending = false,
        Elements = batch.Elements.Select(ToElementPayload).ToList(),
        Comparisons = comparisons
    };

    private static List<ComparisonPayload> BuildComparisons(AprioriData? apriori, FormworkBatchResult batch)
    {
        var comparisons = new List<ComparisonPayload>();
        if (apriori is null || apriori.Elements.Count == 0)
        {
            comparisons.Add(new ComparisonPayload
            {
                Role = "all",
                Status = "NOT_MEASURED",
                Reason = "Missing or empty docs/evidence/formwork-n3/apriori-hand-calc.yaml"
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
                    Reason = expected.ElementId.StartsWith("PENDING", StringComparison.OrdinalIgnoreCase)
                        ? "Fixture PENDING — fill story-column element ids in apriori."
                        : "A-priori incomplete."
                });
                continue;
            }

            var actual = batch.Elements.FirstOrDefault(e =>
                e.ElementId == expected.ElementId &&
                (expected.SegmentIndex is null || e.SegmentIndex == expected.SegmentIndex ||
                 (expected.SegmentIndex == 0 && e.SegmentIndex is null)));

            if (actual is null)
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

    private static void ClassifyRun(N1Result payload, FormworkBatchResult batch)
    {
        var pendingFixture = payload.Comparisons.Any(c =>
            c.Status == "NOT_MEASURED" &&
            (c.Reason?.Contains("PENDING", StringComparison.OrdinalIgnoreCase) == true ||
             c.ElementId?.StartsWith("PENDING", StringComparison.OrdinalIgnoreCase) == true));

        if (pendingFixture)
        {
            payload.RunClass = "INVALID_ENVIRONMENT_RUN";
            payload.AttemptOutcome = "NOT_MEASURED";
            payload.ClassificationReason =
                "N3 story-column fixture pending — fill per-story column ids in apriori (one column per story SSOT).";
            return;
        }

        var measured = payload.Comparisons.Where(c => c.Status is "PASS" or "FAIL").ToList();
        if (measured.Count == 0)
        {
            if (batch.ElementsProcessed > 0)
            {
                payload.RunClass = "PRODUCT_OBSERVE_RUN";
                payload.AttemptOutcome = "MEASURED_NO_APRIORI";
                payload.ClassificationReason = "Elements measured; a-priori comparisons pending.";
                return;
            }

            payload.RunClass = "INVALID_ENVIRONMENT_RUN";
            payload.AttemptOutcome = "NOT_MEASURED";
            payload.ClassificationReason = "No comparisons measured.";
            return;
        }

        if (measured.Any(c => c.Status == "FAIL"))
        {
            payload.RunClass = "PRODUCT_FAILURE_RUN";
            payload.AttemptOutcome = "FALSIFIED";
            payload.ClassificationReason = "One or more segments/elements exceed 1% vs a-priori.";
            return;
        }

        payload.RunClass = "PRODUCT_PASS_RUN";
        payload.AttemptOutcome = "PASS";
        payload.ClassificationReason = "All measured rows within 1% of a-priori hand calc.";
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
