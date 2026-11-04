using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using DSSTech.Revit.Ui;
using Microsoft.Win32;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace DSSTech.Revit.ModelRevit;

/// <summary>A category group the clash check can test.</summary>
internal sealed record ClashSet(string Label, BuiltInCategory[] Categories, bool Structure);

/// <summary>«Check clash»: set A (this model) against set B (this model and links), hard clashes only.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ClashCheckCommand : IExternalCommand
{
    internal static readonly ClashSet[] Sets =
    {
        new("Ống gió", new[] { BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting }, false),
        new("Ống nước", new[] { BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting }, false),
        new("Máng cáp", new[] { BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting }, false),
        new("Thiết bị cơ điện", new[] { BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment }, false),
        new("Dầm", new[] { BuiltInCategory.OST_StructuralFraming }, true),
        new("Cột", new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns }, true),
        new("Sàn", new[] { BuiltInCategory.OST_Floors }, true),
        new("Tường / vách", new[] { BuiltInCategory.OST_Walls }, true),
        new("Trần", new[] { BuiltInCategory.OST_Ceilings }, true)
    };

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        var dlg = new ClashSetupWindow();
        dlg.SetOwner(uiApp.MainWindowHandle);
        if (dlg.ShowDialog() != true)
            return Result.Cancelled;

        Level? level = null;
        Level? above = null;
        if (dlg.LevelOnly)
        {
            if (!ModelRevitLevelContext.TryResolve(doc.ActiveView, doc, out var l, out var t, out var err))
            {
                TaskDialog.Show("DSSTech — Check clash", err);
                return Result.Cancelled;
            }

            level = l;
            above = t.Id == l.Id ? null : t;
        }

        var records = ClashEngine.Run(doc, dlg.SetA, dlg.SetB, dlg.WithLinks, dlg.ToleranceMm, level, above);
        ClashStore.Save(doc, records);
        if (records.Count == 0)
        {
            TaskDialog.Show("DSSTech — Check clash", "Không có xung đột giữa hai nhóm đã chọn.");
            return Result.Succeeded;
        }

        ClashResultsWindow.ShowFor(uiApp, records);
        return Result.Succeeded;
    }
}

/// <summary>«Kết quả»: the last clash results of this model, to review, fix, make views and export.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ClashResultsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc is null)
            return Result.Failed;
        var records = ClashStore.Load(doc);
        if (records.Count == 0)
        {
            TaskDialog.Show("DSSTech — Xung đột", "Chưa có kết quả. Chạy «Check clash» trước.");
            return Result.Cancelled;
        }

        ClashResultsWindow.ShowFor(uiApp, records);
        return Result.Succeeded;
    }
}

/// <summary>Last results per model, kept beside the log so they outlive the session.</summary>
internal static class ClashStore
{
    private static string PathFor(Document doc)
    {
        var name = string.Join("_", (doc.Title ?? "model").Split(System.IO.Path.GetInvalidFileNameChars()));
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSSTech", $"clash-{name}.json");
    }

    public static void Save(Document doc, List<ClashRecord> records)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFor(doc))!);
            File.WriteAllText(PathFor(doc), JsonSerializer.Serialize(records));
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"clash save failed: {ex.Message}");
        }
    }

    public static List<ClashRecord> Load(Document doc)
    {
        try
        {
            return File.Exists(PathFor(doc))
                ? JsonSerializer.Deserialize<List<ClashRecord>>(File.ReadAllText(PathFor(doc))) ?? new List<ClashRecord>()
                : new List<ClashRecord>();
        }
        catch (Exception)
        {
            return new List<ClashRecord>();
        }
    }
}

internal static class ClashEngine
{
    private sealed record Item(Element Element, Transform ToHost, string Model, List<Solid> Solids, XYZ Min, XYZ Max);

