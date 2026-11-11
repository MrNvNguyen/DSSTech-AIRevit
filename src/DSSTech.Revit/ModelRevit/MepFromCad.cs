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
    CableTray,
    /// <summary>Busway drawn as one (wide) stroke: modelled as cable tray of the size typed in, Service Type BW.</summary>
    Busway
}

/// <param name="Codes">Systems listed first in the dialog, where not the whole group's (smoke: SEAD, PAD).</param>
internal sealed record MepChoice(MepRun Run, string Label, string Detail, string? Group, string[]? Codes = null);

// Each discipline is a drop-down on the ribbon; each item builds one kind of run.

/// <summary>«ĐHKK ▸ Ống gió»: supply, return, fresh air, exhaust ducts.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepDuctCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — ĐHKK",
            new MepChoice(MepRun.Duct, "Ống gió", "Cấp, hồi, gió tươi, thải… (Duct)", MepSystemCatalog.Mech));
}

/// <summary>«ĐHKK ▸ Ống Chiller»: chilled, cooling water and refrigerant pipes.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepMechPipeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — ĐHKK",
            new MepChoice(MepRun.PressurePipe, "Ống Chiller", "Nước Chiller, tháp giải nhiệt, gas (Pipe)", MepSystemCatalog.Mech));
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
            new MepChoice(MepRun.CableTray, "Máng / thang cáp", "Trung thế, hạ thế, điện nhẹ, báo cháy (Cable Tray)", MepSystemCatalog.Elec));
}

