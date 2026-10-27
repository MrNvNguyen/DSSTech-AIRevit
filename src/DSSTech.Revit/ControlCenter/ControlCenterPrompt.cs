using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpContracts;
using Dss.McpHost;
using Dss.McpHost.Create;
using DSSTech.Revit.ModelRevit;

namespace DSSTech.Revit;

internal static class ControlCenterPrompt
{
    private const string Help =
        "Theo mô tả, ví dụ:\n" +
        "cột tại 3000,4000 kích thước 400x600\n" +
        "tường từ 0,0 đến 6000,0 dày 200\n" +
        "Thêm cuối câu: đáy Tầng 1 đỉnh Tầng 2\n" +
        "Hoặc gọi tool: get_levels\n" +
        "Hoặc dán JSON create_batch / create_element.";

    public static void RunAddin(UIApplication app)
    {
        if (!ParamSetupRunner.ApplySavedOrPrompt(app, out var note))
        {
            ControlCenterView.Instance.SetPromptResult(string.IsNullOrWhiteSpace(note) ? "Đã huỷ." : note);
            return;
        }

        ControlCenterView.Instance.SetPromptResult(note + "\nTiếp theo: pick layer, pick Text/Dimension, tạo.");
        var result = ModelRevitCommand.Run(app, ensurePackage: false);
        ControlCenterView.Instance.SetPromptResult(note + "\nModelRevit: " + result);
    }

