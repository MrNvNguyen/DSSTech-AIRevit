using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

public sealed class PreflightIssue
{
    public string Code { get; set; } = "";
    public string MessageVi { get; set; } = "";
    public bool HardFail { get; set; }
}

public sealed class PreflightReport
{
    public bool Ok => Failures.Count == 0;
    public List<PreflightIssue> Failures { get; } = new List<PreflightIssue>();
    public List<PreflightIssue> Warnings { get; } = new List<PreflightIssue>();
    public Dictionary<string, int> CountsByKind { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public int CreatedElementCount => 0;
}

public static class PreflightEngine
{
    private static readonly string[] AreaKinds =
    {
        "Floor", "FloorHole", "FinishFloor", "Room", "IsolatedFoundation", "Ceiling"
    };

    public static PreflightReport Run(CadGeometry geom, IReadOnlyList<LayerMap> maps, CadBBox? gridEnvelopeMm)
    {
        var report = new PreflightReport();

        if (geom.InsUnits is null)
        {
            report.Failures.Add(Fail("insunits", "Không đọc được INSUNITS — không giả định mm."));
        }
        else if (geom.InsUnits != 4)
        {
            report.Failures.Add(Fail("insunits", $"INSUNITS={geom.InsUnits} (cần 4 = mm). Sai đơn vị sẽ làm lệch toàn bộ model."));
        }

        var inserts = geom.Entities.OfType<CadInsert>().ToList();
        var hasDoorLikeLines = false;

        foreach (var entity in geom.Entities)
        {
            var layer = entity.Layer ?? "0";
            LayerRule? matched = null;
            var ignored = false;
            foreach (var map in maps)
            {
                if (map.IsIgnored(layer))
                {
                    ignored = true;
                    break;
                }

                matched = map.Match(layer);
                if (matched != null)
                    break;
            }

            if (ignored)
                continue;

            if (matched is null)
            {
                if (!string.Equals(layer, "0", StringComparison.OrdinalIgnoreCase))
                    report.Failures.Add(Fail("unknown_layer", $"Layer lạ: {layer}"));
                continue;
            }

            if (!report.CountsByKind.ContainsKey(matched.Kind))
                report.CountsByKind[matched.Kind] = 0;
            report.CountsByKind[matched.Kind]++;

            if (entity is CadPolyline pl && AreaKinds.Any(k => string.Equals(k, matched.Kind, StringComparison.OrdinalIgnoreCase)))
            {
                if (!pl.Closed)
                    report.Failures.Add(Fail("open_polyline", $"Polyline hở trên layer {layer} (cần closed=1 cho {matched.Kind})."));
            }

            if (matched.RequireBlock)
            {
                if (entity is CadInsert)
                {
                    // ok
                }
                else if (entity is CadLine or CadPolyline)
                {
                    hasDoorLikeLines = true;
                }
            }
        }

        var requiredBlockKinds = maps.SelectMany(m => m.Rules).Where(r => r.RequireBlock).Select(r => r.Kind).Distinct().ToList();
        if (requiredBlockKinds.Count > 0 && inserts.Count == 0 && hasDoorLikeLines)
        {
            report.Failures.Add(Fail("exploded_block", "Cần block INSERT cho cửa/cột/TB nhưng không thấy INSERT — có thể đã nổ block."));
        }

        if (gridEnvelopeMm is not null && geom.Entities.Count > 0)
        {
            var bbox = ComputeBBox(geom);
            if (bbox is not null)
            {
                var dx = Math.Max(0, Math.Max(gridEnvelopeMm.Value.MinX - bbox.Value.MaxX, bbox.Value.MinX - gridEnvelopeMm.Value.MaxX));
                var dy = Math.Max(0, Math.Max(gridEnvelopeMm.Value.MinY - bbox.Value.MaxY, bbox.Value.MinY - gridEnvelopeMm.Value.MaxY));
                // also compare envelopes by center drift
                var cxCad = (bbox.Value.MinX + bbox.Value.MaxX) / 2;
                var cyCad = (bbox.Value.MinY + bbox.Value.MaxY) / 2;
                var cxGrid = (gridEnvelopeMm.Value.MinX + gridEnvelopeMm.Value.MaxX) / 2;
                var cyGrid = (gridEnvelopeMm.Value.MinY + gridEnvelopeMm.Value.MaxY) / 2;
                if (Math.Abs(cxCad - cxGrid) > 1000 || Math.Abs(cyCad - cyGrid) > 1000 || dx > 1000 || dy > 1000)
                    report.Failures.Add(Fail("bbox_vs_grid", "Bao CAD lệch lưới hiện có > 1 m. Kiểm tra Origin-to-Origin / Shared Coordinates."));
            }
        }

        return report;
    }

    private static PreflightIssue Fail(string code, string msg) =>
        new PreflightIssue { Code = code, MessageVi = msg, HardFail = true };

    private static CadBBox? ComputeBBox(CadGeometry geom)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var e in geom.Entities)
        {
            switch (e)
            {
                case CadLine l:
                    xs.Add(l.X0); xs.Add(l.X1); ys.Add(l.Y0); ys.Add(l.Y1);
                    break;
                case CadCircle c:
                    xs.Add(c.Cx - c.Radius); xs.Add(c.Cx + c.Radius);
                    ys.Add(c.Cy - c.Radius); ys.Add(c.Cy + c.Radius);
                    break;
                case CadPolyline p:
                    foreach (var pt in p.Points) { xs.Add(pt.X); ys.Add(pt.Y); }
                    break;
                case CadInsert ins:
                    xs.Add(ins.X); ys.Add(ins.Y);
                    break;
            }
        }

        if (xs.Count == 0)
            return null;
        return new CadBBox(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }
}

public readonly struct CadBBox
{
    public CadBBox(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
}
