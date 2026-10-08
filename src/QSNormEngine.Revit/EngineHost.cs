using Autodesk.Revit.DB;
using QSNormEngine.Core;

namespace QSNormEngine.Revit;

internal static class EngineHost
{
    public static (RuntimeData Data, EngineConfig Config, BoqPipeline Pipeline) Create()
    {
        var data = RuntimeDataLoader.Load(PluginPaths.RuntimeData);
        var config = RuntimeDataLoader.LoadConfig(PluginPaths.Config);
        return (data, config, new BoqPipeline(data, config));
    }
}
