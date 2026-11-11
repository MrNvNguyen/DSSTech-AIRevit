using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>How «Nối đầu phun» joins a head to its branch.</summary>
internal enum SprinklerJoint
{
    /// <summary>Concrete or open ceiling: a DN25 drop (an arm over where the head is off to the side), a reducer at the head.</summary>
    Rigid,

    /// <summary>Suspended or plasterboard ceiling: a DN20 nipple out of the branch's side, a listed flexible hose, a reducer at the head.</summary>
    Flexible
}

/// <summary>
/// «Nối đầu phun»: each sprinkler head with an open pipe connector joined to the branch pipe it faces (a pendent head
/// the pipe over it, an upright one the pipe under it), as installed:
/// rigid — a tee (tap) on the branch's bottom (top for an upright head) and a DN25 drop where the head is under it; an
/// arm over from the branch's side, an elbow and the drop where it is off to the side; the branch drawn on with an
/// elbow where the head is its last; a reducer onto a smaller head;
/// flexible — a DN20 nipple from a tee (tap) on the branch's side toward the head, a DN20 hose of one of the 1–3
/// lengths in use (700 / 1000 / 1200 / 1500 / 1800 mm) leaving it level and coming down square into the head, its
/// slack bowed to the side, the length in Comments; a reducer onto a DN15 head.
/// One transaction per head, so a head Revit refuses is left alone.
/// </summary>
internal static class SprinklerPipes
{
    /// <summary>How far off a branch (plan) a head still joins it.</summary>
    public const double ReachMm = 1500;

    /// <summary>The rigid drop and arm over (DN25, a reducing coupling at the head).</summary>
    private const double DropMm = 25;

    /// <summary>A rigid joint needs this much between the branch's axis and the head (tee or elbow, then the reducer).</summary>
    private const double MinDropMm = 100;

    /// <summary>An unsupported arm over in steel pipe longer than this takes a hanger (NFPA 13).</summary>
    private const double ArmHangerMm = 600;

    /// <summary>The flexible hose's nipple: this far out past the branch's side.</summary>
    private const double NippleMm = 100;

    /// <summary>The stub of the head's own size out of it, the reducer sits on.</summary>
    private const double StubMm = 100;

    /// <summary>Over the stub, a rigid drop's reducer and the tee's (elbow's) outlet take about this much.</summary>
    private const double ReducerRoomMm = 120;

    private const double MaxRiseMm = 3000;