    public static List<ClashRecord> Run(Document doc, IReadOnlyList<ClashSet> a, IReadOnlyList<ClashSet> b, bool withLinks,
        double toleranceMm, Level? level, Level? above)
    {
        var zLo = level?.Elevation ?? double.MinValue;
        var zHi = above?.Elevation ?? (level is null ? double.MaxValue : level.Elevation + UnitConv.MmToInternal(6000));
        var itemsA = Collect(doc, Transform.Identity, "", a.SelectMany(s => s.Categories), zLo, zHi);
        var itemsB = Collect(doc, Transform.Identity, "", b.SelectMany(s => s.Categories), zLo, zHi);
        if (withLinks)
        {
            foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                if (link.GetLinkDocument() is { } linkDoc)
                    itemsB.AddRange(Collect(linkDoc, link.GetTotalTransform(), link.Name, b.SelectMany(s => s.Categories), zLo, zHi));
            }
        }

        var tol = UnitConv.MmToInternal(toleranceMm);
        var shared = doc.ActiveProjectLocation.GetTotalTransform().Inverse;
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
        var seen = new HashSet<(long, string, long)>();
        var records = new List<ClashRecord>();
        foreach (var x in itemsA)
        {
            var joined = MepCombine.Connectors(x.Element).SelectMany(c => c.AllRefs.Cast<Connector>()).Select(c => c.Owner?.Id).ToHashSet();
            foreach (var y in itemsB)
            {
                if (y.Model.Length == 0 && (y.Element.Id == x.Element.Id || joined.Contains(y.Element.Id)))
                    continue;
                if (x.Min.X > y.Max.X || x.Max.X < y.Min.X || x.Min.Y > y.Max.Y || x.Max.Y < y.Min.Y || x.Min.Z > y.Max.Z || x.Max.Z < y.Min.Z)
                    continue;
                // Each pair once, whichever side found it.
                var key = y.Model.Length == 0
                    ? (Math.Min(x.Element.Id.Value, y.Element.Id.Value), "", Math.Max(x.Element.Id.Value, y.Element.Id.Value))
                    : (x.Element.Id.Value, y.Model, y.Element.Id.Value);
                if (seen.Contains(key))
                    continue;
                var hit = Overlap(x.Solids, y.Solids, tol);
                if (hit is null)
                    continue;
                seen.Add(key);
                var p = hit.Value.Center;
                var s = shared.OfPoint(p);
                records.Add(new ClashRecord
                {
                    Index = records.Count + 1,
                    AId = x.Element.Id.Value,
                    ACategory = x.Element.Category?.Name ?? "",
                    AName = Name(x.Element),
                    BId = y.Element.Id.Value,
                    BCategory = y.Element.Category?.Name ?? "",
                    BName = Name(y.Element),
                    BModel = y.Model,
                    Level = levels.LastOrDefault(l => l.Elevation <= p.Z + 1e-6)?.Name ?? "",
                    X = UnitConv.ToMm(p.X),
                    Y = UnitConv.ToMm(p.Y),
                    Z = UnitConv.ToMm(p.Z),
                    SharedX = UnitConv.ToMm(s.X) / 1000,
                    SharedY = UnitConv.ToMm(s.Y) / 1000,
                    SharedZ = UnitConv.ToMm(s.Z) / 1000,
                    VolumeCm3 = hit.Value.Volume * 28316.846592
                });
            }
        }

