using Autodesk.Revit.DB;
using DSSTech.Core.Formwork;

namespace DSSTech.Revit.Formwork;

internal sealed class SolidProvider
{
    private const double VolumeToleranceFt3 = 1e-9;

    private readonly Document _doc;
    private readonly Options _options;
    private readonly FormworkRuleSet _ruleSet;

    public SolidProvider(Document doc, FormworkRuleSet ruleSet)
    {
        _doc = doc;
        _ruleSet = ruleSet;
        _options = new Options
        {
            DetailLevel = ViewDetailLevel.Fine,
            ComputeReferences = true,
            IncludeNonVisibleObjects = false
        };
    }

    public IReadOnlyList<SolidEntry> Collect(IReadOnlyCollection<ElementId>? filter = null)
    {
        var filterSet = filter is { Count: > 0 }
            ? filter.ToHashSet()
            : null;

        var entries = new List<SolidEntry>();
        foreach (var element in CollectStructuralElements(filterSet))
        {
            var rule = FormworkRuleMatcher.Match(element, _ruleSet);
            if (rule?.Skip == true)
                continue;
            if (!FormworkRuleMatcher.PassesStructuralOnly(element, rule))
                continue;

            var solid = MergeSolids(ExtractSolids(element));
            if (solid is null) continue;

            var role = FormworkRuleMatcher.ToRole(rule);
            var priority = rule?.Priority > 0 ? rule.Priority : (int)role;

            entries.Add(new SolidEntry
            {
                Element = element,
                ElementId = element.Id,
                Role = role,
                Rule = rule,
                Priority = priority,
                Solid = solid,
                BoundingBox = element.get_BoundingBox(null) ?? solid.GetBoundingBox()
            });
        }

        return entries;
    }

    private IEnumerable<Element> CollectStructuralElements(HashSet<ElementId>? filterSet)
    {
        foreach (var cat in FormworkRuleMatcher.CollectCategories(_ruleSet))
        {
            foreach (var element in new FilteredElementCollector(_doc)
                         .OfCategory(cat)
                         .WhereElementIsNotElementType()
                         .ToElements())
            {
                if (filterSet is not null && !filterSet.Contains(element.Id))
                    continue;
                yield return element;
            }
        }
    }

    private List<Solid> ExtractSolids(Element element)
    {
        var geometry = element.get_Geometry(_options);
        if (geometry is null) return [];
        var solids = new List<Solid>();
        CollectSolids(geometry, solids);
        return solids.Where(s => s.Volume > VolumeToleranceFt3).ToList();
    }

    private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
    {
        foreach (var obj in geometry)
        {
            switch (obj)
            {
                case Solid solid when solid.Volume > VolumeToleranceFt3:
                    solids.Add(solid);
                    break;
                case GeometryInstance instance:
                    var instGeom = instance.GetInstanceGeometry();
                    if (instGeom is not null) CollectSolids(instGeom, solids);
                    break;
            }
        }
    }

    internal static Solid? MergeSolids(IReadOnlyList<Solid> solids)
    {
        Solid? merged = null;
        foreach (var solid in solids)
        {
            if (merged is null)
            {
                merged = solid;
                continue;
            }

            merged = TryBoolean(merged, solid, BooleanOperationsType.Union) ?? merged;
        }

        return merged is { Volume: > VolumeToleranceFt3 } ? merged : null;
    }

    internal static Solid? TryBoolean(Solid a, Solid b, BooleanOperationsType op)
    {
        try
        {
            var result = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, op);
            return result.Volume > VolumeToleranceFt3 ? result : null;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class SolidEntry
{
    public required Element Element { get; init; }
    public required ElementId ElementId { get; init; }
    public FormworkElementRole Role { get; init; }
    public FormworkRule? Rule { get; init; }
    public int Priority { get; init; }
    public required Solid Solid { get; init; }
    public required BoundingBoxXYZ BoundingBox { get; init; }
    public bool GroundContactDropBottom { get; set; }
    public int? SegmentIndex { get; init; }
    public int? SegmentCount { get; init; }
    public double? ClipBottomZM { get; init; }
    public double? ClipTopZM { get; init; }
    public double? BboxHeightM { get; init; }
    public bool ClipHeightApplied { get; init; }
}
