using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.AssetInfo.Core;
using DSSTech.Revit.Ui;
using Microsoft.Win32;
// Autodesk.Revit.UI / .DB also define Binding, ComboBox, TextBox, ToggleButton… → pin the WPF ones.
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TextBox = System.Windows.Controls.TextBox;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace DSSTech.Revit.AssetInfo;

/// <summary>
/// Import asset information (TS_*) from the BIM form (Excel) and catalogue PDFs into Revit type parameters.
/// Flow: open Excel / add PDF → review matches → (optional) accept PDF links/proposals → "Ghi vào Revit Type" + confirm.
/// Nothing is written to the model before that confirm (same contract as Norm Selector write-back).
/// </summary>
internal sealed class AssetInfoWindow : Window
{
    private readonly UIDocument _uidoc;
    private readonly Document _doc;
    private readonly SpecRuleSet _rules;
    private readonly AssetFormReader _reader;
    private readonly PdfSpecMatcher _pdfMatcher;

    private List<RevitTypeInfo> _types = new();
    private readonly List<AssetRecord> _records = new();
    private readonly List<PdfScanResult> _pdfResults = new();
    private readonly ObservableCollection<MatchRow> _matchRows = new();
    private readonly ObservableCollection<LinkRow> _linkRows = new();
    private readonly ObservableCollection<ProposalRow> _proposalRows = new();

