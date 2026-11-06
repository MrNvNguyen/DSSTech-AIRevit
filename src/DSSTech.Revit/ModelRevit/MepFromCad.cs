using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using DSSTech.Revit.Cad;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.ModelRevit;

/// <summary>What one discipline button can build.</summary>
internal enum MepRun
{
    Duct,
    PressurePipe,
    DrainPipe,
    CableTray
}

internal sealed record MepChoice(MepRun Run, string Label, string Detail, string? Group);

// Each discipline is a drop-down on the ribbon; each item builds one kind of run.

/// <summary>«ĐHKK ▸ Ống gió»: supply, return, fresh air, exhaust ducts.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepDuctCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — ĐHKK",
            new MepChoice(MepRun.Duct, "Ống gió", "Cấp, hồi, gió tươi, thải… (Duct)", MepSystemCatalog.Mech));
}

/// <summary>«ĐHKK ▸ Ống nước ĐHKK»: chilled, cooling water and refrigerant pipes.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepMechPipeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — ĐHKK",
            new MepChoice(MepRun.PressurePipe, "Ống nước ĐHKK", "Nước Chiller, tháp giải nhiệt, gas (Pipe)", MepSystemCatalog.Mech));
}

/// <summary>«ĐHKK ▸ Thoát nước ngưng»: condensate drains falling to their outlet.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepCondensateCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — ĐHKK",
            new MepChoice(MepRun.DrainPipe, "Thoát nước ngưng", "Ống có độ dốc về điểm xả (Pipe)", MepSystemCatalog.Mech));
}

/// <summary>«Điện ▸ Máng / thang cáp»: cable trays, with the system code as Service Type.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepElectricalCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — Điện",
            new MepChoice(MepRun.CableTray, "Máng / thang cáp", "Busway, trung thế, hạ thế, điện nhẹ, báo cháy (Cable Tray)", MepSystemCatalog.Elec));
}

/// <summary>«Nước ▸ Cấp nước»: water supply under the structure.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepPlumbingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — Nước",
            new MepChoice(MepRun.PressurePipe, "Cấp nước", "Nước lạnh, nóng, hồi nóng (Pipe, dưới đáy dầm/sàn)", MepSystemCatalog.Plumb));
}

/// <summary>«Nước ▸ Thoát nước»: drainage with fall to its outlet.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepDrainCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — Nước",
            new MepChoice(MepRun.DrainPipe, "Thoát nước", "Thải đen, xám, bếp, mưa, thông hơi (Pipe, độ dốc về điểm xả)", MepSystemCatalog.Plumb));
}

/// <summary>«PCCC ▸ Sprinkler / chữa cháy»: sprinkler and hydrant pipes.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepFireCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — PCCC",
            new MepChoice(MepRun.PressurePipe, "Sprinkler / chữa cháy", "Sprinkler, vách tường, màn ngăn (Pipe)", MepSystemCatalog.Fire));
}

/// <summary>«PCCC ▸ Tăng áp / hút khói»: pressurisation and smoke exhaust ducts.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepSmokeDuctCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — PCCC",
            new MepChoice(MepRun.Duct, "Tăng áp / hút khói", "Ống gió tạo áp, hút khói (Duct)", null));
}

/// <summary>
/// MEP runs from single-line CAD: the system's layers are ticked in the dialog (a consultant's drawing holds many
/// systems), sizes come from the labels beside the lines (<see cref="MepRoutePlanner"/>), the system from the
/// systems set up in the model. Pressure runs sit under the beam or slab above them and drop under lower beams
/// (or at a typed height); drainage falls from its heads to the outlet, which joins the pipe already there.
/// </summary>
internal static class MepFromCad
{
    private const double StepMm = 100;
    private const double OverheadCellMm = 3000;
    private const string TopOf = "Đỉnh ống";
    private const string CenterOf = "Tâm ống";
    private const string BottomOf = "Đáy ống";

    public static Result Run(UIApplication uiApp, string title, MepChoice choice)
    {
        var uidoc = uiApp.ActiveUIDocument;
        var doc = uidoc?.Document;
        var view = doc?.ActiveView;
        if (doc is null || uidoc is null || view is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        if (!ModelRevitLevelContext.TryResolve(view, doc, out var level, out var topLevel, out var levelErr))
        {
            TaskDialog.Show(title, levelErr);
            return Result.Cancelled;
        }

        try
        {
            var import = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<ImportInstance>().FirstOrDefault();
            if (import is null)
            {
                var picked = uidoc.Selection.PickObject(ObjectType.Element, new ImportInstanceSelectionFilter(),
                    $"Chọn CAD link có hệ {choice.Label.ToLowerInvariant()}.");
                import = doc.GetElement(picked.ElementId) as ImportInstance;
            }

            if (import is null)
                return Result.Cancelled;
            CadLinkLayerVisibility.EnsureAllImportLayersVisible(view, import);
            var geom = ImportInstanceGeometryReader.Read(import, view);
            var linkName = import.Category?.Name ?? import.Name ?? "";
            // Layers holding lines, with how many: the drawing's systems to tick.
            var layers = geom.Entities.Where(e => e is CadLine or CadPolyline)
                .GroupBy(e => e.Layer, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Count: g.Count()))
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (layers.Count == 0)
            {
                TaskDialog.Show(title, "Không thấy nét nào trong CAD link.");
                return Result.Cancelled;
            }

            var setup = Setup.Ask(uiApp, doc, title, choice, linkName, layers, level, topLevel);
            if (setup is null)
                return Result.Cancelled;

            IEnumerable<CadEntity> lines = geom.Entities.Where(e => e is not CadText && setup.Layers.Contains(e.Layer));
            // Ducts: grilles, diffusers, fan coils and boxes drawn on the duct layers are placed by their own tool.
            var devices = 0;
            Dump(lines, geom.Entities.OfType<CadText>());
            if (choice.Run == MepRun.Duct)
                lines = MepRoutePlanner.WithoutDevices(lines, out devices);
            // Ducts are drawn with both sides; pipes and trays with single lines.
            var net = MepRoutePlanner.Build(lines, null, setup.Fallback, geom.Entities.OfType<CadText>(), choice.Run == MepRun.Duct);
            var layerLabel = setup.Layers.Count <= 3 ? "layer " + string.Join(", ", setup.Layers) : $"{setup.Layers.Count} layer";
            if (net.Edges.Count == 0)
            {
                TaskDialog.Show(title, $"Không thấy tuyến nào trên {layerLabel}.");
                return Result.Cancelled;
            }

            // Drainage: the outlets are picked on the plan (Esc when done); a network with none uses its largest end.
            var outlets = new List<XYZ>();
            if (choice.Run == MepRun.DrainPipe)
            {
                TaskDialog.Show(title,
                    "Pick điểm xả (cuối tuyến, chỗ nối vào ống đứng / ống có sẵn) cho từng mạng thoát, Esc khi xong.\n" +
                    "Mạng không pick: điểm xả là đầu hở của đoạn ống lớn nhất.");
                while (true)
                {
                    try
                    {
                        outlets.Add(uidoc.Selection.PickPoint("Pick điểm xả của mạng thoát (Esc khi xong)."));
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        break;
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        break;
                    }
                }
            }

            var report = Build(doc, net, level, topLevel, setup, outlets);
            var sources = string.Join(", ", net.Edges.GroupBy(e => e.SizeFrom).Select(g => g.Key switch
            {
                "text" => $"chữ cạnh nét {g.Count()}",
                "layer" => $"tên layer {g.Count()}",
                "inherited" => $"nối theo đoạn trước {g.Count()}",
                "drawn" => $"bề rộng 2 nét vẽ {g.Count()}",
                _ => $"size mặc định {g.Count()}"
            }));
            var twoLine = net.Edges.Count(e => e.DrawnWidthMm > 0);
            if (twoLine > 0)
                sources += $" (ống vẽ 2 nét: dựng theo tim, {twoLine} đoạn)";
            if (net.FlexSkipped > 0)
                report = $"Bỏ qua {net.FlexSkipped} đoạn / layer ống mềm: nối sau bằng «Nối ống mềm».\n" + report;
            if (devices > 0)
                report = $"Bỏ qua {devices} thiết bị vẽ trên layer ống (block / khung miệng gió, FCU, box): đặt bằng «Thiết bị ĐHKK», nối bằng «Nối ống mềm».\n" + report;
            TaskDialog.Show(title,
                $"{choice.Label} · CAD {linkName} · {layerLabel} · {setup.SystemLabel}\n" +
                $"Size: {sources}.\n{report}");
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
    }

    /// <summary>
    /// The strokes and labels the run was built from, as read from the link (lines, arcs, polylines, texts), to
    /// %LOCALAPPDATA%\DSSTech\mep-cad-input.txt: what to send back when a run comes out wrong.
    /// </summary>
    private static void Dump(IEnumerable<CadEntity> lines, IEnumerable<CadText> texts)
    {
        try
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            string N(double v) => v.ToString("0.0", ci);
            string Points(CadPolyline p) => string.Join("\t", p.Points.Select(q => $"{N(q.X)}\t{N(q.Y)}"));
            foreach (var e in lines)
            {
                if (e is CadLine l)
                    sb.Append($"L\t{e.Layer}\t{e.BlockRef}\t{N(l.X0)}\t{N(l.Y0)}\t{N(l.X1)}\t{N(l.Y1)}\n");
                else if (e is CadPolyline { FromArc: true } arc)
                    sb.Append($"A\t{e.Layer}\t{e.BlockRef}\t{Points(arc)}\n");
                else if (e is CadPolyline { Bulges: { } bulges } round)
                    sb.Append($"Q\t{e.Layer}\t{e.BlockRef}\t{(round.Closed ? 1 : 0)}\t{string.Join("\t", round.Points.Select((q, i) => $"{N(q.X)}\t{N(q.Y)}\t{bulges[i].ToString("0.######", ci)}"))}\n");
                else if (e is CadPolyline poly)
                    sb.Append($"P\t{e.Layer}\t{e.BlockRef}\t{(poly.Closed ? 1 : 0)}\t{Points(poly)}\n");
            }

            foreach (var t in texts)
                sb.Append($"T\t{t.Layer}\t{t.Value.Replace('\t', ' ').Replace('\n', ' ')}\t{N(t.CenterX ?? t.X)}\t{N(t.CenterY ?? t.Y)}\n");
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSSTech", "mep-cad-input.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            DssTechLog.Write($"mep dump: {ex.Message}");
        }
    }

