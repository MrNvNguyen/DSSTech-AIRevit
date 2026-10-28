using System.Text.Json;
using DSSTech.Core;

var baseDir = AppContext.BaseDirectory;
var dataPath = Path.Combine(baseDir, "Data", "norm-engine-runtime.json");
var configPath = Path.Combine(baseDir, "Config", "norm-engine.config.json");
if (!File.Exists(dataPath)) dataPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../Data/norm-engine-runtime.json"));
if (!File.Exists(configPath)) configPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../Config/norm-engine.config.json"));

var repoRoot = FindRepoRoot(baseDir);
var sampleDir = Path.Combine(repoRoot, "SampleInputs");
var expectedPath = Path.Combine(repoRoot, "tests", "expected.json");
var expectedQuantityPath = Path.Combine(repoRoot, "tests", "expected-quantity.json");

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "tests", "expected.json")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("Cannot locate repo root (tests/expected.json).");
}

if (args.Length > 0 && args[0] == "--smoke")
{
    RunSmoke();
    return;
}

if (args.Length > 0 && args[0] == "--boq")
{
    RunBoqSmoke(args.Length > 1 ? args[1] : null);
    return;
}

if (args.Length > 0 && args[0] == "--boq-xlsx")
{
    RunBoqXlsx(args.Length > 1 ? args[1] : Path.Combine(repoRoot, "out", "boq-smoke.xlsx"));
    return;
}

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  DSSTech.Cli <element-facts.json> [output.json]");
    Console.WriteLine("  DSSTech.Cli --smoke");
    Console.WriteLine("  DSSTech.Cli --boq [output.csv]");
    Console.WriteLine("  DSSTech.Cli --boq-xlsx [output.xlsx]");
    return;
}

var inputPath = Path.GetFullPath(args[0]);
var json = File.ReadAllText(inputPath);
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var element = JsonSerializer.Deserialize<ElementFacts>(json, options) ?? throw new InvalidDataException("Input JSON invalid.");
var data = RuntimeDataLoader.Load(dataPath);
var config = RuntimeDataLoader.LoadConfig(configPath);
var engine = new NormSelectionEngine(data, config);
var result = engine.Select(element);
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
if (args.Length > 1) ResultExporter.ExportJson([result], Path.GetFullPath(args[1]));
Environment.ExitCode = result.Status is SelectionStatus.Error or SelectionStatus.NoWorkItemMapping ? 2 : 0;