        DssTechLog.Write($"clash: A={itemsA.Count} B={itemsB.Count} clashes={records.Count}");
        return records;
    }

    private static List<Item> Collect(Document source, Transform toHost, string model, IEnumerable<BuiltInCategory> categories, double zLo, double zHi)
    {
        var list = new List<Item>();
        var filter = new ElementMulticategoryFilter(categories.Distinct().ToList());
        foreach (var e in new FilteredElementCollector(source).WherePasses(filter).WhereElementIsNotElementType())
        {
            var solids = MepCombine.Solids(e, toHost).ToList();
            var box = MepCombine.Box(solids);
            if (box is null || box.Value.Max.Z < zLo || box.Value.Min.Z > zHi)
                continue;
            list.Add(new Item(e, toHost, model, solids, box.Value.Min, box.Value.Max));
        }

        return list;
    }

    /// <summary>Overlap of two solid sets thicker than the tolerance: its centre and volume.</summary>
    private static (XYZ Center, double Volume)? Overlap(List<Solid> a, List<Solid> b, double tol)
    {
        foreach (var sa in a)
        foreach (var sb in b)
        {
            try
            {
                var common = BooleanOperationsUtils.ExecuteBooleanOperation(sa, sb, BooleanOperationsType.Intersect);
                if (common is null || common.Volume < 1e-6)
                    continue;
                var bb = common.GetBoundingBox();
                var size = bb.Max - bb.Min;
                if (Math.Min(size.X, Math.Min(size.Y, size.Z)) < tol)
                    continue;
                return (common.ComputeCentroid(), common.Volume);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Boolean failed on this pair of faces: the boxes still tell.
                var ba = MepCombine.Box(new[] { sa });
                var bbx = MepCombine.Box(new[] { sb });
                if (ba is null || bbx is null)
                    continue;
                var min = new XYZ(Math.Max(ba.Value.Min.X, bbx.Value.Min.X), Math.Max(ba.Value.Min.Y, bbx.Value.Min.Y), Math.Max(ba.Value.Min.Z, bbx.Value.Min.Z));
                var max = new XYZ(Math.Min(ba.Value.Max.X, bbx.Value.Max.X), Math.Min(ba.Value.Max.Y, bbx.Value.Max.Y), Math.Min(ba.Value.Max.Z, bbx.Value.Max.Z));
                var d = max - min;
                if (d.X < tol || d.Y < tol || d.Z < tol)
                    continue;
                return ((min + max) / 2, d.X * d.Y * d.Z);
            }
        }

        return null;
    }

    private static string Name(Element e) => e is FamilyInstance fi ? $"{fi.Symbol.FamilyName} : {fi.Name}" : e.Name;

    /// <summary>The element of a record in this model (A always; B when not linked).</summary>
    public static Element? Host(Document doc, long id) => doc.GetElement(new ElementId(id));

    /// <summary>B's box in this model's frame, linked or not.</summary>
    public static (XYZ Min, XYZ Max)? BoxOfB(Document doc, ClashRecord r)
    {
        if (r.BModel.Length == 0)
            return Host(doc, r.BId) is { } e ? MepCombine.Box(MepCombine.Solids(e)) : null;
        var link = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
            .FirstOrDefault(l => l.Name == r.BModel);
        var linked = link?.GetLinkDocument()?.GetElement(new ElementId(r.BId));
        return linked is null ? null : MepCombine.Box(MepCombine.Solids(linked, link!.GetTotalTransform()));
    }
}

/// <summary>Which categories against which, links, scope, tolerance.</summary>
internal sealed class ClashSetupWindow : DssDialog
{
    private readonly List<(ClashSet Set, CheckBox Box)> _a = new();
    private readonly List<(ClashSet Set, CheckBox Box)> _b = new();
    private readonly CheckBox _links;
    private readonly ComboBox _scope;
    private readonly TextBox _tolerance;

    public List<ClashSet> SetA { get; } = new();
    public List<ClashSet> SetB { get; } = new();
    public bool WithLinks { get; private set; }
    public bool LevelOnly { get; private set; }
    public double ToleranceMm { get; private set; }

    public ClashSetupWindow()
        : base("DSSTech — Check clash", "Nhóm A (trong model này) kiểm tra với nhóm B (model này và file link). Chỉ va chạm cứng, giao dày hơn dung sai.", 620, 700)
    {
        var colA = new StackPanel();
        var colB = new StackPanel();
        foreach (var set in ClashCheckCommand.Sets)
        {
            if (!set.Structure)
            {
                var a = DssTheme.Option(set.Label, set.Label is "Ống gió" or "Ống nước" or "Máng cáp");
                _a.Add((set, a));
                colA.Children.Add(a);
            }

            var b = DssTheme.Option(set.Label, set.Structure && set.Label is "Dầm" or "Cột" or "Tường / vách" || set.Label == "Máng cáp");
            _b.Add((set, b));
            colB.Children.Add(b);
        }

        _links = DssTheme.Option("Nhóm B gồm cả file link (kết cấu, kiến trúc, MEP khác)", true);
        _scope = DssTheme.Combo(new[] { "Level đang mở", "Toàn model" }, "Level đang mở");
        _tolerance = DssTheme.Input("10");
        AddSection(1, "Nhóm", DssTheme.Pair(DssTheme.Field("A — phần tử sẽ được sửa", colA), DssTheme.Field("B — kiểm tra với", colB)));
        AddSection(2, "Phạm vi", _links, DssTheme.Pair(DssTheme.Field("Phạm vi", _scope), DssTheme.Field("Dung sai (mm)", _tolerance)));
        PrimaryButton.Content = "Check clash";
        PrimaryButton.Click += (_, _) =>
        {
            SetA.AddRange(_a.Where(x => x.Box.IsChecked == true).Select(x => x.Set));
            SetB.AddRange(_b.Where(x => x.Box.IsChecked == true).Select(x => x.Set));
            if (SetA.Count == 0 || SetB.Count == 0)
            {
                MessageBox.Show(this, "Chọn ít nhất một loại ở mỗi nhóm.", "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
                SetA.Clear();
                SetB.Clear();
                return;
            }

            WithLinks = _links.IsChecked == true;
            LevelOnly = DssTheme.ComboValue(_scope) == "Level đang mở";
            ToleranceMm = double.TryParse(_tolerance.Text.Trim(), out var t) && t >= 0 ? t : 10;
            DialogResult = true;
            Close();
        };
    }
}

/// <summary>
/// The clash list: zoom to one, make one side detour round the other on the ticked rows, make 3D views, export BCF,
/// Navisworks viewpoints and Excel.
/// </summary>
internal sealed class ClashResultsWindow : DssDialog
{
    private readonly UIApplication _app;
    private readonly List<ClashRecord> _records;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;

