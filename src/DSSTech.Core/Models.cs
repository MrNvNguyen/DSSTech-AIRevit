using System.Text.Json.Serialization;

namespace DSSTech.Core;

public sealed class RuntimeData
{
    public string SchemaVersion { get; set; } = "1.0";
    public string SourceDatabase { get; set; } = "";
    public List<WorkFamily> WorkFamilies { get; set; } = [];
    public List<NormIndexRecord> NormIndex { get; set; } = [];
    public List<NormVariant> NormVariants { get; set; } = [];
    public List<NormCondition> NormConditions { get; set; } = [];
    public List<ResourceDefinition> Resources { get; set; } = [];
    public List<NormResource> NormResources { get; set; } = [];
    public List<AdjustmentRule> AdjustmentRules { get; set; } = [];
    public List<BimPropertyMap> BimPropertyMaps { get; set; } = [];
    public List<SelectorSchema> SelectorSchemas { get; set; } = [];
    public List<ConstructionMethod> ConstructionMethods { get; set; } = [];
    public List<ParameterDefinition> ParameterDictionary { get; set; } = [];
    public List<ClassificationRuleSet> ClassificationRuleSets { get; set; } = [];
    public List<BimMappingRule> BimMappingRules { get; set; } = [];
}

public sealed class ClassificationRuleSet
{
    [JsonPropertyName("setId")] public string SetId { get; set; } = "";
    [JsonPropertyName("revitCategory")] public string RevitCategory { get; set; } = "";
    [JsonPropertyName("workItemId")] public string WorkItemId { get; set; } = "";
    public int Priority { get; set; } = 100;
    public List<ClassificationRuleCondition> Conditions { get; set; } = [];
}

public sealed class ClassificationRuleCondition
{
    [JsonPropertyName("paramKey")] public string ParamKey { get; set; } = "";
    public string Operator { get; set; } = "";
    public string? Value1 { get; set; }
    public string? Value2 { get; set; }
    public string Unit { get; set; } = "";
    [JsonPropertyName("conditionGroup")] public int ConditionGroup { get; set; } = 1;
}

