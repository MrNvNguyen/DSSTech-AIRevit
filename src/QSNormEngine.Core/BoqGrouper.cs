namespace QSNormEngine.Core;

public static class BoqGrouper
{
    private static readonly Dictionary<string, int> StatusPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Đã ánh xạ"] = 0,
        ["Cần xác nhận thông số"] = 1,
        ["Thiếu thông số mô hình"] = 2,
        ["Chờ mã lá TT38"] = 2,
        ["Cần kiểm tra"] = 3
    };

    public static List<BoqLineItem> Group(IEnumerable<BoqLineItem> rows, BoqGroupingMode mode)
    {
        var list = rows.ToList();
        if (mode == BoqGroupingMode.Detail)
            return list.Select((row, index) => Clone(row, index + 1)).ToList();

        var groups = new Dictionary<string, BoqLineItem>(StringComparer.Ordinal);
        foreach (var row in list)
        {
            var key = GroupingKey(row, mode);
            if (!groups.TryGetValue(key, out var existing))
            {
                groups[key] = Clone(row, groups.Count + 1);
                continue;
            }
            Merge(existing, row);
        }
        return groups.Values.OrderBy(x => x.Id).Select((row, index) => Clone(row, index + 1)).ToList();
    }

    private static string GroupingKey(BoqLineItem row, BoqGroupingMode mode)
    {
        var baseParts = new[]
        {
            row.Code, row.NormCode, row.WorkItemId, row.Discipline, row.Unit,
            row.SystemCode, row.SourceSystem, row.ScheduleType, row.Material,
            row.PlacementMethod, row.HostKind, row.SectionClass, row.HeightClass,
            row.DiameterBand, row.SizeClass
        };
        var parts = baseParts.ToList();
        if (mode is BoqGroupingMode.LevelType or BoqGroupingMode.Level) parts.Add(row.Level);
        if (mode is BoqGroupingMode.LevelType or BoqGroupingMode.Type) parts.Add(row.ElementType);
        return string.Join("|", parts);
    }

    private static void Merge(BoqLineItem target, BoqLineItem incoming)
    {
        target.BimQuantity = Math.Round(target.BimQuantity + incoming.BimQuantity, 6);
        target.ElementCount += incoming.ElementCount;
        target.Description = MergeText(target.Description, incoming.Description, "Nhiều mô tả");
        target.Level = MergeText(target.Level, incoming.Level, "Nhiều tầng");
        target.Zone = MergeText(target.Zone, incoming.Zone, "Nhiều zone");
        target.ElementType = MergeText(target.ElementType, incoming.ElementType, "Nhiều type");
        target.Material = MergeText(target.Material, incoming.Material, "Nhiều vật liệu");
        target.SelectorMissing = target.SelectorMissing.Concat(incoming.SelectorMissing).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        target.CandidateNormCodes = target.CandidateNormCodes.Concat(incoming.CandidateNormCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        target.ElementIds = target.ElementIds.Concat(incoming.ElementIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        target.ExternalIds = target.ExternalIds.Concat(incoming.ExternalIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        target.CanIssueOfficially = target.CanIssueOfficially && incoming.CanIssueOfficially;
        if (StatusRank(incoming.Status) > StatusRank(target.Status))
            target.Status = incoming.Status;
        target.Mark = MergeText(target.Mark, incoming.Mark, "Nhiều Mark");
        target.HostId = MergeText(target.HostId, incoming.HostId, "Nhiều Host");
        target.InOutBuilding = MergeText(target.InOutBuilding, incoming.InOutBuilding, "Hỗn hợp");
        target.MaterialGrade = MergeText(target.MaterialGrade, incoming.MaterialGrade, "Nhiều mác");
        target.AreaM2 = SumNullable(target.AreaM2, incoming.AreaM2);
        target.VolumeM3 = SumNullable(target.VolumeM3, incoming.VolumeM3);
        target.WeightKg = SumNullable(target.WeightKg, incoming.WeightKg);
        target.FormworkAreaM2 = SumNullable(target.FormworkAreaM2, incoming.FormworkAreaM2);
        target.FormworkSideM2 = SumNullable(target.FormworkSideM2, incoming.FormworkSideM2);
        target.FormworkBottomM2 = SumNullable(target.FormworkBottomM2, incoming.FormworkBottomM2);
        target.FormworkDeductM2 = SumNullable(target.FormworkDeductM2, incoming.FormworkDeductM2);
        target.FamilyName = MergeText(target.FamilyName, incoming.FamilyName, "Nhiều family");
        target.FormworkWaitPour = MergeText(target.FormworkWaitPour, incoming.FormworkWaitPour, "Hỗn hợp");
        target.FormworkGroundContact = MergeText(target.FormworkGroundContact, incoming.FormworkGroundContact, "Hỗn hợp");
        target.FormworkConfidence = MergeText(target.FormworkConfidence, incoming.FormworkConfidence, "Hỗn hợp");
        target.ContactElementIds = MergeText(target.ContactElementIds, incoming.ContactElementIds, "Nhiều contact");
    }

    private static double? SumNullable(double? a, double? b)
    {
        if (a is null && b is null) return null;
        return Math.Round((a ?? 0) + (b ?? 0), 6);
    }

    private static int StatusRank(string status) =>
        StatusPriority.TryGetValue(status, out var rank) ? rank : 99;

    private static string MergeText(string current, string incoming, string multipleLabel)
    {
        var left = current?.Trim() ?? "";
        var right = incoming?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(left)) return right;
        if (string.IsNullOrWhiteSpace(right) || left.Equals(right, StringComparison.OrdinalIgnoreCase)) return left;
        return multipleLabel;
    }

    private static BoqLineItem Clone(BoqLineItem row, int id) => new()
    {
        Id = id,
        ElementId = row.ElementId,
        ExternalId = row.ExternalId,
        RevitCategory = row.RevitCategory,
        Code = row.Code,
        NormCode = row.NormCode,
        NormName = row.NormName,
        WorkItemId = row.WorkItemId,
        Discipline = row.Discipline,
        Unit = row.Unit,
        BimQuantity = row.BimQuantity,
        QsFactor = row.QsFactor,
        ElementCount = row.ElementCount,
        Source = row.Source,
        SystemCode = row.SystemCode,
        SystemName = row.SystemName,
        SourceSystem = row.SourceSystem,
        ScheduleType = row.ScheduleType,
        Material = row.Material,
        Status = row.Status,
        Level = row.Level,
        Zone = row.Zone,
        ElementType = row.ElementType,
        Dimensions = row.Dimensions,
        SizeClass = row.SizeClass,
        Description = row.Description,
        HostKind = row.HostKind,
        PlacementMethod = row.PlacementMethod,
        GeometryBand = row.GeometryBand,
        MaterialLabel = row.MaterialLabel,
        SectionClass = row.SectionClass,
        HeightClass = row.HeightClass,
        DiameterBand = row.DiameterBand,
        SelectorMissing = row.SelectorMissing.ToList(),
        CandidateNormCodes = row.CandidateNormCodes.ToList(),
        ElementIds = row.ElementIds.ToList(),
        ExternalIds = row.ExternalIds.ToList(),
        CanIssueOfficially = row.CanIssueOfficially,
        LengthM = row.LengthM,
        WidthM = row.WidthM,
        HeightM = row.HeightM,
        Mark = row.Mark,
        HostId = row.HostId,
        InOutBuilding = row.InOutBuilding,
        MaterialGrade = row.MaterialGrade,
        ThicknessM = row.ThicknessM,
        DiameterMm = row.DiameterMm,
        AreaM2 = row.AreaM2,
        VolumeM3 = row.VolumeM3,
        WeightKg = row.WeightKg,
        FormworkAreaM2 = row.FormworkAreaM2,
        FamilyName = row.FamilyName,
        FormworkSideM2 = row.FormworkSideM2,
        FormworkBottomM2 = row.FormworkBottomM2,
        FormworkDeductM2 = row.FormworkDeductM2,
        FormworkWaitPour = row.FormworkWaitPour,
        FormworkGroundContact = row.FormworkGroundContact,
        FormworkConfidence = row.FormworkConfidence,
        ContactElementIds = row.ContactElementIds
    };
}
