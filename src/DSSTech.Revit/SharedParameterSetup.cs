using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSSTech.Core;

namespace DSSTech.Revit;

internal static class SharedParameterSetup
{
    /// <summary>Expected QS_* names from Config/QS_SharedParameters.txt (order for reporting).</summary>
    private static readonly string[] ExpectedParamNames =
        QsValueCatalog.FactToQsParam.Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .Concat(["QS_ConstructionHeight", "QS_Thickness", "QS_NormCode", "QS_NormStatus", "QS_NormStamp"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string BindFromFile(Document doc, string definitionPath)
    {
        if (!File.Exists(definitionPath))
            return "Thiếu file định nghĩa Shared Parameter.";

        if (!ValidateSharedParameterFile(definitionPath, out var validationError))
            return validationError;

        using var tx = new Transaction(doc, "DSSTech bind QS parameters");
        tx.Start();
        try
        {
            var (inserted, updated, missing) = BindDefinitions(doc, definitionPath);
            tx.Commit();

            var lines = new List<string>
            {
                $"Shared Parameter file:\n{definitionPath}",
                $"Group under: General",
                $"Inserted: {inserted}  |  Updated categories: {updated}"
            };
            if (missing.Count > 0)
                lines.Add("Thiếu trong file định nghĩa (không bind được):\n- " + string.Join("\n- ", missing));
            if (inserted == 0 && updated == 0 && missing.Count == 0)
                lines.Add("Tất cả QS_* đã có sẵn trên model với đủ category.");
            lines.Add("Không ghi giá trị QS_NormCode.");
            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return $"Không bind được Shared Parameter:\n{ex.Message}";
        }
    }

    /// <summary>Bind a named subset. Does not write parameter values.</summary>
    public static string BindSelected(
        Document doc,
        string definitionPath,
        IReadOnlyCollection<string> names,
        IReadOnlyCollection<BuiltInCategory> categories)
    {
        if (names.Count == 0)
            return "Gói tham số sẵn có của Revit: không gắn trường QS_*.";
        if (!File.Exists(definitionPath))
            return "Thiếu file định nghĩa Shared Parameter.";
        if (!ValidateSharedParameterFile(definitionPath, out var validationError))
            return validationError;

        using var tx = new Transaction(doc, "DSSTech bind selected QS parameters");
        tx.Start();
        try
        {
            var (inserted, updated, missing) = BindDefinitions(doc, definitionPath, names, categories);
            tx.Commit();
            var lines = new List<string>
            {
                $"Gắn {names.Count} trường QS_*.",
                $"Inserted: {inserted}  |  Updated categories: {updated}",
                "Không ghi giá trị QS_NormCode."
            };
            if (missing.Count > 0)
                lines.Add("Thiếu trong file định nghĩa:\n- " + string.Join("\n- ", missing));
            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return $"Không bind được Shared Parameter:\n{ex.Message}";
        }
    }

    /// <summary>Ensure write-back parameters exist; call inside an open transaction.</summary>
    public static void EnsureBound(Document doc, string definitionPath)
    {
        if (!File.Exists(definitionPath))
            throw new InvalidOperationException($"Thiếu file Shared Parameter:\n{definitionPath}");
        if (!ValidateSharedParameterFile(definitionPath, out var validationError))
            throw new InvalidOperationException(validationError);
        BindDefinitions(doc, definitionPath);
    }

    private static (int Inserted, int Updated, List<string> Missing) BindDefinitions(
        Document doc,
        string definitionPath,
        IReadOnlyCollection<string>? onlyNames = null,
        IReadOnlyCollection<BuiltInCategory>? onlyCategories = null)
    {
        var app = doc.Application;
        var pluginFile = Path.GetFullPath(definitionPath);

        // Always point Revit at the plugin definition file (avoids company SP file with only 3 QS_*).
        app.SharedParametersFilename = pluginFile;
        var defFile = app.OpenSharedParameterFile()
                      ?? throw new InvalidOperationException(
                          "Revit không mở được Shared Parameter file. Kiểm tra định dạng QS_SharedParameters.txt (META/GROUP/PARAM + GUID).");

        var group = defFile.Groups.get_Item("QS-BIM") ?? defFile.Groups.Create("QS-BIM");
        var byName = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
        foreach (DefinitionGroup defGroup in defFile.Groups)
        foreach (Definition definition in defGroup.Definitions)
        {
            if (definition.Name.StartsWith("QS_", StringComparison.Ordinal))
                byName[definition.Name] = definition;
        }

        var known = new HashSet<string>(byName.Keys, StringComparer.OrdinalIgnoreCase);
        List<string> missing;
        if (onlyNames is null)
            missing = ExpectedParamNames.Where(n => !known.Contains(n)).ToList();
        else
        {
            missing = onlyNames.Where(n => !known.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var allow = new HashSet<string>(onlyNames, StringComparer.OrdinalIgnoreCase);
            foreach (var key in byName.Keys.ToList())
            {
                if (!allow.Contains(key))
                    byName.Remove(key);
            }
        }

        if (known.Count == 0)
            throw new InvalidOperationException(
                "File Shared Parameter không chứa định nghĩa QS_*. Revit có thể bỏ qua dòng PARAM do GUID không hợp lệ.");
        if (byName.Count == 0)
            throw new InvalidOperationException("Không có trường QS_* nào trong gói này nằm trong file định nghĩa.");

        var categories = BuildCategorySet(doc, app, onlyCategories);
        if (categories.Size == 0)
            throw new InvalidOperationException("Không tìm thấy category hợp lệ để bind.");

        var inserted = 0;
        var updated = 0;
        foreach (var definition in byName.Values)
        {
            var binding = app.Create.NewInstanceBinding(categories);
            if (doc.ParameterBindings.Contains(definition))
            {
                if (ReInsert(doc, definition, binding))
                    updated++;
            }
            else if (Insert(doc, definition, binding))
            {
                inserted++;
            }
        }

        // Re-bind Formwork aux from SP file (VISIBLE=0) so Properties only shows Area/Side/Bottom.
        foreach (var name in FormworkHiddenOnProperties)
        {
            if (!byName.TryGetValue(name, out var def)) continue;
            if (doc.ParameterBindings.Contains(def))
                doc.ParameterBindings.Remove(def);
            if (Insert(doc, def, app.Create.NewInstanceBinding(categories)))
                updated++;
        }

        return (inserted, updated, missing);
    }

    /// <summary>Bound but hidden on Properties — takeoff triad Area/Side/Bottom stays visible.</summary>
    private static readonly HashSet<string> FormworkHiddenOnProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "QS_FormworkSystem", "QS_FormworkMaterial", "QS_FormworkDeduct", "QS_FormworkWaitPour",
        "QS_FormworkGroundContact", "QS_FormworkConfidence", "QS_FormworkStamp"
    };

    private static bool Insert(Document doc, Definition definition, Binding binding)
    {
        return doc.ParameterBindings.Insert(definition, binding, GroupTypeId.General);
    }

    private static bool ReInsert(Document doc, Definition definition, Binding binding)
    {
        return doc.ParameterBindings.ReInsert(definition, binding, GroupTypeId.General);
    }

    private static CategorySet BuildCategorySet(
        Document doc,
        Autodesk.Revit.ApplicationServices.Application app,
        IReadOnlyCollection<BuiltInCategory>? onlyCategories = null)
    {
        var set = app.Create.NewCategorySet();
        foreach (var cat in GetTargetCategories(doc, onlyCategories))
            set.Insert(cat);
        return set;
    }

    private static bool ValidateSharedParameterFile(string path, out string error)
    {
        error = "";
        var lines = File.ReadAllLines(path);
        if (!lines.Any(l => l.StartsWith("*META", StringComparison.OrdinalIgnoreCase)) ||
            !lines.Any(l => l.StartsWith("*GROUP", StringComparison.OrdinalIgnoreCase)) ||
            !lines.Any(l => l.StartsWith("*PARAM", StringComparison.OrdinalIgnoreCase)))
        {
            error = "QS_SharedParameters.txt thiếu *META / *GROUP / *PARAM — Revit sẽ báo readParamDatabase.";
            return false;
        }

        var badGuids = new List<string>();
        foreach (var line in lines)
        {
            if (!line.StartsWith("PARAM\t", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split('\t');
            if (parts.Length < 3 || !Guid.TryParse(parts[1], out _))
                badGuids.Add(parts.Length > 2 ? parts[2] : line);
        }
        if (badGuids.Count > 0)
        {
            error = "GUID không hợp lệ cho: " + string.Join(", ", badGuids);
            return false;
        }
        return true;
    }

    private static IEnumerable<Category> GetTargetCategories(Document doc, IReadOnlyCollection<BuiltInCategory>? only = null)
    {
        if (only is not null)
        {
            foreach (var bic in only)
            {
                Category? cat = null;
                try { cat = Category.GetCategory(doc, bic); } catch { /* category absent */ }
                if (cat is not null && cat.AllowsBoundParameters)
                    yield return cat;
            }

            yield break;
        }

        // Architecture + Structure + MEP — mirrors categories used by Norm / BOQ / G8 workflows.
        var names = new[]
        {
            // Architecture / finishes
            "OST_Walls", "OST_Floors", "OST_Roofs", "OST_Ceilings",
            "OST_Doors", "OST_Windows", "OST_Stairs", "OST_Ramps",
            "OST_Railings", "OST_CurtainWallPanels", "OST_CurtainWallMullions", "OST_Curtain_Systems",
            "OST_GenericModel", "OST_Furniture", "OST_Casework", "OST_Columns",
            // Structure
            "OST_StructuralColumns", "OST_StructuralFraming", "OST_StructuralFoundation",
            "OST_StructuralTruss", "OST_StructuralConnectionHandler", "OST_Rebar",
            "OST_AreaRein", "OST_FabricReinforcement", "OST_StructuralStiffener",
            // HVAC
            "OST_DuctCurves", "OST_FlexDuctCurves", "OST_DuctFitting", "OST_DuctAccessory",
            "OST_DuctTerminal", "OST_DuctInsulations", "OST_DuctLinings", "OST_MechanicalEquipment",
            // Plumbing / fire
            "OST_PipeCurves", "OST_FlexPipeCurves", "OST_PipeFitting", "OST_PipeAccessory",
            "OST_PipeInsulations", "OST_PlumbingFixtures", "OST_Sprinklers",
            "OST_FireAlarmDevices", "OST_SpecialityEquipment",
            // Electrical / ELV
            "OST_Conduit", "OST_ConduitFitting", "OST_CableTray", "OST_CableTrayFitting",
            "OST_ElectricalEquipment", "OST_ElectricalFixtures", "OST_LightingFixtures",
            "OST_LightingDevices", "OST_DataDevices", "OST_CommunicationDevices",
            "OST_SecurityDevices", "OST_TelephoneDevices", "OST_NurseCallDevices", "OST_Wire"
        };

        foreach (var name in names)
        {
            if (!Enum.TryParse<BuiltInCategory>(name, out var bic)) continue;
            Category? cat = null;
            try { cat = Category.GetCategory(doc, bic); } catch { /* category absent in this template */ }
            if (cat is not null && cat.AllowsBoundParameters)
                yield return cat;
        }
    }
}
