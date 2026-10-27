namespace DSSTech.Core.Formwork;

/// <summary>Global formwork settings (Revit-API-free). N3 keys may deserialize but stay inert until authorized.</summary>
public sealed class FormworkSettings
{
    public double ProbeEpsilonMm { get; set; } = 5.0;
    public double VerticalToleranceDeg { get; set; } = 15.0;
    public bool UseRevitJoinOrder { get; set; }
    public bool GroundContactAutoDetect { get; set; }
    public double GroundContactToleranceMm { get; set; } = 50.0;
    public bool SlabOnGradeSkipsBottom { get; set; } = true;
    /// <summary>Openings below this area (m²) are not deducted from slab bottom; ≥ adds opening side formwork.</summary>
    public double SlabOpeningThresholdM2 { get; set; } = 1.0;
    /// <summary>
    /// When true (default), subtract beam/column/wall plan union from slab Bottom.
    /// Join-independent soffit: sketch − openings − support footprints (not HOST_AREA).
    /// Set false only to inspect gross sketch Bottom without support deduct.
    /// </summary>
    public bool SlabDeductSupportContact { get; set; } = true;
}

/// <summary>One category/kind rule. Inert N3 fields are ignored by N2 runtime.</summary>
public sealed class FormworkRule
{
    public string Kind { get; set; } = "";
    public List<string> Categories { get; set; } = [];
    public string? TypePattern { get; set; }
    public bool Skip { get; set; }
    public List<string> Faces { get; set; } = [];
    public int Priority { get; set; }
    public bool DropBottomWhenGroundContact { get; set; }
    public bool ClipHeight { get; set; }
    public List<string> IgnoreContactWith { get; set; } = [];
    public List<string> KeepUnjoinedAround { get; set; } = [];
    public bool StructuralOnly { get; set; }
}

public sealed class FormworkRuleSet
{
    public FormworkSettings Settings { get; set; } = new();
    public List<FormworkRule> Rules { get; set; } = [];
}
