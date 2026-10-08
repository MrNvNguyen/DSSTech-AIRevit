namespace QSNormEngine.Core.Formwork;

/// <summary>Element role with default contact priority (lower wins). Overridden by FormworkRule.Priority when rules load.</summary>
public enum FormworkElementRole
{
    Unknown = 0,
    Column = 1,
    /// <summary>Structural wall — same side takeoff path as column (peri × H − beam/slab).</summary>
    Wall = 2,
    Beam = 3,
    Slab = 4,
    /// <summary>Isolated footing / pile cap / pad (not piles — those are skipped).</summary>
    Footing = 5
}

public enum FormworkFaceRole
{
    Side,
    Bottom,
    Top,
    Excluded
}

/// <summary>Per-element (or per-segment) formwork area breakdown (Revit-API-free DTO).</summary>
public sealed class FormworkResult
{
    public required string ElementId { get; init; }
    public FormworkElementRole Role { get; init; }
    public string Kind { get; init; } = "";
    public int? SegmentIndex { get; init; }
    public int? SegmentCount { get; init; }
    public double? ClipBottomZM { get; init; }
    public double? ClipTopZM { get; init; }
    public double? SegmentHeightM { get; init; }
    public double? BboxHeightM { get; init; }
    public bool ClipHeightApplied { get; init; }
    public double SideAreaM2 { get; init; }
    public double BottomAreaM2 { get; init; }
    public double TopAreaM2 { get; init; }
    public double GrossAreaM2 { get; init; }
    public double DeductAreaM2 { get; init; }
    public double TotalAreaM2 { get; init; }
    /// <summary>Column shoulder leftover beside narrower beams (beam-depth band).</summary>
    public double WaitPourAreaM2 { get; init; }
    public bool GroundContactDropBottom { get; init; }
    public string Status { get; init; } = "OK";
    public string? Reason { get; init; }
}

public sealed class FormworkBatchResult
{
    public IReadOnlyList<FormworkResult> Elements { get; init; } = [];
    public int NeighborPairs { get; init; }
    public int ElementsProcessed { get; init; }
    public int SegmentsEmitted { get; init; }
    public double EpsilonMm { get; init; } = 5.0;
    public string? RulesPath { get; init; }
    public string? RulesSha256 { get; init; }
    public bool GroundContactAutoDetect { get; init; }
}