    private readonly TextBlock _summary = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _fillFromSpec = new() { Content = "Tự tách thông số từ cột 'Quy cách' cho ô trống", IsChecked = true, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _overwrite = new() { Content = "Ghi đè giá trị đã có trên Type", IsChecked = false, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _filter = new() { Width = 170, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox _search = new() { Width = 220, Margin = new Thickness(8, 0, 0, 0) };
    private DataGrid _matchGrid = new();
    private DataGridComboBoxColumn? _typeColumn;

    public AssetInfoWindow(UIDocument uidoc, SpecRuleSet rules)
    {
        _uidoc = uidoc;
        _doc = uidoc.Document;
        _rules = rules;
        _reader = new AssetFormReader(rules);
        _pdfMatcher = new PdfSpecMatcher(rules);

        Title = "DSSTech — Nhập thông tin tài sản (Excel / PDF → Revit Type)";
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1400, area.Width * 0.9);
        Height = Math.Min(820, area.Height * 0.9);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DssTheme.Paint(this); // same font / page colour as the other DSSTech windows
        Content = BuildUi();
        ReloadTypes();
        UpdateSummary();
    }

    // ───────────────────────────── UI ─────────────────────────────

    private UIElement BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(Btn("1. Mở file Excel…", (_, _) => OpenExcel()));
        tools.Children.Add(Btn("2. Thêm PDF catalogue…", (_, _) => AddPdf()));
        tools.Children.Add(Btn("Bind tham số TS_*", (_, _) => BindOnly()));
        tools.Children.Add(_fillFromSpec);
        tools.Children.Add(_overwrite);
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);

        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        bottom.Children.Add(Btn("Áp dụng mục PDF đã chọn", (_, _) => ApplyPdfSelections()));
        bottom.Children.Add(Btn("Xuất Excel đối chiếu…", (_, _) => ExportReview()));
        var write = DssTheme.Primary("Ghi vào Revit Type…");
        write.IsDefault = false; // Enter in the search box must not start a write
        write.HorizontalAlignment = HorizontalAlignment.Left;
        write.Margin = new Thickness(0, 0, 8, 0);
        write.Click += (_, _) => WriteToRevit();
        bottom.Children.Add(write);
        // IsCancel (set by DssTheme.Cancel) closes the modal window on click and on Esc — no Click handler needed.
        bottom.Children.Add(DssTheme.Cancel("Đóng"));
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Đối chiếu dữ liệu ↔ Revit Type", Content = BuildMatchTab() });
        tabs.Items.Add(new TabItem { Header = "Catalogue PDF (gắn TS_Catalogue)", Content = BuildLinkTab() });
        tabs.Items.Add(new TabItem
        {
            Header = _rules.Pdf.ProposeValues ? "Giá trị đọc từ PDF (đề xuất)" : "Giá trị đọc từ PDF (đang tắt)",
            Content = BuildProposalTab()
        });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildMatchTab()
    {
        var panel = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Lọc:", VerticalAlignment = VerticalAlignment.Center });
        foreach (var s in new[] { "Tất cả", "Chưa khớp", "Cần xác nhận", "Đã khớp", "Đang chọn ghi" }) _filter.Items.Add(s);
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => RefreshFilter();
        bar.Children.Add(_filter);
        bar.Children.Add(new TextBlock { Text = "Tìm:", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _search.TextChanged += (_, _) => RefreshFilter();
        bar.Children.Add(_search);
        bar.Children.Add(Btn("Chọn ghi các dòng khớp chắc", (_, _) => SetAll(r => r.IsMatched && !r.NeedsReview)));
        bar.Children.Add(Btn("Bỏ chọn tất cả", (_, _) => SetAll(_ => false)));
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);

        _matchGrid = NewGrid(_matchRows);
        _matchGrid.Columns.Add(CheckColumn("Ghi", nameof(MatchRow.Include)));
        _matchGrid.Columns.Add(TextCol("Mã (TS_ID)", nameof(MatchRow.Key), 140));
        _matchGrid.Columns.Add(TextCol("Sheet", nameof(MatchRow.Sheet), 100));
        _matchGrid.Columns.Add(TextCol("Tên vật tư", nameof(MatchRow.Name), 220));
        _matchGrid.Columns.Add(TextCol("Model", nameof(MatchRow.Model), 150));
        _matchGrid.Columns.Add(TextCol("Tên Type kỳ vọng", nameof(MatchRow.ExpectedType), 180));
        _typeColumn = new DataGridComboBoxColumn
        {
            Header = "Revit Type (đổi để gán tay)",
            DisplayMemberPath = nameof(RevitTypeInfo.Display),
            SelectedItemBinding = new Binding(nameof(MatchRow.SelectedType)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 340
        };
        _matchGrid.Columns.Add(_typeColumn);
        _matchGrid.Columns.Add(TextCol("Cách khớp", nameof(MatchRow.MethodText), 200));
        _matchGrid.Columns.Add(TextCol("Số Type", nameof(MatchRow.TargetCount), 60));
        _matchGrid.Columns.Add(TextCol("Cấu kiện", nameof(MatchRow.InstanceCount), 70));
        _matchGrid.Columns.Add(TextCol("Số tham số", nameof(MatchRow.ParamCount), 80));
        panel.Children.Add(_matchGrid);

        CollectionViewSource.GetDefaultView(_matchRows).Filter = o => o is MatchRow r && PassesFilter(r);
        return panel;
    }

    private UIElement BuildLinkTab()
    {
        var panel = new DockPanel();
        panel.Children.Add(Note(
            "Mỗi dòng = một model tìm thấy trong file PDF. Tích chọn rồi bấm 'Áp dụng mục PDF đã chọn' để điền TS_Catalogue (file#trang). " +
            "PDF scan (ảnh) không có chữ nên không đọc được — xem cột Tình trạng ở file Excel đối chiếu."));
        var grid = NewGrid(_linkRows);
        grid.Columns.Add(CheckColumn("Gắn", nameof(LinkRow.Include)));
        grid.Columns.Add(TextCol("Mã (TS_ID)", nameof(LinkRow.Key), 150));
        grid.Columns.Add(TextCol("Model", nameof(LinkRow.Model), 200));
        grid.Columns.Add(TextCol("File PDF", nameof(LinkRow.File), 320));
        grid.Columns.Add(TextCol("Trang", nameof(LinkRow.Pages), 120));
        grid.Columns.Add(TextCol("TS_Catalogue hiện có", nameof(LinkRow.Current), 320));
        panel.Children.Add(grid);
        return panel;
    }

    private UIElement BuildProposalTab()
    {
        var panel = new DockPanel();
        panel.Children.Add(Note(_rules.Pdf.ProposeValues
            ? "Giá trị đọc cạnh model trong PDF. KHÔNG tự chọn: catalogue dạng bảng nhiều model dễ cho giá trị sai. Đọc 'Trích đoạn' rồi mới tích."
            : "Đang tắt (pdf.proposeValues = false trong asset-spec-rules.json). Thử trên catalogue thật cho nhiều giá trị sai " +
              "(vd. suất tiêu hao nhiên liệu bị nhận là công suất), nên chỉ bật khi catalogue dạng 'Nhãn: giá trị' cho từng model."));
        var grid = NewGrid(_proposalRows);
        grid.Columns.Add(CheckColumn("Nhận", nameof(ProposalRow.Include)));
        grid.Columns.Add(TextCol("Mã (TS_ID)", nameof(ProposalRow.Key), 140));
        grid.Columns.Add(TextCol("Tham số", nameof(ProposalRow.Param), 150));
        grid.Columns.Add(TextCol("Giá trị", nameof(ProposalRow.Value), 160));
        grid.Columns.Add(TextCol("Tin cậy", nameof(ProposalRow.Confidence), 150));
        grid.Columns.Add(TextCol("File", nameof(ProposalRow.File), 220));
        grid.Columns.Add(TextCol("Trang", nameof(ProposalRow.Page), 50));
        grid.Columns.Add(TextCol("Trích đoạn", nameof(ProposalRow.Snippet), 500));
        panel.Children.Add(grid);
        return panel;
    }

    // ───────────────────────────── Actions ─────────────────────────────

    private void OpenExcel()
    {
        var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", Multiselect = true, Title = "Chọn file Form thông tin BIM" };
        if (dlg.ShowDialog(this) != true) return;

        var warnings = new List<string>();
        Busy(() =>
        {
            // Excel replaces records built from the model (PDF-only mode).
            _records.RemoveAll(r => r.SourceFile == "Revit");
            foreach (var stale in _linkRows.Where(x => x.Record.SourceFile == "Revit").ToList()) _linkRows.Remove(stale);
            foreach (var stale in _proposalRows.Where(x => x.Record.SourceFile == "Revit").ToList()) _proposalRows.Remove(stale);
            foreach (var path in dlg.FileNames)
            {
                var res = _reader.Read(path, _fillFromSpec.IsChecked == true);
                warnings.AddRange(res.Warnings);
                foreach (var rec in res.Records)
                {
                    var dup = _records.FindIndex(x => x.Key.Equals(rec.Key, StringComparison.OrdinalIgnoreCase));
                    if (dup >= 0) { warnings.Add($"{rec.Key}: dòng ở {rec.SourceFile} › {rec.Sheet} thay cho dòng nạp trước."); _records[dup] = rec; }
                    else _records.Add(rec);
                }
            }
            Rematch();
        });
        if (warnings.Count > 0)
            TaskDialog.Show("DSSTech", "Đã đọc Excel, có lưu ý:\n- " + string.Join("\n- ", warnings.Take(25)) + (warnings.Count > 25 ? $"\n… (+{warnings.Count - 25})" : ""));
    }

    private void AddPdf()
    {
        var dlg = new OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf", Multiselect = true, Title = "Chọn catalogue / datasheet PDF" };
        if (dlg.ShowDialog(this) != true) return;

        Busy(() =>
        {
            if (_records.Count == 0)
            {
                // No Excel loaded: search the model codes of the Revit types themselves.
                _records.AddRange(AssetTypeIndex.ToRecords(_types, _rules));
                Rematch();
            }
            var byKey = _records.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var path in dlg.FileNames)
            {
                var res = _pdfMatcher.Scan(path, _records);
                _pdfResults.Add(res);
                foreach (var link in res.Links)
                    if (byKey.TryGetValue(link.RecordKey, out var rec)) _linkRows.Add(new LinkRow(rec, link));
                foreach (var p in res.Proposals)
                    if (byKey.TryGetValue(p.RecordKey, out var rec)) _proposalRows.Add(new ProposalRow(rec, p));
            }
        });

        var scanned = _pdfResults.Count(r => r.IsScanned);
        var errors = _pdfResults.Where(r => !string.IsNullOrEmpty(r.Error)).Select(r => Path.GetFileName(r.FilePath) + ": " + r.Error).ToList();
        if (scanned > 0 || errors.Count > 0)
            TaskDialog.Show("DSSTech", $"{scanned} file là PDF scan (ảnh), cần OCR trước khi đọc." +
                                       (errors.Count > 0 ? "\nKhông mở được:\n- " + string.Join("\n- ", errors) : ""));
    }

    private void ApplyPdfSelections()
    {
        var links = _linkRows.Where(l => l.Include).GroupBy(l => l.Record);
        var nLinks = 0;
        foreach (var g in links)
        {
            var value = string.Join("; ", g.Select(l => l.Link.ToCatalogueValue()).Distinct());
            if (g.Key.Set(AssetParams.Catalogue, value, "PDF (người dùng chọn)", overwrite: true)) nLinks++;
        }
        var nValues = 0;
        foreach (var row in _proposalRows.Where(x => x.Include))
            if (row.Record.Set(row.Param, row.Value, $"PDF {row.File} tr.{row.Page} ({row.Confidence})", overwrite: false)) nValues++;

        foreach (var r in _matchRows) r.RefreshAll();
        foreach (var l in _linkRows) l.RefreshAll();
        UpdateSummary();
        TaskDialog.Show("DSSTech", $"Đã đưa vào dữ liệu: {nLinks} đường dẫn catalogue, {nValues} giá trị PDF.\nBấm 'Ghi vào Revit Type…' để ghi lên mô hình.");
    }

    private void BindOnly()
    {
        using var tx = new Transaction(_doc, "DSSTech – Bind tham số TS_*");
        tx.Start();
        try
        {
            var rep = AssetParameterBinder.EnsureBound(_doc, AssetInfoPaths.SharedParameters, AssetTypeIndex.TargetCategories(_rules));
            tx.Commit();
            TaskDialog.Show("DSSTech", rep.Text);
        }
        catch (Exception ex)
        {
            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
            TaskDialog.Show("DSSTech", "Không bind được tham số TS_*:\n" + ex.Message);
        }
    }

    private void WriteToRevit()
    {
        var chosen = _matchRows.Where(r => r.Include && r.Match.Targets.Count > 0).ToList();
        if (chosen.Count == 0)
        {
            TaskDialog.Show("DSSTech", "Chưa có dòng nào được chọn để ghi (cột 'Ghi').");
            return;
        }
        var typeCount = chosen.SelectMany(r => r.Match.Targets).Select(t => t.Id.Value).Distinct().Count();
        var valueCount = chosen.Sum(r => r.ParamCount * r.TargetCount);
        var review = chosen.Count(r => r.NeedsReview);

        var confirm = new TaskDialog("DSSTech — Xác nhận ghi")
        {
            MainInstruction = $"Ghi tối đa {valueCount} giá trị TS_* lên {typeCount} Revit Type?",
            MainContent = $"{chosen.Count} dòng dữ liệu, {chosen.Sum(r => r.InstanceCount)} cấu kiện dùng các Type này.\n" +
                          $"Ghi đè giá trị đã có: {(_overwrite.IsChecked == true ? "CÓ" : "Không (chỉ điền ô trống)")}." +
                          (review > 0 ? $"\n⚠ {review} dòng khớp theo model trong tên Type — hãy chắc đã kiểm tra." : ""),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        if (confirm.Show() != TaskDialogResult.Yes) return;

        var plan = chosen.Select(r => new WritePlanItem { Record = r.Record, TypeIds = r.Match.Targets.Select(t => t.Id).ToList() }).ToList();
        WriteReport? report = null;
        Busy(() => report = AssetTypeWriter.Write(_doc, plan, _overwrite.IsChecked == true, _rules));
        TaskDialog.Show("DSSTech", report?.Text ?? "Không có kết quả.");

        ReloadTypes(); // types now carry TS_ID → next import matches by TS_ID
    }

    private void ExportReview()
    {
        var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"DoiChieu_TaiSan_{DateTime.Now:yyyyMMdd_HHmm}.xlsx" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var rows = _matchRows.Select(r => new ReviewRow
            {
                Key = r.Key, Sheet = r.Sheet, Name = r.Name, Model = r.Model, ExpectedTypeName = r.ExpectedType,
                RevitTypes = string.Join(" | ", r.Match.Targets.Select(t => $"{t.FamilyName} : {t.TypeName}")),
                MatchMethod = r.MethodText, InstanceCount = r.InstanceCount, ParamCount = r.ParamCount,
                Status = r.Include ? "Chọn ghi" : r.IsMatched ? "Khớp, chưa chọn" : "Chưa khớp"
            });
            AssetReviewExporter.Export(dlg.FileName, rows, _pdfResults);
            TaskDialog.Show("DSSTech", $"Đã xuất:\n{dlg.FileName}");
        }
        catch (Exception ex)
        {
            // File open in Excel, no permission… must not take Revit down.
            TaskDialog.Show("DSSTech", "Không xuất được file:\n" + ex.Message);
        }
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private void ReloadTypes()
    {
        _types = AssetTypeIndex.Build(_doc, _rules);
        if (_typeColumn is not null) _typeColumn.ItemsSource = _types;
        Rematch();
    }

    private void Rematch()
    {
        var matcher = new AssetTypeMatcher(_types, _rules.Pdf.MinModelTokenLength);
        _matchRows.Clear();
        foreach (var rec in _records)
            _matchRows.Add(new MatchRow(rec, matcher.Match(rec)));
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var matched = _matchRows.Count(r => r.IsMatched);
        var review = _matchRows.Count(r => r.NeedsReview);
        _summary.Text =
            $"Mô hình: {_types.Count} Revit Type trong các category thiết bị. " +
            $"Dữ liệu: {_records.Count} loại thiết bị — khớp {matched - review}, cần xác nhận {review}, chưa khớp {_records.Count - matched}. " +
            $"PDF: {_pdfResults.Count} file ({_pdfResults.Count(r => r.IsScanned)} scan), {_linkRows.Count} liên kết model, {_proposalRows.Count} giá trị đề xuất.";
    }

    private bool PassesFilter(MatchRow r)
    {
        var ok = _filter.SelectedIndex switch
        {
            1 => !r.IsMatched,
            2 => r.NeedsReview,
            3 => r.IsMatched && !r.NeedsReview,
            4 => r.Include,
            _ => true
        };
        var q = _search.Text?.Trim();
        if (!ok || string.IsNullOrEmpty(q)) return ok;
        return new[] { r.Key, r.Name, r.Model, r.ExpectedType, r.SelectedType?.Display ?? "" }
            .Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshFilter() => CollectionViewSource.GetDefaultView(_matchRows).Refresh();

    private void SetAll(Func<MatchRow, bool> include)
    {
        foreach (var r in _matchRows) r.Include = include(r) && r.IsMatched;
        RefreshFilter();
    }

    private void Busy(Action action)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try { action(); }
        catch (Exception ex) { TaskDialog.Show("DSSTech", ex.Message); }
        finally { Mouse.OverrideCursor = null; UpdateSummary(); }
    }

    private static Button Btn(string text, RoutedEventHandler onClick)
    {
        var b = DssTheme.Quiet(text);
        b.Click += onClick;
        return b;
    }

    private static TextBlock Note(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6), Foreground = DssTheme.Muted };
        DockPanel.SetDock(t, Dock.Top);
        return t;
    }

    private static DataGrid NewGrid(System.Collections.IEnumerable source) => new()
    {
        ItemsSource = source,
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Extended,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        EnableRowVirtualization = true,
        RowHeight = 24
    };

    private static DataGridTextColumn TextCol(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path) { Mode = BindingMode.OneWay },
        IsReadOnly = true,
        Width = new DataGridLength(width)
    };

    /// <summary>Single-click checkbox (DataGridCheckBoxColumn needs two clicks).</summary>
    private static DataGridTemplateColumn CheckColumn(string header, string path)
    {
        var box = new FrameworkElementFactory(typeof(CheckBox));
        box.SetBinding(ToggleButton.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        box.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        box.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataGridTemplateColumn { Header = header, CellTemplate = new DataTemplate { VisualTree = box }, Width = new DataGridLength(48) };
    }
}
