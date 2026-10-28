using System;
using System.Collections.Generic;
using System.Linq;

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
        if (walls.Count != 1 || Math.Abs(walls[0].ThicknessMm - 200) > 0.1 || Math.Abs(walls[0].Y0 - 101.5) > 0.01)
        {
            message = walls.Count == 0
                ? "wall pair: expected one centerline"
                : $"wall pair: expected thickness 200 at y=101.5 (mid of CAD faces), got {walls[0].ThicknessMm} at y={walls[0].Y0}";
            return false;
        }

        if (!WallCases(out message))
            return false;

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

        var columnLike = new CadPolyline { Layer = "W", Closed = true };
        columnLike.Points.Add(new CadPoint2(0, 0));
        columnLike.Points.Add(new CadPoint2(500, 0));
        columnLike.Points.Add(new CadPoint2(500, 400));
        columnLike.Points.Add(new CadPoint2(0, 400));
        var shortRuns = WallCenterlineReader.FromEntities(new List<CadEntity> { columnLike });
        if (shortRuns.Count != 0)
        {
            message = $"L/b≤4 column outline must not become a wall, got {shortRuns.Count}";
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

    private static bool WallCases(out string message)
    {
        var lShape = WallCenterlineReader.FromEntities(new List<CadEntity>
        {
            Poly(true, (0, 0), (6000, 0), (6000, 200), (200, 200), (200, 5000), (0, 5000))
        });
        if (!HasRuns("L-corner", lShape, 200, out message,
                (100, 100, 6000, 100), (100, 100, 100, 5000)))
            return false;

        var tee = WallCenterlineReader.FromEntities(new List<CadEntity>
        {
            Line(0, 0, 8000, 0), Line(0, 300, 3900, 300), Line(4100, 300, 8000, 300),
            Line(0, 0, 0, 300), Line(8000, 0, 8000, 300),
            Line(3900, 300, 3900, 5000), Line(4100, 300, 4100, 5000), Line(3900, 5000, 4100, 5000)
        });
        if (!HasRuns("T-junction", tee, -1, out message,
                (0, 150, 4000, 150), (4000, 150, 8000, 150), (4000, 150, 4000, 5000)))
            return false;

        var rad = 30.0 * Math.PI / 180.0;
        (double X, double Y) R(double x, double y) =>
            (10000 + x * Math.Cos(rad) - y * Math.Sin(rad), 5000 + x * Math.Sin(rad) + y * Math.Cos(rad));
        var uRuns = WallCenterlineReader.FromEntities(new List<CadEntity>
        {
            Poly(true, R(0, 0), R(3000, 0), R(3000, 2500), R(2600, 2500), R(2600, 400), R(400, 400), R(400, 2500), R(0, 2500))
        });
        var w0 = R(200, 200);
        var w1 = R(2800, 200);
        var l0 = R(200, 2500);
        var l1 = R(2800, 2500);
        if (!HasRuns("U-core 30°", uRuns, 400, out message,
                (w0.X, w0.Y, w1.X, w1.Y), (w0.X, w0.Y, l0.X, l0.Y), (w1.X, w1.Y, l1.X, l1.Y)))
            return false;
        // ClassifyWalls is the architectural envelope helper only (Exterior/Interior).
        // StructuralWall planning uses CoreShaft + centerline and does not consume these flags.
        var uSides = ModelRevitPlanner.ClassifyWalls(uRuns);
        for (var i = 0; i < uRuns.Count; i++)
        {
            var run = uRuns[i];
            var mx = (run.X0 + run.X1) / 2;
            var my = (run.Y0 + run.Y1) / 2;
            var c = R(1500, 1250);
            var away = (mx - c.X) * uSides[i].OutX + (my - c.Y) * uSides[i].OutY;
            if (!uSides[i].Exterior || away <= 0)
            {
                message = $"U-core 30° (arch classify): wall {i} expected exterior with outward away from core, got exterior={uSides[i].Exterior} dot={away:0}";
                return false;
            }
        }

        var building = new List<CadEntity>
        {
            Poly(true, (0, 0), (20000, 0), (20000, 15000), (0, 15000)),
            Poly(true, (300, 300), (19700, 300), (19700, 14700), (300, 14700))
        };
        building.AddRange(new List<CadEntity> { Poly(true, R(0, 0), R(3000, 0), R(3000, 2500), R(2600, 2500), R(2600, 400), R(400, 400), R(400, 2500), R(0, 2500)) });
        var bRuns = WallCenterlineReader.FromEntities(building);
        if (bRuns.Count != 7)
        {
            message = $"building + core: expected 7 walls, got {bRuns.Count}";
            return false;
        }

        var bSides = ModelRevitPlanner.ClassifyWalls(bRuns);
        for (var i = 0; i < bRuns.Count; i++)
        {
            var perimeter = Math.Abs(bRuns[i].ThicknessMm - 300) < 0.1;
            if (bSides[i].Exterior != perimeter)
            {
                message = $"building + core: wall {i} ({bRuns[i].ThicknessMm} mm) exterior={bSides[i].Exterior}, expected {perimeter}";
                return false;
            }
        }

        if (!HasRuns("perimeter corner", bRuns.FindAll(r => Math.Abs(r.ThicknessMm - 300) < 0.1), 300, out message,
                (150, 150, 19850, 150), (19850, 150, 19850, 14850), (150, 14850, 19850, 14850), (150, 150, 150, 14850)))
            return false;

        var broken = WallCenterlineReader.FromEntities(new List<CadEntity>
        {
            Line(0, 0, 4000, 0), Line(0, 200, 4000, 200),
            Line(4500, 0, 9000, 0), Line(4500, 200, 9000, 200),
            ClosedRect(4000, -150, 4500, 350)
        });
        if (!HasRuns("wall broken by column", broken, 200, out message,
                (0, 100, 4000, 100), (4500, 100, 9000, 100)))
            return false;

        var close = WallCenterlineReader.FromEntities(new List<CadEntity>
        {
            Poly(true, (0, 0), (5000, 0), (5000, 200), (0, 200)),
            Poly(true, (0, 350), (5000, 350), (5000, 550), (0, 550))
        });
        if (!HasRuns("two walls 150 apart", close, 200, out message,
                (0, 100, 5000, 100), (0, 450, 5000, 450)))
            return false;

        // StructuralWall plan: CoreShaft + wallcenterline (not Exterior/Interior from envelope).
        var wallGeom = new CadGeometry();
        wallGeom.Entities.Add(Poly(true, (0, 0), (8000, 0), (8000, 300), (0, 300)));
        foreach (var e in wallGeom.Entities)
            e.Layer = "STRUCT";
        var structuralOps = ModelRevitPlanner.Plan(
            wallGeom,
            "STRUCT",
            ModelRevitPartKind.StructuralWall,
            "L1",
            "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { TypeName = "S-WALL" });
        if (structuralOps.Count == 0)
        {
            message = "structural wall plan: expected at least 1 Wall op";
            return false;
        }

        foreach (var op in structuralOps)
        {
            if (!string.Equals(op.Category, "Walls", StringComparison.OrdinalIgnoreCase))
                continue;
            var fn = op.Fields.TryGetProperty("function", out var f) ? f.GetString() : null;
            var loc = op.Fields.TryGetProperty("locationLine", out var l) ? l.GetString() : null;
            var str = op.Fields.TryGetProperty("structural", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.True;
            if (!str || !string.Equals(fn, "CoreShaft", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(loc, "wallcenterline", StringComparison.OrdinalIgnoreCase))
            {
                message = $"structural wall plan: expected CoreShaft+wallcenterline, got function={fn} locationLine={loc} structural={str}";
                return false;
            }
        }

        if (!CoreStubCases(out message) || !RotatedWallPlanCase(out message))
            return false;

        message = "ok";
        return true;
    }

    /// <summary>C-core with short return lips (L/b ≤ 4) at the open ends: lips are walls, not columns.</summary>
    private static bool CoreStubCases(out string message)
    {
        var rad = 30.0 * Math.PI / 180.0;
        (double X, double Y) R(double x, double y) =>
            (10000 + x * Math.Cos(rad) - y * Math.Sin(rad), 5000 + x * Math.Sin(rad) + y * Math.Cos(rad));
        var outline = new[]
        {
            R(0, 0), R(3000, 0), R(3000, 2500), R(2400, 2500), R(2400, 2250), R(2750, 2250),
            R(2750, 250), R(250, 250), R(250, 2250), R(600, 2250), R(600, 2500), R(0, 2500)
        };
        (double, double, double, double) Span((double X, double Y) a, (double X, double Y) b) => (a.X, a.Y, b.X, b.Y);
        var expected = new[]
        {
            Span(R(125, 125), R(2875, 125)), Span(R(125, 125), R(125, 2375)), Span(R(2875, 125), R(2875, 2375)),
            Span(R(125, 2375), R(600, 2375)), Span(R(2400, 2375), R(2875, 2375))
        };

        var poly = Poly(true, outline);
        if (!HasRuns("C-core lips (polyline)", WallCenterlineReader.FromEntities(new List<CadEntity> { poly }), 250, out message, expected))
            return false;

        var lines = new List<CadEntity>();
        for (var i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            lines.Add(Line(a.X, a.Y, b.X, b.Y));
        }

        if (!HasRuns("C-core lips (lines)", WallCenterlineReader.FromEntities(lines), 250, out message, expected))
            return false;

        var stubGeom = new CadGeometry();
        stubGeom.Entities.Add(Poly(true, outline));
        foreach (var e in stubGeom.Entities)
            e.Layer = "STRUCT";
        var ops = ModelRevitPlanner.Plan(
            stubGeom, "STRUCT", ModelRevitPartKind.StructuralWall, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            columnFamily: "COL",
            placement: new ModelRevitPlacement { TypeName = "S-WALL" });
        var wallOps = 0;
        var columnOps = 0;
        foreach (var op in ops)
        {
            if (op.Category == "Walls")
                wallOps++;
            else if (op.Category == "StructuralColumns")
                columnOps++;
        }

        if (wallOps != 5 || columnOps != 0)
        {
            message = $"C-core lips plan: expected 5 walls + 0 columns, got {wallOps} walls + {columnOps} columns";
            return false;
        }

        var isolated = WallCenterlineReader.FromEntities(new List<CadEntity>
        {
            Line(0, 0, 8000, 0), Line(0, 250, 8000, 250),
            Poly(true, (0, 2000), (600, 2000), (600, 2250), (0, 2250))
        });
        if (!HasRuns("isolated short rect", isolated, 250, out message, (0, 125, 8000, 125)))
            return false;

        message = "ok";
        return true;
    }

    /// <summary>30° wall, CAD length 2999.9: whole-mm length and whole-mm offsets in the grid frame.</summary>
    private static bool RotatedWallPlanCase(out string message)
    {
        const double gx = 10000.37;
        const double gy = 5000.81;
        var cad = 30.005 * Math.PI / 180.0;
        (double X, double Y) P(double along, double across) =>
            (gx + along * Math.Cos(cad) - across * Math.Sin(cad), gy + along * Math.Sin(cad) + across * Math.Cos(cad));
        var geom = new CadGeometry();
        geom.Entities.Add(Poly(true, P(1234.37, 2000.4), P(4234.27, 2000.4), P(4234.27, 2250.4), P(1234.37, 2250.4)));
        foreach (var e in geom.Entities)
            e.Layer = "STRUCT";
        var ops = ModelRevitPlanner.Plan(
            geom, "STRUCT", ModelRevitPartKind.StructuralWall, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            gridPoints: new List<CadPoint2> { new(gx, gy), new(gx + 20000, gy - 3000) },
            placement: new ModelRevitPlacement { TypeName = "S-WALL" },
            gridAnglesDeg: new List<double> { 30, -60 });
        if (ops.Count != 1)
        {
            message = $"30° wall plan: expected 1 wall, got {ops.Count}";
            return false;
        }

        var f = ops[0].Fields;
        var s = f.GetProperty("startMm");
        var t = f.GetProperty("endMm");
        var sx = s[0].GetDouble();
        var sy = s[1].GetDouble();
        var ex = t[0].GetDouble();
        var ey = t[1].GetDouble();
        var rad = 30.0 * Math.PI / 180.0;
        double Along(double x, double y) => (x - gx) * Math.Cos(rad) + (y - gy) * Math.Sin(rad);
        double Across(double x, double y) => -(x - gx) * Math.Sin(rad) + (y - gy) * Math.Cos(rad);
        static bool Whole(double v) => Math.Abs(v - Math.Round(v)) < 1e-6;
        var len = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
        var a0 = Along(sx, sy);
        var a1 = Along(ex, ey);
        var c0 = Across(sx, sy);
        var c1 = Across(ex, ey);
        var rotatedFrame = f.TryGetProperty("rotatedFrame", out var rf) && rf.ValueKind == System.Text.Json.JsonValueKind.True;
        var c = P(1234.37, 2125.4);
        var d = P(4234.27, 2125.4);
        var move = Math.Max(
            Math.Min(Math.Sqrt((sx - c.X) * (sx - c.X) + (sy - c.Y) * (sy - c.Y)), Math.Sqrt((sx - d.X) * (sx - d.X) + (sy - d.Y) * (sy - d.Y))),
            Math.Min(Math.Sqrt((ex - c.X) * (ex - c.X) + (ey - c.Y) * (ey - c.Y)), Math.Sqrt((ex - d.X) * (ex - d.X) + (ey - d.Y) * (ey - d.Y))));
        if (!rotatedFrame || Math.Abs(len - 3000) > 1e-6 || !Whole(a0) || !Whole(a1) || !Whole(c0) ||
            Math.Abs(c0 - c1) > 1e-6 || move > 2)
        {
            message = $"30° wall plan: len={len:0.######} along=({a0:0.######},{a1:0.######}) across=({c0:0.######},{c1:0.######}) move={move:0.###} rotatedFrame={rotatedFrame}";
            return false;
        }

        if (!BeamLabelParser.TryParse("D1Y19-40x60", out var bl1) ||
            bl1.Mark != "D1Y19" || bl1.WidthMm != 400 || bl1.DepthMm != 600)
        {
            message = $"beam label D1Y19-40x60: got mark={bl1.Mark} {bl1.WidthMm}x{bl1.DepthMm}";
            return false;
        }

        if (!BeamLabelParser.TryParse("30x60", out var bl2) ||
            bl2.HasMark || bl2.WidthMm != 300 || bl2.DepthMm != 600)
        {
            message = $"beam label 30x60: got mark={bl2.Mark} {bl2.WidthMm}x{bl2.DepthMm}";
            return false;
        }

        if (!BeamLabelParser.TryParse("300x600", out var bl3) ||
            bl3.WidthMm != 300 || bl3.DepthMm != 600)
        {
            message = $"beam label 300x600: got {bl3.WidthMm}x{bl3.DepthMm}";
            return false;
        }

        if (!BeamLabelParser.TryParse("30x140", out var bl30x140) ||
            bl30x140.HasMark || bl30x140.WidthMm != 300 || bl30x140.DepthMm != 1400)
        {
            message = $"beam label 30x140: got mark={bl30x140.Mark} {bl30x140.WidthMm}x{bl30x140.DepthMm}";
            return false;
        }

        // One side 2 digits → both cm×10 (100x65 → 1000×650).
        if (!BeamLabelParser.TryParse("100x65", out var bl100x65) ||
            bl100x65.WidthMm != 1000 || bl100x65.DepthMm != 650)
        {
            message = $"beam label 100x65: got {bl100x65.WidthMm}x{bl100x65.DepthMm}";
            return false;
        }

        if (!BeamLabelParser.TryParse("D1Y21-100x65", out var blD1Y21) ||
            blD1Y21.Mark != "D1Y21" || blD1Y21.WidthMm != 1000 || blD1Y21.DepthMm != 650)
        {
            message = $"beam label D1Y21-100x65: got mark={blD1Y21.Mark} {blD1Y21.WidthMm}x{blD1Y21.DepthMm}";
            return false;
        }

        if (!BeamLabelParser.TryParse("D1X23-30X140", out var bl4) ||
            bl4.Mark != "D1X23" || bl4.WidthMm != 300 || bl4.DepthMm != 1400)
        {
            message = $"beam label D1X23-30X140: got mark={bl4.Mark} {bl4.WidthMm}x{bl4.DepthMm}";
            return false;
        }

        if (!BeamLabelParser.TryParse("D1PY6", out var bl5) || !bl5.HasMark || bl5.HasSize || bl5.Mark != "D1PY6")
        {
            message = $"beam label D1PY6: got mark={bl5.Mark} size={bl5.HasSize}";
            return false;
        }

        BeamCenterlineReader.Orient(100, 50, 0, 50, out var ox0, out var oy0, out var ox1, out var oy1);
        if (ox0 > ox1 || Math.Abs(oy0 - 50) > 1e-9)
        {
            message = $"beam orient H: expected left→right, got ({ox0},{oy0})-({ox1},{oy1})";
            return false;
        }

        BeamCenterlineReader.Orient(10, 800, 10, 100, out ox0, out oy0, out ox1, out oy1);
        if (oy0 > oy1 || Math.Abs(ox0 - 10) > 1e-9)
        {
            message = $"beam orient V: expected bottom→top, got ({ox0},{oy0})-({ox1},{oy1})";
            return false;
        }

        var beamFaces = new List<CadEntity>
        {
            Line(0, 0, 4000, 0),
            Line(0, 400, 4000, 400)
        };
        var beamRuns = BeamCenterlineReader.FromEntities(beamFaces);
        if (beamRuns.Count != 1 || Math.Abs(beamRuns[0].ThicknessMm - 400) > 0.1 ||
            beamRuns[0].X0 > beamRuns[0].X1)
        {
            message = $"beam centerline: expected 1 run 400 thick L→R, got {beamRuns.Count}";
            return false;
        }

        if (!BeamPlanRulesCase(out message))
            return false;

        message = "ok";
        return true;
    }

    /// <summary>
    /// Column-to-column split; merge+extend stubs; size-only wins over far mark+size;
    /// one shared Mark on axis; two Marks; size inheritance to blank segment.
    /// </summary>
    private static bool BeamPlanRulesCase(out string message)
    {
        if (!BeamSpanCases(out message))
            return false;

        // Shared mark on axis (one CadText) → both planned segments get D1Y10.
        var sharedGeom = BeamAxisGeom(
            new CadText { Layer = "ANN", Value = "D1Y10-40x60", X = 2000, Y = 500, RotationDeg = 0 });
        var sharedOps = ModelRevitPlanner.Plan(
            sharedGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        if (sharedOps.Count != 2)
        {
            message = $"beam shared mark plan: expected 2 ops, got {sharedOps.Count}";
            return false;
        }

        foreach (var op in sharedOps)
        {
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() : null;
            var w = op.Fields.TryGetProperty("widthMm", out var ww) ? ww.GetInt32() : 0;
            var d = op.Fields.TryGetProperty("depthMm", out var dd) ? dd.GetInt32() : 0;
            if (!string.Equals(mark, "D1Y10", StringComparison.OrdinalIgnoreCase) || w != 400 || d != 600)
            {
                message = $"beam shared mark: expected D1Y10 400x600, got mark={mark} {w}x{d}";
                return false;
            }
        }

        // Two marks on two segments → each keeps its own.
        var twoGeom = BeamAxisGeom(
            new CadText { Layer = "ANN", Value = "D1Y10", X = 2000, Y = 500 },
            new CadText { Layer = "ANN", Value = "D1Y19-30x60", X = 6000, Y = 500 });
        var twoOps = ModelRevitPlanner.Plan(
            twoGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        if (twoOps.Count != 2)
        {
            message = $"beam two-mark plan: expected 2 ops, got {twoOps.Count}";
            return false;
        }

        var marks = twoOps
            .Select(op => op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (marks.Count != 2 ||
            !string.Equals(marks[0], "D1Y10", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(marks[1], "D1Y19", StringComparison.OrdinalIgnoreCase))
        {
            message = $"beam two-mark: expected D1Y10+D1Y19, got {string.Join(",", marks)}";
            return false;
        }

        // Size inheritance: first span has 30x60; second span blank → inherits 300x600.
        var sizeGeom = BeamAxisGeom(
            new CadText { Layer = "ANN", Value = "30x60", X = 2000, Y = 500 });
        var sizeOps = ModelRevitPlanner.Plan(
            sizeGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        if (sizeOps.Count != 2)
        {
            message = $"beam size inherit plan: expected 2 ops, got {sizeOps.Count}";
            return false;
        }

        foreach (var op in sizeOps)
        {
            var w = op.Fields.TryGetProperty("widthMm", out var ww) ? ww.GetInt32() : 0;
            var d = op.Fields.TryGetProperty("depthMm", out var dd) ? dd.GetInt32() : 0;
            if (w != 300 || d != 600)
            {
                message = $"beam size inherit: expected both 300x600, got {w}x{d}";
                return false;
            }
        }

        // Size-only on THIS vertical segment wins over far horizontal D1X18-50x60.
        var localSizeGeom = new CadGeometry();
        localSizeGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 0, Y1 = 4000 });
        localSizeGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 400, Y0 = 0, X1 = 400, Y1 = 4000 });
        localSizeGeom.Entities.Add(ColRect(0, -200, 400, 200));
        localSizeGeom.Entities.Add(ColRect(0, 3800, 400, 4200));
        localSizeGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "30x60", X = 500, Y = 2000, RotationDeg = 90
        });
        localSizeGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1X18-50x60", X = 2500, Y = 500, RotationDeg = 0
        });
        var localSizeOps = ModelRevitPlanner.Plan(
            localSizeGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        if (localSizeOps.Count < 1)
        {
            message = "beam local size-only: expected ≥1 op";
            return false;
        }

        var localOp = localSizeOps[0];
        var lw = localOp.Fields.TryGetProperty("widthMm", out var lww) ? lww.GetInt32() : 0;
        var ld = localOp.Fields.TryGetProperty("depthMm", out var ldd) ? ldd.GetInt32() : 0;
        if (lw != 300 || ld != 600)
        {
            message = $"beam local size-only: expected 300x600 (not 500x600), got {lw}x{ld}";
            return false;
        }

        // Text under span 2 (middle 550 mm off the centerline) wins over the axis's 30x60 on span 1.
        var besideGeom = BeamAxisGeom(
            new CadText { Layer = "ANN", Value = "30x60", X = 1500, Y = 500, CenterX = 2000, CenterY = 550 },
            new CadText { Layer = "ANN", Value = "50x60", X = 5500, Y = -500, CenterX = 6000, CenterY = -350 });
        var besideOps = ModelRevitPlanner.Plan(
            besideGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var besideTypes = besideOps
            .Select(op => (X: op.Fields.GetProperty("startMm")[0].GetDouble(), Type: op.Fields.GetProperty("familyType").GetString()))
            .OrderBy(t => t.X)
            .Select(t => t.Type)
            .ToList();
        if (besideTypes.Count != 2 || besideTypes[0] != "300x600" || besideTypes[1] != "500x600")
        {
            message = $"beam on-segment text: expected 300x600 then 500x600, got {string.Join(",", besideTypes)}";
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>Main = support face to support face; secondary breaks at main-beam faces.</summary>
    private static bool BeamSpanCases(out string message)
    {
        message = "";
        static SectionFootprint Col(double x, double y) => new(x, y, 400, 400);
        string Describe(List<BeamSegment> segs) =>
            string.Join("; ", segs.Select(s => $"{s.Kind} ({s.Run.X0:0},{s.Run.Y0:0})-({s.Run.X1:0},{s.Run.Y1:0})"));
        bool Has(List<BeamSegment> segs, BeamSpanKind kind, double x0, double y0, double x1, double y1) =>
            segs.Any(s => s.Kind == kind &&
                          Math.Abs(s.Run.X0 - x0) <= 1 && Math.Abs(s.Run.Y0 - y0) <= 1 &&
                          Math.Abs(s.Run.X1 - x1) <= 1 && Math.Abs(s.Run.Y1 - y1) <= 1);

        // (i) 3 columns on one axis → 2 main spans, column face to face.
        var three = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(0, 200, 8000, 200, 400) },
            new[] { Col(0, 200), Col(4000, 200), Col(8000, 200) });
        if (three.Count != 2 || !Has(three, BeamSpanKind.Main, 200, 200, 3800, 200) ||
            !Has(three, BeamSpanKind.Main, 4200, 200, 7800, 200))
        {
            message = $"beam 3 columns: expected main 200..3800 + 4200..7800, got {Describe(three)}";
            return false;
        }

        // (ii) A structural wall across the axis is a support too.
        var wall = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(0, 0, 8000, 0, 300) },
            new[] { Col(0, 0), new SectionFootprint(4000, 0, 200, 3000), Col(8000, 0) });
        if (wall.Count != 2 || !Has(wall, BeamSpanKind.Main, 200, 0, 3900, 0) ||
            !Has(wall, BeamSpanKind.Main, 4100, 0, 7800, 0))
        {
            message = $"beam wall support: expected main 200..3900 + 4100..7800, got {Describe(wall)}";
            return false;
        }

        // (iii) Secondary crossing two main beams → 3 pieces ending at main faces; mains stay whole.
        var grid = BeamCenterlineReader.PrepareRuns(
            new[]
            {
                new WallRun(0, 0, 8000, 0, 400),
                new WallRun(0, 6000, 8000, 6000, 400),
                new WallRun(4000, -2000, 4000, 8000, 300)
            },
            new[] { Col(0, 0), Col(8000, 0), Col(0, 6000), Col(8000, 6000) });
        if (grid.Count(s => s.Kind == BeamSpanKind.Main) != 2 ||
            !Has(grid, BeamSpanKind.Main, 200, 0, 7800, 0) || !Has(grid, BeamSpanKind.Main, 200, 6000, 7800, 6000) ||
            grid.Count(s => s.Kind == BeamSpanKind.Secondary) != 3 ||
            !Has(grid, BeamSpanKind.Secondary, 4000, -2000, 4000, -200) ||
            !Has(grid, BeamSpanKind.Secondary, 4000, 200, 4000, 5800) ||
            !Has(grid, BeamSpanKind.Secondary, 4000, 6200, 4000, 8000))
        {
            message = $"beam secondary split: got {Describe(grid)}";
            return false;
        }

        // (v) Collinear CAD pieces 2500 apart at a crossing beam → one main span column to column.
        var bridged = BeamCenterlineReader.PrepareRuns(
            new[]
            {
                new WallRun(200, 0, 3000, 0, 300),
                new WallRun(5500, 0, 9800, 0, 300),
                new WallRun(4250, -3000, 4250, 3000, 400)
            },
            new[] { Col(0, 0), Col(10000, 0) });
        if (bridged.Count(s => s.Kind == BeamSpanKind.Main) != 1 || !Has(bridged, BeamSpanKind.Main, 200, 0, 9800, 0) ||
            bridged.Count(s => s.Kind == BeamSpanKind.Secondary) != 2)
        {
            message = $"beam gap at crossing: expected main 200..9800 + 2 secondary, got {Describe(bridged)}";
            return false;
        }

        // Stub stopping short of both columns reaches their faces.
        var stub = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(800, 200, 3000, 200, 400) },
            new[] { Col(0, 200), Col(4000, 200) });
        if (stub.Count != 1 || !Has(stub, BeamSpanKind.Main, 200, 200, 3800, 200))
        {
            message = $"beam extend to columns: expected main 200..3800, got {Describe(stub)}";
            return false;
        }

        // (vi) 30° beam in two CAD pieces → one main span between rotated columns.
        var rad = 30.0 * Math.PI / 180.0;
        (double X, double Y) P(double t) => (1000 + t * Math.Cos(rad), 2000 + t * Math.Sin(rad));
        var a0 = P(200);
        var a1 = P(3000);
        var b0 = P(4000);
        var b1 = P(7800);
        var c0 = P(0);
        var c1 = P(8000);
        var diagonal = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(a0.X, a0.Y, a1.X, a1.Y, 400), new WallRun(b0.X, b0.Y, b1.X, b1.Y, 400) },
            new[] { new SectionFootprint(c0.X, c0.Y, 400, 400, 30), new SectionFootprint(c1.X, c1.Y, 400, 400, 30) });
        if (diagonal.Count != 1 || !Has(diagonal, BeamSpanKind.Main, a0.X, a0.Y, b1.X, b1.Y))
        {
            message = $"beam 30° merge: expected one main ({a0.X:0},{a0.Y:0})-({b1.X:0},{b1.Y:0}), got {Describe(diagonal)}";
            return false;
        }

        return true;
    }

    private static CadGeometry BeamAxisGeom(params CadText[] texts)
    {
        var geom = new CadGeometry();
        geom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 8000, Y1 = 0 });
        geom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 400, X1 = 8000, Y1 = 400 });
        geom.Entities.Add(ColRect(-200, 0, 200, 400));
        geom.Entities.Add(ColRect(3800, 0, 4200, 400));
        geom.Entities.Add(ColRect(7800, 0, 8200, 400));
        foreach (var text in texts)
            geom.Entities.Add(text);
        return geom;
    }

    private static CadPolyline ColRect(double minX, double minY, double maxX, double maxY)
    {
        var poly = ClosedRect(minX, minY, maxX, maxY);
        poly.Layer = "COL";
        return poly;
    }

    private static bool HasRuns(string name, List<WallRun> runs, double thickness, out string message,
        params (double X0, double Y0, double X1, double Y1)[] expected)
    {
        message = "";
        var got = string.Join("; ", runs.ConvertAll(r => $"({r.X0:0.#},{r.Y0:0.#})-({r.X1:0.#},{r.Y1:0.#}) t={r.ThicknessMm}"));
        if (runs.Count != expected.Length)
        {
            message = $"{name}: expected {expected.Length} walls, got {runs.Count}: {got}";
            return false;
        }

        foreach (var e in expected)
        {
            var found = runs.Exists(r =>
                (thickness < 0 || Math.Abs(r.ThicknessMm - thickness) < 0.1) &&
                ((Near(r.X0, r.Y0, e.X0, e.Y0) && Near(r.X1, r.Y1, e.X1, e.Y1)) ||
                 (Near(r.X0, r.Y0, e.X1, e.Y1) && Near(r.X1, r.Y1, e.X0, e.Y0))));
            if (!found)
            {
                message = $"{name}: missing wall ({e.X0:0.#},{e.Y0:0.#})-({e.X1:0.#},{e.Y1:0.#}); got {got}";
                return false;
            }
        }

        return true;
    }

    private static bool Near(double x0, double y0, double x1, double y1) =>
        Math.Abs(x0 - x1) < 0.5 && Math.Abs(y0 - y1) < 0.5;

    private static CadPolyline Poly(bool closed, params (double X, double Y)[] points)
    {
        var poly = new CadPolyline { Layer = "W", Closed = closed };
        foreach (var (x, y) in points)
            poly.Points.Add(new CadPoint2(x, y));
        return poly;
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