    /// <summary>Everything the dialog decides.</summary>
    private sealed class Setup
    {
        public MepRun Run { get; init; }
        public HashSet<string> Layers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public ElementId SystemId { get; init; } = ElementId.InvalidElementId;
        public string SystemLabel { get; init; } = "";
        public string ServiceType { get; init; } = "";
        public ElementId RectTypeId { get; init; } = ElementId.InvalidElementId;
        public ElementId RoundTypeId { get; init; } = ElementId.InvalidElementId;
        public MepSize Fallback { get; init; }
        public List<ModelRevitWallFit.Piece> Overhead { get; init; } = new();
        public double ClearanceMm { get; init; }
        public double ManualMm { get; init; }
        public string ManualRef { get; init; } = TopOf;
        public double SlopePercent { get; init; }

        public bool UnderStructure => Overhead.Count > 0;

        public double ManualCenter(double levelMm, double depthMm) => levelMm + ManualMm + ManualRef switch
        {
            TopOf => -depthMm / 2,
            BottomOf => depthMm / 2,
            _ => 0
        };

        public static Setup? Ask(UIApplication uiApp, Document doc, string title, MepChoice choice, string linkName,
            List<(string Name, int Count)> layers, Level level, Level topLevel)
        {
            var overhead = ModelRevitWallFit.Overhead(doc);
            var domain = choice.Run switch
            {
                MepRun.Duct => MepDomain.Duct,
                MepRun.CableTray => MepDomain.CableTray,
                _ => MepDomain.Pipe
            };
            var spec = MepSystemCatalog.FromLayer(linkName, domain);
            var dlg = new ArchModelSetupWindow(title,
                $"{choice.Label} từ CAD link {linkName}. Tick layer của hệ cần dựng; tuyến từ nét single line, size từ chữ ghi cạnh nét, " +
                $"không có thì tên layer, không có nữa thì nối theo đoạn trước. Level {level.Name}." +
                (overhead.Count == 0 ? " Chưa có dầm/sàn kết cấu: dùng cao độ tự nhập." : ""), 760);

            // Systems as set up in the model (or the colour rules' codes for trays, written as Service Type).
            List<(ElementId Id, string Name, string Abbr)> systems;
            if (domain == MepDomain.Duct)
                systems = new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType)).Cast<MEPSystemType>()
                    .Select(s => (s.Id, s.Name, Abbr(s))).OrderBy(s => s.Name).ToList();
            else if (domain == MepDomain.Pipe)
                systems = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<MEPSystemType>()
                    .Select(s => (s.Id, s.Name, Abbr(s))).OrderBy(s => s.Name).ToList();
            else
                systems = MepSystemCatalog.All.Where(s => s.Domain == MepDomain.CableTray)
                    .Select(s => (ElementId.InvalidElementId, $"{s.Abbr} - {s.NameVi}", s.Abbr)).ToList();
            if (systems.Count == 0)
            {
                TaskDialog.Show(title, "Project chưa có system cho loại này. Chạy «Hệ thống» để tạo hệ theo quy định trước.");
                return null;
            }

            // The group's systems first (PCCC shows fire systems first), the one named by the link preselected.
            var groupCodes = MepSystemCatalog.All.Where(s => s.Group == choice.Group).Select(s => s.Abbr).ToHashSet();
            systems = systems.OrderByDescending(s => groupCodes.Contains(s.Abbr)).ThenBy(s => s.Name).ToList();
            var preset = spec is null ? null : systems.Select(s => (string?)s.Name).FirstOrDefault(n =>
                MepSystemCatalog.Match(spec, new[] { new ExistingSystem { Name = n!, Abbreviation = systems.First(x => x.Name == n).Abbr, Domain = domain } })
                    .Kind != SystemMatchKind.None);
            var systemBox = dlg.AddCombo(domain == MepDomain.CableTray ? "Hệ (ghi vào Service Type)" : "System", systems.Select(s => s.Name), preset ?? systems[0].Name);

            // The drawing's layers: those naming the chosen system's code ticked until the list is touched by hand.
            string CodeOf() => systems.FirstOrDefault(s => s.Name == DssTheme.ComboValue(systemBox)).Abbr ?? "";
            var counts = layers.ToDictionary(l => l.Name, l => l.Count, StringComparer.OrdinalIgnoreCase);
            var layerBoxes = dlg.AddChecklist("Layer của hệ (CAD có nhiều hệ: chỉ tick layer cần dựng)", layers.Select(l => l.Name),
                l => NamesCode(l, CodeOf()), l => $"{l}   ({counts[l]} nét)");
            var applying = false;
            var touched = false;
            foreach (var box in layerBoxes)
            {
                box.Checked += (_, _) => touched |= !applying;
                box.Unchecked += (_, _) => touched |= !applying;
            }

            systemBox.SelectionChanged += (_, _) =>
            {
                if (touched)
                    return;
                applying = true;
                var code = CodeOf();
                foreach (var box in layerBoxes)
                    box.IsChecked = NamesCode((string)box.Tag, code);
                applying = false;
            };
            dlg.Validate = () => layerBoxes.Any(b => b.IsChecked == true) ? null : "Tick ít nhất một layer của hệ cần dựng.";

