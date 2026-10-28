using System.IO;
using System.Text.Json;
using Autodesk.Revit.UI;

namespace DSSTech.Revit;

internal static class McpStartupNotice
{
    private static string UiPreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DSSTech",
        "ui.json");

    public static void ShowIfNeeded()
    {
        try
        {
            if (IsSuppressMcpOnToast())
                return;

            var dialog = new TaskDialog("DSSTech")
            {
                MainInstruction = "MCP đang bật",
                MainContent = "AI có thể kết nối. Bấm nút \"AI: Đang bật\" (panel Trợ lý AI) trên ribbon để tắt.",
                CommonButtons = TaskDialogCommonButtons.Ok,
                VerificationText = "Không hiện lại",
                Id = "DSSTech.McpOnToast"
            };
            dialog.EnableDoNotShowAgain("DSSTech.McpOnToast", false, "Không hiện lại");

            dialog.Show();
            if (dialog.WasVerificationChecked())
                SetSuppressMcpOnToast(true);
        }
        catch
        {
            // do not fail startup
        }
    }

    private static bool IsSuppressMcpOnToast()
    {
        try
        {
            if (!File.Exists(UiPreferencesPath))
                return false;

            using var doc = JsonDocument.Parse(File.ReadAllText(UiPreferencesPath));
            return doc.RootElement.TryGetProperty("suppressMcpOnToast", out var prop) && prop.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private static void SetSuppressMcpOnToast(bool suppress)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UiPreferencesPath)!);
            File.WriteAllText(UiPreferencesPath, JsonSerializer.Serialize(new { suppressMcpOnToast = suppress }));
        }
        catch
        {
            // ignore
        }
    }
}