/// <summary>«Điện ▸ Busway»: busways drawn single line, at the size typed in.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepBuswayCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        MepFromCad.Run(commandData.Application, "DSSTech — Busway",
            new MepChoice(MepRun.Busway, "Busway", "Thanh dẫn điện vẽ 1 nét, kích thước chỉ định (Cable Tray, Service Type BW)", MepSystemCatalog.Elec));
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
            // Ducts as «ĐHKK ▸ Ống gió» makes them; only the smoke exhaust and pressurisation systems come first.
            new MepChoice(MepRun.Duct, "Tăng áp / hút khói", "Ống gió tạo áp, hút khói (Duct)", MepSystemCatalog.Mech, new[] { "SEAD", "PAD" }));
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
    /// <summary>Service Type of busways (system BW).</summary>
    private const string BuswayService = "BW";
    private const double OverheadCellMm = 3000;
    private const string TopOf = "Đỉnh (Top Elevation)";
    private const string CenterOf = "Tâm (Middle Elevation)";
    private const string BottomOf = "Đáy (Bottom Elevation)";
    private const string DrainAuto = "Tự đọc CAD: tầng trên WC / phễu thu → trục; tầng dưới cùng trục → hố ga / bể";
    private const string DrainPickHead = "Pick trục (ống đứng) mỗi mạng, chiều chảy theo tầng như trên";
    private const string DrainPickOutlets = "Pick điểm xả (mạng gom nhiều nhánh về điểm xả)";

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
            // Labels: marks and sizes, and the longer notes with a size or an elevation (THANG CÁP 1200X100, H=2800).
            var texts = geom.Entities.OfType<CadText>().Concat(geom.NoteTexts).ToList();
            Dump(lines, texts);
            // Shoes are drawn as small outlines left out with the devices: kept here to turn each takeoff their way.
            Drawn = lines.ToList();
            // Ducts: grilles, dampers, fan coils; pipes: valves and fittings drawn as blocks. Not trays (bends drawn as blocks).
            if (choice.Run == MepRun.Busway)
                lines = BuswayStrokes(lines);
            else if (choice.Run != MepRun.CableTray)
                lines = MepRoutePlanner.WithoutDevices(lines, out devices);
            // Ducts are drawn with both sides; trays either way (each layer read as it looks); pipes and busways single-line.
            bool? readAs = choice.Run switch { MepRun.Duct => true, MepRun.CableTray => null, _ => false };
            var net = MepRoutePlanner.Build(lines, null, setup.Fallback, texts, readAs, busway: choice.Run == MepRun.Busway);
            // A busway's section is not drawn: every run at the size typed in.
            if (choice.Run == MepRun.Busway)
            {
                foreach (var e in net.Edges)
                {
                    e.Size = setup.Fallback;
                    e.SizeFrom = "given";
                }
            }
            var layerLabel = setup.Layers.Count <= 3 ? "layer " + string.Join(", ", setup.Layers) : $"{setup.Layers.Count} layer";
            if (net.Edges.Count == 0)
            {
                TaskDialog.Show(title, $"Không thấy tuyến nào trên {layerLabel}.");
                return Result.Cancelled;
            }

            // Drainage: read from the CAD (one head per network, every other open end an outlet), the head of each network
            // picked, or (a network gathering many branches to one outlet) the outlets picked; Esc when done.
            var outlets = new List<XYZ>();
            var heads = new List<XYZ>();
            if (choice.Run == MepRun.DrainPipe && setup.DrainMode == DrainPickHead)
            {
                TaskDialog.Show(title,
                    "Pick đầu ống tại TRỤC (ống đứng) của từng mạng — mỗi mạng 1 điểm. Tầng trên: WC / phễu thu chảy về trục; " +
                    "tầng dưới cùng (hoặc có chữ bể / hố ga ở đầu ống): từ trục ra mọi đầu hở còn lại. Esc khi xong.\n" +
                    "Mạng không pick: trục tự nhận theo chữ trên CAD (TR, ống đứng) rồi đến ống lớn nhất.");
                while (true)
                {
                    try
                    {
                        heads.Add(uidoc.Selection.PickPoint("Pick đầu tuyến (mỗi mạng 1 điểm) — Esc khi xong."));
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

            if (choice.Run == MepRun.DrainPipe && setup.DrainMode == DrainPickOutlets)
            {
                TaskDialog.Show(title,
                    "Pick MỌI điểm xả: điểm CUỐI tuyến, nơi ống xả vào bể tự hoại / hố ga / ống đứng (không pick điểm đầu tuyến). " +
                    "Một mạng có thể có nhiều điểm xả: mỗi đoạn dốc về điểm xả gần nó nhất theo đường ống. Esc khi xong.\n" +
                    "Mạng không pick: điểm xả là đầu hở của đoạn ống lớn nhất.");
                while (true)
                {
                    try
                    {
                        outlets.Add(uidoc.Selection.PickPoint("Pick điểm xả (cuối tuyến, chỗ xả vào bể / hố ga) — pick hết các điểm xả, Esc khi xong."));
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

            // Runs drawn a few degrees off square get no fitting: tees slid square along the main, free stubs turned to 90°.
            var (teesSquared, cornersSquared) = MepRoutePlanner.SquareUp(net);
            // Sprinkler pipes reduce 150 mm past the head or the branch tee, not at it.
            ReducerNodes.Clear();
            var reducersMoved = choice.Group == MepSystemCatalog.Fire ? MepRoutePlanner.ReducersPast(net, reducers: ReducerNodes) : 0;
            var report = Build(doc, net, level, topLevel, setup, outlets, heads, texts);
            if (reducersMoved > 0)
                report += $"Côn giảm dời {150} mm qua đầu phun / tê nhánh sang phía ống nhỏ: {reducersMoved} chỗ.\n";
            if (teesSquared + cornersSquared > 0)
                report += $"Nét CAD lệch vài độ được chỉnh vuông: {teesSquared} tê trượt dọc ống chính, {cornersSquared} góc có đầu tự do xoay về 90°.\n";
            // Heads are joined by «Nối đầu phun» only (rigid or flexible, chosen there), not here.
            var sources = string.Join(", ", net.Edges.GroupBy(e => e.SizeFrom).Select(g => g.Key switch
            {
                "text" => $"chữ cạnh nét {g.Count()}",
                "layer" => $"tên layer {g.Count()}",
                "inherited" => $"nối theo đoạn trước {g.Count()}",
                "drawn" => $"bề rộng 2 nét vẽ {g.Count()}",
                "given" => $"kích thước chỉ định {setup.Fallback} cho {g.Count()} đoạn",
                _ => $"size mặc định {g.Count()}"
            }));
            var twoLine = net.Edges.Count(e => e.DrawnWidthMm > 0);
            if (twoLine > 0)
                sources += $" (ống vẽ 2 nét: dựng theo tim, {twoLine} đoạn)";
            if (net.FlexSkipped > 0)
                report = $"Bỏ qua {net.FlexSkipped} đoạn / layer ống mềm: nối sau bằng «Nối ống mềm».\n" + report;
            if (net.LadderPiecesSkipped > 0)
                report = $"Bỏ qua {net.LadderPiecesSkipped} mẩu thang cáp đứng riêng trong phụ kiện (tê, chỗ đổi cao độ), không ghi kích thước.\n" + report;
            if (net.OpeningsSkipped > 0)
                report = $"Bỏ qua {net.OpeningsSkipped} ô ngắn đứng riêng, không ghi kích thước (lỗ thông gió / louver qua tường).\n" + report;
            if (devices > 0)
                report = (choice.Run == MepRun.Duct
                    ? $"Bỏ qua {devices} thiết bị vẽ trên layer ống (block / khung miệng gió, FCU, box): đặt bằng «Thiết bị ĐHKK», nối bằng «Nối ống mềm».\n"
                    : $"Bỏ qua {devices} ký hiệu van / phụ kiện vẽ trên layer ống (block): tuyến ống liền qua chỗ đó.\n") + report;
            DoneReport.Show(title,
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

    private static List<CadEntity> BuswayStrokes(IEnumerable<CadEntity> lines)
    {
        var all = lines.ToList();
        var kept = MepRoutePlanner.BuswayStrokes(all);
        // Gaps where a tray drawn over the busway hides it.
        var bridges = MepRoutePlanner.BuswayBridges(kept);
        DssTechLog.Write($"busway strokes: {all.Count} on layer, {all.Count(e => e is CadPolyline { WidthMm: > 0 })} wide, kept {kept.Count}, bridged {bridges.Count} gaps");
        kept.AddRange(bridges);
        return kept;
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
        /// <summary>Drainage: how heads and outlets are found (read from the CAD, start picked, or outlets picked).</summary>
        public string DrainMode { get; init; } = DrainAuto;
        /// <summary>Vent (thông hơi): rises from the branch ends (low) to the riser (high), the other way from a drain.</summary>
        public bool Vent { get; init; }
        /// <summary>Elevations written on the CAD (H=2800) are used where a run has one.</summary>
        public bool UseCadElevation { get; init; }
        /// <summary>What a CAD elevation measures: the run's bottom, middle or top.</summary>
        public string CadRef { get; init; } = BottomOf;
        /// <summary>
        /// Branches joined to the main by tees, crosses and taps. Off: the main runs on through each junction and the
        /// branch stops at its side, to be joined after Combine has moved the runs.
        /// </summary>
        public bool JoinBranches { get; init; } = true;

        /// <summary>
        /// Outside diameter (mm) of each nominal size of the pipe type's segment: a DN150 steel pipe is 168.3 across, and
        /// a bottom at 2600 is its outside, not its nominal bore. Empty for ducts and trays.
        /// </summary>
        public Dictionary<double, double> PipeOuterMm { get; init; } = new();

        /// <summary>What a run's height is measured over: the pipe's outside diameter, else the section's depth.</summary>
        public double Depth(MepSize size) =>
            size.IsRound && PipeOuterMm.TryGetValue(Math.Round(size.DiameterMm), out var od) ? od : size.DepthMm;

        /// <summary>Centerline (absolute mm) of a run whose CAD note gives its height; null where none or not used.</summary>
        public double? CadCenter(double levelMm, MepEdge e) =>
            UseCadElevation && e.ElevationMm is double h ? levelMm + h + Offset(CadRef, Depth(e.Size)) : null;

        private static double Offset(string reference, double depthMm) => reference switch
        {
            TopOf => -depthMm / 2,
            BottomOf => depthMm / 2,
            _ => 0
        };

        public bool UnderStructure => Overhead.Count > 0;

        public double ManualCenter(double levelMm, double depthMm) => levelMm + ManualMm + Offset(ManualRef, depthMm);

        public static Setup? Ask(UIApplication uiApp, Document doc, string title, MepChoice choice, string linkName,
            List<(string Name, int Count)> layers, Level level, Level topLevel)
        {
            var overhead = ModelRevitWallFit.Overhead(doc);
            var domain = choice.Run switch
            {
                MepRun.Duct => MepDomain.Duct,
                MepRun.CableTray or MepRun.Busway => MepDomain.CableTray,
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
            // Each tool its own systems: drainage (Sanitary, Vent) only under «Thoát nước», never under «Cấp nước» or PCCC,
            // so a drain is never modelled without its fall. A project whose systems are not classified keeps them all.
            if (domain == MepDomain.Pipe)
            {
                bool Drains(ElementId id) => doc.GetElement(id) is PipingSystemType t
                    && t.SystemClassification is MEPSystemClassification.Sanitary or MEPSystemClassification.Vent;
                var own = systems.Where(s => Drains(s.Id) == (choice.Run == MepRun.DrainPipe)).ToList();
                if (own.Count > 0)
                    systems = own;
            }

            if (systems.Count == 0)
            {
                TaskDialog.Show(title, "Project chưa có system cho loại này. Chạy «Hệ thống» để tạo hệ theo quy định trước.");
                return null;
            }

            // The group's systems first (PCCC shows fire systems first), the one named by the link preselected.
            var groupCodes = choice.Codes?.ToHashSet() ?? MepSystemCatalog.All.Where(s => s.Group == choice.Group).Select(s => s.Abbr).ToHashSet();
            systems = systems.OrderByDescending(s => groupCodes.Contains(s.Abbr)).ThenBy(s => s.Name).ToList();
            var preset = spec is null ? null : systems.Select(s => (string?)s.Name).FirstOrDefault(n =>
                MepSystemCatalog.Match(spec, new[] { new ExistingSystem { Name = n!, Abbreviation = systems.First(x => x.Name == n).Abbr, Domain = domain } })
                    .Kind != SystemMatchKind.None);
            if (choice.Run == MepRun.Busway)
                preset = systems.Select(s => (string?)s.Name).FirstOrDefault(n => systems.First(x => x.Name == n).Abbr == BuswayService) ?? preset;
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
            var fallback = choice.Run == MepRun.Busway
                ? dlg.AddInput("Kích thước busway (rộng x cao, mm) — dùng cho mọi đoạn", "200x100")
                : dlg.AddInput("Size khi không đọc được (vd 400x300, D100)",
                    domain == MepDomain.Pipe ? "D50" : domain == MepDomain.CableTray ? "300x100" : "400x300");

            System.Windows.Controls.TextBox? slope = null;
            System.Windows.Controls.ComboBox? drainMode = null;
            if (choice.Run == MepRun.DrainPipe)
            {
                slope = dlg.AddInput("Độ dốc (%) — chảy về điểm xả", "1");
                drainMode = dlg.AddCombo("Nhận đầu tuyến / điểm xả", new[] { DrainAuto, DrainPickHead, DrainPickOutlets }, DrainAuto);
            }
            var underStructure = dlg.AddOption(choice.Run switch
            {
                MepRun.DrainPipe => "Điểm đầu tuyến (cao nhất) cách đáy dầm/sàn bằng khe hở dưới; cao độ còn lại nội suy theo độ dốc về điểm xả",
                MepRun.Duct => "So với đáy dầm/sàn: đáy dầm thấp hơn cao độ tự nhập thì cả mạng hạ xuống dưới dầm (mọi ống cùng mặt đáy/đỉnh)",
                _ => "Cao độ theo đáy dầm/sàn kết cấu, uốn né dầm"
            }, overhead.Count > 0);
            var clearance = dlg.AddInput(choice.Run == MepRun.DrainPipe ? "Khe hở từ đáy dầm/sàn đến đỉnh ống tại điểm đầu tuyến (mm)" : "Khe hở dưới đáy dầm/sàn (mm)",
                choice.Run == MepRun.DrainPipe ? "150" : "50");
            var storeyMm = topLevel.Id == level.Id ? 3300 : Math.Round(UnitConv.ToMm(topLevel.Elevation - level.Elevation));
            var reference = dlg.AddCombo(choice.Run == MepRun.DrainPipe ? "Cao độ tự nhập tại điểm đầu tuyến theo (khi không có dầm/sàn)" : "Cao độ tự nhập theo",
                new[] { TopOf, CenterOf, BottomOf }, choice.Run == MepRun.DrainPipe ? BottomOf : TopOf);
            // Drainage under the floor it is drawn on: below the level, a negative height.
            var manual = dlg.AddInput(choice.Run == MepRun.DrainPipe ? "Cao độ tự nhập so với level (mm, âm = dưới level)" : "Cao độ tự nhập so với level (mm)",
                (choice.Run == MepRun.DrainPipe ? -500 : storeyMm - 500).ToString("0"));
            // Drainage keeps its fall from the outlet; the others take the height written on the CAD where there is one.
            System.Windows.Controls.CheckBox? useCad = null;
            System.Windows.Controls.ComboBox? cadRef = null;
            if (choice.Run != MepRun.DrainPipe)
            {
                useCad = dlg.AddOption("Dùng cao độ ghi trên CAD (H=2800, BOT 2600…) cho đoạn có ghi; đoạn không ghi dùng cao độ ở trên", true);
                cadRef = dlg.AddCombo("Cao độ ghi trên CAD là", new[] { BottomOf, CenterOf, TopOf }, BottomOf);
            }

            // Branches joined later (after Combine): the main runs on through, each branch stops at its side.
            System.Windows.Controls.CheckBox? joinBranches = null;
            if (choice.Run != MepRun.DrainPipe)
                joinBranches = dlg.AddOption("Nối nhánh vào tuyến chính (tê, chữ thập, tap) — bỏ chọn để nối sau khi Combine",
                    choice.Run != MepRun.PressurePipe);

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
                ManualMm = Num(manual, choice.Run == MepRun.DrainPipe ? -500 : storeyMm - 500),
                ManualRef = DssTheme.ComboValue(reference),
                SlopePercent = Math.Max(0, Num(slope, 1)),
                DrainMode = drainMode is null ? DrainAuto : DssTheme.ComboValue(drainMode),
                Vent = choice.Run == MepRun.DrainPipe && MepRoutePlanner.IsVent(
                    doc.GetElement(system.Id) is Autodesk.Revit.DB.Plumbing.PipingSystemType pst && pst.SystemClassification == MEPSystemClassification.Vent,
                    system.Name, system.Abbr ?? "", layerBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag)),
                UseCadElevation = useCad?.IsChecked == true,
                CadRef = cadRef is null ? BottomOf : DssTheme.ComboValue(cadRef),
                JoinBranches = joinBranches is null || joinBranches.IsChecked == true,
                PipeOuterMm = domain == MepDomain.Pipe && roundBox is not null
                    ? PipeOuter(doc, round.First(t => t.Name == DssTheme.ComboValue(roundBox)).Id)
                    : new Dictionary<double, double>()
            };
        }

        /// <summary>Nominal → outside diameter (mm) of the segment the pipe type routes with.</summary>
        private static Dictionary<double, double> PipeOuter(Document doc, ElementId pipeTypeId)
        {
            var map = new Dictionary<double, double>();
            try
            {
                var rpm = (doc.GetElement(pipeTypeId) as PipeType)?.RoutingPreferenceManager;
                if (rpm is null || rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments) == 0)
                    return map;
                if (doc.GetElement(rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, 0).MEPPartId) is not PipeSegment segment)
                    return map;
                foreach (var size in segment.GetSizes())
                    map[Math.Round(UnitConv.ToMm(size.NominalDiameter))] = UnitConv.ToMm(size.OuterDiameter);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                DssTechLog.Write($"pipe outer diameters: {ex.Message}");
            }

            return map;
        }

        private static string Abbr(MEPSystemType s) =>
            s.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.AsString() ?? "";

        /// <summary>The layer name holds the system code as a word of its own (M-SAD-600x400, SAD_DUCT; not «SADDLE»).</summary>
        private static bool NamesCode(string layer, string code) =>
            code.Length > 0 && Regex.IsMatch(layer, $"(?<![A-Za-z0-9]){Regex.Escape(code)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
    }

    /// <summary>One piece of one edge: from/to along the edge, centerline elevation at each end (absolute mm).</summary>
    private readonly record struct Piece(double From, double To, double Z0, double Z1);

    private static string Build(Document doc, MepNetwork net, Level level, Level topLevel, Setup setup, List<XYZ> outletPicks,
        List<XYZ> headPicks, IReadOnlyList<CadText> texts)
    {
        var levelMm = UnitConv.ToMm(level.ProjectElevation);
        var fromZ = level.ProjectElevation + UnitConv.MmToInternal(500);
        var toZ = (topLevel.Id == level.Id ? level.ProjectElevation + UnitConv.MmToInternal(6000) : topLevel.ProjectElevation) + UnitConv.MmToInternal(1500);
        if (setup.Run == MepRun.DrainPipe)
        {
            // Drainage hangs under the floor of the level it is drawn on, at heights below the level (negative): the beams
            // and slab of that floor, from the level (a slab's top, its finish a little over) down to 500 mm short of the
            // level below (3000 mm with none), not those of the floor above.
            var below = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Where(l => l.ProjectElevation < level.ProjectElevation - UnitConv.MmToInternal(1))
                .OrderByDescending(l => l.ProjectElevation).FirstOrDefault();
            var downMm = below is not null ? Math.Max(500, UnitConv.ToMm(level.ProjectElevation - below.ProjectElevation) - 500) : 3000;
            fromZ = level.ProjectElevation - UnitConv.MmToInternal(downMm);
            toZ = level.ProjectElevation + UnitConv.MmToInternal(50);
        }
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
            depth[partOf[e.A]] = Math.Max(depth[partOf[e.A]], setup.Depth(e.Size));
        var edgesAt = net.Nodes.Select(_ => new List<int>()).ToList();
        for (var i = 0; i < net.Edges.Count; i++)
        {
            edgesAt[net.Edges[i].A].Add(i);
            edgesAt[net.Edges[i].B].Add(i);
        }

        var pieces = new List<List<Piece>>();
        var notes = new StringBuilder();
        var noStructure = 0;
        var fromCad = 0;
        var outletNodes = new List<int>();
        if (setup.Run == MepRun.DrainPipe)
        {
            // Each network falls toward its outlets: every point picked (the pipe's last point, where it discharges into a
            // septic tank or manhole), each to the open end nearest to it; none picked: its main end.
            var slope = setup.SlopePercent / 100;
            var nodeZ = new Dictionary<int, double>();
            var partOutlets = new List<List<int>>();
            var pickedParts = new List<bool>();
            var headWhy = new List<string>();
            var allEnds = Enumerable.Range(0, net.Nodes.Count).Where(n => edgesAt[n].Count == 1).ToList();
            // The lowest floor (no level under it) runs from the riser foot out; a basement over a foundation level is
            // told by the tank / manhole notes at its ends instead.
            var lowestFloor = !new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Any(l => l.ProjectElevation < level.ProjectElevation - UnitConv.MmToInternal(1));
            for (var p = 0; p < parts.Count; p++)
            {
                var ends = parts[p].Where(n => edgesAt[n].Count == 1).ToList();
                if (setup.DrainMode != DrainPickOutlets)
                {
                    // Each network meets its riser at one end (picked, else read from the CAD). Upper floors: WCs and
                    // floor drains flow to the riser; the lowest floor: from the riser foot out to manholes and tanks.
                    var pickedRiser = headPicks
                        .Select(pt => ends.Select(n => (Node: n, D: PlanDistance(net, n, pt))).Where(x => x.D <= 1500)
                            .OrderBy(x => x.D).Select(x => (int?)x.Node).FirstOrDefault())
                        .FirstOrDefault(n => n is not null);
                    var inPart = new HashSet<int>(parts[p]);
                    var flow = MepRoutePlanner.DrainFlow(net, parts[p], texts,
                        allEnds.Where(n => !inPart.Contains(n)).Select(n => net.Nodes[n]), lowestFloor, pickedRiser);
                    pickedParts.Add(pickedRiser is not null);
                    // Vent: every floor climbs from the branch ends to the riser; the riser is the one top (taken as
                    // the "outlet" here, turned over below).
                    headWhy.Add(setup.Vent
                        ? $"trục ({net.Nodes[flow.Riser].X:0},{net.Nodes[flow.Riser].Y:0}): {flow.Why.Split(';')[0]}; thông hơi: đầu nhánh thấp, dốc lên về trục"
                        : $"trục ({net.Nodes[flow.Riser].X:0},{net.Nodes[flow.Riser].Y:0}): {flow.Why}");
                    partOutlets.Add(setup.Vent ? new List<int> { flow.Riser } : flow.Outlets);
                    outletNodes.AddRange(partOutlets[p]);
                    continue;
                }

                headWhy.Add("");
                var picked = outletPicks
                    .Select(pt => (ends.Count > 0 ? ends : parts[p]).Select(n => (Node: n, D: PlanDistance(net, n, pt)))
                        .Where(x => x.D <= 1500).OrderBy(x => x.D).Select(x => (int?)x.Node).FirstOrDefault())
                    .Where(n => n is not null).Select(n => n!.Value).Distinct().ToList();
                pickedParts.Add(picked.Count > 0);
                partOutlets.Add(picked.Count > 0 ? picked : new List<int> { MepRoutePlanner.DefaultOutlet(net, parts[p]) });
                outletNodes.AddRange(partOutlets[p]);
            }

            // Square corners drawn on the plan become 45° wyes and pairs of 45° bends (profile rule DR-BRANCH / DR-BEND).
            if (CombineProfileStore.Load(doc).DrainWyes)
            {
                var (wyes, bends, square) = MepRoutePlanner.DrainFittings(net, outletNodes);
                if (wyes + bends + square > 0)
                    notes.AppendLine($"Thoát nước: {wyes} nhánh nối chữ Y 45°, {bends} góc vuông thành 2 cút 45°" +
                                     (square > 0 ? $"; {square} chỗ quá ngắn / nhánh ngược dòng giữ góc vuông, cần xem lại." : "."));
                edgesAt = net.Nodes.Select(_ => new List<int>()).ToList();
                for (var i = 0; i < net.Edges.Count; i++)
                {
                    edgesAt[net.Edges[i].A].Add(i);
                    edgesAt[net.Edges[i].B].Add(i);
                }
            }

            var clashes = 0;
            var offFallCount = 0;
            var edgeZ = new Dictionary<MepEdge, (double A, double B)>();
            for (var p = 0; p < parts.Count; p++)
            {
                // Every run straight at the fall drawn on it (D110-1%), flowing one way, every joint at one height. The
                // head (the highest point) fixed: its pipe top the clearance under the beam or slab over it, else at the
                // height typed in; the rest follows down run by run (outlets may end at different heights).
                var outlets = partOutlets[p];
                var (runEnds, offFall) = MepRoutePlanner.RunRises(net, outlets, setup.SlopePercent);
                if (setup.Vent)
                    runEnds = MepRoutePlanner.VentRises(runEnds);
                offFallCount += offFall.Count;
                if (runEnds.Count == 0)
                    continue;
                // Pipe bottoms written at the run's ends (BOP=-0.50): a run with one at each of its two ends falls
                // straight from the one to the other; else the bottom at the outlet (or the head, or another end)
                // fixes the height and the drawn falls do the rest.
                var bottoms = MepRoutePlanner.EndBottoms(net, parts[p], texts);
                var partEnds = parts[p].Where(n => edgesAt[n].Count == 1).ToList();
                double EndDepth(int n) => edgesAt[n].Max(i => setup.Depth(net.Edges[i].Size));
                double RiseAt(int n) => runEnds.Where(kv => kv.Key.A == n || kv.Key.B == n)
                    .Select(kv => kv.Key.A == n ? kv.Value.A : kv.Value.B).DefaultIfEmpty(0).First();
                var bopNote = "";
                if (partEnds.Count == 2 && bottoms.Count == 2 && !setup.Vent)
                {
                    var low = bottoms.OrderBy(kv => kv.Value.BottomMm).First().Key;
                    var high = partEnds.First(n => n != low);
                    var along = MepRoutePlanner.DistancesFrom(net, low);
                    var fall = bottoms[high].BottomMm + EndDepth(high) / 2 - (bottoms[low].BottomMm + EndDepth(low) / 2);
                    var length = along.TryGetValue(high, out var lh) && lh > 0 ? lh : 1;
                    runEnds = runEnds.ToDictionary(kv => kv.Key,
                        kv => (along.TryGetValue(kv.Key.A, out var da) ? da / length * fall : 0, along.TryGetValue(kv.Key.B, out var db) ? db / length * fall : 0));
                    outlets = new List<int> { low };
                    bopNote = $"; cao độ theo BOP hai đầu ({bottoms[high].BottomMm:0} → {bottoms[low].BottomMm:0} mm, dốc {fall / length * 100:0.##}%)";
                }

                var top = runEnds.SelectMany(kv => new[] { (Node: kv.Key.A, Rise: kv.Value.A, Edge: kv.Key), (Node: kv.Key.B, Rise: kv.Value.B, Edge: kv.Key) })
                    .OrderByDescending(x => x.Rise).First();
                var headRise = top.Rise;
                var headDepth = edgesAt[top.Node].Max(i => setup.Depth(net.Edges[i].Size));
                var (hx, hy) = net.Nodes[top.Node];
                var under = setup.UnderStructure ? Under(hx, hy) : null;
                var headZ = under is double u ? u - setup.ClearanceMm - headDepth / 2 : setup.ManualCenter(levelMm, headDepth);
                var baseZ = headZ - headRise;
                var anchor = bottoms.Keys.OrderByDescending(n => outlets.Contains(n)).ThenByDescending(n => n == top.Node).Cast<int?>().FirstOrDefault();
                if (anchor is int an)
                {
                    baseZ = levelMm + bottoms[an].BottomMm + EndDepth(an) / 2 - RiseAt(an);
                    headZ = baseZ + headRise;
                    if (bopNote.Length == 0)
                    {
                        bopNote = $"; cao độ theo BOP tại ({net.Nodes[an].X:0},{net.Nodes[an].Y:0}) = {bottoms[an].BottomMm:0} mm";
                        var off = bottoms.Where(kv => kv.Key != an)
                            .Select(kv => (kv.Key, Diff: baseZ + RiseAt(kv.Key) - EndDepth(kv.Key) / 2 - levelMm - kv.Value.BottomMm))
                            .Where(x => Math.Abs(x.Diff) > 20).ToList();
                        if (off.Count > 0)
                            bopNote += "; lệch BOP ghi ở " + string.Join(", ", off.Select(x => $"({net.Nodes[x.Key].X:0},{net.Nodes[x.Key].Y:0}) {x.Diff:+0;-0} mm"));
                    }
                }
                foreach (var (e, r) in runEnds)
                {
                    edgeZ[e] = (baseZ + r.A, baseZ + r.B);
                    nodeZ[e.A] = baseZ + r.A;
                    nodeZ[e.B] = baseZ + r.B;
                }

                // Where a deeper beam further down the fall comes lower than the pipe: counted, to be looked at.
                if (setup.UnderStructure)
                {
                    foreach (var (e, r) in runEnds)
                    {
                        var (ax, ay) = net.Nodes[e.A];
                        var (bx, by) = net.Nodes[e.B];
                        var len = MepRoutePlanner.Length(net, e);
                        for (var s = 0.0; ; s += StepMm)
                        {
                            var t = len > 0 ? Math.Min(s, len) / len : 0;
                            var pipeTop = baseZ + r.A + (r.B - r.A) * t + setup.Depth(e.Size) / 2;
                            if (Under(ax + (bx - ax) * t, ay + (by - ay) * t) is double ub && pipeTop > ub)
                            {
                                clashes++;
                                break;
                            }

                            if (s >= len)
                                break;
                        }
                    }
                }

                var at = string.Join(", ", outlets.Select(o =>
                    $"({net.Nodes[o].X:0},{net.Nodes[o].Y:0}){(nodeZ.TryGetValue(o, out var oz) ? $" tâm {oz - levelMm:0}" : "")}"));
                notes.AppendLine($"Mạng {p + 1}: điểm đầu tuyến ({hx:0},{hy:0}) tâm ống {headZ - levelMm:0} mm so với {level.Name} " +
                                 $"({(anchor is not null ? "theo BOP ghi trên CAD" : under is null ? "cao độ tự nhập" : $"cách đáy dầm/sàn {setup.ClearanceMm:0} mm")}); {outlets.Count} điểm xả " +
                                 $"{(setup.DrainMode != DrainPickOutlets ? "(" + headWhy[p] + ")" : pickedParts[p] ? "đã pick" : "đầu ống lớn nhất")} {at} mm; dốc theo chữ trên tuyến (D110-1%, i=..%), chỗ không ghi {setup.SlopePercent:0.##}%{bopNote}.");
            }

            if (clashes > 0)
                notes.AppendLine($"Lỗi: {clashes} đoạn ống thoát nước chạm đáy dầm/sàn sâu hơn ở giữa tuyến (cao độ theo điểm đầu tuyến) — xem lại hoặc hạ điểm đầu.");
            if (offFallCount > 0)
                notes.AppendLine($"Lỗi: {offFallCount} đoạn khép mạch vòng, hai đầu không khớp độ dốc bản vẽ (dựng theo cao độ hai đầu) — xem lại.");

            foreach (var e in net.Edges)
            {
                var (za, zb) = edgeZ.TryGetValue(e, out var z) ? z : (nodeZ.TryGetValue(e.A, out var a) ? a : levelMm, nodeZ.TryGetValue(e.B, out var b) ? b : levelMm);
                pieces.Add(new List<Piece> { new(0, MepRoutePlanner.Length(net, e), za, zb) });
            }
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

            // Each duct at the typed height by its own depth (bottom 3200: every duct's bottom at 3200, whatever its
            // depth); a network under a lower beam goes down whole, by as much as its highest top needs.
            var byStructure = 0;
            var shift = new double[parts.Count];
            var highestTop = parts.Select(_ => double.MinValue).ToArray();
            foreach (var e in net.Edges.Where(x => !x.IsTransition))
            {
                var d = e.Size.DepthMm;
                highestTop[partOf[e.A]] = Math.Max(highestTop[partOf[e.A]], setup.ManualCenter(levelMm, d) + d / 2);
            }

            for (var p = 0; p < parts.Count; p++)
            {
                if (lowest[p] == double.MaxValue || highestTop[p] == double.MinValue)
                    continue;
                var room = lowest[p] - setup.ClearanceMm - highestTop[p];
                if (room >= 0)
                    continue;
                shift[p] = Math.Floor(room / 5) * 5;
                byStructure++;
            }

            var ductProfiles = new List<List<MepPiece>>();
            var cadEdge = new bool[net.Edges.Count];
            for (var i = 0; i < net.Edges.Count; i++)
            {
                var e = net.Edges[i];
                var cad = setup.CadCenter(levelMm, e);
                cadEdge[i] = cad is not null;
                if (cad is not null)
                    fromCad++;
                var z = cad ?? setup.ManualCenter(levelMm, e.Size.DepthMm) + shift[partOf[e.A]];
                ductProfiles.Add(new List<MepPiece> { new(0, MepRoutePlanner.Length(net, e), z) });
            }

            // Ducts of different depths on one bottom (or top) meet through their fitting; only a height written on
            // the CAD makes a riser, else each depth change would leave a 50 mm vertical stub.
            MepRoutePlanner.MeetAtNodes(net, edgesAt, ductProfiles, cadEdge);
            pieces.AddRange(ductProfiles.Select(pr => pr.Select(x => new Piece(x.From, x.To, x.Z, x.Z)).ToList()));
            notes.AppendLine(setup.UnderStructure
                ? $"Cao độ: mọi ống {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm so với {level.Name}; mạng có đáy dầm/sàn thấp hơn thì cả mạng hạ xuống, đỉnh ống cao nhất dưới đáy dầm thấp nhất {setup.ClearanceMm:0} mm ({byStructure}/{parts.Count} mạng hạ theo dầm)."
                : $"Cao độ: mọi ống {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm so với {level.Name}.");
            if (noStructure > 0)
                notes.AppendLine($"  {noStructure} điểm dò (mỗi {StepMm:0} mm) không thấy dầm/sàn phía trên.");
        }
        else if (setup.Run == MepRun.Busway)
        {
            // A busway is one straight bar per run: one height for the whole network, no dodging beams. The height
            // written on the CAD for any of its runs (the most written one), else the typed height, lowered whole under
            // the lowest beam or slab over the network where that comes lower.
            var cadZ = new double?[parts.Count];
            for (var p = 0; p < parts.Count; p++)
                cadZ[p] = MepRoutePlanner.MostWritten(net.Edges.Where(e => partOf[e.A] == p).Select(e => setup.CadCenter(levelMm, e)));

            var lowest = parts.Select(_ => double.MaxValue).ToArray();
            if (setup.UnderStructure)
            {
                foreach (var e in net.Edges.Where(e => cadZ[partOf[e.A]] is null))
                {
                    var (ax, ay) = net.Nodes[e.A];
                    var (bx, by) = net.Nodes[e.B];
                    var len = MepRoutePlanner.Length(net, e);
                    for (var s = 0.0; ; s += StepMm)
                    {
                        var t = len > 0 ? Math.Min(s, len) / len : 0;
                        if (Under(ax + (bx - ax) * t, ay + (by - ay) * t) is double u)
                            lowest[partOf[e.A]] = Math.Min(lowest[partOf[e.A]], u);
                        else
                            noStructure++;
                        if (s >= len)
                            break;
                    }
                }
            }

            var byStructure = 0;
            var runZ = new double[parts.Count];
            for (var p = 0; p < parts.Count; p++)
            {
                var manual = setup.ManualCenter(levelMm, depth[p]);
                runZ[p] = MepRoutePlanner.BuswayHeight(cadZ[p], manual, lowest[p] == double.MaxValue ? null : lowest[p],
                    setup.ClearanceMm, depth[p]);
                if (cadZ[p] is null && runZ[p] < manual)
                    byStructure++;
            }

            foreach (var e in net.Edges)
            {
                if (cadZ[partOf[e.A]] is not null)
                    fromCad++;
                pieces.Add(new List<Piece> { new(0, MepRoutePlanner.Length(net, e), runZ[partOf[e.A]], runZ[partOf[e.A]]) });
            }

            notes.AppendLine(setup.UnderStructure
                ? $"Cao độ: mỗi tuyến busway một cao độ (ghi trên CAD, hoặc {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm), không uốn né dầm; tuyến dưới dầm/sàn thấp hơn thì cả tuyến hạ xuống ({byStructure}/{parts.Count} tuyến)."
                : $"Cao độ: mỗi tuyến busway một cao độ (ghi trên CAD, hoặc {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm so với {level.Name}).");
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
                // The typed height exactly (a bottom typed 2600 is 2600, outside the pipe).
                var manualCenter = setup.ManualCenter(levelMm, netDepth);
                if (setup.CadCenter(levelMm, e) is double cad)
                {
                    fromCad++;
                    profiles.Add(new List<MepPiece> { new(0, len, cad) });
                    continue;
                }

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

            MepRoutePlanner.MeetAtNodes(net, edgesAt, profiles);
            pieces.AddRange(profiles.Select(pr => pr.Select(x => new Piece(x.From, x.To, x.Z, x.Z)).ToList()));
            notes.AppendLine(setup.UnderStructure
                ? $"Cao độ: đỉnh (theo đoạn sâu nhất của mạng) dưới đáy dầm/sàn − {setup.ClearanceMm:0} mm, uốn né dầm; chỗ không có dầm/sàn: {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm."
                : $"Cao độ: {setup.ManualRef.ToLowerInvariant()} {setup.ManualMm:0} mm so với {level.Name} (cả mạng một cao độ).");
            if (noStructure > 0)
                notes.AppendLine($"  {noStructure} điểm dò (mỗi {StepMm:0} mm) không thấy dầm/sàn phía trên: dùng cao độ tự nhập.");
        }

        if (fromCad > 0)
            notes.AppendLine($"Cao độ ghi trên CAD (H=…, {setup.CadRef.ToLowerInvariant()}): {fromCad}/{net.Edges.Count} đoạn; chỗ hai đoạn khác cao độ gặp nhau có đoạn đứng nối.");
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
        // Runs already modelled: only of the kind being made (a pipe or a flex crossing under a duct is not it). A
        // busway is drawn on a tray's centre line: a tray is not the busway already there, nor a busway the tray.
        string Service(MEPCurve c) => c.get_Parameter(BuiltInParameter.RBS_CTC_SERVICE_TYPE)?.AsString() ?? "";
        bool SameKind(MEPCurve c) => setup.Run switch
        {
            MepRun.Duct => c is Duct,
            MepRun.Busway => c is CableTray && string.Equals(Service(c), setup.ServiceType, StringComparison.OrdinalIgnoreCase),
            MepRun.CableTray => c is CableTray && !string.Equals(Service(c), BuswayService, StringComparison.OrdinalIgnoreCase),
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
        Reduced.Clear();
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

        // Junctions before corners: a main made one through a branch first leaves the elbow next to it a run long
        // enough (an elbow on the short piece between a corner and a branch close by takes more than its length).
        foreach (var n in Enumerable.Range(0, net.Nodes.Count).OrderBy(i => nodeEnds[i].Count >= 3 ? 0 : 1))
        {
            var ends = nodeEnds[n];
            if (ends.Count >= 2)
                jobs.Add(new FittingJob(Describe(ends[0].At), f => ends.Count >= 3 && !setup.JoinBranches
                    ? Unjoined(doc, ends.Select(x => End(doc, x.Curve, x.At)).ToList(), f)
                    : Join(doc, ends.Select(x => End(doc, x.Curve, x.At)).ToList(), f)));
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
        ReducersOnStart(doc, nodeEnds);

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

        // A run drawn through a junction (the piece after it deleted into it) can come out with System Type Undefined.
        var systemsFilled = 0;
        if (setup.Run is not (MepRun.CableTray or MepRun.Busway))
        {
            var known = beforeIds.ToHashSet();
            systemsFilled = MepSystemFill.Fill(doc, new FilteredElementCollector(doc).OfClass(typeof(MEPCurve))
                .Where(c => !known.Contains(c.Id)).Select(c => c.Id).ToList(), setup.SystemId);
            // Runs of an earlier run of the tool (kept where this one found them already modelled): only from what
            // they are joined to, they may be of another system.
            systemsFilled += MepSystemFill.Fill(doc, before.Where(c => c.IsValidObject && SameKind(c)).Select(c => c.Id).ToList(), byType: false);
        }

        group.Assimilate();

        var sb = new StringBuilder();
        sb.AppendLine($"Tuyến: {net.Edges.Count} đoạn, {net.Nodes.Count} nút, {parts.Count} mạng. Tạo: {curves} đoạn, {risers} đoạn đứng né dầm, {fittings} fitting.");
        if (!setup.JoinBranches)
            sb.AppendLine("Nhánh chưa nối vào tuyến chính: tuyến chính liền qua chỗ rẽ, nhánh dừng ở mép ống — nối sau khi Combine.");
        if (setup.Run == MepRun.DrainPipe)
            sb.AppendLine($"Điểm xả nối vào ống có sẵn: {joined}/{outletNodes.Count}.");
        if (skipped > 0)
            sb.AppendLine($"Đã có ống/máng trùng vị trí, bỏ qua: {skipped} đoạn.");
        if (systemsFilled > 0)
            sb.AppendLine($"Ống bị System Type Undefined được gán lại system: {systemsFilled}.");
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
                case MepRun.Busway:
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

    /// <summary>The nodes the sprinkler reducers were moved to (150 mm past the head / branch tee): where reducing starts.</summary>
    private static readonly List<int> ReducerNodes = new();

    /// <summary>
    /// Revit centres a transition on the joint it is made at, so the reducing starts short of it: each reducer at a
    /// moved node is slid along its run until its large end is on the node (150 mm from the head / branch), the runs
    /// at its ends drawn on or back with it.
    /// </summary>
    private static void ReducersOnStart(Document doc, List<List<(ElementId Curve, XYZ At)>> nodeEnds)
    {
        if (ReducerNodes.Count == 0)
            return;
        int slid = 0, failed = 0;
        var guard = new RollBackOnError();
        using var t = new Transaction(doc, "DSSTech reducers");
        var options = t.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(guard);
        options.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(options);
        t.Start();
        foreach (var n in ReducerNodes)
        {
            if (n >= nodeEnds.Count || nodeEnds[n].Count == 0)
                continue;
            var (curveId, at) = nodeEnds[n][0];
            if (doc.GetElement(curveId) is not MEPCurve curve)
                continue;
            var end = End(curve, at);
            if (end.AllRefs.Cast<Connector>().Select(c => c.Owner).OfType<FamilyInstance>().FirstOrDefault() is not { } reducer)
                continue;
            var ends = reducer.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End).ToList();
            if (ends is not { Count: 2 } || Math.Abs(ends[0].Radius - ends[1].Radius) < 1e-6)
                continue;
            var large = ends[0].Radius > ends[1].Radius ? ends[0] : ends[1];
            var move = new XYZ(at.X - large.Origin.X, at.Y - large.Origin.Y, 0);
            if (move.GetLength() < UnitConv.MmToInternal(1))
                continue;
            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                ElementTransformUtils.MoveElement(doc, reducer.Id, move);
                doc.Regenerate();
                sub.Commit();
                slid++;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                sub.RollBack();
                failed++;
                DssTechLog.Write($"mep fitting: reducer at {Describe(at)} not slid onto its start: {ex.Message}");
            }
        }

        if (t.Commit() != TransactionStatus.Committed || guard.Error is not null)
            DssTechLog.Write($"mep fitting: reducers not slid (Revit refused: {guard.Error}); left centred on the joint");
        else
            DssTechLog.Write($"mep fitting: reducers slid so reducing starts 150 mm past the head / branch: {slid}, failed {failed}");
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
    /// <summary>What <see cref="Round"/> needs of a connector, read before the elbow is tried (a refused try is undone, and the connector with it).</summary>
    private readonly record struct EndInfo(Domain Domain, XYZ Origin, double Width)
    {
        public static EndInfo Of(Connector c) =>
            new(c.Domain, c.Origin, c.Shape == ConnectorProfileType.Round ? 2 * c.Radius : c.Width);
    }

    private static void Round(FamilyInstance? elbow, EndInfo at)
    {
        if (elbow is null || at.Domain != Domain.DomainHvac || Reduced.Contains(elbow.Id))
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

        var width = at.Width;
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
                    {
                        var end = EndInfo.Of(at[0]);
                        Round(NewElbow(doc, at[0], at[1]), end);
                    }
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
                    try
                    {
                        doc.Create.NewTeeFitting(fresh[0], fresh[1], fresh[2]);
                        return 1;
                    }
                    catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
                    {
                        // A branch the type's tee does not take (a 45° wye on a drain, a sloped branch): another tee or
                        // wye family loaded in the project.
                        if (OtherFamily(doc, keys[0].Id, RoutingPreferenceRuleGroupType.Junctions, new[] { PartType.Tee, PartType.Wye, PartType.LateralTee },
                                () => doc.Create.NewTeeFitting(End(doc, keys[0].Id, keys[0].Origin), End(doc, keys[1].Id, keys[1].Origin),
                                    End(doc, keys[2].Id, keys[2].Origin)), where) is not null)
                            return 1;
                        // No fitting for the branch: the main still runs straight on as one duct, the branch ends at
                        // its side, to be joined by hand.
                        if (!MainOnly(doc, keys))
                            throw;
                        failures.Add($"node {where}: {ex.Message}; main kept as one run, branch to join by hand");
                        return 0;
                    }
                }
                case 4:
                {
                    var (m1, m2) = MostOpposite(at);
                    var rest = at.Where(c => c != m1 && c != m2).ToList();
                    // Read before trying: a fitting Revit refuses is undone, and the connectors read before go with it.
                    var keys = new[] { m1, m2, rest[0], rest[1] }.Select(c => (c.Owner.Id, c.Origin)).ToArray();
                    using (var sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            doc.Create.NewCrossFitting(m1, m2, rest[0], rest[1]);
                            sub.Commit();
                            return 1;
                        }
                        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
                        {
                            sub.RollBack();
                            LogJunctions(doc, doc.GetElement(keys[0].Id) is MEPCurve mc ? mc.GetTypeId() : ElementId.InvalidElementId);
                            return CrossByParts(doc, keys, where, ex.Message, failures);
                        }
                    }
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

    /// <summary>
    /// A junction left for after Combine: the two runs going straight on made one (the same size) or joined by a
    /// reducer, each branch drawn back to the main's side and left open. Returns the fittings made (the reducer).
    /// </summary>
    private static int Unjoined(Document doc, List<Connector> at, List<string> failures)
    {
        // The widest straight pair: at a cross the branches (DN32 up, DN50 down) are straight on too.
        static double Across(Connector c) => c.Shape == ConnectorProfileType.Round ? 2 * c.Radius : c.Width;
        var (i1, i2) = MepRoutePlanner.MainPair(at.Select(c =>
            (c.CoordinateSystem.BasisZ.X, c.CoordinateSystem.BasisZ.Y, c.CoordinateSystem.BasisZ.Z, Across(c))).ToList());
        var (m1, m2) = (at[i1], at[i2]);
        if (m1.CoordinateSystem.BasisZ.DotProduct(m2.CoordinateSystem.BasisZ) > -0.999)
            return 0; // no main straight on (a corner with a branch): left open as drawn
        var branches = at.Where(c => c != m1 && c != m2).Select(c => (c.Owner.Id, c.Origin)).ToList();
        var where = Describe(m1.Origin);
        using var sub = new SubTransaction(doc);
        sub.Start();
        try
        {
            MEPCurve main;
            var made = 0;
            if (SameSize(m1, m2) && m1.Owner is MEPCurve a && m2.Owner is MEPCurve b && a.Id != b.Id)
                main = Through(doc, a, m1, b, m2);
            else
            {
                Lengthen(doc, doc.Create.NewTransitionFitting(m1, m2), m1, m2);
                main = (MEPCurve)m1.Owner;
                made = 1;
            }

            foreach (var (id, origin) in branches)
            {
                try
                {
                    ToSide(doc, id, origin, main);
                }
                catch (ArgumentException)
                {
                    // A branch shorter than the main is wide stays as drawn.
                }
            }

            sub.Commit();
            return made;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
        {
            if (sub.HasStarted())
                sub.RollBack();
            failures.Add($"node {where}: main not made one ({ex.Message}); left open");
            return 0;
        }
    }

    /// <summary>
    /// A cross Revit has no fitting for (no Cross in the type's routing preferences, or none for these sizes): the main
    /// made one run through the node and both branches joined by taps; where the type has no tap, a tee for one branch
    /// and the other left at the main's side to join by hand. Returns the fittings made.
    /// </summary>
    private static int CrossByParts(Document doc, (ElementId Id, XYZ Origin)[] keys, string where, string why, List<string> failures)
    {
        using (var sub = new SubTransaction(doc))
        {
            sub.Start();
            try
            {
                var (m1, m2) = (End(doc, keys[0].Id, keys[0].Origin), End(doc, keys[1].Id, keys[1].Origin));
                if (m1.Owner is MEPCurve main && m2.Owner is MEPCurve next && main.Id != next.Id && SameSize(m1, m2))
                {
                    var kept = Through(doc, main, m1, next, m2);
                    Tap(doc, keys[2].Id, keys[2].Origin, kept, turn: false);
                    Tap(doc, keys[3].Id, keys[3].Origin, kept, turn: false);
                    sub.Commit();
                    DssTechLog.Write($"mep fitting: node {where}: no cross ({why}); main through, branches by taps");
                    return 2;
                }

                sub.RollBack();
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
            {
                if (sub.HasStarted())
                    sub.RollBack();
            }
        }

        try
        {
            var fresh = keys.Take(3).Select(k => End(doc, k.Id, k.Origin)).ToArray();
            doc.Create.NewTeeFitting(fresh[0], fresh[1], fresh[2]);
            failures.Add($"node {where} (4 runs): {why}; tee for one branch, the other to join by hand");
            return 1;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
        {
            failures.Add($"node {where} (4 runs): {why}; tee: {ex.Message}");
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
            var branchKey = (branch.Owner.Id, branch.Origin);
            var kept = Through(doc, main, m1, next, m2);
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
    /// The main made one run through a junction: the run after it deleted, this one drawn on to that one's far end and
    /// joined to what that end was joined to.
    /// </summary>
    private static MEPCurve Through(Document doc, MEPCurve main, Connector m1, MEPCurve next, Connector m2)
    {
        var start = Other(main, m1).Origin;
        var far = Other(next, m2);
        var end = far.Origin;
        var nextId = next.Id;
        // What the run being deleted joins beyond the branch, by owner and connector number: a connector read before
        // the delete is no longer valid after it.
        var beyond = far.AllRefs.Cast<Connector>().Where(c => c.Owner.Id != nextId).Select(c => (c.Owner.Id, c.Id)).ToList();
        var mainId = main.Id;
        doc.Delete(nextId);
        var kept = (MEPCurve)doc.GetElement(mainId);
        ((LocationCurve)kept.Location).Curve = Line.CreateBound(start, end);
        var keptEnd = End(kept, end);
        foreach (var (owner, id) in beyond)
        {
            if (Find(doc, owner, id) is { } c)
                keptEnd.ConnectTo(c);
        }

        Merged[nextId] = mainId;
        return kept;
    }

    /// <summary>
    /// A junction Revit gives no fitting for: the two mains, same size and straight on, made one duct (a straight run
    /// is not left broken), the branch drawn back to the main's side. False, nothing changed, when that does not apply.
    /// Straight on to within 0.25°: two drain runs at different falls (1% and 2%, half a degree apart) stay two, else
    /// the one run made of them takes a fall between (0.70% for 1% runs whose heights did not meet).
    /// </summary>
    private static bool MainOnly(Document doc, (ElementId Id, XYZ Origin)[] keys)
    {
        var mergedBefore = new Dictionary<ElementId, ElementId>(Merged);
        using var sub = new SubTransaction(doc);
        sub.Start();
        try
        {
            var (m1, m2) = (End(doc, keys[0].Id, keys[0].Origin), End(doc, keys[1].Id, keys[1].Origin));
            if (m1.Owner is not MEPCurve main || m2.Owner is not MEPCurve next || main.Id == next.Id ||
                m1.CoordinateSystem.BasisZ.DotProduct(m2.CoordinateSystem.BasisZ) > -0.99999 || !SameSize(m1, m2))
            {
                sub.RollBack();
                return false;
            }

            var kept = Through(doc, main, m1, next, m2);
            ToSide(doc, keys[2].Id, keys[2].Origin, kept);
            sub.Commit();
            return true;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
        {
            if (sub.HasStarted())
                sub.RollBack();
            Merged.Clear();
            foreach (var kv in mergedBefore)
                Merged[kv.Key] = kv.Value;
            return false;
        }
    }

    /// <summary>The branch's end drawn back from the main's axis to its side; returns the new end.</summary>
    private static XYZ ToSide(Document doc, ElementId branchId, XYZ branchEnd, MEPCurve main)
    {
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
        return side;
    }

    /// <summary>
    /// The shoe (tap) of the branch on the main. The branch's end comes at the main's axis; refused there, it is tried
    /// again with the branch drawn back to the main's side, where a branch drawn by hand ends.
    /// </summary>
    internal static void Tap(Document doc, ElementId branchId, XYZ branchEnd, MEPCurve main, bool turn = true)
    {
        FamilyInstance? tap = null;
        using (var first = new SubTransaction(doc))
        {
            first.Start();
            try
            {
                tap = doc.Create.NewTakeoffFitting(End(doc, branchId, branchEnd), main);
                first.Commit();
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                first.RollBack();
            }
        }

        if (tap is null)
        {
            var side = ToSide(doc, branchId, branchEnd, main);
            tap = doc.Create.NewTakeoffFitting(End((MEPCurve)doc.GetElement(branchId), side), main);
        }

        try
        {
            if (turn)
                TurnToDrawn(doc, tap, branchId, main);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
        {
            DssTechLog.Write($"mep fitting: shoe turn: {ex.Message}");
        }
    }

    /// <summary>The CAD strokes of the run being modelled, devices and shoes included.</summary>
    private static List<CadEntity> Drawn = new();

    /// <summary>
    /// A shoe whose slant Revit put on the other side from the drawing's (its 45° side faces the air coming along the
    /// main) is flipped, else turned half round about the branch, to the drawn side; left as made when neither does it.
    /// </summary>
    private static void TurnToDrawn(Document doc, FamilyInstance tap, ElementId branchId, MEPCurve main)
    {
        if (doc.GetElement(branchId) is not MEPCurve branch || branch.Location is not LocationCurve { Curve: Line bl } ||
            main.Location is not LocationCurve { Curve: Line ml })
            return;
        var mainDir = new XYZ(ml.Direction.X, ml.Direction.Y, 0);
        if (mainDir.GetLength() < 1e-6)
            return;
        mainDir = mainDir.Normalize();
        // Where the branch's axis meets the main's, in plan, and the branch's way out from it.
        var (b0, b1) = (bl.GetEndPoint(0), bl.GetEndPoint(1));
        var mainP = ml.GetEndPoint(0);
        XYZ Plan(XYZ p) => new(p.X, p.Y, 0);
        var near = Plan(b0 - mainP).CrossProduct(mainDir).GetLength() < Plan(b1 - mainP).CrossProduct(mainDir).GetLength() ? b0 : b1;
        var far = near.IsAlmostEqualTo(b0) ? b1 : b0;
        var outDir = Plan(far - near);
        if (outDir.GetLength() < 1e-6)
            return;
        outDir = outDir.Normalize();
        // near = node + outDir·dist, the node on the main's line: (near − mainP) × mainDir = dist · (outDir × mainDir).
        var cross = outDir.X * mainDir.Y - outDir.Y * mainDir.X;
        if (Math.Abs(cross) < 0.3)
            return;
        var dist = Plan(near - mainP).CrossProduct(mainDir).Z / cross;
        var node = Plan(near) - outDir * dist;
        static double Across(MEPCurve c)
        {
            var dia = c.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() ?? 0;
            return dia > 0 ? dia : c.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0;
        }
        var wanted = MepRoutePlanner.ShoeSide(Drawn, UnitConv.ToMm(node.X), UnitConv.ToMm(node.Y), outDir.X, outDir.Y,
            mainDir.X, mainDir.Y, UnitConv.ToMm(Across(branch)), UnitConv.ToMm(Across(main)));
        if (wanted == 0)
            return;

        // The slant reaches farther from the branch's axis along the main than the square side does.
        int Slant()
        {
            double plus = 0, minus = 0;
            var opt = new Options { DetailLevel = ViewDetailLevel.Fine };
            foreach (var p in Vertices(tap.get_Geometry(opt)))
            {
                var s = Plan(p - node).DotProduct(mainDir);
                plus = Math.Max(plus, s);
                minus = Math.Max(minus, -s);
            }

            var gap = UnitConv.MmToInternal(10);
            return plus > minus + gap ? 1 : minus > plus + gap ? -1 : 0;
        }

        var made = Slant();
        if (made == 0 || made == wanted)
            return;
        var where = Describe(tap.GetTransform().Origin);
        var tries = new List<(string Name, Action Do)>();
        if (tap.CanFlipHand)
            tries.Add(("flip hand", () => tap.flipHand()));
        if (tap.CanFlipFacing)
            tries.Add(("flip facing", () => tap.flipFacing()));
        var axisAt = tap.GetTransform().Origin;
        tries.Add(("half turn", () => ElementTransformUtils.RotateElement(doc, tap.Id, Line.CreateBound(axisAt, axisAt + outDir), Math.PI)));
        foreach (var (name, act) in tries)
        {
            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                act();
                doc.Regenerate();
                if (Slant() == wanted && tap.MEPModel.ConnectorManager.Connectors.Cast<Connector>().All(c => c.IsConnected))
                {
                    sub.Commit();
                    return;
                }
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
            {
                DssTechLog.Write($"mep fitting: shoe at {where}: {name}: {ex.Message}");
            }

            sub.RollBack();
        }

        DssTechLog.Write($"mep fitting: shoe at {where}: slant on the other side from the drawing, family neither flips nor turns");
    }

    private static IEnumerable<XYZ> Vertices(GeometryElement? geometry)
    {
        if (geometry is null)
            yield break;
        foreach (var obj in geometry)
        {
            switch (obj)
            {
                case GeometryInstance gi:
                    foreach (var p in Vertices(gi.GetInstanceGeometry()))
                        yield return p;
                    break;
                case Solid solid:
                    foreach (Edge edge in solid.Edges)
                    {
                        foreach (var p in edge.Tessellate())
                            yield return p;
                    }

                    break;
            }
        }
    }

    /// <summary>Once a run: the junction fittings the duct type's routing preferences offer (a shoe needs a tap there).</summary>
    private static void LogJunctions(Document doc, ElementId typeId)
    {
        if (!JunctionsLogged.Add(typeId) || doc.GetElement(typeId) is not MEPCurveType type)
            return;
        try
        {
            var prefs = type.RoutingPreferenceManager;
            IEnumerable<string> Parts(RoutingPreferenceRuleGroupType group) => Enumerable.Range(0, prefs.GetNumberOfRules(group))
                .Select(i => doc.GetElement(prefs.GetRule(group, i).MEPPartId) as FamilySymbol)
                .Where(s => s is not null)
                .Select(s => $"{s!.FamilyName} : {s.Name} ({(PartType)s.Family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE).AsInteger()})");
            DssTechLog.Write($"mep type {type.Name}: preferred junction {prefs.PreferredJunctionType}; junctions: " +
                             $"{string.Join(" | ", Parts(RoutingPreferenceRuleGroupType.Junctions))}; crosses: {string.Join(" | ", Parts(RoutingPreferenceRuleGroupType.Crosses))}");
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"mep type {type.Name}: junctions not read ({ex.Message})");
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
        // The second try is undone whole when Revit refuses it too: the runs go back to the corner, never left short.
        using var second = new SubTransaction(doc);
        second.Start();
        try
        {
            for (var k = 0; k < 2; k++)
            {
                var back = Math.Min(tangent, 0.45 * away[k].Length);
                ((LocationCurve)runs[k].Location).Curve = Line.CreateBound(corner + away[k].Dir * back, away[k].Far);
            }

            var made = doc.Create.NewElbowFitting(End(runs[0], corner), End(runs[1], corner));
            second.Commit();
            return made;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
        {
            second.RollBack();
            if (Tight(doc, keys, out var tight))
                return tight;
            if (OtherFamily(doc, keys[0].Id, RoutingPreferenceRuleGroupType.Elbows, new[] { PartType.Elbow },
                    () => doc.Create.NewElbowFitting(End(doc, keys[0].Id, keys[0].Origin), End(doc, keys[1].Id, keys[1].Origin)),
                    Describe(corner)) is { } other)
                return other;
            var angles = "";
            try
            {
                if (doc.GetElement(keys[0].Id) is Pipe)
                {
                    var pipeSettings = PipeSettings.GetPipeSettings(doc);
                    angles = $"; pipe fitting angles {pipeSettings.FittingAngleUsage}" +
                             (pipeSettings.FittingAngleUsage == FittingAngleUsage.UseSpecificAngles
                                 ? " " + string.Join("/", pipeSettings.GetSpecificFittingAngles().Where(a => pipeSettings.GetSpecificFittingAngleStatus(a)).Select(a => (a * 180 / Math.PI).ToString("0")))
                                 : "") + ElbowFamilies(doc, keys[0].Id);
                    throw new ArgumentException($"{ex.Message} (bend {turn * 180 / Math.PI:0} deg, radius {UnitConv.ToMm(radius):0}{angles})");
                }

                var settings = DuctSettings.GetDuctSettings(doc);
                angles = $"; duct fitting angles {settings.FittingAngleUsage}" +
                         (settings.FittingAngleUsage == FittingAngleUsage.UseSpecificAngles
                             ? " " + string.Join("/", settings.GetSpecificFittingAngles().Where(a => settings.GetSpecificFittingAngleStatus(a)).Select(a => (a * 180 / Math.PI).ToString("0")))
                             : "");
            }
            catch (Exception)
            {
            }

            throw new ArgumentException($"{ex.Message} (bend {turn * 180 / Math.PI:0} deg, radius {UnitConv.ToMm(radius):0}{angles})");
        }
    }

    /// <summary>Elbows made smaller than drawn (no room for the drawn radius): left as made.</summary>
    private static readonly HashSet<ElementId> Reduced = new();

    /// <summary>
    /// Last tries for an elbow Revit refuses: the two ends the other way round, then a smaller radius (multiplier 0.75,
    /// then 0.5 of the elbow type the routing preferences give), put first in the preferences for this one elbow and
    /// taken out again. The run is joined either way; a smaller elbow is logged.
    /// </summary>
    private static bool Tight(Document doc, (ElementId Id, XYZ Origin)[] keys, out FamilyInstance made)
    {
        made = null!;
        var corner = keys[0].Origin;
        using (var swap = new SubTransaction(doc))
        {
            swap.Start();
            try
            {
                made = doc.Create.NewElbowFitting(End(doc, keys[1].Id, keys[1].Origin), End(doc, keys[0].Id, keys[0].Origin));
                swap.Commit();
                return true;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                swap.RollBack();
            }
        }

        // A smaller elbow through the routing preferences: ducts and pipes have them, trays take fittings from their type.
        if (doc.GetElement(keys[0].Id) is not (Duct or Pipe) || doc.GetElement(doc.GetElement(keys[0].Id).GetTypeId()) is not MEPCurveType curveType)
            return false;
        var prefs = curveType.RoutingPreferenceManager;
        var group = RoutingPreferenceRuleGroupType.Elbows;
        if (prefs.GetNumberOfRules(group) == 0 || doc.GetElement(prefs.GetRule(group, 0).MEPPartId) is not FamilySymbol elbowType)
            return false;
        foreach (var multiplier in new[] { 0.75, 0.5 })
        {
            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                var symbol = ElbowType(elbowType, multiplier);
                if (symbol is null)
                {
                    sub.RollBack();
                    return false;
                }

                if (!symbol.IsActive)
                    symbol.Activate();
                prefs.AddRule(group, new RoutingPreferenceRule(symbol.Id, "DSSTech"), 0);
                made = doc.Create.NewElbowFitting(End(doc, keys[0].Id, keys[0].Origin), End(doc, keys[1].Id, keys[1].Origin));
                prefs.RemoveRule(group, 0);
                sub.Commit();
                Reduced.Add(made.Id);
                DssTechLog.Write($"mep elbow {Describe(corner)}: no room for the drawn radius, made with radius multiplier {multiplier}");
                return true;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                sub.RollBack();
            }
        }

        return false;
    }

    /// <summary>
    /// A fitting Revit refuses with the family the type's routing preferences give first (a fixed 90° bend family asked
    /// for 45°, or for a bend between sloped pipes; a 90° tee asked for a 45° wye): every other pipe fitting family of
    /// that kind loaded in the project is tried, put first in the preferences for this one fitting and taken out again.
    /// Those already in the preferences first, then those whose name shares most with the first (the same maker and
    /// material). Null when none will do.
    /// </summary>
    private static FamilyInstance? OtherFamily(Document doc, ElementId curveId, RoutingPreferenceRuleGroupType group, PartType[] kinds,
        Func<FamilyInstance> make, string where)
    {
        if (doc.GetElement(curveId) is not Pipe pipe || doc.GetElement(pipe.GetTypeId()) is not PipeType type)
            return null;
        var prefs = type.RoutingPreferenceManager;
        var listed = Enumerable.Range(0, prefs.GetNumberOfRules(group)).Select(i => prefs.GetRule(group, i).MEPPartId).ToList();
        var first = listed.Count > 0 ? doc.GetElement(listed[0]) as FamilySymbol : null;
        var firstName = first?.Family.Name ?? "";
        static int Shared(string a, string b)
        {
            var n = 0;
            while (n < a.Length && n < b.Length && char.ToUpperInvariant(a[n]) == char.ToUpperInvariant(b[n]))
                n++;
            return n;
        }

        var candidates = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeFitting).OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(f => f.Id != first?.Id && f.Family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)?.AsInteger() is int k
                        && kinds.Contains((PartType)k))
            .OrderBy(f => listed.Contains(f.Id) ? 0 : 1)
            .ThenByDescending(f => Shared(f.Family.Name, firstName))
            .Take(16)
            .ToList();
        foreach (var symbol in candidates)
        {
            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                if (!symbol.IsActive)
                    symbol.Activate();
                prefs.AddRule(group, new RoutingPreferenceRule(symbol.Id, "DSSTech"), 0);
                var made = make();
                prefs.RemoveRule(group, 0);
                sub.Commit();
                DssTechLog.Write($"mep fitting {where}: {group} refused with {firstName}; made with {symbol.Family.Name} : {symbol.Name}");
                return made;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                sub.RollBack();
            }
        }

        return null;
    }

    /// <summary>The elbow families loaded (for the log, where none would do): name and whether in the preferences.</summary>
    private static string ElbowFamilies(Document doc, ElementId curveId)
    {
        var listed = doc.GetElement(doc.GetElement(curveId).GetTypeId()) is PipeType type
            ? Enumerable.Range(0, type.RoutingPreferenceManager.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows))
                .Select(i => type.RoutingPreferenceManager.GetRule(RoutingPreferenceRuleGroupType.Elbows, i).MEPPartId).ToHashSet()
            : new HashSet<ElementId>();
        var names = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeFitting).OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(f => f.Family.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)?.AsInteger() == (int)PartType.Elbow)
            .Select(f => $"{f.Family.Name}{(listed.Contains(f.Id) ? "*" : "")}").Distinct().Take(12);
        return "; elbow families (* in the type's preferences): " + string.Join(", ", names);
    }

    private static readonly string[] Multipliers = { "Radius Multiplier", "Bend Radius Multiplier", "Multiplier" };

    /// <summary>The type of the elbow's family with radius multiplier <paramref name="wanted"/> ("Standard R0.75"), made from <paramref name="symbol"/> when missing; null without a multiplier.</summary>
    private static FamilySymbol? ElbowType(FamilySymbol symbol, double wanted)
    {
        if (Multipliers.Select(symbol.LookupParameter).FirstOrDefault(q => q is { StorageType: StorageType.Double }) is not { } typeParam)
            return null;
        if (Math.Abs(typeParam.AsDouble() - wanted) < 0.03)
            return symbol;
        var name = $"{System.Text.RegularExpressions.Regex.Replace(symbol.Name, @" R\d+\.\d+$", "")} R{wanted.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";
        var doc = symbol.Document;
        var type = symbol.Family.GetFamilySymbolIds().Select(doc.GetElement).OfType<FamilySymbol>().FirstOrDefault(s => s.Name == name);
        if (type is null)
        {
            type = (FamilySymbol)symbol.Duplicate(name);
            type.LookupParameter(typeParam.Definition.Name)?.Set(wanted);
        }

        return type;
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