void RunSmoke()
{
    if (!File.Exists(expectedPath))
    {
        Console.Error.WriteLine($"Missing {expectedPath}");
        Environment.ExitCode = 1;
        return;
    }

    var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(expectedPath))
                   ?? new Dictionary<string, string>();
    var data = RuntimeDataLoader.Load(dataPath);
    var config = RuntimeDataLoader.LoadConfig(configPath);
    var engine = new NormSelectionEngine(data, config);
    var failed = 0;

    foreach (var (file, leaf) in expected)
    {
        var path = Path.Combine(sampleDir, file);
        if (!File.Exists(path))
        {
            Console.WriteLine($"MISS  {file} (missing fixture)");
            failed++;
            continue;
        }

        var element = JsonSerializer.Deserialize<ElementFacts>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var result = engine.Select(element);
        var actual = result.NormCode ?? "";
        if (string.Equals(actual, leaf, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"PASS  {file} -> {actual}");
        else
        {
            Console.WriteLine($"FAIL  {file} expected {leaf} got {actual} ({result.Status})");
            failed++;
        }
    }

    // BOQ quantity must land in the norm unit (kg → t, m2 → 100m2), not just pick the right code.
    var expectedQuantities = File.Exists(expectedQuantityPath)
        ? JsonSerializer.Deserialize<Dictionary<string, ExpectedQuantity>>(File.ReadAllText(expectedQuantityPath),
              new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []
        : [];
    foreach (var (file, want) in expectedQuantities)
    {
        var path = Path.Combine(sampleDir, file);
        if (!File.Exists(path))
        {
            Console.WriteLine($"MISS  {file} (missing fixture)");
            failed++;
            continue;
        }

        var element = JsonSerializer.Deserialize<ElementFacts>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var result = engine.Select(element);
        var row = BoqAggregator.FromSelection(element, result, data);
        var label = $"{file} -> {row.BoqQuantity:0.######} {row.Unit}";
        if (string.Equals(row.Unit, want.Unit, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(row.BoqQuantity - want.Quantity) <= 1e-6)
            Console.WriteLine($"PASS  qty {label}");
        else
        {
            Console.WriteLine($"FAIL  qty {label}, expected {want.Quantity:0.######} {want.Unit}");
            failed++;
        }
    }

    Environment.ExitCode = failed == 0 ? 0 : 1;
    Console.WriteLine(failed == 0 ? "Smoke PASS" : $"Smoke FAIL ({failed})");
}

void RunBoqSmoke(string? outputPath)
{
    if (outputPath is not null && outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
        // --boq takes an output path; a .json here is almost always a fixture passed by mistake.
        Console.Error.WriteLine($"--boq writes CSV; refusing to overwrite {outputPath}");
        Environment.ExitCode = 1;
        return;
    }

    var data = RuntimeDataLoader.Load(dataPath);
    var config = RuntimeDataLoader.LoadConfig(configPath);
    var pipeline = new BoqPipeline(data, config);
    var facts = Directory.GetFiles(sampleDir, "*.json")
        .Select(path => JsonSerializer.Deserialize<ElementFacts>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
        .ToList();
    var detail = pipeline.Extract(facts, BoqGroupingMode.Detail).ToList();
    var grouped = BoqGrouper.Group(detail, BoqGroupingMode.Code);
    var inspection = BoqInspectionBuilder.Build(grouped, "code", facts.Count);
    var detailQty = Math.Round(detail.Sum(x => x.BimQuantity), 6);
    var groupQty = Math.Round(grouped.Sum(x => x.BimQuantity), 6);
    Console.WriteLine($"BOQ detail: {detail.Count}, grouped: {grouped.Count}, official-ready: {grouped.Count(x => x.CanIssueOfficially)}");
    Console.WriteLine($"Σ BIM qty detail={detailQty} grouped={groupQty} {(detailQty == groupQty ? "PASS" : "FAIL")}");
    foreach (var row in inspection)
        Console.WriteLine($"{row.Metric}: {row.Value}");
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        BoqExporter.ExportCsv(grouped, Path.GetFullPath(outputPath));
        Console.WriteLine($"Wrote {outputPath}");
    }
    if (Math.Abs(detailQty - groupQty) > 1e-9)
        Environment.ExitCode = 1;
}

void RunBoqXlsx(string outputPath)
{
    var data = RuntimeDataLoader.Load(dataPath);
    var config = RuntimeDataLoader.LoadConfig(configPath);
    var pipeline = new BoqPipeline(data, config);
    var facts = Directory.GetFiles(sampleDir, "*.json")
        .Select(path => JsonSerializer.Deserialize<ElementFacts>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
        .ToList();
    var detail = pipeline.Extract(facts, BoqGroupingMode.Detail);
    var rows = BoqGrouper.Group(detail, BoqGroupingMode.Code);
    var inspection = BoqInspectionBuilder.Build(rows, "code", facts.Count);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    var full = Path.GetFullPath(outputPath);
    BoqWorkbookExporter.Export(full, rows, inspection);
    // G8 CSV companion: full rows for dry-run audit + official-only for G8 import
    var g8Path = Path.ChangeExtension(full, null) + "-g8.csv";
    var g8OfficialPath = Path.ChangeExtension(full, null) + "-g8-official.csv";
    G8BoqExporter.ExportCongTrinhCsv(rows, g8Path, officialOnly: false);
    G8BoqExporter.ExportCongTrinhCsv(rows, g8OfficialPath, officialOnly: true);
    Console.WriteLine($"Wrote {outputPath} ({rows.Count} BOQ rows) + {g8Path} + {g8OfficialPath}");
}

sealed record ExpectedQuantity(double Quantity, string Unit);
