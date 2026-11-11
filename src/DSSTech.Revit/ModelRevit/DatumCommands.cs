using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using DSSTech.Revit.Cad;
using DSSTech.Revit.Ui;
using Grid = Autodesk.Revit.DB.Grid;
using TextBox = System.Windows.Controls.TextBox;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Grids from the CAD link: every grid line with a named bubble, on any layer (<see cref="GridPlanner"/>), placed on
/// whole 5 mm spacings; then a chain and an overall dimension per grid direction in the plan.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CadDatumCommand : IExternalCommand
{
    private const string Title = "DSSTech — Trục";
    private const double ChainOffsetMm = 2000;
    private const double OverallOffsetMm = 3200;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        var doc = uidoc?.Document;
        var view = doc?.ActiveView;
        if (doc is null || uidoc is null || view is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        try
        {
            var import = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<ImportInstance>().FirstOrDefault();
            if (import is null)
            {
                var picked = uidoc.Selection.PickObject(ObjectType.Element, new ImportInstanceSelectionFilter(),
                    "Chọn CAD link có lưới trục (đã Move/Rotate khớp model).");
                import = doc.GetElement(picked.ElementId) as ImportInstance;
            }

            if (import is null)
                return Result.Cancelled;

            CadLinkLayerVisibility.EnsureAllImportLayersVisible(view, import);
            var geom = ImportInstanceGeometryReader.Read(import, view);
            try
            {
                var capturePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSSTech", "grid-capture.json");
                PlanCapture.From(geom, "(mọi layer)", ModelRevitPartKind.Column, "", "", new AnnotationSize(null, null, null, ""),
                    new ModelRevitPlacement(), null, null).Save(capturePath);
            }
            catch (Exception ex)
            {
                DssTechLog.Write($"grid capture failed: {ex.Message}");
            }

            var adjusted = 0;
            var lines = GridPlanner.Plan(geom, m =>
            {
                DssTechLog.Write(m);
                if (m.Contains("spacing"))
                    adjusted++;
            });
            if (lines.Count == 0)
            {
                TaskDialog.Show(Title,
                    "Không thấy trục trên CAD: cần nét thẳng chạy vào vòng tròn đầu trục có tên (1, A, 1.A…).");
                return Result.Cancelled;
            }

            var created = new List<Grid>();
            var existingCount = 0;
            var renamedFail = new List<string>();
            using (var tx = new Transaction(doc, "DSSTech grids from CAD"))
            {
                tx.Start();
                var existing = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().ToList();
                var z = (view as ViewPlan)?.GenLevel?.Elevation ?? 0;
                foreach (var g in lines)
                {
                    var a = new XYZ(UnitConv.MmToInternal(g.X0), UnitConv.MmToInternal(g.Y0), z);
                    var b = new XYZ(UnitConv.MmToInternal(g.X1), UnitConv.MmToInternal(g.Y1), z);
                    if (existing.Any(e => string.Equals(e.Name, g.Name, StringComparison.OrdinalIgnoreCase) || SameAxis(e, a, b)))
                    {
                        existingCount++;
                        continue;
                    }

                    var grid = Grid.Create(doc, Line.CreateBound(a, b));
                    try
                    {
                        grid.Name = g.Name;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        renamedFail.Add(g.Name);
                    }

                    created.Add(grid);
                    existing.Add(grid);
                }

                tx.Commit();
            }

            var dims = view is ViewPlan ? Dimension(doc, view) : 0;
            var summary = new StringBuilder();
            summary.AppendLine($"Trục đọc từ CAD (mọi layer, theo vòng tròn đầu trục có tên): {lines.Count}.");
            summary.AppendLine($"Tạo mới {created.Count}; đã có {existingCount} (trùng tên hoặc trùng vị trí).");
            summary.AppendLine($"Trục cùng phương được làm song song tuyệt đối và đặt theo bước 5 mm từ trục đầu (trái/dưới): chỉnh {adjusted} trục.");
            summary.AppendLine(view is ViewPlan
                ? $"Kích thước trục (chuỗi + tổng) trong view {view.Name}: {dims} đường mới."
                : "View đang mở không phải mặt bằng: không tạo kích thước trục.");
            if (renamedFail.Count > 0)
                summary.AppendLine($"Tên đã có trong model, giữ tên Revit tự đặt: {string.Join(", ", renamedFail)}.");
            DoneReport.Show(Title, summary.ToString());
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
    }

    /// <summary>Per grid direction: a chain through every grid and an overall dimension, beyond the grids' start.</summary>
    private static int Dimension(Document doc, View view)
    {
        var grids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<Grid>()
            .Where(g => g.Curve is Line)
            .ToList();
        var families = new List<List<Grid>>();
        foreach (var g in grids)
        {
            var d = ((Line)g.Curve).Direction;
            var family = families.FirstOrDefault(f => Math.Abs(((Line)f[0].Curve).Direction.CrossProduct(d).Z) < 0.01);
            if (family is null)
                families.Add(new List<Grid> { g });
            else
                family.Add(g);
        }

        var existing = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Autodesk.Revit.DB.Dimension))
            .Cast<Autodesk.Revit.DB.Dimension>()
            .Select(d => d.References.Cast<Reference>().Select(r => r.ElementId).OrderBy(id => id.Value).ToList())
            .ToList();
        var count = 0;
        using var tx = new Transaction(doc, "DSSTech grid dimensions");
        tx.Start();
        foreach (var family in families.Where(f => f.Count >= 2))
        {
            var u = ((Line)family[0].Curve).Direction;
            if (u.X < -1e-9 || (Math.Abs(u.X) < 1e-9 && u.Y < 0))
                u = u.Negate();
            var n = new XYZ(-u.Y, u.X, 0);
            var sorted = family.OrderBy(g => g.Curve.GetEndPoint(0).DotProduct(n)).ToList();
            var start = family.Min(g => Math.Min(g.Curve.GetEndPoint(0).DotProduct(u), g.Curve.GetEndPoint(1).DotProduct(u)));
            var z = family[0].Curve.GetEndPoint(0).Z;

            Line At(double offsetMm)
            {
                var along = start - UnitConv.MmToInternal(offsetMm);
                var lo = sorted[0].Curve.GetEndPoint(0).DotProduct(n);
                var hi = sorted[sorted.Count - 1].Curve.GetEndPoint(0).DotProduct(n);
                var p0 = u * along + n * lo;
                var p1 = u * along + n * hi;
                return Line.CreateBound(new XYZ(p0.X, p0.Y, z), new XYZ(p1.X, p1.Y, z));
            }

            bool Has(IEnumerable<Grid> set)
            {
                var ids = set.Select(g => g.Id).OrderBy(id => id.Value).ToList();
                return existing.Any(e => e.SequenceEqual(ids));
            }

            var chain = new ReferenceArray();
            foreach (var g in sorted)
                chain.Append(new Reference(g));
            if (!Has(sorted))
            {
                doc.Create.NewDimension(view, At(ChainOffsetMm), chain);
                count++;
            }

            if (sorted.Count > 2 && !Has(new[] { sorted[0], sorted[sorted.Count - 1] }))
            {
                var overall = new ReferenceArray();
                overall.Append(new Reference(sorted[0]));
                overall.Append(new Reference(sorted[sorted.Count - 1]));
                doc.Create.NewDimension(view, At(OverallOffsetMm), overall);
                count++;
            }
        }

        tx.Commit();
        return count;
    }

    private static bool SameAxis(Grid grid, XYZ a, XYZ b)
    {
        if (grid.Curve is not Line line)
            return false;
        var tol = UnitConv.MmToInternal(5);
        var d = (b - a).Normalize();
        if (Math.Abs(line.Direction.CrossProduct(d).Z) > 0.01)
            return false;
        var n = new XYZ(-d.Y, d.X, 0);
        return Math.Abs((line.GetEndPoint(0) - a).DotProduct(n)) <= tol;
    }
}

