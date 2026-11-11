using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>«Nối nhánh»: branches left open (by the PCCC / water / duct run, or by Combine) joined to their mains.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class BranchJoinCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        BranchJoinTool.Run(data.Application);
}

/// <summary>
/// Each open end of a pipe or duct (the selection, else the active view) heading across a main of its own system (as
/// wide or wider) close by, joined to it with no run's height changed: the branch drawn on along its own line and
/// slope to the main's axis; within the main's depth a tap on its side (a tee where the type has no tap), else an
/// elbow, a riser and a tap (or tee) on the main's top or bottom. A branch off the main's depth by less than those
/// fittings need, or a drain lower than its main, is left and reported. One transaction per branch.
/// </summary>
internal static class BranchJoinTool
{
    private const double ReachMm = 600;

    public static Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument;
        var doc = uidoc?.Document;
        var view = doc?.ActiveView;
        if (doc is null || uidoc is null || view is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        const string title = "DSSTech — Nối nhánh";
        var selected = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(Runs).Select(e => e.Id).ToList();
        var ids = selected.Count > 0
            ? selected
            : new FilteredElementCollector(doc, view.Id).WherePasses(new LogicalOrFilter(
                    new ElementClassFilter(typeof(Pipe)), new ElementClassFilter(typeof(Duct))))
                .Where(Runs).Select(e => e.Id).ToList();
        if (ids.Count == 0)
        {
            TaskDialog.Show(title, "Không có ống nước / ống gió nào (chọn ống, hoặc mở view có ống).");
            return Result.Cancelled;
        }

        var dlg = new ArchModelSetupWindow(title,
            $"{ids.Count} ống {(selected.Count > 0 ? "đang chọn" : "trong view")}. Đầu ống nhánh còn hở được nối vào ống chính cùng hệ gần nhất " +
            "(cỡ bằng hoặc lớn hơn), không đổi cao độ tuyến nào: ngang tâm thì tap / tê bên hông; cao hơn hoặc thấp hơn thì cút + ống đứng + tap / tê " +
            "trên đỉnh hoặc đáy ống chính. Thoát nước chỉ nối khi nhánh cao hơn ống chính.");
        var reach = dlg.AddInput("Đầu nhánh cách mép ống chính tối đa (mm, mặt bằng)", ReachMm.ToString("0"));
        if (!dlg.ShowFor(uiApp.MainWindowHandle))
            return Result.Cancelled;
        var reachMm = double.TryParse(reach.Text.Trim(), out var r) && r > 0 ? r : ReachMm;
        DoneReport.Show(title, Connect(doc, ids, reachMm));
        return Result.Succeeded;
    }

    private static bool Runs(Element e) =>
        e is Pipe or Duct && e.Location is LocationCurve { Curve: Line line } && Math.Abs(line.Direction.Z) < 0.5;

