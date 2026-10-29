using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Types.Units;
using Autodesk.Revit.DB;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;
using CadPoint = CSMath.XYZ;

namespace DSSTech.Revit.Cad;

/// <summary>
/// Revit draws CAD text but does not return the string. Read TEXT/MTEXT from the linked file.
/// </summary>
internal static class CadLinkText
{
    public static void Append(ImportInstance import, View? view, CadGeometry geom)
    {
        var path = TryPath(import);
        if (path is null)
        {
            AppendFromViewExport(import, view, geom);
            return;
        }

        DwgContent? content = null;
        try
        {
            if (path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
                content = new DwgContent(FromDxf(path), new List<CadEntity>());
            else if (path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                content = FromDwg(path);
        }
        catch (Exception ex)
        {
            DSSTech.Revit.DssTechLog.Write($"cad text read failed: {ex.Message}");
        }

        if (content is null)
        {
            DSSTech.Revit.DssTechLog.Write("cad text file empty or locked; exporting the view");
            AppendFromViewExport(import, view, geom);
            return;
        }

        var transform = import.GetTotalTransform();
        if (content.Labels.Count == 0)
        {
            // Marks may be missing. Outlines below still replace Revit strokes when the DWG has curves.
            DSSTech.Revit.DssTechLog.Write("cad text file has no labels; exporting the view for marks");
            AppendFromViewExport(import, view, geom);
        }
        else
        {
            DSSTech.Revit.DssTechLog.Write($"cad text labels={content.Labels.Count}");
            foreach (var item in content.Labels)
            {
                if (string.IsNullOrWhiteSpace(item.Value))
                    continue;
                var point = ToModel(item.Xmm, item.Ymm, transform);
                var center = ToModel(item.Cxmm, item.Cymm, transform);
                geom.Entities.Add(new CadText
                {
                    Value = item.Value.Trim(),
                    X = point.X,
                    Y = point.Y,
                    RotationDeg = ToModelRotationDeg(item.RotationDeg, transform),
                    CenterX = center.X,
                    CenterY = center.Y
                });
            }
        }

        if (content.Curves.Count > 0)
        {
            // Revit's ImportInstance geometry grows between commands. The DWG has every outline.
            geom.Entities.RemoveAll(entity => entity is CadLine or CadPolyline or CadCircle);
            foreach (var curve in content.Curves)
                geom.Entities.Add(PlaceCurve(curve, transform));
            DSSTech.Revit.DssTechLog.Write($"cad outlines={content.Curves.Count}");
        }
    }

    private static CadPoint2 ToModel(double xMm, double yMm, Transform transform)
    {
        var world = transform.OfPoint(new XYZ(UnitConv.MmToInternal(xMm), UnitConv.MmToInternal(yMm), 0));
        return new CadPoint2(UnitConv.ToMm(world.X), UnitConv.ToMm(world.Y));
    }

    private static CadEntity PlaceCurve(CadEntity curve, Transform transform)
    {
        switch (curve)
        {
            case CadLine line:
                var a = ToModel(line.X0, line.Y0, transform);
                var b = ToModel(line.X1, line.Y1, transform);
                return new CadLine { Layer = line.Layer, X0 = a.X, Y0 = a.Y, X1 = b.X, Y1 = b.Y };
            case CadPolyline poly:
                var placed = new CadPolyline { Layer = poly.Layer, Closed = poly.Closed };
                foreach (var point in poly.Points)
                    placed.Points.Add(ToModel(point.X, point.Y, transform));
                return placed;
            case CadCircle circle:
                var center = ToModel(circle.Cx, circle.Cy, transform);
                return new CadCircle
                {
                    Layer = circle.Layer,
                    Cx = center.X,
                    Cy = center.Y,
                    Radius = circle.Radius
                };
            default:
                return curve;
        }
    }

    private sealed class DwgContent
    {
        public DwgContent(List<(string Value, double Xmm, double Ymm, double RotationDeg, double Cxmm, double Cymm)> labels, List<CadEntity> curves)
        {
            Labels = labels;
            Curves = curves;
        }

        public List<(string Value, double Xmm, double Ymm, double RotationDeg, double Cxmm, double Cymm)> Labels { get; }
        public List<CadEntity> Curves { get; }
    }

    private static double ToModelRotationDeg(double cadDeg, Transform transform)
    {
        var rad = cadDeg * Math.PI / 180.0;
        var dir = transform.OfVector(new XYZ(Math.Cos(rad), Math.Sin(rad), 0));
        return Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI;
    }

    private static bool FileExists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);

    /// <summary>
    /// Imported CAD has no DWG path. Export the view once and read TEXT from that file.
    /// Export units are millimetres about the internal origin, same frame as the column centres.
    /// </summary>
    private static void AppendFromViewExport(ImportInstance import, View? view, CadGeometry geom)
    {
        if (view is null || view.ViewType == ViewType.DrawingSheet)
            return;

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DSSTech-cad-text");
        System.IO.Directory.CreateDirectory(dir);
        var name = "marks-" + Guid.NewGuid().ToString("N");
        var dwg = System.IO.Path.Combine(dir, name + ".dwg");
        try
        {
            var options = new DWGExportOptions
            {
                TargetUnit = ExportUnit.Millimeter,
                FileVersion = ACADVersion.R2013,
                MergedViews = false,
                ExportingAreas = false,
                SharedCoords = false
            };
            var views = new List<ElementId> { view.Id };
            if (!import.Document.Export(dir, name, views, options) || !System.IO.File.Exists(dwg))
                return;

            foreach (var item in FromDwg(dwg).Labels)
            {
                if (!CadMark.IsAnnotation(item.Value))
                    continue;
                geom.Entities.Add(new CadText
                {
                    Value = item.Value,
                    X = item.Xmm,
                    Y = item.Ymm,
                    RotationDeg = item.RotationDeg,
                    CenterX = item.Cxmm,
                    CenterY = item.Cymm
                });
            }
        }
        catch (Exception ex)
        {
            DSSTech.Revit.DssTechLog.Write($"cad text export failed: {ex.Message}");
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(dwg))
                    System.IO.File.Delete(dwg);
            }
            catch (Exception)
            {
                // Temp export can stay if Revit still holds the file.
            }
        }
    }

    private static string? TryPath(ImportInstance import)
    {
        try
        {
            var type = import.Document.GetElement(import.GetTypeId()) as CADLinkType;
            var ext = type?.GetExternalFileReference();
            if (ext is null)
                return null;
            var path = ModelPathUtils.ConvertModelPathToUserVisiblePath(ext.GetAbsolutePath());
            if (FileExists(path))
                return path;
            var stored = ModelPathUtils.ConvertModelPathToUserVisiblePath(ext.GetPath());
            if (FileExists(stored))
                return stored;
            var projectDir = System.IO.Path.GetDirectoryName(import.Document.PathName);
            if (!string.IsNullOrWhiteSpace(projectDir) && !string.IsNullOrWhiteSpace(stored))
            {
                var beside = System.IO.Path.Combine(projectDir, System.IO.Path.GetFileName(stored));
                if (FileExists(beside))
                    return beside;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<(string Value, double Xmm, double Ymm, double RotationDeg, double Cxmm, double Cymm)> FromDxf(string path)
    {
        var parsed = DxfParser.Parse(path);
        var list = new List<(string, double, double, double, double, double)>();
        foreach (var text in parsed.Entities.OfType<CadText>())
        {
            var value = Clean(text.Value);
            if (!CadMark.IsAnnotation(value))
                continue;
            var x = ToMm(text.X, parsed.InsUnits);
            var y = ToMm(text.Y, parsed.InsUnits);
            list.Add((value, x, y, text.RotationDeg, x, y));
        }

        return list;
    }

    private static DwgContent FromDwg(string path)
    {
        // Revit keeps the linked DWG open. Copy with shared read, then parse the copy.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DSSTech-cad-text");
        System.IO.Directory.CreateDirectory(dir);
        var temp = System.IO.Path.Combine(dir, "src-" + Guid.NewGuid().ToString("N") + ".dwg");
        try
        {
            using (var input = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            using (var output = new System.IO.FileStream(temp, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read))
                input.CopyTo(output);

            CadDocument? doc;
            using (var reader = new DwgReader(temp))
                doc = reader.Read();
            if (doc is null)
                return new DwgContent(new List<(string, double, double, double, double, double)>(), new List<CadEntity>());

            var units = doc.Header.InsUnits;
            var labels = new List<(string, double, double, double, double, double)>();
            var curves = new List<CadEntity>();
            foreach (var entity in doc.Entities)
                Collect(entity, units, labels, curves, Transform2.Identity, 0, null);
            return new DwgContent(labels, curves);
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(temp))
                    System.IO.File.Delete(temp);
            }
            catch (Exception)
            {
                // Revit or the reader may still hold the temp copy.
            }
        }
    }

    private readonly struct Transform2
    {
        public Transform2(double m11, double m12, double m21, double m22, double dx, double dy)
        {
            M11 = m11;
            M12 = m12;
            M21 = m21;
            M22 = m22;
            Dx = dx;
            Dy = dy;
        }

        public double M11 { get; }
        public double M12 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double Dx { get; }
        public double Dy { get; }

        public static Transform2 Identity { get; } = new(1, 0, 0, 1, 0, 0);

        public CadPoint Apply(CadPoint point) =>
            new(M11 * point.X + M12 * point.Y + Dx, M21 * point.X + M22 * point.Y + Dy, point.Z);

        public Transform2 Then(Insert insert)
        {
            var cos = Math.Cos(insert.Rotation);
            var sin = Math.Sin(insert.Rotation);
            var a = cos * insert.XScale;
            var b = -sin * insert.YScale;
            var c = sin * insert.XScale;
            var d = cos * insert.YScale;
            return new Transform2(
                M11 * a + M12 * c,
                M11 * b + M12 * d,
                M21 * a + M22 * c,
                M21 * b + M22 * d,
                M11 * insert.InsertPoint.X + M12 * insert.InsertPoint.Y + Dx,
                M21 * insert.InsertPoint.X + M22 * insert.InsertPoint.Y + Dy);
        }
    }

    private static void Collect(
        Entity entity,
        UnitsType units,
        List<(string Value, double Xmm, double Ymm, double RotationDeg, double Cxmm, double Cymm)> labels,
        List<CadEntity> curves,
        Transform2 transform,
        int depth,
        string? inheritedLayer)
    {
        // Layer 0 inside a block is drawn on the insert's layer. Revit reports that layer on pick.
        var layer = ResolveLayer(entity.Layer?.Name, inheritedLayer);
        switch (entity)
        {
            case MText mtext:
                Add(mtext.Value, transform.Apply(mtext.InsertPoint), units, labels, WorldRotationDeg(transform, mtext.Rotation),
                    transform.Apply(MTextCenter(mtext)));
                break;
            case TextEntity text:
                Add(text.Value, transform.Apply(text.InsertPoint), units, labels, WorldRotationDeg(transform, text.Rotation),
                    transform.Apply(TextCenter(text)));
                break;
            case ACadSharp.Entities.Line line:
                AddLine(layer, transform.Apply(line.StartPoint), transform.Apply(line.EndPoint), units, curves);
                break;
            case MLine mline:
                AddMLine(layer, mline, transform, units, curves);
                break;
            case IPolyline poly:
                AddPolyline(layer, poly, transform, units, curves);
                break;
            case Hatch:
            case ACadSharp.Entities.Arc:
                break;
            case Circle circle:
                var center = transform.Apply(circle.Center);
                var scale = Math.Sqrt(transform.M11 * transform.M11 + transform.M21 * transform.M21);
                curves.Add(new CadCircle
                {
                    Layer = layer,
                    Cx = ToMm(center.X, units),
                    Cy = ToMm(center.Y, units),
                    Radius = ToMm(circle.Radius * scale, units)
                });
                break;
            case Insert insert when depth < 8 && insert.Block is not null:
                foreach (var attribute in insert.Attributes)
                    Add(attribute.Value, transform.Apply(attribute.InsertPoint), units, labels, WorldRotationDeg(transform.Then(insert), attribute.Rotation),
                        transform.Apply(TextCenter(attribute)));
                var next = transform.Then(insert);
                foreach (var child in insert.Block.Entities)
                    Collect(child, units, labels, curves, next, depth + 1, layer);
                break;
        }
    }

    private static double WorldRotationDeg(Transform2 transform, double localRad)
    {
        var cos = Math.Cos(localRad);
        var sin = Math.Sin(localRad);
        var wx = transform.M11 * cos + transform.M12 * sin;
        var wy = transform.M21 * cos + transform.M22 * sin;
        return Math.Atan2(wy, wx) * 180.0 / Math.PI;
    }

    private static string ResolveLayer(string? own, string? inherited)
    {
        if (!string.IsNullOrWhiteSpace(own) && !string.Equals(own, "0", StringComparison.OrdinalIgnoreCase))
            return own;
        return string.IsNullOrWhiteSpace(inherited) ? "0" : inherited;
    }

    private static void AddLine(string layer, CadPoint start, CadPoint end, UnitsType units, List<CadEntity> curves)
    {
        curves.Add(new CadLine
        {
            Layer = layer,
            X0 = ToMm(start.X, units),
            Y0 = ToMm(start.Y, units),
            X1 = ToMm(end.X, units),
            Y1 = ToMm(end.Y, units)
        });
    }

    /// <summary>
    /// Explode MLINE into parallel face lines (style element offsets × scale, or vertex parameters)
    /// so BeamCenterlineReader can pair them like ordinary CAD edge faces.
    /// </summary>
    private static void AddMLine(
        string layer, MLine mline, Transform2 transform, UnitsType units, List<CadEntity> curves)
    {
        var verts = mline.Vertices;
        if (verts is null || verts.Count < 2)
            return;

        var positions = new List<(double X, double Y)>(verts.Count);
        var miters = new List<(double X, double Y)>(verts.Count);
        var parameters = new List<IReadOnlyList<double>>(verts.Count);
        var anyParams = false;
        foreach (var v in verts)
        {
            var p = transform.Apply(v.Position);
            positions.Add((ToMm(p.X, units), ToMm(p.Y, units)));
            // Miter is a direction — apply the linear part of the insert transform only.
            var mx = transform.M11 * v.Miter.X + transform.M12 * v.Miter.Y;
            var my = transform.M21 * v.Miter.X + transform.M22 * v.Miter.Y;
            miters.Add((mx, my));
            if (v.Segments is { Count: > 0 })
            {
                var faceParams = new List<double>(v.Segments.Count);
                foreach (var seg in v.Segments)
                {
                    var param = seg.Parameters is { Count: > 0 } ? seg.Parameters[0] : 0.0;
                    faceParams.Add(ToMm(param, units));
                }

                parameters.Add(faceParams);
                if (faceParams.Count > 0)
                    anyParams = true;
            }
            else
            {
                parameters.Add(Array.Empty<double>());
            }
        }

        List<CadLine> faces;
        if (anyParams)
        {
            faces = MlineFaceReader.ToFaceLinesFromParameters(
                layer, positions, miters, parameters,
                closed: (mline.Flags & MLineFlags.Closed) != 0);
        }
        else
        {
            var scale = Math.Abs(mline.ScaleFactor) > 1e-9 ? mline.ScaleFactor : 1.0;
            var offsets = new List<double>();
            if (mline.Style?.Elements is { Count: > 0 } elements)
            {
                foreach (var el in elements)
                    offsets.Add(ToMm(el.Offset, units));
            }

            if (offsets.Count == 0)
                offsets.Add(0);
            faces = MlineFaceReader.ToFaceLines(
                layer, positions, miters, offsets, scale,
                closed: (mline.Flags & MLineFlags.Closed) != 0);
        }

        curves.AddRange(faces);
    }

    private static void AddPolyline(string layer, IPolyline poly, Transform2 transform, UnitsType units, List<CadEntity> curves)
    {
        var placed = new CadPolyline { Layer = layer, Closed = poly.IsClosed };
        foreach (var vertex in poly.Vertices)
        {
            var point = transform.Apply(new CadPoint(vertex.Location[0], vertex.Location[1], 0));
            placed.Points.Add(new CadPoint2(ToMm(point.X, units), ToMm(point.Y, units)));
        }

        if (!placed.Closed && placed.Points.Count >= 4)
        {
            var first = placed.Points[0];
            var last = placed.Points[placed.Points.Count - 1];
            if (Math.Abs(first.X - last.X) < 1 && Math.Abs(first.Y - last.Y) < 1)
                placed.Closed = true;
        }

        if (placed.Points.Count >= 2)
            curves.Add(placed);
    }

    private static void Add(
        string? value,
        CadPoint point,
        UnitsType units,
        List<(string Value, double Xmm, double Ymm, double RotationDeg, double Cxmm, double Cymm)> list,
        double rotationDeg,
        CadPoint center)
    {
        var clean = Clean(value);
        if (!CadMark.IsAnnotation(clean))
            return;
        list.Add((clean, ToMm(point.X, units), ToMm(point.Y, units), rotationDeg, ToMm(center.X, units), ToMm(center.Y, units)));
    }

    /// <summary>Average glyph advance as a fraction of text height (digits / capitals in common CAD fonts).</summary>
    private const double GlyphWidthRatio = 0.7;

    /// <summary>
    /// Middle of a TEXT box in its own frame. Left-aligned text is anchored at the baseline start; other
    /// justifications at the alignment point; Aligned/Fit span insertion → alignment point.
    /// </summary>
    private static CadPoint TextCenter(TextEntity text)
    {
        var height = text.Height;
        var width = Clean(text.Value).Length * height * (text.WidthFactor > 0 ? text.WidthFactor : 1) * GlyphWidthRatio;
        var horizontal = (int)text.HorizontalAlignment;
        var aligned = text.AlignmentPoint;
        var hasAlignment = Math.Abs(aligned.X) > 1e-9 || Math.Abs(aligned.Y) > 1e-9;
        var anchor = text.InsertPoint;
        var startFraction = 0.0;
        if (hasAlignment && horizontal is 3 or 5)
        {
            var dx = aligned.X - anchor.X;
            var dy = aligned.Y - anchor.Y;
            width = Math.Sqrt(dx * dx + dy * dy);
        }
        else if (hasAlignment && horizontal is 1 or 2 or 4)
        {
            anchor = aligned;
            startFraction = horizontal == 2 ? 1.0 : 0.5;
        }

        var baseFraction = horizontal == 4 ? 0.5 : (int)text.VerticalAlignment switch
        {
            2 => 0.5,
            3 => 1.0,
            _ => 0.0
        };
        return Offset(anchor, text.Rotation, width * (0.5 - startFraction), height * (0.5 - baseFraction));
    }

    /// <summary>Middle of an MTEXT box from its attachment point (1 top-left … 9 bottom-right).</summary>
    private static CadPoint MTextCenter(MText mtext)
    {
        var height = mtext.Height;
        var estimate = Clean(mtext.Value).Length * height * GlyphWidthRatio;
        var width = mtext.RectangleWidth > 0 ? Math.Min(mtext.RectangleWidth, estimate) : estimate;
        var attach = Math.Max(1, Math.Min(9, (int)mtext.AttachmentPoint)) - 1;
        var column = attach % 3;
        var row = attach / 3;
        return Offset(mtext.InsertPoint, mtext.Rotation, width * (0.5 - 0.5 * column), height * (0.5 * row - 0.5));
    }

    private static CadPoint Offset(CadPoint origin, double rotationRad, double along, double across)
    {
        var cos = Math.Cos(rotationRad);
        var sin = Math.Sin(rotationRad);
        return new CadPoint(origin.X + cos * along - sin * across, origin.Y + sin * along + cos * across, origin.Z);
    }

    private static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var s = raw.Replace("\\P", " ", StringComparison.OrdinalIgnoreCase);
        var semi = s.LastIndexOf(';');
        if (semi >= 0 && s.Contains('{'))
            s = s[(semi + 1)..].Trim().TrimEnd('}');
        return s.Trim().ToUpperInvariant();
    }

    private static double ToMm(double value, int? insUnits) => insUnits switch
    {
        1 => value * 25.4,
        4 => value,
        5 => value * 10.0,
        6 => value * 1000.0,
        _ => value
    };

    private static double ToMm(double value, UnitsType units) => units switch
    {
        UnitsType.Inches => value * 25.4,
        UnitsType.Millimeters => value,
        UnitsType.Centimeters => value * 10.0,
        UnitsType.Meters => value * 1000.0,
        _ => value
    };
}
