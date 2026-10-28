namespace DSSTech.Core;

public static class BoqAggregator
{
    public static BoqLineItem FromSelection(ElementFacts facts, NormSelectionResult result, RuntimeData data)
    {
        var family = data.WorkFamilies.FirstOrDefault(x =>
            x.WorkItemId.Equals(result.WorkItemId, StringComparison.OrdinalIgnoreCase));
        facts.Properties.TryGetValue("Description", out var description);
        facts.Properties.TryGetValue("LevelName", out var level);
        facts.Properties.TryGetValue("ZoneName", out var zone);
        facts.Properties.TryGetValue("SystemName", out var systemName);
        facts.Properties.TryGetValue("SystemCode", out var systemCode);
        facts.Properties.TryGetValue("ElementType", out var elementType);
        facts.Properties.TryGetValue("ExternalId", out var externalId);
        facts.Properties.TryGetValue("PlacementMethod", out var placementMethod);
        // BimQuantity stays in the model unit; QsFactor carries it into the norm unit.
        var unitFactor = ResourceCalculator.UnitFactor(result.QuantityUnit, result.NormUnit);

        return new BoqLineItem
        {
            ElementId = result.ElementId,
            ExternalId = Convert.ToString(externalId) ?? "",
            RevitCategory = result.RevitCategory,
            Code = result.WorkItemId,
            NormCode = result.NormCode,
            NormName = result.NormName,
            WorkItemId = result.WorkItemId,
            Discipline = family?.Discipline ?? "",
            Unit = string.IsNullOrWhiteSpace(result.NormUnit) ? result.QuantityUnit : result.NormUnit,
            BimQuantity = result.Quantity,
            BimUnit = result.QuantityUnit,
            QsFactor = unitFactor ?? 1,
            ElementCount = 1,
            Material = facts.MaterialName,
            MaterialLabel = facts.MaterialName,
            Status = unitFactor.HasValue ? MapStatus(result) : "Cần kiểm tra",
            Level = Convert.ToString(level) ?? "",
            Zone = Convert.ToString(zone) ?? "",
            ElementType = Convert.ToString(elementType) ?? "",
            Description = Convert.ToString(description) ?? "",
            HostKind = Convert.ToString(elementType) ?? "",
            PlacementMethod = Convert.ToString(placementMethod) ?? "",
            SystemName = Convert.ToString(systemName) ?? "",
            SystemCode = Convert.ToString(systemCode) ?? "",
            SourceSystem = Convert.ToString(systemName) ?? "",
            ScheduleType = facts.TypeName,
            SelectorMissing = result.MissingParameters.ToList(),
            CandidateNormCodes = result.CandidateCodes.ToList(),
            ElementIds = [result.ElementId],
            ExternalIds = string.IsNullOrWhiteSpace(Convert.ToString(externalId)) ? [] : [Convert.ToString(externalId)!],
            CanIssueOfficially = result.CanIssueOfficially && unitFactor.HasValue,
            LengthM = TryDouble(facts, "LengthM"),
            WidthM = TryDouble(facts, "WidthM"),
            HeightM = TryDouble(facts, "HeightM"),
            Mark = TryString(facts, "Mark"),
            HostId = TryString(facts, "HostId"),
            InOutBuilding = FirstString(facts, "InOutBuilding", "QS_InOutBuilding"),
            MaterialGrade = TryString(facts, "MaterialGrade"),
            ThicknessM = TryDouble(facts, "ThicknessM"),
            DiameterMm = TryDouble(facts, "DiameterMm"),
            AreaM2 = TryDouble(facts, "QuantityArea"),
            VolumeM3 = TryDouble(facts, "QuantityVolume"),
            WeightKg = TryDouble(facts, "WeightKg"),
            FormworkAreaM2 = TryDouble(facts, "FormworkArea")
                ?? TryDouble(facts, "QS_FormworkArea"),
            FamilyName = facts.FamilyName ?? "",
            FormworkSideM2 = TryDouble(facts, "FormworkSide"),
            FormworkBottomM2 = TryDouble(facts, "FormworkBottom"),
            FormworkDeductM2 = TryDouble(facts, "FormworkDeduct"),
            FormworkWaitPour = TryString(facts, "FormworkWaitPour"),
            FormworkGroundContact = TryString(facts, "FormworkGroundContact"),
            FormworkConfidence = TryString(facts, "FormworkConfidence"),
            ContactElementIds = FirstString(facts, "ContactElementIds", "FormworkContactIds")
        };
    }

    public static string MapStatus(NormSelectionResult result) => result.Status switch
    {
        SelectionStatus.SelectedApproved => "Đã ánh xạ",
        SelectionStatus.SelectedQc1 => "Cần xác nhận thông số",
        SelectionStatus.SelectedCodeOnly => "Chờ mã lá TT38",
        SelectionStatus.MissingParameters => "Thiếu thông số mô hình",
        SelectionStatus.Ambiguous => "Cần kiểm tra",
        SelectionStatus.NoNormMatched => "Chờ mã lá TT38",
        SelectionStatus.NoWorkItemMapping => "Cần kiểm tra",
        _ => "Cần kiểm tra"
    };

    private static double? TryDouble(ElementFacts facts, string key) =>
        facts.Properties.TryGetValue(key, out var value) && ValueTools.TryDouble(value, out var number) ? number : null;

    private static string TryString(ElementFacts facts, string key) =>
        facts.Properties.TryGetValue(key, out var value) ? Convert.ToString(value)?.Trim() ?? "" : "";

    private static string FirstString(ElementFacts facts, params string[] keys)
    {
        foreach (var key in keys)
        {
            var s = TryString(facts, key);
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return "";
    }
}
