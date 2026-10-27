using Autodesk.Revit.DB;

namespace DSSTech.Revit;

internal static class ProjectKeys
{
    public static string For(Document doc) => doc.Title ?? doc.PathName ?? "untitled";
}
