using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Flex duct from each open air connector of a device (grille, diffuser, fan coil, box) to the nearest duct of the
/// same system: a tap on the duct over it, down (or across) to the device's neck. One transaction per connector,
/// so a tap Revit refuses leaves only that device unjoined.
/// </summary>
internal static class HvacFlex
{
    private const double EndMarginMm = 200;
    private const double NeckMm = 150;

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

        const string title = "DSSTech — Nối ống mềm";
        var selected = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>().Where(HasOpenAir).ToList();
        List<FamilyInstance> devices;
        string scope;
        if (selected.Count > 0)
        {
            devices = selected;
            scope = $"{selected.Count} thiết bị đang chọn";
        }
        else
        {
            if (!ModelRevitLevelContext.TryResolve(view, doc, out var level, out var topLevel, out var levelErr))
            {
                TaskDialog.Show(title, levelErr);
                return Result.Cancelled;
            }

            var top = topLevel.Id == level.Id ? level.ProjectElevation + UnitConv.MmToInternal(6000) : topLevel.ProjectElevation;
            devices = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(
                    new ElementCategoryFilter(BuiltInCategory.OST_DuctTerminal),
                    new ElementCategoryFilter(BuiltInCategory.OST_MechanicalEquipment)))
                .WhereElementIsNotElementType().OfType<FamilyInstance>()
                .Where(f => f.Location is LocationPoint lp && lp.Point.Z >= level.ProjectElevation - 0.5 && lp.Point.Z < top)
                .Where(HasOpenAir).ToList();
            scope = $"thiết bị chưa nối trên level {level.Name}";
        }

        if (devices.Count == 0)
        {
            TaskDialog.Show(title, "Không có miệng gió / FCU / box nào còn cổng gió chưa nối (chọn thiết bị, hoặc mở mặt bằng của level).");
            return Result.Cancelled;
        }

        var dlg = new ArchModelSetupWindow(title,
            $"{devices.Count} {scope}. Mỗi cổng gió chưa nối được nối bằng ống mềm tới ống gió cùng hệ gần nhất (Supply với Supply, Return với Return…), " +
            "rẽ nhánh (tap) trên ống gió ngay phía trên thiết bị.");
        var reach = dlg.AddInput("Khoảng tìm ống gió quanh thiết bị (mm, mặt bằng)", "3000");
        if (!dlg.ShowFor(uiApp.MainWindowHandle))
            return Result.Cancelled;
        var reachMm = double.TryParse(reach.Text.Trim(), out var r) && r > 0 ? r : 3000;
        TaskDialog.Show(title, Connect(doc, devices.Select(d => d.Id).ToList(), reachMm));
        return Result.Succeeded;
    }

    public static string Connect(Document doc, IList<ElementId> deviceIds, double reachMm)
    {
        var flexTypes = new FilteredElementCollector(doc).OfClass(typeof(FlexDuctType)).Cast<FlexDuctType>().ToList();
        if (flexTypes.Count == 0)
            return "Ống mềm: project chưa có type Flex Duct — load hoặc tạo type ống mềm rồi chạy «Nối ống mềm».";

        var ducts = new FilteredElementCollector(doc).OfClass(typeof(Duct)).Cast<Duct>()
            .Where(d => d.Location is LocationCurve { Curve: Line line } && Math.Abs(line.Direction.Z) < 1e-3)
            .ToList();
        if (ducts.Count == 0)
            return "Ống mềm: chưa có ống gió nằm ngang trong model để nối vào.";

        int joined = 0, noDuct = 0;
        var notes = new List<string>();
        using var group = new TransactionGroup(doc, "DSSTech flex duct");
        group.Start();
        foreach (var id in deviceIds)
        {
            if (doc.GetElement(id) is not FamilyInstance { IsValidObject: true } first)
                continue;
            var name = $"{first.Symbol?.FamilyName} #{id.Value}";
            // Connectors read afresh for each one: a transaction rolled back for the one before leaves them stale.
            foreach (var connectorId in OpenAir(first).Select(c => c.Id).ToList())
            {
                if (doc.GetElement(id) is not FamilyInstance { IsValidObject: true } device ||
                    OpenAir(device).FirstOrDefault(c => c.Id == connectorId) is not { } connector)
                    continue;
                (Duct Duct, XYZ Tap)? target;
                try
                {
                    target = NearestDuct(ducts, connector, reachMm);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    notes.Add($"{name}: {ex.Message}");
                    continue;
                }


                if (target is null)
                {
                    noDuct++;
                    continue;
                }

                // Round flex, as flex is made; a rectangular one only where the project has no round type.
                var flexType = flexTypes.FirstOrDefault(t => t.Shape == ConnectorProfileType.Round) ?? flexTypes[0];
                var guard = new MepFromCad.RollBackOnError();
                using var tx = new Transaction(doc, "DSSTech flex duct");
                var options = tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(guard);
                options.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(options);
                tx.Start();
                try
                {
                    Join(doc, device, connector.Origin, target.Value.Duct, target.Value.Tap, flexType);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
                {
                    tx.RollBack();
                    notes.Add($"{name}: {ex.Message}");
                    continue;
                }

                if (tx.Commit() == TransactionStatus.Committed && guard.Error is null)
                    joined++;
                else
                    notes.Add($"{name}: {guard.Error ?? "Revit không nhận nối"}");
            }
        }

        group.Assimilate();
        var sb = new StringBuilder();
        sb.AppendLine($"Ống mềm: nối {joined} cổng gió.");
        if (noDuct > 0)
            sb.AppendLine($"Không thấy ống gió cùng hệ trong {reachMm:0} mm: {noDuct} cổng (dựng ống gió trước rồi chạy «Nối ống mềm»).");
        if (notes.Count > 0)
            sb.Append(MepCombine.Summary(notes));
        return sb.ToString();
    }

    private static bool HasOpenAir(FamilyInstance fi) => OpenAir(fi).Any();

    private static IEnumerable<Connector> OpenAir(FamilyInstance fi) =>
        fi.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>()
            .Where(c => c.Domain == Domain.DomainHvac && c.ConnectorType == ConnectorType.End && !c.IsConnected)
        ?? Enumerable.Empty<Connector>();

    /// <summary>The duct of the connector's system nearest it, within reach in plan, and the tap point on its axis.</summary>
    private static (Duct Duct, XYZ Tap)? NearestDuct(List<Duct> ducts, Connector connector, double reachMm)
    {
        var o = connector.Origin;
        var reach = UnitConv.MmToInternal(reachMm);
        var margin = UnitConv.MmToInternal(EndMarginMm);
        (Duct, XYZ)? best = null;
        var bestD = double.MaxValue;
        foreach (var duct in ducts)
        {
            if (!duct.IsValidObject || !SameSystem(connector, duct))
                continue;
            var line = (Line)((LocationCurve)duct.Location).Curve;
            var a = line.GetEndPoint(0);
            var len = line.Length;
            var t = (o - a).DotProduct(line.Direction);
            if (len < 2 * margin)
                continue;
            t = Math.Max(margin, Math.Min(len - margin, t));
            var tap = a + line.Direction * t;
            var plan = new XYZ(tap.X - o.X, tap.Y - o.Y, 0).GetLength();
            if (plan > reach)
                continue;
            var d = tap.DistanceTo(o);
            if (d >= bestD)
                continue;
            bestD = d;
            best = (duct, tap);
        }

        return best;
    }

    /// <summary>Supply to supply, return to return, exhaust to exhaust; a connector or duct of no system takes any.</summary>
    private static bool SameSystem(Connector connector, Duct duct)
    {
        DuctSystemType kind;
        try
        {
            kind = connector.DuctSystemType;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return true;
        }

        if (kind is not (DuctSystemType.SupplyAir or DuctSystemType.ReturnAir or DuctSystemType.ExhaustAir))
            return true;
        var classification = (duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "").Replace(" ", "");
        return classification.Length == 0 || classification.IndexOf(kind.ToString(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Round flex as sized for the device: its neck's diameter, or for a rectangular neck the round duct of equal
    /// friction (up to a standard size). A short collar of the round duct type comes off the main with that type's tap,
    /// the flex runs from it to the device, and a rectangular neck takes a transition onto a round stub.
    /// </summary>
    private static void Join(Document doc, FamilyInstance device, XYZ at, Duct duct, XYZ tap, FlexDuctType flexType)
    {
        var connector = OpenAir(device).OrderBy(c => c.Origin.DistanceTo(at)).First();
        var round = RoundType(doc, duct);
        if (flexType.Shape != ConnectorProfileType.Round || round is null)
        {
            Straight(doc, device, at, duct, tap, flexType);
            return;
        }

        var o = connector.Origin;
        var n = connector.CoordinateSystem.BasisZ;
        var dia = connector.Shape == ConnectorProfileType.Round
            ? connector.Radius * 2
            : UnitConv.MmToInternal(DevicePlanner.RoundFor(UnitConv.ToMm(connector.Width), UnitConv.ToMm(connector.Height)));
        var neckLen = UnitConv.MmToInternal(NeckMm);
        var depth = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() is double dd && dd > 0
            ? dd
            : duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0;
        var systemType = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
        var levelId = duct.ReferenceLevel.Id;

        // The device's end: the neck itself when round, else a round stub out of a transition on it.
        var deviceEnd = o;
        Connector? deviceJoin = connector;
        if (connector.Shape != ConnectorProfileType.Round)
        {
            deviceJoin = null;
            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                var far = o + n * UnitConv.MmToInternal(StubMm);
                var stub = Duct.Create(doc, systemType, round.Id, levelId, o, far);
                stub.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(dia);
                doc.Regenerate();
                doc.Create.NewTransitionFitting(MepFromCad.End(stub, o), connector);
                doc.Regenerate();
                deviceJoin = MepFromCad.End(stub, far);
                deviceEnd = deviceJoin.Origin;
                sub.Commit();
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                // No transition for the type: the flex comes onto the rectangular neck as it is.
                sub.RollBack();
                deviceJoin = OpenAir(device).OrderBy(c => c.Origin.DistanceTo(at)).FirstOrDefault();
            }
        }

        // The collar on the main: down past its underside to over the device, else across toward it.
        var below = tap.Z - depth / 2 - neckLen;
        var collarEnd = deviceEnd.Z < below - neckLen
            ? new XYZ(tap.X, tap.Y, below)
            : tap + new XYZ(deviceEnd.X - tap.X, deviceEnd.Y - tap.Y, 0).Normalize() * (depth / 2 + neckLen);
        var collar = Duct.Create(doc, systemType, round.Id, levelId, tap, collarEnd);
        collar.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(dia);
        doc.Regenerate();
        MepFromCad.Tap(doc, collar.Id, tap, duct, turn: false);
        doc.Regenerate();
        var collarJoin = MepFromCad.End((Duct)doc.GetElement(collar.Id), collarEnd);

        var outward = deviceJoin is not null && deviceJoin.Owner.Id != device.Id ? deviceJoin.CoordinateSystem.BasisZ : n;
        var points = new List<XYZ>();
        foreach (var p in new[] { collarJoin.Origin, deviceEnd + outward * neckLen, deviceEnd })
        {
            if (points.Count == 0 || points[points.Count - 1].DistanceTo(p) > UnitConv.MmToInternal(20))
                points.Add(p);
        }

        if (points.Count < 2)
            throw new ArgumentException("thiết bị nằm sát ống gió, không cần ống mềm");
        var flex = FlexDuct.Create(doc, systemType, flexType.Id, levelId, points);
        flex.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(dia);
        doc.Regenerate();
        var ends = flex.ConnectorManager.Connectors.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End).ToList();
        ends.OrderBy(c => c.Origin.DistanceTo(collarJoin.Origin)).First().ConnectTo(collarJoin);
        if (deviceJoin is not null)
            ends.OrderBy(c => c.Origin.DistanceTo(deviceEnd)).First().ConnectTo(deviceJoin);
    }

    private const double StubMm = 300;

    /// <summary>The round duct type for collars and stubs: the one named most like the main's type.</summary>
    private static DuctType? RoundType(Document doc, Duct main)
    {
        var mainName = main.DuctType?.Name ?? "";
        var tail = mainName.Contains('-') ? mainName.Substring(mainName.LastIndexOf('-') + 1) : mainName;
        return new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>()
            .Where(t => t.Shape == ConnectorProfileType.Round)
            .OrderByDescending(t => tail.Length > 0 && t.Name.IndexOf(tail, StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenBy(t => t.Name)
            .FirstOrDefault();
    }

    /// <summary>Flex from the tap on the duct axis, down past the duct, to a short neck straight out of the device's connector.</summary>
    private static void Straight(Document doc, FamilyInstance device, XYZ at, Duct duct, XYZ tap, FlexDuctType flexType)
    {
        var connector = OpenAir(device).OrderBy(c => c.Origin.DistanceTo(at)).First();
        var o = connector.Origin;
        var neck = o + connector.CoordinateSystem.BasisZ * UnitConv.MmToInternal(NeckMm);
        var depth = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() is double dia && dia > 0
            ? dia
            : duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0;
        var points = new List<XYZ> { tap };
        if (tap.Z > neck.Z + depth / 2 + UnitConv.MmToInternal(NeckMm))
            points.Add(new XYZ(tap.X, tap.Y, tap.Z - depth / 2 - UnitConv.MmToInternal(NeckMm)));
        points.Add(neck);
        points.Add(o);
        var path = new List<XYZ>();
        foreach (var p in points)
        {
            if (path.Count == 0 || path[path.Count - 1].DistanceTo(p) > UnitConv.MmToInternal(20))
                path.Add(p);
        }

        if (path.Count < 2)
            throw new ArgumentException("thiết bị nằm sát ống gió, không cần ống mềm");

        var systemType = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
        var flex = FlexDuct.Create(doc, systemType, flexType.Id, duct.ReferenceLevel.Id, path);
        if (connector.Shape == ConnectorProfileType.Round || flexType.Shape == ConnectorProfileType.Round)
            flex.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(connector.Shape == ConnectorProfileType.Round
                ? connector.Radius * 2
                : Math.Max(connector.Width, connector.Height));
        else
        {
            flex.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(connector.Width);
            flex.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(connector.Height);
        }

        doc.Regenerate();
        var ends = flex.ConnectorManager.Connectors.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End).ToList();
        ends.OrderBy(c => c.Origin.DistanceTo(o)).First().ConnectTo(connector);
        doc.Create.NewTakeoffFitting(ends.OrderBy(c => c.Origin.DistanceTo(tap)).First(), duct);
    }
}
