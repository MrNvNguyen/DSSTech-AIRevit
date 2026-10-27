using System.IO;

namespace DSSTech.Revit;

internal static class DssTechLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DSSTech",
        "mcp-host.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must not break add-in
        }
    }
}
