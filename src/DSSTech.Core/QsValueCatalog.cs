namespace DSSTech.Core;

/// <summary>
/// Canonical pick-lists for QS selector values (engine-facing names).
/// Only enum / discrete values — continuous geometry (Height, Volume) stays from BIM.
/// </summary>
public static class QsValueCatalog
{
    public static readonly IReadOnlyDictionary<string, string[]> Values =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // --- Classification override ---
            ["WorkItemId"] =
            [
                "XD-CONC", "XD-REBAR", "XD-FORM", "XD-MASON-BRICK", "XD-MASON-AAC", "XD-MASON-STONE", "XD-MASON-REF",
                "XD-PLASTER", "XD-TILE", "XD-PAINT", "XD-WATERPROOF", "XD-CLAD", "XD-SCREED", "XD-CEILING", "XD-ROOF",
                "XD-CARPENTRY", "XD-TIMBER", "XD-STEEL-FAB", "XD-STEEL-ERECT", "XD-PRECAST-PROD", "XD-PRECAST-ERECT",
                "XD-EARTH-EXC", "XD-EARTH-FILL", "XD-EARTH-TRANS", "XD-PILE-DRIVE", "XD-PILE-BORED", "XD-PILE-SHEET",
                "XD-ROAD-BASE", "XD-ROAD-SURF", "XD-DEMO", "XD-SCAFF",
                "MEP-PIPE-STEEL", "MEP-PIPE-PLASTIC", "MEP-PIPE-COPPER", "MEP-PIPE-CI", "MEP-PIPE-CONC",
                "MEP-FITTING", "MEP-VALVE", "MEP-PLUMB-FIX", "MEP-HVAC-AC", "MEP-HVAC-FAN", "MEP-INSUL",
                "MEP-CONDUIT", "MEP-WIRE", "MEP-LIGHT", "MEP-SWITCH", "MEP-FIRE-CTRL",
                "MEP-HDD", "MEP-WELL", "MEP-LPS", "MEP-PROTECT", "MEP-PUBLIC-LIGHT"
            ],

            // --- Structure / concrete ---
            ["ElementType"] =
            [
                "Foundation", "Blinding", "Column", "Wall", "Slab", "Beam", "MachineBase",
                "Stair", "Ramp", "Pile", "Footing", "PileCap", "GradeBeam", "RetainingWall"
            ],
            ["PlacementMethod"] = ["Manual", "Crane", "Pump"],
            ["ConcreteSupply"] = ["ReadyMix", "SiteMix", "SiteBatchPlant"],
            ["PumpType"] = ["Static", "Mobile"],
            ["ConcreteGrade"] = ["B15", "B20", "B25", "B30", "B35", "B40", "B45", "B50"],
            ["Environment"] = ["Dry", "Underwater", "Tunnel", "Marine", "Indoor", "Outdoor"],
            ["FormworkSystem"] = ["PlywoodFrame", "Steel", "Wood", "Aluminum", "Climbing"],
            ["FormworkMaterial"] = ["Plywood", "Steel", "Timber", "Plastic"],
            ["StairType"] = ["Regular", "Spiral"],
            ["SteelGrade"] = ["CB240-T", "CB300-T", "CB400-V", "CB500-V", "SD295", "SD390", "SD490"],
            ["Prestress"] = ["None", "PreTension", "PostTension"],

            // --- Masonry / finishes ---
            ["BrickType"] = ["Clay", "Concrete", "AAC", "SandLime", "Clinker"],
            ["BlockType"] = ["AAC", "Concrete", "Hollow", "Solid"],
            ["MortarGrade"] = ["M25", "M50", "M75", "M100", "M150"],
            ["MortarType"] = ["Cement", "Lime", "CementLime", "ThinBed"],
            ["SurfaceLocation"] = ["Interior", "Exterior", "Wall", "Ceiling", "Floor", "Soffit"],
            ["InOutBuilding"] = ["Interior", "Exterior", "Mixed"],
            ["CoatSystem"] = ["1P1F", "1P2F", "2P2F", "PrimerOnly"],
            ["PrePutty"] = ["true", "false"],
            ["TileStoneType"] = ["Ceramic", "Porcelain", "Granite", "Marble", "Terrazzo", "Stone", "Mosaic"],
            ["TileArea"] = ["0.023", "0.04", "0.06", "0.09"],
            ["Pattern"] = ["Straight", "Diagonal", "Herringbone", "Brick", "Modular"],
            ["CeilingType"] = ["Gypsum", "Metal", "MineralFiber", "Wood", "Stretch"],
            ["FrameType"] = ["Exposed", "Concealed", "DirectFix"],
            ["PanelType"] = ["600x600", "600x1200", "1200x1200", "Custom"],
            ["RoofMaterial"] = ["MetalSheet", "Tile", "Membrane", "Concrete", "Shingle"],
            ["WaterproofType"] = ["Membrane", "Coat", "Bitumen", "PU", "Cementitious"],

