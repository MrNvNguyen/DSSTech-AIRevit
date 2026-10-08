using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Dss.McpContracts;

namespace Dss.McpHost.Tools;

internal static class ReadTools
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly (string Name, BuiltInCategory Bic)[] CategoryList =
    {
        ("Levels", BuiltInCategory.OST_Levels),
        ("Grids", BuiltInCategory.OST_Grids),
        ("Walls", BuiltInCategory.OST_Walls),
        ("Floors", BuiltInCategory.OST_Floors),
        ("Ceilings", BuiltInCategory.OST_Ceilings),
        ("Doors", BuiltInCategory.OST_Doors),
        ("Windows", BuiltInCategory.OST_Windows),
        ("Rooms", BuiltInCategory.OST_Rooms),
        ("StructuralColumns", BuiltInCategory.OST_StructuralColumns),
        ("StructuralFraming", BuiltInCategory.OST_StructuralFraming),
        ("StructuralFoundation", BuiltInCategory.OST_StructuralFoundation),
        ("Pipes", BuiltInCategory.OST_PipeCurves),
        ("Ducts", BuiltInCategory.OST_DuctCurves),
        ("CableTrays", BuiltInCategory.OST_CableTray),
        ("LightingFixtures", BuiltInCategory.OST_LightingFixtures),
        ("MechanicalEquipment", BuiltInCategory.OST_MechanicalEquipment),
        ("ElectricalEquipment", BuiltInCategory.OST_ElectricalEquipment),
        ("PlumbingFixtures", BuiltInCategory.OST_PlumbingFixtures),
        ("Sprinklers", BuiltInCategory.OST_Sprinklers)
    };

    public static McpResult GetDocument(UIApplication app)
    {
        var doc = RequireDoc(app, out var fail);
        if (doc is null)
            return fail!;

        return Ok(new
        {
            title = doc.Title,
            pathName = doc.PathName,
            isWorkshared = doc.IsWorkshared,
            versionNumber = app.Application.VersionNumber
        });
    }

    public static McpResult GetLevels(UIApplication app)
    {
        var doc = RequireDoc(app, out var fail);
        if (doc is null)
            return fail!;

        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => new
            {
                id = l.Id.Value.ToString(),
                name = l.Name,
                elevationMm = ToMm(l.Elevation)
            });
        return Ok(new { levels });
    }

    public static McpResult GetGrids(UIApplication app)
    {
        var doc = RequireDoc(app, out var fail);
        if (doc is null)
            return fail!;

        var grids = new List<object>();
        foreach (var grid in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
        {
            double[]? start = null;
            double[]? end = null;
            if (grid.Curve is Line line)
            {
                start = XyzMm(line.GetEndPoint(0));
                end = XyzMm(line.GetEndPoint(1));
            }

            grids.Add(new
            {
                id = grid.Id.Value.ToString(),
                name = grid.Name,
                startMm = start,
                endMm = end
            });
        }

        return Ok(new { grids });
    }

    public static McpResult GetView(UIApplication app)
    {
        var uidoc = app.ActiveUIDocument;
        var view = uidoc?.ActiveView;
        if (view is null)
            return McpResult.Fail("no_active_view", "No active view.");

        string? genLevelId = null;
        if (view.GenLevel != null)
            genLevelId = view.GenLevel.Id.Value.ToString();

        return Ok(new
        {
            id = view.Id.Value.ToString(),
            name = view.Name,
            viewType = view.ViewType.ToString(),
            genLevelId
        });
    }

    public static McpResult ListCategories()
    {
        var categories = CategoryList.Select(c => new
        {
            name = c.Name,
            builtInCategory = c.Bic.ToString()
        });
        return Ok(new { categories });
    }

    public static McpResult ListTypes(UIApplication app, JsonElement args)
    {
        var doc = RequireDoc(app, out var fail);
        if (doc is null)
            return fail!;

        if (!TryCategory(args, out var bic, out var categoryError))
            return categoryError!;

        var types = new List<object>();
        if (bic == BuiltInCategory.OST_Walls)
            types.AddRange(Names(new FilteredElementCollector(doc).OfClass(typeof(WallType))));
        else if (bic == BuiltInCategory.OST_Floors)
            types.AddRange(Names(new FilteredElementCollector(doc).OfClass(typeof(FloorType))));
        else if (bic == BuiltInCategory.OST_Ceilings)
            types.AddRange(Names(new FilteredElementCollector(doc).OfClass(typeof(CeilingType))));
        else if (bic == BuiltInCategory.OST_PipeCurves)
            types.AddRange(Names(new FilteredElementCollector(doc).OfClass(typeof(PipeType))));
        else if (bic == BuiltInCategory.OST_DuctCurves)
            types.AddRange(Names(new FilteredElementCollector(doc).OfClass(typeof(DuctType))));
        else if (bic == BuiltInCategory.OST_CableTray)
            types.AddRange(Names(new FilteredElementCollector(doc).OfClass(typeof(CableTrayType))));
        else
            types.AddRange(Names(new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(bic)));

        object? systemTypes = null;
        if (bic == BuiltInCategory.OST_PipeCurves)
            systemTypes = Names(new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)));

        return Ok(new { types, systemTypes });
    }

    public static McpResult QueryByCategory(UIApplication app, JsonElement args)
    {
        var doc = RequireDoc(app, out var fail);
        if (doc is null)
            return fail!;

        if (!TryCategory(args, out var bic, out var categoryError))
            return categoryError!;

        var pageSize = 50;
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("pageSize", out var sizeEl) &&
            sizeEl.TryGetInt32(out var requested))
            pageSize = requested;

        if (pageSize < 1)
            return McpResult.Fail("invalid_page_size", "pageSize must be >= 1.");
        if (pageSize > 100)
            return McpResult.Fail("page_size_exceeded", "pageSize max is 100.");

        long after = -1;
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("pageToken", out var tokenEl) &&
            tokenEl.ValueKind == JsonValueKind.String &&
            long.TryParse(tokenEl.GetString(), out var parsed))
            after = parsed;

        var ids = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToElementIds()
            .Where(id => id.Value > after)
            .OrderBy(id => id.Value)
            .Take(pageSize + 1)
            .ToList();

        var pageIds = ids.Take(pageSize).ToList();
        string? next = ids.Count > pageSize ? ids[pageSize].Value.ToString() : null;
        var page = new List<object>();
        foreach (var id in pageIds)
        {
            var element = doc.GetElement(id);
            if (element is null)
                continue;

            page.Add(new
            {
                id = id.Value.ToString(),
                name = element.Name,
                typeName = TypeName(element),
                levelId = LevelId(element)
            });
        }

        return Ok(new { items = page, nextPageToken = next });
    }

    private static string? TypeName(Element element)
    {
        if (element is Wall wall)
            return wall.WallType?.Name;
        if (element is FamilyInstance fi)
            return fi.Symbol?.Name;
        return element.Name;
    }

    private static string? LevelId(Element element)
    {
        var id = element.LevelId;
        if (id == null || id == ElementId.InvalidElementId)
            return null;
        return id.Value.ToString();
    }

    private static List<object> Names(FilteredElementCollector collector) =>
        collector.Cast<Element>()
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => (object)new { id = e.Id.Value.ToString(), name = e.Name })
            .ToList();

    private static bool TryCategory(JsonElement args, out BuiltInCategory bic, out McpResult? error)
    {
        bic = BuiltInCategory.INVALID;
        error = null;
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("category", out var catEl) ||
            catEl.ValueKind != JsonValueKind.String)
        {
            error = McpResult.Fail("missing_category", "Argument 'category' is required.");
            return false;
        }

        var name = catEl.GetString() ?? "";
        foreach (var row in CategoryList)
        {
            if (row.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                row.Bic.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                bic = row.Bic;
                return true;
            }
        }

        if (name.Equals("PipeCurves", StringComparison.OrdinalIgnoreCase))
        {
            bic = BuiltInCategory.OST_PipeCurves;
            return true;
        }

        error = McpResult.Fail("unknown_category", $"Unknown category '{name}'.");
        return false;
    }

    private static Document? RequireDoc(UIApplication app, out McpResult? fail)
    {
        var doc = app.ActiveUIDocument?.Document;
        if (doc is null)
        {
            fail = McpResult.Fail("no_active_document", "Open a Revit project first.");
            return null;
        }

        fail = null;
        return doc;
    }

    private static McpResult Ok(object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        using var doc = JsonDocument.Parse(json);
        return McpResult.Success(doc.RootElement.Clone());
    }

    private static double ToMm(double internalFeet) =>
        UnitUtils.ConvertFromInternalUnits(internalFeet, UnitTypeId.Millimeters);

    private static double[] XyzMm(XYZ p) => new[] { ToMm(p.X), ToMm(p.Y), ToMm(p.Z) };
}
