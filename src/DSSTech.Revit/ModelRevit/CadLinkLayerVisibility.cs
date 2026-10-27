using Autodesk.Revit.DB;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Ensures CAD link layer subcategories stay visible on the active view (no isolate/hide).
/// Unhides categories left hidden by older ModelRevit builds in the same session.
/// </summary>
internal static class CadLinkLayerVisibility
{
    private const string TxShowLayers = "DSSTech ModelRevit show layers";

    public static void EnsureAllImportLayersVisible(View view, ImportInstance import)
    {
        var doc = import.Document;
        using var tx = new Transaction(doc, TxShowLayers);
        tx.Start();
        foreach (Category sub in import.Category.SubCategories)
        {
            if (view.GetCategoryHidden(sub.Id))
                view.SetCategoryHidden(sub.Id, false);
        }

        // Geometry read needs a fresh model. Regenerate only succeeds inside a transaction.
        doc.Regenerate();
        tx.Commit();
    }
}