            // --- MEP ---
            ["PipeMaterial"] = ["PVC", "HDPE", "PPR", "Steel", "Galvanized", "Copper", "CastIron", "Concrete", "PEX", "ABS"],
            ["JointType"] = ["Threaded", "Welded", "Flanged", "Socket", "Grooved", "Compression", "Fusion", "Solvent"],
            ["SystemType"] =
            [
                "DomesticCold", "DomesticHot", "Drainage", "Storm", "Fire", "ChilledWater", "HotWater",
                "Condensate", "Gas", "CompressedAir", "SupplyAir", "ReturnAir", "Exhaust", "Power", "Lighting", "ELV", "Data"
            ],
            ["EquipmentType"] =
            [
                "Fan", "AHU", "FCU", "SplitAC", "VRV", "Pump", "Boiler", "Chiller", "CoolingTower",
                "PanelBoard", "Transformer", "Generator", "Luminaire", "Sprinkler", "FireAlarm"
            ],
            ["FixtureType"] = ["Toilet", "Sink", "Lavatory", "Urinal", "Bidet", "Shower", "Bathtub"],
            ["ValveType"] = ["Gate", "Globe", "Ball", "Butterfly", "Check", "PRV"],
            ["DeviceType"] =
            [
                "Switch", "Outlet", "Socket", "Breaker", "Aptomat", "MCB",
                "FireAlarm", "Detector", "Smoke", "Heat", "MCP", "CallPoint"
            ],
            ["ProductType"] = ["HDPE", "Pipe", "Cable"],
            ["DrillMethod"] = ["CablePercussion", "Rotary", "Auger", "HDD"],
            ["SoilRockClass"] = ["Soft", "Medium", "Hard", "Rock", "Sand", "Clay"],
            ["InsulationMaterial"] = ["Rockwool", "Glasswool", "Foam", "Rubber", "PIR", "PUR", "Elastomeric"],
            ["InsulationThickness"] = ["25", "30", "50", "100"],

            // --- Earth / piles / road (common discrete) ---
            ["SoilClass"] = ["Soft", "Medium", "Hard", "Rock", "Sand", "Clay", "Fill"],
            ["RockClass"] = ["SoftRock", "MediumRock", "HardRock"],
            ["PileMaterial"] = ["Concrete", "Steel", "Timber", "Composite"],
            ["PileType"] = ["Driven", "Bored", "CFA", "Sheet", "Micropile"],
            ["SteelActivity"] = ["Fabrication", "Erection"],
            ["SurfaceType"] = ["Asphalt", "Concrete", "Pavers", "Gravel"],
            ["AsphaltType"] = ["AC", "SMA", "PMA", "BaseCourse"],

