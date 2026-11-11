using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>«Giá treo PCCC»: hangers on the sprinkler pipes, and brackets at the heads on flexible hoses.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class FireHangerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        FireHangers.Run(data.Application);
}

/// <summary>
/// Hangers of a chosen family on the level's (or the selected) horizontal fire pipes, spaced by
/// <see cref="HangerPlanner"/>: at the pipe's axis, turned along it, the pipe's size and the rod up to the beam or slab
/// over it written where the family has such parameters. Optionally a bracket family at each head fed by a flexible
/// hose. A spot already holding the same family is left. One transaction.
/// </summary>
internal static class FireHangers
{
    private const string None = "(không đặt)";

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

        const string title = "DSSTech — Giá treo PCCC";
        var pipes = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<Pipe>().Where(Horizontal).ToList();
        var scope = $"{pipes.Count} ống đang chọn";
        if (pipes.Count == 0)
        {
            if (!ModelRevitLevelContext.TryResolve(view, doc, out var level, out var topLevel, out var levelErr))
            {
                TaskDialog.Show(title, levelErr);
                return Result.Cancelled;
            }

            var top = topLevel.Id == level.Id ? level.ProjectElevation + UnitConv.MmToInternal(6000) : topLevel.ProjectElevation;
            pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
                .Where(p => Horizontal(p) && Fire(p) && ((LocationCurve)p.Location).Curve.GetEndPoint(0).Z is var z &&
                            z >= level.ProjectElevation - 0.5 && z < top)
                .ToList();
            scope = $"{pipes.Count} ống PCCC nằm ngang trên level {level.Name}";
        }

        if (pipes.Count == 0)
        {
            TaskDialog.Show(title, "Không có ống PCCC nằm ngang nào (chọn ống, hoặc mở mặt bằng của level có ống PCCC).");
            return Result.Cancelled;
        }

        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(s => s.Family?.FamilyPlacementType == FamilyPlacementType.OneLevelBased && s.Category?.Id.Value is long c &&
                        c is (long)BuiltInCategory.OST_PipeAccessory or (long)BuiltInCategory.OST_GenericModel or
                            (long)BuiltInCategory.OST_SpecialityEquipment or (long)BuiltInCategory.OST_MechanicalEquipment)
            .ToDictionary(s => $"{s.Family.Name} : {s.Name}", s => s);
        if (symbols.Count == 0)
        {
            TaskDialog.Show(title, "Project chưa có family giá treo (Pipe Accessory / Generic Model, đặt theo level). Load family giá treo rồi chạy lại.");
            return Result.Cancelled;
        }

        static bool Looks(string n, params string[] words) => words.Any(w => n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        var names = symbols.Keys.OrderBy(n => n).ToList();
        var hangerGuess = names.FirstOrDefault(n => Looks(n, "hanger", "treo", "gia do", "giá đỡ", "support", "ty ren")) ?? names[0];
        var bracketGuess = names.FirstOrDefault(n => Looks(n, "bracket", "support bar", "kep", "kẹp", "thanh do", "thanh đỡ")) ?? None;

        var dlg = new ArchModelSetupWindow(title,
            $"{scope}. Giá treo đặt tại tim ống, xoay theo ống: cách nhau ≤ 3,6 m (≤ DN32) / ≤ 4,5 m (≥ DN40); đầu nhánh / đầu phun cuối " +
            "≤ 0,9 m (ống ≤ DN25) / ≤ 1,2 m (ống lớn hơn); tránh phụ kiện 150 mm. Khoảng cách theo NFPA 13 — đối chiếu TCVN 7336 Đ5.5.21 " +
            "và chỉ dẫn kỹ thuật dự án. Family có tham số đường kính / chiều dài ty thì được ghi theo ống và dầm / sàn phía trên.", 500);
        var hanger = dlg.AddCombo("Family giá treo ống", names, hangerGuess);
        var bracket = dlg.AddCombo("Giá đỡ đầu phun nối ống mềm (trên khung trần)", new[] { None }.Concat(names), bracketGuess);
        var small = dlg.AddInput("Khoảng cách tối đa ống ≤ DN32 (mm)", "3600");
        var large = dlg.AddInput("Khoảng cách tối đa ống ≥ DN40 (mm)", "4500");
        if (!dlg.ShowFor(uiApp.MainWindowHandle))
            return Result.Cancelled;
        var smallMm = double.TryParse(small.Text.Trim(), out var s1) && s1 > 500 ? s1 : 3600;
        var largeMm = double.TryParse(large.Text.Trim(), out var s2) && s2 > 500 ? s2 : 4500;
        var bracketSymbol = (string)bracket.SelectedItem == None ? null : symbols[(string)bracket.SelectedItem];
        DoneReport.Show(title, Place(doc, pipes, symbols[(string)hanger.SelectedItem], bracketSymbol, smallMm, largeMm));
        return Result.Succeeded;
    }

