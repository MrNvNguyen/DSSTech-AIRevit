using System.Text.Json;

namespace DSSTech.Core;

public static class RuntimeDataLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public static RuntimeData Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy runtime database JSON.", path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RuntimeData>(json, Options)
               ?? throw new InvalidDataException("Runtime JSON không hợp lệ.");
    }

    public static EngineConfig LoadConfig(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new EngineConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EngineConfig>(json, Options) ?? new EngineConfig();
    }
}
