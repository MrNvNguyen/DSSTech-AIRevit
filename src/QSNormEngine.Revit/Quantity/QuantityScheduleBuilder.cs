using System.IO;
using Autodesk.Revit.DB;
using QSNormEngine.Core;

namespace QSNormEngine.Revit.Quantity;

/// <summary>
/// Creates / updates multi-category ViewSchedule "QS-BIM Khối lượng cấu kiện" (Q3).
/// Idempotent by exact schedule name. Requires QS_* shared params already bound.
/// </summary>
internal static class QuantityScheduleBuilder
{
    public const string MultiCategoryScheduleName = "QS-BIM Khối lượng cấu kiện";

    private static readonly BuiltInParameter[] NativePriority =
    [
        BuiltInParameter.ALL_MODEL_MARK,
        BuiltInParameter.ELEM_FAMILY_PARAM,
        BuiltInParameter.ELEM_TYPE_PARAM,
        BuiltInParameter.SCHEDULE_LEVEL_PARAM,
        BuiltInParameter.ALL_MODEL_DESCRIPTION,
        BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
        BuiltInParameter.HOST_AREA_COMPUTED,
        BuiltInParameter.HOST_VOLUME_COMPUTED,
        BuiltInParameter.PHASE_CREATED,
        BuiltInParameter.PHASE_DEMOLISHED
    ];

    public static QuantityScheduleResult CreateOrUpdate(Document doc, ElementQuantityExporter.ColumnConfig? config = null)
    {
        config ??= LoadConfig();
        var schedule = FindByName(doc, MultiCategoryScheduleName);
        var created = false;
        if (schedule is null)
        {
            schedule = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
            schedule.Name = MultiCategoryScheduleName;
            created = true;
        }

        var definition = schedule.Definition;
        var schedulable = definition.GetSchedulableFields().ToList();

        ClearFields(definition);
        AddBuiltInFromList(definition, schedulable, NativePriority);
        AddSharedFromList(doc, definition, schedulable, config);

        return new QuantityScheduleResult(schedule.Id, created, MultiCategoryScheduleName);
    }

    public static ElementQuantityExporter.ColumnConfig LoadConfig()
    {
        var path = Path.Combine(PluginPaths.Root, "Config", ElementQuantityExporter.DefaultConfigFileName);
        return ElementQuantityExporter.LoadConfig(path);
    }

    public static ViewSchedule? FindByName(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    private static void ClearFields(ScheduleDefinition definition)
    {
        for (var i = definition.GetFieldCount() - 1; i >= 0; i--)
        {
            try { definition.RemoveField(i); }
            catch { /* ignore */ }
        }
    }

    private static void AddBuiltInFromList(
        ScheduleDefinition definition,
        List<SchedulableField> schedulable,
        BuiltInParameter[] wanted)
    {
        foreach (var bip in wanted)
        {
            var id = new ElementId(bip);
            var field = schedulable.FirstOrDefault(f => f.ParameterId == id);
            if (field is null) continue;
            try
            {
                definition.AddField(field);
            }
            catch
            {
                // skip unavailable
            }
        }
    }

    private static void AddSharedFromList(
        Document doc,
        ScheduleDefinition definition,
        List<SchedulableField> schedulable,
        ElementQuantityExporter.ColumnConfig config)
    {
        var enabled = new HashSet<string>(
            config.EnabledGroups.Select(g => g.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (enabled.Count == 0) enabled.Add("1");

        var names = new List<string>
        {
            "QS_InOutBuilding",
            "QS_MaterialGrade",
            "QS_WeightKg",
            "QS_FormworkArea",
            "QS_HostId"
        };

        if (enabled.Contains("4"))
        {
            names.AddRange(
            [
                "QS_FormworkSide",
                "QS_FormworkBottom",
                "QS_FormworkDeduct",
                "QS_FormworkWaitPour",
                "QS_FormworkGroundContact",
                "QS_FormworkConfidence"
            ]);
        }

        if (enabled.Contains("5"))
        {
            names.AddRange(["QS_WorkItemId", "QS_NormCode", "QS_NormStatus"]);
        }

        var byId = IndexSharedParameterIds(doc);
        foreach (var name in names)
        {
            if (!byId.TryGetValue(name, out var paramId)) continue;
            var field = schedulable.FirstOrDefault(f => f.ParameterId == paramId);
            if (field is null) continue;
            try
            {
                var added = definition.AddField(field);
                added.ColumnHeading = name;
            }
            catch
            {
                // skip
            }
        }
    }

    private static Dictionary<string, ElementId> IndexSharedParameterIds(Document doc)
    {
        var map = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
        foreach (var spe in new FilteredElementCollector(doc)
                     .OfClass(typeof(SharedParameterElement))
                     .Cast<SharedParameterElement>())
        {
            if (!string.IsNullOrWhiteSpace(spe.Name))
                map[spe.Name] = spe.Id;
        }
        return map;
    }
}

internal readonly record struct QuantityScheduleResult(ElementId ScheduleId, bool Created, string Name);