            List<(ElementId Id, string Name)> rect, round;
            if (domain == MepDomain.Duct)
            {
                var all = new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>().ToList();
                rect = all.Where(t => t.Shape == ConnectorProfileType.Rectangular).Select(t => (t.Id, t.Name)).ToList();
                round = all.Where(t => t.Shape == ConnectorProfileType.Round).Select(t => (t.Id, t.Name)).ToList();
            }
            else if (domain == MepDomain.Pipe)
            {
                rect = new List<(ElementId, string)>();
                round = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().Select(t => (t.Id, t.Name)).ToList();
            }
            else
            {
                rect = new FilteredElementCollector(doc).OfClass(typeof(CableTrayType)).Cast<CableTrayType>().Select(t => (t.Id, t.Name)).ToList();
                round = new List<(ElementId, string)>();
            }

            if (rect.Count + round.Count == 0)
            {
                TaskDialog.Show(title, "Project chưa có type ống / máng cho loại này.");
                return null;
            }

            System.Windows.Controls.ComboBox? rectBox = null, roundBox = null;
            if (rect.Count > 0)
                rectBox = dlg.AddCombo(domain == MepDomain.CableTray ? "Type máng" : "Type chữ nhật", rect.Select(t => t.Name), rect[0].Name);
            if (round.Count > 0)
                roundBox = dlg.AddCombo(domain == MepDomain.Pipe ? "Type ống" : "Type tròn", round.Select(t => t.Name), round[0].Name);
            var fallback = dlg.AddInput("Size khi không đọc được (vd 400x300, D100)",
                domain == MepDomain.Pipe ? "D50" : domain == MepDomain.CableTray ? "300x100" : "400x300");

            System.Windows.Controls.TextBox? slope = null;
            if (choice.Run == MepRun.DrainPipe)
                slope = dlg.AddInput("Độ dốc (%) — chảy về điểm xả", "1");
            var underStructure = dlg.AddOption(choice.Run switch
            {
                MepRun.DrainPipe => "Đầu tuyến cao nhất sát đáy dầm/sàn kết cấu (điểm xả tính lùi theo độ dốc)",
                MepRun.Duct => "So với đáy dầm/sàn: đáy dầm thấp hơn cao độ tự nhập thì cả mạng hạ xuống dưới dầm (một cao độ)",
                _ => "Cao độ theo đáy dầm/sàn kết cấu, uốn né dầm"
            }, overhead.Count > 0);
            var clearance = dlg.AddInput("Khe hở dưới đáy dầm/sàn (mm)", "50");
            var storeyMm = topLevel.Id == level.Id ? 3300 : Math.Round(UnitConv.ToMm(topLevel.Elevation - level.Elevation));
            var reference = dlg.AddCombo(choice.Run == MepRun.DrainPipe ? "Cao độ tự nhập tại điểm xả theo" : "Cao độ tự nhập theo",
                new[] { TopOf, CenterOf, BottomOf }, choice.Run == MepRun.DrainPipe ? BottomOf : TopOf);
            var manual = dlg.AddInput("Cao độ tự nhập so với level (mm)", (storeyMm - 500).ToString("0"));
            if (!dlg.ShowFor(uiApp.MainWindowHandle))
                return null;

            double Num(System.Windows.Controls.TextBox? box, double fallbackValue) =>
                double.TryParse((box?.Text ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallbackValue;
            var system = systems.First(s => s.Name == DssTheme.ComboValue(systemBox));
            return new Setup
            {
                Run = choice.Run,
                Layers = new HashSet<string>(layerBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag), StringComparer.OrdinalIgnoreCase),
                SystemId = system.Id,
                SystemLabel = domain == MepDomain.CableTray ? $"Service Type {system.Abbr}" : $"System {system.Name}",
                ServiceType = system.Abbr,
                RectTypeId = rectBox is null ? ElementId.InvalidElementId : rect.First(t => t.Name == DssTheme.ComboValue(rectBox)).Id,
                RoundTypeId = roundBox is null ? ElementId.InvalidElementId : round.First(t => t.Name == DssTheme.ComboValue(roundBox)).Id,
                Fallback = MepRoutePlanner.TryParseSize(fallback.Text, out var fs) ? fs : new MepSize(400, 300, 0),
                Overhead = underStructure.IsChecked == true ? overhead : new List<ModelRevitWallFit.Piece>(),
                ClearanceMm = Math.Max(0, Num(clearance, 50)),
                ManualMm = Num(manual, storeyMm - 500),
                ManualRef = DssTheme.ComboValue(reference),
                SlopePercent = Math.Max(0, Num(slope, 1))
            };
        }

        private static string Abbr(MEPSystemType s) =>
            s.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.AsString() ?? "";

        /// <summary>The layer name holds the system code as a word of its own (M-SAD-600x400, SAD_DUCT; not «SADDLE»).</summary>
        private static bool NamesCode(string layer, string code) =>
            code.Length > 0 && Regex.IsMatch(layer, $"(?<![A-Za-z0-9]){Regex.Escape(code)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
    }

    /// <summary>One piece of one edge: from/to along the edge, centerline elevation at each end (absolute mm).</summary>
    private readonly record struct Piece(double From, double To, double Z0, double Z1);