public sealed class WorkFamily
{
    [JsonPropertyName("work_item_id")] public string WorkItemId { get; set; } = "";
    [JsonPropertyName("module_id")] public string ModuleId { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Chapter { get; set; } = "";
    [JsonPropertyName("group_l1")] public string GroupL1 { get; set; } = "";
    [JsonPropertyName("group_l2")] public string GroupL2 { get; set; } = "";
    public string Discipline { get; set; } = "";
    [JsonPropertyName("bim_object_hint")] public string BimObjectHint { get; set; } = "";
    [JsonPropertyName("selector_parameters")] public string SelectorParameters { get; set; } = "";
    [JsonPropertyName("quantity_basis")] public string QuantityBasis { get; set; } = "";
    [JsonPropertyName("default_unit")] public string DefaultUnit { get; set; } = "";
    [JsonPropertyName("mapping_status")] public string MappingStatus { get; set; } = "";
    [JsonPropertyName("resource_status")] public string ResourceStatus { get; set; } = "";
    [JsonPropertyName("official_source")] public string OfficialSource { get; set; } = "";
}

public sealed class NormIndexRecord
{
    [JsonPropertyName("norm_code")] public string NormCode { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Chapter { get; set; } = "";
    [JsonPropertyName("work_item_id")] public string WorkItemId { get; set; } = "";
    [JsonPropertyName("normalized_label")] public string NormalizedLabel { get; set; } = "";
    [JsonPropertyName("parent_inferred")] public string ParentInferred { get; set; } = "";
    [JsonPropertyName("hierarchy_level")] public int HierarchyLevel { get; set; }
    [JsonPropertyName("digitization_status")] public string DigitizationStatus { get; set; } = "";
    [JsonPropertyName("official_source")] public string OfficialSource { get; set; } = "";
    [JsonPropertyName("qc_note")] public string QcNote { get; set; } = "";
}

public sealed class NormVariant
{
    [JsonPropertyName("variant_code")] public string VariantCode { get; set; } = "";
    [JsonPropertyName("parent_norm_code")] public string ParentNormCode { get; set; } = "";
    [JsonPropertyName("variant_name")] public string VariantName { get; set; } = "";
    [JsonPropertyName("category_id")] public string? CategoryId { get; set; }
    [JsonPropertyName("method_id")] public string? MethodId { get; set; }
    public string Unit { get; set; } = "";
    [JsonPropertyName("page_no")] public int PageNo { get; set; }
    [JsonPropertyName("option_no")] public int OptionNo { get; set; }
}

public sealed class NormCondition
{
    [JsonPropertyName("condition_id")] public string ConditionId { get; set; } = "";
    [JsonPropertyName("variant_code")] public string VariantCode { get; set; } = "";
    [JsonPropertyName("param_key")] public string ParamKey { get; set; } = "";
    public string Operator { get; set; } = "";
    public string? Value1 { get; set; }
    public string? Value2 { get; set; }
    public string Unit { get; set; } = "";
    [JsonPropertyName("condition_group")] public int ConditionGroup { get; set; }
}

public sealed class ResourceDefinition
{
    [JsonPropertyName("resource_id")] public string ResourceId { get; set; } = "";
    [JsonPropertyName("resource_type")] public string ResourceType { get; set; } = "";
    [JsonPropertyName("resource_name")] public string ResourceName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class NormResource
{
    [JsonPropertyName("norm_resource_id")] public string NormResourceId { get; set; } = "";
    [JsonPropertyName("variant_code")] public string VariantCode { get; set; } = "";
    [JsonPropertyName("resource_id")] public string ResourceId { get; set; } = "";
    public double Quantity { get; set; }
}

public sealed class AdjustmentRule
{
    [JsonPropertyName("adjustment_id")] public string AdjustmentId { get; set; } = "";
    [JsonPropertyName("norm_scope")] public string NormScope { get; set; } = "";
    public string Description { get; set; } = "";
    [JsonPropertyName("trigger_param")] public string TriggerParam { get; set; } = "";
    [JsonPropertyName("trigger_operator")] public string TriggerOperator { get; set; } = "";
    [JsonPropertyName("trigger_value")] public string TriggerValue { get; set; } = "";
    [JsonPropertyName("action_type")] public string ActionType { get; set; } = "";
    [JsonPropertyName("target_resource_id")] public string TargetResourceId { get; set; } = "";
    public double Factor { get; set; }
    public string Note { get; set; } = "";
}

public sealed class BimPropertyMap
{
    [JsonPropertyName("revit_category")] public string RevitCategory { get; set; } = "";
    [JsonPropertyName("default_work_item")] public string DefaultWorkItem { get; set; } = "";
    [JsonPropertyName("quantity_parameter")] public string QuantityParameter { get; set; } = "";
    [JsonPropertyName("required_properties")] public string RequiredProperties { get; set; } = "";
    [JsonPropertyName("mapping_note")] public string MappingNote { get; set; } = "";
}

public sealed class SelectorSchema
{
    [JsonPropertyName("work_item_id")] public string WorkItemId { get; set; } = "";
    [JsonPropertyName("group_l1")] public string GroupL1 { get; set; } = "";
    [JsonPropertyName("group_l2")] public string GroupL2 { get; set; } = "";
    [JsonPropertyName("selector_keys")] public string SelectorKeys { get; set; } = "";
    [JsonPropertyName("primary_key")] public string PrimaryKey { get; set; } = "";
    [JsonPropertyName("rule_note")] public string RuleNote { get; set; } = "";
    [JsonPropertyName("mapping_status")] public string MappingStatus { get; set; } = "";
}

public sealed class ConstructionMethod
{
    [JsonPropertyName("method_id")] public string MethodId { get; set; } = "";
    [JsonPropertyName("work_group")] public string WorkGroup { get; set; } = "";
    [JsonPropertyName("method_name")] public string MethodName { get; set; } = "";
    [JsonPropertyName("method_key")] public string MethodKey { get; set; } = "";
    public int Active { get; set; }
}

public sealed class ParameterDefinition
{
    [JsonPropertyName("param_id")] public string ParamId { get; set; } = "";
    [JsonPropertyName("param_key")] public string ParamKey { get; set; } = "";
    [JsonPropertyName("param_name_vi")] public string ParamNameVi { get; set; } = "";
    [JsonPropertyName("data_type")] public string DataType { get; set; } = "";
    public string Unit { get; set; } = "";
    [JsonPropertyName("example_value")] public string ExampleValue { get; set; } = "";
    [JsonPropertyName("role_in_mapping")] public string RoleInMapping { get; set; } = "";
    [JsonPropertyName("required_default")] public int RequiredDefault { get; set; }
}

public sealed class EngineConfig
{
    public bool AllowQc1Calculation { get; set; } = true;
    public bool AllowCodeOnlyResult { get; set; } = true;
    public bool RequireExplicitConstructionMethod { get; set; } = true;
    public Dictionary<string, object?> GlobalDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, object?>> WorkItemDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ElementFacts
{
    public string ElementId { get; set; } = "";
    public string RevitCategory { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public double Quantity { get; set; }
    public string QuantityUnit { get; set; } = "";
    public Dictionary<string, object?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum SelectionStatus
{
    SelectedApproved,
    SelectedQc1,
    SelectedCodeOnly,
    Ambiguous,
    MissingParameters,
    NoNormMatched,
    NoWorkItemMapping,
    Error
}

public sealed class NormSelectionResult
{
    public string ElementId { get; set; } = "";
    public string RevitCategory { get; set; } = "";
    public SelectionStatus Status { get; set; }
    public string WorkItemId { get; set; } = "";
    public string WorkItemName { get; set; } = "";
    public string NormCode { get; set; } = "";
    public string NormName { get; set; } = "";
    public string NormUnit { get; set; } = "";
    public string DigitizationStatus { get; set; } = "";
    public double Quantity { get; set; }
    public string QuantityUnit { get; set; } = "";
    public double NormQuantityFactor { get; set; }
    public double Confidence { get; set; }
    public bool CanIssueOfficially { get; set; }
    public List<string> MissingParameters { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> CandidateCodes { get; set; } = [];
    public List<AppliedAdjustment> Adjustments { get; set; } = [];
    public List<ResourceDemand> Resources { get; set; } = [];
}

public sealed class ResourceDemand
{
    public string ResourceId { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string Unit { get; set; } = "";
    public double NormQuantity { get; set; }
    public double? TotalQuantity { get; set; }
    public bool IsPercentage { get; set; }
    public string Note { get; set; } = "";
}

public sealed class AppliedAdjustment
{
    public string AdjustmentId { get; set; } = "";
    public string Description { get; set; } = "";
    public string ActionType { get; set; } = "";
    public string TargetResourceId { get; set; } = "";
    public double Factor { get; set; }
}

internal sealed class CandidateEvaluation
{
    public NormVariant Variant { get; set; } = null!;
    public NormIndexRecord Index { get; set; } = null!;
    public bool IsMatch { get; set; }
    public int MatchedConditions { get; set; }
    public int TotalConditions { get; set; }
    public List<string> Missing { get; set; } = [];
    public List<string> Failed { get; set; } = [];
    public double Score => TotalConditions == 0 ? 0 : (double)MatchedConditions / TotalConditions;
}
