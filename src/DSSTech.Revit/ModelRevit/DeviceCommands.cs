using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using DSSTech.Revit.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;

namespace DSSTech.Revit.ModelRevit;

// ── Thiết bị tường: ổ cắm, công tắc, đèn ốp tường, điều hòa cục bộ ──────────
[Transaction(TransactionMode.Manual)]
public sealed class WallDevicesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Wall, null);
}

[Transaction(TransactionMode.Manual)]
public sealed class SocketCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Wall, DeviceKind.Socket);
}

[Transaction(TransactionMode.Manual)]
public sealed class SwitchCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Wall, DeviceKind.Switch);
}

[Transaction(TransactionMode.Manual)]
public sealed class WallLightCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Wall, DeviceKind.WallLight);
}

[Transaction(TransactionMode.Manual)]
public sealed class SplitAcCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Wall, DeviceKind.SplitAc);
}

// ── Thiết bị trần: đèn trần, đầu phun sprinkler, đầu báo cháy ─────────────
[Transaction(TransactionMode.Manual)]
public sealed class CeilingDevicesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Ceiling, null);
}

[Transaction(TransactionMode.Manual)]
public sealed class CeilingLightCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Ceiling, DeviceKind.CeilingLight);
}

[Transaction(TransactionMode.Manual)]
public sealed class SprinklerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Ceiling, DeviceKind.Sprinkler);
}

[Transaction(TransactionMode.Manual)]
public sealed class FireAlarmCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Ceiling, DeviceKind.FireAlarm);
}

// ── Thiết bị ĐHKK: miệng gió, FCU, box gió, nối ống mềm ─────────────────
[Transaction(TransactionMode.Manual)]
public sealed class HvacDevicesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Hvac, null);
}

// ── Thiết bị PCCC: tủ chữa cháy trên tường, đầu phun sprinkler và đầu báo cháy dưới trần ──
[Transaction(TransactionMode.Manual)]
public sealed class FireDevicesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        DeviceTool.Run(data.Application, DeviceMount.Fire, null);
}

/// <summary>Flex duct from the selected (else every unconnected) air devices of the level to the nearest duct.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class FlexConnectCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        HvacFlex.Run(data.Application);
}

/// <summary>One block name mapped to the family type it becomes.</summary>
internal sealed record DeviceRow(DeviceBlockGroup Group, DeviceSpec Spec, FamilySymbol Symbol, double Mm);

/// <summary>
/// Devices from the blocks of a CAD link. Wall devices land on the face of the nearest wall (this model or a
/// link) at their mounting height, facing the room; ceiling devices on the underside of the ceiling over them.
/// Face-based families are hosted on that face; others stand free at the same height, turned to match.
/// </summary>
internal static class DeviceTool
{
    private const double RayLeadMm = 300;
    private const double DuplicateMm = 50;
    private const string ViewName = "DSS Thiết bị 3D";

    public static Result Run(UIApplication uiApp, DeviceMount mount, DeviceKind? only)
    {
        var uidoc = uiApp.ActiveUIDocument;
        var doc = uidoc?.Document;
        var view = doc?.ActiveView;
        if (doc is null || uidoc is null || view is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        var title = "DSSTech — " + (only is DeviceKind k ? DevicePlanner.Spec(k).Label
            : mount switch { DeviceMount.Wall => "Thiết bị tường", DeviceMount.Hvac => "Thiết bị ĐHKK", DeviceMount.Fire => "Thiết bị PCCC", _ => "Thiết bị trần" });
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
                    "Chọn CAD link có block thiết bị.");
                import = doc.GetElement(picked.ElementId) as ImportInstance;
            }

            if (import is null)
                return Result.Cancelled;
            var blocks = DeviceBlocks.Read(import);
            if (blocks.Count == 0)
            {
                TaskDialog.Show(title, "CAD link không có block nào. Thiết bị cần vẽ bằng block (ổ cắm, công tắc, đèn…).");
                return Result.Cancelled;
            }

