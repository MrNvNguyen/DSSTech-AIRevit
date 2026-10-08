using System.Text.Json;
using Dss.McpContracts;
using Dss.McpHost.Create;

namespace Dss.McpHost.Tools;

internal sealed class HostBuiltInProvider : IMcpToolProvider
{
    private const string EmptySchema = "{\"type\":\"object\",\"properties\":{}}";
    private const string CategorySchema =
        "{\"type\":\"object\",\"properties\":{\"category\":{\"type\":\"string\"}},\"required\":[\"category\"]}";
    private const string QuerySchema =
        "{\"type\":\"object\",\"properties\":{\"category\":{\"type\":\"string\"},\"pageSize\":{\"type\":\"integer\"},\"pageToken\":{\"type\":\"string\"}},\"required\":[\"category\"]}";
    private const string CreateElementSchema =
        "{\"type\":\"object\",\"properties\":{\"category\":{\"type\":\"string\"},\"fields\":{\"type\":\"object\"},\"skipIfExists\":{\"type\":\"boolean\"},\"dryRun\":{\"type\":\"boolean\"},\"units\":{\"type\":\"string\"}},\"required\":[\"category\",\"fields\"]}";
    private const string CreateBatchSchema =
        "{\"type\":\"object\",\"properties\":{\"operations\":{\"type\":\"array\"},\"dryRun\":{\"type\":\"boolean\"}},\"required\":[\"operations\"]}";

    public string ModuleId => "dss.host";

    public IReadOnlyList<McpTool> Tools { get; } = new[]
    {
        Tool("ping", "Health check the DSSTech Host", EmptySchema),
        Tool("get_document", "Active document title, path, worksharing, Revit version", EmptySchema),
        Tool("get_levels", "Levels with elevation in millimetres", EmptySchema),
        Tool("get_grids", "Grids with start/end in millimetres", EmptySchema),
        Tool("get_view", "Active view id, name, type, level", EmptySchema),
        Tool("list_categories", "Allow-listed categories for later create", EmptySchema),
        Tool("list_types", "Type names for one category", CategorySchema),
        Tool("query_by_category", "Page elements of one category (max 100)", QuerySchema),
        Tool("create_element", "Create one Level/Grid/Wall/Column/Pipe (pilot)", CreateElementSchema),
        Tool("create_batch", "Create many elements in one Undoable transaction", CreateBatchSchema)
    };

    public McpResult Invoke(string tool, JsonElement args, McpCallContext ctx)
    {
        if (tool.Equals("delete_element", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("delete_elements", StringComparison.OrdinalIgnoreCase))
            return McpResult.Fail("forbidden_permission", "Delete is out of first create waves.");

        if (tool.Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            var ping = PingTool.Execute(Environment.ProcessId, ctx.RevitYear, ctx.DocumentTitle, mcpOn: true);
            return ReadToolsOk(ping);
        }

        if (tool.Equals("list_categories", StringComparison.OrdinalIgnoreCase))
            return ReadTools.ListCategories();

        if (ctx.HostScope is not RevitCallScope scope)
            return McpResult.Fail("no_revit_scope", "Host scope is missing.");

        var app = scope.App;
        return tool.ToLowerInvariant() switch
        {
            "get_document" => ReadTools.GetDocument(app),
            "get_levels" => ReadTools.GetLevels(app),
            "get_grids" => ReadTools.GetGrids(app),
            "get_view" => ReadTools.GetView(app),
            "list_types" => ReadTools.ListTypes(app, args),
            "query_by_category" => ReadTools.QueryByCategory(app, args),
            "create_element" => CreateBatchTool.ExecuteOne(app, args, ctx.Permission),
            "create_batch" => CreateBatchTool.Execute(app, args, ctx.Permission),
            _ => McpResult.Fail("method_not_found", $"Unknown tool '{tool}'.")
        };
    }

    private static McpResult ReadToolsOk(PingResult ping)
    {
        var json = JsonSerializer.Serialize(ping);
        using var doc = JsonDocument.Parse(json);
        return McpResult.Success(doc.RootElement.Clone());
    }

    private static McpTool Tool(string name, string description, string schema) =>
        new() { Name = name, Description = description, InputSchemaJson = schema };
}
