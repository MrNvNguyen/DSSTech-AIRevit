using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Dss.McpHost.Create;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// «Nâng sát dầm»: the selected MEP networks (or every run of one kind on the plan's level) go up as far as the
/// lowest beam or slab over them allows, less the clearance. A connected network moves as one, so its joints hold.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepRaiseCommand : IExternalCommand
{
    private const string Title = "DSSTech — Nâng sát dầm";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uidoc = commandData.Application.ActiveUIDocument;
        var doc = uidoc?.Document;
        if (doc is null || uidoc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        var pool = uidoc.Selection.GetElementIds().Select(doc.GetElement)
            .Where(e => e is MEPCurve || e is FamilyInstance { MEPModel: not null })
            .ToList();
        string scope;
        if (pool.Count == 0)
        {
            if (!ModelRevitLevelContext.TryResolve(doc.ActiveView, doc, out var level, out _, out var err))
            {
                TaskDialog.Show(Title, err + "\nHoặc chọn ống / máng trước rồi gọi lệnh.");
                return Result.Cancelled;
            }

            var td = new TaskDialog(Title) { MainInstruction = "Nâng loại nào trên level " + level.Name + "?", CommonButtons = TaskDialogCommonButtons.Cancel };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Ống gió");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Ống nước (trừ ống thoát có dốc)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Máng cáp");
            var pick = td.Show();
            var type = pick switch
            {
                TaskDialogResult.CommandLink1 => typeof(Duct),
                TaskDialogResult.CommandLink2 => typeof(Pipe),
                TaskDialogResult.CommandLink3 => typeof(CableTray),
                _ => null
            };
            if (type is null)
                return Result.Cancelled;
            var runs = new FilteredElementCollector(doc).OfClass(type).Cast<MEPCurve>()
                .Where(c => c.ReferenceLevel?.Id == level.Id)
                .ToList();
            pool = runs.Cast<Element>()
                .Concat(runs.SelectMany(MepCombine.Connectors).SelectMany(c => c.AllRefs.Cast<Connector>())
                    .Select(c => c.Owner).Where(o => o is FamilyInstance { MEPModel: not null }))
                .GroupBy(e => e.Id).Select(g => g.First()).ToList();
            scope = $"{MepCombine.Kind(runs.FirstOrDefault() ?? (Element)level)} level {level.Name}";
        }
        else
            scope = $"{pool.Count} phần tử đang chọn";

        var profile = CombineProfileStore.Load(doc);
        var clearanceMm = profile.StructureClearanceMm;
        var clearance = UnitConv.MmToInternal(clearanceMm);
        var overhead = ModelRevitWallFit.Overhead(doc);
        if (overhead.Count == 0)
        {
            TaskDialog.Show(Title, "Không thấy dầm/sàn kết cấu (model hoặc file link) để nâng tới.");
            return Result.Cancelled;
        }

        int moved = 0, flat = 0, none = 0, sloped = 0, locked = 0;
        var lifts = new List<double>();
        using (var tx = new Transaction(doc, "DSSTech raise MEP to structure"))
        {
            tx.Start();
            foreach (var net in MepCombine.Networks(doc, pool))
            {
                var curves = net.OfType<MEPCurve>().Where(c => c.Location is LocationCurve { Curve: Line }).ToList();
                if (curves.Count == 0)
                    continue;
                // Pinned, or a locked system (gas keeps its corridor): stays where it is.
                if (net.Any(e => e.Pinned) || curves.Any(c => CombineProfileStore.Identify(c) is var (sys, code) && profile.IsLocked(sys, code)))
                {
                    locked++;
                    continue;
                }
                if (curves.Any(c => c is Pipe && Math.Abs(((Line)((LocationCurve)c.Location).Curve).Direction.Z) is > 1e-4 and < 0.99))
                {
                    // Drainage keeps its fall to the outlet: moving it would lift the outlet off its riser.
                    sloped++;
                    continue;
                }

                double? lift = null;
                foreach (var c in curves)
                {
                    var line = (Line)((LocationCurve)c.Location).Curve;
                    if (Math.Abs(line.Direction.Z) > 0.99)
                        continue;
                    var insulation = CombineProfileStore.Identify(c) is var (sys, code) ? UnitConv.MmToInternal(profile.Insulation(sys, code)) : 0;
                    var top = Math.Max(line.GetEndPoint(0).Z, line.GetEndPoint(1).Z) + MepCombine.Depth(c) / 2 + insulation;
                    var len = line.Length;
                    var step = UnitConv.MmToInternal(300);
                    for (var s = 0.0; ; s += step)
                    {
                        var p = line.Evaluate(Math.Min(s, len) / len, true);
                        if (ModelRevitWallFit.Underside(overhead, p.X, p.Y, top - UnitConv.MmToInternal(1), top + UnitConv.MmToInternal(6000)) is double u)
                        {
                            var room = u - clearance - top;
                            lift = lift is null ? room : Math.Min(lift.Value, room);
                        }

                        if (s >= len)
                            break;
                    }
                }

                if (lift is null)
                {
                    none++;
                    continue;
                }

                // Whole 5 mm.
                var liftMm = Math.Floor(UnitConv.ToMm(lift.Value) / 5) * 5;
                if (liftMm < 1)
                {
                    flat++;
                    continue;
                }

                ElementTransformUtils.MoveElements(doc, net.Select(e => e.Id).ToList(), new XYZ(0, 0, UnitConv.MmToInternal(liftMm)));
                lifts.Add(liftMm);
                moved++;
            }

            tx.Commit();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Phạm vi: {scope}. Đỉnh ống/máng (kể cả bảo ôn mặc định của quy tắc Combine) lên sát đáy dầm/sàn thấp nhất phía trên tuyến − {clearanceMm:0} mm; mỗi mạng nối liền nâng cùng một mức.");
        sb.AppendLine($"Đã nâng {moved} mạng" + (lifts.Count > 0 ? $" (từ {lifts.Min():0} đến {lifts.Max():0} mm)" : "") + $"; đã sát sẵn {flat}; không có dầm/sàn phía trên {none}.");
        if (sloped > 0)
            sb.AppendLine($"Bỏ qua {sloped} mạng ống dốc (thoát nước): giữ độ dốc và điểm xả.");
        if (locked > 0)
            sb.AppendLine($"Bỏ qua {locked} mạng bị khóa (pin, hoặc hệ khóa trong quy tắc Combine như Gas).");
        TaskDialog.Show(Title, sb.ToString());
        return Result.Succeeded;
    }
}