/// <summary>
/// Levels from a table: how many storeys the file has and the height of each. Levels already in the model are
/// renamed and moved to match their row; new rows get a level and a floor plan. Levels left out of the table stay.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ModelLevelCommand : IExternalCommand
{
    private const string Title = "DSSTech — Level";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
        var rows = new List<LevelRow>();
        for (var i = 0; i < levels.Count; i++)
        {
            var next = i + 1 < levels.Count ? levels[i + 1] : null;
            rows.Add(new LevelRow
            {
                Id = levels[i].Id,
                Name = levels[i].Name,
                HeightMm = next is null ? null : Math.Round(UnitConv.ToMm(next.Elevation - levels[i].Elevation))
            });
        }

        if (rows.Count == 0)
        {
            rows.Add(new LevelRow { Name = "TẦNG 1", HeightMm = 3600 });
            rows.Add(new LevelRow { Name = "TẦNG 2", HeightMm = 3600 });
            rows.Add(new LevelRow { Name = "MÁI" });
        }

        var baseMm = levels.Count > 0 ? Math.Round(UnitConv.ToMm(levels[0].Elevation)) : 0;
        var dlg = new LevelSetupWindow(rows, baseMm);
        dlg.SetOwner(commandData.Application.MainWindowHandle);
        if (dlg.ShowDialog() != true)
            return Result.Cancelled;

        var table = dlg.Result;
        var elevations = new List<double>();
        var at = dlg.BaseMm;
        foreach (var row in table)
        {
            elevations.Add(at);
            at += row.HeightMm ?? 0;
        }

        int moved = 0, made = 0, plans = 0;
        var failed = new List<string>();
        using (var tx = new Transaction(doc, "DSSTech levels"))
        {
            tx.Start();
            // Names may swap between rows: park the kept levels on temporary names first.
            var kept = table.Where(r => r.Id is not null).Select(r => (Level)doc.GetElement(r.Id)).ToList();
            foreach (var level in kept)
                level.Name = "dsst-tmp " + level.Id.Value;
            var planType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan);
            var levelType = LevelTypeFor(doc, levels);
            for (var i = 0; i < table.Count; i++)
            {
                var row = table[i];
                // Revit refuses names with \ : { } [ ] | ; < > ? ` ~ (the whole command failed on them).
                row.Name = CleanName(row.Name);
                var elevation = UnitConv.MmToInternal(elevations[i]);
                Level level;
                if (row.Id is not null)
                {
                    level = (Level)doc.GetElement(row.Id);
                    if (Math.Abs(level.Elevation - elevation) > 1e-6)
                    {
                        level.Elevation = elevation;
                        moved++;
                    }
                }
                else
                {
                    level = Level.Create(doc, elevation);
                    // Level.Create takes the project's default type (often "No Head"): the type the levels already
                    // there use instead.
                    if (levelType is not null && level.GetTypeId() != levelType)
                        level.ChangeTypeId(levelType);
                    made++;
                    if (planType is not null)
                    {
                        var plan = ViewPlan.Create(doc, planType.Id, level.Id);
                        try
                        {
                            plan.Name = row.Name;
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException)
                        {
                            // A view with this name exists: Revit's own name stays.
                        }

                        plans++;
                    }
                }

                try
                {
                    level.Name = row.Name;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    failed.Add(row.Name);
                }
            }

            tx.Commit();
        }

        var left = levels.Where(l => table.All(r => r.Id != l.Id)).Select(l => l.Name).ToList();
        var summary = new StringBuilder();
        summary.AppendLine($"Bảng tầng: {table.Count} level, cao độ đầu {dlg.BaseMm:0} mm.");
        summary.AppendLine($"Level mới: {made} (kèm {plans} mặt bằng); level đã có đổi cao độ: {moved}.");
        for (var i = 0; i < table.Count; i++)
            summary.AppendLine($"  {table[i].Name}: {elevations[i]:+0;-0;0} mm" + (table[i].HeightMm is double h ? $" · cao {h:0}" : ""));
        if (left.Count > 0)
            summary.AppendLine($"Level không có trong bảng (giữ nguyên): {string.Join(", ", left)}.");
        if (failed.Count > 0)
            summary.AppendLine($"Không đặt được tên (trùng tên khác trong model): {string.Join(", ", failed)}.");
        DoneReport.Show(Title, summary.ToString());
        return Result.Succeeded;
    }

    /// <summary>
    /// The type for new levels: the one most of the levels already there use (those with a head first); with none, a
    /// level type with a head symbol, not "No Head".
    /// </summary>
    private static ElementId? LevelTypeFor(Document doc, IEnumerable<Level> existing)
    {
        static bool Headed(Element? type) =>
            type is not null && type.Name.IndexOf("No Head", StringComparison.OrdinalIgnoreCase) < 0 &&
            type.get_Parameter(BuiltInParameter.LEVEL_HEAD_TAG)?.AsElementId() is { } head && head != ElementId.InvalidElementId;
        var used = existing.GroupBy(l => l.GetTypeId())
            .OrderByDescending(g => Headed(doc.GetElement(g.Key)) ? 1 : 0).ThenByDescending(g => g.Count())
            .Select(g => g.Key).FirstOrDefault();
        if (used is not null && Headed(doc.GetElement(used)))
            return used;
        return new FilteredElementCollector(doc).OfClass(typeof(LevelType)).FirstOrDefault(Headed)?.Id ?? used;
    }

    /// <summary>A level name Revit takes: its prohibited characters dropped, blank as "Level".</summary>
    private static string CleanName(string name)
    {
        var clean = new string(name.Where(c => "\\:{}[]|;<>?`~".IndexOf(c) < 0).ToArray()).Trim();
        return clean.Length > 0 ? clean : "Level";
    }
}

