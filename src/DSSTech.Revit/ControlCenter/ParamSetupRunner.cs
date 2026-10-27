using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;

namespace DSSTech.Revit;

internal readonly record struct ParamPackageChoice(string Package, string? Part, string? Discipline);

internal static class ParamSetupRunner
{
    public static bool EnsureChosen(UIApplication app)
    {
        var doc = app.ActiveUIDocument?.Document;
        if (doc is null)
            return false;
        if (CadProjectProfileStore.TryGetParamPackage(ProjectKeys.For(doc), out _, out _, out _))
            return true;
        return PromptAndApply(app, announce: false, out _);
    }

    public static bool ApplySavedOrPrompt(UIApplication app, out string note)
    {
        var doc = app.ActiveUIDocument?.Document;
        if (doc is null)
        {
            note = "Mở project Revit trước.";
            return false;
        }

        var key = ProjectKeys.For(doc);
        if (!CadProjectProfileStore.TryGetParamPackage(key, out var package, out var part, out var discipline))
            return PromptAndApply(app, announce: false, out note);

        note = Apply(doc, package, part, discipline);
        ControlCenterView.Instance.RefreshStatus(doc);
        if (IsBindFailure(note))
        {
            TaskDialog.Show("DSSTech", note);
            return false;
        }

        return true;
    }

    public static bool PromptAndApply(UIApplication app, bool announce, out string note)
    {
        note = "";
        var doc = app.ActiveUIDocument?.Document;
        if (doc is null)
        {
            note = "Mở project Revit trước.";
            TaskDialog.Show("DSSTech", note);
            return false;
        }

        var key = ProjectKeys.For(doc);
        CadProjectProfileStore.TryGetParamPackage(key, out var package, out var part, out var discipline);
        if (!ParamSetupWindow.TryPick(app.MainWindowHandle, package, part, discipline, out var choice))
            return false;

        CadProjectProfileStore.RememberParamPackage(key, choice.Package, choice.Part, choice.Discipline);
        note = Apply(doc, choice.Package, choice.Part, choice.Discipline);
        ControlCenterView.Instance.RefreshStatus(doc);
        if (announce || IsBindFailure(note))
            TaskDialog.Show("DSSTech", note);
        return !IsBindFailure(note);
    }

    private static string Apply(Document doc, string package, string? part, string? discipline)
    {
        if (package == ParamPackageCatalog.Builtin)
            return "Gói tham số sẵn có của Revit. Không gắn trường QS_*.";

        var path = Path.Combine(PluginPaths.Root, "Config", "QS_SharedParameters.txt");
        if (package == ParamPackageCatalog.Full)
            return SharedParameterSetup.BindFromFile(doc, path);

        var names = ParamPackageCatalog.Names(package, part, discipline);
        var categories = ParamPackageCatalog.Categories(package, part, discipline);
        return SharedParameterSetup.BindSelected(doc, path, names, categories);
    }

    private static bool IsBindFailure(string message) =>
        message.StartsWith("Không", StringComparison.OrdinalIgnoreCase) ||
        message.StartsWith("Thiếu", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("readParamDatabase", StringComparison.OrdinalIgnoreCase);
}
