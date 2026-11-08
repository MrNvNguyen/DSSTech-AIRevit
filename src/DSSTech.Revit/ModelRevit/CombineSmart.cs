using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using DSSTech.Revit.Ui;
using Microsoft.Win32;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// «Combine» (MEPF Smart Combine, docs/COMBINE_PLAN.md): the runs in scope are told apart by system, checked pair
/// by pair against the profile's clearances, their fittings against the fitting rules, sprinkler heads for runs
/// under them; the lower priority run of each crossing goes under or over the other. Nothing changes until Apply;
/// each run's detour is checked after it is made and undone if it fails.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CombineSmartCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
        if (uiApp.ActiveUIDocument?.Document is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        var w = new CombineWindow(uiApp);
        w.SetOwner(uiApp.MainWindowHandle);
        w.ShowDialog();
        return Result.Succeeded;
    }
}

/// <summary>One line of the Combine result.</summary>
internal sealed class CombineRow
{
    public string Status { get; set; } = "";
    public string Element { get; set; } = "";
    public string System { get; set; } = "";
    public string Against { get; set; } = "";
    public string Issue { get; set; } = "";
    public string Action { get; set; } = "";
    public long AId { get; set; }
    public long BId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public CombineConflict? Conflict { get; set; }
    public bool Under { get; set; }
    /// <summary>Corridor mode: the network's elements moved together, and by how much (mm, + up).</summary>
    public List<long> MoveIds { get; set; } = new();
    public double ShiftMm { get; set; }
    public bool IsStack { get; set; }
    /// <summary>Combine can fix it: a crossing with a run free to move, or a network to put on its layer.</summary>
    public bool Fixable => Status == "FAIL" && (IsStack || Conflict is { Parallel: false, Yielder: not null });

    public static string Name(CombineStatus s) => s switch
    {
        CombineStatus.Pass => "PASS",
        CombineStatus.Warning => "WARNING",
        CombineStatus.Fail => "FAIL",
        _ => "REVIEW"
    };
}

internal sealed class CombineWindow : DssDialog
{
    private const string Auto = "Tự động (theo thứ tự ưu tiên)";
    private const string Manual = "Thủ công (chọn hệ phải né)";
    private const string Corridor = "Xếp tầng hành lang (theo ưu tiên)";
    private const string ScopeSelection = "Đang chọn";
    private const string ScopeView = "View hiện tại";
    private const string ScopeLevel = "Level của view";

    private readonly UIApplication _app;
    private CombineProfile _profile;
    private readonly ComboBox _scope;
    private readonly ComboBox _mode;
    private readonly ComboBox _yieldSystem;
    private readonly ComboBox _direction;
    private readonly TextBox _minBottom;
    private readonly ListBox _priority;
    private readonly CheckBox _locked;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly List<CombineRow> _rows = new();

