using System.Globalization;
using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

internal static class ModelRevitAnnotationReader
{
    private const double MaxCadTextDistanceMm = 8000;

    public static bool TryRead(
        Document doc,
        ImportInstance import,
        CadGeometry geom,
        Reference reference,
        out AnnotationSize size,
        out string error)
    {
        size = default;
        error = "";
        var element = doc.GetElement(reference.ElementId);
        var raw = element switch
        {
            TextNote note => note.Text,
            Dimension dim => ReadDimension(dim),
            ImportInstance => NearestCadText(import, geom, reference),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = element is ImportInstance
                ? "Chữ trên CAD link chỉ còn nét vẽ, không còn chuỗi số (400, 200). Pick Text Note hoặc Dimension của Revit."
                : "Không đọc được chữ hoặc số từ Text Note / Dimension vừa pick.";
            return false;
        }

        if (!AnnotationSizeParser.TryParse(Clean(raw), out size))
        {
            error = "Không đọc được kích thước.";
            return false;
        }

        return true;
    }

    private static string Clean(string raw)
    {
        var s = raw.Replace("\\P", " ", StringComparison.OrdinalIgnoreCase);
        var semi = s.LastIndexOf(';');
        if (semi >= 0 && s.Contains('{'))
            s = s[(semi + 1)..].Trim().TrimEnd('}');
        return s.Trim();
    }

    private static string? ReadDimension(Dimension dim)
    {
        if (!string.IsNullOrWhiteSpace(dim.ValueOverride))
            return dim.ValueOverride;
        if (!string.IsNullOrWhiteSpace(dim.ValueString))
            return dim.ValueString;
        if (dim.Value is double feet)
            return Math.Round(UnitConv.ToMm(feet)).ToString(CultureInfo.InvariantCulture);

        try
        {
            var segments = dim.Segments;
            if (segments is null || segments.Size == 0)
                return null;
            var parts = new List<string>();
            foreach (DimensionSegment segment in segments)
            {
                if (!string.IsNullOrWhiteSpace(segment.ValueString))
                    parts.Add(segment.ValueString);
                else if (segment.Value is double segFeet)
                    parts.Add(Math.Round(UnitConv.ToMm(segFeet)).ToString(CultureInfo.InvariantCulture));
                if (parts.Count == 2)
                    break;
            }

            return parts.Count == 0 ? null : string.Join("x", parts);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? NearestCadText(ImportInstance import, CadGeometry geom, Reference reference)
    {
        var point = reference.GlobalPoint;
        if (point is null)
            return null;

        XYZ local;
        try
        {
            local = import.GetTotalTransform().Inverse.OfPoint(point);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            local = point;
        }

        var x = UnitConv.ToMm(local.X);
        var y = UnitConv.ToMm(local.Y);
        CadText? best = null;
        var bestD = MaxCadTextDistanceMm;
        foreach (var text in geom.Entities.OfType<CadText>())
        {
            if (string.IsNullOrWhiteSpace(text.Value))
                continue;
            var dx = text.X - x;
            var dy = text.Y - y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > bestD)
                continue;
            bestD = distance;
            best = text;
        }

        return best is null ? null : Clean(best.Value);
    }
}
