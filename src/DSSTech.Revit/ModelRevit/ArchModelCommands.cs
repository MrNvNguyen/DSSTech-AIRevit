using System.Text;
using System.Text.Json;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpContracts;
using Dss.McpHost.Create;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Lintels and stiffener columns for the doors and windows already in the model on the plan's level
/// (rules in <see cref="LintelPlanner"/>). Only masonry walls: concrete walls need neither.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ModelLintelCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        if (!ModelRevitLevelContext.TryResolve(doc.ActiveView, doc, out var baseLevel, out var topLevel, out var levelErr))
        {
            TaskDialog.Show("DSSTech — Lanh tô", levelErr);
            return Result.Cancelled;
        }

        if (topLevel.Id == baseLevel.Id)
        {
            TaskDialog.Show("DSSTech — Lanh tô", ModelRevitLevelContext.NoUpperLevelMessage(baseLevel));
            return Result.Cancelled;
        }

        var beams = CreateBatchTool.ListRectangularBeamFamilies(doc);
        var columns = CreateBatchTool.ListRectangularColumnFamilies(doc);
        if (beams.Count == 0 || columns.Count == 0)
        {
            TaskDialog.Show("DSSTech — Lanh tô",
                "Cần family dầm và family cột chữ nhật có tham số b/h trong project (lanh tô dùng dầm, bổ trụ dùng cột).");
            return Result.Cancelled;
        }

        var dlg = new ArchModelSetupWindow("DSSTech — Lanh tô, bổ trụ",
            $"Level {baseLevel.Name} → {topLevel.Name}. Lanh tô: dài B + 2×200, cao 200, rộng bằng tường, đáy ở đầu cửa. " +
            $"Bổ trụ: 200 × bề dày tường hai bên cửa rộng từ {LintelPlanner.StiffenerMinOpeningMm:0}, cao hết tầng.");
        var beamBox = dlg.AddCombo("Family lanh tô (dầm)", beams, Prefer(beams, "lanh", "lintel"));
        var columnBox = dlg.AddCombo("Family bổ trụ (cột)", columns, Prefer(columns, "bo tru", "bổ trụ", "stiff"));
        if (!dlg.ShowFor(uiApp.MainWindowHandle))
            return Result.Cancelled;

        var openings = ArchModelOpenings.Collect(doc, baseLevel, masonryOnly: true, out var skippedConcrete);
        if (openings.Count == 0)
        {
            TaskDialog.Show("DSSTech — Lanh tô",
                $"Không có cửa đi / cửa sổ trên tường xây tại level {baseLevel.Name}. Dựng cửa trước." +
                (skippedConcrete > 0 ? $"\n{skippedConcrete} cửa nằm trên vách nên không cần lanh tô." : ""));
            return Result.Cancelled;
        }

        var ops = LintelPlanner.Plan(
            openings.Select(o => o.Opening).ToList(), baseLevel.Name, topLevel.Name,
            DssTheme.ComboValue(beamBox), DssTheme.ComboValue(columnBox), DssTechLog.Write);
        var batchArgs = JsonSerializer.SerializeToElement(new { dryRun = false, operations = ops });
        var result = CreateBatchTool.Execute(uiApp, batchArgs, McpPermission.ReadCreate);
        if (!result.Ok)
        {
            TaskDialog.Show("DSSTech — Lanh tô", $"{result.ErrorCode}: {result.Message}");
            return Result.Failed;
        }

        var lintels = ops.Count(op => op.Category == "StructuralFraming");
        var summary = new StringBuilder();
        summary.AppendLine($"Cửa trên tường xây: {openings.Count}" + (skippedConcrete > 0 ? $" (bỏ qua {skippedConcrete} cửa trên vách)" : ""));
        summary.AppendLine($"Lanh tô: {lintels} (cửa sát nhau dùng chung một lanh tô; lanh tô dừng ở đầu tường).");
        summary.AppendLine($"Bổ trụ: {ops.Count - lintels} (cửa rộng từ {LintelPlanner.StiffenerMinOpeningMm:0}; bỏ phía sát đầu tường).");
        ArchModelOpenings.AppendResults(summary, result.Data);
        DoneReport.Show("DSSTech — Lanh tô, bổ trụ", summary.ToString());
        return Result.Succeeded;
    }

    private static string? Prefer(IReadOnlyList<string> names, params string[] keys) =>
        names.FirstOrDefault(n => keys.Any(k => n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));
}