            var kinds = DevicePlanner.Specs.Where(s => DevicePlanner.Offers(mount, s) && (only is null || s.Kind == only)).ToList();
            var groups = DevicePlanner.Groups(blocks.Select(b => b.Insert));
            var dlg = new DeviceSetupWindow(doc, title, mount, kinds, groups, level.Name);
            if (!dlg.ShowFor(uiApp.MainWindowHandle) || dlg.Rows.Count == 0)
                return Result.Cancelled;
            DeviceMemory.Save(dlg.Rows);

            var view3D = DeviceView(doc);
            var placed = new List<ElementId>();
            var report = Place(doc, view3D, level, topLevel, mount, blocks, dlg.Rows, dlg.ReachMm, dlg.DefaultCeilingMm,
                dlg.IgnoreCeiling, placed);
            if (mount == DeviceMount.Hvac && dlg.ConnectFlex && placed.Count > 0)
                report += "\n" + HvacFlex.Connect(doc, placed, dlg.FlexReachMm);
            TaskDialog.Show(title, report);
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
    }

    private static string Place(Document doc, View3D view3D, Level level, Level topLevel, DeviceMount mount,
        List<DeviceBlocks.Hit> blocks, IReadOnlyList<DeviceRow> rows, double reachMm, double defaultCeilingMm,
        bool ignoreCeiling = false, List<ElementId>? placed = null)
    {
        var byName = rows.ToDictionary(r => r.Group.Name, StringComparer.OrdinalIgnoreCase);
        // A block inside a chosen block is part of its symbol, not a device of its own.
        bool InsideChosen(DeviceBlocks.Hit hit)
        {
            for (var p = hit.Parent; p >= 0; p = blocks[p].Parent)
            {
                if (byName.ContainsKey(blocks[p].Insert.BlockName))
                    return true;
            }

            return false;
        }

        var walls = rows.Any(r => DevicePlanner.OnWall(r.Spec)) ? DeviceWalls.Collect(doc, level) : new List<HostWallAxis>();
        var storey = topLevel.Elevation - level.Elevation;
        var existing = new Dictionary<string, List<XYZ>>();
        var made = new Dictionary<string, int>();
        var hosted = 0;
        var free = 0;
        var noWall = 0;
        var noCeiling = 0;
        var skipped = 0;
        var notes = new List<string>();
        using (var tx = new Transaction(doc, "DSSTech devices from CAD"))
        {
            tx.Start();
            foreach (var hit in blocks)
            {
                if (!byName.TryGetValue(hit.Insert.BlockName, out var row) || InsideChosen(hit))
                    continue;
                if (!row.Symbol.IsActive)
                    row.Symbol.Activate();
                var x = DevicePlanner.Snap(hit.Insert.X);
                var y = DevicePlanner.Snap(hit.Insert.Y);
                var cat = row.Spec.Category;
                if (!existing.TryGetValue(cat, out var points))
                {
                    points = Existing(doc, cat, level.ProjectElevation, topLevel.ProjectElevation);
                    existing[cat] = points;
                }

                var plan = new XYZ(UnitConv.MmToInternal(x), UnitConv.MmToInternal(y), 0);
                if (points.Any(p => new XYZ(p.X - plan.X, p.Y - plan.Y, 0).GetLength() < UnitConv.MmToInternal(DuplicateMm)))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var wall = DevicePlanner.OnWall(row.Spec);
                    var outcome = wall
                        ? OnWall(doc, view3D, level, row, walls, x, y, hit.Insert.RotationDeg, reachMm, notes)
                        : OnCeiling(doc, view3D, level, storey, row, x, y, hit.Insert.RotationDeg, defaultCeilingMm, notes, ignoreCeiling, placed);
                    if (outcome is null)
                        continue;
                    points.Add(plan);
                    made[row.Spec.Label] = made.TryGetValue(row.Spec.Label, out var n) ? n + 1 : 1;
                    if (outcome.Value.Hosted)
                        hosted++;
                    else
                        free++;
                    if (outcome.Value.Missing)
                    {
                        if (wall)
                            noWall++;
                        else
                            noCeiling++;
                    }
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
                {
                    notes.Add($"{row.Group.Name} ({x:0},{y:0}): {ex.Message}");
                }
            }

            tx.Commit();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Level {level.Name}: " + (made.Count == 0 ? "không dựng thiết bị nào." : string.Join(", ", made.Select(m => $"{m.Key} {m.Value}")) + "."));
        sb.AppendLine($"Gắn vào mặt {mount switch { DeviceMount.Wall => "tường", DeviceMount.Fire => "tường / trần", _ => "trần" }}: {hosted}; đặt tự do theo cao độ: {free}.");
        if (noWall > 0)
            sb.AppendLine($"Không thấy tường trong {reachMm:0} mm: {noWall} (đặt tại điểm block, theo hướng block).");
        if (mount != DeviceMount.Wall && ignoreCeiling)
            sb.AppendLine($"Cao độ chỉ định: trần tính ở {defaultCeilingMm:0} mm từ level cho mọi thiết bị.");
        else if (noCeiling > 0)
            sb.AppendLine($"Không có trần phía trên: {noCeiling} (đặt ở cao độ trần mặc định {defaultCeilingMm:0} mm).");
        if (skipped > 0)
            sb.AppendLine($"Đã có thiết bị cùng loại tại chỗ (bỏ qua): {skipped}.");
        sb.Append(MepCombine.Summary(notes));
        return sb.ToString();
    }

    /// <summary>On the face of the wall beside the block at the row's height; null when nothing was placed.</summary>
    private static (bool Hosted, bool Missing)? OnWall(Document doc, View3D view3D, Level level, DeviceRow row, List<HostWallAxis> walls,
        double x, double y, double rotationDeg, double reachMm, List<string> notes)
    {
        var z = level.ProjectElevation + UnitConv.MmToInternal(row.Mm);
        var snap = DevicePlanner.SnapToWall(x, y, walls, reachMm);
        var placement = row.Symbol.Family.FamilyPlacementType;
        if (snap is null)
        {
            if (placement == FamilyPlacementType.OneLevelBasedHosted)
            {
                notes.Add($"{row.Group.Name} ({x:0},{y:0}): family gắn tường nhưng không thấy tường gần block");
                return null;
            }

            Free(doc, row.Symbol, level, new XYZ(UnitConv.MmToInternal(x), UnitConv.MmToInternal(y), 0), row.Mm, Turn(rotationDeg));
            return (false, true);
        }

        var normal = new XYZ(snap.NormalX, snap.NormalY, 0);
        var along = new XYZ(-snap.NormalY, snap.NormalX, 0);
        var face = new XYZ(UnitConv.MmToInternal(snap.X), UnitConv.MmToInternal(snap.Y), z);
        if (placement == FamilyPlacementType.WorkPlaneBased)
        {
            var lead = UnitConv.MmToInternal(RayLeadMm);
            var hit = Nearest(view3D, BuiltInCategory.OST_Walls, face + normal * lead, -normal, lead * 2);
            if (hit is not null)
            {
                doc.Create.NewFamilyInstance(hit.Value.Reference, hit.Value.Point, along, row.Symbol);
                return (true, false);
            }
        }
        else if (placement == FamilyPlacementType.OneLevelBasedHosted)
        {
            if (!long.TryParse(snap.WallId, out var wallId) || doc.GetElement(new ElementId(wallId)) is not Wall wall)
            {
                notes.Add($"{row.Group.Name} ({x:0},{y:0}): tường nằm trong file link, family gắn tường không đặt được (dùng family face-based)");
                return null;
            }

            var fi = doc.Create.NewFamilyInstance(face, row.Symbol, wall, level, StructuralType.NonStructural);
            Offset(fi, row.Mm);
            doc.Regenerate();
            if (fi.CanFlipFacing && fi.FacingOrientation.DotProduct(normal) < 0)
                fi.flipFacing();
            return (true, false);
        }

        Free(doc, row.Symbol, level, new XYZ(face.X, face.Y, 0), row.Mm, normal);
        return (false, false);
    }

    /// <summary>Under the ceiling over the block (else at the default ceiling height), less the row's drop.</summary>
    private static (bool Hosted, bool Missing)? OnCeiling(Document doc, View3D view3D, Level level, double storey, DeviceRow row,
        double x, double y, double rotationDeg, double defaultCeilingMm, List<string> notes, bool ignoreCeiling = false,
        List<ElementId>? placed = null)
    {
        var start = new XYZ(UnitConv.MmToInternal(x), UnitConv.MmToInternal(y), level.ProjectElevation + UnitConv.MmToInternal(RayLeadMm));
        // A typed height stands for every device: no ceiling is looked for.
        var hit = ignoreCeiling ? null : Nearest(view3D, BuiltInCategory.OST_Ceilings, start, XYZ.BasisZ, storey - UnitConv.MmToInternal(RayLeadMm));
        var turn = Turn(rotationDeg);
        if (hit is not null && Math.Abs(row.Mm) < 1 && row.Symbol.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased)
        {
            try
            {
                placed?.Add(doc.Create.NewFamilyInstance(hit.Value.Reference, hit.Value.Point, turn, row.Symbol).Id);
                return (true, false);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                notes.Add($"{row.Group.Name} ({x:0},{y:0}): không gắn được vào mặt trần ({ex.Message}), đặt tự do");
            }
        }

        if (row.Symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted)
        {
            notes.Add($"{row.Group.Name}: family gắn tường không dùng cho thiết bị trần");
            return null;
        }

        var ceilingMm = hit is not null ? UnitConv.ToMm(hit.Value.Point.Z - level.ProjectElevation) : defaultCeilingMm;
        var fi = Free(doc, row.Symbol, level, new XYZ(start.X, start.Y, 0), DevicePlanner.Snap(ceilingMm - row.Mm), turn);
        placed?.Add(fi.Id);
        return (false, hit is null && !ignoreCeiling);
    }

    /// <summary>A free-standing instance on the level at <paramref name="heightMm"/>, its face turned to <paramref name="facing"/>.</summary>
    private static FamilyInstance Free(Document doc, FamilySymbol symbol, Level level, XYZ plan, double heightMm, XYZ facing)
    {
        var at = new XYZ(plan.X, plan.Y, level.ProjectElevation);
        var fi = doc.Create.NewFamilyInstance(at, symbol, level, StructuralType.NonStructural);
        Offset(fi, heightMm);
        doc.Regenerate();
        var current = fi.FacingOrientation;
        var angle = Math.Atan2(current.X * facing.Y - current.Y * facing.X, current.X * facing.X + current.Y * facing.Y);
        if (Math.Abs(angle) > 1e-6)
            ElementTransformUtils.RotateElement(doc, fi.Id, Line.CreateBound(at, at + XYZ.BasisZ), angle);
        return fi;
    }

    private static void Offset(FamilyInstance fi, double mm)
    {
        foreach (var bip in new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM })
        {
            if (fi.get_Parameter(bip) is { IsReadOnly: false } p)
            {
                p.Set(UnitConv.MmToInternal(mm));
                return;
            }
        }
    }

    /// <summary>Block rotation as a plan direction: the block's +X turned by it.</summary>
    private static XYZ Turn(double rotationDeg)
    {
        var r = rotationDeg * Math.PI / 180;
        return new XYZ(Math.Cos(r), Math.Sin(r), 0);
    }

    private static (Reference Reference, XYZ Point)? Nearest(View3D view, BuiltInCategory category, XYZ origin, XYZ direction, double maxDistance)
    {
        var finder = new ReferenceIntersector(new ElementCategoryFilter(category), FindReferenceTarget.Face, view)
        {
            FindReferencesInRevitLinks = true
        };
        var ctx = finder.FindNearest(origin, direction);
        if (ctx is null || ctx.Proximity > maxDistance)
            return null;
        return (ctx.GetReference(), origin + direction * ctx.Proximity);
    }

    /// <summary>The Revit categories a spec's category names (one, or several split by ';'), those this Revit has.</summary>
    internal static List<BuiltInCategory> Categories(string category) =>
        category.Split(';').Select(c => Enum.TryParse<BuiltInCategory>(c.Trim(), out var bic) ? bic : (BuiltInCategory?)null)
            .Where(c => c is not null).Select(c => c!.Value).ToList();

    private static List<XYZ> Existing(Document doc, string category, double fromZ, double toZ)
    {
        var cats = Categories(category);
        if (cats.Count == 0)
            return new List<XYZ>();
        return new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(cats)).WhereElementIsNotElementType().OfType<FamilyInstance>()
            .Select(f => f.Location is LocationPoint lp ? lp.Point : null)
            .Where(p => p is not null && p.Z >= fromZ - 0.5 && p.Z < toZ)
            .Select(p => p!).ToList();
    }

    /// <summary>A plain 3D view the face search can see through (no section box, no template).</summary>
    private static View3D DeviceView(Document doc)
    {
        var view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate && v.Name == ViewName);
        if (view is not null)
            return view;
        var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
        using var tx = new Transaction(doc, "DSSTech device 3D view");
        tx.Start();
        view = View3D.CreateIsometric(doc, type.Id);
        view.ViewTemplateId = ElementId.InvalidElementId;
        view.IsSectionBoxActive = false;
        try
        {
            view.Name = ViewName;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Name taken by a view of another kind: Revit's own name stays.
        }

        tx.Commit();
        return view;
    }
}

