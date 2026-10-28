using Autodesk.Revit.UI;
using Dss.McpHost;

namespace DSSTech.Revit;

/// <summary>
/// Giữ tham chiếu tới các nút ribbon cần đổi hình / chữ khi đang chạy (hiện chỉ có nút AI Bật/Tắt).
/// </summary>
internal static class RibbonState
{
    public static PushButton? AiButton { get; set; }

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