/// <summary>
/// «Né nhau»: select the runs (ducts, pipes, trays) and what they meet; pick which kind gives way and whether it
/// passes under or over. Each run of that kind detours round the others with elbows, clear of them by the clearance.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepAvoidCommand : IExternalCommand
{
    private const string Title = "DSSTech — Né nhau";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;
        var doc = uidoc?.Document;
        if (doc is null || uidoc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        try
        {
            var selected = uidoc.Selection.GetElementIds().Select(doc.GetElement)
                .Where(e => e is MEPCurve || e is FamilyInstance { MEPModel: not null }).ToList();
            if (selected.Count == 0)
            {
                selected = uidoc.Selection.PickObjects(ObjectType.Element, new MepFilter(),
                        "Chọn ống gió, ống nước, máng cáp ở khu vực cần né (Finish khi xong).")
                    .Select(r => doc.GetElement(r)).ToList();
            }

            var kinds = selected.OfType<MEPCurve>().Select(MepCombine.Kind).Distinct().ToList();
            if (kinds.Count < 2 && selected.Count(e => e is not MEPCurve) == 0)
            {
                TaskDialog.Show(Title, "Cần chọn ít nhất hai loại (ví dụ ống gió và máng cáp) ở khu vực va chạm.");
                return Result.Cancelled;
            }

            var dlg = new ArchModelSetupWindow(Title,
                $"Đang chọn: {string.Join(", ", selected.GroupBy(MepCombine.Kind).Select(g => $"{g.Key} {g.Count()}"))}. " +
                "Loại né đổi tuyến bằng cút qua dưới (hoặc trên) các phần tử khác đang chọn.");
            var yieldBox = dlg.AddCombo("Loại phải né", kinds, kinds.Contains("Ống gió") ? "Ống gió" : kinds[0]);
            var dirBox = dlg.AddCombo("Hướng né", new[] { "Đi dưới", "Đi trên" }, "Đi dưới");
            var clearanceBox = dlg.AddInput("Khe hở (mm)", "50");
            if (!dlg.ShowFor(uiApp.MainWindowHandle))
                return Result.Cancelled;

            var yieldKind = DssTheme.ComboValue(yieldBox);
            var under = DssTheme.ComboValue(dirBox) == "Đi dưới";
            var clearance = UnitConv.MmToInternal(double.TryParse(clearanceBox.Text.Trim(), out var c) && c >= 0 ? c : 50);
            var runs = selected.OfType<MEPCurve>().Where(e => MepCombine.Kind(e) == yieldKind).ToList();
            var others = selected.Where(e => MepCombine.Kind(e) != yieldKind)
                .Select(e => MepCombine.Box(MepCombine.Solids(e)))
                .Where(b => b is not null).Select(b => b!.Value).ToList();
            var (made, notes) = Apply(doc, runs, others, under, clearance);
            TaskDialog.Show(Title,
                $"{yieldKind} {(under ? "đi dưới" : "đi trên")} {string.Join(", ", kinds.Where(k => k != yieldKind))}: {made} chỗ đổi tuyến " +
                $"(4 cút mỗi chỗ, khe hở {UnitConv.ToMm(clearance):0} mm).\n" + MepCombine.Summary(notes));
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
    }

    /// <summary>Detours each run round the obstacles its box meets (with the clearance).</summary>
    internal static (int Made, List<string> Notes) Apply(Document doc, List<MEPCurve> runs, List<(XYZ Min, XYZ Max)> obstacles, bool under, double clearance)
    {
        var notes = new List<string>();
        var made = 0;
        using var tx = new Transaction(doc, "DSSTech MEP detours");
        tx.Start();
        foreach (var run in runs)
        {
            var box = MepCombine.Box(MepCombine.Solids(run));
            if (box is null)
                continue;
            var (min, max) = box.Value;
            var hits = obstacles.Where(o =>
                o.Min.X <= max.X + clearance && o.Max.X >= min.X - clearance &&
                o.Min.Y <= max.Y + clearance && o.Max.Y >= min.Y - clearance &&
                o.Min.Z <= max.Z + clearance && o.Max.Z >= min.Z - clearance).ToList();
            if (hits.Count == 0)
                continue;
            try
            {
                made += MepCombine.Detour(doc, run, hits, under, clearance, notes);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
            {
                notes.Add($"{MepCombine.Kind(run)} {run.Id.Value}: {ex.Message}");
            }
        }

        tx.Commit();
        return (made, notes);
    }

    private sealed class MepFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is MEPCurve || e is FamilyInstance { MEPModel: not null };
        public bool AllowReference(Reference r, XYZ p) => false;
    }
}