            // --- Discrete numeric bands (stored as text/number) ---
            ["PaintThickness"] = ["1.0", "1.5", "2.0", "3.0", "3.2"],
            ["WallThickness"] = ["10", "11", "15", "20", "22", "30", "33"],
            ["RebarDiameter"] = ["6", "8", "10", "12", "14", "16", "18", "20", "22", "25", "28", "32"]
        };

    /// <summary>Map engine fact key → Revit QS_* parameter name.</summary>
    public static readonly IReadOnlyDictionary<string, string> FactToQsParam =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkItemId"] = "QS_WorkItemId",
            ["ElementType"] = "QS_ElementType",
            ["PlacementMethod"] = "QS_PlacementMethod",
            ["ConcreteSupply"] = "QS_ConcreteSupply",
            ["PumpType"] = "QS_PumpType",
            ["ConcreteGrade"] = "QS_ConcreteGrade",
            ["Environment"] = "QS_Environment",
            ["FormworkSystem"] = "QS_FormworkSystem",
            ["FormworkMaterial"] = "QS_FormworkMaterial",
            ["FormworkArea"] = "QS_FormworkArea",
            ["FormworkSide"] = "QS_FormworkSide",
            ["FormworkBottom"] = "QS_FormworkBottom",
            ["FormworkDeduct"] = "QS_FormworkDeduct",
            ["FormworkWaitPour"] = "QS_FormworkWaitPour",
            ["FormworkGroundContact"] = "QS_FormworkGroundContact",
            ["FormworkConfidence"] = "QS_FormworkConfidence",
            ["FormworkStamp"] = "QS_FormworkStamp",
            ["InOutBuilding"] = "QS_InOutBuilding",
            ["InOutSource"] = "QS_InOutSource",
            ["StairType"] = "QS_StairType",
            ["SteelGrade"] = "QS_SteelGrade",
            ["Prestress"] = "QS_Prestress",
            ["BrickType"] = "QS_BrickType",
            ["BlockType"] = "QS_BlockType",
            ["MortarGrade"] = "QS_MortarGrade",
            ["MortarType"] = "QS_MortarType",
            ["SurfaceLocation"] = "QS_SurfaceLocation",
            ["CoatSystem"] = "QS_CoatSystem",
            ["PrePutty"] = "QS_PrePutty",
            ["TileStoneType"] = "QS_TileStoneType",
            ["TileArea"] = "QS_TileArea",
            ["Pattern"] = "QS_Pattern",
            ["CeilingType"] = "QS_CeilingType",
            ["FrameType"] = "QS_FrameType",
            ["PanelType"] = "QS_PanelType",
            ["RoofMaterial"] = "QS_RoofMaterial",
            ["WaterproofType"] = "QS_WaterproofType",
            ["PipeMaterial"] = "QS_PipeMaterial",
            ["JointType"] = "QS_JointType",
            ["SystemType"] = "QS_SystemType",
            ["EquipmentType"] = "QS_EquipmentType",
            ["DeviceType"] = "QS_DeviceType",
            ["ComponentType"] = "QS_ComponentType",
            ["FixtureType"] = "QS_FixtureType",
            ["ValveType"] = "QS_ValveType",
            ["InsulationMaterial"] = "QS_InsulationMaterial",
            ["InsulationThickness"] = "QS_InsulationThickness",
            ["ProductType"] = "QS_ProductType",
            ["DrillMethod"] = "QS_DrillMethod",
            ["SoilRockClass"] = "QS_SoilRockClass",
            ["SoilClass"] = "QS_SoilClass",
            ["RockClass"] = "QS_RockClass",
            ["PileMaterial"] = "QS_PileMaterial",
            ["PileType"] = "QS_PileType",
            ["SteelActivity"] = "QS_SteelActivity",
            ["SurfaceType"] = "QS_SurfaceType",
            ["AsphaltType"] = "QS_AsphaltType",
            ["PaintThickness"] = "QS_PaintThickness",
            ["WallThickness"] = "QS_WallThickness",
            ["RebarDiameter"] = "QS_RebarDiameter"
        };

    /// <summary>Display order for assign UI (all disciplines).</summary>
    public static readonly string[] UiOrder =
    [
        "WorkItemId", "ElementType",
        "PlacementMethod", "ConcreteSupply", "PumpType", "ConcreteGrade", "Environment",
        "FormworkSystem", "FormworkMaterial", "StairType", "SteelGrade", "Prestress", "RebarDiameter",
        "BrickType", "BlockType", "MortarGrade", "MortarType", "WallThickness",
        "InOutBuilding", "SurfaceLocation", "CoatSystem", "PrePutty", "TileStoneType", "TileArea", "Pattern",
        "CeilingType", "FrameType", "PanelType", "RoofMaterial", "WaterproofType", "PaintThickness",
        "PipeMaterial", "JointType", "SystemType", "EquipmentType", "DeviceType", "ComponentType",
        "FixtureType", "ValveType", "InsulationMaterial", "InsulationThickness",
        "ProductType", "DrillMethod", "SoilRockClass",
        "SoilClass", "RockClass", "PileMaterial", "PileType", "SteelActivity", "SurfaceType", "AsphaltType"
    ];

    public static readonly IReadOnlyDictionary<string, string> LabelsVi =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkItemId"] = "Mã công tác BIM (WorkItemId)",
            ["ElementType"] = "Loại cấu kiện (ElementType)",
            ["PlacementMethod"] = "Biện pháp đổ (PlacementMethod)",
            ["ConcreteSupply"] = "Nguồn bê tông (ConcreteSupply)",
            ["PumpType"] = "Loại bơm (PumpType)",
            ["ConcreteGrade"] = "Mác bê tông (ConcreteGrade)",
            ["Environment"] = "Môi trường thi công (Environment)",
            ["FormworkSystem"] = "Hệ ván khuôn (FormworkSystem)",
            ["FormworkMaterial"] = "Vật liệu ván khuôn (FormworkMaterial)",
            ["StairType"] = "Loại cầu thang (StairType)",
            ["SteelGrade"] = "Mác thép (SteelGrade)",
            ["Prestress"] = "Ứng suất trước (Prestress)",
            ["RebarDiameter"] = "Đường kính cốt thép mm (RebarDiameter)",
            ["BrickType"] = "Loại gạch (BrickType)",
            ["BlockType"] = "Loại block (BlockType)",
            ["MortarGrade"] = "Mác vữa (MortarGrade)",
            ["MortarType"] = "Loại vữa (MortarType)",
            ["WallThickness"] = "Chiều dày tường cm (WallThickness)",
            ["SurfaceLocation"] = "Vị trí bề mặt (SurfaceLocation)",
            ["InOutBuilding"] = "Trong/ngoài nhà (InOutBuilding)",
            ["InOutSource"] = "Nguồn InOut (InOutSource)",
            ["CoatSystem"] = "Hệ lớp sơn (CoatSystem)",
            ["PrePutty"] = "Đã bả (PrePutty)",
            ["TileStoneType"] = "Loại gạch/đá (TileStoneType)",
            ["TileArea"] = "Diện tích viên m² (TileArea)",
            ["Pattern"] = "Hoa văn lát (Pattern)",
            ["CeilingType"] = "Loại trần (CeilingType)",
            ["FrameType"] = "Khung trần (FrameType)",
            ["PanelType"] = "Tấm trần (PanelType)",
            ["RoofMaterial"] = "Vật liệu mái (RoofMaterial)",
            ["WaterproofType"] = "Loại chống thấm (WaterproofType)",
            ["PaintThickness"] = "Chiều dày sơn mm (PaintThickness)",
            ["PipeMaterial"] = "Vật liệu ống (PipeMaterial)",
            ["JointType"] = "Kiểu nối (JointType)",
            ["SystemType"] = "Hệ MEP (SystemType)",
            ["EquipmentType"] = "Loại thiết bị (EquipmentType)",
            ["DeviceType"] = "Loại thiết bị điện (DeviceType)",
            ["ComponentType"] = "Loại cấu phần (ComponentType)",
            ["FixtureType"] = "Loại thiết bị VS (FixtureType)",
            ["ValveType"] = "Loại van (ValveType)",
            ["InsulationMaterial"] = "Vật liệu bảo ôn (InsulationMaterial)",
            ["InsulationThickness"] = "Chiều dày bảo ôn mm (InsulationThickness)",
            ["ProductType"] = "Loại sản phẩm HDD (ProductType)",
            ["DrillMethod"] = "Phương pháp khoan (DrillMethod)",
            ["SoilRockClass"] = "Cấp đất/đá (SoilRockClass)",
            ["SoilClass"] = "Cấp đất (SoilClass)",
            ["RockClass"] = "Cấp đá (RockClass)",
            ["PileMaterial"] = "Vật liệu cọc (PileMaterial)",
            ["PileType"] = "Loại cọc (PileType)",
            ["SteelActivity"] = "Công đoạn thép (SteelActivity)",
            ["SurfaceType"] = "Loại mặt đường (SurfaceType)",
            ["AsphaltType"] = "Loại asphalt (AsphaltType)"
        };

    public static IReadOnlyList<string> For(string factKey) =>
        Values.TryGetValue(factKey, out var list) ? list : Array.Empty<string>();

    public static string? ToQsParam(string factKey) =>
        FactToQsParam.TryGetValue(factKey, out var name) ? name : null;

    public static string Label(string factKey) =>
        LabelsVi.TryGetValue(factKey, out var label) ? label : factKey;
}
