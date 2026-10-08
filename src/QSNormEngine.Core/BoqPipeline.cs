namespace QSNormEngine.Core;

public sealed class BoqPipeline
{
    private readonly RuntimeData _data;
    private readonly EngineConfig _config;
    private readonly NormSelectionEngine _engine;

    public BoqPipeline(RuntimeData data, EngineConfig? config = null)
    {
        _data = data;
        _config = config ?? new EngineConfig();
        _engine = new NormSelectionEngine(data, _config);
    }

    public IReadOnlyList<BoqLineItem> Extract(IEnumerable<ElementFacts> elements, BoqGroupingMode mode = BoqGroupingMode.Code)
    {
        var detail = elements.Select(element =>
        {
            var result = _engine.Select(element);
            return BoqAggregator.FromSelection(element, result, _data);
        }).ToList();
        return BoqGrouper.Group(detail, mode);
    }

    public IReadOnlyList<NormSelectionResult> SelectMany(IEnumerable<ElementFacts> elements) =>
        _engine.SelectMany(elements);
}
