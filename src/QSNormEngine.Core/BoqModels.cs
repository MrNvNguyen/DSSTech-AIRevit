using System.Text.Json.Serialization;

namespace QSNormEngine.Core;

public enum BoqGroupingMode
{
    Detail,
    Code,
    Level,
    Type,
    LevelType
}

public sealed class BoqLineItem
{
    public int Id { get; set; }
    public string ElementId { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string RevitCategory { get; set; } = "";
    public string Code { get; set; } = "";
    public string NormCode { get; set; } = "";
    public string NormName { get; set; } = "";
    public string WorkItemId { get; set; } = "";
    public string Discipline { get; set; } = "";
    public string Unit { get; set; } = "";
    public double BimQuantity { get; set; }
    public double QsFactor { get; set; } = 1;
    public double BoqQuantity => Math.Round(BimQuantity * QsFactor, 6);
    public int ElementCount { get; set; } = 1;
    public string Source { get; set; } = "Revit";
    public string SystemCode { get; set; } = "";
    public string SystemName { get; set; } = "";
    public string SourceSystem { get; set; } = "";
    public string ScheduleType { get; set; } = "";
    public string Material { get; set; } = "";
    public string Status { get; set; } = "";
    public string Level { get; set; } = "";
    public string Zone { get; set; } = "";
    public string ElementType { get; set; } = "";
    public string Dimensions { get; set; } = "";
    public string SizeClass { get; set; } = "";
    public string Description { get; set; } = "";
    public string HostKind { get; set; } = "";
    public string PlacementMethod { get; set; } = "";
    public string GeometryBand { get; set; } = "";
    public string MaterialLabel { get; set; } = "";
    public string SectionClass { get; set; } = "";
    public string HeightClass { get; set; } = "";
    public string DiameterBand { get; set; } = "";
    public List<string> SelectorMissing { get; set; } = [];
    public List<string> CandidateNormCodes { get; set; } = [];
    public List<string> ElementIds { get; set; } = [];
    public List<string> ExternalIds { get; set; } = [];
    public bool CanIssueOfficially { get; set; }
    public double? LengthM { get; set; }
    public double? WidthM { get; set; }
    public double? HeightM { get; set; }
    // Q1 — element quantity table fields
    public string Mark { get; set; } = "";
    public string HostId { get; set; } = "";
    public string InOutBuilding { get; set; } = "";
    public string MaterialGrade { get; set; } = "";
    public double? ThicknessM { get; set; }
    public double? DiameterMm { get; set; }
    public double? AreaM2 { get; set; }
    public double? VolumeM3 { get; set; }
    public double? WeightKg { get; set; }
    public double? FormworkAreaM2 { get; set; }
    // Q2 — optional quantity-table extras (groups 2/4)
    public string FamilyName { get; set; } = "";
    public double? FormworkSideM2 { get; set; }
    public double? FormworkBottomM2 { get; set; }
    public double? FormworkDeductM2 { get; set; }
    public string FormworkWaitPour { get; set; } = "";
    public string FormworkGroundContact { get; set; } = "";
    public string FormworkConfidence { get; set; } = "";
    public string ContactElementIds { get; set; } = "";
}

public sealed class BoqInspectionRow
{
    [JsonPropertyName("Chi số")] public string Metric { get; set; } = "";
    [JsonPropertyName("Giá trị")] public string Value { get; set; } = "";
}

public sealed class BimMappingRule
{
    [JsonPropertyName("ruleId")] public string RuleId { get; set; } = "";
    [JsonPropertyName("revitCategory")] public string RevitCategory { get; set; } = "";
    [JsonPropertyName("workItemId")] public string WorkItemId { get; set; } = "";
    public int Priority { get; set; } = 100;
    public string? FamilyPattern { get; set; }
    public string? TypePattern { get; set; }
    public string? SystemCode { get; set; }
    public string? SystemName { get; set; }
    public string? MatchParameterNames { get; set; }
    public string? MatchParameterValues { get; set; }
    public List<ClassificationRuleCondition> Conditions { get; set; } = [];
}
