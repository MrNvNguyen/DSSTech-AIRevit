using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;

namespace DSSTech.Revit;

internal static class ParamPackageCatalog
{
    public const string Builtin = "Builtin";
    public const string ByPart = "ByPart";
    public const string ByDiscipline = "ByDiscipline";
    public const string Full = "Full";

    public static readonly (string Label, ModelRevitPartKind Kind)[] Parts =
    {
        ("Cột", ModelRevitPartKind.Column),
        ("Tường", ModelRevitPartKind.StructuralWall),
        ("Dầm", ModelRevitPartKind.Beam),
        ("Sàn", ModelRevitPartKind.Floor),
        ("Đài cọc", ModelRevitPartKind.PileCap),
        ("Móng đơn", ModelRevitPartKind.IsolatedFoundation),
        ("Móng băng", ModelRevitPartKind.StripFoundation),
        ("Móng bè", ModelRevitPartKind.RaftFoundation),
        ("Cọc", ModelRevitPartKind.Pile),
        ("Cửa", ModelRevitPartKind.Door),
        ("Cửa sổ", ModelRevitPartKind.Window),
        ("Phòng", ModelRevitPartKind.Room),
        ("Trần", ModelRevitPartKind.Ceiling),
        ("Ống nước", ModelRevitPartKind.Pipe),
        ("Ống gió", ModelRevitPartKind.Duct),
        ("Máng cáp", ModelRevitPartKind.CableTray),
        ("Thiết bị", ModelRevitPartKind.Equipment)
    };

    public static readonly (string Label, string Id)[] Disciplines =
    {
        ("Kết cấu", "Structure"),
        ("Kiến trúc", "Architecture"),
        ("MEPF", "Mepf")
    };

    private static readonly Dictionary<ModelRevitPartKind, string[]> PartParameters = new()
    {
        [ModelRevitPartKind.Column] = ["QS_ElementType", "QS_ConcreteGrade", "QS_ConstructionHeight", "QS_SteelGrade", "QS_FormworkArea", "QS_FormworkSide", "QS_RebarDiameter"],
        [ModelRevitPartKind.StructuralWall] = ["QS_ElementType", "QS_ConcreteGrade", "QS_WallThickness", "QS_Thickness", "QS_ConstructionHeight", "QS_BrickType", "QS_FormworkArea"],
        [ModelRevitPartKind.Beam] = ["QS_ElementType", "QS_ConcreteGrade", "QS_SteelGrade", "QS_ConstructionHeight", "QS_FormworkArea", "QS_FormworkSide"],
        [ModelRevitPartKind.Floor] = ["QS_ElementType", "QS_ConcreteGrade", "QS_Thickness", "QS_ConstructionHeight", "QS_FormworkArea", "QS_FormworkBottom"],
        [ModelRevitPartKind.FloorOpening] = ["QS_ElementType", "QS_Thickness"],
        [ModelRevitPartKind.IsolatedFoundation] = ["QS_ElementType", "QS_ConcreteGrade", "QS_ConstructionHeight"],
        [ModelRevitPartKind.PileCap] = ["QS_ElementType", "QS_ConcreteGrade", "QS_ConstructionHeight"],
        [ModelRevitPartKind.RaftFoundation] = ["QS_ElementType", "QS_ConcreteGrade", "QS_ConstructionHeight"],
        [ModelRevitPartKind.Pile] = ["QS_ElementType", "QS_ConcreteGrade", "QS_PileType", "QS_PileMaterial", "QS_ConstructionHeight"],
        [ModelRevitPartKind.StripFoundation] = ["QS_ElementType", "QS_ConcreteGrade", "QS_ConstructionHeight"],
        [ModelRevitPartKind.Door] = ["QS_ElementType", "QS_FrameType"],
        [ModelRevitPartKind.Window] = ["QS_ElementType", "QS_FrameType"],
        [ModelRevitPartKind.Room] = ["QS_ElementType", "QS_InOutBuilding"],
        [ModelRevitPartKind.Ceiling] = ["QS_CeilingType", "QS_FrameType", "QS_PanelType"],
        [ModelRevitPartKind.Pipe] = ["QS_PipeMaterial", "QS_SystemType", "QS_JointType", "QS_InsulationMaterial", "QS_InsulationThickness"],
        [ModelRevitPartKind.Duct] = ["QS_SystemType", "QS_InsulationMaterial", "QS_InsulationThickness", "QS_EquipmentType"],
        [ModelRevitPartKind.CableTray] = ["QS_SystemType", "QS_ElementType"],
        [ModelRevitPartKind.Equipment] = ["QS_EquipmentType", "QS_SystemType"]
    };