    private static string Build(Document doc, MepNetwork net, Level level, Level topLevel, Setup setup, List<XYZ> outletPicks)
    {
        var levelMm = UnitConv.ToMm(level.ProjectElevation);
        var fromZ = level.ProjectElevation + UnitConv.MmToInternal(500);
        var toZ = (topLevel.Id == level.Id ? level.ProjectElevation + UnitConv.MmToInternal(6000) : topLevel.ProjectElevation) + UnitConv.MmToInternal(1500);
        // Beams and slabs by plan cell: each probe tests only those over it (a probe every 100 mm along every run).
        var overheadCells = new Dictionary<(long, long), List<ModelRevitWallFit.Piece>>();
        var overheadEverywhere = new List<ModelRevitWallFit.Piece>();
        var cell = UnitConv.MmToInternal(OverheadCellMm);
        foreach (var piece in setup.Overhead)
        {
            long x0 = (long)Math.Floor(piece.Box.Min.X / cell), x1 = (long)Math.Floor(piece.Box.Max.X / cell);
            long y0 = (long)Math.Floor(piece.Box.Min.Y / cell), y1 = (long)Math.Floor(piece.Box.Max.Y / cell);
            if ((x1 - x0 + 1.0) * (y1 - y0 + 1.0) > 4096)
            {
                overheadEverywhere.Add(piece);
                continue;
            }

            for (var ix = x0; ix <= x1; ix++)
            for (var iy = y0; iy <= y1; iy++)
            {
                if (!overheadCells.TryGetValue((ix, iy), out var list))
                    overheadCells[(ix, iy)] = list = new List<ModelRevitWallFit.Piece>();
                list.Add(piece);
            }
        }

        double? Under(double xMm, double yMm)
        {
            var x = UnitConv.MmToInternal(xMm);
            var y = UnitConv.MmToInternal(yMm);
            var near = overheadCells.TryGetValue(((long)Math.Floor(x / cell), (long)Math.Floor(y / cell)), out var list)
                ? overheadEverywhere.Count == 0 ? list : list.Concat(overheadEverywhere).ToList()
                : overheadEverywhere;
            return ModelRevitWallFit.Underside(near, x, y, fromZ, toZ) is double u ? UnitConv.ToMm(u) : null;
        }

        var parts = MepRoutePlanner.Parts(net);
        var partOf = new Dictionary<int, int>();
        for (var p = 0; p < parts.Count; p++)
            foreach (var n in parts[p])
                partOf[n] = p;
        var depth = parts.Select(_ => 0.0).ToList();
        foreach (var e in net.Edges)
            depth[partOf[e.A]] = Math.Max(depth[partOf[e.A]], e.Size.DepthMm);
        var edgesAt = net.Nodes.Select(_ => new List<int>()).ToList();
        for (var i = 0; i < net.Edges.Count; i++)
        {
            edgesAt[net.Edges[i].A].Add(i);
            edgesAt[net.Edges[i].B].Add(i);
        }

        var pieces = new List<List<Piece>>();
        var notes = new StringBuilder();
        var noStructure = 0;
        var outletNodes = new List<int>();
        if (setup.Run == MepRun.DrainPipe)
        {
            // Each network falls toward its outlet: the picked point nearest to one of its open ends, else its main end.
            var slope = setup.SlopePercent / 100;
            var nodeZ = new Dictionary<int, double>();
            for (var p = 0; p < parts.Count; p++)
            {
                var ends = parts[p].Where(n => edgesAt[n].Count == 1).ToList();
                var picked = outletPicks
                    .SelectMany(pt => (ends.Count > 0 ? ends : parts[p]).Select(n => (Node: n, D: PlanDistance(net, n, pt))))
                    .Where(x => x.D <= 1500)
                    .OrderBy(x => x.D)
                    .Select(x => (int?)x.Node)
                    .FirstOrDefault();
                var outlet = picked ?? MepRoutePlanner.DefaultOutlet(net, parts[p]);
                outletNodes.Add(outlet);
                var outletDepth = net.Edges[edgesAt[outlet][0]].Size.DepthMm;
                var manualCenter = setup.ManualCenter(levelMm, outletDepth);
                var outletZ = setup.UnderStructure
                    ? MepRoutePlanner.OutletUnderStructure(net, outlet, slope, Under, setup.ClearanceMm, StepMm, manualCenter)
                    : manualCenter;
                foreach (var (n, d) in MepRoutePlanner.DistancesFrom(net, outlet))
                    nodeZ[n] = outletZ + slope * d;
                notes.AppendLine($"Mạng {p + 1}: điểm xả {(picked is null ? "đầu ống lớn nhất" : "đã pick")} ({net.Nodes[outlet].X:0},{net.Nodes[outlet].Y:0}), tâm ống {outletZ - levelMm:0} mm so với {level.Name}, dốc {setup.SlopePercent:0.##}%.");
            }

            foreach (var e in net.Edges)
                pieces.Add(new List<Piece> { new(0, MepRoutePlanner.Length(net, e), nodeZ[e.A], nodeZ[e.B]) });
        }
        else if (setup.Run == MepRun.Duct)
        {
            // One height per network, no drops: the typed height, or under the lowest beam or slab bottom over the
            // network (less the clearance) where that comes lower.
            var lowest = parts.Select(_ => double.MaxValue).ToArray();
            if (setup.UnderStructure)
            {
                foreach (var e in net.Edges)
                {
                    var (ax, ay) = net.Nodes[e.A];
                    var (bx, by) = net.Nodes[e.B];
                    var len = MepRoutePlanner.Length(net, e);
                    var p = partOf[e.A];
                    for (var s = 0.0; ; s += StepMm)
                    {
                        var t = len > 0 ? Math.Min(s, len) / len : 0;
                        if (Under(ax + (bx - ax) * t, ay + (by - ay) * t) is double u)
                            lowest[p] = Math.Min(lowest[p], u);
                        else
                            noStructure++;
                        if (s >= len)
                            break;
                    }
                }
            }

            var byStructure = 0;
            var partZ = new double[parts.Count];
            for (var p = 0; p < parts.Count; p++)
            {
                // The typed height as typed (bottom 3300 is 3300 above the level); under a beam, down to 5 mm steps
                // from the level.
                partZ[p] = setup.ManualCenter(levelMm, depth[p]);
                if (lowest[p] == double.MaxValue)
                    continue;
                var under = levelMm + Math.Floor((lowest[p] - setup.ClearanceMm - depth[p] / 2 - levelMm) / 5) * 5;
                if (under >= partZ[p])
                    continue;
                partZ[p] = under;
                byStructure++;
            }

            foreach (var e in net.Edges)
                pieces.Add(new List<Piece> { new(0, MepRoutePlanner.Length(net, e), partZ[partOf[e.A]], partZ[partOf[e.A]]) });
            notes.AppendLine(setup.UnderStructure
                ? $"Cao độ: mỗi mạng một cao độ, {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm so với {level.Name}; mạng có đáy dầm/sàn thấp hơn thì đỉnh ống dưới đáy dầm thấp nhất {setup.ClearanceMm:0} mm ({byStructure}/{parts.Count} mạng hạ theo dầm)."
                : $"Cao độ: {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm so với {level.Name} (cả mạng một cao độ).");
            if (noStructure > 0)
                notes.AppendLine($"  {noStructure} điểm dò (mỗi {StepMm:0} mm) không thấy dầm/sàn phía trên.");
        }
        else
        {
            var profiles = new List<List<MepPiece>>();
            foreach (var e in net.Edges)
            {
                var (ax, ay) = net.Nodes[e.A];
                var (bx, by) = net.Nodes[e.B];
                var len = MepRoutePlanner.Length(net, e);
                var netDepth = depth[partOf[e.A]];
                var manualCenter = levelMm + Math.Floor((setup.ManualCenter(levelMm, netDepth) - levelMm) / 5) * 5;
                if (!setup.UnderStructure)
                {
                    profiles.Add(new List<MepPiece> { new(0, len, manualCenter) });
                    continue;
                }

                // Where no beam or slab is above, the typed height stands in for the underside.
                var standIn = manualCenter + netDepth / 2 + setup.ClearanceMm;
                var samples = new List<double>();
                for (var s = 0.0; ; s += StepMm)
                {
                    var t = Math.Min(s, len) / len;
                    var under = Under(ax + (bx - ax) * t, ay + (by - ay) * t);
                    if (under is null)
                        noStructure++;
                    samples.Add(under ?? standIn);
                    if (s >= len)
                        break;
                }

                profiles.Add(MepRoutePlanner.Profile(samples, StepMm, len, netDepth, setup.ClearanceMm));
            }

            // A node sits at the lowest end of the runs meeting there.
            for (var n = 0; n < net.Nodes.Count; n++)
            {
                var ends = new List<(int Edge, bool AtStart)>();
                foreach (var i in edgesAt[n])
                {
                    if (net.Edges[i].A == n)
                        ends.Add((i, true));
                    if (net.Edges[i].B == n)
                        ends.Add((i, false));
                }

                if (ends.Count < 2)
                    continue;
                var z = ends.Min(x => x.AtStart ? profiles[x.Edge][0].Z : profiles[x.Edge][profiles[x.Edge].Count - 1].Z);
                foreach (var (edge, atStart) in ends)
                    profiles[edge] = MepRoutePlanner.SetEnd(profiles[edge], atStart, z);
            }

            pieces.AddRange(profiles.Select(pr => pr.Select(x => new Piece(x.From, x.To, x.Z, x.Z)).ToList()));
            notes.AppendLine(setup.UnderStructure
                ? $"Cao độ: đỉnh (theo đoạn sâu nhất của mạng) dưới đáy dầm/sàn − {setup.ClearanceMm:0} mm, uốn né dầm; chỗ không có dầm/sàn: {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm."
                : $"Cao độ: {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm so với {level.Name} (cả mạng một cao độ).");
            if (noStructure > 0)
                notes.AppendLine($"  {noStructure} điểm dò (mỗi {StepMm:0} mm) không thấy dầm/sàn phía trên: dùng cao độ tự nhập.");
        }

        int curves = 0, risers = 0, fittings = 0, failed = 0, skipped = 0, joined = 0;
        var failures = new List<string>();
        // Runs first, in one transaction; fittings after, each batch on its own so a fitting Revit refuses (one that
        // would flip a short piece end for end) is dropped alone instead of rolling the whole command back.
        using var group = new TransactionGroup(doc, "DSSTech MEP from CAD");
        group.Start();
        using var tx = new Transaction(doc, "DSSTech MEP runs");
        tx.Start();
        var jobs = new List<FittingJob>();
        var before = new FilteredElementCollector(doc).OfClass(typeof(MEPCurve)).Cast<MEPCurve>()
            .Where(c => c.Location is LocationCurve)
            .ToList();
        var reach = UnitConv.MmToInternal(30);
        var beforeIds = before.Select(c => c.Id).ToList();
        // Runs already modelled: only of the kind being made (a pipe or a flex crossing under a duct is not it).
        bool SameKind(MEPCurve c) => setup.Run switch
        {
            MepRun.Duct => c is Duct,
            MepRun.CableTray => c is CableTray,
            _ => c is Pipe
        };
        var existing = before.Where(SameKind).Select(c => ((LocationCurve)c.Location).Curve)
            .Select(c => (Curve: c, MinX: Math.Min(c.GetEndPoint(0).X, c.GetEndPoint(1).X) - reach,
                MaxX: Math.Max(c.GetEndPoint(0).X, c.GetEndPoint(1).X) + reach,
                MinY: Math.Min(c.GetEndPoint(0).Y, c.GetEndPoint(1).Y) - reach,
                MaxY: Math.Max(c.GetEndPoint(0).Y, c.GetEndPoint(1).Y) + reach))
            .ToList();
        var nodeEnds = net.Nodes.Select(_ => new List<(ElementId Curve, XYZ At)>()).ToList();
        var transitions = new List<MepEdge>();
        Merged.Clear();
        JunctionsLogged.Clear();
        Bends.Clear();
        Bends.AddRange(net.Bends.Select(b => (UnitConv.MmToInternal(b.X), UnitConv.MmToInternal(b.Y), UnitConv.MmToInternal(b.RadiusMm))));
        BendLogged = false;
        for (var i = 0; i < net.Edges.Count; i++)
        {
            var e = net.Edges[i];
            if (e.IsTransition)
            {
                transitions.Add(e);
                continue;
            }

            var (ax, ay) = net.Nodes[e.A];
            var (bx, by) = net.Nodes[e.B];
            var len = MepRoutePlanner.Length(net, e);
            XYZ At(double s, double zMm) => new(
                UnitConv.MmToInternal(ax + (bx - ax) * s / len), UnitConv.MmToInternal(ay + (by - ay) * s / len), UnitConv.MmToInternal(zMm));
            var first = pieces[i][0];
            var mid = At(len / 2, (first.Z0 + first.Z1) / 2);
            var along = new XYZ(bx - ax, by - ay, 0).Normalize();
            if (existing.Any(c => mid.X >= c.MinX && mid.X <= c.MaxX && mid.Y >= c.MinY && mid.Y <= c.MaxY && PlanNear(c.Curve, mid) && Parallel(c.Curve, along)))
            {
                skipped++;
                continue;
            }

            MEPCurve? previous = null;
            var list = pieces[i];
            for (var k = 0; k < list.Count; k++)
            {
                var p = list[k];
                var run = Make(doc, setup, level, At(p.From, p.Z0), At(p.To, p.Z1), e.Size);
                if (run is null)
                {
                    failed++;
                    continue;
                }

                curves++;
                if (k == 0)
                    nodeEnds[e.A].Add((run.Id, At(p.From, p.Z0)));
                if (k == list.Count - 1)
                    nodeEnds[e.B].Add((run.Id, At(p.To, p.Z1)));
                if (previous is not null)
                {
                    // Drop under (or rise over) the beam: a vertical piece and an elbow at each end.
                    var top = At(p.From, list[k - 1].Z1);
                    var bottom = At(p.From, p.Z0);
                    var riser = Make(doc, setup, level, top, bottom, e.Size);
                    if (riser is not null)
                    {
                        risers++;
                        // By id: an element object read before a batch that was rolled back is no longer valid.
                        var (upper, lower, vertical) = (previous.Id, run.Id, riser.Id);
                        jobs.Add(new FittingJob(Describe(top), f => Elbow(doc, End(doc, upper, top), End(doc, vertical, top), f)));
                        jobs.Add(new FittingJob(Describe(bottom), f => Elbow(doc, End(doc, vertical, bottom), End(doc, lower, bottom), f)));
                    }
                }

                previous = run;
            }
        }

        tx.Commit();

        for (var n = 0; n < net.Nodes.Count; n++)
        {
            var ends = nodeEnds[n];
            if (ends.Count >= 2)
                jobs.Add(new FittingJob(Describe(ends[0].At), f => Join(doc, ends.Select(x => End(doc, x.Curve, x.At)).ToList(), f)));
        }

        // A reducer drawn off-centre: a transition between the two runs' ends, each run kept on its own line.
        foreach (var t in transitions)
        {
            if (nodeEnds[t.A].Count == 0 || nodeEnds[t.B].Count == 0)
                continue;
            var (ca, pa) = nodeEnds[t.A][0];
            var (cb, pb) = nodeEnds[t.B][0];
            jobs.Add(new FittingJob($"transition {Describe(pa)}", f =>
            {
                var (a, b) = (End(doc, ca, pa), End(doc, cb, pb));
                try
                {
                    Lengthen(doc, doc.Create.NewTransitionFitting(a, b), a, b);
                    return 1;
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
                {
                    f.Add($"transition at {Describe(a.Origin)}: {ex.Message}");
                    return 0;
                }
            }));
        }

        fittings += RunFittings(doc, jobs, failures);

        // Drainage outlets join the pipe already there (riser, main of another drawing).
        var outletJobs = new List<FittingJob>();
        foreach (var outlet in outletNodes)
        {
            if (nodeEnds[outlet].Count == 0)
                continue;
            var (curve, at) = nodeEnds[outlet][0];
            outletJobs.Add(new FittingJob($"outlet {Describe(at)}", f =>
            {
                var mine = End(doc, curve, at);
                if (mine.IsConnected)
                    return 0;
                var other = beforeIds.Select(doc.GetElement).OfType<MEPCurve>().SelectMany(c => c.ConnectorManager.Connectors.Cast<Connector>())
                    .Where(c => c.ConnectorType == ConnectorType.End && !c.IsConnected)
                    .OrderBy(c => c.Origin.DistanceTo(mine.Origin))
                    .FirstOrDefault(c => c.Origin.DistanceTo(mine.Origin) < UnitConv.MmToInternal(600));
                return other is null ? 0 : Elbow(doc, mine, other, f);
            }));
        }

        joined = RunFittings(doc, outletJobs, failures);
        group.Assimilate();

        var sb = new StringBuilder();
        sb.AppendLine($"Tuyến: {net.Edges.Count} đoạn, {net.Nodes.Count} nút, {parts.Count} mạng. Tạo: {curves} đoạn, {risers} đoạn đứng né dầm, {fittings} fitting.");
        if (setup.Run == MepRun.DrainPipe)
            sb.AppendLine($"Điểm xả nối vào ống có sẵn: {joined}/{outletNodes.Count}.");
        if (skipped > 0)
            sb.AppendLine($"Đã có ống/máng trùng vị trí, bỏ qua: {skipped} đoạn.");
        sb.Append(notes);
        if (failed + failures.Count > 0)
            sb.AppendLine($"Không tạo được: {failed} đoạn, {failures.Count} fitting (đoạn ống quá ngắn cho fitting, hoặc type thiếu Routing Preferences cho cút, tê, côn). Ống vẫn giữ, nối tay chỗ đó. Xem log.");
        foreach (var f in failures.Take(30))
            DssTechLog.Write($"mep fitting: {f}");
        return sb.ToString();
    }

    private static MEPCurve? Make(Document doc, Setup setup, Level level, XYZ a, XYZ b, MepSize size)
    {
        if (a.DistanceTo(b) < UnitConv.MmToInternal(20))
            return null;
        try
        {
            MEPCurve curve;
            switch (setup.Run)
            {
                case MepRun.Duct:
                {
                    var type = size.IsRound && setup.RoundTypeId != ElementId.InvalidElementId ? setup.RoundTypeId
                        : setup.RectTypeId != ElementId.InvalidElementId ? setup.RectTypeId : setup.RoundTypeId;
                    curve = Duct.Create(doc, setup.SystemId, type, level.Id, a, b);
                    var round = doc.GetElement(type) is DuctType { Shape: ConnectorProfileType.Round };
                    if (round)
                        curve.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(UnitConv.MmToInternal(size.IsRound ? size.DiameterMm : Math.Max(size.WidthMm, size.HeightMm)));
                    else
                    {
                        curve.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(UnitConv.MmToInternal(size.IsRound ? size.DiameterMm : size.WidthMm));
                        curve.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(UnitConv.MmToInternal(size.IsRound ? size.DiameterMm : size.HeightMm));
                    }

                    break;
                }
                case MepRun.CableTray:
                    curve = CableTray.Create(doc, setup.RectTypeId, a, b, level.Id);
                    curve.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.Set(UnitConv.MmToInternal(size.IsRound ? size.DiameterMm : size.WidthMm));
                    curve.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.Set(UnitConv.MmToInternal(size.IsRound ? size.DiameterMm : size.HeightMm));
                    curve.get_Parameter(BuiltInParameter.RBS_CTC_SERVICE_TYPE)?.Set(setup.ServiceType);
                    break;
                default:
                    curve = Pipe.Create(doc, setup.SystemId, setup.RoundTypeId, level.Id, a, b);
                    curve.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(UnitConv.MmToInternal(size.IsRound ? size.DiameterMm : Math.Max(size.WidthMm, size.HeightMm)));
                    break;
            }

            return curve;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
        {
            DssTechLog.Write($"mep create failed {Describe(a)}-{Describe(b)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>One fitting to make: where (for the log) and how; returns how many it made, failures go to the list.</summary>
    private sealed record FittingJob(string Where, Func<List<string>, int> Make);

    private const int FittingBatch = 50;

    /// <summary>
    /// Fittings in batches, each batch its own transaction. A batch Revit refuses (an error that cannot be ignored,
    /// such as a fitting longer than the piece it joins, which flips the piece) is rolled back and redone one
    /// fitting at a time, so only the refused fittings are left out.
    /// </summary>
    private static int RunFittings(Document doc, List<FittingJob> jobs, List<string> failures)
    {
        var made = 0;
        for (var start = 0; start < jobs.Count; start += FittingBatch)
        {
            var batch = jobs.Skip(start).Take(FittingBatch).ToList();
            var notes = new List<string>();
            if (TryFittings(doc, batch, notes, out var count, out _))
            {
                made += count;
                failures.AddRange(notes);
                continue;
            }

            foreach (var job in batch)
            {
                var one = new List<string>();
                if (TryFittings(doc, new List<FittingJob> { job }, one, out var single, out var refused))
                {
                    made += single;
                    failures.AddRange(one);
                }
                else
                    failures.Add($"{job.Where}: {refused ?? "Revit refused the fitting"}");
            }
        }

        return made;
    }

    private static bool TryFittings(Document doc, List<FittingJob> jobs, List<string> notes, out int made, out string? refused)
    {
        made = 0;
        var merged = new Dictionary<ElementId, ElementId>(Merged);
        var guard = new RollBackOnError();
        using var t = new Transaction(doc, "DSSTech MEP fittings");
        var options = t.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(guard);
        options.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(options);
        t.Start();
        foreach (var job in jobs)
        {
            try
            {
                made += job.Make(notes);
            }
            // Any fitting Revit refuses (or whose runs a refused one took with it) is left out alone, never the command.
            catch (Exception ex)
            {
                notes.Add($"{job.Where}: {ex.Message}");
            }
        }
        var committed = t.Commit() == TransactionStatus.Committed && guard.Error is null;
        refused = guard.Error;
        if (!committed)
        {
            // The batch's merges were rolled back with it.
            made = 0;
            Merged.Clear();
            foreach (var kv in merged)
                Merged[kv.Key] = kv.Value;
        }
        return committed;
    }

    /// <summary>Warnings are dropped; an error rolls the transaction back quietly (no dialog) and is kept for the log.</summary>
    internal sealed class RollBackOnError : IFailuresPreprocessor
    {
        public string? Error { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(failure);
                else
                    Error ??= failure.GetDescriptionText();
            }

            return Error is null ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
        }
    }

    /// <summary>The end of the run with this id nearest the point (read fresh: fittings change and roll back runs).</summary>
    private static Connector End(Document doc, ElementId curve, XYZ near)
    {
        // A main run through a branch's shoe was made one with the run after it.
        while (Merged.TryGetValue(curve, out var into))
            curve = into;
        return End(doc.GetElement(curve) as MEPCurve ?? throw new ArgumentException($"run {curve.Value} no longer exists"), near);
    }

    /// <summary>Bends drawn round in the CAD: plan corner and radius at the duct's centre (internal units).</summary>
    private static readonly List<(double X, double Y, double Radius)> Bends = new();
    private static bool BendLogged;

    /// <summary>
    /// The elbow at a bend drawn round takes the drawn radius (at the duct's centre): a radius parameter of the elbow
    /// set to it, or a radius multiplier to it over the duct's width, where the family lets either be set.
    /// </summary>
    private static void Round(FamilyInstance? elbow, Connector at)
    {
        if (elbow is null || at.Domain != Domain.DomainHvac)
            return;
        var p = at.Origin;
        var bend = Bends.Where(b => Math.Abs(b.X - p.X) < UnitConv.MmToInternal(100) && Math.Abs(b.Y - p.Y) < UnitConv.MmToInternal(100))
            .Select(b => (double?)b.Radius).FirstOrDefault();
        if (bend is not { } radius)
            return;
        Parameter? Settable(string name) =>
            elbow.LookupParameter(name) is { IsReadOnly: false, StorageType: StorageType.Double } q ? q : null;
        if (new[] { "Center Radius", "Centre Radius", "Bend Radius", "Duct Radius", "Radius", "R" }.Select(Settable).FirstOrDefault(q => q is not null) is { } r)
        {
            r.Set(radius);
            return;
        }

        var width = at.Shape == ConnectorProfileType.Round ? 2 * at.Radius : at.Width;
        var multipliers = new[] { "Radius Multiplier", "Bend Radius Multiplier", "Multiplier" };
        if (multipliers.Select(Settable).FirstOrDefault(q => q is not null) is { } m && width > 0)
        {
            m.Set(radius / width);
            return;
        }

        // The multiplier a type parameter (M-DUFI-REC_Cut): the elbow takes a type of its family with the drawn
        // multiplier (to 0.05), made from its own type when the project has none yet ("Standard R1.50").
        var symbol = elbow.Symbol;
        if (width > 0 && multipliers.Select(symbol.LookupParameter).FirstOrDefault(q => q is { StorageType: StorageType.Double }) is { } typeParam)
        {
            var wanted = Math.Round(radius / width / 0.05) * 0.05;
            if (wanted < 0.5 || Math.Abs(typeParam.AsDouble() - wanted) < 0.03)
                return;
            var name = $"{System.Text.RegularExpressions.Regex.Replace(symbol.Name, @" R\d+\.\d+$", "")} R{wanted.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";
            var doc = elbow.Document;
            var type = symbol.Family.GetFamilySymbolIds().Select(doc.GetElement).OfType<FamilySymbol>().FirstOrDefault(s => s.Name == name);
            if (type is null)
            {
                type = (FamilySymbol)symbol.Duplicate(name);
                type.LookupParameter(typeParam.Definition.Name)?.Set(wanted);
            }

            elbow.ChangeTypeId(type.Id);
            return;
        }

        if (BendLogged)
            return;
        BendLogged = true;
        var names = elbow.Parameters.Cast<Parameter>().Where(q => q.StorageType == StorageType.Double)
            .Select(q => $"{q.Definition.Name}{(q.IsReadOnly ? "(ro)" : "")}");
        DssTechLog.Write($"mep elbow {elbow.Symbol.FamilyName}: no settable radius; parameters: {string.Join(", ", names)}");
    }

    /// <summary>Runs deleted into the run before them (a main made one through a shoe): the run that took each one's place.</summary>
    private static readonly Dictionary<ElementId, ElementId> Merged = new();

    internal static Connector End(MEPCurve curve, XYZ near) =>
        curve.ConnectorManager.Connectors.Cast<Connector>()
            .Where(c => c.ConnectorType == ConnectorType.End)
            .OrderBy(c => c.Origin.DistanceTo(near))
            .First();

    internal static int Elbow(Document doc, Connector a, Connector b, List<string> failures)
    {
        try
        {
            doc.Create.NewElbowFitting(a, b);
            return 1;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
        {
            failures.Add($"elbow at {Describe(a.Origin)}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Fitting for the runs meeting at a node: straight joint or transition, elbow, tee, cross.</summary>
    private static int Join(Document doc, List<Connector> at, List<string> failures)
    {
        if (at.Count < 2)
            return 0;
        var where = Describe(at[0].Origin);
        try
        {
            switch (at.Count)
            {
                case 2:
                {
                    var straight = Math.Abs(at[0].CoordinateSystem.BasisZ.DotProduct(at[1].CoordinateSystem.BasisZ)) > 0.999;
                    if (!straight)
                        Round(NewElbow(doc, at[0], at[1]), at[0]);
                    else if (SameSize(at[0], at[1]))
                        at[0].ConnectTo(at[1]);
                    else
                        Lengthen(doc, doc.Create.NewTransitionFitting(at[0], at[1]), at[0], at[1]);
                    return 1;
                }
                case 3:
                {
                    var (m1, m2) = MostOpposite(at);
                    var branch = at.First(c => c != m1 && c != m2);
                    // Read before the shoe: a shoe Revit refuses is undone, and the connectors read before go with it.
                    var keys = new[] { m1, m2, branch }.Select(c => (c.Owner.Id, c.Origin)).ToArray();
                    if (Shoe(doc, m1, m2, branch, failures))
                        return 1;
                    var fresh = keys.Select(k => End(doc, k.Id, k.Origin)).ToArray();
                    doc.Create.NewTeeFitting(fresh[0], fresh[1], fresh[2]);
                    return 1;
                }
                case 4:
                {
                    var (m1, m2) = MostOpposite(at);
                    var rest = at.Where(c => c != m1 && c != m2).ToList();
                    doc.Create.NewCrossFitting(m1, m2, rest[0], rest[1]);
                    return 1;
                }
                default:
                    failures.Add($"{at.Count} runs at {Describe(at[0].Origin)}: join by hand");
                    return 0;
            }
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
        {
            failures.Add($"node {where} ({at.Count} runs): {ex.Message}");
            return 0;
        }
    }

    private static (Connector, Connector) MostOpposite(List<Connector> at)
    {
        var best = (at[0], at[1]);
        var bestDot = double.MaxValue;
        for (var i = 0; i < at.Count; i++)
        for (var j = i + 1; j < at.Count; j++)
        {
            var dot = at[i].CoordinateSystem.BasisZ.DotProduct(at[j].CoordinateSystem.BasisZ);
            if (dot < bestDot)
            {
                bestDot = dot;
                best = (at[i], at[j]);
            }
        }

        return best;
    }

    /// <summary>
    /// A narrower duct branching off a main that runs on the same size: the main made one run through the branch (the
    /// run after it deleted into it) and the branch joined with a takeoff, the shoe of the routing preferences. False,
    /// with nothing changed, when that does not apply or Revit refuses (no takeoff in the preferences): a tee then.
    /// </summary>
    private static bool Shoe(Document doc, Connector m1, Connector m2, Connector branch, List<string> failures)
    {
        if (m1.Owner is not Duct main || m2.Owner is not Duct next || branch.Owner is not Duct || main.Id == next.Id)
            return false;
        // The run already made one through an earlier shoe carries that takeoff: it stays, the other goes.
        if (Merged.ContainsValue(next.Id) && !Merged.ContainsValue(main.Id))
        {
            (main, next) = (next, main);
            (m1, m2) = (m2, m1);
        }
        if (m1.CoordinateSystem.BasisZ.DotProduct(m2.CoordinateSystem.BasisZ) > -0.999 || !SameSize(m1, m2))
            return false;
        static double Across(Connector c) => c.Shape == ConnectorProfileType.Round ? 2 * c.Radius : c.Width;
        if (Across(branch) >= Across(m1) - UnitConv.MmToInternal(1))
            return false;

        var where = Describe(branch.Origin);
        var nextId = next.Id;
        var mainTypeId = main.GetTypeId();
        var branchTypeId = branch.Owner.GetTypeId();
        var branchShape = branch.Shape;
        using var sub = new SubTransaction(doc);
        sub.Start();
        try
        {
            var start = Other(main, m1).Origin;
            var far = Other(next, m2);
            var end = far.Origin;
            // What the run being deleted joins beyond the branch, by owner and connector number: a connector read
            // before the delete is no longer valid after it.
            var beyond = far.AllRefs.Cast<Connector>().Where(c => c.Owner.Id != nextId).Select(c => (c.Owner.Id, c.Id)).ToList();
            var branchKey = (branch.Owner.Id, branch.Origin);
            var mainId = main.Id;
            doc.Delete(nextId);
            var kept = (Duct)doc.GetElement(mainId);
            ((LocationCurve)kept.Location).Curve = Line.CreateBound(start, end);
            var keptEnd = End(kept, end);
            foreach (var (owner, id) in beyond)
            {
                if (Find(doc, owner, id) is { } c)
                    keptEnd.ConnectTo(c);
            }

            Merged[nextId] = mainId;
            Tap(doc, branchKey.Item1, branchKey.Item2, kept);
            sub.Commit();
            return true;
        }
        catch (Exception ex)
        {
            if (sub.HasStarted())
                sub.RollBack();
            Merged.Remove(nextId);
            failures.Add($"shoe at {where} ({branchShape} branch): {ex.Message} (tee instead)");
            LogJunctions(doc, mainTypeId);
            LogJunctions(doc, branchTypeId);
            return false;
        }
    }

    private static readonly HashSet<ElementId> JunctionsLogged = new();

    /// <summary>
    /// The shoe (tap) of the branch on the main. The branch's end comes at the main's axis; refused there, it is tried
    /// again with the branch drawn back to the main's side, where a branch drawn by hand ends.
    /// </summary>
    private static void Tap(Document doc, ElementId branchId, XYZ branchEnd, Duct main)
    {
        using (var first = new SubTransaction(doc))
        {
            first.Start();
            try
            {
                doc.Create.NewTakeoffFitting(End(doc, branchId, branchEnd), main);
                first.Commit();
                return;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                first.RollBack();
            }
        }

        var branch = (MEPCurve)doc.GetElement(branchId);
        var line = (Line)((LocationCurve)branch.Location).Curve;
        var (p0, p1) = (line.GetEndPoint(0), line.GetEndPoint(1));
        var atStart = p0.DistanceTo(branchEnd) < p1.DistanceTo(branchEnd);
        var (end, far) = atStart ? (p0, p1) : (p1, p0);
        var away = (far - end).Normalize();
        var mainEnd = main.ConnectorManager.Connectors.Cast<Connector>().First(c => c.ConnectorType == ConnectorType.End);
        var half = (mainEnd.Shape == ConnectorProfileType.Round ? 2 * mainEnd.Radius : mainEnd.Width) / 2;
        // Across the main's axis in plan: the branch's own direction meets the side at half the width over the sine.
        var mainDir = ((Line)((LocationCurve)main.Location).Curve).Direction;
        var sine = Math.Max(0.3, Math.Abs(mainDir.X * away.Y - mainDir.Y * away.X));
        var side = end + away * (half / sine);
        if (side.DistanceTo(far) < UnitConv.MmToInternal(100))
            throw new ArgumentException("branch shorter than the main is wide");
        ((LocationCurve)branch.Location).Curve = atStart ? Line.CreateBound(side, far) : Line.CreateBound(far, side);
        doc.Create.NewTakeoffFitting(End(branch, side), main);
    }

    /// <summary>Once a run: the junction fittings the duct type's routing preferences offer (a shoe needs a tap there).</summary>
    private static void LogJunctions(Document doc, ElementId typeId)
    {
        if (!JunctionsLogged.Add(typeId) || doc.GetElement(typeId) is not MEPCurveType type)
            return;
        try
        {
            var prefs = type.RoutingPreferenceManager;
            var group = RoutingPreferenceRuleGroupType.Junctions;
            var parts = Enumerable.Range(0, prefs.GetNumberOfRules(group))
                .Select(i => doc.GetElement(prefs.GetRule(group, i).MEPPartId) as FamilySymbol)
                .Where(s => s is not null)
                .Select(s => $"{s!.FamilyName} : {s.Name} ({(PartType)s.Family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE).AsInteger()})");
            DssTechLog.Write($"mep duct type {type.Name}: preferred junction {prefs.PreferredJunctionType}; junctions: {string.Join(" | ", parts)}");
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"mep duct type {type.Name}: junctions not read ({ex.Message})");
        }
    }

    /// <summary>Connector number <paramref name="id"/> of a run or fitting, read fresh; null when it is gone.</summary>
    private static Connector? Find(Document doc, ElementId owner, int id)
    {
        var manager = doc.GetElement(owner) switch
        {
            MEPCurve c => c.ConnectorManager,
            FamilyInstance f => f.MEPModel?.ConnectorManager,
            _ => null
        };
        return manager?.Connectors.Cast<Connector>().FirstOrDefault(c => c.Id == id);
    }

    private static Connector Other(MEPCurve curve, Connector end) =>
        curve.ConnectorManager.Connectors.Cast<Connector>().First(c => c.ConnectorType == ConnectorType.End && !c.Origin.IsAlmostEqualTo(end.Origin));

    /// <summary>A duct transition as long as the duct is deep (the deeper of its two ends), where its family lets it be set.</summary>
    private static void Lengthen(Document doc, FamilyInstance? fitting, Connector a, Connector b)
    {
        if (fitting is null || a.Domain != Domain.DomainHvac)
            return;
        static double Depth(Connector c) => c.Shape == ConnectorProfileType.Round ? 2 * c.Radius : c.Height;
        var length = new[] { "Duct Length", "Length", "L" }.Select(fitting.LookupParameter).FirstOrDefault(p => p is not null);
        if (length is not { IsReadOnly: false, StorageType: StorageType.Double })
        {
            DssTechLog.Write($"mep transition {fitting.Symbol.FamilyName}: no settable Length");
            return;
        }

        var wanted = Math.Max(Depth(a), Depth(b));
        if (Math.Abs(length.AsDouble() - wanted) < 1e-6)
            return;
        // A longer transition pushes the runs back: Revit does not move them itself (it refuses the change as a
        // broken connection), so the runs are let go, the length set, each run's end brought onto its end of the
        // transition and joined again.
        var ends = new List<(Connector Fitting, ElementId Run)>();
        foreach (var fc in fitting.MEPModel.ConnectorManager.Connectors.Cast<Connector>())
        {
            var run = fc.AllRefs.Cast<Connector>().FirstOrDefault(c => c.Owner is MEPCurve);
            if (run is null)
                continue;
            ends.Add((fc, run.Owner.Id));
            fc.DisconnectFrom(run);
        }

        length.Set(wanted);
        doc.Regenerate();
        foreach (var (fc, runId) in ends)
        {
            var curve = (MEPCurve)doc.GetElement(runId);
            var line = (Line)((LocationCurve)curve.Location).Curve;
            var at = fc.Origin;
            var (p0, p1) = (line.GetEndPoint(0), line.GetEndPoint(1));
            ((LocationCurve)curve.Location).Curve = p0.DistanceTo(at) < p1.DistanceTo(at) ? Line.CreateBound(at, p1) : Line.CreateBound(p0, at);
            End(curve, at).ConnectTo(fc);
        }
    }

    /// <summary>
    /// An elbow between two runs meeting at a corner. Revit refuses some with both runs reaching the corner (two bends
    /// close together): then, as a trim would, both runs are drawn back to where the bend starts (the drawn radius's
    /// tangent, else the duct's width) and the elbow made between them.
    /// </summary>
    private static FamilyInstance NewElbow(Document doc, Connector a, Connector b)
    {
        var keys = new[] { a, b }.Select(c => (c.Owner.Id, c.Origin)).ToArray();
        using (var first = new SubTransaction(doc))
        {
            first.Start();
            try
            {
                var made = doc.Create.NewElbowFitting(a, b);
                first.Commit();
                return made;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                first.RollBack();
            }
        }

        var corner = keys[0].Origin;
        var runs = keys.Select(k => (MEPCurve)doc.GetElement(k.Id)).ToArray();
        var away = runs.Select(r =>
        {
            var line = (Line)((LocationCurve)r.Location).Curve;
            var far = line.GetEndPoint(0).DistanceTo(corner) > line.GetEndPoint(1).DistanceTo(corner) ? line.GetEndPoint(0) : line.GetEndPoint(1);
            return (Far: far, Dir: (far - corner).Normalize(), Length: far.DistanceTo(corner));
        }).ToArray();
        var turn = Math.PI - Math.Acos(Math.Max(-1, Math.Min(1, away[0].Dir.DotProduct(away[1].Dir))));
        var fresh = End(doc, keys[0].Id, keys[0].Origin);
        var width = fresh.Shape == ConnectorProfileType.Round ? 2 * fresh.Radius : fresh.Width;
        var radius = Bends.Where(x => Math.Abs(x.X - corner.X) < UnitConv.MmToInternal(100) && Math.Abs(x.Y - corner.Y) < UnitConv.MmToInternal(100))
            .Select(x => (double?)x.Radius).FirstOrDefault() ?? width;
        var tangent = radius * Math.Tan(turn / 2);
        for (var k = 0; k < 2; k++)
        {
            var back = Math.Min(tangent, 0.45 * away[k].Length);
            ((LocationCurve)runs[k].Location).Curve = Line.CreateBound(corner + away[k].Dir * back, away[k].Far);
        }

        return doc.Create.NewElbowFitting(End(runs[0], corner), End(runs[1], corner));
    }

    private static bool SameSize(Connector a, Connector b) =>
        a.Shape == b.Shape && (a.Shape == ConnectorProfileType.Round
            ? Math.Abs(a.Radius - b.Radius) < 1e-6
            : Math.Abs(a.Width - b.Width) < 1e-6 && Math.Abs(a.Height - b.Height) < 1e-6);

    private static double PlanDistance(MepNetwork net, int node, XYZ p)
    {
        var (x, y) = net.Nodes[node];
        var dx = x - UnitConv.ToMm(p.X);
        var dy = y - UnitConv.ToMm(p.Y);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>A straight run along the plan direction <paramref name="along"/> (within 3°), either way.</summary>
    private static bool Parallel(Curve c, XYZ along)
    {
        var d = c.GetEndPoint(1) - c.GetEndPoint(0);
        var flat = new XYZ(d.X, d.Y, 0);
        return flat.GetLength() > 1e-6 && Math.Abs(flat.Normalize().DotProduct(along)) > 0.9986;
    }

    private static bool PlanNear(Curve c, XYZ p)
    {
        if (Math.Abs(c.GetEndPoint(0).Z - c.GetEndPoint(1).Z) > UnitConv.MmToInternal(300))
            return false;
        var q = c.Project(new XYZ(p.X, p.Y, c.GetEndPoint(0).Z));
        return q is not null && Math.Abs(q.XYZPoint.X - p.X) < UnitConv.MmToInternal(30) &&
               Math.Abs(q.XYZPoint.Y - p.Y) < UnitConv.MmToInternal(30) &&
               Math.Abs(q.XYZPoint.Z - p.Z) < UnitConv.MmToInternal(1500);
    }

    internal static string Describe(XYZ p) => $"({UnitConv.ToMm(p.X):0},{UnitConv.ToMm(p.Y):0},{UnitConv.ToMm(p.Z):0})";
}