/// <summary>Block references in a CAD link, nested ones too (with the block they sit in), in millimetres.</summary>
internal static class DeviceBlocks
{
    private const int MaxDepth = 4;

    internal sealed record Hit(CadInsert Insert, int Parent);

    public static List<Hit> Read(ImportInstance import)
    {
        var hits = new List<Hit>();
        var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
        var geometry = import.get_Geometry(opt);
        if (geometry is null)
            return hits;
        var link = import.GetTotalTransform();
        foreach (var obj in geometry)
        {
            if (obj is not GeometryInstance top)
                continue;
            foreach (var child in top.GetSymbolGeometry())
            {
                if (child is GeometryInstance gi)
                    Walk(gi, link, -1, 1, import.Document, hits);
            }
        }

        return hits;
    }

    private static void Walk(GeometryInstance gi, Transform parent, int parentIndex, int depth, Document doc, List<Hit> hits)
    {
        var t = parent.Multiply(gi.Transform);
        var symbol = gi.GetSymbolGeometry();
        var insert = new CadInsert
        {
            BlockName = Name(gi, doc),
            Layer = Layer(gi, symbol, doc),
            X = UnitConv.ToMm(t.Origin.X),
            Y = UnitConv.ToMm(t.Origin.Y),
            RotationDeg = Math.Atan2(t.BasisX.Y, t.BasisX.X) * 180 / Math.PI
        };
        hits.Add(new Hit(insert, parentIndex));
        if (depth >= MaxDepth)
            return;
        var me = hits.Count - 1;
        foreach (var child in symbol)
        {
            if (child is GeometryInstance inner)
                Walk(inner, t, me, depth + 1, doc, hits);
        }
    }

