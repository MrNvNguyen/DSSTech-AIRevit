using Microsoft.Win32;
using Autodesk.Revit.DB;
using QSNormEngine.Core;
using QSNormEngine.Revit.Quantity;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using UIDocument = Autodesk.Revit.UI.UIDocument;

namespace QSNormEngine.Revit;

internal sealed class BoqResultsWindow : Window
{
    private readonly List<BoqLineItem> _detailRows;
    private readonly List<BoqInspectionRow> _inspection;
    private readonly int _sourceCount;
    private readonly Document? _doc;
    private readonly UIDocument? _uidoc;
    private DataGrid? _grid;
    private DataGrid? _qtyGrid;
    private TextBlock? _summary;
    private List<BoqLineItem> _displayRows;
    private ElementQuantityExporter.ColumnConfig _qtyConfig;

    public BoqResultsWindow(
        List<BoqLineItem> detailRows,
        List<BoqInspectionRow> inspection,
        int sourceCount,
        Document? doc = null,
        UIDocument? uidoc = null)
    {
        _detailRows = detailRows;
        _displayRows = BoqGrouper.Group(detailRows, BoqGroupingMode.Code);
        _inspection = inspection;
        _sourceCount = sourceCount;
        _doc = doc;
        _uidoc = uidoc;
        _qtyConfig = LoadQtyConfig();
        Title = "DSSTech — BOQ Extract";
        Width = 1360;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildUi(sourceCount);
    }

    private static ElementQuantityExporter.ColumnConfig LoadQtyConfig()
    {
        var path = Path.Combine(PluginPaths.Root, "Config", ElementQuantityExporter.DefaultConfigFileName);
        return ElementQuantityExporter.LoadConfig(path);
    }

