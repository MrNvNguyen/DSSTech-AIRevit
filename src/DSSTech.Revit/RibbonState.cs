using Autodesk.Revit.UI;
using Dss.McpHost;

namespace DSSTech.Revit;

/// <summary>
/// Giữ tham chiếu tới ribbon để đổi hình / chữ khi đang chạy:
/// nút AI Bật/Tắt, và bộ icon sáng/tối khi người dùng đổi giao diện Revit.
/// </summary>
internal static class RibbonState
{
    public const string Tab = "DSSTech";

    public static PushButton? AiButton { get; set; }
    public static UIControlledApplication? Application { get; set; }

    /// <summary>ID nút → tên icon gốc (không kèm cỡ / -dark). App.Build ghi vào đây.</summary>
    public static Dictionary<string, string> IconById { get; } = new(StringComparer.Ordinal);

    /// <summary>Nạp lại icon mọi nút DSSTech theo giao diện hiện tại (sáng / tối).</summary>
    public static void RefreshTheme()
    {
        var app = Application;
        if (app is null)
            return;

        try
        {
            foreach (var panel in app.GetRibbonPanels(Tab))
            foreach (var item in panel.GetItems())
            {
                ApplyIcon(item);
                if (item is PulldownButton pulldown)
                    foreach (var child in pulldown.GetItems())
                        ApplyIcon(child);
            }
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"ribbon theme refresh: {ex.Message}");
        }

        RefreshAi(); // nút AI có icon theo trạng thái, nạp lại sau cùng
    }

    private static void ApplyIcon(RibbonItem item)
    {
        if (item is not RibbonButton button || !IconById.TryGetValue(item.Name, out var icon))
            return;
        button.LargeImage = RibbonIcons.Load(icon, 32);
        button.Image = RibbonIcons.Load(icon, 16);
    }

    /// <summary>Đồng bộ nút AI với trạng thái MCP thật: icon xanh "Đang bật" / icon xám "Đã tắt".</summary>
    public static void RefreshAi()
    {
        var button = AiButton;
        if (button is null)
            return;

        try
        {
            var on = McpHostService.Instance.IsOn;
            var icon = on ? "icon-mcp" : "icon-mcp-off";
            button.ItemText = on ? "AI: Đang bật\nAI: On" : "AI: Đã tắt\nAI: Off";
            button.LargeImage = RibbonIcons.Load(icon, 32);
            button.Image = RibbonIcons.Load(icon, 16);
            button.ToolTip = on
                ? "Cổng AI (MCP) đang BẬT — AI trên máy này đọc / dựng được mô hình. Bấm để tắt."
                : "Cổng AI (MCP) đang TẮT — AI không kết nối được. Bấm để bật.";
            button.LongDescription = on
                ? "AI gateway (MCP) is ON — local AI clients can read / create in this model. Click to turn off."
                : "AI gateway (MCP) is OFF — AI clients cannot connect. Click to turn on.";
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"ribbon ai refresh: {ex.Message}");
        }
    }
}
