using Autodesk.Revit.DB;
using DSSTech.Core;

namespace DSSTech.Revit;

internal static class EngineHost
{
    public static (RuntimeData Data, EngineConfig Config, BoqPipeline Pipeline) Create()
    {
        var data = RuntimeDataLoader.Load(PluginPaths.RuntimeData);
        var config = RuntimeDataLoader.LoadConfig(PluginPaths.Config);
        return (data, config, new BoqPipeline(data, config));
    }
}