    private static bool Horizontal(Pipe p) => p.Location is LocationCurve { Curve: Line line } && Math.Abs(line.Direction.Z) < 1e-3;

    private static bool Fire(Pipe pipe)
    {
        var classification = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
        return classification.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string Place(Document doc, List<Pipe> pipes, FamilySymbol hanger, FamilySymbol? bracket, double smallMm, double largeMm)
    {
        var overhead = ModelRevitWallFit.Overhead(doc);
        var existing = Existing(doc, hanger);
        int placed = 0, kept = 0, noRod = 0, brackets = 0;
        var failures = new List<string>();
        using var tx = new Transaction(doc, "DSSTech fire hangers");
        tx.Start();
        if (!hanger.IsActive)
            hanger.Activate();
        if (bracket is { IsActive: false })
            bracket.Activate();
        foreach (var pipe in pipes)
        {
            var line = (Line)((LocationCurve)pipe.Location).Curve;
            var (a, b) = (line.GetEndPoint(0), line.GetEndPoint(1));
            var dn = UnitConv.ToMm(pipe.Diameter);
            var outer = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)?.AsDouble() is double od && od > 0 ? od : pipe.Diameter;
            var level = pipe.ReferenceLevel;
            var angle = Math.Atan2(line.Direction.Y, line.Direction.X);
            foreach (var s in HangerPlanner.Plan(UnitConv.ToMm(line.Length), dn, Free(MepFromCad.End(pipe, a)), Free(MepFromCad.End(pipe, b)), smallMm, largeMm))
            {
                var p = a + line.Direction * UnitConv.MmToInternal(s);
                if (existing.Any(e => e.DistanceTo(p) < UnitConv.MmToInternal(100)))
                {
                    kept++;
                    continue;
                }

                try
                {
                    var fi = doc.Create.NewFamilyInstance(p, hanger, level, StructuralType.NonStructural);
                    ElementTransformUtils.RotateElement(doc, fi.Id, Line.CreateBound(p, p + XYZ.BasisZ), angle);
                    SetLength(fi, outer, "pipe diameter", "pipe od", "đường kính", "duong kinh", "diameter", "dn");
                    var pipeTop = p.Z + outer / 2;
                    if (ModelRevitWallFit.Underside(overhead, p.X, p.Y, pipeTop, pipeTop + UnitConv.MmToInternal(6000)) is double under)
                        SetLength(fi, under - pipeTop, "rod length", "rod", "chiều dài ty", "chieu dai ty", "ty treo", "hanger length", "drop");
                    else
                        noRod++;
                    fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"Giá treo PCCC DN{dn:0}");
                    existing.Add(p);
                    placed++;
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
                {
                    failures.Add($"pipe #{pipe.Id.Value} at {MepFromCad.Describe(p)}: {ex.Message}");
                }
            }
        }

        if (bracket is not null)
            brackets = Brackets(doc, bracket, failures);
        tx.Commit();

        var sb = new StringBuilder();
        sb.AppendLine($"Giá treo ống PCCC: đặt {placed}, giữ {kept} đã có.");
        if (bracket is not null)
            sb.AppendLine($"Giá đỡ đầu phun nối ống mềm: {brackets}.");
        if (noRod > 0)
            sb.AppendLine($"{noRod} giá treo không thấy dầm / sàn phía trên trong 6 m: chiều dài ty để mặc định.");
        if (failures.Count > 0)
            sb.AppendLine($"Không đặt được {failures.Count} giá treo (xem log).");
        foreach (var f in failures.Take(60))
            DssTechLog.Write($"fire hanger: {f}");
        return sb.ToString();
    }

