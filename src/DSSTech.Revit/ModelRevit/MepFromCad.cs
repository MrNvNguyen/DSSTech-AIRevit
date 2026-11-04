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

/// <summary>«Cơ ▸ Ống gió»: supply, return, fresh air, exhaust ducts.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepDuctCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — Cơ",
            new MepChoice(MepRun.Duct, "Ống gió", "Cấp, hồi, gió tươi, thải… (Duct)", MepSystemCatalog.Mech));
}

/// <summary>«Cơ ▸ Ống nước Cơ»: chilled, cooling water and refrigerant pipes.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepMechPipeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — Cơ",
            new MepChoice(MepRun.PressurePipe, "Ống nước Cơ", "Nước Chiller, tháp giải nhiệt, gas (Pipe)", MepSystemCatalog.Mech));
}

/// <summary>«Cơ ▸ Thoát nước ngưng»: condensate drains falling to their outlet.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepCondensateCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — Cơ",
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

            var lines = geom.Entities.Where(e => e is not CadText && setup.Layers.Contains(e.Layer));
            var net = MepRoutePlanner.Build(lines, null, setup.Fallback, geom.Entities.OfType<CadText>());
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
                _ => $"size mặc định {g.Count()}"
            }));
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
            var underStructure = dlg.AddOption(choice.Run == MepRun.DrainPipe
                ? "Đầu tuyến cao nhất sát đáy dầm/sàn kết cấu (điểm xả tính lùi theo độ dốc)"
                : "Cao độ theo đáy dầm/sàn kết cấu, uốn né dầm", overhead.Count > 0);
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
        var levelMm = UnitConv.ToMm(level.Elevation);
        var fromZ = level.Elevation + UnitConv.MmToInternal(500);
        var toZ = (topLevel.Id == level.Id ? level.Elevation + UnitConv.MmToInternal(6000) : topLevel.Elevation) + UnitConv.MmToInternal(1500);
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
        else
        {
            var profiles = new List<List<MepPiece>>();
            foreach (var e in net.Edges)
            {
                var (ax, ay) = net.Nodes[e.A];
                var (bx, by) = net.Nodes[e.B];
                var len = MepRoutePlanner.Length(net, e);
                var netDepth = depth[partOf[e.A]];
                var manualCenter = Math.Floor(setup.ManualCenter(levelMm, netDepth) / 5) * 5;
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
        using var tx = new Transaction(doc, "DSSTech MEP from CAD");
        tx.Start();
        var before = new FilteredElementCollector(doc).OfClass(typeof(MEPCurve)).Cast<MEPCurve>()
            .Where(c => c.Location is LocationCurve)
            .ToList();
        var reach = UnitConv.MmToInternal(30);
        var existing = before.Select(c => ((LocationCurve)c.Location).Curve)
            .Select(c => (Curve: c, MinX: Math.Min(c.GetEndPoint(0).X, c.GetEndPoint(1).X) - reach,
                MaxX: Math.Max(c.GetEndPoint(0).X, c.GetEndPoint(1).X) + reach,
                MinY: Math.Min(c.GetEndPoint(0).Y, c.GetEndPoint(1).Y) - reach,
                MaxY: Math.Max(c.GetEndPoint(0).Y, c.GetEndPoint(1).Y) + reach))
            .ToList();
        var nodeConnectors = net.Nodes.Select(_ => new List<Connector>()).ToList();
        for (var i = 0; i < net.Edges.Count; i++)
        {
            var e = net.Edges[i];
            var (ax, ay) = net.Nodes[e.A];
            var (bx, by) = net.Nodes[e.B];
            var len = MepRoutePlanner.Length(net, e);
            XYZ At(double s, double zMm) => new(
                UnitConv.MmToInternal(ax + (bx - ax) * s / len), UnitConv.MmToInternal(ay + (by - ay) * s / len), UnitConv.MmToInternal(zMm));
            var first = pieces[i][0];
            var mid = At(len / 2, (first.Z0 + first.Z1) / 2);
            if (existing.Any(c => mid.X >= c.MinX && mid.X <= c.MaxX && mid.Y >= c.MinY && mid.Y <= c.MaxY && PlanNear(c.Curve, mid)))
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
                    nodeConnectors[e.A].Add(End(run, At(p.From, p.Z0)));
                if (k == list.Count - 1)
                    nodeConnectors[e.B].Add(End(run, At(p.To, p.Z1)));
                if (previous is not null)
                {
                    // Drop under (or rise over) the beam: a vertical piece and an elbow at each end.
                    var top = At(p.From, list[k - 1].Z1);
                    var bottom = At(p.From, p.Z0);
                    var riser = Make(doc, setup, level, top, bottom, e.Size);
                    if (riser is not null)
                    {
                        risers++;
                        fittings += Elbow(doc, End(previous, top), End(riser, top), failures);
                        fittings += Elbow(doc, End(riser, bottom), End(run, bottom), failures);
                    }
                }

                previous = run;
            }
        }

        for (var n = 0; n < net.Nodes.Count; n++)
            fittings += Join(doc, nodeConnectors[n], failures);

        // Drainage outlets join the pipe already there (riser, main of another drawing).
        foreach (var outlet in outletNodes)
        {
            var mine = nodeConnectors[outlet].FirstOrDefault();
            if (mine is null || mine.IsConnected)
                continue;
            var other = before.SelectMany(c => c.ConnectorManager.Connectors.Cast<Connector>())
                .Where(c => c.ConnectorType == ConnectorType.End && !c.IsConnected)
                .OrderBy(c => c.Origin.DistanceTo(mine.Origin))
                .FirstOrDefault(c => c.Origin.DistanceTo(mine.Origin) < UnitConv.MmToInternal(600));
            if (other is null)
                continue;
            try
            {
                doc.Create.NewElbowFitting(mine, other);
                joined++;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                failures.Add($"outlet join at {Describe(mine.Origin)}: {ex.Message}");
            }
        }

        tx.Commit();

        var sb = new StringBuilder();
        sb.AppendLine($"Tuyến: {net.Edges.Count} đoạn, {net.Nodes.Count} nút, {parts.Count} mạng. Tạo: {curves} đoạn, {risers} đoạn đứng né dầm, {fittings} fitting.");
        if (setup.Run == MepRun.DrainPipe)
            sb.AppendLine($"Điểm xả nối vào ống có sẵn: {joined}/{outletNodes.Count}.");
        if (skipped > 0)
            sb.AppendLine($"Đã có ống/máng trùng vị trí, bỏ qua: {skipped} đoạn.");
        sb.Append(notes);
        if (failed + failures.Count > 0)
            sb.AppendLine($"Không tạo được: {failed} đoạn, {failures.Count} fitting (type cần Routing Preferences / fitting cho cút, tê, côn). Xem log.");
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
        try
        {
            switch (at.Count)
            {
                case 2:
                {
                    var straight = Math.Abs(at[0].CoordinateSystem.BasisZ.DotProduct(at[1].CoordinateSystem.BasisZ)) > 0.999;
                    if (!straight)
                        doc.Create.NewElbowFitting(at[0], at[1]);
                    else if (SameSize(at[0], at[1]))
                        at[0].ConnectTo(at[1]);
                    else
                        doc.Create.NewTransitionFitting(at[0], at[1]);
                    return 1;
                }
                case 3:
                {
                    var (m1, m2) = MostOpposite(at);
                    doc.Create.NewTeeFitting(m1, m2, at.First(c => c != m1 && c != m2));
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
            failures.Add($"node {Describe(at[0].Origin)} ({at.Count} runs): {ex.Message}");
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