    private static string Name(GeometryInstance gi, Document doc)
    {
        try
        {
            var id = gi.GetSymbolGeometryId();
            var name = doc.GetElement(id.SymbolId)?.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                // Imports name their blocks "file.dwg.BLOCK".
                var cut = name!.LastIndexOf(".dwg.", StringComparison.OrdinalIgnoreCase);
                return cut >= 0 ? name.Substring(cut + 5) : name;
            }

            return "Block " + id.SymbolId.Value;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return "Block ?";
        }
    }

    private static string Layer(GeometryInstance gi, GeometryElement symbol, Document doc)
    {
        var style = gi.GraphicsStyleId;
        if (style == ElementId.InvalidElementId)
            style = symbol.Select(o => o.GraphicsStyleId).FirstOrDefault(s => s != ElementId.InvalidElementId) ?? ElementId.InvalidElementId;
        return doc.GetElement(style) is GraphicsStyle { GraphicsStyleCategory: { } c } ? c.Name : "0";
    }
}

/// <summary>Straight walls of this model and the links that stand on the level, as plan axes in millimetres.</summary>
internal static class DeviceWalls
{
    private const double ProbeMm = 1000;

    public static List<HostWallAxis> Collect(Document doc, Level level)
    {
        var result = ModelRevitHostWalls.At(doc, level.Name);
        var probe = level.ProjectElevation + UnitConv.MmToInternal(ProbeMm);
        foreach (var link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            var linkDoc = link.GetLinkDocument();
            if (linkDoc is null)
                continue;
            var t = link.GetTotalTransform();
            foreach (var wall in new FilteredElementCollector(linkDoc).OfClass(typeof(Wall)).Cast<Wall>())
            {
                if (wall.Location is not LocationCurve { Curve: Line line } || wall.Width <= 0 || wall.get_BoundingBox(null) is not { } box)
                    continue;
                var low = t.OfPoint(box.Min).Z;
                var high = t.OfPoint(box.Max).Z;
                if (Math.Min(low, high) > probe || Math.Max(low, high) < probe)
                    continue;
                var a = t.OfPoint(line.GetEndPoint(0));
                var b = t.OfPoint(line.GetEndPoint(1));
                result.Add(new HostWallAxis($"link{link.Id.Value}:{wall.Id.Value}",
                    UnitConv.ToMm(a.X), UnitConv.ToMm(a.Y), UnitConv.ToMm(b.X), UnitConv.ToMm(b.Y), UnitConv.ToMm(wall.Width)));
            }
        }

        return result;
    }
}