    private UIElement BuildUi(int sourceCount)
    {
        var root = new DockPanel { Margin = new Thickness(12) };
        _summary = new TextBlock
        {
            Text = BuildSummary(_displayRows, _sourceCount),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);

        var modeBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        modeBar.Children.Add(new TextBlock { Text = "Grouping:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var modeBox = new ComboBox { Width = 180, SelectedIndex = 1 };
        modeBox.Items.Add("Detail (per element)");
        modeBox.Items.Add("By norm code");
        modeBox.Items.Add("By level + type");
        modeBox.Items.Add("By level");
        modeBox.Items.Add("By type");
        modeBox.SelectionChanged += (_, _) => ApplyGrouping(modeBox.SelectedIndex);
        modeBar.Children.Add(modeBox);
        DockPanel.SetDock(modeBar, Dock.Top);
        root.Children.Add(modeBar);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var exportXlsx = new Button { Content = "Export BOQ Excel", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var exportBoq = new Button { Content = "Export BOQ CSV", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var exportQty = new Button { Content = "Export Quantity CSV", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var exportG8 = new Button { Content = "Export G8 CSV", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var exportInspect = new Button { Content = "Export Inspection", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var createSchedule = new Button { Content = "Tạo / cập nhật Schedule", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 6, 12, 6) };
        exportXlsx.Click += (_, _) => Save("Excel (*.xlsx)|*.xlsx", "boq-export.xlsx", p =>
            BoqWorkbookExporter.Export(p, _displayRows, _inspection, quantityConfig: _qtyConfig));
        exportBoq.Click += (_, _) => Save("CSV (*.csv)|*.csv", "boq-export.csv", p => BoqExporter.ExportCsv(_displayRows, p));
        exportQty.Click += (_, _) => Save("CSV (*.csv)|*.csv", "element-quantity.csv", p =>
            ElementQuantityExporter.ExportCsv(DetailOrDisplay(), p, _qtyConfig));
        exportG8.Click += (_, _) => Save("CSV (*.csv)|*.csv", "g8-cong-trinh.csv", p => G8BoqExporter.ExportCongTrinhCsv(_displayRows, p));
        exportInspect.Click += (_, _) => Save("CSV (*.csv)|*.csv", "boq-inspection.csv", p => BoqExporter.ExportInspectionCsv(_inspection, p));
        createSchedule.Click += (_, _) => CreateOrUpdateSchedule();
        close.Click += (_, _) => Close();
        buttons.Children.Add(exportXlsx);
        buttons.Children.Add(exportBoq);
        buttons.Children.Add(exportQty);
        buttons.Children.Add(exportG8);
        buttons.Children.Add(exportInspect);
        buttons.Children.Add(createSchedule);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "BOQ", Content = BuildBoqGrid() });
        tabs.Items.Add(new TabItem { Header = "Bảng khối lượng", Content = BuildQuantityTab() });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildBoqGrid()
    {
        var grid = new DataGrid { ItemsSource = _displayRows, AutoGenerateColumns = false, IsReadOnly = true };
        _grid = grid;
        Add(grid, "Work Item", "WorkItemId", 110);
        Add(grid, "Norm Code", "NormCode", 95);
        Add(grid, "Norm Name", "NormName", 260);
        Add(grid, "Status", "Status", 150);
        Add(grid, "Level", "Level", 90);
        Add(grid, "Qty BOQ", "BoqQuantity", 90, "0.###");
        Add(grid, "Unit", "Unit", 55);
        Add(grid, "Elements", "ElementCount", 70);
        Add(grid, "Official", "CanIssueOfficially", 70);
        Add(grid, "Missing", "SelectorMissing", 180);
        return grid;
    }

    private UIElement BuildQuantityTab()
    {
        var panel = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(new TextBlock
        {
            Text = "Nhóm cột:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var groupBox = new ComboBox { Width = 320 };
        groupBox.Items.Add("1+4+5 (mặc định)");
        groupBox.Items.Add("Chỉ nhóm 1 (21 cột)");
        groupBox.Items.Add("Tất cả nhóm 1–5");
        groupBox.Items.Add("Theo config file");
        groupBox.SelectedIndex = 3;
        groupBox.SelectionChanged += (_, _) =>
        {
            _qtyConfig = groupBox.SelectedIndex switch
            {
                0 => new ElementQuantityExporter.ColumnConfig { EnabledGroups = ["1", "4", "5"] },
                1 => new ElementQuantityExporter.ColumnConfig { EnabledGroups = ["1"] },
                2 => new ElementQuantityExporter.ColumnConfig { EnabledGroups = ["1", "2", "3", "4", "5"] },
                _ => LoadQtyConfig()
            };
            RefreshQuantityGrid();
        };
        bar.Children.Add(groupBox);
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);

        _qtyGrid = new DataGrid
        {
            ItemsSource = DetailOrDisplay(),
            AutoGenerateColumns = false,
            IsReadOnly = true
        };
        RefreshQuantityGrid();
        panel.Children.Add(_qtyGrid);
        return panel;
    }

    private void RefreshQuantityGrid()
    {
        if (_qtyGrid is null) return;
        _qtyGrid.Columns.Clear();
        var cols = ElementQuantityExporter.ResolveColumns(_qtyConfig);
        foreach (var col in cols)
        {
            var header = col.Header;
            var bindingPath = MapHeaderToBinding(header);
            if (bindingPath is not null)
                Add(_qtyGrid, header, bindingPath, Math.Min(140, 40 + header.Length * 7));
            else
                _qtyGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = header,
                    Width = Math.Min(140, 40 + header.Length * 7),
                    Binding = new System.Windows.Data.Binding
                    {
                        Converter = new QtyColumnConverter(col.Read)
                    }
                });
        }
        _qtyGrid.ItemsSource = null;
        _qtyGrid.ItemsSource = DetailOrDisplay();
    }

    private static string? MapHeaderToBinding(string header) => header switch
    {
        "Mark" => "Mark",
        "Level" => "Level",
        "QS_InOutBuilding" => "InOutBuilding",
        "Mác vật liệu" => "MaterialGrade",
        "Count" => "ElementCount",
        "QS_FormworkArea" => "FormworkAreaM2",
        "Host ID" => "HostId",
        "WorkItemId" => "WorkItemId",
        "QS_NormCode" => "NormCode",
        "Official" => "CanIssueOfficially",
        "Family" => "FamilyName",
        _ => null
    };

    private List<BoqLineItem> DetailOrDisplay() =>
        // Quantity table is element-oriented; prefer detail rows when available.
        _detailRows.Count > 0 ? _detailRows : _displayRows;

    private void ApplyGrouping(int selectedIndex)
    {
        var mode = selectedIndex switch
        {
            0 => BoqGroupingMode.Detail,
            2 => BoqGroupingMode.LevelType,
            3 => BoqGroupingMode.Level,
            4 => BoqGroupingMode.Type,
            _ => BoqGroupingMode.Code
        };
        var grouped = BoqGrouper.Group(_detailRows, mode);
        _displayRows = grouped;
        if (_grid is not null)
            _grid.ItemsSource = grouped;
        if (_summary is not null)
            _summary.Text = BuildSummary(grouped, _sourceCount);
        RefreshQuantityGrid();
    }

    private static string BuildSummary(IReadOnlyList<BoqLineItem> rows, int sourceCount) =>
        $"Source elements: {sourceCount}   |   BOQ rows: {rows.Count}   |   Official-ready: {rows.Count(x => x.CanIssueOfficially)}   |   Mapped: {rows.Count(x => x.Status == "Đã ánh xạ")}   |   Missing: {rows.Count(x => x.Status == "Thiếu thông số mô hình")}";

    private static void Add(DataGrid grid, string header, string path, double width, string? format = null) =>
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Width = width,
            Binding = new System.Windows.Data.Binding(path) { StringFormat = format }
        });

    private void Save(string filter, string fileName, Action<string> action)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true };
        if (dlg.ShowDialog(this) != true) return;
        action(dlg.FileName);
        MessageBox.Show(this, $"Đã xuất:\n{dlg.FileName}", "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateOrUpdateSchedule()
    {
        if (_doc is null)
        {
            MessageBox.Show(this, "Không có Document Revit — dùng lệnh Quantity Schedule trên ribbon.", "DSSTech",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            QuantityScheduleResult result;
            using (var tx = new Transaction(_doc, "DSSTech quantity schedule"))
            {
                tx.Start();
                result = QuantityScheduleBuilder.CreateOrUpdate(_doc, _qtyConfig);
                tx.Commit();
            }

            if (_uidoc is not null)
            {
                var view = _doc.GetElement(result.ScheduleId) as View;
                if (view is not null)
                    _uidoc.ActiveView = view;
            }

            var verb = result.Created ? "Đã tạo" : "Đã cập nhật";
            MessageBox.Show(this, $"{verb} schedule:\n{result.Name}", "DSSTech",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "DSSTech", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class QtyColumnConverter : System.Windows.Data.IValueConverter
    {
        private readonly Func<BoqLineItem, string> _read;
        public QtyColumnConverter(Func<BoqLineItem, string> read) => _read = read;

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is BoqLineItem row ? _read(row) : "";

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
