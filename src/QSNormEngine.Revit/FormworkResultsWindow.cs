using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using QSNormEngine.Core.Formwork;
using WpfBinding = System.Windows.Data.Binding;

namespace QSNormEngine.Revit;

internal sealed class FormworkResultsWindow : Window
{
    private readonly Document _document;
    private readonly ObservableCollection<FormworkTakeoffRow> _rows;
    private readonly FormworkBatchResult _batch;
    private TextBlock? _summary;

    public FormworkResultsWindow(
        IReadOnlyList<FormworkTakeoffRow> rows,
        Document document,
        FormworkBatchResult batch)
    {
        _document = document;
        _rows = new ObservableCollection<FormworkTakeoffRow>(rows);
        _batch = batch;
        Title = "DSSTech — Formwork Takeoff";
        Width = 1100;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildUi();
        RefreshSummary();
    }

    private UIElement BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        _summary = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);

        var hint = new TextBlock
        {
            Text =
                "Chỉ ghi QS_FormworkArea / Side / Bottom khi bấm «Ghi». " +
                "Móng/Cột/Vách/Dầm/Sàn: cùng logic OOG — Σ mặt solid − overlap mặt cùng phẳng (extrude∩). " +
                "Footing/ground → Bottom=0. Total = Side + Bottom.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = System.Windows.Media.Brushes.DimGray
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var exportCsv = new Button
        {
            Content = "Export CSV",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6)
        };
        var write = new Button
        {
            Content = "Ghi vào cấu kiện",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6)
        };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 6, 12, 6) };

        exportCsv.Click += (_, _) => ExportCsv();
        write.Click += (_, _) => WriteBack();
        close.Click += (_, _) => Close();

        buttons.Children.Add(exportCsv);
        buttons.Children.Add(write);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true
        };
        Add(grid, "Element", "ElementId", 90);
        Add(grid, "Kind", "Kind", 80);
        Add(grid, "Seg", "SegmentCount", 45);
        Add(grid, "Side m²", "SideAreaM2", 90, "0.###");
        Add(grid, "Bottom m²", "BottomAreaM2", 90, "0.###");
        Add(grid, "Deduct m²", "DeductAreaM2", 90, "0.###");
        Add(grid, "WaitPour m²", "WaitPourAreaM2", 90, "0.###");
        Add(grid, "Total m²", "TotalAreaM2", 90, "0.###");
        Add(grid, "GC", "GroundContactDropBottom", 50);
        Add(grid, "Conf", "Confidence", 60);
        Add(grid, "Warnings", "Warnings", 220);
        root.Children.Add(grid);

        return root;
    }

    private void RefreshSummary()
    {
        if (_summary is null) return;
        var total = _rows.Sum(r => r.TotalAreaM2);
        var low = _rows.Count(r => r.Confidence.Equals("Low", StringComparison.OrdinalIgnoreCase));
        var sha = string.IsNullOrWhiteSpace(_batch.RulesSha256)
            ? "(no SHA)"
            : _batch.RulesSha256[..Math.Min(12, _batch.RulesSha256.Length)] + "…";
        _summary.Text =
            $"Elements: {_rows.Count}  |  Segments: {_batch.SegmentsEmitted}  |  Total: {total:0.###} m²  |  Low: {low}  |  Rules: {sha}";
    }

    private void ExportCsv()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "formwork-takeoff.csv"
        };
        if (dlg.ShowDialog() != true) return;
        FormworkExporter.ExportCsv(_rows, dlg.FileName);
        TaskDialog.Show("DSSTech", $"Đã xuất CSV:\n{dlg.FileName}");
    }

    private void WriteBack()
    {
        var td = new TaskDialog("DSSTech — Formwork write")
        {
            MainInstruction = $"Ghi QS_Formwork* cho {_rows.Count} cấu kiện?",
            MainContent =
                "Ghi QS_FormworkArea / Side / Bottom. Side đã gồm vai (WaitPour). " +
                "Không ghi các param Formwork phụ.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
        };
        if (td.Show() != TaskDialogResult.Yes) return;

        var outcome = FormworkWriteBack.WriteResults(_document, _rows);
        TaskDialog.Show("DSSTech", outcome.Message);
    }

    private static void Add(DataGrid grid, string header, string path, double width, string? format = null)
    {
        var col = new DataGridTextColumn
        {
            Header = header,
            Width = width,
            Binding = format is null
                ? new WpfBinding(path)
                : new WpfBinding(path) { StringFormat = format, ConverterCulture = CultureInfo.InvariantCulture }
        };
        grid.Columns.Add(col);
    }
}
