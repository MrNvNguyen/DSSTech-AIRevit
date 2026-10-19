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
            if (part is ModelRevitPartKind.Column or ModelRevitPartKind.StructuralWall)
            {
                // Linked CAD text is strokes only. Column section and wall thickness come from the outline.
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

            IReadOnlyList<CadPoint2>? grids = part == ModelRevitPartKind.Column
                ? ModelRevitGrids.Intersections(doc, view)
                : null;
            IReadOnlyList<double>? gridAngles = part == ModelRevitPartKind.Column
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
                Function = choice.Function
            };
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
                        "Không thấy cụm nét cột (khoảng 80–2500 mm) trên layer này. Pick nét boundary/hatch của cột, không pick layer dim.",
                    ModelRevitPartKind.StructuralWall =>
                        "Không thấy cặp nét song song của tường (bề dày khoảng 80–1200 mm). Pick nét biên tường, không pick layer dim.",
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
                summary.AppendLine("Tâm cột chỉ bỏ phần thập phân, 2175,4 thành 2175. Không kéo về hàng 5 hay hàng chục.");
                if (choice.AssignMark)
                    summary.AppendLine(DescribeMarks(geom, ops));
            }
            else if (part == ModelRevitPartKind.StructuralWall)
            {
                summary.AppendLine($"Type gốc: {choice.TypeName}");
                summary.AppendLine("Bề dày đo từ hai nét song song, làm tròn 10 mm, nhân type tường theo bề dày và chức năng.");
            }
            summary.AppendLine($"Level đáy: {placement.BaseLevelName} (offset {placement.BaseOffsetMm:0} mm) → đỉnh: {placement.TopLevelName} (offset {placement.TopOffsetMm:0} mm)");
            summary.AppendLine($"Tạo {ops.Count} cấu kiện (Undo một lần hoàn tác batch).");
            if (part == ModelRevitPartKind.Column)
                AppendColumnBatchSummary(summary, result.Data);
            else
                summary.AppendLine(result.Data.GetRawText());
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
}