internal sealed class LevelRow
{
    public ElementId? Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Storey height up to the next level; none for the top level.</summary>
    public double? HeightMm { get; set; }
}

/// <summary>Table of storeys: name and height each, elevations summed from the first level's.</summary>
internal sealed class LevelSetupWindow : DssDialog
{
    private readonly StackPanel _rows = new();
    private readonly TextBox _base;
    private readonly List<(LevelRow Row, TextBox Name, TextBox Height, TextBlock Elevation, System.Windows.Controls.Grid Line)> _items = new();

    /// <summary>
    /// The name of a storey added on top: one more than the highest "TẦNG n" in the table (basements, "TẦNG HẦM 1",
    /// are not counted: two basements and TẦNG 1–4 give TẦNG 5, not TẦNG 7); with no numbered storey, the storeys
    /// above the basements counted.
    /// </summary>
    private string NextStoreyName()
    {
        var names = _items.Select(i => i.Name.Text.Trim()).ToList();
        var numbers = names
            .Select(n => System.Text.RegularExpressions.Regex.Match(n, @"^(?:TẦNG|TANG|LEVEL|L)\s*(\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        var next = numbers.Count > 0
            ? numbers.Max() + 1
            : names.Count(n => n.IndexOf("HẦM", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("HAM", StringComparison.OrdinalIgnoreCase) < 0) + 1;
        return $"TẦNG {next}";
    }

    public List<LevelRow> Result { get; } = new();
    public double BaseMm { get; private set; }

    public LevelSetupWindow(IEnumerable<LevelRow> rows, double baseMm)
        : base("DSSTech — Level", "Khai báo các tầng từ dưới lên: tên và chiều cao tầng (tới level kế trên). Cao độ tự cộng dồn.", 620, 640)
    {
        _base = DssTheme.Input(baseMm.ToString("0"));
        _base.TextChanged += (_, _) => Recalc();
        var header = Row(new TextBlock { Text = "Tên level", Foreground = DssTheme.Muted },
            new TextBlock { Text = "Cao tầng (mm)", Foreground = DssTheme.Muted },
            new TextBlock { Text = "Cao độ (mm)", Foreground = DssTheme.Muted }, null);
        foreach (var row in rows)
            Add(row);
        var add = DssTheme.Quiet("+ Thêm tầng");
        add.Click += (_, _) =>
        {
            // The new storey goes on top: the old top level gets a height.
            if (_items.Count > 0 && string.IsNullOrWhiteSpace(_items[_items.Count - 1].Height.Text))
                _items[_items.Count - 1].Height.Text = "3600";
            Add(new LevelRow { Name = NextStoreyName() });
            Recalc();
        };
        AddSection(1, "Cao độ level thấp nhất", DssTheme.Field("Cao độ (mm)", _base));
        AddSection(2, "Các tầng", header, _rows, add);
        PrimaryButton.Content = "Cập nhật level";
        PrimaryButton.Click += (_, _) =>
        {
            if (!Collect(out var error))
            {
                MessageBox.Show(this, error, "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        };
        Recalc();
    }

    private void Add(LevelRow row)
    {
        var name = DssTheme.Input(row.Name);
        var height = DssTheme.Input(row.HeightMm?.ToString("0") ?? "");
        var elevation = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = DssTheme.Ink };
        var remove = DssTheme.Quiet("×");
        var line = Row(name, height, elevation, remove);
        var item = (row, name, height, elevation, line);
        _items.Add(item);
        _rows.Children.Add(line);
        height.TextChanged += (_, _) => Recalc();
        remove.Click += (_, _) =>
        {
            _items.Remove(item);
            _rows.Children.Remove(line);
            Recalc();
        };
    }

    private static System.Windows.Controls.Grid Row(UIElement name, UIElement height, UIElement elevation, UIElement? remove)
    {
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var w in new[] { 2.2, 1.2, 1.2, 0.5 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        var cells = new[] { name, height, elevation, remove };
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is not FrameworkElement cell)
                continue;
            cell.Margin = new Thickness(0, 0, 8, 0);
            System.Windows.Controls.Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private void Recalc()
    {
        var at = Parse(_base.Text) ?? 0;
        foreach (var item in _items)
        {
            item.Elevation.Text = at.ToString("+0;-0;0");
            at += Parse(item.Height.Text) ?? 0;
        }
    }

    private bool Collect(out string error)
    {
        error = "";
        Result.Clear();
        if (Parse(_base.Text) is not double baseMm)
        {
            error = "Cao độ level thấp nhất phải là số (mm).";
            return false;
        }

        BaseMm = baseMm;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _items.Count; i++)
        {
            var (row, name, height, _, _) = _items[i];
            var n = name.Text.Trim();
            if (n.Length == 0 || !names.Add(n))
            {
                error = $"Dòng {i + 1}: tên level trống hoặc trùng.";
                return false;
            }

            var h = Parse(height.Text);
            if (i < _items.Count - 1 && h is not > 0)
            {
                error = $"Dòng {i + 1} ({n}): nhập chiều cao tầng > 0.";
                return false;
            }

            Result.Add(new LevelRow { Id = row.Id, Name = n, HeightMm = h });
        }

        if (Result.Count == 0)
        {
            error = "Bảng chưa có tầng nào.";
            return false;
        }

        return true;
    }

    private static double? Parse(string? text) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Round(v) : null;
}
