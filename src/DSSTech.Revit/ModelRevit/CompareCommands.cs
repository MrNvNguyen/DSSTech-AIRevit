using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Dss.Cad2Bim.Core;
using Dss.McpContracts;
using Dss.McpHost.Create;
using DSSTech.Revit.Cad;
using DSSTech.Revit.Ui;
using Microsoft.Win32;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// «So sánh»: the model against the drawing (the CAD link, or a new revision not linked yet), or the old drawing
/// against the new one. Results list what is missing, extra, moved or changed; clouds, colours, moves and Excel
/// from there. Nothing is changed unless a row is moved on request.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class DrawingCompareCommand : IExternalCommand
{
    private const string Title = "DSSTech — So sánh bản vẽ";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
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
            TaskDialog.Show(Title, levelErr);
            return Result.Cancelled;
        }

        try
        {
            var import = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<ImportInstance>().FirstOrDefault();
            if (import is null)
            {
                var picked = uidoc.Selection.PickObject(ObjectType.Element, new ImportInstanceSelectionFilter(),
                    "Chọn CAD link đang dùng để dựng mô hình.");
                import = doc.GetElement(picked.ElementId) as ImportInstance;
            }

            if (import is null)
                return Result.Cancelled;

            var td = new TaskDialog(Title)
            {
                MainInstruction = "So sánh gì?",
                MainContent = $"Level {level.Name} · CAD link {import.Category?.Name ?? import.Name}",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Mô hình ↔ CAD link này",
                "Tìm cấu kiện thiếu, thừa, lệch vị trí, khác size so với bản vẽ đang link.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Mô hình ↔ bản vẽ mới (chọn file DWG/DXF)",
                "Bản cập nhật chưa link: tool link tạm đúng vị trí link này, đọc xong thì bỏ.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "CAD link này ↔ bản vẽ mới (chỉ so bản vẽ)",
                "Khoanh vùng nét, block, chữ được thêm / xoá / sửa giữa hai bản vẽ.");
            var mode = td.Show();
            if (mode is not (TaskDialogResult.CommandLink1 or TaskDialogResult.CommandLink2 or TaskDialogResult.CommandLink3))
                return Result.Cancelled;

            CadLinkLayerVisibility.EnsureAllImportLayersVisible(view, import);
            var current = ImportInstanceGeometryReader.Read(import, view);
            CadGeometry? revision = null;
            var revisionName = "";
            if (mode != TaskDialogResult.CommandLink1)
            {
                var open = new OpenFileDialog { Filter = "Bản vẽ CAD (*.dwg;*.dxf)|*.dwg;*.dxf", Title = "Chọn bản vẽ cập nhật" };
                if (open.ShowDialog() != true)
                    return Result.Cancelled;
                revisionName = System.IO.Path.GetFileName(open.FileName);
                revision = CompareSources.ReadUnlinked(doc, view, import, open.FileName, out var readError);
                if (revision is null)
                {
                    TaskDialog.Show(Title, "Không đọc được bản vẽ mới: " + readError);
                    return Result.Failed;
                }
            }

            List<CompareResult> results;
            string subject;
            if (mode == TaskDialogResult.CommandLink3)
            {
                results = CadDiff.Compare(current, revision!);
                subject = $"CAD link ↔ {revisionName}";
            }
            else
            {
                var drawing = revision ?? current;
                var setup = new CompareSetupWindow(drawing, level.Name);
                if (!setup.ShowFor(uiApp.MainWindowHandle))
                    return Result.Cancelled;
                results = CompareSources.ModelAgainst(doc, view, level, topLevel, drawing, import, setup);
                subject = $"Mô hình ↔ {(revision is null ? "CAD link" : revisionName)}";
            }

            if (results.Count == 0)
            {
                TaskDialog.Show(Title, "Không có gì để so (bản vẽ trống hoặc chưa chọn hạng mục).");
                return Result.Cancelled;
            }

            CompareResultsWindow.ShowFor(uiApp, level, subject, results);
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
    }
}

/// <summary>What the drawing asks for and what the model holds, as items the comparer reads.</summary>
internal static class CompareSources
{
    private const double RiseMm = 1000;
    private const double ExtentMarginMm = 1000;

    /// <summary>
    /// A drawing not linked yet: linked for a moment in a transaction that is rolled back, placed where the current
    /// link sits, read, and gone again.
    /// </summary>
    public static CadGeometry? ReadUnlinked(Document doc, View view, ImportInstance like, string path, out string error)
    {
        error = "";
        using var tx = new Transaction(doc, "DSSTech compare read revision");
        tx.Start();
        try
        {
            var options = new DWGImportOptions { Placement = ImportPlacement.Origin, ThisViewOnly = false };
            if (!doc.Link(path, options, view, out var id) || doc.GetElement(id) is not ImportInstance temp)
            {
                error = "Revit không link được file.";
                return null;
            }

            doc.Regenerate();
            var want = like.GetTotalTransform();
            var have = temp.GetTotalTransform();
            var turn = Math.Atan2(want.BasisX.Y, want.BasisX.X) - Math.Atan2(have.BasisX.Y, have.BasisX.X);
            if (Math.Abs(turn) > 1e-9)
            {
                ElementTransformUtils.RotateElement(doc, id, Line.CreateBound(have.Origin, have.Origin + XYZ.BasisZ), turn);
                doc.Regenerate();
                have = temp.GetTotalTransform();
            }

            var shift = want.Origin - have.Origin;
            if (shift.GetLength() > 1e-9)
            {
                ElementTransformUtils.MoveElement(doc, id, new XYZ(shift.X, shift.Y, 0));
                doc.Regenerate();
            }

            return ImportInstanceGeometryReader.Read(temp, view);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            tx.RollBack();
        }
    }