/// <summary>Block → type choices remembered across projects, so the next floor or job opens filled in.</summary>
internal static class DeviceMemory
{
    internal sealed class Saved
    {
        public string Kind { get; set; } = "";
        public string Type { get; set; } = "";
        public double Mm { get; set; }
    }

    private static string PathFor() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSSTech", "devices.json");

    public static Dictionary<string, Saved> Load()
    {
        try
        {
            if (File.Exists(PathFor()))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(PathFor()));
                if (map is not null)
                    return new Dictionary<string, Saved>(map, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DssTechLog.Write("devices memory: " + ex.Message);
        }

        return new Dictionary<string, Saved>(StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(IEnumerable<DeviceRow> rows)
    {
        var map = Load();
        foreach (var r in rows)
            map[r.Group.Name] = new Saved { Kind = r.Spec.Kind.ToString(), Type = DeviceSetupWindow.TypeName(r.Symbol), Mm = r.Mm };
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFor())!);
            File.WriteAllText(PathFor(), JsonSerializer.Serialize(map));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DssTechLog.Write("devices memory: " + ex.Message);
        }
    }
}

/// <summary>Table of the link's blocks: which device each becomes, its family type and height.</summary>
internal sealed class DeviceSetupWindow : DssDialog
{
    private const string Skip = "Bỏ qua";
    private const int MaxRows = 80;
    private readonly List<(DeviceBlockGroup Group, ComboBox Kind, ComboBox Type, TextBox Mm)> _items = new();
    private readonly IReadOnlyList<DeviceSpec> _kinds;
    private readonly Dictionary<DeviceKind, List<FamilySymbol>> _types = new();
    private readonly TextBox _extra;
    private readonly DeviceMount _mount;

