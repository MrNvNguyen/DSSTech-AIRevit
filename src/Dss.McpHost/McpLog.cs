namespace Dss.McpHost;

internal static class McpLog
{
    private const long MaxBytes = 1024 * 1024;
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
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                {
                    var archive = LogPath + ".old";
                    if (File.Exists(archive))
                        File.Delete(archive);
                    File.Move(LogPath, archive);
                }

                File.AppendAllText(LogPath, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must not break host
        }
    }
}