    public static void RunDescription(UIApplication app, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            ControlCenterView.Instance.SetPromptResult(Help);
            return;
        }

        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            ControlCenterView.Instance.SetPromptResult(RunJson(app, trimmed));
            return;
        }

        var tool = Regex.Match(
            trimmed,
            @"^(?i)(ping|get_document|get_levels|get_grids|get_view|list_categories|list_types|query_by_category)(?:\s+(.+))?$");
        if (tool.Success)
        {
            ControlCenterView.Instance.SetPromptResult(RunTool(app, tool.Groups[1].Value, tool.Groups[2].Value));
            return;
        }

        var columnAt = IndexOfAny(trimmed, "cột", "column");
        var wallAt = IndexOfAny(trimmed, "tường", "wall");
        string message;
        if (columnAt >= 0 && (wallAt < 0 || columnAt <= wallAt))
            message = RunColumn(app, trimmed);
        else if (wallAt >= 0)
            message = RunWall(app, trimmed);
        else
            message = Help;

        ControlCenterView.Instance.SetPromptResult(message);
    }

    private static string RunTool(UIApplication app, string name, string rest)
    {
        JsonElement args;
        if (string.IsNullOrWhiteSpace(rest))
            args = JsonSerializer.SerializeToElement(new { });
        else
            args = JsonSerializer.SerializeToElement(new { category = rest.Trim() });
        return Format(McpHostService.Instance.InvokeLocal(app, name, args));
    }

    private static string RunJson(UIApplication app, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Help;
            if (root.TryGetProperty("operations", out _))
                return Format(McpHostService.Instance.InvokeLocal(app, "create_batch", root.Clone()));
            if (root.TryGetProperty("category", out _))
                return Format(McpHostService.Instance.InvokeLocal(app, "create_element", root.Clone()));
            return Help;
        }
        catch (JsonException ex)
        {
            return ex.Message + "\n" + Help;
        }
    }

    private static string RunColumn(UIApplication app, string text)
    {
        var match = Regex.Match(
            text,
            @"(?i)(?:cột|column)\s+(?:tại|at)\s+(?<x>-?\d+(?:\.\d+)?)\s*[,;]\s*(?<y>-?\d+(?:\.\d+)?)(?:\s+(?:kích thước|size|kt))?\s+(?<w>\d+(?:\.\d+)?)\s*[x×]\s*(?<h>\d+(?:\.\d+)?)");
        if (!match.Success)
            return Help;

        var doc = app.ActiveUIDocument?.Document;
        var view = doc?.ActiveView;
        if (doc is null || view is null)
            return "Mở project Revit trước.";
        if (!TryLevels(doc, view, text, out var baseName, out var topName, out var levelError))
            return levelError;

        var families = CreateBatchTool.ListRectangularColumnFamilies(doc);
        if (families.Count == 0)
            return "Không có family cột chữ nhật có tham số b/h. Load family trước khi dựng theo mô tả.";

        var x = Num(match.Groups["x"].Value);
        var y = Num(match.Groups["y"].Value);
        var w = (int)Math.Round(Num(match.Groups["w"].Value));
        var h = (int)Math.Round(Num(match.Groups["h"].Value));
        var family = families[0];
        var payload = new
        {
            dryRun = false,
            operations = new[]
            {
                new
                {
                    category = "StructuralColumns",
                    units = "mm",
                    fields = new Dictionary<string, object>
                    {
                        ["xyzMm"] = new[] { x, y, 0.0 },
                        ["familyType"] = $"{w} x {h}",
                        ["widthMm"] = w,
                        ["depthMm"] = h,
                        ["baseLevel"] = baseName,
                        ["topLevel"] = topName,
                        ["sourceFamily"] = family
                    }
                }
            }
        };
        var summary = $"Cột tại {x:0},{y:0} kích thước {w}x{h}, {baseName} → {topName}, family {family}.";
        return summary + "\n" + Format(McpHostService.Instance.InvokeLocal(app, "create_batch", JsonSerializer.SerializeToElement(payload)));
    }

    private static string RunWall(UIApplication app, string text)
    {
        var match = Regex.Match(
            text,
            @"(?i)(?:tường|wall)\s+(?:từ|from)\s+(?<x0>-?\d+(?:\.\d+)?)\s*[,;]\s*(?<y0>-?\d+(?:\.\d+)?)\s+(?:đến|to|->)\s+(?<x1>-?\d+(?:\.\d+)?)\s*[,;]\s*(?<y1>-?\d+(?:\.\d+)?)");
        if (!match.Success)
            return Help;

        var doc = app.ActiveUIDocument?.Document;
        var view = doc?.ActiveView;
        if (doc is null || view is null)
            return "Mở project Revit trước.";
        if (!TryLevels(doc, view, text, out var baseName, out var topName, out var levelError))
            return levelError;

        var thickMatch = Regex.Match(
            text,
            @"(?i)(?:dày|thickness|(?<![A-Za-z])t(?![A-Za-z]))\s*[:=]?\s*(?<t>\d+(?:\.\d+)?)");
        var thick = thickMatch.Success ? Num(thickMatch.Groups["t"].Value) : 200;
        var wallType = AnnotationSizeParser.WallTypeHint(new AnnotationSize(thick, null, null, thick.ToString(CultureInfo.InvariantCulture)));
        var structural = Regex.IsMatch(text, @"(?i)kết cấu|structural");
        var payload = new
        {
            dryRun = false,
            operations = new[]
            {
                new
                {
                    category = "Walls",
                    units = "mm",
                    fields = new Dictionary<string, object>
                    {
                        ["startMm"] = new[] { Num(match.Groups["x0"].Value), Num(match.Groups["y0"].Value), 0.0 },
                        ["endMm"] = new[] { Num(match.Groups["x1"].Value), Num(match.Groups["y1"].Value), 0.0 },
                        ["wallType"] = wallType,
                        ["level"] = baseName,
                        ["topLevel"] = topName,
                        ["structural"] = structural
                    }
                }
            }
        };
        var summary = $"Tường {match.Groups["x0"].Value},{match.Groups["y0"].Value} → {match.Groups["x1"].Value},{match.Groups["y1"].Value}, dày {thick:0} mm, {baseName} → {topName}, type {wallType}.";
        return summary + "\n" + Format(McpHostService.Instance.InvokeLocal(app, "create_batch", JsonSerializer.SerializeToElement(payload)));
    }

    private static bool TryLevels(Document doc, View view, string text, out string baseName, out string topName, out string error)
    {
        var match = Regex.Match(text, @"(?i)(?:đáy|base)\s+(?<b>.+?)\s+(?:đỉnh|top)\s+(?<t>.+?)\s*$");
        if (match.Success)
        {
            baseName = match.Groups["b"].Value.Trim();
            topName = match.Groups["t"].Value.Trim();
            error = "";
            return baseName.Length > 0 && topName.Length > 0;
        }

        if (!ModelRevitLevelContext.TryResolve(view, doc, out var baseLevel, out var topLevel, out error))
        {
            baseName = "";
            topName = "";
            return false;
        }

        baseName = baseLevel.Name;
        topName = topLevel.Name;
        return true;
    }

    private static int IndexOfAny(string text, params string[] tokens)
    {
        var best = -1;
        foreach (var token in tokens)
        {
            var at = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (best < 0 || at < best))
                best = at;
        }

        return best;
    }

    private static double Num(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Format(McpResult result)
    {
        if (!result.Ok)
            return $"{result.ErrorCode}: {result.Message}";
        if (result.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "OK";
        var raw = result.Data.GetRawText();
        return raw.Length > 4000 ? raw.Substring(0, 4000) + "..." : raw;
    }
}