    public CombineWindow(UIApplication app)
        : base("DSSTech — Combine MEPF", "Phân tích → xem trước → áp dụng. Hệ ưu tiên thấp né hệ ưu tiên cao; thoát nước giữ dốc, gas đốt (LPG) giữ hành lang. Hệ theo mã của công ty (SAD, BWS, CWS…). Model chỉ đổi khi bấm Áp dụng.", 1180, 860)
    {
        _app = app;
        var errors = new List<string>();
        _profile = CombineProfileStore.Load(Doc, errors);

        var hasSelection = app.ActiveUIDocument.Selection.GetElementIds().Count > 0;
        _scope = DssTheme.Combo(new[] { ScopeSelection, ScopeView, ScopeLevel }, hasSelection ? ScopeSelection : ScopeView);
        _mode = DssTheme.Combo(new[] { Auto, Corridor, Manual }, Auto);
        _yieldSystem = DssTheme.Combo(_profile.Priority.Select(MepSystemClassifier.CodeLabel), MepSystemClassifier.CodeLabel("SAD"));
        _direction = DssTheme.Combo(new[] { "Đi dưới", "Đi trên" }, "Đi dưới");
        var manualRow = DssTheme.Pair(DssTheme.Field("Hệ phải né", _yieldSystem), DssTheme.Field("Hướng né", _direction));
        manualRow.Visibility = System.Windows.Visibility.Collapsed;
        _minBottom = DssTheme.Input("");
        var corridorRow = DssTheme.Field("Đáy thấp nhất cho phép (mm so với level, VD trần 2700; trống = không giới hạn)", _minBottom);
        corridorRow.Visibility = System.Windows.Visibility.Collapsed;
        _mode.SelectionChanged += (_, _) =>
        {
            manualRow.Visibility = DssTheme.ComboValue(_mode) == Manual ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            corridorRow.Visibility = DssTheme.ComboValue(_mode) == Corridor ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _rows.Clear();
            _grid?.Items.Refresh();
        };

        _priority = new ListBox { Height = 260, Margin = new Thickness(0, 0, 0, 8) };
        _locked = DssTheme.Option("Không dời hệ đang chọn (khóa)");
        _priority.SelectionChanged += (_, _) =>
        {
            var code = SelectedCode();
            _locked.IsEnabled = code is not null;
            _locked.IsChecked = code is not null && _profile.Locked.Contains(code);
        };
        _locked.Click += (_, _) =>
        {
            if (SelectedCode() is not { } code)
                return;
            _profile.Locked.Remove(code);
            if (_locked.IsChecked == true)
                _profile.Locked.Add(code);
            FillPriority(_priority.SelectedIndex);
        };
        FillPriority(0);

        var order = new WrapPanel();
        Button Small(string text, Action run)
        {
            var b = DssTheme.Quiet(text);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Click += (_, _) => Guard(run);
            order.Children.Add(b);
            return b;
        }

        Small("▲ Lên", () => MovePriority(-1));
        Small("▼ Xuống", () => MovePriority(1));
        Small("Lưu quy tắc", SaveProfile);
        Small("Mở file quy tắc (khe hở, phụ kiện)", OpenProfile);
        Small("Tải lại", ReloadProfile);
        Small("Khôi phục mặc định", () =>
        {
            _profile = CombineProfile.Default();
            FillPriority(0);
            _status.Text = "Đã về quy tắc mặc định (chưa lưu).";
        });

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            ItemsSource = _rows,
            Height = 330,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        foreach (var (header, path) in new[]
                 {
                     ("Trạng thái", nameof(CombineRow.Status)), ("Phần tử", nameof(CombineRow.Element)), ("Hệ", nameof(CombineRow.System)),
                     ("Với", nameof(CombineRow.Against)), ("Vấn đề", nameof(CombineRow.Issue)), ("Xử lý đề xuất", nameof(CombineRow.Action))
                 })
            _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path) });
        _grid.MouseDoubleClick += (_, _) => Guard(ShowSelected);
        _status = new TextBlock { Foreground = DssTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        if (errors.Count > 0)
            _status.Text = "Không đọc được file quy tắc: " + string.Join("; ", errors);

        var actions = new WrapPanel();
        Button Act(string text, Action run)
        {
            var b = DssTheme.Quiet(text);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Click += (_, _) => Guard(run);
            actions.Children.Add(b);
            return b;
        }

        Act("Phân tích", Analyze);
        Act("Xem trong model", ShowSelected);
        Act("Xuất Excel", ExportExcel);

        var left = new StackPanel();
        left.Children.Add(DssTheme.Field("Phạm vi", _scope));
        left.Children.Add(DssTheme.Field("Chế độ", _mode));
        left.Children.Add(manualRow);
        left.Children.Add(corridorRow);
        var right = new StackPanel();
        right.Children.Add(DssTheme.Field("Thứ tự ưu tiên (trên cùng giữ chỗ, dưới né)", _priority));
        right.Children.Add(_locked);
        right.Children.Add(order);
        var setup = new Grid();
        setup.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        setup.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        right.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        setup.Children.Add(left);
        setup.Children.Add(right);

        AddSection(1, "Thiết lập", setup);
        AddSection(2, "Kết quả (PASS / WARNING / FAIL / REVIEW) — nhấp đúp để xem trong model", _grid, actions, _status);
        PrimaryButton.Content = "Áp dụng (dòng đang chọn, hoặc tất cả)";
        PrimaryButton.Click += (_, _) => Guard(Apply);
        CancelButton.Content = "Đóng";
    }

    private Document Doc => _app.ActiveUIDocument.Document;

    private void Guard(Action run)
    {
        try
        {
            run();
        }
        catch (Exception ex)
        {
            _status.Text = "Lỗi: " + ex.Message;
            DssTechLog.Write($"combine: {ex}");
        }

        _grid.Items.Refresh();
    }

    // ── Profile ─────────────────────────────────────────────────────────

    private string? SelectedCode() =>
        _priority.SelectedIndex >= 0 && _priority.SelectedIndex < _profile.Priority.Count ? _profile.Priority[_priority.SelectedIndex] : null;

    private void FillPriority(int select)
    {
        _priority.Items.Clear();
        for (var i = 0; i < _profile.Priority.Count; i++)
        {
            var code = _profile.Priority[i];
            _priority.Items.Add($"{i + 1}. {MepSystemClassifier.CodeLabel(code)}{(_profile.Locked.Contains(code) ? "  — khóa, không dời" : "")}");
        }

        _priority.SelectedIndex = Math.Max(0, Math.Min(select, _priority.Items.Count - 1));
    }

    private void MovePriority(int by)
    {
        var i = _priority.SelectedIndex;
        var j = i + by;
        if (i < 0 || j < 0 || j >= _profile.Priority.Count)
            return;
        (_profile.Priority[i], _profile.Priority[j]) = (_profile.Priority[j], _profile.Priority[i]);
        FillPriority(j);
    }

    private void SaveProfile()
    {
        var path = CombineProfileStore.EditableFile(Doc);
        _profile.Save(path);
        _status.Text = $"Đã lưu quy tắc: {path}";
    }

    private void OpenProfile()
    {
        var path = CombineProfileStore.EditableFile(Doc);
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        _status.Text = $"Đang mở {path}. Sửa khe hở (pairs), phụ kiện (fittings), lưu file rồi bấm «Tải lại». " +
                       $"File riêng cho dự án: đặt DSSTech-combine.json cạnh file .rvt.";
    }

    private void ReloadProfile()
    {
        var errors = new List<string>();
        _profile = CombineProfileStore.Load(Doc, errors);
        FillPriority(0);
        _status.Text = errors.Count > 0 ? "Không đọc được: " + string.Join("; ", errors) : $"Đã tải quy tắc «{_profile.Name}».";
    }

    // ── Analyse ─────────────────────────────────────────────────────────

    /// <summary>Runs in scope; fittings and sprinkler heads near them.</summary>
    private (List<MEPCurve> Curves, List<FamilyInstance> Fittings, List<FamilyInstance> Sprinklers, string Label) Scope()
    {
        var doc = Doc;
        var view = doc.ActiveView;
        var scope = DssTheme.ComboValue(_scope);
        List<MEPCurve> curves;
        string label;
        if (scope == ScopeSelection)
        {
            var ids = _app.ActiveUIDocument.Selection.GetElementIds();
            curves = ids.Select(doc.GetElement).OfType<MEPCurve>().ToList();
            label = $"{curves.Count} ống/máng đang chọn";
        }
        else if (scope == ScopeLevel)
        {
            if (!ModelRevitLevelContext.TryResolve(view, doc, out var level, out _, out var err))
                throw new InvalidOperationException(err);
            curves = new FilteredElementCollector(doc).OfClass(typeof(MEPCurve)).Cast<MEPCurve>()
                .Where(c => c.ReferenceLevel?.Id == level.Id).ToList();
            label = $"level {level.Name}";
        }
        else
        {
            curves = new FilteredElementCollector(doc, view.Id).OfClass(typeof(MEPCurve)).Cast<MEPCurve>().ToList();
            label = $"view {view.Name}";
        }

        curves = curves.Where(c => c is not Autodesk.Revit.DB.Mechanical.FlexDuct and not Autodesk.Revit.DB.Plumbing.FlexPipe).ToList();
        var fittings = curves.SelectMany(MepCombine.Connectors)
            .SelectMany(c => c.AllRefs.Cast<Connector>())
            .Select(c => c.Owner)
            .OfType<FamilyInstance>()
            .Where(f => f.MEPModel is Autodesk.Revit.DB.Mechanical.MechanicalFitting)
            .GroupBy(f => f.Id).Select(g => g.First()).ToList();
        var sprinklers = new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Sprinklers)
            .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
        return (curves, fittings, sprinklers, label);
    }

    private void Analyze()
    {
        _rows.Clear();
        var (curves, fittings, sprinklers, label) = Scope();
        var runs = curves.Select(c => CombineProfileStore.Run(c, _profile)).Where(r => r is not null).Select(r => r!).ToList();
        var byId = runs.ToDictionary(r => r.Id);

        // Clearances and who gives way.
        var manual = DssTheme.ComboValue(_mode) == Manual;
        var manualSystem = _profile.Priority.FirstOrDefault(c => MepSystemClassifier.CodeLabel(c) == DssTheme.ComboValue(_yieldSystem)) ?? "";
        var manualUnder = DssTheme.ComboValue(_direction) == "Đi dưới";
        if (DssTheme.ComboValue(_mode) == Corridor)
            AnalyzeCorridor(curves);
        else
        foreach (var c in CombineAnalyzer.Analyze(runs, _profile))
        {
            if (manual)
                ManualYield(c, manualSystem, manualUnder);
            var row = new CombineRow
            {
                Status = CombineRow.Name(c.Status),
                Issue = c.Issue,
                Action = c.Action,
                AId = c.A.Id,
                BId = c.B.Id,
                X = c.X,
                Y = c.Y,
                Conflict = c
            };
            var mover = c.Yielder == 1 ? c.B : c.A;
            var other = c.Yielder == 1 ? c.A : c.B;
            row.Element = mover.Name;
            row.System = mover.Label;
            row.Against = other.Name;
            row.Under = manual ? manualUnder : (mover.Az + mover.Bz) / 2 <= CombineAnalyzer.Mid(other, c.X, c.Y);
            if (c.Yielder == 1)
                (row.AId, row.BId) = (c.B.Id, c.A.Id);
            _rows.Add(row);
        }

        // Fittings against the fitting rules (C3).
        foreach (var f in fittings)
        {
            var check = CheckFitting(f);
            if (check is null)
                continue;
            var (status, reason) = check.Value;
            _rows.Add(new CombineRow
            {
                Status = CombineRow.Name(status),
                Element = $"Phụ kiện {f.Id.Value} ({f.Symbol?.FamilyName})",
                System = CombineProfileStore.Label(f),
                Issue = reason,
                Action = "Thay phụ kiện đúng loại (Routing Preferences của Pipe Type) hoặc dựng lại tuyến",
                AId = f.Id.Value
            });
        }

        // Runs under sprinkler heads (C6).
        foreach (var s in sprinklers)
        {
            if (s.Location is not LocationPoint lp)
                continue;
            double sx = UnitConv.ToMm(lp.Point.X), sy = UnitConv.ToMm(lp.Point.Y), sz = UnitConv.ToMm(lp.Point.Z);
            foreach (var r in runs.Where(r => r.System != MepSystemCode.FP && r.IsLevel))
            {
                var top = CombineAnalyzer.Mid(r, sx, sy) + r.DepthMm / 2;
                if (top > sz || top < sz - _profile.SprinklerClearBelowMm)
                    continue;
                if (PlanDistance(r, sx, sy) - r.WidthMm / 2 > _profile.SprinklerClearSideMm)
                    continue;
                _rows.Add(new CombineRow
                {
                    Status = "WARNING",
                    Element = r.Name,
                    System = r.Label,
                    Against = $"Đầu phun {s.Id.Value}",
                    Issue = $"Nằm dưới đầu phun trong vùng {_profile.SprinklerClearSideMm:0} mm ngang / {_profile.SprinklerClearBelowMm:0} mm dưới: che vùng phun",
                    Action = "Dời ống/máng hoặc bổ sung đầu phun phía dưới (theo thiết kế PCCC)",
                    AId = r.Id,
                    BId = s.Id.Value
                });
            }
        }

        var unknown = runs.Count(r => r.System == MepSystemCode.Unknown);
        var order = new[] { "FAIL", "REVIEW", "WARNING", "PASS" };
        var sorted = _rows.OrderBy(r => Array.IndexOf(order, r.Status)).ToList();
        _rows.Clear();
        _rows.AddRange(sorted);
        _status.Text = $"Phạm vi {label}: {runs.Count} đoạn ({string.Join(", ", runs.GroupBy(r => r.Code.Length > 0 ? r.Code : MepSystemClassifier.Label(r.System)).Select(g => $"{g.Key} {g.Count()}"))}). " +
                       $"FAIL {_rows.Count(r => r.Status == "FAIL")} (tự xử lý được {_rows.Count(r => r.Fixable)}), REVIEW {_rows.Count(r => r.Status == "REVIEW")}, WARNING {_rows.Count(r => r.Status == "WARNING")}." +
                       (unknown > 0 ? $" {unknown} đoạn chưa rõ hệ: đặt System Type / tên hệ theo mã công ty (VD: BWS - Black Sewage, CWS - Cold Water Supply, SP - Sprinkler)." : "") +
                       (_rows.Count == 0 ? " Không có vấn đề — PASS." : "");
    }

    private void ManualYield(CombineConflict c, string code, bool under)
    {
        // The chosen company system; a run without a code counts when it is of that system's group.
        var system = MepSystemClassifier.Family(code);
        bool Is(CombineRun r) => r.Code == code || (r.Code.Length == 0 && r.System == system);
        var label = MepSystemClassifier.CodeLabel(code);
        var aIs = Is(c.A);
        var bIs = Is(c.B);
        if (aIs == bIs)
        {
            c.Status = CombineStatus.Warning;
            c.Action = "Không có hệ phải né trong cặp này (chế độ thủ công)";
            c.Yielder = null;
            return;
        }

        var mover = aIs ? c.A : c.B;
        if (mover.Locked || !_profile.CanDetour(system))
        {
            c.Status = CombineStatus.Review;
            c.Action = $"{label} không được dời (khóa / giữ dốc)";
            c.Yielder = null;
            return;
        }

        c.Yielder = aIs ? 0 : 1;
        if (c.Parallel)
        {
            c.Status = CombineStatus.Warning;
            c.Action = $"Dời ngang {label} thêm {c.RequiredMm - c.GapMm:0} mm (sửa tay)";
            return;
        }

        c.Status = CombineStatus.Fail;
        c.Action = $"{label} đi {(under ? "dưới" : "trên")}, cút {_profile.DetourAngle(system):0}°";
    }

    // ── Corridor (C5) ───────────────────────────────────────────────────

    private static bool Vertical(MEPCurve c) =>
        c.Location is LocationCurve { Curve: Line l } && Math.Abs(l.Direction.Z) > 0.99;

    /// <summary>Fittings and accessories: they move with their network.</summary>
    private static bool Part(Element e) =>
        e is FamilyInstance { MEPModel: not null } fi && (fi.Category is null ? BuiltInCategory.INVALID : (BuiltInCategory)fi.Category.Id.Value) is
            BuiltInCategory.OST_PipeFitting or BuiltInCategory.OST_DuctFitting or BuiltInCategory.OST_CableTrayFitting or
            BuiltInCategory.OST_ConduitFitting or BuiltInCategory.OST_PipeAccessory or BuiltInCategory.OST_DuctAccessory;

    /// <summary>
    /// The connected networks in scope stacked by priority under the structure: one row per network, with how far
    /// it moves. Vertical pieces stay out of the move (they stretch to what they drop to).
    /// </summary>
    private void AnalyzeCorridor(List<MEPCurve> curves)
    {
        var doc = Doc;
        var levelMm = ModelRevitLevelContext.TryResolve(doc.ActiveView, doc, out var level, out _, out _) ? UnitConv.ToMm(level.Elevation) : 0.0;
        double? minBottom = null;
        if (_minBottom.Text.Trim().Length > 0)
        {
            if (!double.TryParse(_minBottom.Text.Trim(), out var mb))
                throw new InvalidOperationException("Đáy thấp nhất: nhập số mm (so với level của view).");
            minBottom = levelMm + mb;
        }

        var parts = curves.SelectMany(MepCombine.Connectors).SelectMany(c => c.AllRefs.Cast<Connector>())
            .Select(c => c.Owner).Where(Part).GroupBy(e => e.Id).Select(g => g.First());
        var pool = curves.Cast<Element>().Concat(parts).ToList();
        var overhead = ModelRevitWallFit.Overhead(doc);
        var stack = new List<StackNet>();
        var members = new List<List<Element>>();
        foreach (var net in MepCombine.Networks(doc, pool))
        {
            var runs = net.OfType<MEPCurve>().Select(c => CombineProfileStore.Run(c, _profile)).Where(r => r is { IsLevel: true }).Select(r => r!).ToList();
            if (runs.Count == 0)
                continue;
            var main = runs.GroupBy(r => (r.System, r.Code)).OrderByDescending(g => g.Count()).First().Key;
            var system = main.System;
            var ids = net.Select(e => e.Id).ToHashSet();
            var n = new StackNet { Index = stack.Count, System = system, Code = main.Code, Runs = runs };
            if (net.Any(e => e.Pinned))
                n.FixedReason = "có phần tử bị pin";
            else if (runs.Any(r => _profile.IsLocked(r.System, r.Code)))
                n.FixedReason = "hệ khóa trong quy tắc";
            else if (runs.Any(r => r.IsSloped) || !_profile.CanDetour(system))
                n.FixedReason = "ống dốc (thoát nước) giữ dốc và điểm xả";
            else if (net.OfType<MEPCurve>().Where(c => !Vertical(c)).SelectMany(MepCombine.Connectors)
                     .Any(c => Math.Abs(c.CoordinateSystem.BasisZ.Z) < 0.5 &&
                               c.AllRefs.Cast<Connector>().Any(r => !ids.Contains(r.Owner.Id) && r.Owner is FamilyInstance && !Part(r.Owner))))
                n.FixedReason = "nối ngang vào thiết bị (FCU, quạt…): dời tay cùng thiết bị";
            n.Fixed = n.FixedReason.Length > 0;

            // Lowest beam or slab over the network.
            foreach (var r in runs)
            {
                var len = r.PlanLength;
                var top = UnitConv.MmToInternal(Math.Max(r.Az, r.Bz) + r.DepthMm / 2);
                for (var at = 0.0; ; at += 300)
                {
                    var t = Math.Min(at, len) / len;
                    var x = UnitConv.MmToInternal(r.Ax + (r.Bx - r.Ax) * t);
                    var y = UnitConv.MmToInternal(r.Ay + (r.By - r.Ay) * t);
                    if (ModelRevitWallFit.Underside(overhead, x, y, top - UnitConv.MmToInternal(1), top + UnitConv.MmToInternal(6000)) is double u)
                        n.CeilingMm = Math.Min(n.CeilingMm ?? double.MaxValue, UnitConv.ToMm(u));
                    if (at >= len)
                        break;
                }
            }

            stack.Add(n);
            members.Add(net.Where(e => e is not MEPCurve c || !Vertical(c)).ToList());
        }

        CorridorStacker.Stack(stack, _profile, minBottom);
        foreach (var n in stack.OrderBy(x => x.Layer))
        {
            var first = n.Runs[0];
            _rows.Add(new CombineRow
            {
                Status = CombineRow.Name(n.Status),
                Element = $"Mạng {n.Label} ({n.Runs.Count} đoạn, từ {first.Id})",
                System = n.Label,
                Against = $"Tầng {n.Layer}",
                Issue = $"Đỉnh {n.Top - levelMm:0} → {n.Top + n.ShiftMm - levelMm:0}, đáy {n.Bottom - levelMm:0} → {n.Bottom + n.ShiftMm - levelMm:0} mm so với level" +
                        (n.CeilingMm is double c ? $"; đáy dầm/sàn {c - levelMm:0}" : "; không thấy dầm/sàn phía trên"),
                Action = n.Note,
                AId = first.Id,
                X = (first.Ax + first.Bx) / 2,
                Y = (first.Ay + first.By) / 2,
                IsStack = true,
                ShiftMm = n.ShiftMm,
                MoveIds = members[n.Index].Select(e => e.Id.Value).ToList()
            });
        }
    }

    /// <summary>Each network moved up or down as one; undone when it loses a connection.</summary>
    private void ApplyCorridor(List<CombineRow> rows)
    {
        var doc = Doc;
        int moved = 0, undone = 0;
        using (var group = new TransactionGroup(doc, "DSSTech Combine hành lang"))
        {
            group.Start();
            foreach (var r in rows)
            {
                var ids = r.MoveIds.Select(id => new ElementId(id)).Where(id => doc.GetElement(id) is not null).ToList();
                if (ids.Count == 0)
                    continue;
                int Connected() => ids.Select(doc.GetElement).SelectMany(MepCombine.Connectors).Count(c => c.IsConnected);
                using var tx = new Transaction(doc, "DSSTech Combine layer");
                tx.Start();
                var before = Connected();
                string? error = null;
                try
                {
                    ElementTransformUtils.MoveElements(doc, ids, new XYZ(0, 0, UnitConv.MmToInternal(r.ShiftMm)));
                    doc.Regenerate();
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
                {
                    error = ex.Message;
                }

                var lost = error is null ? before - Connected() : 0;
                if (error is not null || lost > 0)
                {
                    tx.RollBack();
                    undone++;
                    r.Status = "REVIEW";
                    r.Action = "Đã thử, hoàn tác: " + (error ?? $"mất {lost} kết nối (ống đứng / thiết bị không theo được)");
                }
                else
                {
                    tx.Commit();
                    moved++;
                    r.Status = "PASS";
                    r.Action += " — đã làm";
                }
            }

            group.Assimilate();
        }

        _status.Text = $"Đã xếp {moved} mạng về tầng; {undone} mạng hoàn tác (REVIEW). Ctrl+Z một lần bỏ cả lần này. " +
                       "Tiếp theo: chế độ «Tự động» → Phân tích để né các chỗ giao cắt còn lại.";
    }

    /// <summary>A fitting that breaks its system's BEND or BRANCH rule, or null.</summary>
    private (CombineStatus, string)? CheckFitting(FamilyInstance f)
    {
        var system = CombineProfileStore.System(f);
        if (system == MepSystemCode.Unknown)
            return null;
        var ends = (f.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>() ?? Enumerable.Empty<Connector>())
            .Where(c => c.ConnectorType == ConnectorType.End)
            .Select(c => c.CoordinateSystem.BasisZ)
            .ToList();
        static bool Level(XYZ d) => Math.Abs(d.Z) < 0.2;
        if (ends.Count == 2 && ends.All(Level))
        {
            var deflection = CombineAnalyzer.Deflection(ends[0].X, ends[0].Y, ends[0].Z, ends[1].X, ends[1].Y, ends[1].Z);
            if (deflection < 1)
                return null;
            var (status, reason) = _profile.CheckBend(system, deflection);
            return status == CombineStatus.Pass ? null : (status, reason);
        }

        if (ends.Count == 3)
        {
            var pairs = new[] { (0, 1, 2), (0, 2, 1), (1, 2, 0) };
            var (i, j, k) = pairs.OrderBy(p => ends[p.Item1].DotProduct(ends[p.Item2])).First();
            if (!Level(ends[i]) || !Level(ends[j]) || !Level(ends[k]))
                return null;
            var angle = CombineAnalyzer.AxisAngle(ends[i].X, ends[i].Y, ends[i].Z, ends[k].X, ends[k].Y, ends[k].Z);
            var (status, reason) = _profile.CheckBranch(system, angle);
            return status == CombineStatus.Pass ? null : (status, reason);
        }

        return null;
    }

    private static double PlanDistance(CombineRun r, double x, double y)
    {
        double dx = r.Bx - r.Ax, dy = r.By - r.Ay;
        var len2 = dx * dx + dy * dy;
        var t = len2 < 1 ? 0 : Math.Max(0, Math.Min(1, ((x - r.Ax) * dx + (y - r.Ay) * dy) / len2));
        double px = r.Ax + dx * t - x, py = r.Ay + dy * t - y;
        return Math.Sqrt(px * px + py * py);
    }

    // ── Apply ───────────────────────────────────────────────────────────

    private void Apply()
    {
        if (_rows.Count == 0)
            Analyze();
        var picked = _grid.SelectedItems.Cast<CombineRow>().ToList();
        var rows = (picked.Count > 0 ? picked : _rows).Where(r => r.Fixable).ToList();
        if (rows.Count == 0)
        {
            _status.Text = "Không có dòng FAIL nào Combine tự xử lý được (giao cắt có một bên được dời). Các dòng còn lại cần sửa tay.";
            return;
        }

        if (rows.All(r => r.IsStack))
        {
            ApplyCorridor(rows);
            return;
        }

        var doc = Doc;
        int made = 0, undone = 0;
        var notes = new List<string>();
        // Ids only grow: what a detour adds is newer than everything here now.
        var maxId = new FilteredElementCollector(doc).WhereElementIsNotElementType().Max(e => e.Id.Value);
        using (var group = new TransactionGroup(doc, "DSSTech Combine"))
        {
            group.Start();
            foreach (var g in rows.GroupBy(r => r.AId))
            {
                if (doc.GetElement(new ElementId(g.Key)) is not MEPCurve run)
                    continue;
                // One direction per run: the one most of its crossings want.
                var under = g.Count(r => r.Under) * 2 >= g.Count();
                var system = CombineProfileStore.System(run);
                var obstacles = g.Where(r => r.Under == under)
                    .Select(r => (Other: doc.GetElement(new ElementId(r.BId)), Clearance: UnitConv.MmToInternal(r.Conflict!.RequiredMm)))
                    .Where(o => o.Other is not null)
                    .Select(o => (Box: MepCombine.Box(MepCombine.Solids(o.Other!)), o.Clearance, Other: o.Other!))
                    .Where(o => o.Box is not null)
                    .ToList();
                if (obstacles.Count == 0)
                    continue;

                var runNotes = new List<string>();
                using var tx = new Transaction(doc, "DSSTech Combine detour");
                tx.Start();
                var count = 0;
                try
                {
                    count = MepCombine.Detour(doc, run, obstacles.Select(o => (o.Box!.Value.Min, o.Box!.Value.Max, o.Clearance)), under,
                        _profile.DetourAngle(system), runNotes);
                    doc.Regenerate();
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
                {
                    runNotes.Add($"{run.Id.Value}: {ex.Message}");
                }

                var failed = count == 0 || runNotes.Any(n => n.Contains("elbow") || n.Contains("nối lại"));
                if (!failed)
                {
                    // Post-check: the pieces now in place keep their clearance from what they went round, and every
                    // new elbow is joined at both ends.
                    var added = Added(run, maxId);
                    var pieces = added.OfType<MEPCurve>().Append(run).ToList();
                    var open = added.OfType<FamilyInstance>().SelectMany(MepCombine.Connectors).Count(c => c.ConnectorType == ConnectorType.End && !c.IsConnected);
                    var still = pieces.Select(p => CombineProfileStore.Run(p, _profile)).Where(p => p is not null)
                        .SelectMany(p => obstacles.Select(o => o.Other).OfType<MEPCurve>().Select(o => CombineProfileStore.Run(o, _profile))
                            .Where(o => o is not null).Select(o => CombineAnalyzer.Check(p!, o!, _profile)))
                        .Count(c => c is { Parallel: false, GapMm: var gap } && gap < c.RequiredMm - 1);
                    if (open > 0 || still > 0)
                    {
                        failed = true;
                        runNotes.Add($"{run.Id.Value}: kiểm tra sau khi dựng không đạt ({open} đầu cút hở, {still} chỗ còn thiếu khe hở)");
                    }
                }

                foreach (var r in g)
                {
                    r.Status = failed ? "REVIEW" : "PASS";
                    r.Action = failed ? "Đã thử, hoàn tác: " + (runNotes.FirstOrDefault() ?? "Revit không tạo được cút") : r.Action + " — đã làm";
                }

                if (failed)
                {
                    tx.RollBack();
                    undone++;
                }
                else
                {
                    tx.Commit();
                    made += count;
                }

                notes.AddRange(runNotes);
            }

            group.Assimilate();
        }

        foreach (var n in notes)
            DssTechLog.Write("combine: " + n);
        _status.Text = $"Đã đổi tuyến {made} chỗ; {undone} tuyến không đạt kiểm tra sau khi dựng nên hoàn tác (REVIEW). " +
                       "Undo một lần (Ctrl+Z) bỏ toàn bộ lần áp dụng này. Bấm «Phân tích» để kiểm tra lại.";
    }

    /// <summary>The pieces and elbows joined to <paramref name="run"/> that are newer than <paramref name="maxId"/>.</summary>
    private static List<Element> Added(Element run, long maxId)
    {
        var seen = new HashSet<long> { run.Id.Value };
        var result = new List<Element>();
        var open = new Stack<Element>();
        open.Push(run);
        while (open.Count > 0)
        {
            foreach (var c in MepCombine.Connectors(open.Pop()))
            {
                foreach (Connector r in c.AllRefs)
                {
                    var o = r.Owner;
                    if (o is null || o.Id.Value <= maxId || !seen.Add(o.Id.Value))
                        continue;
                    result.Add(o);
                    open.Push(o);
                }
            }
        }

        return result;
    }

    // ── View / report ───────────────────────────────────────────────────

    private void ShowSelected()
    {
        if (_grid.SelectedItem is not CombineRow r)
            return;
        var ids = new[] { r.AId, r.BId }.Where(id => id > 0).Select(id => new ElementId(id))
            .Where(id => Doc.GetElement(id) is not null).ToList();
        _app.ActiveUIDocument.Selection.SetElementIds(ids);
        if (ids.Count > 0)
            _app.ActiveUIDocument.ShowElements(ids);
        _status.Text = $"{r.Status}: {r.Element} — {r.Issue}. {r.Action}";
    }

    private void ExportExcel()
    {
        var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"{Doc.Title}-combine.xlsx", AddExtension = true };
        if (dlg.ShowDialog() != true)
            return;
        DSSTech.Core.TableWorkbookExporter.Export(dlg.FileName, "Combine", $"Combine MEPF — {Doc.Title} — {DateTime.Now:dd/MM/yyyy HH:mm} — quy tắc «{_profile.Name}»",
            new[] { "Trạng thái", "Phần tử", "ID", "Hệ", "Với", "ID với", "Vấn đề", "Xử lý", "X (mm)", "Y (mm)" },
            _rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.Status, r.Element, r.AId, r.System, r.Against, r.BId == 0 ? null : r.BId, r.Issue, r.Action, Math.Round(r.X), Math.Round(r.Y)
            }));
        _status.Text = $"Đã xuất Excel ({_rows.Count} dòng): {dlg.FileName}";
    }
}
