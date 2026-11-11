using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;

namespace DSSTech.Revit;

/// <summary>
/// The report at the end of a modelling command: written to the log in full, shown only when something failed, and
/// then only the failure lines. A command that went through ends quietly.
/// </summary>
internal static class DoneReport
{
    public static void Show(string title, string report)
    {
        DssTechLog.Write($"report {title}: {report.Replace("\r\n", " | ").Replace("\n", " | ")}");
        var errors = ReportErrors.Lines(report);
        if (errors.Count == 0)
            return;
        TaskDialog.Show(title, string.Join("\n", errors) +
                               "\n\nBáo cáo đầy đủ: %LOCALAPPDATA%\\DSSTech\\mcp-host.log");
    }
}
