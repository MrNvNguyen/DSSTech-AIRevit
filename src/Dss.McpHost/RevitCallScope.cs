using Autodesk.Revit.UI;

namespace Dss.McpHost;

internal sealed class RevitCallScope
{
    public required UIApplication App { get; init; }
    public required McpHostRuntimeState Runtime { get; init; }
}
