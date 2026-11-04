using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Room finishes, two parts (like the Room Finishing add-in): finish walls on the room side of every face bounding
/// the room (rough walls, structural columns and walls, linked ones too when the link is room bounding), broken at
/// doors and holed at windows; and a finish floor on each room's outline, running into the door openings to the
/// middle of the wall. The selected rooms, or every room of the plan's level.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ModelFinishWallCommand : IExternalCommand
{
    private const string Title = "DSSTech — Hoàn thiện";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
        var uidoc = uiApp.ActiveUIDocument;
        var doc = uidoc?.Document;
        var view = doc?.ActiveView;
        if (doc is null || view is null || uidoc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        if (!ModelRevitLevelContext.TryResolve(view, doc, out var baseLevel, out var topLevel, out var levelErr))
        {
            TaskDialog.Show(Title, levelErr);
            return Result.Cancelled;
        }

        var wallTypes = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
            .Where(t => t.Kind == WallKind.Basic && t.Width > 0)
            .OrderBy(t => t.Width).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var floorTypes = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
            .Where(t => !t.IsFoundationSlab)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedRooms = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<Room>().Where(r => r.Area > 0).ToList();

        var dlg = new ArchModelSetupWindow(Title,
            (selectedRooms.Count > 0 ? $"{selectedRooms.Count} phòng đang chọn" : $"Mọi phòng của level {baseLevel.Name}") +
            ". Tường hoàn thiện bám mặt tường thô, cột, vách (cả file link) phía trong phòng, ngắt tại cửa đi, khoét tại cửa sổ. " +
            "Sàn hoàn thiện theo đường bao phòng, chạy vào ô cửa tới giữa tường.");
        var doWalls = dlg.AddOption("Tường hoàn thiện", wallTypes.Count > 0);
        var wallBox = dlg.AddCombo("Type tường hoàn thiện", wallTypes.Select(t => t.Name),
            Prefer(wallTypes.Select(t => t.Name).ToList(), "trát", "trat", "hoàn thiện", "hoan thien", "finish", "plaster", "ốp"));
        var wallHeight = dlg.AddInput("Chiều cao tường hoàn thiện (mm, để trống = hết tầng; nhỏ = chân tường)", "");
        var doFloors = dlg.AddOption("Sàn hoàn thiện", floorTypes.Count > 0);
        var floorBox = dlg.AddCombo("Type sàn hoàn thiện", floorTypes.Select(t => t.Name),
            Prefer(floorTypes.Select(t => t.Name).ToList(), "hoàn thiện", "hoan thien", "finish", "lát", "lat", "gạch", "gach", "tile"));
        var floorTop = dlg.AddInput("Cao độ mặt sàn hoàn thiện so với level (mm)", "0");
        var intoDoors = dlg.AddOption("Sàn chạy vào ô cửa tới giữa tường", true);
        var makeRooms = dlg.AddOption("Tầng chưa có phòng thì tự đặt phòng (Place Rooms)", true);
        if (!dlg.ShowFor(uiApp.MainWindowHandle))
            return Result.Cancelled;

        var summary = new StringBuilder();
        var rooms = selectedRooms.Count > 0 ? selectedRooms : RoomsOn(doc, baseLevel);
        if (rooms.Count == 0 && makeRooms.IsChecked == true)
        {
            using var roomTx = new Transaction(doc, "DSSTech place rooms");
            roomTx.Start();
            var phase = doc.GetElement(view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() ?? ElementId.InvalidElementId) as Phase;
            var placed = phase is not null ? doc.Create.NewRooms2(baseLevel, phase) : doc.Create.NewRooms2(baseLevel);
            roomTx.Commit();
            summary.AppendLine($"Tầng chưa có phòng: đã đặt {placed.Count} phòng.");
            rooms = RoomsOn(doc, baseLevel);
        }

        if (rooms.Count == 0)
        {
            TaskDialog.Show(Title, $"Level {baseLevel.Name} chưa có phòng nào. Đặt phòng (Room) trước.");
            return Result.Cancelled;
        }

        summary.AppendLine($"Phòng: {rooms.Count}" + (selectedRooms.Count > 0 ? " (đang chọn)." : $" (level {baseLevel.Name})."));
        var openings = ArchModelOpenings.Collect(doc, baseLevel, masonryOnly: false, out _);
        if (doWalls.IsChecked == true && wallTypes.Count > 0)
        {
            var type = wallTypes.First(t => t.Name == DssTheme.ComboValue(wallBox));
            var heightMm = Mm(wallHeight.Text);
            if (heightMm is null && topLevel.Id == baseLevel.Id)
                summary.AppendLine("Tường hoàn thiện: không có level phía trên — nhập chiều cao.");
            else
                FinishWalls(doc, rooms, baseLevel, topLevel, type, heightMm, openings, summary);
        }

        if (doFloors.IsChecked == true && floorTypes.Count > 0)
        {
            var type = floorTypes.First(t => t.Name == DssTheme.ComboValue(floorBox));
            var top = double.TryParse(floorTop.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0;
            FinishFloors(doc, rooms, baseLevel, type, top, intoDoors.IsChecked == true, openings, summary);
        }

        var notBounding = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>()
            .Where(t => t.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING)?.AsInteger() == 0)
            .Select(t => t.Name)
            .ToList();
        if (notBounding.Count > 0)
            summary.AppendLine($"File link chưa bật Room Bounding nên cột/vách trong đó chưa bao phòng: {string.Join(", ", notBounding)}.");
        TaskDialog.Show(Title, summary.ToString());
        return Result.Succeeded;
    }

    private static void FinishWalls(
        Document doc, List<Room> rooms, Level baseLevel, Level topLevel, WallType type, double? heightMm,
        List<ArchModelOpenings.Entry> openings, StringBuilder summary)
    {
        var thicknessMm = UnitConv.ToMm(type.Width);
        var faces = new List<RoomFace>();
        var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
        var probeZ = baseLevel.Elevation + UnitConv.MmToInternal(1000);
        foreach (var room in rooms)
        {
            foreach (var loop in room.GetBoundarySegments(options) ?? new List<IList<BoundarySegment>>())
            {
                foreach (var seg in loop)
                {
                    if (seg.GetCurve() is not Line line || line.Length < UnitConv.MmToInternal(FinishWallPlanner.MinLengthMm))
                        continue;
                    var owner = doc.GetElement(seg.ElementId);
                    // Room separation lines and earlier finish walls are not faces to finish.
                    if (owner is ModelCurve || owner is Wall w && w.WallType.Id == type.Id)
                        continue;
                    if (owner is null && seg.LinkElementId == ElementId.InvalidElementId)
                        continue;
                    var a = line.GetEndPoint(0);
                    var b = line.GetEndPoint(1);
                    var d = (b - a).Normalize();
                    var n = new XYZ(-d.Y, d.X, 0);
                    var mid = (a + b) / 2;
                    var probe = UnitConv.MmToInternal(Math.Max(50, thicknessMm));
                    if (!room.IsPointInRoom(new XYZ(mid.X + n.X * probe, mid.Y + n.Y * probe, probeZ)))
                        n = n.Negate();
                    faces.Add(new RoomFace
                    {
                        X0 = UnitConv.ToMm(a.X), Y0 = UnitConv.ToMm(a.Y), X1 = UnitConv.ToMm(b.X), Y1 = UnitConv.ToMm(b.Y),
                        NormalX = n.X, NormalY = n.Y
                    });
                }
            }
        }

        var doors = openings.Where(o => o.IsDoor).Select(o => o.Opening).ToList();
        var windows = openings.Where(o => !o.IsDoor).Select(o => (o.Opening, o.SillMm)).ToList();
        var lines = FinishWallPlanner.Plan(faces, thicknessMm, doors, windows);
        int created = 0, skipped = 0, holes = 0, failed = 0;
        using (var tx = new Transaction(doc, "DSSTech finish walls"))
        {
            tx.Start();
            var existing = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Where(w => w.WallType.Id == type.Id && w.Location is LocationCurve { Curve: Line })
                .Select(w => (Line)((LocationCurve)w.Location).Curve)
                .ToList();
            var height = heightMm is double h ? UnitConv.MmToInternal(h) : topLevel.Elevation - baseLevel.Elevation;
            foreach (var l in lines)
            {
                var a = new XYZ(UnitConv.MmToInternal(l.X0), UnitConv.MmToInternal(l.Y0), baseLevel.Elevation);
                var b = new XYZ(UnitConv.MmToInternal(l.X1), UnitConv.MmToInternal(l.Y1), baseLevel.Elevation);
                if (existing.Any(e => SameLine(e, a, b)))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var wall = Wall.Create(doc, Line.CreateBound(a, b), type.Id, baseLevel.Id, height, 0, false, false);
                    if (heightMm is null)
                        wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(topLevel.Id);
                    // Rooms keep their rough-wall boundaries: the finish must not shrink them on the next run.
                    wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING)?.Set(0);
                    WallUtils.DisallowWallJoinAtEnd(wall, 0);
                    WallUtils.DisallowWallJoinAtEnd(wall, 1);
                    created++;
                    var u = (b - a).Normalize();
                    foreach (var (from, to, sill, head) in l.Holes)
                    {
                        // A skirting lower than the sill has nothing to hole.
                        if (sill >= UnitConv.ToMm(height))
                            continue;
                        var p1 = a + u * UnitConv.MmToInternal(from) + XYZ.BasisZ * UnitConv.MmToInternal(sill);
                        var p2 = a + u * UnitConv.MmToInternal(to) + XYZ.BasisZ * UnitConv.MmToInternal(Math.Min(head, UnitConv.ToMm(height)));
                        try
                        {
                            doc.Create.NewOpening(wall, p1, p2);
                            holes++;
                        }
                        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                        {
                            DssTechLog.Write($"finish wall {wall.Id.Value} window hole failed: {ex.Message}");
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    failed++;
                    DssTechLog.Write($"finish wall failed ({l.X0:0},{l.Y0:0})-({l.X1:0},{l.Y1:0}): {ex.Message}");
                }
            }

            tx.Commit();
        }

        summary.AppendLine(
            $"Tường hoàn thiện «{type.Name}» (dày {thicknessMm:0} mm, " +
            (heightMm is double hh ? $"cao {hh:0} mm" : $"{baseLevel.Name} → {topLevel.Name}") +
            $"): tạo {created}, đã có {skipped}" + (failed > 0 ? $", lỗi {failed} (xem log)" : "") +
            $"; ngắt tại cửa đi, khoét {holes} lỗ tại cửa sổ. Không bao phòng, không join.");
    }

    private static void FinishFloors(
        Document doc, List<Room> rooms, Level level, FloorType type, double topMm, bool intoDoors,
        List<ArchModelOpenings.Entry> openings, StringBuilder summary)
    {
        var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
        var probeZ = level.Elevation + UnitConv.MmToInternal(1000);
        int created = 0, skipped = 0, failed = 0, thresholds = 0;
        using var tx = new Transaction(doc, "DSSTech finish floors");
        tx.Start();
        var existing = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
            .Where(f => f.GetTypeId() == type.Id && f.LevelId == level.Id)
            .Select(f => f.get_BoundingBox(null))
            .Where(b => b is not null)
            .ToList();

        void Make(IList<CurveLoop> loops, string what)
        {
            var box = PlanBox(loops[0]);
            if (existing.Any(e => SameBox(e!, box)))
            {
                skipped++;
                return;
            }

            try
            {
                var floor = Floor.Create(doc, loops, type.Id, level.Id);
                floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(UnitConv.MmToInternal(topMm));
                floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.Set(0);
                created++;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
            {
                failed++;
                DssTechLog.Write($"finish floor {what} failed: {ex.Message}");
            }
        }

        foreach (var room in rooms)
        {
            var loops = new List<CurveLoop>();
            foreach (var segs in room.GetBoundarySegments(options) ?? new List<IList<BoundarySegment>>())
            {
                var loop = new CurveLoop();
                try
                {
                    foreach (var seg in segs)
                        loop.Append(Flat(seg.GetCurve(), level.Elevation));
                    if (!loop.IsOpen())
                        loops.Add(loop);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    DssTechLog.Write($"finish floor room {room.Id.Value} loop: {ex.Message}");
                }
            }

            // Outer loop first: Revit takes the others as holes (columns inside the room).
            loops = loops.OrderByDescending(l => Area(l)).ToList();
            if (loops.Count > 0)
                Make(loops, $"room {room.Number} {room.Name}");

            if (!intoDoors)
                continue;
            // The floor runs into each door of the room up to the wall's middle; the next room fills the other half.
            foreach (var door in openings.Where(o => o.IsDoor).Select(o => o.Opening))
            {
                var c = new XYZ(UnitConv.MmToInternal(door.X), UnitConv.MmToInternal(door.Y), level.Elevation);
                var u = new XYZ(door.DirX, door.DirY, 0);
                var n = new XYZ(-u.Y, u.X, 0);
                var half = UnitConv.MmToInternal(door.WallThicknessMm / 2);
                var reach = half + UnitConv.MmToInternal(100);
                var side = room.IsPointInRoom(new XYZ(c.X + n.X * reach, c.Y + n.Y * reach, probeZ)) ? 1
                    : room.IsPointInRoom(new XYZ(c.X - n.X * reach, c.Y - n.Y * reach, probeZ)) ? -1 : 0;
                if (side == 0)
                    continue;
                var w = UnitConv.MmToInternal(door.WidthMm / 2);
                var face = c + n * (side * half);
                var p0 = c - u * w;
                var p1 = c + u * w;
                var p2 = face + u * w;
                var p3 = face - u * w;
                var strip = CurveLoop.Create(new List<Curve>
                {
                    Line.CreateBound(p0, p1), Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p0)
                });
                Make(new List<CurveLoop> { strip }, $"door {door.Id}");
                thresholds++;
            }
        }

        tx.Commit();
        summary.AppendLine(
            $"Sàn hoàn thiện «{type.Name}» (mặt sàn {topMm:0} mm so với {level.Name}): tạo {created}, đã có {skipped}" +
            (failed > 0 ? $", lỗi {failed} (xem log)" : "") +
            (intoDoors ? $"; {thresholds} ô cửa có dải sàn tới giữa tường." : "."));
    }

    private static Curve Flat(Curve curve, double z)
    {
        var dz = z - curve.GetEndPoint(0).Z;
        return Math.Abs(dz) < 1e-9 ? curve : curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, dz)));
    }

    private static double Area(CurveLoop loop)
    {
        var pts = loop.SelectMany(c => c.Tessellate().Take(c.Tessellate().Count - 1)).ToList();
        double a = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }

        return Math.Abs(a) / 2;
    }

    private static (XYZ Min, XYZ Max) PlanBox(CurveLoop loop)
    {
        var pts = loop.SelectMany(c => c.Tessellate()).ToList();
        return (new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), 0), new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), 0));
    }

    private static bool SameBox(BoundingBoxXYZ e, (XYZ Min, XYZ Max) b)
    {
        var tol = UnitConv.MmToInternal(50);
        return Math.Abs(e.Min.X - b.Min.X) <= tol && Math.Abs(e.Min.Y - b.Min.Y) <= tol &&
               Math.Abs(e.Max.X - b.Max.X) <= tol && Math.Abs(e.Max.Y - b.Max.Y) <= tol;
    }

    private static double? Mm(string? text) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;

    private static string? Prefer(IReadOnlyList<string> names, params string[] keys) =>
        names.FirstOrDefault(n => keys.Any(k => n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) ?? names.FirstOrDefault();

    private static List<Room> RoomsOn(Document doc, Level level) =>
        new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
            .OfType<Room>()
            .Where(r => r.LevelId == level.Id && r.Area > 0)
            .ToList();

    private static bool SameLine(Line existing, XYZ a, XYZ b)
    {
        var tol = UnitConv.MmToInternal(3);
        var e0 = existing.GetEndPoint(0);
        var e1 = existing.GetEndPoint(1);
        bool Near(XYZ p, XYZ q) => Math.Abs(p.X - q.X) <= tol && Math.Abs(p.Y - q.Y) <= tol;
        return (Near(e0, a) && Near(e1, b)) || (Near(e0, b) && Near(e1, a));
    }
}
