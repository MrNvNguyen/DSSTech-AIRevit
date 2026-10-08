using System.Globalization;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QSNormEngine.Core.Formwork;
using QSNormEngine.Revit.Formwork;

namespace FormworkN1.AddIn;

/// <summary>
/// N2 measurement harness — loads Config/formwork-rules.json and writes formwork-n2 evidence.
/// Same assembly as N1; read-only.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class FormworkN2MeasureCommand : IExternalCommand
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
            var evidenceDir = Path.Combine(repoRoot, "docs", "evidence", "formwork-n2", "N2-FORMWORK-RULES");
            Directory.CreateDirectory(evidenceDir);

            var aprioriPath = Path.Combine(repoRoot, "docs", "evidence", "formwork-n2", "apriori-hand-calc.yaml");
            var apriori = AprioriLoader.TryLoad(aprioriPath);
            var filterIds = apriori?.Elements
                .Where(e => !string.IsNullOrWhiteSpace(e.ElementId) && !e.ElementId.StartsWith("PENDING", StringComparison.OrdinalIgnoreCase))
                .Select(e => new ElementId(long.Parse(e.ElementId, CultureInfo.InvariantCulture)))
                .ToList();

            var rulesPath = FormworkRulesPath.Resolve(repoRoot);
            var ruleSet = FormworkRuleSetLoader.Load(rulesPath);
            // N2 regression bar: clipHeight must stay inert for frozen N2 pilot IDs.
            foreach (var rule in ruleSet.Rules)
            {
                if (string.Equals(rule.Kind, "Column", StringComparison.OrdinalIgnoreCase))
                    rule.ClipHeight = false;
            }
            var rulesSha = FormworkRuleSetLoader.Sha256Hex(rulesPath);
            var rulesRel = Path.GetRelativePath(repoRoot, rulesPath).Replace('\\', '/');

            var batch = FormworkCalculator.Compute(
                doc, ruleSet, filterIds is { Count: > 0 } ? filterIds : null, rulesRel, rulesSha);

            var comparisons = BuildComparisons(apriori, batch);
            var payload = new N1Result
            {
                Schema = "formwork-n2/1",
                Campaign = "N2-FORMWORK-RULES",
                WaveId = "N2-FORMWORK-RULES-ATTEMPT-1",
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
                    : "docs/evidence/formwork-n2/apriori-hand-calc.yaml",
                AprioriSlabPending = false,
                Elements = batch.Elements.Select(ToElementPayload).ToList(),
                Comparisons = comparisons
            };
            ClassifyRun(payload);

            var resultPath = Path.Combine(evidenceDir, "result.json");
            var outputName = Environment.GetEnvironmentVariable("QS_FORMWORK_RESULT_NAME");
            if (!string.IsNullOrWhiteSpace(outputName))
            {
                var toggleDir = Path.Combine(repoRoot, "docs", "evidence", "formwork-n2", "rules-toggle");
                Directory.CreateDirectory(toggleDir);
                resultPath = Path.Combine(toggleDir, outputName);
            }

            File.WriteAllText(resultPath, JsonSerializer.Serialize(payload, N1Json.Options), new UTF8Encoding(false));

            TaskDialog.Show(
                "Formwork N2 Measure",
                $"Elements: {batch.ElementsProcessed}\n" +
                $"Neighbor pairs: {batch.NeighborPairs}\n" +
                $"groundContactAutoDetect: {batch.GroundContactAutoDetect}\n" +
                $"Rules SHA: {(batch.RulesSha256 ?? "").Substring(0, Math.Min(12, (batch.RulesSha256 ?? "").Length))}…\n" +
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

        if (apriori is null || apriori.Elements.Count == 0)
        {
            comparisons.Add(new ComparisonPayload
            {
                Role = "all",
                Status = "NOT_MEASURED",
                Reason = "Missing or empty docs/evidence/formwork-n2/apriori-hand-calc.yaml"
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
                    Reason = "A-priori incomplete."
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
        if (measured.Count == 0)
        {
            // Still a valid rules-load product run if elements were computed (toggle proof).
            if (payload.ElementsProcessed > 0)
            {
                payload.RunClass = "PRODUCT_OBSERVE_RUN";
                payload.AttemptOutcome = "MEASURED_NO_APRIORI";
                payload.ClassificationReason = "Elements measured; a-priori comparisons pending.";
                return;
            }

            payload.RunClass = "INVALID_ENVIRONMENT_RUN";
            payload.AttemptOutcome = "NOT_MEASURED";
            payload.ClassificationReason = "No comparisons measured against a-priori hand calc.";
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