    public List<DeviceRow> Rows { get; } = new();
    public double ReachMm { get; private set; } = 400;
    public double DefaultCeilingMm { get; private set; } = 2700;
    public bool IgnoreCeiling { get; private set; }
    public bool ConnectFlex { get; private set; }
    public double FlexReachMm { get; private set; } = 3000;
    private readonly CheckBox? _ignoreCeiling;
    private readonly CheckBox? _flex;
    private readonly TextBox? _flexReach;
    private readonly TextBox? _reach;

    public DeviceSetupWindow(Document doc, string title, DeviceMount mount, IReadOnlyList<DeviceSpec> kinds, List<DeviceBlockGroup> groups, string levelName)
        : base(title,
            mount switch
            {
                DeviceMount.Wall => $"Level {levelName}. Mỗi block của CAD thành một loại thiết bị: đặt lên mặt tường gần nhất (model hoặc link), cao độ lắp tính từ level.",
                DeviceMount.Hvac => $"Level {levelName}. Miệng gió, FCU, box gió từ block CAD: theo mặt trần phía trên (hoặc cao độ chỉ định); số âm = nằm trên trần. " +
                                    "Sau khi đặt, tự nối ống mềm từ cổng gió của thiết bị tới ống gió cùng hệ gần nhất.",
                DeviceMount.Fire => $"Level {levelName}. Tủ chữa cháy đặt lên mặt tường gần nhất (cao lắp = chân tủ từ level); đầu phun sprinkler, " +
                                    "đầu báo cháy dưới mặt trần phía trên (khoảng dưới trần 0 thì gắn vào mặt trần).",
                _ => $"Level {levelName}. Mỗi block của CAD thành một loại thiết bị: đặt dưới mặt trần phía trên (model hoặc link); khoảng dưới trần 0 thì gắn vào mặt trần."
            },
            860, 680)
    {
        _mount = mount;
        _kinds = kinds;
        foreach (var spec in kinds)
        {
            var cats = DeviceTool.Categories(spec.Category);
            // A fire cabinet is mechanical equipment too: it is not offered for an air box or a fan coil.
            _types[spec.Kind] = cats.Count > 0
                ? new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).WherePasses(new ElementMulticategoryFilter(cats)).Cast<FamilySymbol>()
                    .Where(s => DevicePlanner.IsFire(spec.Kind) || !DevicePlanner.IsFire(DevicePlanner.Guess(TypeName(s), "")))
                    .OrderBy(TypeName).ToList()
                : new List<FamilySymbol>();
        }

