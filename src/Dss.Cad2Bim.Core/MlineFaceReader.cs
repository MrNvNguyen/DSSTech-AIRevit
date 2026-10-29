using System;
using System.Collections.Generic;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Explode CAD MLINE vertices + element offsets into parallel face <see cref="CadLine"/>s
/// that <see cref="BeamCenterlineReader"/> can pair like two drawn edges.
/// </summary>
public static class MlineFaceReader
{
    /// <param name="vertices">MLINE vertex positions in drawing units (already mm for ModelRevit).</param>
    /// <param name="miters">Unit or raw miter at each vertex (same length as vertices); null → perpendicular of chord.</param>
    /// <param name="elementOffsets">Style element offsets (drawing units before scale), typically two faces.</param>
    /// <param name="scale">MLINE scale factor.</param>
    /// <param name="closed">Closed MLINE loops back to the first vertex.</param>
    public static List<CadLine> ToFaceLines(
        string layer,
        IReadOnlyList<(double X, double Y)> vertices,
        IReadOnlyList<(double X, double Y)>? miters,
        IReadOnlyList<double> elementOffsets,
        double scale,
        bool closed = false)
    {
        var lines = new List<CadLine>();
        if (vertices == null || vertices.Count < 2 || elementOffsets == null || elementOffsets.Count == 0)
            return lines;

        var n = vertices.Count;
        var segCount = closed ? n : n - 1;
        foreach (var offset in elementOffsets)
        {
            var o = offset * (Math.Abs(scale) > 1e-9 ? scale : 1.0);
            for (var i = 0; i < segCount; i++)
            {
                var i0 = i;
                var i1 = (i + 1) % n;
                var m0 = MiterAt(vertices, miters, i0, i1, prev: true);
                var m1 = MiterAt(vertices, miters, i1, i0, prev: false);
                var a = vertices[i0];
                var b = vertices[i1];
                var x0 = a.X + m0.X * o;
                var y0 = a.Y + m0.Y * o;
                var x1 = b.X + m1.X * o;
                var y1 = b.Y + m1.Y * o;
                if (Math.Abs(x1 - x0) < 1e-6 && Math.Abs(y1 - y0) < 1e-6)
                    continue;
                lines.Add(new CadLine { Layer = layer, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });
            }
        }

        return lines;
    }

    /// <summary>
    /// When vertex element parameters are known (DXF group 41 / ACadSharp Segment.Parameters),
    /// use the first parameter per face index instead of style offset × scale.
    /// </summary>
    public static List<CadLine> ToFaceLinesFromParameters(
        string layer,
        IReadOnlyList<(double X, double Y)> vertices,
        IReadOnlyList<(double X, double Y)>? miters,
        IReadOnlyList<IReadOnlyList<double>> parametersPerVertex,
        bool closed = false)
    {
        var lines = new List<CadLine>();
        if (vertices == null || vertices.Count < 2 || parametersPerVertex == null ||
            parametersPerVertex.Count != vertices.Count)
            return lines;

        var faceCount = 0;
        foreach (var p in parametersPerVertex)
            faceCount = Math.Max(faceCount, p?.Count ?? 0);
        if (faceCount == 0)
            return lines;

        var n = vertices.Count;
        var segCount = closed ? n : n - 1;
        for (var face = 0; face < faceCount; face++)
        {
            for (var i = 0; i < segCount; i++)
            {
                var i0 = i;
                var i1 = (i + 1) % n;
                var p0 = parametersPerVertex[i0];
                var p1 = parametersPerVertex[i1];
                if (p0 == null || p1 == null || face >= p0.Count || face >= p1.Count)
                    continue;
                var m0 = MiterAt(vertices, miters, i0, i1, prev: true);
                var m1 = MiterAt(vertices, miters, i1, i0, prev: false);
                var a = vertices[i0];
                var b = vertices[i1];
                var x0 = a.X + m0.X * p0[face];
                var y0 = a.Y + m0.Y * p0[face];
                var x1 = b.X + m1.X * p1[face];
                var y1 = b.Y + m1.Y * p1[face];
                if (Math.Abs(x1 - x0) < 1e-6 && Math.Abs(y1 - y0) < 1e-6)
                    continue;
                lines.Add(new CadLine { Layer = layer, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });
            }
        }

        return lines;
    }

    private static (double X, double Y) MiterAt(
        IReadOnlyList<(double X, double Y)> vertices,
        IReadOnlyList<(double X, double Y)>? miters,
        int index,
        int other,
        bool prev)
    {
        if (miters != null && index < miters.Count)
        {
            var m = miters[index];
            var len = Math.Sqrt(m.X * m.X + m.Y * m.Y);
            if (len > 1e-9)
                return (m.X / len, m.Y / len);
        }

        var a = vertices[index];
        var b = vertices[other];
        var dx = prev ? b.X - a.X : a.X - b.X;
        var dy = prev ? b.Y - a.Y : a.Y - b.Y;
        var lenChord = Math.Sqrt(dx * dx + dy * dy);
        if (lenChord < 1e-9)
            return (0, 1);
        // Left normal of the outgoing chord.
        return (-dy / lenChord, dx / lenChord);
    }
}
