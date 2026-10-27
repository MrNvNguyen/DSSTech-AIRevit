using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.McpHost;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit;

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
            ShowNotice(commandData.Application.MainWindowHandle, "DSSTech — MCP", "Cổng AI trên máy này đang bật.",
                "MCP đang bật.\n\n" +
                "Pipe: session %LOCALAPPDATA%\\DSSTech\\mcp-session.json\n" +
                $"HTTP: {url}\n" +
                $"Bearer (dán vào ChatGPT, chỉ hiện lần này):\n{token}");
        }
        else
        {
            ShowNotice(commandData.Application.MainWindowHandle, "DSSTech — MCP", "Cổng AI trên máy này đang tắt.",
                "MCP đã tắt (Off). AI mất cổng; CAD ribbon vẫn chạy.");
        }

        return Result.Succeeded;
    }

    internal static void ShowNotice(IntPtr owner, string title, string subtitle, string body)
    {
        var dlg = new DssDialog(title, subtitle, 560, 420);
        dlg.SetOwner(owner);
        dlg.AddSection(1, "Thông tin", new System.Windows.Controls.TextBlock
        {
            Text = body,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Foreground = DssTheme.Ink
        });
        dlg.PrimaryButton.Content = "Đóng";
        dlg.CancelButton.Visibility = System.Windows.Visibility.Collapsed;
        dlg.PrimaryButton.Click += (_, _) => { dlg.DialogResult = true; };
        dlg.ShowDialog();
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
        McpToggleCommand.ShowNotice(commandData.Application.MainWindowHandle,
            "DSSTech — MCP Clients",
            on ? "MCP đang bật." : "MCP đang tắt.",
            $"Trạng thái: {(on ? "On" : "Off")}\n\n" +
            "Cursor / Claude Desktop:\n" +
            "  command = ...\\DSSTech\\Dss.RevitMcp.exe (stdio)\n\n" +
            "ChatGPT (remote MCP):\n" +
            $"  URL = {url}\n" +
            $"  Bearer = {token}\n\n" +
            "Chỉ bind 127.0.0.1. Không tunnel. Token mới mỗi lần On.");
        return Result.Succeeded;
    }
}
