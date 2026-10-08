using System.Windows;
using System.Windows.Controls;
using QSNormEngine.Core;

namespace QSNormEngine.Revit;

/// <summary>Assign QS fact values from canonical pick-lists for all disciplines.</summary>
internal sealed class AssignQsValuesDialog : Window
{
    private readonly Dictionary<string, ComboBox> _combos = new(StringComparer.OrdinalIgnoreCase);
    private readonly CheckBox _scopeMissing;
    private readonly ComboBox _filterBox;

    public IReadOnlyDictionary<string, string> SelectedValues { get; private set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool OnlyMissingOrUnmatched => _scopeMissing.IsChecked == true;

    public AssignQsValuesDialog(IEnumerable<NormSelectionResult> results)
    {
        var list = results.ToList();
        Title = "DSSTech — Gán giá trị QS (danh sách)";
        Width = 560;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var missing = list.SelectMany(r => r.MissingParameters)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();
        var workItems = list.Select(r => r.WorkItemId).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock
        {
            Text = "Chọn giá trị từ danh sách chuẩn cho mọi bộ môn (KT / KC / MEP / hạ tầng). Chỉ field được chọn mới ghi QS_*. Geometry (Height/Volume…) lấy từ BIM.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var tip = BuildTip(list);
        if (!string.IsNullOrWhiteSpace(tip))
        {
            header.Children.Add(new TextBlock
            {
                Text = tip,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.DarkRed,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }
        if (missing.Count > 0)
        {
            header.Children.Add(new TextBlock
            {
                Text = "Thiếu nhiều nhất: " + string.Join(" · ", missing.Take(8).Select(g => $"{g.Key} ({g.Count()})")),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        header.Children.Add(new TextBlock { Text = "Lọc nhóm field:", Margin = new Thickness(0, 8, 0, 4) });
        _filterBox = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
        _filterBox.Items.Add("Tất cả field (full catalog)");
        _filterBox.Items.Add("Ưu tiên theo WorkItem trong lưới");
        _filterBox.Items.Add("Chỉ field đang Missing");
        _filterBox.SelectedIndex = workItems.Count > 0 ? 1 : 0;
        header.Children.Add(_filterBox);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "Gán", Width = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Hủy", Width = 100, IsCancel = true };
        ok.Click += (_, _) =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, box) in _combos)
            {
                if (box.IsVisible && box.SelectedItem is string s && !string.IsNullOrWhiteSpace(s) && s != "(không gán)")
                    map[key] = s;
            }
            if (map.Count == 0)
            {
                MessageBox.Show(this, "Hãy chọn ít nhất một giá trị từ danh sách.", "DSSTech",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedValues = map;
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _scopeMissing = new CheckBox
        {
            Content = "Chỉ gán cho dòng Missing / NoNormMatched / Ambiguous (bỏ chọn = mọi cấu kiện trong lưới)",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(_scopeMissing, Dock.Bottom);
        root.Children.Add(_scopeMissing);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new StackPanel { Name = "FormPanel" };
        foreach (var key in QsValueCatalog.UiOrder)
        {
            var values = QsValueCatalog.For(key);
            if (values.Count == 0) continue;
            var label = new TextBlock
            {
                Text = QsValueCatalog.Label(key),
                Margin = new Thickness(0, 6, 0, 2),
                FontWeight = FontWeights.SemiBold,
                Tag = key
            };
            var box = new ComboBox { Margin = new Thickness(0, 0, 0, 4), Tag = key };
            box.Items.Add("(không gán)");
            foreach (var v in values) box.Items.Add(v);
            box.SelectedIndex = 0;
            if (missing.Any(g => g.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                PreferDefault(box, key, list);
            _combos[key] = box;
            form.Children.Add(label);
            form.Children.Add(box);
        }
        scroll.Content = form;
        root.Children.Add(scroll);
        Content = root;

        void ApplyFilter()
        {
            var mode = _filterBox.SelectedIndex;
            var relevant = RelevantKeys(list, missing.Select(g => g.Key), mode);
            foreach (var child in form.Children)
            {
                if (child is FrameworkElement fe && fe.Tag is string key)
                    fe.Visibility = relevant.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        _filterBox.SelectionChanged += (_, _) => ApplyFilter();
        ApplyFilter();
    }

    private static HashSet<string> RelevantKeys(
        List<NormSelectionResult> results,
        IEnumerable<string> missingKeys,
        int mode)
    {
        var all = new HashSet<string>(QsValueCatalog.UiOrder, StringComparer.OrdinalIgnoreCase);
        if (mode == 0) return all;
        if (mode == 2)
        {
            var set = new HashSet<string>(missingKeys, StringComparer.OrdinalIgnoreCase);
            set.Add("WorkItemId");
            set.Add("ElementType");
            return set.Where(all.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // mode 1: by work items present
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WorkItemId", "ElementType", "Environment" };
        foreach (var wi in results.Select(r => r.WorkItemId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var k in KeysForWorkItem(wi)) keys.Add(k);
        }
        foreach (var m in missingKeys) keys.Add(m);
        return keys.Where(all.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> KeysForWorkItem(string workItemId)
    {
        var w = workItemId ?? "";
        if (w.StartsWith("XD-CONC", StringComparison.OrdinalIgnoreCase))
            return ["PlacementMethod", "ConcreteSupply", "PumpType", "ConcreteGrade", "ElementType", "Environment", "FormworkSystem"];
        if (w.StartsWith("XD-REBAR", StringComparison.OrdinalIgnoreCase))
            return ["ElementType", "RebarDiameter", "SteelGrade", "Prestress", "Environment"];
        if (w.StartsWith("XD-FORM", StringComparison.OrdinalIgnoreCase))
            return ["ElementType", "FormworkSystem", "FormworkMaterial"];
        if (w.Contains("MASON", StringComparison.OrdinalIgnoreCase))
            return ["BrickType", "BlockType", "MortarGrade", "MortarType", "WallThickness", "ElementType"];
        if (w.Contains("PLASTER", StringComparison.OrdinalIgnoreCase) || w.Contains("PAINT", StringComparison.OrdinalIgnoreCase))
            return ["SurfaceLocation", "CoatSystem", "PrePutty", "MortarGrade", "PaintThickness"];
        if (w.Contains("TILE", StringComparison.OrdinalIgnoreCase) || w.Contains("CLAD", StringComparison.OrdinalIgnoreCase))
            return ["TileStoneType", "TileArea", "Pattern", "SurfaceLocation"];
        if (w.Contains("CEILING", StringComparison.OrdinalIgnoreCase))
            return ["CeilingType", "FrameType", "PanelType"];
        if (w.Contains("ROOF", StringComparison.OrdinalIgnoreCase) || w.Contains("WATERPROOF", StringComparison.OrdinalIgnoreCase))
            return ["RoofMaterial", "WaterproofType", "SurfaceLocation"];
        if (w.StartsWith("MEP-PIPE", StringComparison.OrdinalIgnoreCase) || w.Contains("FITTING", StringComparison.OrdinalIgnoreCase) || w.Contains("VALVE", StringComparison.OrdinalIgnoreCase))
            return ["PipeMaterial", "JointType", "SystemType"];
        if (w.Contains("INSUL", StringComparison.OrdinalIgnoreCase))
            return ["InsulationMaterial", "InsulationThickness", "ElementType", "SystemType"];
        if (w.Contains("FIRE", StringComparison.OrdinalIgnoreCase))
            return ["DeviceType", "SystemType", "ElementType"];
        if (w.Contains("HDD", StringComparison.OrdinalIgnoreCase))
            return ["ProductType", "Diameter", "Environment", "ElementType"];
        if (w.Contains("WELL", StringComparison.OrdinalIgnoreCase))
            return ["DrillMethod", "SoilRockClass", "Diameter", "ElementType"];
        if (w.StartsWith("MEP-", StringComparison.OrdinalIgnoreCase))
            return ["SystemType", "EquipmentType", "PipeMaterial", "JointType"];
        if (w.Contains("PILE", StringComparison.OrdinalIgnoreCase) || w.Contains("EARTH", StringComparison.OrdinalIgnoreCase) || w.Contains("ROCK", StringComparison.OrdinalIgnoreCase))
            return ["SoilClass", "RockClass", "PileMaterial", "PileType", "Environment"];
        if (w.Contains("ROAD", StringComparison.OrdinalIgnoreCase))
            return ["SurfaceType", "AsphaltType"];
        if (w.Contains("STEEL", StringComparison.OrdinalIgnoreCase))
            return ["ElementType", "SteelGrade", "Environment"];
        return ["ElementType"];
    }

    private static void PreferDefault(ComboBox box, string key, List<NormSelectionResult> results)
    {
        var dominantCat = results.GroupBy(r => r.RevitCategory).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "";
        var prefer = key switch
        {
            "PlacementMethod" => dominantCat.Contains("Foundation", StringComparison.OrdinalIgnoreCase) ? "Crane" : "Pump",
            "ConcreteSupply" => "ReadyMix",
            "PumpType" => "Static",
            "ElementType" => dominantCat.Contains("Foundation", StringComparison.OrdinalIgnoreCase) ? "Foundation"
                : dominantCat.Contains("Column", StringComparison.OrdinalIgnoreCase) ? "Column"
                : dominantCat.Contains("Wall", StringComparison.OrdinalIgnoreCase) ? "Wall"
                : dominantCat.Contains("Floor", StringComparison.OrdinalIgnoreCase) ? "Slab"
                : "Column",
            "ConcreteGrade" => "B25",
            "SurfaceLocation" => "Interior",
            "CoatSystem" => "1P2F",
            "PipeMaterial" => "Steel",
            "TileArea" => "0.09",
            _ => null
        };
        if (prefer is null) return;
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is string s && s.Equals(prefer, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                break;
            }
        }
    }

    private static string BuildTip(IEnumerable<NormSelectionResult> results)
    {
        var list = results.ToList();
        var foundation = list.Count(r =>
            r.WorkItemId.Equals("XD-CONC", StringComparison.OrdinalIgnoreCase)
            && r.RevitCategory.Contains("Foundation", StringComparison.OrdinalIgnoreCase)
            && r.Status is SelectionStatus.NoNormMatched or SelectionStatus.MissingParameters or SelectionStatus.Ambiguous);
        if (foundation > 0)
            return $"Gợi ý móng ({foundation} dòng): PlacementMethod = Manual/Crane (không Pump). Width lấy từ BIM.";
        return "";
    }
}
