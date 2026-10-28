using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.McpContracts;

namespace Dss.McpHost.Create;

public static class CreateBatchTool
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static McpResult Execute(UIApplication app, JsonElement args, McpPermission permission)
    {
        if (permission is not (McpPermission.ReadCreate or McpPermission.ReadCreateModify))
            return McpResult.Fail("forbidden_permission", "Create requires Read+Create permission.");

        var doc = app.ActiveUIDocument?.Document;
        if (doc is null)
            return McpResult.Fail("no_active_document", "Open a Revit project first.");

        if (!TryParseOperations(args, out var operations, out var error))
            return error!;

        bool? batchDryRun = null;
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("dryRun", out var dryEl) &&
            dryEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            batchDryRun = dryEl.GetBoolean();

        // Operation dryRun does not lift the cap. Only an explicit batch false does.
        var forceDry = BatchDryRunPolicy.ForceDryRun(batchDryRun, operations.Count);

        if (forceDry)
        {
            var planned = operations.Select(op => CategoryDispatcher.Execute(doc, op, forceDryRun: true)).ToList();
            return Ok(new { dryRun = true, results = planned });
        }

        var results = new List<CreateItemResult>();
        using (var tx = new Transaction(doc, "DSSTech MCP create"))
        {
            tx.Start();
            try
            {
                foreach (var op in operations)
                    results.Add(ExecuteInSubTransaction(doc, op));
                CategoryDispatcher.ApplyWallLocationLines(doc, operations, results);
                tx.Commit();
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }
        }

        return Ok(new { dryRun = false, results });
    }

    private static CreateItemResult ExecuteInSubTransaction(Document doc, CreateOperation op)
    {
        using var sub = new SubTransaction(doc);
        sub.Start();
        var item = CategoryDispatcher.Execute(doc, op, forceDryRun: op.DryRun == true);
        try
        {
            if (item.Status == "failed")
                sub.RollBack();
            else
                sub.Commit();
        }
        catch (Exception ex)
        {
            if (sub.GetStatus() == TransactionStatus.Started)
                sub.RollBack();
            item.Status = "failed";
            item.Reason = string.IsNullOrWhiteSpace(item.Reason) ? ex.Message : item.Reason;
        }

        return item;
    }

    public static IReadOnlyList<string> ListRectangularColumnFamilies(Document doc) =>
        CategoryDispatcher.ListRectangularColumnFamilies(doc);

    public static IReadOnlyList<string> ListRectangularBeamFamilies(Document doc) =>
        CategoryDispatcher.ListRectangularBeamFamilies(doc);

    public static McpResult ExecuteOne(UIApplication app, JsonElement args, McpPermission permission)
    {
        // create_element = batch of 1
        CreateOperation op;
        try
        {
            op = JsonSerializer.Deserialize<CreateOperation>(args.GetRawText(), Json) ?? new CreateOperation();
        }
        catch (Exception ex)
        {
            return McpResult.Fail("invalid_params", ex.Message);
        }

        if (string.IsNullOrWhiteSpace(op.Category) &&
            args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("category", out var cat))
            op.Category = cat.GetString() ?? "";

        var wrapper = JsonSerializer.SerializeToElement(new { operations = new[] { op } }, Json);
        return Execute(app, wrapper, permission);
    }

    private static bool TryParseOperations(JsonElement args, out List<CreateOperation> operations, out McpResult? error)
    {
        operations = new List<CreateOperation>();
        error = null;
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("operations", out var opsEl) ||
            opsEl.ValueKind != JsonValueKind.Array)
        {
            error = McpResult.Fail("invalid_params", "create_batch requires params.operations[].");
            return false;
        }

        foreach (var item in opsEl.EnumerateArray())
        {
            var op = JsonSerializer.Deserialize<CreateOperation>(item.GetRawText(), Json);
            if (op is null || string.IsNullOrWhiteSpace(op.Category))
            {
                error = McpResult.Fail("invalid_params", "Each operation needs category.");
                return false;
            }
            operations.Add(op);
        }

        if (operations.Count == 0)
        {
            error = McpResult.Fail("invalid_params", "operations is empty.");
            return false;
        }

        return true;
    }

    private static McpResult Ok(object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        using var doc = JsonDocument.Parse(json);
        return McpResult.Success(doc.RootElement.Clone());
    }
}
