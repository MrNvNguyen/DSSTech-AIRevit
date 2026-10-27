using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace DSSTech.Revit.ModelRevit;

internal sealed class ImportInstanceSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => elem is ImportInstance;

    public bool AllowReference(Reference reference, XYZ position) => true;
}

internal sealed class ImportLayerPickFilter : ISelectionFilter
{
    private readonly ElementId _importId;

    public ImportLayerPickFilter(ElementId importId) => _importId = importId;

    public bool AllowElement(Element elem) => elem.Id == _importId;

    public bool AllowReference(Reference reference, XYZ position) => reference.ElementId == _importId;
}

internal sealed class TextOrDimensionPickFilter : ISelectionFilter
{
    private readonly ElementId _importId;

    public TextOrDimensionPickFilter(ElementId importId) => _importId = importId;

    public bool AllowElement(Element elem) =>
        elem is TextNote || elem is Dimension || elem.Id == _importId;

    public bool AllowReference(Reference reference, XYZ position) => true;
}