    private static readonly Dictionary<ModelRevitPartKind, BuiltInCategory[]> PartCategories = new()
    {
        [ModelRevitPartKind.Column] = [BuiltInCategory.OST_StructuralColumns],
        [ModelRevitPartKind.StructuralWall] = [BuiltInCategory.OST_Walls],
        [ModelRevitPartKind.Beam] = [BuiltInCategory.OST_StructuralFraming],
        [ModelRevitPartKind.Floor] = [BuiltInCategory.OST_Floors],
        [ModelRevitPartKind.FloorOpening] = [BuiltInCategory.OST_Floors],
        [ModelRevitPartKind.IsolatedFoundation] = [BuiltInCategory.OST_StructuralFoundation],
        [ModelRevitPartKind.PileCap] = [BuiltInCategory.OST_StructuralFoundation],
        [ModelRevitPartKind.RaftFoundation] = [BuiltInCategory.OST_StructuralFoundation],
        [ModelRevitPartKind.Pile] = [BuiltInCategory.OST_StructuralFoundation],
        [ModelRevitPartKind.StripFoundation] = [BuiltInCategory.OST_StructuralFoundation],
        [ModelRevitPartKind.Door] = [BuiltInCategory.OST_Doors],
        [ModelRevitPartKind.Window] = [BuiltInCategory.OST_Windows],
        [ModelRevitPartKind.Room] = [BuiltInCategory.OST_Rooms],
        [ModelRevitPartKind.Ceiling] = [BuiltInCategory.OST_Ceilings],
        [ModelRevitPartKind.Pipe] = [BuiltInCategory.OST_PipeCurves],
        [ModelRevitPartKind.Duct] = [BuiltInCategory.OST_DuctCurves],
        [ModelRevitPartKind.CableTray] = [BuiltInCategory.OST_CableTray],
        [ModelRevitPartKind.Equipment] = [BuiltInCategory.OST_MechanicalEquipment]
    };

    private static readonly ModelRevitPartKind[] StructureParts =
    [
        ModelRevitPartKind.Column, ModelRevitPartKind.StructuralWall, ModelRevitPartKind.Beam, ModelRevitPartKind.Floor,
        ModelRevitPartKind.IsolatedFoundation, ModelRevitPartKind.PileCap, ModelRevitPartKind.RaftFoundation,
        ModelRevitPartKind.Pile, ModelRevitPartKind.StripFoundation
    ];

    private static readonly ModelRevitPartKind[] ArchitectureParts =
    [
        ModelRevitPartKind.StructuralWall, ModelRevitPartKind.Floor, ModelRevitPartKind.Door,
        ModelRevitPartKind.Window, ModelRevitPartKind.Room, ModelRevitPartKind.Ceiling
    ];

    private static readonly ModelRevitPartKind[] MepfParts =
    [
        ModelRevitPartKind.Pipe, ModelRevitPartKind.Duct, ModelRevitPartKind.CableTray, ModelRevitPartKind.Equipment
    ];

    public static ModelRevitPartKind ParsePart(string? value) =>
        Enum.TryParse<ModelRevitPartKind>(value, true, out var kind) ? kind : ModelRevitPartKind.Column;

    public static string ParseDiscipline(string? value) =>
        value is "Architecture" or "Mepf" or "Structure" ? value : "Structure";

    public static string Describe(string? package, string? part, string? discipline) => package switch
    {
        Builtin => "Gói: tham số sẵn có của Revit.",
        ByPart => $"Gói: theo phần đang dựng ({PartLabel(ParsePart(part))}).",
        ByDiscipline => $"Gói: theo bộ môn ({DisciplineLabel(ParseDiscipline(discipline))}).",
        Full => "Gói: đủ bộ QS_*.",
        _ => "Chưa chọn gói. Mặc định khi gắn: theo phần đang dựng."
    };

    public static IReadOnlyList<string> Names(string package, string? part, string? discipline)
    {
        if (package == Builtin || package == Full)
            return Array.Empty<string>();
        var kinds = package == ByDiscipline ? KindsForDiscipline(ParseDiscipline(discipline)) : new[] { ParsePart(part) };
        return kinds.SelectMany(k => PartParameters.TryGetValue(k, out var names) ? names : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<BuiltInCategory> Categories(string package, string? part, string? discipline)
    {
        var kinds = package == ByDiscipline ? KindsForDiscipline(ParseDiscipline(discipline)) : new[] { ParsePart(part) };
        return kinds.SelectMany(k => PartCategories.TryGetValue(k, out var cats) ? cats : Array.Empty<BuiltInCategory>())
            .Distinct()
            .ToArray();
    }

    public static string PartLabel(ModelRevitPartKind kind)
    {
        foreach (var item in Parts)
        {
            if (item.Kind == kind)
                return item.Label;
        }

        return kind.ToString();
    }

    public static string DisciplineLabel(string id)
    {
        foreach (var item in Disciplines)
        {
            if (item.Id == id)
                return item.Label;
        }

        return id;
    }

    private static ModelRevitPartKind[] KindsForDiscipline(string discipline) => discipline switch
    {
        "Architecture" => ArchitectureParts,
        "Mepf" => MepfParts,
        _ => StructureParts
    };
}
