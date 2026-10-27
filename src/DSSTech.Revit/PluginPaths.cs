using System.IO;
using System.Reflection;

namespace DSSTech.Revit;

internal static class PluginPaths
{
    public static string Root => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
    public static string RuntimeData => Path.Combine(Root, "Data", "norm-engine-runtime.json");
    public static string Config => Path.Combine(Root, "Config", "norm-engine.config.json");
    public static string FormworkRules => Path.Combine(Root, "Config", "formwork-rules.json");
}