    public static string Connect(Document doc, List<ElementId> ids, double reachMm)
    {
        // The open ends, read once: each join re-reads what it needs, the runs change as it goes.
        var ends = ids.Select(doc.GetElement).OfType<MEPCurve>()
            .SelectMany(c => Open(c).Select(o => (c.Id, o.Origin))).ToList();
        var mains = new List<ElementId>(ids);
        var known = new Dictionary<ElementId, RunInfo>();
        int side = 0, risers = 0, offsets = 0, close = 0, uphill = 0;
        var notes = new List<string>();
        using var group = new TransactionGroup(doc, "DSSTech branch join");
        group.Start();
        // Runs with System Type Undefined (left so by an earlier tool or edit) take their system back first: a branch
        // or a main without one joins nothing.
        var systemsFilled = MepSystemFill.Fill(doc, ids);

        foreach (var (id, at) in ends)
        {
            if (doc.GetElement(id) is not MEPCurve { IsValidObject: true } branch ||
                Open(branch).FirstOrDefault(c => c.Origin.DistanceTo(at) < 1e-6) is null)
                continue;
            if (Nearest(doc, branch, at, mains, known, reachMm) is not { } target)
                continue;
            var (main, plan) = target;
            var where = $"{Label(branch)} #{branch.Id.Value} → #{main.Id.Value} tại {MepFromCad.Describe(new XYZ(UnitConv.MmToInternal(plan.X), UnitConv.MmToInternal(plan.Y), UnitConv.MmToInternal(plan.MainZ)))}";
            if (plan.Kind == BranchJoinKind.TooClose)
            {
                close++;
                notes.Add($"{where}: lệch cao độ {plan.RiseMm:0} mm, nhánh quá ngắn cho 2 cút 45° + tê");
                continue;
            }

            if (plan.Kind == BranchJoinKind.Uphill)
            {
                uphill++;
                notes.Add($"{where}: nhánh thoát nước thấp hơn ống chính {-plan.RiseMm:0} mm");
                continue;
            }

            var guard = new MepFromCad.RollBackOnError();
            using var tx = new Transaction(doc, "DSSTech branch join");
            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(guard);
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            tx.Start();
            try
            {
                if (Join(doc, branch, at, main, plan) is { } split)
                    mains.Add(split);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
            {
                tx.RollBack();
                notes.Add($"{where}: {ex.Message}");
                continue;
            }

            if (tx.Commit() == TransactionStatus.Committed && guard.Error is null)
            {
                if (plan.Kind == BranchJoinKind.Side)
                    side++;
                else if (plan.Kind == BranchJoinKind.Offset)
                    offsets++;
                else
                    risers++;
            }
            else
                notes.Add($"{where}: {guard.Error ?? "Revit không nhận nối"}");
        }

        group.Assimilate();
        var failed = notes.Count - close - uphill;
        var sb = new StringBuilder();
        sb.AppendLine($"Nhánh nối vào ống chính: {side + risers + offsets} ({side} tap / tê bên hông, {risers} qua ống đứng trên / dưới ống chính, " +
                      $"{offsets} lệch ít qua 2 cút 45° sát ống chính); cao độ các tuyến giữ nguyên.");
        if (systemsFilled > 0)
            sb.AppendLine($"Ống bị System Type Undefined được gán lại system: {systemsFilled}.");
        if (close > 0)
            sb.AppendLine($"Không nối được {close} nhánh: lệch cao độ với ống chính mà đoạn nhánh quá ngắn cho 2 cút 45° — nối tay (xem log).");
        if (uphill > 0)
            sb.AppendLine($"Không nối được {uphill} nhánh thoát nước: thấp hơn ống chính, nước không chảy vào được (xem log).");
        if (failed > 0)
            sb.AppendLine($"Không nối được {failed} nhánh: Revit không nhận phụ kiện (xem log).");
        foreach (var n in notes.Take(60))
            DssTechLog.Write($"branch join: {n}");
        return sb.ToString();
    }

    private static IEnumerable<Connector> Open(MEPCurve c) =>
        c.ConnectorManager.Connectors.Cast<Connector>().Where(x => x.ConnectorType == ConnectorType.End && !x.IsConnected).ToList();

    private static string Label(MEPCurve c) => c is Duct ? "ống gió" : "ống";

    /// <summary>What a run is, read once (system, size: a split keeps them; its line is read afresh each time).</summary>
    private sealed record RunInfo(Type Kind, double WidthMm, double DepthMm, MepSystemCode System, string Code, ElementId SystemType);

    private static RunInfo Read(MEPCurve c, Dictionary<ElementId, RunInfo> known)
    {
        if (known.TryGetValue(c.Id, out var r))
            return r;
        var (system, code) = CombineProfileStore.Identify(c);
        return known[c.Id] = new RunInfo(c.GetType(), UnitConv.ToMm(MepCombine.Width(c)), UnitConv.ToMm(MepCombine.Depth(c)), system, code, SystemType(c));
    }

    /// <summary>
    /// The main the branch end heads across: a run of the same kind and system, as wide or wider, not already joined
    /// to it; a side joint before a riser, then the nearest. Runs far off in plan are passed over before anything else
    /// is read of them.
    /// </summary>
    private static (MEPCurve Main, BranchJoin Plan)? Nearest(Document doc, MEPCurve branch, XYZ at, List<ElementId> mains,
        Dictionary<ElementId, RunInfo> known, double reachMm)
    {
        var reach = UnitConv.MmToInternal(reachMm + 2000);
        var self = Read(branch, known);
        var bl = (Line)((LocationCurve)branch.Location).Curve;
        var far = bl.GetEndPoint(0).DistanceTo(at) < bl.GetEndPoint(1).DistanceTo(at) ? bl.GetEndPoint(1) : bl.GetEndPoint(0);
        var (bw, bd) = (self.WidthMm, self.DepthMm);
        var gravity = self.System is MepSystemCode.DR or MepSystemCode.CND;
        var joined = branch.ConnectorManager.Connectors.Cast<Connector>().SelectMany(c => c.AllRefs.Cast<Connector>())
            .Select(c => c.Owner.Id).ToHashSet();
        (MEPCurve, BranchJoin)? best = null;
        var bestScore = double.MaxValue;
        foreach (var id in mains)
        {
            if (id == branch.Id || joined.Contains(id) || doc.GetElement(id) is not MEPCurve { IsValidObject: true } main ||
                main.GetType() != self.Kind || main.Location is not LocationCurve { Curve: Line ml } || Math.Abs(ml.Direction.Z) > 0.5)
                continue;
            var (a, b) = (ml.GetEndPoint(0), ml.GetEndPoint(1));
            if (PlanDistance(at, a, b) > reach)
                continue;
            var run = Read(main, known);
            var mw = run.WidthMm;
            if (mw < bw - 1 || !SameSystem(self, run))
                continue;
            var plan = BranchJoinPlanner.Plan(UnitConv.ToMm(far.X), UnitConv.ToMm(far.Y), UnitConv.ToMm(far.Z),
                UnitConv.ToMm(at.X), UnitConv.ToMm(at.Y), UnitConv.ToMm(at.Z),
                UnitConv.ToMm(a.X), UnitConv.ToMm(a.Y), UnitConv.ToMm(a.Z), UnitConv.ToMm(b.X), UnitConv.ToMm(b.Y), UnitConv.ToMm(b.Z),
                mw, run.DepthMm, bw, bd, reachMm, gravity, eccentric: branch is Duct);
            if (plan is not { } p)
                continue;
            var score = (p.Kind == BranchJoinKind.Side ? 0 : 10000) + Math.Abs(p.ReachMm) + 0.1 * Math.Abs(p.RiseMm);
            if (score >= bestScore)
                continue;
            bestScore = score;
            best = (main, p);
        }

        return best;
    }

    /// <summary>The same system group and company code (a run with no code takes any of its group); unknown, the same system type.</summary>
    private static bool SameSystem(RunInfo branch, RunInfo main)
    {
        if (branch.System == MepSystemCode.Unknown || main.System == MepSystemCode.Unknown)
            return branch.System == main.System && branch.SystemType == main.SystemType;
        return main.System == branch.System && (branch.Code.Length == 0 || main.Code.Length == 0 || main.Code == branch.Code);
    }

    private static double PlanDistance(XYZ p, XYZ a, XYZ b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var len2 = dx * dx + dy * dy;
        var t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
        var (qx, qy) = (a.X + dx * t - p.X, a.Y + dy * t - p.Y);
        return Math.Sqrt(qx * qx + qy * qy);
    }

    private static ElementId SystemType(MEPCurve c) =>
        (c is Duct
            ? c.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)
            : c.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM))?.AsElementId() ?? ElementId.InvalidElementId;

    /// <summary>The joint made; returns the new piece of a main split for a tee.</summary>
    private static ElementId? Join(Document doc, MEPCurve branch, XYZ at, MEPCurve main, BranchJoin plan)
    {
        XYZ P(double x, double y, double z) => new(UnitConv.MmToInternal(x), UnitConv.MmToInternal(y), UnitConv.MmToInternal(z));
        var end = P(plan.X, plan.Y, plan.BranchZ);
        var foot = P(plan.X, plan.Y, plan.MainZ);
        // The branch drawn on (or back) along its own line: its far end, height and slope stay.
        var line = (Line)((LocationCurve)branch.Location).Curve;
        var atStart = line.GetEndPoint(0).DistanceTo(at) < line.GetEndPoint(1).DistanceTo(at);
        var far = atStart ? line.GetEndPoint(1) : line.GetEndPoint(0);
        if (plan.Kind == BranchJoinKind.Offset)
        {
            // Two 45° elbows near the main: the branch at its own height (and slope) to the upper one, down (up) at 45°
            // to the lower one at the main's height, on level into the main's side.
            var run = new XYZ(end.X - far.X, end.Y - far.Y, 0);
            var u = run.Normalize();
            var (knee, rise) = (UnitConv.MmToInternal(plan.KneeMm), UnitConv.MmToInternal(Math.Abs(plan.RiseMm)));
            var upper = end - (end - far) * ((knee + rise) / run.GetLength());
            var lower = new XYZ(foot.X - u.X * knee, foot.Y - u.Y * knee, foot.Z);
            ((LocationCurve)branch.Location).Curve = atStart ? Line.CreateBound(upper, far) : Line.CreateBound(far, upper);
            var slant = Riser(doc, branch, upper, lower);
            var level = Riser(doc, branch, lower, foot);
            doc.Regenerate();
            doc.Create.NewElbowFitting(MepFromCad.End(branch, upper), MepFromCad.End(slant, upper));
            doc.Create.NewElbowFitting(MepFromCad.End(slant, lower), MepFromCad.End(level, lower));
            return OntoMain(doc, main, level.Id, foot);
        }

        ((LocationCurve)branch.Location).Curve = atStart ? Line.CreateBound(end, far) : Line.CreateBound(far, end);
        doc.Regenerate();
        if (plan.Kind == BranchJoinKind.Side)
            return OntoMain(doc, main, branch.Id, end);

        var riser = Riser(doc, branch, foot, end);
        doc.Regenerate();
        doc.Create.NewElbowFitting(MepFromCad.End(branch, end), MepFromCad.End(riser, end));
        return OntoMain(doc, main, riser.Id, foot);
    }

    /// <summary>A run like the branch (type, system, size) from a to b.</summary>
    private static MEPCurve Riser(Document doc, MEPCurve branch, XYZ a, XYZ b)
    {
        var system = SystemType(branch);
        if (system == ElementId.InvalidElementId)
            throw new ArgumentException("ống nhánh chưa có System Type");
        var level = branch.ReferenceLevel.Id;
        MEPCurve riser = branch is Duct
            ? Duct.Create(doc, system, branch.GetTypeId(), level, a, b)
            : Pipe.Create(doc, system, branch.GetTypeId(), level, a, b);
        foreach (var p in new[] { BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM,
                     BuiltInParameter.RBS_CURVE_WIDTH_PARAM, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM })
        {
            if (branch.get_Parameter(p) is { HasValue: true } from && from.AsDouble() > 0 && riser.get_Parameter(p) is { IsReadOnly: false } to)
                to.Set(from.AsDouble());
        }

        return riser;
    }

    /// <summary>The branch end on the main: a tap, or the main split there and a tee.</summary>
    private static ElementId? OntoMain(Document doc, MEPCurve main, ElementId branchId, XYZ end)
    {
        using (var sub = new SubTransaction(doc))
        {
            sub.Start();
            try
            {
                MepFromCad.Tap(doc, branchId, end, main, turn: false);
                sub.Commit();
                return null;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                sub.RollBack();
            }
        }

        // No tap in the type's routing preferences: the main split on its axis and a tee.
        var line = (Line)((LocationCurve)main.Location).Curve;
        var onAxis = line.Project(end).XYZPoint;
        var otherId = main is Duct
            ? MechanicalUtils.BreakCurve(doc, main.Id, onAxis)
            : PlumbingUtils.BreakCurve(doc, main.Id, onAxis);
        doc.Regenerate();
        var other = (MEPCurve)doc.GetElement(otherId);
        var branch = (MEPCurve)doc.GetElement(branchId);
        doc.Create.NewTeeFitting(MepFromCad.End(main, onAxis), MepFromCad.End(other, onAxis), MepFromCad.End(branch, end));
        return otherId;
    }
}
