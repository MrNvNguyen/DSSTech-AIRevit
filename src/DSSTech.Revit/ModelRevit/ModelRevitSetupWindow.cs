using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Dss.Cad2Bim.Core;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.ModelRevit;

internal sealed class ModelRevitFormChoice
{
    public ModelRevitPartKind Part { get; init; }
    public string Layer { get; init; } = "";
    public string BaseLevel { get; init; } = "";
    public string TopLevel { get; init; } = "";
    public double BaseOffsetMm { get; init; }
    public double TopOffsetMm { get; init; }
    public string Family { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string SectionShape { get; init; } = "";
    public string WidthParam { get; init; } = "";
    public string HeightParam { get; init; } = "";
    public string Material { get; init; } = "";
    public bool AssignMark { get; init; }
    public string Function { get; init; } = "";
    public string SystemName { get; init; } = "";
    public double? ThicknessMm { get; init; }
    /// <summary>Bored / Pressed / Driven — filters Family : Type list for piles.</summary>
    public string PileSubtype { get; init; } = "";
    /// <summary>When true, BaseOffsetMm is đáy đài; pile top = đáy + EmbedMm.</summary>
    public bool PileFromCapBottom { get; init; }
    /// <summary>Ngàm cọc vào đài (mm), typically 100–200.</summary>
    public double EmbedMm { get; init; }
}

internal sealed class ModelRevitFormCatalog
{
    public IReadOnlyList<string> Levels { get; init; } = Array.Empty<string>();
    public string BaseLevel { get; init; } = "";
    public string TopLevel { get; init; } = "";
    public IReadOnlyList<string> Layers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ColumnFamilies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BeamFamilies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DoorFamilies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WindowFamilies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EquipmentFamilies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BeamTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WallTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FloorTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FoundationTypes { get; init; } = Array.Empty<string>();
    /// <summary>Type Mark values on Structural Foundation symbols (Đài cọc CAD labels: Đ4, Đ3…).</summary>
    public IReadOnlyList<string> FoundationTypeMarks { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CeilingTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Materials { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PipeSystems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PipeTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DuctSystems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DuctTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CableTrayTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> LengthParams { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();

    public static ModelRevitFormCatalog Load(Document doc, Level baseLevel, Level topLevel, ImportInstance? import)
    {
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => l.Name)
            .ToList();
        return new ModelRevitFormCatalog
        {
            Levels = levels,
            BaseLevel = baseLevel.Name,
            TopLevel = topLevel.Name,
            Layers = LayersOf(import),
            ColumnFamilies = Dss.McpHost.Create.CreateBatchTool.ListRectangularColumnFamilies(doc),
            BeamFamilies = Dss.McpHost.Create.CreateBatchTool.ListRectangularBeamFamilies(doc),
            DoorFamilies = SymbolNames(doc, BuiltInCategory.OST_Doors),
            WindowFamilies = SymbolNames(doc, BuiltInCategory.OST_Windows),
            EquipmentFamilies = SymbolNames(doc, BuiltInCategory.OST_MechanicalEquipment),
            BeamTypes = SymbolNames(doc, BuiltInCategory.OST_StructuralFraming),
            WallTypes = TypeNames<WallType>(doc),
            FloorTypes = TypeNames<FloorType>(doc),
            FoundationTypes = SymbolNames(doc, BuiltInCategory.OST_StructuralFoundation),
            FoundationTypeMarks = FoundationTypeMarkValues(doc),
            CeilingTypes = TypeNames<CeilingType>(doc),
            Materials = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                .Select(m => m.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PipeSystems = TypeNames<PipingSystemType>(doc),
            PipeTypes = TypeNames<PipeType>(doc),
            DuctSystems = TypeNames<MechanicalSystemType>(doc),
            DuctTypes = TypeNames<DuctType>(doc),
            CableTrayTypes = TypeNames<CableTrayType>(doc),
            LengthParams = CollectLengthParams(doc)
        };
    }

    private static List<string> LayersOf(ImportInstance? import)
    {
        var names = new List<string>();
        if (import?.Category?.SubCategories is null)
            return names;
        foreach (Category sub in import.Category.SubCategories)
        {
            if (!string.IsNullOrWhiteSpace(sub.Name))
                names.Add(sub.Name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static List<string> SymbolNames(Document doc, BuiltInCategory category) =>
        new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(category)
            .Cast<FamilySymbol>()
            .Select(s => s.FamilyName + " : " + s.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Collect Đài cọc lookup keys: Type Mark param, plus pile-cap id prefix of type name
    /// when Type Mark is empty (<c>Đ4A-1000x1000x1100mm</c> → <c>Đ4A</c>).
    /// </summary>
    private static List<string> FoundationTypeMarkValues(Document doc)
    {
        var marks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                     .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                     .Cast<FamilySymbol>())
        {
            try
            {
                var tm = symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString();
                if (!string.IsNullOrWhiteSpace(tm))
                    marks.Add(tm.Trim());
            }
            catch
            {
                // ignore locked/unavailable params
            }

            if (CadMark.TryExtractPileCapId(symbol.Name, out var fromName))
                marks.Add(fromName);
            else if (CadMark.TryExtractPileCapId(symbol.FamilyName + " : " + symbol.Name, out var fromFull))
                marks.Add(fromFull);
        }

        return marks.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> TypeNames<T>(Document doc) where T : Element =>
        new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>()
            .Select(t => t.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Dictionary<string, IReadOnlyList<string>> CollectLengthParams(Document doc)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
        {
            var key = symbol.FamilyName;
            var symbolKey = symbol.FamilyName + " : " + symbol.Name;
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (map.TryGetValue(key, out var existing))
            {
                map[symbolKey] = existing;
                continue;
            }

            var names = new List<string>();
            foreach (Parameter parameter in symbol.Parameters)
            {
                if (parameter.StorageType != StorageType.Double)
                    continue;
                var name = parameter.Definition?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            var stored = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            map[key] = stored;
            map[symbolKey] = stored;
        }

        return map;
    }
}

internal sealed class ModelRevitSetupWindow : DssDialog
{
    private readonly ModelRevitFormCatalog _catalog;
    private readonly ComboBox _part;
    private readonly ComboBox _layer;
    private readonly ComboBox _family;
    private readonly ComboBox _shape;
    private readonly ComboBox _widthParam;
    private readonly ComboBox _heightParam;
    private readonly ComboBox _material;
    private readonly ComboBox _type;
    private readonly ComboBox _function;
    private readonly ComboBox _system;
    private readonly TextBox _thickness;
    private readonly CheckBox _mark;
    private readonly ComboBox _pileSubtype;
    private readonly CheckBox _fromCapBottom;
    private readonly TextBox _embedMm;
    private readonly ComboBox _baseLevel;
    private readonly ComboBox _topLevel;
    private readonly TextBox _baseOffset;
    private readonly TextBox _topOffset;
    private readonly TextBlock _typeLabel;
    private readonly TextBlock _systemLabel;
    private readonly TextBlock _baseOffsetLabel;
    private readonly StackPanel _familyRow;
    private readonly StackPanel _shapeRow;
    private readonly System.Windows.Controls.Grid _paramRow;
    private readonly StackPanel _materialRow;
    private readonly StackPanel _typeRow;
    private readonly StackPanel _functionRow;
    private readonly StackPanel _thickRow;
    private readonly StackPanel _systemRow;
    private readonly StackPanel _pileSubtypeRow;
    private readonly StackPanel _fromCapRow;
    private readonly StackPanel _embedRow;
    private readonly UIElement _markRow;
    private readonly StackPanel _baseRow;
    private readonly StackPanel _topRow;
    private readonly StackPanel _baseOffsetRow;
    private readonly StackPanel _topOffsetRow;
    public ModelRevitFormChoice? Choice { get; private set; }

    private static readonly (string Label, string Id)[] PileSubtypeItems =
    {
        ("Cọc khoan nhồi / Bored", FoundationCatalogFilter.PileSubtypeBored),
        ("Cọc ép / Pressed (PHC)", FoundationCatalogFilter.PileSubtypePressed),
        ("Cọc đóng / Driven", FoundationCatalogFilter.PileSubtypeDriven)
    };

    private ModelRevitSetupWindow(ModelRevitFormCatalog catalog, ModelRevitPartKind? presetPart)
        : base(
            presetPart is ModelRevitPartKind titlePart ? $"DSSTech — {ParamPackageCatalog.PartLabel(titlePart)}" : "DSSTech — Dựng từ CAD",
            "Chỉnh đầu vào rồi dựng. Cột và tường đo tiết diện từ nét vẽ. Loại khác vẫn đọc Text hoặc Dimension. Để trống layer thì pick nét trên bản vẽ.",
            640,
            720)
    {
        _catalog = catalog;
        var partLabel = presetPart is ModelRevitPartKind preset
            ? ParamPackageCatalog.PartLabel(preset)
            : ParamPackageCatalog.Parts[0].Label;
        _part = DssTheme.Combo(ParamPackageCatalog.Parts.Select(p => p.Label), partLabel);
        _layer = DssTheme.Combo();
        _layer.Items.Add("(để trống — pick nét trên bản vẽ)");
        foreach (var layer in catalog.Layers)
            _layer.Items.Add(layer);
        _layer.SelectedIndex = 0;

        _family = DssTheme.Combo();
        _shape = DssTheme.Combo(new[] { "Chữ nhật", "Tròn" }, "Chữ nhật");
        _widthParam = DssTheme.Combo();
        _heightParam = DssTheme.Combo();
        _material = DssTheme.Combo(WithBlank(catalog.Materials));
        _type = DssTheme.Combo();
        // Structural walls default to CoreShaft (Lõi). Exterior/Interior are for architectural walls;
        // kept in the list only as an explicit override.
        _function = DssTheme.Combo(
            new[] { "Lõi", "Nội thất", "Ngoại thất", "Móng", "Tường chắn", "Mặt dưới" },
            "Lõi");
        _system = DssTheme.Combo();
        _thickness = DssTheme.Input("");
        _mark = DssTheme.Option("Gán Mark từ text lân cận");
        _pileSubtype = DssTheme.Combo(PileSubtypeItems.Select(p => p.Label), PileSubtypeItems[1].Label);
        _fromCapBottom = DssTheme.Option("Đỉnh cọc theo đáy đài + ngàm");
        _embedMm = DssTheme.Input("150");
        _baseLevel = DssTheme.Combo(catalog.Levels, catalog.BaseLevel);
        _topLevel = DssTheme.Combo(catalog.Levels, catalog.TopLevel);
        _baseOffset = DssTheme.Input("0");
        _topOffset = DssTheme.Input("0");
        _typeLabel = new TextBlock { Text = "Type", Foreground = DssTheme.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        _systemLabel = new TextBlock { Text = "Hệ thống", Foreground = DssTheme.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        _baseOffsetLabel = new TextBlock { Text = "Offset đáy (mm)", Foreground = DssTheme.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };

        _familyRow = DssTheme.Field("Family", _family);
        _shapeRow = DssTheme.Field("Hình tiết diện", _shape);
        _paramRow = DssTheme.Pair(DssTheme.Field("Tham số rộng", _widthParam), DssTheme.Field("Tham số cao", _heightParam));
        _materialRow = DssTheme.Field("Vật liệu", _material);
        _typeRow = new StackPanel();
        _typeRow.Children.Add(_typeLabel);
        _typeRow.Children.Add(_type);
        _functionRow = DssTheme.Field("Chức năng", _function);
        _thickRow = DssTheme.Field("Bề dày hoặc tiết diện (mm)", _thickness);
        _systemRow = new StackPanel();
        _systemRow.Children.Add(_systemLabel);
        _systemRow.Children.Add(_system);
        _pileSubtypeRow = DssTheme.Field("Loại cọc", _pileSubtype);
        _fromCapRow = new StackPanel { Children = { _fromCapBottom } };
        _embedRow = DssTheme.Field("Ngàm vào đài (mm)", _embedMm);
        _markRow = _mark;
        _baseRow = DssTheme.Field("Level đáy", _baseLevel);
        _topRow = DssTheme.Field("Level đỉnh", _topLevel);
        _baseOffsetRow = new StackPanel();
        _baseOffsetRow.Children.Add(_baseOffsetLabel);
        _baseOffsetRow.Children.Add(_baseOffset);
        _topOffsetRow = DssTheme.Field("Offset đỉnh (mm)", _topOffset);

        var kindHost = new StackPanel();
        kindHost.Children.Add(new TextBlock
        {
            Text = "Cột/dầm: chọn family có tham số b/h. Dầm đọc Mark/kích thước từ CadText DWG (D1Y19-40x60 → 400×600; 30x140 → 300×1400; một cạnh <3 chữ số → cả hai cm×10). Không ghi Assembly Code. Móng/cọc: Type lọc theo tên family đã load (S-FOUN-*, PHC, Bored…). Đài cọc: kích thước theo Type đã setup — gán Type Mark = tên CAD (Đ4, Đ3…); CAD chỉ chọn type + vị trí.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = DssTheme.Muted,
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var row in new UIElement[]
                 {
                     _pileSubtypeRow, _familyRow, _shapeRow, _paramRow, _materialRow, _typeRow, _functionRow,
                     _thickRow, _fromCapRow, _embedRow, _systemRow, _markRow
                 })
            kindHost.Children.Add(row);

        // From a module button the part is already chosen, so only the layer is left to pick.
        if (presetPart is null)
            AddSection(1, "Loại cấu kiện",
                DssTheme.Pair(DssTheme.Field("Loại", _part), DssTheme.Field("Layer", _layer)));
        else
            AddSection(1, "Layer CAD", DssTheme.Field("Layer", _layer));
        AddSection(2, "Thông số", kindHost);
        AddSection(3, "Level",
            DssTheme.Pair(_baseRow, _topRow),
            DssTheme.Pair(_baseOffsetRow, _topOffsetRow));

        _part.SelectionChanged += (_, _) => ApplyKind();
        _pileSubtype.SelectionChanged += (_, _) =>
        {
            if (SelectedKind() == ModelRevitPartKind.Pile)
                FillTypes(ModelRevitPartKind.Pile);
        };
        _fromCapBottom.Checked += (_, _) => UpdatePileOffsetLabels();
        _fromCapBottom.Unchecked += (_, _) => UpdatePileOffsetLabels();
        _family.SelectionChanged += (_, _) =>
        {
            FillLengthParams();
            if (SelectedKind() == ModelRevitPartKind.Beam)
                FillBeamTypes();
        };
        PrimaryButton.Click += (_, _) => Accept();
        ApplyKind();
    }

    public static bool TryPick(
        IntPtr owner, ModelRevitFormCatalog catalog, out ModelRevitFormChoice choice, ModelRevitPartKind? presetPart = null)
    {
        choice = null!;
        var dlg = new ModelRevitSetupWindow(catalog, presetPart);
        dlg.SetOwner(owner);
        if (dlg.ShowDialog() != true || dlg.Choice is null)
            return false;
        choice = dlg.Choice;
        return true;
    }

    private void ApplyKind()
    {
        var kind = SelectedKind();
        PrimaryButton.Content = Verb(kind);
        // Structural wall on a shared column layer can also place short footprints as columns.
        var families = kind == ModelRevitPartKind.StructuralWall
            ? _catalog.ColumnFamilies
            : FamiliesFor(kind);
        Show(_familyRow, families is not null && families.Count > 0);
        if (families is not null && families.Count > 0)
        {
            if (kind is ModelRevitPartKind.StructuralWall or ModelRevitPartKind.Beam)
                DssTheme.FillCombo(_family, WithBlank(families), "");
            else
                DssTheme.FillCombo(_family, families, null);
        }
        Show(_shapeRow, kind == ModelRevitPartKind.Column);
        Show(_paramRow, kind is ModelRevitPartKind.Column or ModelRevitPartKind.Beam
            or ModelRevitPartKind.Door or ModelRevitPartKind.Window);
        Show(_markRow, kind is ModelRevitPartKind.Column or ModelRevitPartKind.Beam
            or ModelRevitPartKind.Pile or ModelRevitPartKind.PileCap or ModelRevitPartKind.IsolatedFoundation
            or ModelRevitPartKind.StripFoundation or ModelRevitPartKind.RaftFoundation
            or ModelRevitPartKind.Door or ModelRevitPartKind.Window);
        if (kind is ModelRevitPartKind.Pile or ModelRevitPartKind.PileCap
            or ModelRevitPartKind.IsolatedFoundation)
            _mark.IsChecked = true;
        Show(_materialRow, kind is ModelRevitPartKind.Column or ModelRevitPartKind.StructuralWall
            or ModelRevitPartKind.ArchitecturalWall or ModelRevitPartKind.Beam or ModelRevitPartKind.Floor or ModelRevitPartKind.IsolatedFoundation
            or ModelRevitPartKind.PileCap or ModelRevitPartKind.RaftFoundation
            or ModelRevitPartKind.Pile or ModelRevitPartKind.StripFoundation);
        Show(_functionRow, kind == ModelRevitPartKind.StructuralWall);
        Show(_thickRow, kind is ModelRevitPartKind.StructuralWall or ModelRevitPartKind.ArchitecturalWall or ModelRevitPartKind.Floor
            or ModelRevitPartKind.IsolatedFoundation or ModelRevitPartKind.PileCap
            or ModelRevitPartKind.RaftFoundation or ModelRevitPartKind.Pile or ModelRevitPartKind.StripFoundation);
        Show(_typeRow, kind is ModelRevitPartKind.StructuralWall or ModelRevitPartKind.ArchitecturalWall
            or ModelRevitPartKind.Beam or ModelRevitPartKind.Floor
            or ModelRevitPartKind.IsolatedFoundation or ModelRevitPartKind.PileCap or ModelRevitPartKind.RaftFoundation
            or ModelRevitPartKind.Pile or ModelRevitPartKind.StripFoundation
            or ModelRevitPartKind.Ceiling or ModelRevitPartKind.Pipe or ModelRevitPartKind.Duct
            or ModelRevitPartKind.CableTray);
        Show(_systemRow, kind is ModelRevitPartKind.Pipe or ModelRevitPartKind.Duct);
        Show(_pileSubtypeRow, kind == ModelRevitPartKind.Pile);
        Show(_fromCapRow, kind == ModelRevitPartKind.Pile);
        Show(_embedRow, kind == ModelRevitPartKind.Pile && _fromCapBottom.IsChecked == true);

        var bothLevels = kind is ModelRevitPartKind.Column or ModelRevitPartKind.StructuralWall
            or ModelRevitPartKind.ArchitecturalWall;
        // Beams + piles: one reference level (= open-plan GenLevel by default) + one offset.
        var levelOnly = kind is ModelRevitPartKind.Beam or ModelRevitPartKind.Pile;
        Show(_baseRow, !levelOnly);
        Show(_topRow, bothLevels || levelOnly);
        Show(_baseOffsetRow, kind != ModelRevitPartKind.Beam && kind != ModelRevitPartKind.Pile
            && kind != ModelRevitPartKind.Room);
        Show(_topOffsetRow, bothLevels || levelOnly);
        _baseOffsetLabel.Text = kind == ModelRevitPartKind.Ceiling ? "Cao độ bù (mm)" : "Offset đáy (mm)";
        // Beams/piles: Catalog.TopLevel is the floor above (for columns/walls); do not default onto that.
        if (_topRow.Children[0] is TextBlock topLevelLabel)
            topLevelLabel.Text = levelOnly ? "Level" : "Level đỉnh";
        if (_topOffsetRow.Children[0] is TextBlock topOffsetLabel)
            topOffsetLabel.Text = levelOnly ? "Offset (mm)" : "Offset đỉnh (mm)";
        if (_thickRow.Children[0] is TextBlock thickLabel)
            thickLabel.Text = kind == ModelRevitPartKind.Pile
                ? "Chiều sâu cọc (mm)"
                : kind is ModelRevitPartKind.IsolatedFoundation or ModelRevitPartKind.PileCap
                    or ModelRevitPartKind.RaftFoundation or ModelRevitPartKind.StripFoundation
                    ? "Chiều dày móng (mm, tùy chọn)"
                    : "Bề dày hoặc tiết diện (mm)";
        if (levelOnly)
        {
            DssTheme.FillCombo(_topLevel, _catalog.Levels, _catalog.BaseLevel);
            if (ParseMm(_topOffset.Text) == 0 && ParseMm(_baseOffset.Text) != 0)
                _topOffset.Text = _baseOffset.Text;
        }
        UpdatePileOffsetLabels();
        _typeLabel.Text = kind switch
        {
            ModelRevitPartKind.Pipe => "Loại ống",
            ModelRevitPartKind.Duct => "Loại gió",
            ModelRevitPartKind.CableTray => "Loại máng",
            ModelRevitPartKind.Beam => "Family / type",
            ModelRevitPartKind.Pile => "Family / type cọc",
            ModelRevitPartKind.PileCap => "Family / type đài (lọc; Type Mark = tên CAD)",
            ModelRevitPartKind.IsolatedFoundation => "Family / type móng đơn",
            ModelRevitPartKind.StripFoundation => "Family / type móng băng",
            ModelRevitPartKind.RaftFoundation => "Family / type móng bè",
            _ => "Type"
        };
        _systemLabel.Text = "Hệ thống";
        FillTypes(kind);
        FillLengthParams();
    }

    private void UpdatePileOffsetLabels()
    {
        if (SelectedKind() != ModelRevitPartKind.Pile)
            return;
        var fromCap = _fromCapBottom.IsChecked == true;
        Show(_embedRow, fromCap);
        if (_topOffsetRow.Children[0] is TextBlock topOffsetLabel)
            topOffsetLabel.Text = fromCap ? "Offset đáy đài (mm)" : "Offset đỉnh cọc (mm)";
    }

    private void FillTypes(ModelRevitPartKind kind)
    {
        if (kind == ModelRevitPartKind.Beam)
        {
            FillBeamTypes();
        }
        else
        {
            IReadOnlyList<string> types = kind switch
            {
                ModelRevitPartKind.StructuralWall or ModelRevitPartKind.ArchitecturalWall => _catalog.WallTypes,
                ModelRevitPartKind.Floor => _catalog.FloorTypes,
                ModelRevitPartKind.Pile => FoundationCatalogFilter.FilterPileTypes(
                    _catalog.FoundationTypes, SelectedPileSubtype()),
                ModelRevitPartKind.PileCap => FoundationCatalogFilter.FilterFoundationTypes(
                    _catalog.FoundationTypes, FoundationCatalogFilter.KindPileCap),
                ModelRevitPartKind.IsolatedFoundation => FoundationCatalogFilter.FilterFoundationTypes(
                    _catalog.FoundationTypes, FoundationCatalogFilter.KindIsolated),
                ModelRevitPartKind.StripFoundation => FoundationCatalogFilter.FilterFoundationTypes(
                    _catalog.FoundationTypes, FoundationCatalogFilter.KindStrip),
                ModelRevitPartKind.RaftFoundation => FoundationCatalogFilter.FilterFoundationTypes(
                    _catalog.FoundationTypes, FoundationCatalogFilter.KindRaft),
                ModelRevitPartKind.Ceiling => _catalog.CeilingTypes,
                ModelRevitPartKind.Pipe => _catalog.PipeTypes,
                ModelRevitPartKind.Duct => _catalog.DuctTypes,
                ModelRevitPartKind.CableTray => _catalog.CableTrayTypes,
                _ => Array.Empty<string>()
            };
            DssTheme.FillCombo(_type, WithBlank(types), "");
        }

        IReadOnlyList<string> systems = kind switch
        {
            ModelRevitPartKind.Pipe => _catalog.PipeSystems,
            ModelRevitPartKind.Duct => _catalog.DuctSystems,
            _ => Array.Empty<string>()
        };
        DssTheme.FillCombo(_system, WithBlank(systems), "");
    }

    private string SelectedPileSubtype()
    {
        var label = DssTheme.ComboValue(_pileSubtype);
        foreach (var item in PileSubtypeItems)
        {
            if (item.Label == label)
                return item.Id;
        }

        return FoundationCatalogFilter.PileSubtypePressed;
    }

    /// <summary>
    /// Beam "Family / type" lists only symbols of the selected Family (e.g. S-FRM-RFC-Rec).
    /// Blank Family keeps the full framing catalog so a type-only pick still works.
    /// </summary>
    private void FillBeamTypes()
    {
        var family = DssTheme.ComboValue(_family);
        IReadOnlyList<string> types;
        if (string.IsNullOrWhiteSpace(family))
        {
            types = _catalog.BeamTypes;
        }
        else
        {
            var prefix = family + " : ";
            types = _catalog.BeamTypes
                .Where(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        DssTheme.FillCombo(_type, WithBlank(types), "");
    }

    private void FillLengthParams()
    {
        var family = DssTheme.ComboValue(_family);
        _catalog.LengthParams.TryGetValue(family, out var names);
        names ??= Array.Empty<string>();
        DssTheme.FillCombo(_widthParam, names, Prefer(names, "b", "Width", "W", "Rộng"));
        DssTheme.FillCombo(_heightParam, names, Prefer(names, "h", "Height", "Depth", "D", "Cao"));
    }

    private void Accept()
    {
        var kind = SelectedKind();
        if (kind == ModelRevitPartKind.Column && _catalog.ColumnFamilies.Count == 0)
        {
            MessageBox.Show(this,
                "Không có family cột chữ nhật có tham số b/h. Load family trước khi dựng.",
                "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (kind == ModelRevitPartKind.Beam &&
            _catalog.BeamFamilies.Count == 0 &&
            string.IsNullOrWhiteSpace(BlankToEmpty(DssTheme.ComboValue(_type))))
        {
            MessageBox.Show(this,
                "Không có family dầm chữ nhật có tham số b/h, và chưa chọn type. Load family hoặc chọn type trước khi dựng.",
                "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if ((kind is ModelRevitPartKind.Column or ModelRevitPartKind.Door or ModelRevitPartKind.Window or ModelRevitPartKind.Equipment)
            && FamiliesFor(kind) is { Count: > 0 }
            && string.IsNullOrWhiteSpace(DssTheme.ComboValue(_family)))
        {
            MessageBox.Show(this, "Chọn family trước khi dựng.", "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (kind == ModelRevitPartKind.Beam &&
            string.IsNullOrWhiteSpace(DssTheme.ComboValue(_family)) &&
            string.IsNullOrWhiteSpace(BlankToEmpty(DssTheme.ComboValue(_type))))
        {
            MessageBox.Show(this,
                "Chọn family dầm (để nhân type theo 40x60 từ CadText) hoặc type có sẵn.",
                "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (kind is ModelRevitPartKind.StructuralWall or ModelRevitPartKind.ArchitecturalWall &&
            string.IsNullOrWhiteSpace(BlankToEmpty(DssTheme.ComboValue(_type))))
        {
            MessageBox.Show(this,
                "Chọn type tường gốc (ví dụ S-WALL-RFC400). Bề dày đo từ nét vẽ sẽ nhân type này.",
                "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var layer = DssTheme.ComboValue(_layer);
        if (layer.StartsWith("(để trống", StringComparison.Ordinal))
            layer = "";
        double? thickness = null;
        if (double.TryParse((_thickness.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var thick) && thick > 0)
            thickness = thick;

        var baseOff = ParseMm(_baseOffset.Text);
        var topOff = ParseMm(_topOffset.Text);
        var baseLevel = DssTheme.ComboValue(_baseLevel);
        var topLevel = DssTheme.ComboValue(_topLevel);
        if (kind is ModelRevitPartKind.Beam or ModelRevitPartKind.Pile)
        {
            // One Level + one Offset (top fields). Carry đáy→đỉnh if user typed offset before
            // switching part; keep Base* in sync so summary matches create.
            if (topOff == 0 && baseOff != 0)
                topOff = baseOff;
            if (string.IsNullOrWhiteSpace(topLevel))
                topLevel = baseLevel;
            baseLevel = topLevel;
            baseOff = topOff;
        }

        if (kind == ModelRevitPartKind.Pile)
        {
            if (string.IsNullOrWhiteSpace(BlankToEmpty(DssTheme.ComboValue(_type))))
            {
                MessageBox.Show(this,
                    "Chọn type cọc (ví dụ S-FOUN-PHC-Cir : D400) trước khi dựng.",
                    "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (thickness is null || thickness <= 0)
            {
                MessageBox.Show(this,
                    "Nhập chiều sâu cọc (mm). Đỉnh cọc tại Level đã chọn (+ offset); đáy = đỉnh − chiều sâu.",
                    "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var embed = ParseMm(_embedMm.Text);
            if (_fromCapBottom.IsChecked == true && (embed < 100 || embed > 200))
            {
                var ask = MessageBox.Show(this,
                    $"Ngàm thường 100–200 mm (hiện {embed:0} mm). Tiếp tục?",
                    "DSSTech", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ask != MessageBoxResult.Yes)
                    return;
            }
        }

        if ((kind is ModelRevitPartKind.PileCap or ModelRevitPartKind.IsolatedFoundation
                or ModelRevitPartKind.StripFoundation or ModelRevitPartKind.RaftFoundation)
            && string.IsNullOrWhiteSpace(BlankToEmpty(DssTheme.ComboValue(_type))))
        {
            MessageBox.Show(this,
                "Chọn Family / type móng đã load trong project trước khi dựng.",
                "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fromCap = kind == ModelRevitPartKind.Pile && _fromCapBottom.IsChecked == true;
        var embedMm = fromCap ? ParseMm(_embedMm.Text) : 0;

        Choice = new ModelRevitFormChoice
        {
            Part = kind,
            Layer = layer,
            BaseLevel = baseLevel,
            TopLevel = topLevel,
            BaseOffsetMm = baseOff,
            TopOffsetMm = topOff,
            Family = DssTheme.ComboValue(_family),
            TypeName = BlankToEmpty(DssTheme.ComboValue(_type)),
            SectionShape = DssTheme.ComboValue(_shape) == "Tròn" ? "Circle" : "Rectangle",
            WidthParam = DssTheme.ComboValue(_widthParam),
            HeightParam = DssTheme.ComboValue(_heightParam),
            Material = BlankToEmpty(DssTheme.ComboValue(_material)),
            AssignMark = _mark.IsChecked == true,
            // Architectural walls: Exterior / Interior comes from the envelope in the planner.
            Function = kind == ModelRevitPartKind.ArchitecturalWall ? "" : DssTheme.ComboValue(_function) switch
            {
                "Nội thất" => "Interior",
                "Ngoại thất" => "Exterior",
                "Móng" => "Foundation",
                "Tường chắn" => "Retaining",
                "Mặt dưới" => "Soffit",
                "Lõi" => "CoreShaft",
                // StructuralWall UI defaults to Lõi; blank / unknown → CoreShaft.
                _ => kind == ModelRevitPartKind.StructuralWall ? "CoreShaft" : "Interior"
            },
            SystemName = BlankToEmpty(DssTheme.ComboValue(_system)),
            ThicknessMm = thickness,
            PileSubtype = kind == ModelRevitPartKind.Pile ? SelectedPileSubtype() : "",
            PileFromCapBottom = fromCap,
            EmbedMm = embedMm
        };
        DialogResult = true;
        Close();
    }

    private ModelRevitPartKind SelectedKind()
    {
        var label = DssTheme.ComboValue(_part);
        foreach (var item in ParamPackageCatalog.Parts)
        {
            if (item.Label == label)
                return item.Kind;
        }

        return ModelRevitPartKind.Column;
    }

    private IReadOnlyList<string>? FamiliesFor(ModelRevitPartKind kind) => kind switch
    {
        ModelRevitPartKind.Column => _catalog.ColumnFamilies,
        ModelRevitPartKind.Beam => _catalog.BeamFamilies,
        ModelRevitPartKind.Door => _catalog.DoorFamilies,
        ModelRevitPartKind.Window => _catalog.WindowFamilies,
        ModelRevitPartKind.Equipment => _catalog.EquipmentFamilies,
        _ => null
    };

    private static string Verb(ModelRevitPartKind kind) => kind switch
    {
        ModelRevitPartKind.Column => "Dựng cột",
        ModelRevitPartKind.StructuralWall => "Dựng tường kết cấu",
        ModelRevitPartKind.ArchitecturalWall => "Dựng tường kiến trúc",
        ModelRevitPartKind.Beam => "Dựng dầm",
        ModelRevitPartKind.Floor => "Dựng sàn",
        ModelRevitPartKind.IsolatedFoundation => "Dựng móng đơn",
        ModelRevitPartKind.PileCap => "Dựng đài cọc",
        ModelRevitPartKind.RaftFoundation => "Dựng móng bè",
        ModelRevitPartKind.Pile => "Dựng cọc",
        ModelRevitPartKind.StripFoundation => "Dựng móng băng",
        ModelRevitPartKind.Door => "Dựng cửa",
        ModelRevitPartKind.Window => "Dựng cửa sổ",
        ModelRevitPartKind.Room => "Dựng phòng",
        ModelRevitPartKind.Ceiling => "Dựng trần",
        ModelRevitPartKind.Pipe => "Dựng ống",
        ModelRevitPartKind.Duct => "Dựng ống gió",
        ModelRevitPartKind.CableTray => "Dựng máng cáp",
        ModelRevitPartKind.Equipment => "Dựng thiết bị",
        _ => "Dựng"
    };

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    private static List<string> WithBlank(IReadOnlyList<string> items)
    {
        var list = new List<string> { "" };
        list.AddRange(items);
        return list;
    }

    private static string BlankToEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "" : value;

    private static string? Prefer(IReadOnlyList<string> names, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var hit = names.FirstOrDefault(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
        }

        return names.Count > 0 ? names[0] : null;
    }

    private static double ParseMm(string? text)
    {
        if (double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        return 0;
    }
}
