namespace Dss.McpHost;

internal static class McpPaths
{
    public static string SessionFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DSSTech",
        "mcp-session.json");

}
