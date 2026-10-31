using System.Text;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Dss.Cad2Bim.Core;
using Dss.McpContracts;
using Dss.McpHost.Create;
using DSSTech.Revit.Cad;
using DSSTech.Revit.ModelRevit;

namespace DSSTech.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class ModelRevitCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        Run(commandData.Application);

    public static Result Run(UIApplication uiApp, bool ensurePackage = true)
    {
        var uidoc = uiApp.ActiveUIDocument;
        var doc = uidoc?.Document;
        if (doc is null || uidoc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        var view = doc.ActiveView;
        if (!ModelRevitLevelContext.TryResolve(view, doc, out var baseLevel, out var topLevel, out var levelErr))
        {
            TaskDialog.Show("DSSTech — ModelRevit", levelErr);
            return Result.Cancelled;
        }

        if (ensurePackage && !ParamSetupRunner.EnsureChosen(uiApp))
            return Result.Cancelled;

        var selectedImport = uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<ImportInstance>()
            .FirstOrDefault();
        var catalog = ModelRevitFormCatalog.Load(doc, baseLevel, topLevel, selectedImport);
        if (!ModelRevitSetupWindow.TryPick(uiApp.MainWindowHandle, catalog, out var choice))
            return Result.Cancelled;

        var part = choice.Part;
        var columnFamily = choice.Family;
        var projectKey = ProjectKeys.For(doc);

        try
        {
            var import = selectedImport;
            if (import is null)
            {
                var importRef = uidoc.Selection.PickObject(ObjectType.Element, new ImportInstanceSelectionFilter(),
                    "Chọn CAD link (ImportInstance) đã Move/Rotate khớp model.");
                import = doc.GetElement(importRef.ElementId) as ImportInstance;
            }

            if (import is null)
            {
                TaskDialog.Show("DSSTech", "Phải chọn CAD link.");
                return Result.Failed;
            }

            var layerName = choice.Layer;
            if (string.IsNullOrWhiteSpace(layerName))
            {
                var layerRef = uidoc.Selection.PickObject(ObjectType.PointOnElement, new ImportLayerPickFilter(import.Id),
                    "Pick một nét trên layer cần dựng (layer lấy từ nét vừa chọn).");
                layerName = ImportInstanceGeometryReader.TryGetLayerFromReference(import, layerRef);
            }

            if (string.IsNullOrWhiteSpace(layerName))
            {
                TaskDialog.Show("DSSTech", "Không đọc được tên layer từ nét vừa pick.");
                return Result.Failed;
            }

            CadLinkLayerVisibility.EnsureAllImportLayersVisible(view, import);

            var geom = ImportInstanceGeometryReader.Read(import, view);
            AnnotationSize size;
            if (part is ModelRevitPartKind.Column or ModelRevitPartKind.StructuralWall
                or ModelRevitPartKind.Beam or ModelRevitPartKind.Pile
                or ModelRevitPartKind.PileCap or ModelRevitPartKind.IsolatedFoundation
                or ModelRevitPartKind.StripFoundation or ModelRevitPartKind.RaftFoundation)
            {
                // Linked CAD text is strokes in the Revit view; Cad2Bim reads CadText from the DWG.
                // Columns/walls size from outline; beams parse Mark/size from CadText on/near the layer.
                // Piles/isolated: section from CAD footprint; Mark from CadText; UI type = family.
                // Pile caps: Type Mark = CAD label; size from pre-setup Type (no WxH invent).
                size = new AnnotationSize(null, null, null, "");
            }
            else
            {
                var annRef = uidoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new TextOrDimensionPickFilter(import.Id),
                    "Pick Text Note hoặc Dimension của Revit. Chữ trên CAD link không còn nội dung số.");
                if (!ModelRevitAnnotationReader.TryRead(doc, import, geom, annRef, out size, out var sizeErr))
                {
                    TaskDialog.Show("DSSTech", sizeErr);
                    return Result.Failed;
                }
            }

            if (choice.ThicknessMm is double thick && thick > 0)
                size = new AnnotationSize(thick, size.HeightMm ?? thick, size.DiameterMm, size.Raw);

            IReadOnlyList<CadPoint2>? grids = part is ModelRevitPartKind.Column
                or ModelRevitPartKind.StructuralWall or ModelRevitPartKind.Beam or ModelRevitPartKind.Pile
                or ModelRevitPartKind.PileCap or ModelRevitPartKind.IsolatedFoundation
                or ModelRevitPartKind.StripFoundation or ModelRevitPartKind.RaftFoundation
                ? ModelRevitGrids.Intersections(doc, view)
                : null;
            IReadOnlyList<double>? gridAngles = part is ModelRevitPartKind.Column
                or ModelRevitPartKind.StructuralWall or ModelRevitPartKind.Beam
                ? ModelRevitGrids.Angles(doc, view)
                : null;
            var placement = new ModelRevitPlacement
            {
                BaseLevelName = choice.BaseLevel,
                TopLevelName = string.IsNullOrWhiteSpace(choice.TopLevel) ? choice.BaseLevel : choice.TopLevel,
                BaseOffsetMm = choice.BaseOffsetMm,
                TopOffsetMm = choice.TopOffsetMm,
                FamilyName = choice.Family,
                TypeName = choice.TypeName,
                WidthParam = choice.WidthParam,
                HeightParam = choice.HeightParam,
                Material = choice.Material,
                AssignMark = choice.AssignMark,
                SystemName = choice.SystemName,
                SectionShape = choice.SectionShape,
                Function = choice.Function,
                LengthMm = part == ModelRevitPartKind.Pile && choice.ThicknessMm is double pileLen && pileLen > 0
                    ? pileLen
                    : 0,
                EmbedMm = choice.EmbedMm,
                CapBottomOffsetMm = choice.PileFromCapBottom ? choice.BaseOffsetMm : null,
                FoundationTypeMarks = part == ModelRevitPartKind.PileCap
                    ? catalog.FoundationTypeMarks
                    : null,
                Log = part is ModelRevitPartKind.PileCap or ModelRevitPartKind.Column ? DssTechLog.Write : null
            };
            var supportColumns = 0;
            var supportWalls = 0;
            if (part == ModelRevitPartKind.Beam)
            {
                var beamLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => string.Equals(l.Name, placement.TopLevelName, StringComparison.Ordinal)) ?? topLevel;
                placement.Supports = ModelRevitSupports.Collect(doc, beamLevel, placement.TopOffsetMm, out supportColumns, out supportWalls);
                placement.Log = DssTechLog.Write;
                DssTechLog.Write($"beam supports level={beamLevel.Name} columns={supportColumns} walls={supportWalls}");
                // Planner input for offline replay (tests/cad2bim-section-smoke --replay). Must not break the command.
                try
                {
                    var capturePath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSSTech", "beam-capture.json");
                    PlanCapture.From(geom, layerName, part, placement.BaseLevelName, placement.TopLevelName,
                        size, placement, grids, gridAngles).Save(capturePath);
                    DssTechLog.Write($"beam capture {capturePath}");
                }
                catch (Exception ex)
                {
                    DssTechLog.Write($"beam capture failed: {ex.Message}");
                }
            }

            if (part == ModelRevitPartKind.Column)
            {
                // Planner input for checking columns found on other layers. Must not break the command.
                try
                {
                    var capturePath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSSTech", "column-capture.json");
                    PlanCapture.From(geom, layerName, part, placement.BaseLevelName, placement.TopLevelName,
                        size, placement, grids, gridAngles).Save(capturePath);
                }
                catch (Exception ex)
                {
                    DssTechLog.Write($"column capture failed: {ex.Message}");
                }
            }

            var ops = ModelRevitPlanner.Plan(
                geom,
                layerName,
                part,
                placement.BaseLevelName,
                placement.TopLevelName,
                size,
                equipmentFamilyType: choice.Family,
                gridPoints: grids,
                columnFamily: columnFamily,
                placement: placement,
                gridAnglesDeg: gridAngles).ToList();

            if (ops.Count == 0)
            {
                var hint = part switch
                {
                    ModelRevitPartKind.Column =>
                        "Không thấy cụm nét cột (L/b ≤ 4, cạnh 80–2500 mm) trên layer này. Bao dài hơn (L/b > 4) là vách — chọn phần Tường kết cấu.",
                    ModelRevitPartKind.StructuralWall =>
                        "Không thấy cặp nét song song của vách (bề dày 80–1200 mm, L/b > 4). Bao ngắn (L/b ≤ 4) là cột — chọn phần Cột.",
                    ModelRevitPartKind.Beam =>
                        "Không thấy cặp nét song song của dầm (hoặc nét tim) trên layer. Chữ Mark/kích thước đọc từ CadText DWG (vd. D1Y19-40x60, 30x60).",
                    ModelRevitPartKind.Pile =>
                        "Không thấy vòng tròn / block / polyline kín của cọc trên layer này (cạnh ≥ 80 mm).",
                    ModelRevitPartKind.PileCap or ModelRevitPartKind.IsolatedFoundation
                        or ModelRevitPartKind.RaftFoundation =>
                        "Không thấy polyline kín / block của móng trên layer (cạnh ≥ 200 mm).",
                    ModelRevitPartKind.StripFoundation =>
                        "Không thấy đường thẳng CadLine của móng băng trên layer.",
                    _ => "Kiểm tra nét/block trên layer."
                };
                TaskDialog.Show("DSSTech",
                    $"Không suy ra cấu kiện trên layer '{layerName}' cho phần {part}.\n{hint}");
                return Result.Failed;
            }

            var batchArgs = JsonSerializer.SerializeToElement(new { dryRun = false, operations = ops });
            var result = CreateBatchTool.Execute(uiApp, batchArgs, McpPermission.ReadCreate);

            CadProjectProfileStore.RememberLayer(projectKey, layerName, part, size);

            if (!result.Ok)
            {
                TaskDialog.Show("DSSTech — ModelRevit", $"{result.ErrorCode}: {result.Message}");
                return Result.Failed;
            }

            var summary = new StringBuilder();
            summary.AppendLine($"Layer: {layerName}");
            summary.AppendLine($"Kích thước đọc được: {DescribeSize(size)}");
            if (part == ModelRevitPartKind.Column)
            {
                summary.AppendLine($"Family gốc: {columnFamily}");
                summary.AppendLine("Tiết diện đo từ outline trên layer, gồm cột vát góc và cột xoay theo grid.");
                summary.AppendLine("Tâm cột làm tròn 5 mm tính từ trục (2172,4 → 2170; 2173 → 2175). Không kéo về hàng chục.");
                if (placement.OtherLayerColumns.Count > 0)
                {
                    var layers = placement.OtherLayerColumns.GroupBy(l => l).Select(g => $"{g.Key} ({g.Count()})");
                    summary.AppendLine($"Cột ở layer khác: {placement.OtherLayerColumns.Count} — {string.Join(", ", layers)}");
                }

                if (choice.AssignMark)
                    summary.AppendLine(DescribeMarks(geom, ops));
            }
            else if (part == ModelRevitPartKind.StructuralWall)
            {
                summary.AppendLine($"Type gốc: {choice.TypeName}");
                summary.AppendLine("Bề dày đo từ hai nét song song, làm tròn 10 mm, nhân type tường theo bề dày và chức năng (mặc định CoreShaft / Lõi).");
                summary.AppendLine("Layer chung cột/vách: vách khi L/b > 4; cột khi L/b ≤ 4 (nếu đã chọn family cột).");
                summary.AppendLine("Tường kết cấu: Function = CoreShaft, Location Line = Wall Centerline (tim CAD). Exterior/Interior chỉ dùng cho tường kiến trúc.");
                summary.AppendLine("Đầu tường kéo tới tim tường vuông góc để góc L/T join khít.");
            }
            else if (part == ModelRevitPartKind.Beam)
            {
                summary.AppendLine($"Family gốc: {FirstNonEmpty(choice.Family, choice.TypeName)}");
                summary.AppendLine("Tim dầm từ cặp nét song song (hoặc nét tim); hướng trái→phải / dưới→trên.");
                summary.AppendLine(supportColumns + supportWalls > 0
                    ? $"Gối đỡ lấy từ model: {supportColumns} cột, {supportWalls} vách kết cấu tại level dầm."
                    : "Model chưa có cột/vách kết cấu tại level dầm → gối đỡ lấy từ CAD (outline cột mọi layer, cặp nét vách layer khác).");
                summary.AppendLine("Dầm chính: tim gối → tim gối (cột/vách), liên tục qua dầm phụ. Dầm phụ (không qua cột): cắt tại tim dầm chính.");
                summary.AppendLine("Nhịp gối→gối bị dầm chính khác cắt ngang giữa nhịp (không có gối tại đó) → dầm hẹp hơn (bằng nhau: dài hơn) thành dầm phụ, cắt tại tim dầm kia. Hai dầm phụ giao nhau: dầm hẹp hơn (bằng nhau: trục ngắn hơn) bị cắt.");
                summary.AppendLine("Mark/kích thước từ CadText DWG sau khi cắt đoạn: D1Y19-40x60 / D1O1-200x500 → Mark + tiết diện trên đúng đoạn; không lấy chữ ngoài đoạn.");
                summary.AppendLine("Chữ có hình chiếu trên đoạn (cách mặt dầm ≤ 600 mm, song song ±10°), mỗi chữ chỉ gán cho đoạn gần nhất; ưu tiên Mark-WxH. Đoạn không chữ trên cùng trục kế thừa tiết diện từ đoạn có Mark-WxH gần nhất; Mark trống kế thừa Mark cùng trục (Mark duy nhất hoặc đoạn gần nhất).");
                summary.AppendLine("Đoạn cùng tiết diện WxH thẳng hàng, không gối thật giữa chúng, được gộp thành một dầm gối→gối.");
                summary.AppendLine(DescribeBeamMarks(ops));
            }
            else if (part == ModelRevitPartKind.Pile)
            {
                summary.AppendLine($"Type gốc (family): {choice.TypeName}");
                if (!string.IsNullOrWhiteSpace(choice.PileSubtype))
                    summary.AppendLine($"Loại cọc (lọc type): {choice.PileSubtype}");
                var pileTop = placement.EffectivePileTopOffsetMm();
                if (choice.PileFromCapBottom)
                {
                    summary.AppendLine(
                        $"Cọc theo đáy đài: Level {placement.BaseLevelName}; " +
                        $"đáy đài offset {choice.BaseOffsetMm:0} mm + ngàm {choice.EmbedMm:0} mm → " +
                        $"đỉnh cọc offset {pileTop:0} mm; chiều sâu {placement.LengthMm:0} mm.");
                    summary.AppendLine(
                        "Công thức: offset_đỉnh_cọc = offset_đáy_đài + ngàm (cùng Level; ngàm dương lên trong đài).");
                }
                else
                {
                    summary.AppendLine(
                        $"Cọc: đỉnh tại Level {placement.BaseLevelName} (offset {pileTop:0} mm); " +
                        $"chiều sâu {placement.LengthMm:0} mm (đáy = đỉnh − chiều sâu).");
                }

                summary.AppendLine(
                    "Tiết diện từ outline CAD tại mỗi vị trí: cọc tròn → ĐK lớn nhất (vòng đồng tâm); " +
                    "cọc vuông → bxh lớn nhất; nhân/tạo type Dxxx hoặc WxH trong family đã chọn.");
                summary.AppendLine("Tâm cọc làm tròn 5 mm tính từ trục (xxx0/xxx5), giống cột.");
                if (choice.AssignMark)
                    summary.AppendLine(DescribePileMarks(ops));
            }
            else if (part is ModelRevitPartKind.PileCap or ModelRevitPartKind.IsolatedFoundation
                     or ModelRevitPartKind.RaftFoundation or ModelRevitPartKind.StripFoundation)
            {
                summary.AppendLine($"Type gốc: {choice.TypeName}");
                summary.AppendLine(part switch
                {
                    ModelRevitPartKind.PileCap =>
                        "Đài cọc: tâm từ polyline kín / block; type theo Type Mark hoặc tên type (Đ4A…); ưu tiên chữ Đ*; xoay theo cạnh dài CAD; kích thước = Type đã setup (không nhân WxH từ CAD). Không khớp → bỏ qua. UI type chỉ khi không có chữ CAD gần outline.",
                    ModelRevitPartKind.IsolatedFoundation =>
                        "Móng đơn: tâm từ outline CAD; tiết diện bxh từ CAD; nhân type WxH.",
                    ModelRevitPartKind.RaftFoundation =>
                        "Móng bè: đặt tại tâm polyline kín; giữ nguyên type đã chọn (không nhân WxH từ CAD).",
                    _ =>
                        "Móng băng: điểm giữa mỗi CadLine, xoay theo hướng nét; type đã chọn (point footing)."
                });
                summary.AppendLine("Tâm làm tròn 5 mm tính từ trục (xxx0/xxx5), giống cột/cọc.");
                if (part == ModelRevitPartKind.PileCap)
                    summary.AppendLine(DescribePileCapTypeMarks(ops));
                else if (choice.AssignMark)
                    summary.AppendLine(DescribePileMarks(ops));
            }
            if (part is ModelRevitPartKind.Beam or ModelRevitPartKind.Pile)
                summary.AppendLine(
                    $"Level: {placement.BaseLevelName} (offset {placement.EffectivePileTopOffsetMm():0} mm)");
            else if (part is ModelRevitPartKind.PileCap or ModelRevitPartKind.IsolatedFoundation
                     or ModelRevitPartKind.RaftFoundation or ModelRevitPartKind.StripFoundation)
                summary.AppendLine($"Level: {placement.BaseLevelName} (offset {placement.BaseOffsetMm:0} mm)");
            else
                summary.AppendLine($"Level đáy: {placement.BaseLevelName} (offset {placement.BaseOffsetMm:0} mm) → đỉnh: {placement.TopLevelName} (offset {placement.TopOffsetMm:0} mm)");
            summary.AppendLine($"Tạo {ops.Count} cấu kiện (Undo một lần hoàn tác batch).");
            if (part == ModelRevitPartKind.Column)
                AppendColumnBatchSummary(summary, result.Data);
            else if (part == ModelRevitPartKind.PileCap)
                AppendFoundationBatchSummary(summary, result.Data);
            else if (part is not (ModelRevitPartKind.StructuralWall or ModelRevitPartKind.Beam or ModelRevitPartKind.Pile
                         or ModelRevitPartKind.IsolatedFoundation
                         or ModelRevitPartKind.RaftFoundation or ModelRevitPartKind.StripFoundation))
                summary.AppendLine(result.Data.GetRawText());

            if (part is ModelRevitPartKind.Column or ModelRevitPartKind.StructuralWall
                or ModelRevitPartKind.Beam or ModelRevitPartKind.Pile
                or ModelRevitPartKind.PileCap or ModelRevitPartKind.IsolatedFoundation
                or ModelRevitPartKind.RaftFoundation or ModelRevitPartKind.StripFoundation)
            {
                var dimCount = ModelRevitAxisDimensions.CreateFromBatch(doc, view, result.Data);
                summary.AppendLine(
                    dimCount > 0
                        ? $"Dimension tới trục: {dimCount} (offset làm tròn 5 mm, dạng xxx0/xxx5)."
                        : "Dimension tới trục: 0 (không có grid song song trong tầm hoặc view không phải plan).");
            }

            TaskDialog.Show("DSSTech — ModelRevit", summary.ToString());
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"modelrevit error: {ex}");
            TaskDialog.Show("DSSTech", $"Lỗi Model Revit: {ex.Message}");
            return Result.Failed;
        }
    }

    private static string DescribeMarks(CadGeometry geom, IReadOnlyList<CreateOperation> ops)
    {
        var labeled = 0;
        foreach (var op in ops)
        {
            if (op.Fields.ValueKind == JsonValueKind.Object &&
                op.Fields.TryGetProperty("mark", out var mark) &&
                mark.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(mark.GetString()))
                labeled++;
        }

        if (labeled > 0)
            return $"Mark: gán {labeled}/{ops.Count} từ chữ gần cột (C1, C5). Cột đã có vẫn được ghi Mark.";

        var texts = 0;
        foreach (var text in geom.Entities.OfType<CadText>())
        {
            if (CadMark.IsLabel(text.Value))
                texts++;
        }

        return texts == 0
            ? "Mark: không đọc được chữ C1/C5 trên CAD."
            : "Mark: có chữ nhưng không chữ nào nằm cạnh outline cột.";
    }

    private static string DescribeBeamMarks(IReadOnlyList<CreateOperation> ops)
    {
        var marked = 0;
        var sized = 0;
        foreach (var op in ops)
        {
            if (op.Fields.ValueKind != JsonValueKind.Object)
                continue;
            if (op.Fields.TryGetProperty("mark", out var mark) &&
                mark.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(mark.GetString()))
                marked++;
            if (op.Fields.TryGetProperty("widthMm", out var w) && w.ValueKind == JsonValueKind.Number && w.GetInt32() > 0 &&
                op.Fields.TryGetProperty("depthMm", out var d) && d.ValueKind == JsonValueKind.Number && d.GetInt32() > 0)
                sized++;
        }

        return $"Mark từ CadText: {marked}/{ops.Count}; tiết diện WxH: {sized}/{ops.Count}.";
    }

    private static string DescribePileMarks(IReadOnlyList<CreateOperation> ops)
    {
        var marked = 0;
        var sized = 0;
        foreach (var op in ops)
        {
            if (op.Fields.ValueKind != JsonValueKind.Object)
                continue;
            if (op.Fields.TryGetProperty("mark", out var mark) &&
                mark.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(mark.GetString()))
                marked++;
            if ((op.Fields.TryGetProperty("diameterMm", out var dia) && dia.ValueKind == JsonValueKind.Number && dia.GetInt32() > 0) ||
                (op.Fields.TryGetProperty("widthMm", out var w) && w.ValueKind == JsonValueKind.Number && w.GetInt32() > 0))
                sized++;
        }

        return $"Mark từ CadText gần cọc (vd. K1): {marked}/{ops.Count}; tiết diện CAD: {sized}/{ops.Count}.";
    }

    private static string DescribePileCapTypeMarks(IReadOnlyList<CreateOperation> ops)
    {
        var byMark = 0;
        var uiFallback = 0;
        var instanceMark = 0;
        foreach (var op in ops)
        {
            if (op.Fields.ValueKind != JsonValueKind.Object)
                continue;
            if (op.Fields.TryGetProperty("resolveByTypeMark", out var r) && r.ValueKind == JsonValueKind.True)
                byMark++;
            else
                uiFallback++;
            if (op.Fields.TryGetProperty("mark", out var mark) &&
                mark.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(mark.GetString()))
                instanceMark++;
        }

        return $"Type Mark từ CAD: {byMark}/{ops.Count}; fallback UI type (không chữ): {uiFallback}; instance Mark: {instanceMark}.";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return "(chưa chọn)";
    }

    private static string DescribeSize(AnnotationSize size)
    {
        if (size.WidthMm is double w && size.HeightMm is double h)
            return $"{size.Raw} ({w:0} x {h:0} mm)";
        if (size.DiameterMm is double d)
            return $"{size.Raw} (D {d:0} mm)";
        if (size.WidthMm is double t)
            return $"{size.Raw} ({t:0} mm)";
        return string.IsNullOrWhiteSpace(size.Raw)
            ? "(không có số — cột lấy tiết diện từ nét)"
            : $"{size.Raw} (không đủ số — cột lấy tiết diện từ nét)";
    }

    private static void AppendColumnBatchSummary(StringBuilder summary, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            summary.AppendLine("Kết quả: (không đọc được batch).");
            return;
        }

        var created = 0;
        var skipped = 0;
        var failed = 0;
        var rotated90 = 0;
        var newTypes = 0;
        var failReasons = new Dictionary<string, int>();
        var skippedIds = new List<string>();
        var originNudged = false;
        foreach (var item in results.EnumerateArray())
        {
            var status = item.TryGetProperty("status", out var st) ? st.GetString() : null;
            var reason = item.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? reasonEl.GetString() ?? ""
                : "";
            switch (status)
            {
                case "created":
                    created++;
                    break;
                case "skipped":
                    skipped++;
                    if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        skippedIds.Add(idEl.GetString() ?? "");
                    break;
                case "failed":
                    failed++;
                    failReasons[reason] = failReasons.TryGetValue(reason, out var n) ? n + 1 : 1;
                    break;
            }

            if (reason.Length == 0)
                continue;
            if (reason.Contains("rotated90", StringComparison.Ordinal))
                rotated90++;
            if (reason.Contains("newType", StringComparison.Ordinal))
                newTypes++;
            if (reason.Contains("originNudge", StringComparison.Ordinal))
                originNudged = true;
        }

        summary.AppendLine(
            $"Kết quả: tạo {created} | xoay 90° {rotated90} | type mới {newTypes} | bỏ qua {skipped} | lỗi {failed}");
        foreach (var pair in failReasons.OrderByDescending(p => p.Value).Take(3))
            summary.AppendLine($"Lỗi ({pair.Value}): {pair.Key}");
        if (originNudged)
            summary.AppendLine(
                "Cột tại gốc Internal được đặt lệch 1 mm theo X để Revit vẽ được (đúng tại 0,0 thường bị ẩn).");
        if (skippedIds.Count > 0)
            summary.AppendLine(
                $"Bỏ qua do đã có cột cùng type, cùng level tại vị trí — Id: {string.Join(", ", skippedIds.Take(10))} " +
                "(Manage → Select by ID để kiểm tra).");
    }

    private static void AppendFoundationBatchSummary(StringBuilder summary, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            summary.AppendLine("Kết quả: (không đọc được batch).");
            return;
        }

        var created = 0;
        var skipped = 0;
        var failed = 0;
        var failReasons = new Dictionary<string, int>();
        foreach (var item in results.EnumerateArray())
        {
            var status = item.TryGetProperty("status", out var st) ? st.GetString() : null;
            var reason = item.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? reasonEl.GetString() ?? ""
                : "";
            switch (status)
            {
                case "created":
                    created++;
                    break;
                case "skipped":
                    skipped++;
                    break;
                case "failed":
                    failed++;
                    failReasons[reason] = failReasons.TryGetValue(reason, out var n) ? n + 1 : 1;
                    break;
            }
        }

        summary.AppendLine($"Kết quả: tạo {created} | bỏ qua (đã có) {skipped} | lỗi {failed}");
        foreach (var pair in failReasons.OrderByDescending(p => p.Value).Take(5))
            summary.AppendLine($"Lỗi ({pair.Value}): {pair.Key}");
    }
}
