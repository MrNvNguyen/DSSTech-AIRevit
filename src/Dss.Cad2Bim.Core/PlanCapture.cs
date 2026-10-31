using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Everything <see cref="ModelRevitPlanner.Plan"/> reads, saved as JSON so a run on a real DWG can be
/// replayed offline (tests/cad2bim-section-smoke --replay). Coordinates are model mm, as passed to the planner.
/// </summary>
public sealed class PlanCapture
{
    public int Version { get; set; } = 1;
    public string CapturedUtc { get; set; } = "";
    public string Layer { get; set; } = "";
    public ModelRevitPartKind Part { get; set; }
    public string BaseLevel { get; set; } = "";
    public string TopLevel { get; set; } = "";
    public double? SizeWidthMm { get; set; }
    public double? SizeHeightMm { get; set; }
    public double? SizeDiameterMm { get; set; }
    public string SizeRaw { get; set; } = "";
    public string? Family { get; set; }
    public string? TypeName { get; set; }
    public double BaseOffsetMm { get; set; }
    public double TopOffsetMm { get; set; }
    /// <summary>Model columns / structural walls; null when the model had none (planner falls back to CAD).</summary>
    public List<double[]>? Supports { get; set; }
    public List<double[]>? GridPoints { get; set; }
    public List<double>? GridAngles { get; set; }
    public List<Ent> Entities { get; set; } = new();

    /// <summary>One CAD entity. K = L line, P polyline, C circle, T text, I insert.</summary>
    public sealed class Ent
    {
        public string K { get; set; } = "";
        public string Layer { get; set; } = "";
        /// <summary>Line x0,y0,x1,y1 · polyline x,y pairs · circle cx,cy,r · text x,y,rot[,cx,cy] · insert x,y,rot.</summary>
        public double[] V { get; set; } = Array.Empty<double>();
        public bool Closed { get; set; }
        public string? S { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static PlanCapture From(
        CadGeometry geom, string layer, ModelRevitPartKind part, string baseLevel, string topLevel,
        AnnotationSize size, ModelRevitPlacement place,
        IReadOnlyList<CadPoint2>? gridPoints, IReadOnlyList<double>? gridAngles)
    {
        var capture = new PlanCapture
        {
            CapturedUtc = DateTime.UtcNow.ToString("o"),
            Layer = layer,
            Part = part,
            BaseLevel = baseLevel,
            TopLevel = topLevel,
            SizeWidthMm = size.WidthMm,
            SizeHeightMm = size.HeightMm,
            SizeDiameterMm = size.DiameterMm,
            SizeRaw = size.Raw ?? "",
            Family = place.FamilyName,
            TypeName = place.TypeName,
            BaseOffsetMm = place.BaseOffsetMm,
            TopOffsetMm = place.TopOffsetMm,
            Supports = place.Supports?.Select(s => new[] { s.CenterX, s.CenterY, s.WidthMm, s.DepthMm, s.RotationDeg }).ToList(),
            GridPoints = gridPoints?.Select(p => new[] { p.X, p.Y }).ToList(),
            GridAngles = gridAngles?.ToList()
        };
        foreach (var e in geom.Entities)
        {
            switch (e)
            {
                case CadLine l:
                    capture.Entities.Add(new Ent { K = "L", Layer = l.Layer, V = new[] { l.X0, l.Y0, l.X1, l.Y1 } });
                    break;
                case CadPolyline p:
                    capture.Entities.Add(new Ent
                    {
                        K = "P", Layer = p.Layer, Closed = p.Closed,
                        V = p.Points.SelectMany(pt => new[] { pt.X, pt.Y }).ToArray()
                    });
                    break;
                case CadCircle c:
                    capture.Entities.Add(new Ent { K = "C", Layer = c.Layer, V = new[] { c.Cx, c.Cy, c.Radius } });
                    break;
                case CadText t:
                    capture.Entities.Add(new Ent
                    {
                        K = "T", Layer = t.Layer, S = t.Value,
                        V = t.CenterX is double cx && t.CenterY is double cy
                            ? new[] { t.X, t.Y, t.RotationDeg, cx, cy }
                            : new[] { t.X, t.Y, t.RotationDeg }
                    });
                    break;
                case CadInsert i:
                    capture.Entities.Add(new Ent { K = "I", Layer = i.Layer, S = i.BlockName, V = new[] { i.X, i.Y, i.RotationDeg } });
                    break;
            }
        }

        return capture;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public static PlanCapture Load(string path) =>
        JsonSerializer.Deserialize<PlanCapture>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Empty capture: {path}");

    public CadGeometry Geometry()
    {
        var geom = new CadGeometry();
        foreach (var e in Entities)
        {
            var v = e.V;
            switch (e.K)
            {
                case "L":
                    geom.Entities.Add(new CadLine { Layer = e.Layer, X0 = v[0], Y0 = v[1], X1 = v[2], Y1 = v[3] });
                    break;
                case "P":
                    var poly = new CadPolyline { Layer = e.Layer, Closed = e.Closed };
                    for (var k = 0; k + 1 < v.Length; k += 2)
                        poly.Points.Add(new CadPoint2(v[k], v[k + 1]));
                    geom.Entities.Add(poly);
                    break;
                case "C":
                    geom.Entities.Add(new CadCircle { Layer = e.Layer, Cx = v[0], Cy = v[1], Radius = v[2] });
                    break;
                case "T":
                    geom.Entities.Add(new CadText
                    {
                        Layer = e.Layer, Value = e.S ?? "", X = v[0], Y = v[1], RotationDeg = v[2],
                        CenterX = v.Length >= 5 ? v[3] : null,
                        CenterY = v.Length >= 5 ? v[4] : null
                    });
                    break;
                case "I":
                    geom.Entities.Add(new CadInsert { Layer = e.Layer, BlockName = e.S ?? "", X = v[0], Y = v[1], RotationDeg = v[2] });
                    break;
            }
        }

        return geom;
    }

    public AnnotationSize Size() => new(SizeWidthMm, SizeHeightMm, SizeDiameterMm, SizeRaw);

    public ModelRevitPlacement Placement(Action<string>? log) => new()
    {
        BaseLevelName = BaseLevel,
        TopLevelName = TopLevel,
        BaseOffsetMm = BaseOffsetMm,
        TopOffsetMm = TopOffsetMm,
        FamilyName = Family,
        TypeName = TypeName,
        Supports = Supports?.Select(s => new SectionFootprint(s[0], s[1], s[2], s[3], s[4])).ToList(),
        Log = log
    };

    public List<CadPoint2>? Grids() => GridPoints?.Select(p => new CadPoint2(p[0], p[1])).ToList();
}