    public static List<CompareResult> ModelAgainst(Document doc, View view, Level level, Level topLevel, CadGeometry geom, ImportInstance import,
        CompareSetupWindow setup)
    {
        var cad = new List<CompareItem>();
        var model = new List<CompareItem>();
        var results = new List<CompareResult>();
        var notes = new List<string>();
        var extent = Extent(geom);
        bool Inside(CompareItem i) =>
            Math.Max(i.X0, i.X1) >= extent.MinX && Math.Min(i.X0, i.X1) <= extent.MaxX &&
            Math.Max(i.Y0, i.Y1) >= extent.MinY && Math.Min(i.Y0, i.Y1) <= extent.MaxY;

        void Part(string kind, Action run)
        {
            try
            {
                run();
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException
                                           or KeyNotFoundException or JsonException)
            {
                notes.Add($"{kind}: {ex.Message}");
                DssTechLog.Write($"compare {kind}: {ex}");
            }
        }

        var grids = ModelRevitGrids.Intersections(doc, view);
        var angles = ModelRevitGrids.Angles(doc, view);
        var placement = new ModelRevitPlacement { BaseLevelName = level.Name, TopLevelName = topLevel.Name };
        var size = new AnnotationSize(null, null, null, "");
        IEnumerable<CreateOperation> Plan(string layer, ModelRevitPartKind part, ModelRevitPlacement? place = null) =>
            ModelRevitPlanner.Plan(geom, layer, part, level.Name, topLevel.Name, size, gridPoints: grids, placement: place ?? placement, gridAnglesDeg: angles);

        if (setup.Grids)
        {
            Part("Trục", () =>
            {
                cad.AddRange(GridPlanner.Plan(geom).Select(g => LineItem("Trục", g.X0, g.Y0, g.X1, g.Y1, g.Name)));
                foreach (var g in new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Grid)).Cast<Autodesk.Revit.DB.Grid>())
                {
                    if (g.Curve is Line l)
                        model.Add(LineItem("Trục", l.GetEndPoint(0), l.GetEndPoint(1), g.Name, g.Id.Value));
                }
            });
        }

        if (setup.Layer("Cột") is string columnLayer)
        {
            Part("Cột", () =>
            {
                cad.AddRange(Plan(columnLayer, ModelRevitPartKind.Column).Select(op =>
                    PointItem("Cột", Arr(op, "xyzMm"), Size(Num(op, "widthMm"), Num(op, "depthMm")))));
                foreach (var fi in Instances(doc, level, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns))
                {
                    if (fi.Location is LocationPoint lp && fi.get_BoundingBox(null) is { } box)
                    {
                        var dims = new[] { UnitConv.ToMm(box.Max.X - box.Min.X), UnitConv.ToMm(box.Max.Y - box.Min.Y) };
                        model.Add(PointItem("Cột", lp.Point, Size(dims[0], dims[1]), fi.Id.Value));
                    }
                }
            });
        }

        foreach (var (kind, structural) in new[] { ("Tường", false), ("Vách", true) })
        {
            if (setup.Layer(kind) is not string wallLayer)
                continue;
            Part(kind, () =>
            {
                var part = structural ? ModelRevitPartKind.StructuralWall : ModelRevitPartKind.ArchitecturalWall;
                cad.AddRange(Plan(wallLayer, part).Where(op => op.Category == "Walls").Select(op =>
                {
                    var s = Arr(op, "startMm");
                    var e = Arr(op, "endMm");
                    return new CompareItem { Kind = kind, Shape = CompareShape.Line, X0 = s[0], Y0 = s[1], X1 = e[0], Y1 = e[1], Size = $"{Num(op, "thicknessMm"):0}" };
                }));
                foreach (var wall in Walls(doc, level).Where(w => IsStructural(w) == structural))
                {
                    var (a, b) = Center(wall);
                    var item = LineItem(kind, a, b, "", wall.Id.Value);
                    item.Size = $"{UnitConv.ToMm(wall.Width):0}";
                    model.Add(item);
                }
            });
        }

        if (setup.Layer("Dầm") is string beamLayer)
        {
            Part("Dầm", () =>
            {
                var beamPlace = new ModelRevitPlacement
                {
                    BaseLevelName = level.Name,
                    TopLevelName = topLevel.Name,
                    Supports = ModelRevitSupports.Collect(doc, topLevel, 0, out _, out _)
                };
                cad.AddRange(Plan(beamLayer, ModelRevitPartKind.Beam, beamPlace).Where(op => op.Category == "StructuralFraming").Select(op =>
                {
                    var s = Arr(op, "startMm");
                    var e = Arr(op, "endMm");
                    return new CompareItem { Kind = "Dầm", Shape = CompareShape.Line, X0 = s[0], Y0 = s[1], X1 = e[0], Y1 = e[1] };
                }));
                foreach (var fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType()
                             .OfType<FamilyInstance>())
                {
                    if (fi.Location is LocationCurve { Curve: Line l } && ReferenceLevel(fi) == topLevel.Id)
                        model.Add(LineItem("Dầm", l.GetEndPoint(0), l.GetEndPoint(1), "", fi.Id.Value));
                }
            });
        }

        foreach (var (kind, part, bic) in new[]
                 {
                     ("Cửa đi", ModelRevitPartKind.Door, BuiltInCategory.OST_Doors),
                     ("Cửa sổ", ModelRevitPartKind.Window, BuiltInCategory.OST_Windows)
                 })
        {
            if (setup.Layer(kind) is not string openingLayer)
                continue;
            Part(kind, () =>
            {
                var place = new ModelRevitPlacement
                {
                    BaseLevelName = level.Name,
                    TopLevelName = topLevel.Name,
                    HostWalls = ModelRevitHostWalls.At(doc, level.Name),
                    InsertTypeMarks = TypeMarks(doc, bic)
                };
                foreach (var op in Plan(openingLayer, part, place))
                {
                    var item = PointItem(kind, Arr(op, "xyzMm"), "");
                    item.Name = Str(op, "typeMark");
                    cad.Add(item);
                }

                foreach (var fi in new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
                {
                    if (fi.LevelId != level.Id || fi.Location is not LocationPoint lp)
                        continue;
                    var item = PointItem(kind, lp.Point, "", fi.Id.Value);
                    item.Name = fi.Symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString()?.Trim() ?? "";
                    model.Add(item);
                }
            });
        }

        if (setup.Rooms)
            Part("Phòng", () => results.AddRange(Rooms(doc, level, geom, extent)));

        if (setup.MepKind is { } mep)
        {
            Part(mep.Label, () =>
            {
                var net = MepRoutePlanner.Build(geom.Entities, null, new MepSize(0, 0, 0), geom.Entities.OfType<CadText>());
                foreach (var e in net.Edges)
                {
                    var (x0, y0) = net.Nodes[e.A];
                    var (x1, y1) = net.Nodes[e.B];
                    cad.Add(new CompareItem
                    {
                        Kind = mep.Label, Shape = CompareShape.Line, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1,
                        Size = e.SizeFrom is "text" or "layer" or "inherited" ? e.Size.ToString() : "", Layer = e.Layer
                    });
                }

                foreach (var c in new FilteredElementCollector(doc).OfClass(mep.Type).Cast<MEPCurve>())
                {
                    if (c.ReferenceLevel?.Id != level.Id || c.Location is not LocationCurve { Curve: Line l })
                        continue;
                    var a = l.GetEndPoint(0);
                    var b = l.GetEndPoint(1);
                    if (new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength() < UnitConv.MmToInternal(10))
                        continue;
                    var item = LineItem(mep.Label, a, b, "", c.Id.Value);
                    item.Size = MepSizeText(c);
                    model.Add(item);
                }
            });
        }

        if (setup.Devices)
            Part("Thiết bị", () => Devices(doc, level, topLevel, import, cad, model));

        results.AddRange(DrawingCompare.Compare(cad, model.Where(m => m.Kind == "Trục" || Inside(m)).ToList(),
            new CompareOptions { ToleranceMm = setup.ToleranceMm }));
        foreach (var n in notes)
            results.Add(new CompareResult { Status = CompareStatus.Changed, Kind = "Lỗi đọc", Detail = n });
        return results;
    }

    /// <summary>Rooms by their name labels: the room at each label in the model, its name, and rooms with no label.</summary>
    private static IEnumerable<CompareResult> Rooms(Document doc, Level level, CadGeometry geom, (double MinX, double MinY, double MaxX, double MaxY) extent)
    {
        var labels = RoomPlanner.Scan(geom.Entities.OfType<CadText>(), TypeMarks(doc, BuiltInCategory.OST_Doors).Concat(TypeMarks(doc, BuiltInCategory.OST_Windows)).ToList());
        var z = level.Elevation + UnitConv.MmToInternal(RiseMm);
        var seen = new HashSet<ElementId>();
        foreach (var label in labels)
        {
            var at = new XYZ(UnitConv.MmToInternal(label.X), UnitConv.MmToInternal(label.Y), z);
            var room = doc.GetRoomAtPoint(at);
            var cadItem = new CompareItem { Kind = "Phòng", Shape = CompareShape.Point, X0 = label.X, Y0 = label.Y, X1 = label.X, Y1 = label.Y, Name = label.Name };
            if (room is null || room.LevelId != level.Id)
            {
                yield return new CompareResult
                {
                    Status = CompareStatus.Missing, Kind = "Phòng", Cad = cadItem, Detail = $"Phòng «{label.Name}» chưa có trong mô hình",
                    MinX = label.X, MinY = label.Y, MaxX = label.X, MaxY = label.Y
                };
                continue;
            }

            seen.Add(room.Id);
            var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            var modelItem = new CompareItem { Kind = "Phòng", Shape = CompareShape.Point, X0 = label.X, Y0 = label.Y, X1 = label.X, Y1 = label.Y, Name = name, Id = room.Id.Value };
            var same = string.Equals(DevicePlanner.Plain(name), DevicePlanner.Plain(label.Name), StringComparison.Ordinal);
            yield return new CompareResult
            {
                Status = same ? CompareStatus.Match : CompareStatus.Changed, Kind = "Phòng", Cad = cadItem, Model = modelItem,
                Detail = same ? "" : $"Phòng: bản vẽ «{label.Name}», mô hình «{name}»",
                MinX = label.X, MinY = label.Y, MaxX = label.X, MaxY = label.Y
            };
        }

        foreach (var room in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>())
        {
            if (room.LevelId != level.Id || room.Area <= 0 || seen.Contains(room.Id) || room.Location is not LocationPoint lp)
                continue;
            var x = UnitConv.ToMm(lp.Point.X);
            var y = UnitConv.ToMm(lp.Point.Y);
            if (x < extent.MinX || x > extent.MaxX || y < extent.MinY || y > extent.MaxY)
                continue;
            var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            yield return new CompareResult
            {
                Status = CompareStatus.Extra, Kind = "Phòng",
                Model = new CompareItem { Kind = "Phòng", Shape = CompareShape.Point, X0 = x, Y0 = y, X1 = x, Y1 = y, Name = name, Id = room.Id.Value },
                Detail = $"Phòng «{name}» không còn tên trên bản vẽ", MinX = x, MinY = y, MaxX = x, MaxY = y
            };
        }
    }

    /// <summary>Device blocks of the drawing (the kinds remembered or guessed for each block name) against the families.</summary>
    private static void Devices(Document doc, Level level, Level topLevel, ImportInstance import, List<CompareItem> cad, List<CompareItem> model)
    {
        var memory = DeviceMemory.Load();
        var kinds = new HashSet<DeviceKind>();
        foreach (var hit in DeviceBlocks.Read(import))
        {
            var name = hit.Insert.BlockName;
            DeviceKind? kind = memory.TryGetValue(name, out var saved) && Enum.TryParse<DeviceKind>(saved.Kind, out var k)
                ? k
                : DevicePlanner.Guess(name, hit.Insert.Layer);
            if (kind is not DeviceKind dk)
                continue;
            kinds.Add(dk);
            cad.Add(new CompareItem
            {
                Kind = DevicePlanner.Spec(dk).Label, Shape = CompareShape.Point,
                X0 = hit.Insert.X, Y0 = hit.Insert.Y, X1 = hit.Insert.X, Y1 = hit.Insert.Y, Layer = hit.Insert.Layer
            });
        }

        // Wall devices sit on the wall face, a little off the block: the comparer's point reach covers it.
        foreach (var dk in kinds)
        {
            var spec = DevicePlanner.Spec(dk);
            if (!Enum.TryParse<BuiltInCategory>(spec.Category, out var bic))
                continue;
            foreach (var fi in new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                if (fi.Location is LocationPoint lp && lp.Point.Z >= level.Elevation - 0.5 && lp.Point.Z < topLevel.Elevation)
                    model.Add(PointItem(spec.Label, lp.Point, "", fi.Id.Value));
            }
        }
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Extent(CadGeometry geom)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var e in geom.Entities)
        {
            switch (e)
            {
                case CadLine l:
                    xs.Add(l.X0); xs.Add(l.X1); ys.Add(l.Y0); ys.Add(l.Y1);
                    break;
                case CadPolyline p:
                    xs.AddRange(p.Points.Select(q => q.X)); ys.AddRange(p.Points.Select(q => q.Y));
                    break;
                case CadInsert i:
                    xs.Add(i.X); ys.Add(i.Y);
                    break;
            }
        }

        return xs.Count == 0
            ? (double.MinValue, double.MinValue, double.MaxValue, double.MaxValue)
            : (xs.Min() - ExtentMarginMm, ys.Min() - ExtentMarginMm, xs.Max() + ExtentMarginMm, ys.Max() + ExtentMarginMm);
    }

    private static IEnumerable<FamilyInstance> Instances(Document doc, Level level, params BuiltInCategory[] categories)
    {
        var floor = level.Elevation;
        var tol = UnitConv.MmToInternal(10);
        var rise = UnitConv.MmToInternal(RiseMm);
        foreach (var bic in categories)
        {
            foreach (var fi in new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                var box = fi.get_BoundingBox(null);
                if (box is not null && box.Min.Z <= floor + tol && box.Max.Z >= floor + rise)
                    yield return fi;
            }
        }
    }

    private static IEnumerable<Wall> Walls(Document doc, Level level)
    {
        var floor = level.Elevation;
        var tol = UnitConv.MmToInternal(10);
        var rise = UnitConv.MmToInternal(RiseMm);
        foreach (var wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
        {
            if (wall.Location is not LocationCurve { Curve: Line } || wall.Width <= 0)
                continue;
            var box = wall.get_BoundingBox(null);
            if (wall.LevelId == level.Id || (box is not null && box.Min.Z <= floor + tol && box.Max.Z >= floor + rise))
                yield return wall;
        }
    }

    private static bool IsStructural(Wall wall) => wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() == 1;

    /// <summary>The wall's centerline, whatever its Location Line (the drawing's wall runs are centerlines).</summary>
    private static (XYZ A, XYZ B) Center(Wall wall)
    {
        var line = (Line)((LocationCurve)wall.Location).Curve;
        var key = (WallLocationLine)(wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.AsInteger() ?? 0);
        var half = wall.Width / 2;
        var shift = key switch
        {
            WallLocationLine.FinishFaceExterior or WallLocationLine.CoreExterior => -half,
            WallLocationLine.FinishFaceInterior or WallLocationLine.CoreInterior => half,
            _ => 0
        };
        var move = wall.Orientation * shift;
        return (line.GetEndPoint(0) + move, line.GetEndPoint(1) + move);
    }

    private static ElementId ReferenceLevel(FamilyInstance fi) =>
        fi.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId() ?? fi.LevelId;

    private static List<string> TypeMarks(Document doc, BuiltInCategory category) =>
        new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(category).Cast<FamilySymbol>()
            .Select(s => s.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString()?.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string MepSizeText(MEPCurve c)
    {
        double Mm(BuiltInParameter bip) => c.get_Parameter(bip) is { StorageType: StorageType.Double } p ? UnitConv.ToMm(p.AsDouble()) : 0;
        return c switch
        {
            Pipe => $"Ø{Mm(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM):0}",
            Duct when Mm(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM) > 0 => $"Ø{Mm(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM):0}",
            Duct => $"{Mm(BuiltInParameter.RBS_CURVE_WIDTH_PARAM):0}x{Mm(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM):0}",
            CableTray => $"{Mm(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM):0}x{Mm(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM):0}",
            _ => ""
        };
    }

    private static string Size(double? a, double? b) => a is > 0 && b is > 0 ? $"{a:0}x{b:0}" : "";

    private static CompareItem PointItem(string kind, double[] xyz, string size, long id = 0) =>
        new() { Kind = kind, Shape = CompareShape.Point, X0 = xyz[0], Y0 = xyz[1], X1 = xyz[0], Y1 = xyz[1], Size = size, Id = id };

    private static CompareItem PointItem(string kind, XYZ p, string size, long id = 0) =>
        PointItem(kind, new[] { UnitConv.ToMm(p.X), UnitConv.ToMm(p.Y) }, size, id);

    private static CompareItem LineItem(string kind, double x0, double y0, double x1, double y1, string name, long id = 0) =>
        new() { Kind = kind, Shape = CompareShape.Line, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Name = name, Id = id };

    private static CompareItem LineItem(string kind, XYZ a, XYZ b, string name, long id = 0) =>
        LineItem(kind, UnitConv.ToMm(a.X), UnitConv.ToMm(a.Y), UnitConv.ToMm(b.X), UnitConv.ToMm(b.Y), name, id);

    private static double[] Arr(CreateOperation op, string name) =>
        op.Fields.GetProperty(name).EnumerateArray().Select(v => v.GetDouble()).ToArray();

    private static double? Num(CreateOperation op, string name) =>
        op.Fields.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string Str(CreateOperation op, string name) =>
        op.Fields.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

internal sealed record MepCompareKind(string Label, Type Type);

/// <summary>What to compare and which layer of the drawing holds each kind.</summary>
internal sealed class CompareSetupWindow : DssDialog
{
    private const string None = "(không so)";

    private static readonly (string Kind, string[] Words)[] LayerKinds =
    {
        ("Cột", new[] { "COT", "COL", "COLUMN" }),
        ("Vách", new[] { "VACH", "SHEAR", "CORE" }),
        ("Tường", new[] { "TUONG", "WALL" }),
        ("Dầm", new[] { "DAM", "BEAM" }),
        ("Cửa đi", new[] { "DOOR", "CUA DI", "CUADI" }),
        ("Cửa sổ", new[] { "WINDOW", "WIN", "CUA SO", "CUASO" })
    };

    private static readonly MepCompareKind[] MepKinds =
    {
        new("Ống gió", typeof(Duct)), new("Ống nước", typeof(Pipe)), new("Máng cáp", typeof(CableTray))
    };

    private readonly Dictionary<string, ComboBox> _layers = new();
    private readonly CheckBox _grids;
    private readonly CheckBox _rooms;
    private readonly CheckBox _devices;
    private readonly ComboBox _mep;
    private readonly TextBox _tolerance;

    public bool Grids => _grids.IsChecked == true;
    public bool Rooms => _rooms.IsChecked == true;
    public bool Devices => _devices.IsChecked == true;
    public MepCompareKind? MepKind => MepKinds.FirstOrDefault(m => m.Label == DssTheme.ComboValue(_mep));
    public double ToleranceMm { get; private set; } = 10;

    public string? Layer(string kind) =>
        _layers.TryGetValue(kind, out var box) && DssTheme.ComboValue(box) is var v && v != None && v.Length > 0 ? v : null;

    public CompareSetupWindow(CadGeometry geom, string levelName)
        : base("DSSTech — So sánh bản vẽ",
            $"Level {levelName}. Bản vẽ được đọc như khi dựng (cùng layer, cùng cách định vị chẵn 5 mm) rồi đối chiếu với mô hình. Không sửa gì mô hình.",
            640, 720)
    {
        var layers = geom.Entities.Select(e => e.Layer).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
        var rows = new List<UIElement>();
        foreach (var (kind, words) in LayerKinds)
        {
            var guess = layers.FirstOrDefault(l => words.Any(w => (" " + DevicePlanner.Plain(l) + " ").Contains(" " + w + " ") ||
                                                                  DevicePlanner.Plain(l).Replace(" ", "").Contains(w.Replace(" ", ""))));
            var box = DssTheme.Combo(new[] { None }.Concat(layers), guess ?? None);
            _layers[kind] = box;
            rows.Add(DssTheme.Field($"{kind} — layer CAD", box));
        }

        _grids = DssTheme.Option("Trục (theo tên trục, mọi layer)", true);
        _rooms = DssTheme.Option("Phòng (theo tên phòng trên bản vẽ)", true);
        _devices = DssTheme.Option("Thiết bị (block ổ cắm, công tắc, đèn, sprinkler, báo cháy…)", false);
        _mep = DssTheme.Combo(new[] { None }.Concat(MepKinds.Select(m => m.Label)), None);
        _tolerance = DssTheme.Input("10");
        AddSection(1, "Kết cấu & kiến trúc theo layer", rows.ToArray());
        AddSection(2, "Tự đọc", _grids, _rooms, _devices,
            DssTheme.Field("Hệ MEP của bản vẽ này (cả link là một hệ)", _mep));
        AddSection(3, "Dung sai", DssTheme.Field("Lệch quá bao nhiêu thì báo (mm)", _tolerance));
        PrimaryButton.Content = "So sánh";
        PrimaryButton.Click += (_, _) =>
        {
            if (!double.TryParse(_tolerance.Text.Trim(), out var tol) || tol <= 0)
            {
                MessageBox.Show(this, "Dung sai phải là số mm > 0.", "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ToleranceMm = tol;
            DialogResult = true;
            Close();
        };
    }

    public bool ShowFor(IntPtr owner)
    {
        SetOwner(owner);
        return ShowDialog() == true;
    }
}

/// <summary>One line of the results table.</summary>
internal sealed class CompareRow
{
    public int Index { get; set; }
    public CompareResult Result { get; set; } = new();
    public string Status => CompareResult.StatusText(Result.Status);
    public string Kind => Result.Kind;
    public string Detail => Result.Detail;
    public string Offset => Result.OffsetMm > 0.5 ? Result.OffsetMm.ToString("0") : "";
    public string Id => Result.Model?.Id is > 0 ? Result.Model.Id.ToString() : "";
    public string Note { get; set; } = "";
}

internal sealed class CompareResultsWindow : DssDialog
{
    private const double CloudMarginMm = 500;
    private const double CloudMinMm = 1000;
    private readonly UIApplication _app;
    private readonly Level _level;
    private readonly string _subject;
    private readonly List<CompareRow> _all;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly CheckBox _showMatches;

    private CompareResultsWindow(UIApplication app, Level level, string subject, List<CompareResult> results)
        : base("DSSTech — Kết quả so sánh", Summary(subject, results), 1100, 760)
    {
        _app = app;
        _level = level;
        _subject = subject;
        _all = results.OrderBy(r => r.Status == CompareStatus.Match ? 1 : 0).ThenBy(r => r.Kind).ThenBy(r => r.Status)
            .Select((r, i) => new CompareRow { Index = i + 1, Result = r }).ToList();
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            Height = 420,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        foreach (var (header, path) in new[]
                 {
                     ("#", nameof(CompareRow.Index)), ("Trạng thái", nameof(CompareRow.Status)), ("Hạng mục", nameof(CompareRow.Kind)),
                     ("Mô tả", nameof(CompareRow.Detail)), ("Lệch (mm)", nameof(CompareRow.Offset)), ("ID", nameof(CompareRow.Id)),
                     ("Ghi chú", nameof(CompareRow.Note))
                 })
            _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path) });
        _status = new TextBlock { Foreground = DssTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        _showMatches = DssTheme.Option("Hiện cả dòng khớp", false);
        _showMatches.Click += (_, _) => Filter();
        Filter();

        var actions = new WrapPanel();
        void Act(string text, Action run)
        {
            var b = DssTheme.Quiet(text);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Click += (_, _) =>
            {
                try
                {
                    run();
                }
                catch (Exception ex)
                {
                    _status.Text = "Lỗi: " + ex.Message;
                    DssTechLog.Write($"compare action: {ex}");
                }

                _grid.Items.Refresh();
            };
            actions.Children.Add(b);
        }

        Act("Xem trong model", ShowSelected);
        Act("Vẽ đám mây (revision cloud)", Clouds);
        Act("Tô màu phần tử", Colour);
        Act("Dời theo bản vẽ (dòng lệch)", MoveToDrawing);
        Act("Xuất Excel", ExportExcel);
        AddSection(1, "Danh sách", _showMatches, _grid, _status);
        AddSection(2, "Thao tác", actions);
        PrimaryButton.Content = "Đóng";
        PrimaryButton.Click += (_, _) => Close();
        CancelButton.Visibility = System.Windows.Visibility.Collapsed;
    }

    public static void ShowFor(UIApplication app, Level level, string subject, List<CompareResult> results)
    {
        var w = new CompareResultsWindow(app, level, subject, results);
        w.SetOwner(app.MainWindowHandle);
        w.ShowDialog();
    }

    private Document Doc => _app.ActiveUIDocument.Document;

    private static string Summary(string subject, List<CompareResult> results)
    {
        var counts = results.Where(r => r.Status != CompareStatus.Match).GroupBy(r => r.Status)
            .Select(g => $"{CompareResult.StatusText(g.Key).ToLowerInvariant()} {g.Count()}");
        var matched = results.Count(r => r.Status == CompareStatus.Match);
        return $"{subject}: " + string.Join(", ", counts) + (matched > 0 ? $"; khớp {matched}" : "") +
               ". Chọn dòng (Ctrl/Shift để chọn nhiều) rồi bấm thao tác; không chọn thì áp cho mọi dòng đang hiện.";
    }

    private void Filter() =>
        _grid.ItemsSource = _showMatches.IsChecked == true ? _all : _all.Where(r => r.Result.Status != CompareStatus.Match).ToList();

    private List<CompareRow> Selected()
    {
        var rows = _grid.SelectedItems.Cast<CompareRow>().ToList();
        return rows.Count > 0 ? rows : ((IEnumerable<CompareRow>)_grid.ItemsSource).ToList();
    }

    private void ShowSelected()
    {
        if (_grid.SelectedItem is not CompareRow row)
        {
            _status.Text = "Chọn một dòng.";
            return;
        }

        var r = row.Result;
        var uidoc = _app.ActiveUIDocument;
        var id = r.Model?.Id is > 0 ? new ElementId(r.Model.Id) : ElementId.InvalidElementId;
        if (id != ElementId.InvalidElementId && Doc.GetElement(id) is not null)
        {
            uidoc.Selection.SetElementIds(new[] { id });
            uidoc.ShowElements(id);
        }
        else
        {
            uidoc.Selection.SetElementIds(Array.Empty<ElementId>());
            var ui = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == Doc.ActiveView.Id);
            var (min, max) = Box(r, 3000);
            ui?.ZoomAndCenterRectangle(min, max);
        }

        _status.Text = $"#{row.Index} {row.Status} — {r.Detail}";
    }

    private void Clouds()
    {
        var view = Doc.ActiveView;
        if (view is not ViewPlan)
        {
            _status.Text = "Mở mặt bằng (floor plan) rồi vẽ đám mây.";
            return;
        }

        var rows = Selected().Where(r => r.Result.Status != CompareStatus.Match && r.Result.Kind != "Lỗi đọc").ToList();
        var made = 0;
        using (var tx = new Transaction(Doc, "DSSTech compare clouds"))
        {
            tx.Start();
            var revision = Revision.Create(Doc);
            revision.Description = $"DSS So sánh {DateTime.Now:dd/MM/yyyy HH:mm} — {_subject}";
            var z = view.GenLevel?.Elevation ?? _level.Elevation;
            foreach (var row in rows)
            {
                var (min, max) = Box(row.Result, CloudMarginMm);
                var p = new[] { new XYZ(min.X, min.Y, z), new XYZ(max.X, min.Y, z), new XYZ(max.X, max.Y, z), new XYZ(min.X, max.Y, z) };
                var curves = Enumerable.Range(0, 4).Select(i => (Curve)Line.CreateBound(p[i], p[(i + 1) % 4])).ToList();
                try
                {
                    var cloud = RevisionCloud.Create(Doc, view, revision.Id, curves);
                    cloud.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"#{row.Index} {row.Status}: {row.Detail}");
                    row.Note = "Đã khoanh";
                    made++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    row.Note = "Không khoanh được: " + ex.Message;
                }
            }

            tx.Commit();
        }

        _status.Text = $"Đã vẽ {made} đám mây trên view {view.Name} (revision «DSS So sánh»; nội dung trong Comments).";
    }

    private void Colour()
    {
        var view = Doc.ActiveView;
        var solid = new FilteredElementCollector(Doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
        var made = 0;
        using (var tx = new Transaction(Doc, "DSSTech compare colours"))
        {
            tx.Start();
            foreach (var row in Selected())
            {
                if (row.Result.Model?.Id is not > 0 || Doc.GetElement(new ElementId(row.Result.Model.Id)) is null)
                    continue;
                var color = row.Result.Status switch
                {
                    CompareStatus.Extra => new Color(220, 40, 40),
                    CompareStatus.Moved or CompareStatus.Changed => new Color(240, 150, 0),
                    _ => new Color(40, 160, 80)
                };
                var ogs = new OverrideGraphicSettings().SetProjectionLineColor(color).SetCutLineColor(color);
                if (solid is not null)
                {
                    ogs.SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(color)
                        .SetCutForegroundPatternId(solid.Id).SetCutForegroundPatternColor(color);
                }

                view.SetElementOverrides(new ElementId(row.Result.Model.Id), ogs);
                made++;
            }

            tx.Commit();
        }

        _status.Text = $"Đã tô {made} phần tử trên view {view.Name}: đỏ = thừa, cam = lệch / khác. Phần thiếu không có phần tử để tô: dùng đám mây.";
    }

    /// <summary>Moved rows only: the model element goes to the drawing's place (points), or sideways onto it (lines).</summary>
    private void MoveToDrawing()
    {
        var rows = Selected().Where(r => r.Result.Status == CompareStatus.Moved && r.Result.Cad is not null && r.Result.Model?.Id > 0).ToList();
        if (rows.Count == 0)
        {
            _status.Text = "Không có dòng «Lệch vị trí» nào để dời.";
            return;
        }

        var moved = 0;
        using (var tx = new Transaction(Doc, "DSSTech move to drawing"))
        {
            tx.Start();
            foreach (var row in rows)
            {
                var c = row.Result.Cad!;
                var m = row.Result.Model!;
                var id = new ElementId(m.Id);
                if (Doc.GetElement(id) is null)
                    continue;
                double dx, dy;
                if (m.Shape == CompareShape.Point)
                {
                    dx = c.X0 - m.X0;
                    dy = c.Y0 - m.Y0;
                }
                else
                {
                    // Sideways: from the model line to the drawing line's middle, square to the model line.
                    var len = m.Length;
                    var nx = -(m.Y1 - m.Y0) / len;
                    var ny = (m.X1 - m.X0) / len;
                    var d = ((c.X0 + c.X1) / 2 - m.X0) * nx + ((c.Y0 + c.Y1) / 2 - m.Y0) * ny;
                    dx = nx * d;
                    dy = ny * d;
                }

                try
                {
                    ElementTransformUtils.MoveElement(Doc, id, new XYZ(UnitConv.MmToInternal(dx), UnitConv.MmToInternal(dy), 0));
                    row.Note = $"Đã dời {Math.Sqrt(dx * dx + dy * dy):0} mm";
                    moved++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    row.Note = "Không dời được: " + ex.Message;
                }
            }

            tx.Commit();
        }

        _status.Text = $"Đã dời {moved}/{rows.Count} phần tử theo bản vẽ (Ctrl+Z để hoàn tác). Phần thiếu: chạy lại tool dựng tương ứng, tool bỏ qua cái đã có.";
    }

    private void ExportExcel()
    {
        var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"{Doc.Title}-so-sanh-{_level.Name}.xlsx", AddExtension = true };
        if (dlg.ShowDialog() != true)
            return;
        var rows = Selected();
        DSSTech.Core.TableWorkbookExporter.Export(dlg.FileName, "So sánh", $"So sánh {_subject} — {Doc.Title} — level {_level.Name} — {DateTime.Now:dd/MM/yyyy HH:mm}",
            new[] { "#", "Trạng thái", "Hạng mục", "Mô tả", "Lệch (mm)", "ID", "X (mm)", "Y (mm)", "Ghi chú" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.Index, r.Status, r.Kind, r.Detail, r.Result.OffsetMm > 0.5 ? Math.Round(r.Result.OffsetMm) : null,
                r.Result.Model?.Id is > 0 ? r.Result.Model.Id : null, Math.Round(r.Result.X), Math.Round(r.Result.Y), r.Note
            }));
        _status.Text = $"Đã xuất Excel ({rows.Count} dòng): {dlg.FileName}";
    }

    private (XYZ Min, XYZ Max) Box(CompareResult r, double marginMm)
    {
        var w = Math.Max(r.MaxX - r.MinX + 2 * marginMm, CloudMinMm);
        var h = Math.Max(r.MaxY - r.MinY + 2 * marginMm, CloudMinMm);
        var z = _level.Elevation;
        return (new XYZ(UnitConv.MmToInternal(r.X - w / 2), UnitConv.MmToInternal(r.Y - h / 2), z),
            new XYZ(UnitConv.MmToInternal(r.X + w / 2), UnitConv.MmToInternal(r.Y + h / 2), z));
    }
}