    public static List<ElementId> OpenHeads(Document doc, Level level, Level topLevel)
    {
        var top = topLevel.Id == level.Id ? level.ProjectElevation + UnitConv.MmToInternal(6000) : topLevel.ProjectElevation;
        return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sprinklers).WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Where(f => f.Location is LocationPoint lp && lp.Point.Z >= level.ProjectElevation - 0.5 && lp.Point.Z < top)
            .Where(f => Open(f) is not null)
            .Select(f => f.Id).ToList();
    }

    /// <summary>«Nối đầu phun»: the selected heads (else every open head of the level), rigid or flexible.</summary>
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

        const string title = "DSSTech — Nối đầu phun";
        var ids = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
            .Where(f => f.Category?.Id.Value == (long)BuiltInCategory.OST_Sprinklers && Open(f) is not null).Select(f => f.Id).ToList();
        var scope = $"{ids.Count} đầu phun đang chọn";
        if (ids.Count == 0)
        {
            if (!ModelRevitLevelContext.TryResolve(view, doc, out var level, out var topLevel, out var levelErr))
            {
                TaskDialog.Show(title, levelErr);
                return Result.Cancelled;
            }

            ids = OpenHeads(doc, level, topLevel);
            scope = $"{ids.Count} đầu phun chưa nối trên level {level.Name}";
        }

        if (ids.Count == 0)
        {
            TaskDialog.Show(title, "Mọi đầu phun đã nối (chọn đầu phun, hoặc mở mặt bằng của level có đầu phun chưa nối).");
            return Result.Cancelled;
        }

        // The hose each head needs, read before asking: the lengths offered are the fewest that cover them.
        var pipes = new PipeLines(doc, FirePipes(doc));
        var runs = ids.Select(doc.GetElement).OfType<FamilyInstance>()
            .Select(h => Open(h) is { } c && Nearest(doc, pipes, c, ReachMm) is { } t ? Hose(c, t.Pipe, t.Drop, SprinklerHose.DiameterMm).RunMm : -1)
            .Where(r => r > 0).ToList();
        var proposed = SprinklerHose.PickSet(runs);
        var counts = string.Join(", ", proposed.Select(l => $"{l:0} mm × {runs.Count(r => SprinklerHose.Pick(r, proposed) == l)}"));

        var dlg = new ArchModelSetupWindow(title,
            $"{scope}. Ống cứng: tê dưới ống nhánh + ống đứng DN25 (lệch ống thì tay ngang từ hông ống + cút), côn thu tại đầu phun. " +
            $"Ống mềm: tê hông ống nhánh + nipple, ống mềm inox xuống thẳng đầu phun, côn thu tại đầu phun (không ghép 2 ống mềm). Đề xuất ống mềm: {counts}.", 500);
        var mode = dlg.AddCombo("Kiểu nối", new[] { FlexLabel, RigidLabel }, FlexLabel);
        var size = dlg.AddCombo("Cỡ ống mềm (và nipple)", new[] { "DN20", "DN25" }, "DN20");
        var lengths = dlg.AddInput("Chiều dài ống mềm dùng (mm, 1–3 loại trong 700 / 1000 / 1200 / 1500 / 1800)",
            string.Join(", ", proposed.Select(l => l.ToString("0"))));
        var align = dlg.AddInput("Căn đầu phun vào tim ống nhánh khi lệch ≤ (mm mặt bằng; 0 = không căn)", AlignMm.ToString("0"));
        dlg.Validate = () => (string)mode.SelectedItem == RigidLabel || Lengths(lengths.Text) is not null
            ? null
            : "Chiều dài ống mềm: 1 đến 3 loại trong 700, 1000, 1200, 1500, 1800 (mm), cách nhau bằng dấu phẩy.";
        if (!dlg.ShowFor(uiApp.MainWindowHandle))
            return Result.Cancelled;
        var joint = (string)mode.SelectedItem == RigidLabel ? SprinklerJoint.Rigid : SprinklerJoint.Flexible;
        var hoseMm = (string)size.SelectedItem == "DN25" ? 25 : SprinklerHose.DiameterMm;
        var alignMm = double.TryParse(align.Text.Trim(), out var am) && am >= 0 ? am : AlignMm;
        DoneReport.Show(title, Connect(doc, ids, joint, Lengths(lengths.Text) ?? proposed, hoseMm, alignMm));
        return Result.Succeeded;
    }

    private const string FlexLabel = "Ống mềm — trần thả / trần thạch cao";
    private const string RigidLabel = "Ống cứng — trần bê tông / trần hở";

    private static double[]? Lengths(string text)
    {
        var values = text.Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.TryParse(s, out var v) ? v : -1).Distinct().OrderBy(v => v).ToArray();
        return values.Length is >= 1 and <= 3 && values.All(v => SprinklerHose.LengthsMm.Contains(v)) ? values : null;
    }

    private static List<ElementId> FirePipes(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
            .Where(p => p.Location is LocationCurve { Curve: Line line } && Math.Abs(line.Direction.Z) < 1e-3 && Fire(p))
            .Select(p => p.Id).ToList();

    /// <summary>The fire pipes' lines, kept as the joints change them: a head looks at the ones near it in plan only.</summary>
    private sealed class PipeLines
    {
        private readonly Document _doc;
        public readonly Dictionary<ElementId, (XYZ A, XYZ B)> Lines = new();

        public PipeLines(Document doc, IEnumerable<ElementId> ids)
        {
            _doc = doc;
            foreach (var id in ids)
                Refresh(id);
        }

        public void Refresh(ElementId? id)
        {
            if (id is null)
                return;
            if (_doc.GetElement(id) is Pipe { IsValidObject: true, Location: LocationCurve { Curve: Line line } })
                Lines[id] = (line.GetEndPoint(0), line.GetEndPoint(1));
            else
                Lines.Remove(id);
        }

        /// <summary>Plan distance from the point to the nearest line, and that line's height above it (mm), any way up.</summary>
        public (double PlanMm, double RiseMm)? Closest(XYZ o)
        {
            (double, double)? best = null;
            foreach (var (a, b) in Lines.Values)
            {
                var d = UnitConv.ToMm(PlanDistance(o, a, b));
                if (best is null || d < best.Value.Item1)
                    best = (d, UnitConv.ToMm(a.Z - o.Z));
            }

            return best;
        }
    }

    private static double PlanDistance(XYZ p, XYZ a, XYZ b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var len2 = dx * dx + dy * dy;
        var t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
        var (qx, qy) = (a.X + dx * t - p.X, a.Y + dy * t - p.Y);
        return Math.Sqrt(qx * qx + qy * qy);
    }

    /// <summary>What a run of joints came to (kept only when its transaction stands).</summary>
    private sealed class Tally
    {
        public int Joined, NoPipe, TooClose, TooFar, LongArms, Failed, Aligned, Raised, Direct;
        public readonly List<string> Notes = new();
        public readonly List<double> Lengths = new();

        public void Add(Tally t)
        {
            (Joined, NoPipe, TooClose, TooFar, LongArms, Failed, Aligned, Raised, Direct) =
                (Joined + t.Joined, NoPipe + t.NoPipe, TooClose + t.TooClose, TooFar + t.TooFar, LongArms + t.LongArms, Failed + t.Failed,
                    Aligned + t.Aligned, Raised + t.Raised, Direct + t.Direct);
            Notes.AddRange(t.Notes);
            Lengths.AddRange(t.Lengths);
        }
    }

    private const int Batch = 20;

    /// <summary>A head this far off its branch (plan) or less is moved onto the branch's axis; further, an arm over.</summary>
    private const double AlignMm = 200;

    /// <summary>An upright head this close under its branch in plan (or within the align distance) is stood on it.</summary>
    private const double UnderMm = 50;

    public static string Connect(Document doc, IList<ElementId> headIds, SprinklerJoint joint, double[] lengthsMm,
        double hoseMm = SprinklerHose.DiameterMm, double alignMm = AlignMm)
    {
        List<FamilyInstance> Heads() => headIds.Select(doc.GetElement).OfType<FamilyInstance>()
            .Where(f => f.Category?.Id.Value == (long)BuiltInCategory.OST_Sprinklers && Open(f) is not null).ToList();
        var heads = Heads();
        if (heads.Count == 0)
            return "";
        // A head whose inlet is of another classification than the fire pipes (Hydronic Supply): Revit joins it with
        // "different system classification" and the pipes it joins can be left Undefined. Its family's pipe connectors
        // take the pipes' classification and the family is loaded again (instances and their values kept).
        var inlets = Inlets(doc, heads, FirePipes(doc));
        if (inlets.Count > 0)
            heads = Heads();
        var flexType = new FilteredElementCollector(doc).OfClass(typeof(FlexPipeType)).Cast<FlexPipeType>().FirstOrDefault();
        if (joint == SprinklerJoint.Flexible && flexType is null)
            return $"Lỗi: project chưa có type ống mềm (Flexible Pipe) — load type ống mềm DN{hoseMm:0} rồi chạy lại «Nối đầu phun», hoặc chọn nối ống cứng.";
        // A pipe with System Type Undefined: no drop or nipple can be drawn off it (a new pipe takes its system).
        var fire = FirePipes(doc);
        var systemsFilled = MepSystemFill.Fill(doc, fire, byType: false);

        var lines = new PipeLines(doc, fire);

        // Heads in batches, one transaction each (a transaction per head is slow); a batch Revit refuses on commit is
        // undone and done again a head at a time, so only the refused heads are left out.
        var total = new Tally();
        using var group = new TransactionGroup(doc, "DSSTech sprinkler joints");
        group.Start();
        for (var start = 0; start < heads.Count; start += Batch)
        {
            var batch = heads.Skip(start).Take(Batch).ToList();
            if (Joints(doc, batch, joint, lengthsMm, hoseMm, alignMm, flexType, lines) is { } done)
            {
                total.Add(done);
                continue;
            }

            foreach (var head in batch)
            {
                if (Joints(doc, new List<FamilyInstance> { head }, joint, lengthsMm, hoseMm, alignMm, flexType, lines) is { } one)
                    total.Add(one);
            }
        }

        group.Assimilate();
        var sb = new StringBuilder();
        sb.AppendLine($"Đầu phun nối {(joint == SprinklerJoint.Rigid ? "ống cứng" : "ống mềm")}: {total.Joined}/{heads.Count}.");
        if (total.Aligned > 0)
            sb.AppendLine($"Đầu phun căn vào tim ống nhánh (lệch ≤ {alignMm:0} mm): {total.Aligned}.");
        foreach (var i in inlets)
            sb.AppendLine($"Họ đầu phun sửa classification cổng nối cho khớp ống chữa cháy: {i}.");
        if (total.Direct > 0)
            sb.AppendLine($"Đầu phun sát ống nhánh nối thẳng vào ống (tap / tê, như Connect Into): {total.Direct}.");
        if (total.Raised > 0)
            sb.AppendLine($"Đầu phun quay lên đang nằm dưới ống nhánh được nâng lên đứng trên ống: {total.Raised}.");
        if (systemsFilled > 0)
            sb.AppendLine($"Ống bị System Type Undefined được gán lại system: {systemsFilled}.");
        if (joint == SprinklerJoint.Flexible)
            sb.AppendLine($"Ống mềm DN{hoseMm:0} dùng: " + string.Join(", ", lengthsMm.Select(l => $"{l:0} mm × {total.Lengths.Count(x => x == l)}")) + " (ghi trong Comments).");
        if (total.LongArms > 0)
            sb.AppendLine($"Tay ngang ống cứng dài hơn {ArmHangerMm:0} mm: {total.LongArms} chỗ — cần giá treo (NFPA 13).");
        if (total.NoPipe > 0)
            sb.AppendLine($"Không nối được {total.NoPipe} đầu phun: không có ống nhánh PCCC trong {ReachMm:0} mm theo hướng cổng nối (xem log: ống gần nhất).");
        if (total.TooClose > 0)
            sb.AppendLine($"Không nối được {total.TooClose} đầu phun bằng ống cứng: tim ống nhánh cách đầu phun < {MinDropMm:0} mm — dùng ống mềm hoặc hạ đầu phun (xem log).");
        if (total.TooFar > 0)
            sb.AppendLine($"Không nối được {total.TooFar} đầu phun: xa hơn ống mềm dài nhất đang dùng — thêm loại dài hơn (tối đa 1800) hoặc nối ống cứng (xem log).");
        if (total.Failed > 0)
            sb.AppendLine($"Không nối được {total.Failed} đầu phun: Revit không nhận phụ kiện (xem log).");
        foreach (var n in total.Notes.Take(300))
            DssTechLog.Write($"sprinkler joint: {n}");
        return sb.ToString();
    }

    /// <summary>
    /// The heads joined in one transaction, each in a sub-transaction of its own (one Revit refuses at once is undone
    /// alone). Null when the transaction would not commit: nothing of it stands, the pipe lines read again.
    /// </summary>
    private static Tally? Joints(Document doc, List<FamilyInstance> heads, SprinklerJoint joint, double[] lengthsMm, double hoseMm,
        double alignMm, FlexPipeType? flexType, PipeLines lines)
    {
        var tally = new Tally();
        var touched = new List<ElementId>();
        var guard = new MepFromCad.RollBackOnError();
        using var tx = new Transaction(doc, "DSSTech sprinkler joints");
        var options = tx.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(guard);
        options.SetClearAfterRollback(true);
        tx.SetFailureHandlingOptions(options);
        tx.Start();
        foreach (var head in heads)
        {
            if (!head.IsValidObject || Open(head) is not { } connector)
                continue;
            var name = $"{head.Symbol?.FamilyName} #{head.Id.Value}";
            // An upright head (inlet facing down) placed at the ceiling, under its branch: stood on the branch instead.
            var raise = false;
            var found = Nearest(doc, lines, connector, ReachMm);
            if (found is null && connector.CoordinateSystem.BasisZ.Z < -0.5 && Nearest(doc, lines, connector, ReachMm, flip: true) is { } over
                && over.Drop.OffsetMm <= Math.Max(alignMm, UnderMm))
                (found, raise) = (over, true);
            if (found is not { } target)
            {
                tally.NoPipe++;
                var near = lines.Closest(connector.Origin);
                tally.Notes.Add($"{name}: không có ống nhánh — ống PCCC gần nhất cách {near?.PlanMm:0} mm mặt bằng, cao hơn đầu phun {near?.RiseMm:0} mm");
                continue;
            }

            double? length = null;
            // A head close under its branch (a head with its own hose or drop to its inlet): joined straight into the pipe
            // by a tap or a tee, as Revit's Connect Into does.
            var direct = !raise && joint == SprinklerJoint.Rigid && target.ClearMm < MinDropMm && target.Drop.End == 0
                         && (!target.Drop.Arm || SprinklerDropPlanner.Aligns(target.Drop, alignMm));
            if (!raise && !direct && joint == SprinklerJoint.Rigid && target.ClearMm < MinDropMm)
            {
                tally.TooClose++;
                tally.Notes.Add($"{name}: cách tim ống nhánh {target.ClearMm:0} mm, không đủ chỗ tê cho ống cứng");
                continue;
            }

            if (joint == SprinklerJoint.Flexible && !raise)
            {
                var run = Hose(connector, target.Pipe, target.Drop, hoseMm).RunMm;
                length = SprinklerHose.Pick(run, lengthsMm);
                if (length is null)
                {
                    tally.TooFar++;
                    tally.Notes.Add($"{name}: cần ống mềm {run:0} mm, dài hơn loại dài nhất đang dùng {lengthsMm.Max():0} mm");
                    continue;
                }
            }

            var mainId = target.Pipe.Id;
            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                // A head a little off its branch onto the branch's axis: the heads of a branch in line with it.
                var aligned = false;
                if (raise)
                {
                    var o = connector.Origin;
                    var axis = ((Line)((LocationCurve)target.Pipe.Location).Curve).GetEndPoint(0).Z;
                    var z = axis + UnitConv.MmToInternal(SprinklerDropPlanner.UprightRiseMm(UnitConv.ToMm(Outer(target.Pipe)), MinDropMm));
                    aligned = SprinklerDropPlanner.Aligns(target.Drop, alignMm);
                    var move = aligned
                        ? new XYZ(UnitConv.MmToInternal(target.Drop.TapX) - o.X, UnitConv.MmToInternal(target.Drop.TapY) - o.Y, z - o.Z)
                        : new XYZ(0, 0, z - o.Z);
                    ElementTransformUtils.MoveElement(doc, head.Id, move);
                    doc.Regenerate();
                    if (Open(head) is not { } moved || Nearest(doc, lines, moved, ReachMm) is not { } again)
                        throw new InvalidOperationException("nâng đầu phun quay lên lên trên ống nhánh rồi không còn thấy ống");
                    (connector, target) = (moved, again);
                    if (joint == SprinklerJoint.Flexible)
                    {
                        var run = Hose(connector, target.Pipe, target.Drop, hoseMm).RunMm;
                        length = SprinklerHose.Pick(run, lengthsMm)
                                 ?? throw new InvalidOperationException($"cần ống mềm {run:0} mm, dài hơn loại dài nhất đang dùng");
                    }
                }
                else if (SprinklerDropPlanner.Aligns(target.Drop, alignMm))
                {
                    var o = connector.Origin;
                    var move = new XYZ(UnitConv.MmToInternal(target.Drop.TapX) - o.X, UnitConv.MmToInternal(target.Drop.TapY) - o.Y, 0);
                    ElementTransformUtils.MoveElement(doc, head.Id, move);
                    doc.Regenerate();
                    if (Open(head) is not { } moved || Nearest(doc, lines, moved, ReachMm) is not { } again)
                        throw new InvalidOperationException("căn vào tim ống nhánh rồi không còn thấy ống");
                    (connector, target) = (moved, again);
                    if (length is not null)
                        length = SprinklerHose.Pick(Hose(connector, target.Pipe, target.Drop, hoseMm).RunMm, lengthsMm) ?? length;
                    aligned = true;
                }

                var split = length is double l
                    ? JoinFlex(doc, head, target.Pipe, target.Drop, flexType!, l, hoseMm)
                    : direct
                        ? Direct(doc, head, target.Pipe, target.Drop)
                        : JoinRigid(doc, head, target.Pipe, target.Drop);
                sub.Commit();
                tally.Joined++;
                if (aligned)
                    tally.Aligned++;
                if (raise)
                    tally.Raised++;
                if (direct)
                    tally.Direct++;
                if (length is double used)
                    tally.Lengths.Add(used);
                else if (target.Drop.Arm && target.Drop.OffsetMm > ArmHangerMm)
                    tally.LongArms++;
                lines.Refresh(mainId);
                lines.Refresh(split);
                touched.Add(mainId);
                if (split is not null)
                    touched.Add(split);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
            {
                sub.RollBack();
                tally.Failed++;
                tally.Notes.Add($"{name}: {ex.Message}");
            }
        }

        if (tx.Commit() == TransactionStatus.Committed && guard.Error is null)
            return tally;
        if (heads.Count == 1)
        {
            // A single head refused on commit: counted, the next one goes on.
            foreach (var id in touched)
                lines.Refresh(id);
            var t = new Tally { Failed = 1 };
            t.Notes.Add($"{heads[0].Symbol?.FamilyName} #{heads[0].Id.Value}: {guard.Error ?? "Revit không nhận nối"}");
            return t;
        }

        foreach (var id in touched)
            lines.Refresh(id);
        return null;
    }

    private static Connector? Open(FamilyInstance head) =>
        head.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>()
            .FirstOrDefault(c => c.Domain == Domain.DomainPiping && c.ConnectorType == ConnectorType.End && !c.IsConnected);

    /// <summary>A fire protection pipe, or one of no system yet.</summary>
    private static bool Fire(Pipe pipe)
    {
        var classification = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
        return classification.Length == 0 || classification.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// The branch the head faces (over a pendent head, under an upright one): nearest in plan, then nearest in height.
    /// ClearMm: from the pipe's axis to the head's connector, up or down. Lines far off in plan are passed over unread.
    /// </summary>
    /// <param name="flip">The pipe on the side the connector faces away from (an upright head under its branch).</param>
    private static (Pipe Pipe, SprinklerDrop Drop, double ClearMm)? Nearest(Document doc, PipeLines lines, Connector head, double reachMm,
        bool flip = false)
    {
        var o = head.Origin;
        var reach = UnitConv.MmToInternal(reachMm + 50);
        // A connector facing sideways (a hose end) takes the nearer pipe above or below.
        var facing = Math.Abs(head.CoordinateSystem.BasisZ.Z) < 0.5 ? 0 : Math.Sign(head.CoordinateSystem.BasisZ.Z);
        if (flip)
            facing = -facing;
        (Pipe, SprinklerDrop, double)? best = null;
        var bestScore = double.MaxValue;
        foreach (var kv in lines.Lines)
        {
            var (a, b) = kv.Value;
            var riseMm = UnitConv.ToMm(a.Z - o.Z);
            if ((facing != 0 && riseMm * facing <= 0) || Math.Abs(riseMm) > MaxRiseMm || PlanDistance(o, a, b) > reach)
                continue;
            if (doc.GetElement(kv.Key) is not Pipe { IsValidObject: true } pipe)
                continue;
            var drop = SprinklerDropPlanner.Plan(UnitConv.ToMm(a.X), UnitConv.ToMm(a.Y), UnitConv.ToMm(b.X), UnitConv.ToMm(b.Y),
                UnitConv.ToMm(o.X), UnitConv.ToMm(o.Y), reachMm, OpenEnd(pipe, a), OpenEnd(pipe, b));
            if (drop is not { } d)
                continue;
            var score = d.OffsetMm + 0.1 * Math.Abs(riseMm);
            if (score >= bestScore)
                continue;
            bestScore = score;
            best = (pipe, d, Math.Abs(riseMm));
        }

        return best;
    }

    private static bool OpenEnd(Pipe pipe, XYZ at) => !MepFromCad.End(pipe, at).IsConnected;

    private static double Outer(Pipe pipe) =>
        pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)?.AsDouble() is double d && d > 0 ? d : pipe.Diameter;

    private static ElementId SystemType(Pipe pipe) =>
        pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;

    /// <summary>
    /// The flexible joint laid out: the tap on the branch's axis (clear of its ends), the way out of its side toward
    /// the head (either side for a head under it), the nipple's end, the hose's seat over the head (on the reducer's
    /// stub for a smaller head), and the hose that run needs (along, then down).
    /// </summary>
    private static (XYZ Tap, XYZ Out, XYZ NippleEnd, XYZ Seat, bool Reduce, double RunMm) Hose(Connector head, Pipe main, SprinklerDrop plan,
        double hoseMm)
    {
        var o = head.Origin;
        var line = (Line)((LocationCurve)main.Location).Curve;
        var a = line.GetEndPoint(0);
        var margin = UnitConv.MmToInternal(100);
        var wanted = new XYZ(UnitConv.MmToInternal(plan.TapX), UnitConv.MmToInternal(plan.TapY), a.Z);
        var t = Math.Max(margin, Math.Min(line.Length - margin, (wanted - a).DotProduct(line.Direction)));
        var tap = a + line.Direction * t;
        var square = new XYZ(-line.Direction.Y, line.Direction.X, 0).Normalize();
        var toHead = new XYZ(o.X - tap.X, o.Y - tap.Y, 0);
        var way = toHead.DotProduct(square) < 0 ? -square : square;
        var nippleEnd = tap + way * (Outer(main) / 2 + UnitConv.MmToInternal(NippleMm));
        var reduce = UnitConv.ToMm(head.Radius * 2) < hoseMm - 1;
        var seat = reduce ? o + head.CoordinateSystem.BasisZ * UnitConv.MmToInternal(StubMm) : o;
        var run = new XYZ(seat.X - nippleEnd.X, seat.Y - nippleEnd.Y, 0).GetLength() + Math.Abs(seat.Z - nippleEnd.Z);
        return (tap, way, nippleEnd, seat, reduce, UnitConv.ToMm(run));
    }

    /// <summary>
    /// The flexible joint: the nipple out of a tap (tee) on the branch's side, the hose of the given length off it
    /// level and into the head square, its slack bowed to the side; a stub and a reducer onto a smaller head.
    /// Returns the new piece of a branch split for a tee.
    /// </summary>
    private static ElementId? JoinFlex(Document doc, FamilyInstance head, Pipe main, SprinklerDrop plan, FlexPipeType flexType,
        double lengthMm, double hoseMm)
    {
        var connector = Open(head)!;
        var (tap, way, nippleEnd, seat, reduce, _) = Hose(connector, main, plan, hoseMm);
        var level = main.ReferenceLevel.Id;
        var systemType = SystemType(main);
        var hoseDia = UnitConv.MmToInternal(hoseMm);

        var nipple = Pipe.Create(doc, systemType, main.GetTypeId(), level, tap, nippleEnd);
        nipple.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(hoseDia);
        doc.Regenerate();

        var mm = UnitConv.MmToInternal(1);
        var points = new List<XYZ> { nippleEnd };
        var line = (Line)((LocationCurve)main.Location).Curve;
        if (SprinklerHose.Bow(nippleEnd.X / mm, nippleEnd.Y / mm, nippleEnd.Z / mm, seat.X / mm, seat.Y / mm, seat.Z / mm,
                lengthMm, line.Direction.X, line.Direction.Y) is { } bow)
            points.Add(new XYZ(bow.X * mm, bow.Y * mm, bow.Z * mm));
        points.Add(seat);
        var hose = FlexPipe.Create(doc, systemType, flexType.Id, level, way, -connector.CoordinateSystem.BasisZ, points);
        hose.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(hoseDia);
        hose.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"Ống mềm DN{hoseMm:0} L={lengthMm:0}");
        doc.Regenerate();
        var ends = hose.ConnectorManager.Connectors.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End).ToList();
        ends.OrderBy(c => c.Origin.DistanceTo(nippleEnd)).First().ConnectTo(MepFromCad.End(nipple, nippleEnd));
        var hoseEnd = ends.OrderBy(c => c.Origin.DistanceTo(seat)).First();
        if (reduce)
        {
            var stub = Pipe.Create(doc, main.GetTypeId(), level, connector, seat);
            if (systemType != ElementId.InvalidElementId)
                stub.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.Set(systemType);
            doc.Regenerate();
            doc.Create.NewTransitionFitting(MepFromCad.End(stub, seat), hoseEnd);
        }
        else
            hoseEnd.ConnectTo(connector);
        return OntoMain(doc, main, MepFromCad.End(nipple, tap), tap);
    }

    /// <summary>The branch end on the main's axis: a tap, or the main split there and a tee.</summary>
    private static ElementId? OntoMain(Document doc, Pipe main, Connector branch, XYZ tap)
    {
        using (var sub = new SubTransaction(doc))
        {
            sub.Start();
            try
            {
                doc.Create.NewTakeoffFitting(branch, main);
                sub.Commit();
                return null;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                sub.RollBack();
            }
        }

        // No tap in the type's routing preferences: the main split there and a tee.
        var (branchOwner, branchAt) = (branch.Owner.Id, branch.Origin);
        var otherId = PlumbingUtils.BreakCurve(doc, main.Id, tap);
        doc.Regenerate();
        var other = (Pipe)doc.GetElement(otherId);
        var manager = doc.GetElement(branchOwner) switch
        {
            MEPCurve c => c.ConnectorManager,
            FamilyInstance f => f.MEPModel?.ConnectorManager,
            _ => null
        } ?? throw new InvalidOperationException("mất cổng nối nhánh");
        var fresh = manager.Connectors.Cast<Connector>()
            .Where(c => c.ConnectorType == ConnectorType.End).OrderBy(c => c.Origin.DistanceTo(branchAt)).First();
        doc.Create.NewTeeFitting(MepFromCad.End(main, tap), MepFromCad.End(other, tap), fresh);
        return otherId;
    }

    /// <summary>The families of the heads whose inlet is not of the fire pipes' classification, set to it; what was set.</summary>
    private static List<string> Inlets(Document doc, List<FamilyInstance> heads, List<ElementId> pipes)
    {
        var done = new List<string>();
        var target = pipes.Select(doc.GetElement).OfType<Pipe>()
            .Select(p => doc.GetElement(SystemType(p)) as PipingSystemType).Where(t => t is not null)
            .GroupBy(t => t!.SystemClassification).OrderByDescending(g => g.Count()).Select(g => (MEPSystemClassification?)g.Key)
            .FirstOrDefault();
        if (target is not { } fire || !fire.ToString().StartsWith("FireProtect", StringComparison.Ordinal))
            return done;
        // By id, one head of each family: a family loaded again leaves the objects read before it no longer valid.
        var samples = heads.Where(h => h.Symbol?.Family is not null).GroupBy(h => h.Symbol!.Family.Id)
            .Select(g => (Family: g.Key, Head: g.First().Id)).ToList();
        foreach (var (familyId, headId) in samples)
        {
            if (doc.GetElement(familyId) is not Family family || doc.GetElement(headId) is not FamilyInstance head)
                continue;
            if (Open(head) is not { } inlet || inlet.PipeSystemType.ToString() == fire.ToString() || !family.IsEditable)
                continue;
            var was = inlet.PipeSystemType.ToString();
            var name = family.Name;
            Document? fdoc = null;
            try
            {
                fdoc = doc.EditFamily(family);
                using (var t = new Transaction(fdoc, "DSSTech sprinkler inlet"))
                {
                    t.Start();
                    foreach (var c in new FilteredElementCollector(fdoc).OfClass(typeof(ConnectorElement)).Cast<ConnectorElement>()
                                 .Where(c => c.Domain == Domain.DomainPiping))
                        c.SystemClassification = fire;
                    t.Commit();
                }

                fdoc.LoadFamily(doc, new KeepValues());
                done.Add($"{name} ({was} → {fire})");
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
            {
                DssTechLog.Write($"sprinkler joint: {name}: inlet classification not changed: {ex.Message}");
            }
            finally
            {
                fdoc?.Close(false);
            }
        }

        foreach (var d in done)
            DssTechLog.Write($"sprinkler joint: family inlet {d}");
        return done;
    }

    private sealed class KeepValues : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = false;
            return true;
        }
    }

    /// <summary>The head's inlet straight into the branch over it: a tap, else the branch split and a tee.</summary>
    private static ElementId? Direct(Document doc, FamilyInstance head, Pipe main, SprinklerDrop plan)
    {
        var connector = Open(head)!;
        var z = ((Line)((LocationCurve)main.Location).Curve).GetEndPoint(0).Z;
        return OntoMain(doc, main, connector, new XYZ(UnitConv.MmToInternal(plan.TapX), UnitConv.MmToInternal(plan.TapY), z));
    }

    /// <summary>
    /// The rigid joint: a DN25 drop (the branch's size where it is the last head, off its elbow) from the tap, or from
    /// an arm over out of the branch's side and an elbow; a stub of the head's size and a reducer where the head is
    /// smaller. Returns the new piece of a branch split for a tee.
    /// </summary>
    private static ElementId? JoinRigid(Document doc, FamilyInstance head, Pipe main, SprinklerDrop plan)
    {
        var connector = Open(head)!;
        var o = connector.Origin;
        var line = (Line)((LocationCurve)main.Location).Curve;
        var z = line.GetEndPoint(0).Z;
        var tap = new XYZ(UnitConv.MmToInternal(plan.TapX), UnitConv.MmToInternal(plan.TapY), z);
        var level = main.ReferenceLevel.Id;
        var systemType = SystemType(main);

        // The last head of a branch: the pipe drawn on (or back) to over it, the drop its size off an elbow.
        if (plan.End != 0)
        {
            var (a, b) = (line.GetEndPoint(0), line.GetEndPoint(1));
            ((LocationCurve)main.Location).Curve = plan.End == 2 ? Line.CreateBound(a, tap) : Line.CreateBound(tap, b);
            doc.Regenerate();
        }

        var dropDia = plan.End != 0 ? main.Diameter : Math.Min(main.Diameter, UnitConv.MmToInternal(DropMm));
        // A drop and a reducer need the tee's (elbow's) outlet, the reducer and the head's stub between the branch and the
        // head; with less (a branch just over the ceiling) the drop is the head's own size off a reducing tee.
        var room = Outer(main) / 2 + UnitConv.MmToInternal(ReducerRoomMm + StubMm);
        if (Math.Abs(z - o.Z) < room)
            dropDia = connector.Radius * 2;
        var top = plan.Arm ? new XYZ(o.X, o.Y, z) : tap;
        var reduce = connector.Radius * 2 < dropDia - UnitConv.MmToInternal(1);
        var seat = reduce ? o + connector.CoordinateSystem.BasisZ * UnitConv.MmToInternal(StubMm) : o;
        var drop = Pipe.Create(doc, systemType, main.GetTypeId(), level, top, seat);
        drop.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(dropDia);
        doc.Regenerate();
        if (reduce)
        {
            var stub = Pipe.Create(doc, main.GetTypeId(), level, connector, seat);
            if (systemType != ElementId.InvalidElementId)
                stub.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.Set(systemType);
            doc.Regenerate();
            doc.Create.NewTransitionFitting(MepFromCad.End(stub, seat), MepFromCad.End(drop, seat));
        }
        else
            MepFromCad.End(drop, seat).ConnectTo(connector);

        var branch = MepFromCad.End(drop, top);
        if (plan.Arm)
        {
            var arm = Pipe.Create(doc, systemType, main.GetTypeId(), level, tap, top);
            arm.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(dropDia);
            doc.Regenerate();
            doc.Create.NewElbowFitting(MepFromCad.End(arm, top), branch);
            branch = MepFromCad.End(arm, tap);
        }

        if (plan.End != 0)
        {
            doc.Create.NewElbowFitting(MepFromCad.End(main, tap), branch);
            return null;
        }

        return OntoMain(doc, main, branch, tap);
    }
}
