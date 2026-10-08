using Dss.McpContracts;

namespace Dss.McpHost;

public sealed class McpToolRegistry
{
    private readonly List<IMcpToolProvider> _providers = new();
    private readonly Dictionary<string, IMcpToolProvider> _byTool = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IMcpToolProvider provider)
    {
        foreach (var tool in provider.Tools)
        {
            if (_byTool.ContainsKey(tool.Name))
                throw new InvalidOperationException($"Duplicate MCP tool '{tool.Name}'.");
            _byTool[tool.Name] = provider;
        }

        _providers.Add(provider);
    }

    public IReadOnlyList<McpTool> List() =>
        _providers.SelectMany(p => p.Tools).ToList();

    public bool TryGet(string name, out IMcpToolProvider provider) =>
        _byTool.TryGetValue(name, out provider!);
}
