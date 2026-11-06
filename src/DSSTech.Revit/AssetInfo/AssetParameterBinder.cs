using System.IO;
using Autodesk.Revit.DB;
using Dss.AssetInfo.Core;

namespace DSSTech.Revit.AssetInfo;

internal sealed class BindReport
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public List<string> Conflicts { get; } = new();

    public string Text =>
        $"Tham số TS_*: thêm mới {Inserted}, cập nhật category {Updated}." +
        (Conflicts.Count > 0 ? "\nKhông bind được (trùng tên với tham số khác đã có):\n- " + string.Join("\n- ", Conflicts) : "");
}

/// <summary>
/// Binds the TS_* shared parameters as TYPE parameters (data belongs to the asset type, like the form).
/// Unlike SharedParameterSetup (QS_*), it restores the user's previous shared-parameter file afterwards.
/// Must run inside an open transaction.
/// </summary>
internal static class AssetParameterBinder
{
    public static BindReport EnsureBound(Document doc, string sharedParameterFile, IEnumerable<BuiltInCategory> categories)
    {
        if (!File.Exists(sharedParameterFile))
            throw new InvalidOperationException($"Thiếu file Shared Parameter TS_*:\n{sharedParameterFile}");

        var app = doc.Application;
        var previous = app.SharedParametersFilename;
        var report = new BindReport();
        try
        {
            app.SharedParametersFilename = sharedParameterFile;
            var defFile = app.OpenSharedParameterFile()
                          ?? throw new InvalidOperationException("Revit không mở được file Shared Parameter TS_* (kiểm tra *META/*GROUP/*PARAM và GUID).");

            var definitions = new List<ExternalDefinition>();
            foreach (DefinitionGroup group in defFile.Groups)
            foreach (Definition d in group.Definitions)
                if (d is ExternalDefinition ext && AssetParams.IsAssetParam(ext.Name))
                    definitions.Add(ext);
            if (definitions.Count == 0)
                throw new InvalidOperationException("File Shared Parameter không có tham số TS_*.");

            var catSet = app.Create.NewCategorySet();
            foreach (var bic in categories)
            {
                Category? cat = null;
                try { cat = Category.GetCategory(doc, bic); } catch { /* not in this template */ }
                if (cat is { AllowsBoundParameters: true }) catSet.Insert(cat);
            }
            if (catSet.IsEmpty)
                throw new InvalidOperationException("Không có category hợp lệ để bind TS_*.");

            var existing = ExistingBindings(doc);
            foreach (var def in definitions)
            {
                var binding = app.Create.NewTypeBinding(catSet);
                if (existing.TryGetValue(def.Name, out var found))
                {
                    // Same name but different GUID (project parameter / other SP file) or bound per instance:
                    // never replace silently — report it.
                    if (found.Definition is not ExternalDefinition fe || fe.GUID != def.GUID || found.Binding is not TypeBinding)
                    {
                        report.Conflicts.Add($"{def.Name} ({(found.Binding is InstanceBinding ? "đang là Instance" : "khác GUID")})");
                        continue;
                    }
                    if (doc.ParameterBindings.ReInsert(def, binding, GroupTypeId.Data)) report.Updated++;
                }
                else if (doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data))
                {
                    report.Inserted++;
                }
            }
        }
        finally
        {
            // Restore the user's own SP file (if they had none, leave ours: Revit rejects an empty path).
            if (!string.IsNullOrEmpty(previous))
                try { app.SharedParametersFilename = previous; } catch { /* binding already done */ }
        }
        return report;
    }

    private static Dictionary<string, (Definition Definition, Binding Binding)> ExistingBindings(Document doc)
    {
        var map = new Dictionary<string, (Definition, Binding)>(StringComparer.OrdinalIgnoreCase);
        var it = doc.ParameterBindings.ForwardIterator();
        it.Reset();
        while (it.MoveNext())
        {
            if (it.Key is Definition d && AssetParams.IsAssetParam(d.Name) && it.Current is Binding b)
                map[d.Name] = (d, b);
        }
        return map;
    }
}