/// <summary>
/// Openings in this model's walls of the plan's level for the ducts crossing them and the fire-hose cabinets
/// recessed in them, from this model and linked MEP models (rules in <see cref="WallPenetrationPlanner"/>).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ModelWallOpeningCommand : IExternalCommand
{
    private static readonly string[] CabinetWords =
        { "pccc", "chữa cháy", "chua chay", "fhc", "fire hose", "hose cabinet", "fire cabinet", "hose reel", "tủ cc", "tu cc" };

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        if (!ModelRevitLevelContext.TryResolve(doc.ActiveView, doc, out var baseLevel, out _, out var levelErr))
        {
            TaskDialog.Show("DSSTech — Lỗ mở", levelErr);
            return Result.Cancelled;
        }

        var dlg = new ArchModelSetupWindow("DSSTech — Lỗ mở tường",
            $"Level {baseLevel.Name}. Đục lỗ chữ nhật trên tường của model này tại chỗ ống gió xuyên qua và tại tủ PCCC âm tường " +
            "(ống gió, tủ lấy từ model này và file link MEP). Kích thước làm tròn lên 10 mm.");
        var clearance = dlg.AddInput("Khe hở mỗi phía (mm)", "50");
        var ducts = dlg.AddOption("Ống gió xuyên tường", true);
        var cabinets = dlg.AddOption("Tủ PCCC âm tường", true);
        if (!dlg.ShowFor(uiApp.MainWindowHandle))
            return Result.Cancelled;
        var clearanceMm = double.TryParse(clearance.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var c) && c >= 0 ? c : 50;

        var walls = WallsOn(doc, baseLevel);
        var sources = new List<(Document Doc, Transform ToHost)> { (doc, Transform.Identity) };
        foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            if (link.GetLinkDocument() is { } linkDoc)
                sources.Add((linkDoc, link.GetTotalTransform()));
        }

        var holes = new List<WallHole>();
        var ductCount = 0;
        var cabinetCount = 0;
        if (ducts.IsChecked == true)
        {
            var runs = sources.SelectMany(s => DuctRuns(s.Doc, s.ToHost)).ToList();
            ductCount = runs.Count;
            holes.AddRange(WallPenetrationPlanner.Ducts(walls, runs, clearanceMm));
        }

        if (cabinets.IsChecked == true)
        {
            var found = sources.SelectMany(s => Cabinets(s.Doc, s.ToHost)).ToList();
            cabinetCount = found.Count;
            holes.AddRange(WallPenetrationPlanner.Cabinets(walls, found, clearanceMm));
        }

        var created = 0;
        var existing = 0;
        var failed = 0;
        using (var tx = new Transaction(doc, "DSSTech wall openings"))
        {
            tx.Start();
            foreach (var hole in holes)
            {
                if (doc.GetElement(new ElementId(long.Parse(hole.WallId))) is not Wall { Location: LocationCurve { Curve: Line line } } wall)
                    continue;
                var a = line.GetEndPoint(0);
                var u = line.Direction.Normalize();
                var p1 = new XYZ(a.X, a.Y, 0) + u * UnitConv.MmToInternal(hole.FromMm) + XYZ.BasisZ * UnitConv.MmToInternal(hole.BottomMm);
                var p2 = new XYZ(a.X, a.Y, 0) + u * UnitConv.MmToInternal(hole.ToMm) + XYZ.BasisZ * UnitConv.MmToInternal(hole.TopMm);
                if (HasOpening(doc, wall, p1, p2))
                {
                    existing++;
                    continue;
                }

                try
                {
                    doc.Create.NewOpening(wall, p1, p2);
                    created++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    failed++;
                    DssTechLog.Write($"wall opening {hole.Source} wall={hole.WallId}: {ex.Message}");
                }
            }

            tx.Commit();
        }

        var summary = new StringBuilder();
        summary.AppendLine($"Tường của model tại {baseLevel.Name}: {walls.Count}. Ống gió đọc được: {ductCount}; tủ PCCC: {cabinetCount}.");
        summary.AppendLine($"Lỗ mở: tạo {created}, đã có {existing}" + (failed > 0 ? $", lỗi {failed} (xem log)" : "") + $"; khe hở {clearanceMm:0} mm mỗi phía.");
        summary.AppendLine("Ống chéo tường: lỗ rộng thêm theo góc xiên qua bề dày tường. Ống đứng (qua sàn) và ống chạy dọc tường không đục.");
        summary.AppendLine("Tường trong file link không đục được; tủ PCCC nhận theo tên family/type (PCCC, chữa cháy, FHC, fire hose).");
        DoneReport.Show("DSSTech — Lỗ mở tường", summary.ToString());
        return Result.Succeeded;
    }

    private static List<WallBand> WallsOn(Document doc, Level level)
    {
        var result = new List<WallBand>();
        var rise = UnitConv.MmToInternal(1000);
        foreach (var wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
        {
            if (wall.Location is not LocationCurve { Curve: Line line } || wall.get_BoundingBox(null) is not { } box)
                continue;
            if (wall.LevelId != level.Id && !(box.Min.Z <= level.Elevation + UnitConv.MmToInternal(10) && box.Max.Z >= level.Elevation + rise))
                continue;
            var a = line.GetEndPoint(0);
            var b = line.GetEndPoint(1);
            result.Add(new WallBand
            {
                Id = wall.Id.Value.ToString(),
                X0 = UnitConv.ToMm(a.X), Y0 = UnitConv.ToMm(a.Y), X1 = UnitConv.ToMm(b.X), Y1 = UnitConv.ToMm(b.Y),
                ThicknessMm = UnitConv.ToMm(wall.Width),
                BottomMm = UnitConv.ToMm(box.Min.Z),
                TopMm = UnitConv.ToMm(box.Max.Z)
            });
        }

        return result;
    }

    private static IEnumerable<DuctRun> DuctRuns(Document source, Transform toHost)
    {
        foreach (var e in new FilteredElementCollector(source).OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType())
        {
            if (e.Location is not LocationCurve { Curve: Line line })
                continue;
            var dia = e.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() ?? 0;
            var w = dia > 0 ? dia : e.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0;
            var h = dia > 0 ? dia : e.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0;
            if (w <= 0 || h <= 0)
                continue;
            var a = toHost.OfPoint(line.GetEndPoint(0));
            var b = toHost.OfPoint(line.GetEndPoint(1));
            yield return new DuctRun
            {
                X0 = UnitConv.ToMm(a.X), Y0 = UnitConv.ToMm(a.Y), Z0 = UnitConv.ToMm(a.Z),
                X1 = UnitConv.ToMm(b.X), Y1 = UnitConv.ToMm(b.Y), Z1 = UnitConv.ToMm(b.Z),
                WidthMm = UnitConv.ToMm(w), HeightMm = UnitConv.ToMm(h),
                Source = $"duct {e.Id.Value}"
            };
        }
    }

    private static IEnumerable<(string Source, IReadOnlyList<(double X, double Y, double Z)> Points)> Cabinets(Document source, Transform toHost)
    {
        foreach (var fi in new FilteredElementCollector(source).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
        {
            var name = $"{fi.Symbol?.FamilyName} {fi.Symbol?.Name}";
            if (!CabinetWords.Any(k => name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                continue;
            var points = new List<(double X, double Y, double Z)>();
            void Walk(GeometryElement? g)
            {
                if (g is null)
                    return;
                foreach (var obj in g)
                {
                    if (obj is Solid { Volume: > 1e-9 } solid)
                    {
                        foreach (Edge edge in solid.Edges)
                        {
                            foreach (var p in edge.Tessellate())
                            {
                                var q = toHost.OfPoint(p);
                                points.Add((UnitConv.ToMm(q.X), UnitConv.ToMm(q.Y), UnitConv.ToMm(q.Z)));
                            }
                        }
                    }
                    else if (obj is GeometryInstance gi)
                        Walk(gi.GetInstanceGeometry());
                }
            }

            Walk(fi.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));
            if (points.Count > 0)
                yield return ($"cabinet {fi.Id.Value} {name.Trim()}", points);
        }
    }

    /// <summary>An opening already cut in this wall overlapping the new one (earlier run).</summary>
    private static bool HasOpening(Document doc, Wall wall, XYZ p1, XYZ p2)
    {
        var tol = UnitConv.MmToInternal(5);
        var mid = (p1 + p2) / 2;
        foreach (var opening in new FilteredElementCollector(doc).OfClass(typeof(Opening)).Cast<Opening>())
        {
            if (opening.Host?.Id != wall.Id || !opening.IsRectBoundary)
                continue;
            var r = opening.BoundaryRect;
            if (r.Count < 2)
                continue;
            bool Inside(double v, double lo, double hi) => v >= Math.Min(lo, hi) - tol && v <= Math.Max(lo, hi) + tol;
            if (Inside(mid.X, r[0].X, r[1].X) && Inside(mid.Y, r[0].Y, r[1].Y) && Inside(mid.Z, r[0].Z, r[1].Z))
                return true;
        }

        return false;
    }
}

/// <summary>Doors and windows of a level with their host wall, in model mm.</summary>
internal static class ArchModelOpenings
{
    public sealed record Entry(HostedOpening Opening, bool IsDoor, double SillMm);

    public static List<Entry> Collect(Document doc, Level level, bool masonryOnly, out int skippedConcrete)
    {
        skippedConcrete = 0;
        var result = new List<Entry>();
        foreach (var bic in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })
        {
            foreach (var fi in new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                if (fi.LevelId != level.Id || fi.Location is not LocationPoint lp ||
                    fi.Host is not Wall { Location: LocationCurve { Curve: Line line } } wall)
                    continue;
                if (masonryOnly && wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() == 1)
                {
                    skippedConcrete++;
                    continue;
                }

                var width = Length(fi, bic == BuiltInCategory.OST_Doors ? BuiltInParameter.DOOR_WIDTH : BuiltInParameter.WINDOW_WIDTH);
                var head = fi.get_Parameter(BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM)?.AsDouble() ?? 0;
                var sill = fi.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.AsDouble() ?? 0;
                if (width <= 0 || head <= 0)
                {
                    DssTechLog.Write($"opening {fi.Id.Value}: no width/head height, skipped");
                    continue;
                }

                var dir = new XYZ(line.Direction.X, line.Direction.Y, 0).Normalize();
                var p = lp.Point;
                double At(XYZ q) => (q - p).DotProduct(dir);
                var s0 = At(line.GetEndPoint(0));
                var s1 = At(line.GetEndPoint(1));
                result.Add(new Entry(new HostedOpening
                {
                    Id = fi.Id.Value.ToString(),
                    X = UnitConv.ToMm(p.X),
                    Y = UnitConv.ToMm(p.Y),
                    DirX = dir.X,
                    DirY = dir.Y,
                    WidthMm = UnitConv.ToMm(width),
                    HeadMm = UnitConv.ToMm(head),
                    WallId = wall.Id.Value.ToString(),
                    WallThicknessMm = UnitConv.ToMm(wall.Width),
                    WallStartMm = UnitConv.ToMm(Math.Min(s0, s1)),
                    WallEndMm = UnitConv.ToMm(Math.Max(s0, s1))
                }, bic == BuiltInCategory.OST_Doors, UnitConv.ToMm(sill)));
            }
        }

        return result;
    }

    /// <summary>Opening width: the instance or its type, by built-in width then the usual names.</summary>
    private static double Length(FamilyInstance fi, BuiltInParameter builtIn)
    {
        foreach (var element in new Element[] { fi, fi.Symbol })
        {
            foreach (var p in new[]
                     {
                         element.get_Parameter(builtIn), element.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM),
                         element.LookupParameter("Width"), element.LookupParameter("Rộng"), element.LookupParameter("Rough Width")
                     })
            {
                if (p is { StorageType: StorageType.Double } && p.AsDouble() > 0)
                    return p.AsDouble();
            }
        }

        return 0;
    }

    public static void AppendResults(StringBuilder summary, JsonElement data)
    {
        if (!data.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return;
        foreach (var group in results.EnumerateArray()
                     .Select(r => r.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "")
                     .GroupBy(st => st))
            summary.AppendLine($"  {group.Key}: {group.Count()}");
        foreach (var r in results.EnumerateArray().Where(r =>
                     r.TryGetProperty("status", out var st) && st.GetString() == "failed").Take(10))
            summary.AppendLine($"  lỗi: {(r.TryGetProperty("reason", out var m) ? m.GetString() : r.GetRawText())}");
    }
}

/// <summary>Small setup dialog for the modules that work from the model (no CAD pick).</summary>
internal sealed class ArchModelSetupWindow : DssDialog
{
    private readonly List<System.Windows.UIElement> _rows = new();

    public ArchModelSetupWindow(string title, string subtitle, double height = 420)
        : base(title, subtitle, 520, height)
    {
        PrimaryButton.Content = "Dựng";
        PrimaryButton.Click += (_, _) =>
        {
            if (Validate?.Invoke() is { } problem)
            {
                System.Windows.MessageBox.Show(this, problem, Title);
                return;
            }

            DialogResult = true;
            Close();
        };
    }

    /// <summary>Checked before «Dựng» closes the dialog: a message keeps it open.</summary>
    public Func<string?>? Validate { get; set; }

    /// <summary>A list of ticks with a filter box above it (names containing the typed text stay shown).</summary>
    public List<CheckBox> AddChecklist(string label, IEnumerable<string> items, Func<string, bool> isChecked,
        Func<string, string>? display = null)
    {
        var boxes = items.Select(i => new CheckBox
        {
            Content = display?.Invoke(i) ?? i,
            Tag = i,
            IsChecked = isChecked(i),
            Margin = new System.Windows.Thickness(4, 2, 4, 2),
            Foreground = DssTheme.Ink
        }).ToList();
        var list = new System.Windows.Controls.StackPanel();
        foreach (var box in boxes)
            list.Children.Add(box);
        var filter = DssTheme.Input("");
        filter.ToolTip = "Lọc theo tên";
        filter.TextChanged += (_, _) =>
        {
            var text = filter.Text.Trim();
            foreach (var box in boxes)
                box.Visibility = text.Length == 0 || ((string)box.Tag).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
        };
        var all = DssTheme.Quiet("Chọn hết (đang hiện)");
        all.Click += (_, _) => boxes.Where(b => b.Visibility == System.Windows.Visibility.Visible).ToList().ForEach(b => b.IsChecked = true);
        var none = DssTheme.Quiet("Bỏ chọn hết");
        none.Click += (_, _) => boxes.ForEach(b => b.IsChecked = false);
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new System.Windows.Thickness(0, 4, 0, 4)
        };
        all.Margin = new System.Windows.Thickness(0, 0, 8, 0);
        buttons.Children.Add(all);
        buttons.Children.Add(none);
        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(filter);
        panel.Children.Add(buttons);
        panel.Children.Add(new System.Windows.Controls.Border
        {
            BorderBrush = DssTheme.Line,
            BorderThickness = new System.Windows.Thickness(1),
            Child = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 220,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Content = list
            }
        });
        _rows.Add(DssTheme.Field(label, panel));
        return boxes;
    }

    public ComboBox AddCombo(string label, IEnumerable<string> items, string? selected)
    {
        var box = DssTheme.Combo(items, selected);
        _rows.Add(DssTheme.Field(label, box));
        return box;
    }

    public System.Windows.Controls.TextBox AddInput(string label, string text)
    {
        var box = DssTheme.Input(text);
        _rows.Add(DssTheme.Field(label, box));
        return box;
    }

    public CheckBox AddOption(string text, bool isChecked)
    {
        var box = DssTheme.Option(text, isChecked);
        _rows.Add(box);
        return box;
    }

    public bool ShowFor(IntPtr owner)
    {
        AddSection(1, "Thiết lập", _rows.ToArray());
        SetOwner(owner);
        return ShowDialog() == true;
    }
}
