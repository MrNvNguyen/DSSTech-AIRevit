using System;
using System.Collections.Generic;

namespace Dss.Cad2Bim.Core;

/// <summary>Non-Revit sanity checks for column footprint extraction.</summary>
public static class SectionFootprintSelfTest
{
    public static bool Run(out string message)
    {
        var entities = new List<CadEntity>
        {
            ClosedRect(50, 150, 550, 550),
            new CadLine { Layer = "4", X0 = 80, Y0 = 180, X1 = 520, Y1 = 520 },
            new CadLine { Layer = "4", X0 = 520, Y0 = 180, X1 = 80, Y1 = 520 },
            new CadLine { Layer = "4", X0 = 550, Y0 = 150, X1 = 700, Y1 = 150 }
        };

        var footprints = SectionFootprintReader.FromEntities(entities);
        if (footprints.Count != 1)
        {
            message = $"expected 1 footprint, got {footprints.Count}";
            return false;
        }

        var fp = footprints[0];
        if (Math.Abs(fp.CenterX - 300) > 1 || Math.Abs(fp.CenterY - 350) > 1)
        {
            message = $"center expected (300,350), got ({fp.CenterX},{fp.CenterY})";
            return false;
        }

        if (Math.Abs(fp.WidthMm - 500) > 0.1 || Math.Abs(fp.DepthMm - 400) > 0.1)
        {
            message = $"size expected 500x400, got {fp.WidthMm}x{fp.DepthMm}";
            return false;
        }

        var corner = new List<CadEntity>
        {
            Line(0, 0, 400, 0),
            Line(400, 0, 400, 400),
            Line(400, 400, 0, 400),
            Line(0, 400, 0, 0),
            Line(0, 400, 0, 8000),
            Line(400, 0, 9000, 0),
            Line(30, 30, 370, 370)
        };

        var cornerFootprints = SectionFootprintReader.FromEntities(corner);
        if (cornerFootprints.Count != 1)
        {
            message = $"corner column: expected 1 footprint, got {cornerFootprints.Count}";
            return false;
        }

        var cfp = cornerFootprints[0];
        if (Math.Abs(cfp.CenterX - 200) > 1 || Math.Abs(cfp.CenterY - 200) > 1 ||
            Math.Abs(cfp.WidthMm - 400) > 0.1 || Math.Abs(cfp.DepthMm - 400) > 0.1)
        {
            message = $"corner column: expected 400x400 at (200,200), got {cfp.WidthMm}x{cfp.DepthMm} at ({cfp.CenterX},{cfp.CenterY})";
            return false;
        }

        var chamfer = new List<CadEntity>
        {
            Line(0, 0, 400, 0),
            Line(0, 0, 0, 200),
            Line(0, 200, 320, 200),
            Line(400, 0, 400, 120),
            Line(320, 200, 400, 120),
            Line(400, 0, 6000, 0),
            Line(6000, 0, 6000, 200)
        };
        var chamferFootprints = SectionFootprintReader.FromEntities(chamfer);
        if (chamferFootprints.Count != 1)
        {
            message = $"chamfer column: expected 1 footprint, got {chamferFootprints.Count}";
            return false;
        }

        var ch = chamferFootprints[0];
        if (Math.Abs(ch.CenterX - 200) > 1 || Math.Abs(ch.CenterY - 100) > 1 ||
            Math.Abs(ch.WidthMm - 400) > 0.1 || Math.Abs(ch.DepthMm - 200) > 0.1)
        {
            message = $"chamfer column: expected 400x200 at (200,100), got {ch.WidthMm}x{ch.DepthMm} at ({ch.CenterX},{ch.CenterY})";
            return false;
        }

        var rotated = RotatedColumn();
        var rotatedFootprints = SectionFootprintReader.FromEntities(rotated);
        if (rotatedFootprints.Count != 1)
        {
            message = $"rotated column: expected 1 footprint, got {rotatedFootprints.Count}";
            return false;
        }

        var rot = rotatedFootprints[0];
        if (Math.Abs(rot.CenterX - 1000) > 2 || Math.Abs(rot.CenterY - 2000) > 2 ||
            Math.Abs(rot.WidthMm - 1200) > 0.1 || Math.Abs(rot.DepthMm - 400) > 0.1 ||
            Math.Abs(rot.RotationDeg - 30) > 1)
        {
            message = $"rotated column: expected 1200x400 at 30° (1000,2000), got {rot.WidthMm}x{rot.DepthMm} at {rot.RotationDeg:0.#}° ({rot.CenterX:0},{rot.CenterY:0})";
            return false;
        }

        var walls = WallCenterlineReader.FromEntities(new List<CadEntity>
        {
            new CadLine { Layer = "W", X0 = 0, Y0 = 0, X1 = 4000, Y1 = 0 },
            new CadLine { Layer = "W", X0 = 0, Y0 = 203, X1 = 4000, Y1 = 203 }
        });
        if (walls.Count != 1 || Math.Abs(walls[0].ThicknessMm - 200) > 0.1 || Math.Abs(walls[0].Y0 - 100) > 1)
        {
            message = walls.Count == 0
                ? "wall pair: expected one centerline"
                : $"wall pair: expected thickness 200 at y=100, got {walls[0].ThicknessMm} at y={walls[0].Y0}";
            return false;
        }

        var box = new CadPolyline { Layer = "W", Closed = true };
        box.Points.Add(new CadPoint2(0, 0));
        box.Points.Add(new CadPoint2(5000, 0));
        box.Points.Add(new CadPoint2(5000, 300));
        box.Points.Add(new CadPoint2(0, 300));
        var boxed = WallCenterlineReader.FromEntities(new List<CadEntity> { box });
        if (boxed.Count != 1 || Math.Abs(boxed[0].ThicknessMm - 300) > 0.1)
        {
            message = $"wall rectangle: expected one 300 mm wall, got {boxed.Count}";
            return false;
        }

        var (rx, ry) = ModelRevitPlanner.RoundColumnPosition(2175, 250, 0);
        if (Math.Abs(rx - 2175) > 0.1 || Math.Abs(ry - 250) > 0.1)
        {
            message = $"round position: expected (2175,250), got ({rx},{ry})";
            return false;
        }

        var (fx, fy) = ModelRevitPlanner.RoundColumnPosition(2175.4, 250.6, 0);
        if (Math.Abs(fx - 2175) > 0.1 || Math.Abs(fy - 251) > 0.1)
        {
            message = $"round fraction: expected (2175,251), got ({fx},{fy})";
            return false;
        }

        var rad = 30.0 * Math.PI / 180.0;
        var (ax, ay) = ModelRevitPlanner.RoundColumnPosition(2175 * Math.Cos(rad), 2175 * Math.Sin(rad), 30);
        if (Math.Abs(ax - 2175 * Math.Cos(rad)) > 0.1 || Math.Abs(ay - 2175 * Math.Sin(rad)) > 0.1)
        {
            message = $"round rotated position: expected 2175 along 30°, got ({ax},{ay})";
            return false;
        }

        message = "ok";
        return true;
    }

    private static List<CadEntity> RotatedColumn()
    {
        var rad = 30.0 * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        const double cx = 1000;
        const double cy = 2000;
        CadPoint2 At(double along, double across) =>
            new(cx + along * cos - across * sin, cy + along * sin + across * cos);
        var a = At(-600, -200);
        var b = At(600, -200);
        var c = At(600, 200);
        var d = At(-600, 200);
        return new List<CadEntity>
        {
            Line(a.X, a.Y, b.X, b.Y),
            Line(b.X, b.Y, c.X, c.Y),
            Line(c.X, c.Y, d.X, d.Y),
            Line(d.X, d.Y, a.X, a.Y)
        };
    }

    private static CadLine Line(double x0, double y0, double x1, double y1) =>
        new CadLine { Layer = "4", X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };

    private static CadPolyline ClosedRect(double minX, double minY, double maxX, double maxY)
    {
        var poly = new CadPolyline { Layer = "4", Closed = true };
        poly.Points.Add(new CadPoint2(minX, minY));
        poly.Points.Add(new CadPoint2(maxX, minY));
        poly.Points.Add(new CadPoint2(maxX, maxY));
        poly.Points.Add(new CadPoint2(minX, maxY));
        return poly;
    }
}