    private ClashResultsWindow(UIApplication app, List<ClashRecord> records)
        : base("DSSTech — Xung đột", $"{records.Count} xung đột. Chọn dòng (Ctrl/Shift để chọn nhiều) rồi bấm thao tác.", 1100, 760)
    {
        _app = app;
        _records = records;
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            ItemsSource = _records,
            Height = 420,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        foreach (var (header, path) in new[]
                 {
                     ("Tên", nameof(ClashRecord.Name)), ("Trạng thái", nameof(ClashRecord.Status)), ("Level", nameof(ClashRecord.Level)),
                     ("Loại A", nameof(ClashRecord.ACategory)), ("A", nameof(ClashRecord.AName)), ("ID A", nameof(ClashRecord.AId)),
                     ("Loại B", nameof(ClashRecord.BCategory)), ("B", nameof(ClashRecord.BName)), ("ID B", nameof(ClashRecord.BId)),
                     ("Model B", nameof(ClashRecord.BModel)), ("Ghi chú", nameof(ClashRecord.Note))
                 })
            _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path) });
        _status = new TextBlock { Foreground = DssTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

        var actions = new WrapPanel();
        Button Act(string text, Action run)
        {
            var b = DssTheme.Quiet(text);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Click += (_, _) =>
            {
                try
                {
                    run();
                }
                catch (Exception ex)
                {
                    _status.Text = "Lỗi: " + ex.Message;
                    DssTechLog.Write($"clash action: {ex}");
                }

                _grid.Items.Refresh();
            };
            actions.Children.Add(b);
            return b;
        }

        Act("Xem trong model", ShowSelected);
        Act("A né B (đi dưới)", () => Avoid(aYields: true, under: true));
        Act("A né B (đi trên)", () => Avoid(aYields: true, under: false));
        Act("B né A (đi dưới)", () => Avoid(aYields: false, under: true));
        Act("Tạo view 3D cho dòng chọn", () => MakeViews(Selected()));
        Act("Xuất BCF", ExportBcf);
        Act("Xuất viewpoint Navisworks (XML)", ExportNavisworks);
        Act("Xuất Excel", ExportExcel);
        AddSection(1, "Danh sách", _grid, _status);
        AddSection(2, "Thao tác", actions);
        PrimaryButton.Content = "Đóng";
        PrimaryButton.Click += (_, _) => Close();
        CancelButton.Visibility = System.Windows.Visibility.Collapsed;
    }

    public static void ShowFor(UIApplication app, List<ClashRecord> records)
    {
        var w = new ClashResultsWindow(app, records);
        w.SetOwner(app.MainWindowHandle);
        w.ShowDialog();
        ClashStore.Save(app.ActiveUIDocument.Document, records);
    }

    private Document Doc => _app.ActiveUIDocument.Document;

    private List<ClashRecord> Selected()
    {
        var rows = _grid.SelectedItems.Cast<ClashRecord>().ToList();
        return rows.Count > 0 ? rows : _records;
    }

    private void ShowSelected()
    {
        var r = _grid.SelectedItem as ClashRecord ?? _records[0];
        var ids = new List<ElementId> { new(r.AId) };
        if (r.BModel.Length == 0)
            ids.Add(new ElementId(r.BId));
        ids = ids.Where(id => Doc.GetElement(id) is not null).ToList();
        _app.ActiveUIDocument.Selection.SetElementIds(ids);
        if (ids.Count > 0)
            _app.ActiveUIDocument.ShowElements(ids);
        _status.Text = $"{r.Title} — {r.Description}";
    }

    private void Avoid(bool aYields, bool under)
    {
        var rows = _grid.SelectedItems.Cast<ClashRecord>().ToList();
        if (rows.Count == 0)
        {
            _status.Text = "Chọn các dòng (khu vực) cần đổi tuyến.";
            return;
        }

        var made = 0;
        var notes = new List<string>();
        foreach (var group in rows.GroupBy(r => aYields ? r.AId : r.BId))
        {
            var first = group.First();
            if (!aYields && first.BModel.Length > 0)
            {
                notes.Add($"{first.Name}: B nằm trong file link, không sửa được");
                continue;
            }

            if (ClashEngine.Host(Doc, group.Key) is not MEPCurve run)
            {
                notes.Add($"{first.Name}: phần tử né phải là đoạn ống / máng (không phải fitting, thiết bị)");
                continue;
            }

            var obstacles = group
                .Select(r => aYields ? ClashEngine.BoxOfB(Doc, r) : (ClashEngine.Host(Doc, r.AId) is { } a ? MepCombine.Box(MepCombine.Solids(a)) : null))
                .Where(b => b is not null).Select(b => b!.Value).ToList();
            var (m, n) = MepAvoidCommand.Apply(Doc, new List<MEPCurve> { run }, obstacles, under, UnitConv.MmToInternal(50));
            made += m;
            notes.AddRange(n);
            foreach (var r in group)
            {
                r.Status = m > 0 ? "Đã sửa" : r.Status;
                r.Note = m > 0 ? $"{(aYields ? "A" : "B")} đi {(under ? "dưới" : "trên")}" : "Cần sửa tay";
            }
        }

        _status.Text = $"Đổi tuyến {made} chỗ. Chạy lại «Check clash» để kiểm tra. " + MepCombine.Summary(notes, 4).Replace(Environment.NewLine, " ");
    }

    /// <summary>One 3D view per clash: section box 1 m round it, A in red; named «DSS Clash 001».</summary>
    private Dictionary<int, View3D> MakeViews(List<ClashRecord> rows)
    {
        var doc = Doc;
        var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
        var existing = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(v => !v.IsTemplate)
            .ToDictionary(v => v.Name, v => v);
        var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
        var red = new OverrideGraphicSettings().SetProjectionLineColor(new Color(255, 0, 0));
        if (solid is not null)
            red = red.SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(new Color(255, 0, 0));
        var yellow = new OverrideGraphicSettings().SetProjectionLineColor(new Color(255, 190, 0));
        if (solid is not null)
            yellow = yellow.SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(new Color(255, 190, 0));
        var views = new Dictionary<int, View3D>();
        using var tx = new Transaction(doc, "DSSTech clash views");
        tx.Start();
        foreach (var r in rows)
        {
            var name = $"DSS {r.Name}";
            if (!existing.TryGetValue(name, out var view))
            {
                view = View3D.CreateIsometric(doc, type.Id);
                view.Name = name;
            }

            var p = new XYZ(UnitConv.MmToInternal(r.X), UnitConv.MmToInternal(r.Y), UnitConv.MmToInternal(r.Z));
            var half = UnitConv.MmToInternal(1000);
            view.IsSectionBoxActive = true;
            view.SetSectionBox(new BoundingBoxXYZ { Min = p - new XYZ(half, half, half), Max = p + new XYZ(half, half, half) });
            var dir = new XYZ(1, 1, -1).Normalize();
            var eye = p - dir * UnitConv.MmToInternal(4000);
            var up = (XYZ.BasisZ - dir * dir.DotProduct(XYZ.BasisZ)).Normalize();
            view.SetOrientation(new ViewOrientation3D(eye, up, dir));
            if (doc.GetElement(new ElementId(r.AId)) is not null)
                view.SetElementOverrides(new ElementId(r.AId), red);
            if (r.BModel.Length == 0 && doc.GetElement(new ElementId(r.BId)) is not null)
                view.SetElementOverrides(new ElementId(r.BId), yellow);
            views[r.Index] = view;
        }

        tx.Commit();
        _status.Text = $"Đã tạo / cập nhật {views.Count} view 3D «DSS Clash …» (A đỏ, B vàng, hộp cắt 2 m).";
        return views;
    }

    private void ExportBcf()
    {
        var dlg = new SaveFileDialog { Filter = "BCF 2.1 (*.bcfzip)|*.bcfzip", FileName = $"{Doc.Title}-clash.bcfzip", AddExtension = true };
        if (dlg.ShowDialog() != true)
            return;
        var rows = Selected();
        var snapshots = new Dictionary<int, byte[]>();
        if (MessageBox.Show(this, "Kèm ảnh chụp từng xung đột (tạo view 3D và xuất ảnh, chậm hơn)?", "DSSTech",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var views = MakeViews(rows);
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DSSTech-bcf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach (var (index, view) in views)
            {
                try
                {
                    var options = new ImageExportOptions
                    {
                        ExportRange = ExportRange.SetOfViews,
                        FilePath = System.IO.Path.Combine(dir, $"c{index}"),
                        ZoomType = ZoomFitType.FitToPage,
                        PixelSize = 900,
                        HLRandWFViewsFileType = ImageFileType.PNG,
                        ShadowViewsFileType = ImageFileType.PNG,
                        ImageResolution = ImageResolution.DPI_150
                    };
                    options.SetViewsAndSheets(new List<ElementId> { view.Id });
                    Doc.ExportImage(options);
                    var file = Directory.GetFiles(dir, $"c{index}*.png").FirstOrDefault();
                    if (file is not null)
                        snapshots[index] = File.ReadAllBytes(file);
                }
                catch (Exception ex)
                {
                    DssTechLog.Write($"bcf snapshot {index}: {ex.Message}");
                }
            }
        }

        ClashExchange.WriteBcf(dlg.FileName, rows, Doc.Title, snapshots);
        _status.Text = $"Đã xuất BCF 2.1 ({rows.Count} xung đột, {snapshots.Count} ảnh): {dlg.FileName}";
    }

    private void ExportNavisworks()
    {
        var dlg = new SaveFileDialog { Filter = "Navisworks viewpoints (*.xml)|*.xml", FileName = $"{Doc.Title}-clash-viewpoints.xml", AddExtension = true };
        if (dlg.ShowDialog() != true)
            return;
        var rows = Selected();
        ClashExchange.WriteNavisworksViewpoints(dlg.FileName, rows);
        _status.Text = $"Đã xuất {rows.Count} viewpoint: {dlg.FileName}. Navisworks: File ▸ Import ▸ Viewpoints XML (model NWC xuất theo Shared coordinates).";
    }

    private void ExportExcel()
    {
        var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"{Doc.Title}-clash.xlsx", AddExtension = true };
        if (dlg.ShowDialog() != true)
            return;
        var rows = Selected();
        DSSTech.Core.TableWorkbookExporter.Export(dlg.FileName, "Xung đột", $"Báo cáo xung đột — {Doc.Title} — {DateTime.Now:dd/MM/yyyy HH:mm}",
            new[]
            {
                "Tên", "Trạng thái", "Level", "Loại A", "A", "ID A", "Loại B", "B", "ID B", "Model B",
                "X (mm)", "Y (mm)", "Z (mm)", "Shared X (m)", "Shared Y (m)", "Shared Z (m)", "Thể tích giao (cm³)", "Ghi chú"
            },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.Name, r.Status, r.Level, r.ACategory, r.AName, r.AId, r.BCategory, r.BName, r.BId, r.BModel,
                Math.Round(r.X), Math.Round(r.Y), Math.Round(r.Z), Math.Round(r.SharedX, 3), Math.Round(r.SharedY, 3), Math.Round(r.SharedZ, 3),
                Math.Round(r.VolumeCm3), r.Note
            }));
        _status.Text = $"Đã xuất Excel ({rows.Count} dòng): {dlg.FileName}";
    }
}
