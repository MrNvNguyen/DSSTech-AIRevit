using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.McpHost;

namespace QSNormEngine.Revit;

[Transaction(TransactionMode.ReadOnly)]
public sealed class McpToggleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        McpHostService.Instance.Toggle();
        if (McpHostService.Instance.IsOn)
        {
            var url = McpHostService.Instance.HttpUrl ?? "(http chưa mở)";
            var token = McpHostService.Instance.HttpBearerToken ?? "";
            TaskDialog.Show("DSSTech",
                "MCP đang bật.\n\n" +
                $"Pipe: session %LOCALAPPDATA%\\DSSTech\\mcp-session.json\n" +
                $"HTTP: {url}\n" +
                $"Bearer (dán vào ChatGPT, chỉ hiện lần này):\n{token}");
        }
        else
        {
            TaskDialog.Show("DSSTech", "MCP đã tắt (Off). AI mất cổng; CAD ribbon vẫn chạy.");
        }

        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.ReadOnly)]
public sealed class McpClientsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var on = McpHostService.Instance.IsOn;
        var url = McpHostService.Instance.HttpUrl ?? "(off)";
        var token = on ? (McpHostService.Instance.HttpBearerToken ?? "") : "(off)";
        TaskDialog.Show("DSSTech — MCP Clients",
            $"Trạng thái: {(on ? "On" : "Off")}\n\n" +
            "Cursor / Claude Desktop:\n" +
            "  command = ...\\QSNormEngine\\Dss.RevitMcp.exe (stdio)\n\n" +
            "ChatGPT (remote MCP):\n" +
            $"  URL = {url}\n" +
            $"  Bearer = {token}\n\n" +
            "Chỉ bind 127.0.0.1. Không tunnel. Token mới mỗi lần On.");
        return Result.Succeeded;
    }
}