        var memory = DeviceMemory.Load();
        var rows = new StackPanel();
        var header = Row(Muted("Block CAD (số lượng)"), Muted("Loại thiết bị"), Muted("Family : Type"),
            Muted(mount switch
            {
                DeviceMount.Wall => "Cao lắp (mm)", DeviceMount.Hvac => "Dưới trần (mm, âm = trên)", DeviceMount.Fire => "Cao lắp / dưới trần (mm)",
                _ => "Dưới trần (mm)"
            }));
        foreach (var g in groups.Take(MaxRows))
        {
            DeviceSpec? spec = null;
            memory.TryGetValue(g.Name, out var saved);
            if (saved is not null && Enum.TryParse<DeviceKind>(saved.Kind, out var savedKind))
                spec = kinds.FirstOrDefault(s => s.Kind == savedKind);
            spec ??= kinds.FirstOrDefault(s => s.Kind == g.Guess);
            var kindBox = DssTheme.Combo(new[] { Skip }.Concat(kinds.Select(s => s.Label)), spec?.Label ?? Skip);
            var typeBox = DssTheme.Combo();
            var mm = DssTheme.Input(spec is null ? "" : (saved is not null && spec.Kind.ToString() == saved.Kind ? saved.Mm : spec.DefaultMm).ToString("0"));
            Fill(typeBox, spec, saved?.Type, g.Name);
            kindBox.SelectionChanged += (_, _) =>
            {
                var s = SpecOf(kindBox);
                Fill(typeBox, s, null, g.Name);
                mm.Text = s?.DefaultMm.ToString("0") ?? "";
            };
            var name = new TextBlock
            {
                Text = $"{g.Name} ({g.Count})",
                ToolTip = "Layer: " + string.Join(", ", g.Layers),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = DssTheme.Ink
            };
            rows.Children.Add(Row(name, kindBox, typeBox, mm));
            _items.Add((g, kindBox, typeBox, mm));
        }

        var missing = kinds.Where(s => _types[s.Kind].Count == 0).Select(s => s.Label).ToList();
        var note = new TextBlock
        {
            Text = (groups.Count > MaxRows ? $"Hiện {MaxRows}/{groups.Count} block (block đoán được loại xếp trước). " : "") +
                   (missing.Count > 0 ? $"Chưa có family trong project cho: {string.Join(", ", missing)} — load family rồi chạy lại." : ""),
            Foreground = DssTheme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        _extra = DssTheme.Input(mount == DeviceMount.Wall ? "400" : "2700");
        AddSection(1, "Block → thiết bị", header, rows, note);
        var settings = new List<UIElement>
        {
            DssTheme.Field(mount == DeviceMount.Wall ? "Khoảng tìm tường quanh block (mm)" : "Cao độ trần khi chưa có trần (mm, từ level)", _extra)
        };
        if (mount == DeviceMount.Fire)
        {
            _reach = DssTheme.Input("400");
            settings.Add(DssTheme.Field("Khoảng tìm tường quanh tủ chữa cháy (mm)", _reach));
        }

        if (mount == DeviceMount.Hvac)
        {
            _ignoreCeiling = DssTheme.Option("Cao độ chỉ định: bỏ qua trần, mọi thiết bị theo cao độ trần ở trên", false);
            _flex = DssTheme.Option("Tự nối ống mềm từ thiết bị tới ống gió cùng hệ gần nhất", true);
            _flexReach = DssTheme.Input("3000");
            settings.Add(_ignoreCeiling);
            settings.Add(_flex);
            settings.Add(DssTheme.Field("Khoảng tìm ống gió quanh thiết bị (mm, mặt bằng)", _flexReach));
        }

        AddSection(2, "Thiết lập", settings.ToArray());
        PrimaryButton.Content = "Dựng";
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
    }

