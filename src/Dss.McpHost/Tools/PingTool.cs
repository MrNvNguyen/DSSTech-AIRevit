using Dss.McpContracts;

namespace Dss.McpHost.Tools;

internal static class PingTool
{
    public static PingResult Execute(int pid, int revitYear, string? documentTitle, bool mcpOn) =>
        new()
        {
            Ok = true,
            Pid = pid,
            RevitYear = revitYear,
            DocumentTitle = documentTitle,
            McpOn = mcpOn
        };
}
