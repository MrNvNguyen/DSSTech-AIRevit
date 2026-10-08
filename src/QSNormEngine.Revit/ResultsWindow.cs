using Microsoft.Win32;
using QSNormEngine.Core;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using WpfGrid = System.Windows.Controls.Grid;
using WpfBinding = System.Windows.Data.Binding;

namespace QSNormEngine.Revit;

internal sealed class ResultsWindow : Window
{
    private readonly Document? _document;
    private readonly ObservableCollection<NormSelectionResult> _results;
    private TextBlock? _summary;
    private TextBlock? _missingPanel;
    private DataGrid? _resultGrid;
    private DataGrid? _resourceGrid;
    private Button? _writeBackButton;

    public ResultsWindow(List<NormSelectionResult> results, Document? document = null)
    {
        _document = document;
        _results = new ObservableCollection<NormSelectionResult>(results);
        Title = "DSSTech — Norm Selector";
        Width = 1360;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildUi();
        RefreshSummary();
    }

    private UIElement BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        _summary = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_summary, Dock.Top); root.Children.Add(_summary);

        _missingPanel = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = System.Windows.Media.Brushes.DarkRed
        };
        DockPanel.SetDock(_missingPanel, Dock.Top); root.Children.Add(_missingPanel);

        var hint = new TextBlock
        {
            Text = "Gợi ý: chọn giá trị từ danh sách «Gán giá trị QS» (Manual/Crane/Pump, ElementType, …) rồi «Chạy lại chọn mã». Móng AF cần Manual/Crane + Width — không dùng Pump. Ghi Official chỉ khi digitization APPROVED.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = System.Windows.Media.Brushes.DimGray
        };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var fillMethod = new Button { Content = "Gán giá trị QS", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6), IsEnabled = _document is not null };
        var fillInOut = new Button { Content = "Gán InOut đề xuất", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6), IsEnabled = _document is not null };
        var reselect = new Button { Content = "Chạy lại chọn mã", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6), IsEnabled = _document is not null };
        var exportCsv = new Button { Content = "Export Results CSV", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var exportRes = new Button { Content = "Export Resources CSV", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var exportJson = new Button { Content = "Export JSON", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        _writeBackButton = new Button { Content = "Ghi vào cấu kiện", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6), IsEnabled = _document is not null };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 6, 12, 6) };

        fillMethod.Click += (_, _) => AssignQsValues();
        fillInOut.Click += (_, _) => AssignInOutProposed();
        reselect.Click += (_, _) => Reselect();
        exportCsv.Click += (_, _) => Save("CSV (*.csv)|*.csv", "norm-selection.csv", p => ResultExporter.ExportCsv(_results, p));
        exportRes.Click += (_, _) => Save("CSV (*.csv)|*.csv", "norm-resources.csv", p => ResultExporter.ExportResourceCsv(_results, p));
        exportJson.Click += (_, _) => Save("JSON (*.json)|*.json", "norm-selection.json", p => ResultExporter.ExportJson(_results, p));
        _writeBackButton.Click += (_, _) => WriteBackOfficialOnly();
        close.Click += (_, _) => Close();

        buttons.Children.Add(fillMethod);
        buttons.Children.Add(fillInOut);
        buttons.Children.Add(reselect);
        buttons.Children.Add(exportCsv);
        buttons.Children.Add(exportRes);
        buttons.Children.Add(exportJson);
        buttons.Children.Add(_writeBackButton);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);

        var split = new WpfGrid();
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _resultGrid = new DataGrid { ItemsSource = _results, AutoGenerateColumns = false, IsReadOnly = true, CanUserSortColumns = true };
        Add(_resultGrid, "Element", "ElementId", 85);
        Add(_resultGrid, "Category", "RevitCategory", 150);
        Add(_resultGrid, "Work Item", "WorkItemId", 130);
        Add(_resultGrid, "Norm Code", "NormCode", 95);
        Add(_resultGrid, "Norm Name", "NormName", 260);
        Add(_resultGrid, "Status", "Status", 125);
        Add(_resultGrid, "Official", "CanIssueOfficially", 70);
        Add(_resultGrid, "Qty", "Quantity", 80, "0.###");
        Add(_resultGrid, "Unit", "QuantityUnit", 55);
        Add(_resultGrid, "Missing", "MissingParameters", 160, new ListJoinConverter());
        Add(_resultGrid, "Warnings", "Warnings", 260, new ListJoinConverter());
        WpfGrid.SetRow(_resultGrid, 0); split.Children.Add(_resultGrid);

        _resourceGrid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, Margin = new Thickness(0, 8, 0, 0) };
        WpfGrid.SetRow(_resourceGrid, 1); split.Children.Add(_resourceGrid);
        root.Children.Add(split);
        RefreshResources();
        return root;
    }

    private void RefreshSummary()
    {
        var official = _results.Count(x => x.CanIssueOfficially);
        var qc1 = _results.Count(x => x.Status == SelectionStatus.SelectedQc1);
        var missing = _results.Count(x => x.Status is SelectionStatus.MissingParameters or SelectionStatus.Ambiguous or SelectionStatus.NoNormMatched);
        if (_summary is not null)
            _summary.Text = $"Elements: {_results.Count}   |   Selected: {_results.Count(x => x.NormCode.Length > 0)}   |   Official: {official}   |   Qc1: {qc1}   |   Missing/Ambiguous: {missing}";
        if (_missingPanel is not null)
            _missingPanel.Text = BuildMissingDashboard();
        if (_writeBackButton is not null)
            _writeBackButton.IsEnabled = _document is not null;
    }

    private void RefreshResources()
    {
        if (_resourceGrid is null) return;
        _resourceGrid.ItemsSource = _results
            .SelectMany(r => r.Resources.Select(d => new { r.ElementId, r.NormCode, d.ResourceId, d.ResourceType, d.ResourceName, d.Unit, d.TotalQuantity }))
            .ToList();
    }

    private string BuildMissingDashboard()
    {
        var missing = _results.SelectMany(r => r.MissingParameters).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(8);
        var text = string.Join(" · ", missing.Select(g => $"{g.Key} ({g.Count()})"));
        return string.IsNullOrWhiteSpace(text) ? "" : $"Missing parameters: {text}";
    }

    private void AssignQsValues()
    {
        if (_document is null) return;
        var dlg = new AssignQsValuesDialog(_results) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var outcome = QsParameterFiller.FillValues(_document, _results, dlg.SelectedValues, dlg.OnlyMissingOrUnmatched);
        MessageBox.Show(this, outcome.Message, "DSSTech",
            MessageBoxButton.OK, outcome.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
        if (!outcome.Failed && outcome.WrittenCount > 0)
            Reselect();
    }

    private void AssignInOutProposed()
    {
        if (_document is null) return;
        var confirm = MessageBox.Show(this,
            "Gán QS_InOutBuilding theo đề xuất (WallFunction → Room → default)?\n" +
            "Không ghi đè giá trị đã có. Không đụng QS_SurfaceLocation.",
            "DSSTech", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var outcome = InOutWriteBack.WriteProposed(_document, _results.Select(r => r.ElementId));
        MessageBox.Show(this, outcome.Message, "DSSTech",
            MessageBoxButton.OK, outcome.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
        if (!outcome.Failed && outcome.WrittenCount > 0)
            Reselect();
    }

    private void Reselect()
    {
        if (_document is null) return;
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            var data = RuntimeDataLoader.Load(PluginPaths.RuntimeData);
            var config = RuntimeDataLoader.LoadConfig(PluginPaths.Config);
            var engine = new NormSelectionEngine(data, config);
            var factory = new RevitElementFactsFactory(_document);
            var ids = _results.Select(r => r.ElementId).ToList();
            _results.Clear();
            foreach (var id in ids)
            {
                if (!long.TryParse(id, out var idValue)) continue;
                var element = _document.GetElement(new ElementId(idValue));
                if (element?.Category is null) continue;
                _results.Add(engine.Select(factory.Create(element)));
            }
            RefreshSummary();
            RefreshResources();
        }
        finally
        {
            Cursor = null;
        }
    }

    private void WriteBackOfficialOnly()
    {
        if (_document is null) return;
        var official = _results.Count(x => x.CanIssueOfficially);
        if (official == 0)
        {
            var qc1Count = _results.Count(x => x.Status == SelectionStatus.SelectedQc1 && !string.IsNullOrWhiteSpace(x.NormCode));
            if (qc1Count == 0)
            {
                MessageBox.Show(this,
                    "Không có dòng Official-ready để ghi.\n\n" +
                    "1) Setup Parameters (nếu chưa)\n" +
                    "2) Gán biện pháp đổ cho XD-CONC\n" +
                    "3) Chạy lại chọn mã\n" +
                    "4) Official chỉ khi digitization APPROVED; hầu hết model VN đang ở Qc1.",
                    "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var draft = MessageBox.Show(this,
                $"Không có Official-ready.\nCó {qc1Count} dòng Qc1 (nháp).\n\nGhi QS_NormCode nháp cho Qc1?\n(Không coi là phát hành chính thức.)",
                "DSSTech", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (draft != MessageBoxResult.Yes) return;
            var draftOutcome = NormWriteBack.WriteResults(_document, _results, officialOnly: false);
            MessageBox.Show(this, draftOutcome.Message + "\n\nLưu ý: đã ghi cả Qc1 — chưa phải hồ sơ chính thức.", "DSSTech",
                MessageBoxButton.OK, draftOutcome.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Chỉ ghi {official} dòng Official-ready (QS_NormCode / QS_NormStatus / QS_NormStamp).\nTiếp tục?",
            "DSSTech", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        var outcome = NormWriteBack.WriteResults(_document, _results, officialOnly: true);
        MessageBox.Show(this, outcome.Message, "DSSTech",
            MessageBoxButton.OK, outcome.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private static void Add(DataGrid grid, string header, string path, double width, string? format = null) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Width = width, Binding = new WpfBinding(path) { StringFormat = format } });

    private static void Add(DataGrid grid, string header, string path, double width, IValueConverter converter) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Width = width, Binding = new WpfBinding(path) { Converter = converter } });

    private void Save(string filter, string fileName, Action<string> action)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true };
        if (dlg.ShowDialog(this) == true)
        {
            action(dlg.FileName);
            MessageBox.Show(this, $"Đã xuất:\n{dlg.FileName}", "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private sealed class ListJoinConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is IEnumerable<string> xs ? string.Join("; ", xs) : value?.ToString() ?? "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