    /// <summary>
    /// An end with nothing beyond: open, or a fitting (elbow, reducer) on to a riser or drop, or straight into a head —
    /// the end of a branch, where the last head's hanger rule applies.
    /// </summary>
    private static bool Free(Connector end)
    {
        if (!end.IsConnected)
            return true;
        foreach (Connector r in end.AllRefs)
        {
            if (r.Owner is FamilyInstance fi && fi.Category?.Id.Value == (long)BuiltInCategory.OST_Sprinklers)
                return true;
            if (r.Owner is not FamilyInstance fitting || fitting.MEPModel?.ConnectorManager is not { } cm)
                continue;
            var others = cm.Connectors.Cast<Connector>().Where(c => c.Id != r.Id).ToList();
            if (others.Count != 1)
                return false;
            return others[0].AllRefs.Cast<Connector>().Any(o =>
                o.Owner is FamilyInstance { Category: { } cat } && cat.Id.Value == (long)BuiltInCategory.OST_Sprinklers ||
                o.Owner is Pipe { Location: LocationCurve { Curve: Line l } } && Math.Abs(l.Direction.Z) > 0.9);
        }

        return false;
    }

    /// <summary>A bracket at each head fed by a flexible hose, unless one stands there.</summary>
    private static int Brackets(Document doc, FamilySymbol bracket, List<string> failures)
    {
        var existing = Existing(doc, bracket);
        var made = 0;
        foreach (var head in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sprinklers).WhereElementIsNotElementType().OfType<FamilyInstance>())
        {
            if (!OnHose(head, 4) || head.Location is not LocationPoint lp || existing.Any(e => e.DistanceTo(lp.Point) < UnitConv.MmToInternal(100)))
                continue;
            try
            {
                var level = doc.GetElement(head.LevelId) as Level ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().First();
                doc.Create.NewFamilyInstance(lp.Point, bracket, level, StructuralType.NonStructural);
                existing.Add(lp.Point);
                made++;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                failures.Add($"bracket at head #{head.Id.Value}: {ex.Message}");
            }
        }

        return made;
    }

    /// <summary>A flexible hose within a few elements of the element (the head's stub and reducer between).</summary>
    private static bool OnHose(Element start, int depth)
    {
        var seen = new HashSet<ElementId> { start.Id };
        var front = new List<Element> { start };
        for (var d = 0; d < depth && front.Count > 0; d++)
        {
            var next = new List<Element>();
            foreach (var e in front)
            {
                var cm = e is MEPCurve c ? c.ConnectorManager : (e as FamilyInstance)?.MEPModel?.ConnectorManager;
                if (cm is null)
                    continue;
                foreach (Connector c1 in cm.Connectors)
                {
                    foreach (Connector r in c1.AllRefs)
                    {
                        if (r.Owner is FlexPipe)
                            return true;
                        if (r.Owner is { } o && seen.Add(o.Id) && o is not Pipe { Location: LocationCurve { Curve: Line { Length: > 1.0 } } })
                            next.Add(o);
                    }
                }
            }

            front = next;
        }

        return false;
    }

    private static List<XYZ> Existing(Document doc, FamilySymbol symbol) =>
        new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f.Symbol?.Id == symbol.Id && f.Location is LocationPoint)
            .Select(f => ((LocationPoint)f.Location).Point).ToList();

    /// <summary>The first writable length parameter whose name holds one of the words.</summary>
    private static void SetLength(FamilyInstance fi, double value, params string[] words)
    {
        foreach (Parameter p in fi.Parameters)
        {
            var name = p.Definition?.Name ?? "";
            if (p.IsReadOnly || p.StorageType != StorageType.Double ||
                !words.Any(w => name.Equals(w, StringComparison.OrdinalIgnoreCase) || name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 && w.Length > 3))
                continue;
            p.Set(value);
            return;
        }
    }
}