    public bool ShowFor(IntPtr owner)
    {
        SetOwner(owner);
        return ShowDialog() == true;
    }

    public static string TypeName(FamilySymbol s) => $"{s.FamilyName} : {s.Name}";

    private DeviceSpec? SpecOf(ComboBox box)
    {
        var label = DssTheme.ComboValue(box);
        return _kinds.FirstOrDefault(s => s.Label == label);
    }

    /// <summary>The kind's types; the remembered one, else one whose name says the same kind as the block.</summary>
    private void Fill(ComboBox box, DeviceSpec? spec, string? remembered, string blockName)
    {
        var types = spec is null ? new List<FamilySymbol>() : _types[spec.Kind];
        var names = types.Select(TypeName).ToList();
        var pick = remembered is not null && names.Contains(remembered)
            ? remembered
            : names.FirstOrDefault(n => DevicePlanner.Guess(n, "") == spec?.Kind) ?? names.FirstOrDefault();
        DssTheme.FillCombo(box, names, pick);
        box.IsEnabled = spec is not null;
    }

    private bool Collect(out string error)
    {
        error = "";
        Rows.Clear();
        if (!double.TryParse(_extra.Text.Trim(), out var extra) || extra <= 0)
        {
            error = "Nhập số mm > 0 ở phần Thiết lập.";
            return false;
        }

        if (_mount == DeviceMount.Wall)
            ReachMm = extra;
        else
            DefaultCeilingMm = extra;
        if (_reach is not null)
        {
            if (!double.TryParse(_reach.Text.Trim(), out var wallReach) || wallReach <= 0)
            {
                error = "Khoảng tìm tường phải là số mm > 0.";
                return false;
            }

            ReachMm = wallReach;
        }

        IgnoreCeiling = _ignoreCeiling?.IsChecked == true;
        ConnectFlex = _flex?.IsChecked == true;
        if (_flexReach is not null)
        {
            if (!double.TryParse(_flexReach.Text.Trim(), out var reach) || reach <= 0)
            {
                error = "Khoảng tìm ống gió phải là số mm > 0.";
                return false;
            }

            FlexReachMm = reach;
        }
        foreach (var (group, kindBox, typeBox, mmBox) in _items)
        {
            var spec = SpecOf(kindBox);
            if (spec is null)
                continue;
            var type = _types[spec.Kind].FirstOrDefault(s => TypeName(s) == DssTheme.ComboValue(typeBox));
            if (type is null)
            {
                error = $"Block {group.Name}: chưa có family {spec.Label.ToLowerInvariant()} trong project.";
                return false;
            }

            if (!double.TryParse(mmBox.Text.Trim(), out var mm) || (mm < 0 && _mount != DeviceMount.Hvac))
            {
                error = $"Block {group.Name}: cao độ phải là số mm ≥ 0.";
                return false;
            }

            Rows.Add(new DeviceRow(group, spec, type, mm));
        }

        if (Rows.Count == 0)
        {
            error = "Chưa gán block nào thành thiết bị.";
            return false;
        }

        return true;
    }

    private static TextBlock Muted(string text) => new() { Text = text, Foreground = DssTheme.Muted };

    private static Grid Row(UIElement name, UIElement kind, UIElement type, UIElement mm)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var w in new[] { 2.0, 1.5, 2.6, 0.9 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        var cells = new[] { name, kind, type, mm };
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is not FrameworkElement cell)
                continue;
            cell.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }
}
