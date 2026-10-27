using Autodesk.Revit.DB;

namespace DSSTech.Revit;

internal sealed class QcIssue
{
    public long ElementId { get; init; }
    public string Rule { get; init; } = "";
    public string Detail { get; init; } = "";
    public override string ToString() => $"{Rule}: {Detail}";
}

internal static class ModelQcScanner
{
    private static readonly BuiltInCategory[] Scope =
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_StructuralFoundation
    };

    private static readonly BuiltInCategory[] OverlapScope =
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Floors
    };

    public static IReadOnlyList<QcIssue> Scan(Document doc)
    {
        var issues = new List<QcIssue>();
        foreach (var level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
        {
            if (AbsurdMeters(level.Elevation, -50, 500))
                issues.Add(new QcIssue
                {
                    ElementId = level.Id.Value,
                    Rule = "Cao độ bất thường",
                    Detail = $"Level «{level.Name}»."
                });
        }

        var spans = new List<Span>();
        foreach (var bic in Scope)
        {
            var elements = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();
            foreach (var element in elements)
            {
                CheckElement(doc, element, issues);
                if (OverlapScope.Contains(bic))
                    TryAddSpan(element, bic, spans);
            }
        }

        CheckOverlap(spans, issues);
        if (issues.Count <= 400)
            return issues;

        var trimmed = issues.Take(399).ToList();
        trimmed.Add(new QcIssue
        {
            ElementId = 0,
            Rule = "Giới hạn",
            Detail = $"Còn {issues.Count - 399} dòng chưa hiện."
        });
        return trimmed;
    }

    private static void CheckElement(Document doc, Element element, List<QcIssue> issues)
    {
        if (element.GetTypeId() == ElementId.InvalidElementId)
            issues.Add(Issue(element, "Thiếu thông tin", "Không có type."));

        string? name = null;
        try { name = element.Name; }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
        if (IsBadName(name))
            issues.Add(Issue(element, "Tên không hợp lệ", string.IsNullOrWhiteSpace(name) ? "(trống)" : name!));

        CheckLevels(doc, element, issues);
    }

    private static void CheckLevels(Document doc, Element element, List<QcIssue> issues)
    {
        var baseId = HostLevelId(element);
        if (baseId is null || doc.GetElement(baseId) is not Level baseLevel)
        {
            issues.Add(Issue(element, "Sai tầng", "Không có Level hợp lệ."));
            return;
        }

        Level? top = null;
        if (IsCat(element, BuiltInCategory.OST_StructuralColumns))
        {
            var topId = element.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId();
            if (topId is null || topId == ElementId.InvalidElementId || doc.GetElement(topId) is not Level columnTop)
                issues.Add(Issue(element, "Thiếu thông tin", "Cột thiếu Top Level."));
            else
                top = columnTop;
        }
        else if (element is Wall)
        {
            var topId = element.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId();
            if (topId is not null && topId != ElementId.InvalidElementId)
            {
                if (doc.GetElement(topId) is not Level wallTop)
                    issues.Add(Issue(element, "Sai tầng", "Top Constraint không phải Level."));
                else
                    top = wallTop;
            }
        }

        if (top is not null && top.Elevation <= baseLevel.Elevation + 1e-6)
            issues.Add(Issue(element, "Sai tầng", $"Đỉnh «{top.Name}» không cao hơn đáy «{baseLevel.Name}»."));

        var box = element.get_BoundingBox(null);
        if (box is not null && (AbsurdMeters(box.Min.Z, -100, 1000) || AbsurdMeters(box.Max.Z, -100, 1000)))
            issues.Add(Issue(element, "Cao độ bất thường", "Bounding box nằm ngoài khoảng cao độ hợp lý."));

        if (top is not null)
        {
            var heightM = (top.Elevation - baseLevel.Elevation) * 0.3048;
            if (heightM > 100)
                issues.Add(Issue(element, "Cao độ bất thường", $"Chiều cao {heightM:0.#} m."));
        }
        else if (element is Wall wall)
        {
            var feet = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble();
            if (feet is double heightFeet)
            {
                var meters = heightFeet * 0.3048;
                if (meters > 100 || meters < 0.05)
                    issues.Add(Issue(element, "Cao độ bất thường", $"Chiều cao tường {meters:0.##} m."));
            }
        }
    }

    private static void TryAddSpan(Element element, BuiltInCategory bic, List<Span> spans)
    {
        var levelId = HostLevelId(element);
        var box = element.get_BoundingBox(null);
        if (levelId is null || box is null)
            return;
        spans.Add(new Span
        {
            Id = element.Id.Value,
            LevelId = levelId.Value,
            Cat = (long)bic,
            Box = box
        });
    }

    private static void CheckOverlap(List<Span> spans, List<QcIssue> issues)
    {
        const double cell = 6.56168;
        var buckets = new Dictionary<(long Level, long Cat, long X, long Y), List<Span>>();
        foreach (var span in spans)
        {
            var cx = (long)Math.Floor(((span.Box.Min.X + span.Box.Max.X) * 0.5) / cell);
            var cy = (long)Math.Floor(((span.Box.Min.Y + span.Box.Max.Y) * 0.5) / cell);
            var key = (span.LevelId, span.Cat, cx, cy);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<Span>();
                buckets[key] = list;
            }

            list.Add(span);
        }

        var reported = 0;
        foreach (var span in spans)
        {
            if (reported >= 100)
                break;
            var cx = (long)Math.Floor(((span.Box.Min.X + span.Box.Max.X) * 0.5) / cell);
            var cy = (long)Math.Floor(((span.Box.Min.Y + span.Box.Max.Y) * 0.5) / cell);
            for (var ix = cx - 1; ix <= cx + 1; ix++)
            for (var iy = cy - 1; iy <= cy + 1; iy++)
            {
                if (!buckets.TryGetValue((span.LevelId, span.Cat, ix, iy), out var list))
                    continue;
                foreach (var other in list)
                {
                    if (other.Id <= span.Id || reported >= 100)
                        continue;
                    if (!Overlaps(span.Box, other.Box))
                        continue;
                    issues.Add(new QcIssue
                    {
                        ElementId = span.Id,
                        Rule = "Chồng hình",
                        Detail = $"Id {span.Id} chồng Id {other.Id}."
                    });
                    reported++;
                }
            }
        }
    }

    private static bool Overlaps(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        const double shrink = 10.0 / 304.8;
        var minX = Math.Max(a.Min.X, b.Min.X);
        var minY = Math.Max(a.Min.Y, b.Min.Y);
        var minZ = Math.Max(a.Min.Z, b.Min.Z);
        var maxX = Math.Min(a.Max.X, b.Max.X);
        var maxY = Math.Min(a.Max.Y, b.Max.Y);
        var maxZ = Math.Min(a.Max.Z, b.Max.Z);
        return maxX - minX > shrink && maxY - minY > shrink && maxZ - minZ > shrink;
    }

    private static ElementId? HostLevelId(Element element)
    {
        if (element is Wall wall)
        {
            var id = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId();
            if (id is not null && id != ElementId.InvalidElementId)
                return id;
        }

        var familyLevel = element.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId()
                          ?? element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId()
                          ?? element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)?.AsElementId();
        if (familyLevel is not null && familyLevel != ElementId.InvalidElementId)
            return familyLevel;

        if (element.LevelId != ElementId.InvalidElementId)
            return element.LevelId;

        var level = element.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsElementId();
        if (level is not null && level != ElementId.InvalidElementId)
            return level;
        return null;
    }

    private static bool IsBadName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        var text = name.Trim();
        if (text is "?" or "??" or "???" or "xxx" or "XXX" or "TODO")
            return true;
        return System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"^(Wall|Floor|Column|Beam|Basic Wall|Generic)\s*\d+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool AbsurdMeters(double feet, double minM, double maxM)
    {
        var meters = feet * 0.3048;
        return meters < minM || meters > maxM;
    }

    private static bool IsCat(Element element, BuiltInCategory bic) =>
        element.Category?.Id.Value == (long)bic;

    private static QcIssue Issue(Element element, string rule, string detail) =>
        new()
        {
            ElementId = element.Id.Value,
            Rule = rule,
            Detail = $"{detail} — {element.Category?.Name} {element.Id.Value}"
        };

    private sealed class Span
    {
        public long Id { get; set; }
        public long LevelId { get; set; }
        public long Cat { get; set; }
        public BoundingBoxXYZ Box { get; set; } = null!;
    }
}
