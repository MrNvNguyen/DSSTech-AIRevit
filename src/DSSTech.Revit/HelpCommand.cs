using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DSSTech.Revit;

/// <summary>Nút "Hướng dẫn · Help": mở Help/HuongDan.html cài kèm add-in.</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class HelpCommand : IExternalCommand
{
    public static string HelpFile => Path.Combine(PluginPaths.Root, "Help", "HuongDan.html");

    /// <summary>Đường dẫn file:/// tới trang hướng dẫn (kèm #mục nếu có). Null nếu chưa cài file.</summary>
    public static string? HelpUri(string anchor)
    {
        if (!File.Exists(HelpFile))
            return null;
        var uri = new Uri(HelpFile).AbsoluteUri;
        return string.IsNullOrEmpty(anchor) ? uri : $"{uri}#{anchor}";
    }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (!File.Exists(HelpFile))
        {
            TaskDialog.Show("DSSTech — Hướng dẫn",
                "Không tìm thấy file hướng dẫn:\n" + HelpFile + "\n\nHãy cài lại add-in hoặc báo bộ phận BIM.");
            return Result.Failed;
        }

        try
        {
            Process.Start(new ProcessStartInfo(HelpFile) { UseShellExecute = true });
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            DssTechLog.Write($"help open: {ex.Message}");
            return Result.Failed;
        }
    }
}
