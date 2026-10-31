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

        var (fx, fy) = ModelRevitPlanner.RoundColumnPosition(2172.4, 2173, 0);
        if (Math.Abs(fx - 2170) > 0.1 || Math.Abs(fy - 2175) > 0.1)
        {
            message = $"round 5 mm: expected (2170,2175), got ({fx},{fy})";
            return false;
        }

        // From a grid intersection at (100.3, 50.7): offsets 2172.4 / 2173 → 2170 / 2175.
        var (gx, gy) = ModelRevitPlanner.RoundColumnPosition(100.3 + 2172.4, 50.7 + 2173, 0, 100.3, 50.7);
        if (Math.Abs(gx - 100.3 - 2170) > 0.01 || Math.Abs(gy - 50.7 - 2175) > 0.01)
        {
            message = $"round 5 mm from grid: expected offsets (2170,2175), got ({gx - 100.3},{gy - 50.7})";
            return false;
        }

        var rad = 30.0 * Math.PI / 180.0;
        var (ax, ay) = ModelRevitPlanner.RoundColumnPosition(2175 * Math.Cos(rad), 2175 * Math.Sin(rad), 30);
        if (Math.Abs(ax - 2175 * Math.Cos(rad)) > 0.1 || Math.Abs(ay - 2175 * Math.Sin(rad)) > 0.1)
        {
            message = $"round rotated position: expected 2175 along 30°, got ({ax},{ay})";
            return false;
        }

        // Pure math Round5 (same as UnitConv.Round5Mm) — CAD residue + float near-integers.
        foreach (var (input, expected) in new (double, double)[]
                 {
                     (219.76134733, 220),
                     (49.76144733, 50),
                     (0.23865267, 0),
                     (699.99999998, 700),
                     (249.99999999, 250),
                     (299.99999999, 300),
                     (100.00000010, 100),
                     (500.00727921, 500),
                     (499.98614503, 500)
                 })
        {
            var got = ModelRevitPlanner.Round5Mm(input);
            if (Math.Abs(got - expected) > 1e-9)
            {
                message = $"Round5Mm({input}): expected {expected}, got {got}";
                return false;
            }
        }

        if (!PilePlanCases(out message))
            return false;
        if (!FoundationNgàmAndPlanCases(out message))
            return false;

        message = "ok";
        return true;
    }

    /// <summary>
    /// Đỉnh cọc = đáy đài + ngàm; đài: Type Mark from CAD label (no WxH invent); type filter keywords.
    /// </summary>
    private static bool FoundationNgàmAndPlanCases(out string message)
    {
        var top = FoundationCatalogFilter.PileTopOffsetFromCapBottomMm(-800, 150);
        if (Math.Abs(top - (-650)) > 0.01)
        {
            message = $"ngàm: expected pile top offset -650 (= -800+150), got {top}";
            return false;
        }

        var place = new ModelRevitPlacement
        {
            BaseLevelName = "1F",
            BaseOffsetMm = -800,
            CapBottomOffsetMm = -800,
            EmbedMm = 150,
            LengthMm = 12000,
            TypeName = "S-FOUN-PHC-Cir : D400"
        };
        if (Math.Abs(place.EffectivePileTopOffsetMm() - (-650)) > 0.01)
        {
            message = $"ngàm placement: EffectivePileTopOffsetMm expected -650, got {place.EffectivePileTopOffsetMm()}";
            return false;
        }

        if (!FoundationCatalogFilter.MatchesPileSubtype("S-FOUN-PHC-Cir : D400", FoundationCatalogFilter.PileSubtypePressed))
        {
            message = "filter: PHC should match Pressed (ép)";
            return false;
        }

        if (!FoundationCatalogFilter.MatchesPileSubtype("Bored pile khoan nhoi : D800", FoundationCatalogFilter.PileSubtypeBored))
        {
            message = "filter: khoan nhoi should match Bored";
            return false;
        }

        if (!FoundationCatalogFilter.MatchesFoundationKind("S-FOUN-CAP-Rec : 2000x2000", FoundationCatalogFilter.KindPileCap)
            && !FoundationCatalogFilter.MatchesFoundationKind("Pile Cap 2000x2000", FoundationCatalogFilter.KindPileCap))
        {
            message = "filter: pile cap name should match KindPileCap";
            return false;
        }

        if (!CadMark.IsLabel("Đ4") || !CadMark.IsLabel("Đ2F")
            || !CadMark.LooksLikePileCapId("Đ4A") || !CadMark.LooksLikePileCapId("đ2F")
            || CadMark.LooksLikePileCapId("GMY23(50X100)") || CadMark.LooksLikePileCapId("B3"))
        {
            message = "type mark: CadMark must accept Đ* pile-cap ids and reject beam/grid labels";
            return false;
        }

        if (!CadMark.TryExtractPileCapId("Đ4A-1000x1000x1100mm", out var extractedName)
            || extractedName != "Đ4A"
            || FoundationCatalogFilter.TypeNameMatchesMark("Đ4A-1000x1000x1100mm", "Đ4")
            || !FoundationCatalogFilter.TypeNameMatchesMark("Đ4A-1000x1000x1100mm", "Đ4A"))
        {
            message = "type mark: type name prefix Đ4A-… must match Đ4A only (not Đ4)";
            return false;
        }

        if (!FoundationCatalogFilter.TypeMarkEquals("Đ4", "đ4")
            || FoundationCatalogFilter.TypeMarkEquals("Đ4", "D4")
            || !FoundationCatalogFilter.TryMatchTypeMark(" đ4 ", new[] { "Đ3", "Đ4", "Đ2F" }, out var matched)
            || matched != "Đ4")
        {
            message = "type mark: Normalize/match must be case-insensitive and keep Đ≠D";
            return false;
        }

        if (FoundationCatalogFilter.TryMatchTypeMark("Đ9", new[] { "Đ4", "Đ3" }, out _))
        {
            message = "type mark: miss must not match";
            return false;
        }

        var geom = new CadGeometry();
        // 3000×2500 pile cap outline + CAD label Đ4 near center (size from Type Mark, not outline).
        geom.Entities.Add(new CadPolyline
        {
            Layer = "DAI",
            Closed = true,
            Points =
            {
                new CadPoint2(100.3 + 0, 50.7 + 0),
                new CadPoint2(100.3 + 3000, 50.7 + 0),
                new CadPoint2(100.3 + 3000, 50.7 + 2500),
                new CadPoint2(100.3 + 0, 50.7 + 2500)
            }
        });
        geom.Entities.Add(new CadText
        {
            Layer = "TEXT",
            X = 100.3 + 1500,
            Y = 50.7 + 1250,
            Value = "Đ4"
        });
        var grids = new List<CadPoint2> { new(100.3, 50.7) };
        var ops = ModelRevitPlanner.Plan(
            geom,
            "DAI",
            ModelRevitPartKind.PileCap,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                BaseOffsetMm = -500,
                TypeName = "S-FOUN-CAP-Rec : 1500x1500",
                AssignMark = true,
                FoundationTypeMarks = new[] { "Đ4", "Đ3", "Đ2F", "Đ2D" }
            },
            gridPoints: grids);

        if (ops.Count != 1)
        {
            message = $"pilecap plan: expected 1 op, got {ops.Count}";
            return false;
        }

        var op = ops[0];
        if (!op.Fields.TryGetProperty("kind", out var kind) || kind.GetString() != FoundationCatalogFilter.KindPileCap)
        {
            message = $"pilecap plan: kind expected PileCap, got {kind}";
            return false;
        }

        if (op.Fields.TryGetProperty("widthMm", out _) || op.Fields.TryGetProperty("depthMm", out _))
        {
            message = "pilecap plan: must not invent widthMm/depthMm from CAD outline";
            return false;
        }

        if (!op.Fields.TryGetProperty("typeMark", out var tm) || tm.GetString() != "Đ4"
            || !op.Fields.TryGetProperty("resolveByTypeMark", out var rbt) || rbt.ValueKind != System.Text.Json.JsonValueKind.True)
        {
            message = $"pilecap plan: expected typeMark=Đ4 resolveByTypeMark=true, got tm={tm}";
            return false;
        }

        if (!op.Fields.TryGetProperty("mark", out var markEl) || markEl.GetString() != "Đ4")
        {
            message = $"pilecap plan: AssignMark should set instance mark Đ4, got {markEl}";
            return false;
        }

        if (!op.Fields.TryGetProperty("sourceFamily", out var sf) || sf.GetString() != "S-FOUN-CAP-Rec")
        {
            message = $"pilecap plan: sourceFamily expected S-FOUN-CAP-Rec, got {sf}";
            return false;
        }

        if (!op.Fields.TryGetProperty("exactMm", out var exact) || exact.ValueKind != System.Text.Json.JsonValueKind.True)
        {
            message = "pilecap plan: exactMm must be true";
            return false;
        }

        // Center of rect at (100.3+1500, 50.7+1250); round 5 mm from grid origin → xxx0/xxx5.
        var xyz = op.Fields.GetProperty("xyzMm").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var (rx, ry) = ModelRevitPlanner.RoundColumnPosition(100.3 + 1500, 50.7 + 1250, 0, 100.3, 50.7);
        if (Math.Abs(xyz[0] - rx) > 0.01 || Math.Abs(xyz[1] - ry) > 0.01)
        {
            message = $"pilecap plan: expected snapped ({rx},{ry}), got ({xyz[0]},{xyz[1]})";
            return false;
        }

        // Skip-on-miss: CAD label not in Type Mark catalog → 0 ops.
        var missGeom = new CadGeometry();
        missGeom.Entities.Add(new CadPolyline
        {
            Layer = "DAI",
            Closed = true,
            Points =
            {
                new CadPoint2(0, 0),
                new CadPoint2(2000, 0),
                new CadPoint2(2000, 2000),
                new CadPoint2(0, 2000)
            }
        });
        missGeom.Entities.Add(new CadText { Layer = "TEXT", X = 1000, Y = 1000, Value = "Đ9" });
        var missOps = ModelRevitPlanner.Plan(
            missGeom,
            "DAI",
            ModelRevitPartKind.PileCap,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                TypeName = "S-FOUN-CAP-Rec : 1500x1500",
                FoundationTypeMarks = new[] { "Đ4", "Đ3" }
            });
        if (missOps.Count != 0)
        {
            message = $"pilecap skip-on-miss: expected 0 ops for Đ9, got {missOps.Count}";
            return false;
        }

        // No CAD label → UI type fallback (still no WxH invent).
        var noLabel = new CadGeometry();
        noLabel.Entities.Add(new CadPolyline
        {
            Layer = "DAI",
            Closed = true,
            Points =
            {
                new CadPoint2(0, 0),
                new CadPoint2(2500, 0),
                new CadPoint2(2500, 2500),
                new CadPoint2(0, 2500)
            }
        });
        var fallbackOps = ModelRevitPlanner.Plan(
            noLabel,
            "DAI",
            ModelRevitPartKind.PileCap,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                TypeName = "S-FOUN-CAP-Rec : 1500x1500",
                FoundationTypeMarks = new[] { "Đ4" }
            });
        if (fallbackOps.Count != 1
            || !fallbackOps[0].Fields.TryGetProperty("familyType", out var ft)
            || ft.GetString() != "S-FOUN-CAP-Rec : 1500x1500"
            || fallbackOps[0].Fields.TryGetProperty("resolveByTypeMark", out _)
            || fallbackOps[0].Fields.TryGetProperty("widthMm", out _))
        {
            message = "pilecap no-label: expected UI type fallback without resolveByTypeMark/WxH";
            return false;
        }

        // Prefer Đ4A over nearer beam/grid text; match catalog token from type-name list.
        var clutter = new CadGeometry();
        clutter.Entities.Add(new CadPolyline
        {
            Layer = "DAI",
            Closed = true,
            Points =
            {
                new CadPoint2(0, 0),
                new CadPoint2(3000, 0),
                new CadPoint2(3000, 2500),
                new CadPoint2(0, 2500)
            }
        });
        clutter.Entities.Add(new CadText { Layer = "ANN", X = 1500, Y = 1250, Value = "GMY23(50X100)" });
        clutter.Entities.Add(new CadText { Layer = "ANN", X = 1600, Y = 1400, Value = "Đ4A" });
        clutter.Entities.Add(new CadText { Layer = "ANN", X = 1400, Y = 1200, Value = "B" });
        var clutterOps = ModelRevitPlanner.Plan(
            clutter,
            "DAI",
            ModelRevitPartKind.PileCap,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                TypeName = "S-FOUD-CON-REC : Đ1-1000x1000x1100mm",
                AssignMark = true,
                FoundationTypeMarks = new[] { "Đ1", "Đ4A", "Đ3" }
            });
        if (clutterOps.Count != 1
            || !clutterOps[0].Fields.TryGetProperty("typeMark", out var clutterTm)
            || clutterTm.GetString() != "Đ4A"
            || !clutterOps[0].Fields.TryGetProperty("mark", out var clutterMark)
            || clutterMark.GetString() != "Đ4A")
        {
            var tmShown = clutterOps.Count > 0 && clutterOps[0].Fields.TryGetProperty("typeMark", out var t) ? t.GetString() : "?";
            message = $"pilecap prefer Đ*: expected typeMark/mark Đ4A, got ops={clutterOps.Count} tm={tmShown}";
            return false;
        }

        // Beam-only nearby text → skip (no UI Đ1 fallback).
        var beamOnly = new CadGeometry();
        beamOnly.Entities.Add(new CadPolyline
        {
            Layer = "DAI",
            Closed = true,
            Points =
            {
                new CadPoint2(0, 0),
                new CadPoint2(2000, 0),
                new CadPoint2(2000, 2000),
                new CadPoint2(0, 2000)
            }
        });
        beamOnly.Entities.Add(new CadText { Layer = "ANN", X = 1000, Y = 1000, Value = "GMY10(50X100)" });
        var beamOnlyOps = ModelRevitPlanner.Plan(
            beamOnly,
            "DAI",
            ModelRevitPartKind.PileCap,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                TypeName = "S-FOUD-CON-REC : Đ1-1000x1000x1100mm",
                FoundationTypeMarks = new[] { "Đ1", "Đ4A" }
            });
        if (beamOnlyOps.Count != 0)
        {
            message = $"pilecap beam-only: expected 0 ops (no UI fallback), got {beamOnlyOps.Count}";
            return false;
        }

        // Horizontal vs vertical outline → CAD long-edge rotationDeg ~0 vs ~90; center = outline centroid.
        var horiz = SectionFootprintReader.FromEntitiesFoundation(new List<CadEntity>
        {
            new CadPolyline
            {
                Layer = "DAI", Closed = true,
                Points =
                {
                    new CadPoint2(0, 0), new CadPoint2(3000, 0),
                    new CadPoint2(3000, 1500), new CadPoint2(0, 1500)
                }
            }
        });
        var vert = SectionFootprintReader.FromEntitiesFoundation(new List<CadEntity>
        {
            new CadPolyline
            {
                Layer = "DAI", Closed = true,
                Points =
                {
                    new CadPoint2(0, 0), new CadPoint2(1500, 0),
                    new CadPoint2(1500, 3000), new CadPoint2(0, 3000)
                }
            }
        });
        if (horiz.Count != 1 || vert.Count != 1
            || Math.Abs(horiz[0].RotationDeg) > 1
            || Math.Abs(Math.Abs(vert[0].RotationDeg) - 90) > 1
            || Math.Abs(horiz[0].CenterX - 1500) > 1 || Math.Abs(horiz[0].CenterY - 750) > 1
            || Math.Abs(vert[0].CenterX - 750) > 1 || Math.Abs(vert[0].CenterY - 1500) > 1)
        {
            message =
                $"pilecap rotation/center: expected horiz~0@(1500,750) vert~±90@(750,1500), got " +
                $"h={horiz.FirstOrDefault().RotationDeg:0.#}@({horiz.FirstOrDefault().CenterX:0},{horiz.FirstOrDefault().CenterY:0}) " +
                $"v={vert.FirstOrDefault().RotationDeg:0.#}@({vert.FirstOrDefault().CenterX:0},{vert.FirstOrDefault().CenterY:0})";
            return false;
        }

        // Family long on +Y (h>b): Revit needs cadLong−90 so horizontal CAD places long side E-W.
        if (Math.Abs(ModelRevitPlanner.FoundationInstanceRotationDeg(0, 1000, 2500) - (-90)) > 0.01
            || Math.Abs(ModelRevitPlanner.FoundationInstanceRotationDeg(90, 1000, 2500)) > 0.01
            || Math.Abs(ModelRevitPlanner.FoundationInstanceRotationDeg(0, 2500, 1000)) > 0.01)
        {
            message =
                "pilecap RevitRot: expected horiz fam 1000x2500 → −90, vert → 0, fam 2500x1000 horiz → 0";
            return false;
        }

        // Clipped pentagon (Đ3): tip north → rotation ~0 (tip on +perp); center = polygon centroid (±1 mm).
        var pent = SectionFootprintReader.FromEntitiesFoundation(new List<CadEntity>
        {
            new CadPolyline
            {
                Layer = "DAI", Closed = true,
                Points =
                {
                    new CadPoint2(0, 0),
                    new CadPoint2(2500, 0),
                    new CadPoint2(2500, 2000),
                    new CadPoint2(1750, 2500),
                    new CadPoint2(750, 2500),
                    new CadPoint2(0, 2000)
                }
            }
        });
        if (pent.Count != 1)
        {
            message = $"pilecap clipped: expected 1 footprint, got {pent.Count}";
            return false;
        }

        // Shoelace centroid of the open ring (no duplicate close).
        double[] px = { 0, 2500, 2500, 1750, 750, 0 };
        double[] py = { 0, 0, 2000, 2500, 2500, 2000 };
        double a2 = 0, cxAcc = 0, cyAcc = 0;
        for (var i = 0; i < 6; i++)
        {
            var j = (i + 1) % 6;
            var cross = px[i] * py[j] - px[j] * py[i];
            a2 += cross;
            cxAcc += (px[i] + px[j]) * cross;
            cyAcc += (py[i] + py[j]) * cross;
        }

        var expectCx = cxAcc / (3.0 * a2);
        var expectCy = cyAcc / (3.0 * a2);
        if (Math.Abs(pent[0].CenterX - expectCx) > 1 || Math.Abs(pent[0].CenterY - expectCy) > 1)
        {
            message =
                $"pilecap clipped center: expected centroid ({expectCx:0.###},{expectCy:0.###}), got ({pent[0].CenterX:0.###},{pent[0].CenterY:0.###})";
            return false;
        }

        // Tip north → long-edge angle ~0 (not −180 from wrong short-edge / AABB tip).
        if (Math.Abs(pent[0].RotationDeg) > 5)
        {
            message = $"pilecap clipped rot: tip north must be ~0, got {pent[0].RotationDeg:0.#}";
            return false;
        }

        var rotGeom = new CadGeometry();
        rotGeom.Entities.Add(new CadPolyline
        {
            Layer = "DAI", Closed = true,
            Points =
            {
                new CadPoint2(0, 0), new CadPoint2(3000, 0),
                new CadPoint2(3000, 1500), new CadPoint2(0, 1500)
            }
        });
        rotGeom.Entities.Add(new CadText { Layer = "ANN", X = 1500, Y = 750, Value = "Đ2A" });
        var rotOps = ModelRevitPlanner.Plan(
            rotGeom,
            "DAI",
            ModelRevitPartKind.PileCap,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                TypeName = "S-FOUN-CAP-Rec : Đ2A-3000x1500",
                FoundationTypeMarks = new[] { "Đ2A" }
            });
        if (rotOps.Count != 1
            || !rotOps[0].Fields.TryGetProperty("rotationDeg", out var rotEl)
            || Math.Abs(rotEl.GetDouble()) > 1)
        {
            var rotShown = rotOps.Count > 0 && rotOps[0].Fields.TryGetProperty("rotationDeg", out var r)
                ? r.GetDouble().ToString("0.#")
                : "missing";
            message = $"pilecap plan rotation: expected rotationDeg~0 on horizontal outline, got {rotShown}";
            return false;
        }

        var planXyz = rotOps[0].Fields.GetProperty("xyzMm").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        if (Math.Abs(planXyz[0] - 1500) > 1 || Math.Abs(planXyz[1] - 750) > 1)
        {
            message = $"pilecap plan center: expected (1500,750) before snap, got ({planXyz[0]},{planXyz[1]})";
            return false;
        }

        // Pile with ngàm: offset must be đáy+ngàm.
        var pileGeom = new CadGeometry();
        pileGeom.Entities.Add(new CadCircle { Layer = "COC", Cx = 5000, Cy = 6000, Radius = 200 });
        var pileOps = ModelRevitPlanner.Plan(
            pileGeom,
            "COC",
            ModelRevitPartKind.Pile,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                BaseOffsetMm = -800,
                CapBottomOffsetMm = -800,
                EmbedMm = 150,
                TypeName = "S-FOUN-PHC-Cir : D400",
                LengthMm = 12000
            });
        if (pileOps.Count != 1)
        {
            message = $"pile ngàm plan: expected 1 op, got {pileOps.Count}";
            return false;
        }

        if (!pileOps[0].Fields.TryGetProperty("offsetMm", out var poff))
        {
            message = "pile ngàm plan: missing offsetMm";
            return false;
        }

        if (Math.Abs(poff.GetDouble() - (-650)) > 0.1)
        {
            message = $"pile ngàm plan: offsetMm expected -650, got {poff.GetDouble()}";
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>
    /// Piles: selected Level + lengthMm; XY exactMm rounded 5 mm from fractional grids (like columns);
    /// concentric CAD circles → max diameter type; nearby CadText → Mark.
    /// </summary>
    private static bool PilePlanCases(out string message)
    {
        // One pile near a fractional grid: 2172.4/2173 → 2170/2175 (exactMm, no world whole-mm).
        var geom = new CadGeometry();
        geom.Entities.Add(new CadCircle { Layer = "COC", Cx = 100.3 + 2172.4, Cy = 50.7 + 2173, Radius = 200 });
        var grids = new List<CadPoint2> { new(100.3, 50.7) };
        var ops = ModelRevitPlanner.Plan(
            geom,
            "COC",
            ModelRevitPartKind.Pile,
            "HẦM",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                BaseOffsetMm = -50,
                TypeName = "S-FOUN-PHC-Cir : D400",
                LengthMm = 12000
            },
            gridPoints: grids);

        if (ops.Count != 1)
        {
            message = $"pile plan: expected 1 op, got {ops.Count}";
            return false;
        }

        var op = ops[0];
        if (!string.Equals(op.Category, "StructuralFoundation", StringComparison.OrdinalIgnoreCase))
        {
            message = $"pile plan: expected StructuralFoundation, got {op.Category}";
            return false;
        }

        // Selected placement BaseLevel wins over Plan() baseLevelName arg ("HẦM").
        if (!op.Fields.TryGetProperty("level", out var lvl) || lvl.GetString() != "1F")
        {
            message = $"pile plan: level must be selected BaseLevel (1F), got {lvl}";
            return false;
        }

        if (!op.Fields.TryGetProperty("lengthMm", out var len) || Math.Abs(len.GetDouble() - 12000) > 0.1)
        {
            message = $"pile plan: lengthMm expected 12000, got {len}";
            return false;
        }

        if (!op.Fields.TryGetProperty("offsetMm", out var off) || Math.Abs(off.GetDouble() - (-50)) > 0.1)
        {
            message = $"pile plan: offsetMm expected -50, got {off}";
            return false;
        }

        if (!op.Fields.TryGetProperty("exactMm", out var exact) || exact.ValueKind != System.Text.Json.JsonValueKind.True)
        {
            message = "pile plan: exactMm must be true (no whole-mm world rounding vs fractional grids)";
            return false;
        }

        var xyz = op.Fields.GetProperty("xyzMm").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        if (Math.Abs(xyz[0] - (100.3 + 2170)) > 0.01 || Math.Abs(xyz[1] - (50.7 + 2175)) > 0.01)
        {
            message = $"pile plan: expected (2270.3,2225.7), got ({xyz[0]},{xyz[1]})";
            return false;
        }

        // CAD diameter 400 → familyType D400 (UI type leaf size not forced when footprint is clear).
        if (!op.Fields.TryGetProperty("familyType", out var ft) || ft.GetString() != "D400")
        {
            message = $"pile plan: familyType expected D400 from CAD R=200, got {ft}";
            return false;
        }

        if (!op.Fields.TryGetProperty("sourceFamily", out var sf) || sf.GetString() != "S-FOUN-PHC-Cir")
        {
            message = $"pile plan: sourceFamily expected S-FOUN-PHC-Cir, got {sf}";
            return false;
        }

        if (!op.Fields.TryGetProperty("diameterMm", out var dia) || Math.Abs(dia.GetDouble() - 400) > 0.1)
        {
            message = $"pile plan: diameterMm expected 400, got {dia}";
            return false;
        }

        if (!PileMaxDiameterClusterCases(out message))
            return false;
        if (!PileMarkParseCases(out message))
            return false;

        message = "ok";
        return true;
    }

    /// <summary>Concentric rings → one pile with the largest diameter; nested rects → max b×h.</summary>
    private static bool PileMaxDiameterClusterCases(out string message)
    {
        var concentric = new List<SectionFootprint>
        {
            new(1000, 2000, 300, 300),
            new(1005, 2002, 400, 400),
            new(998, 1997, 250, 250)
        };
        var clustered = ModelRevitPlanner.ClusterLargestPileSections(concentric);
        if (clustered.Count != 1)
        {
            message = $"pile cluster: expected 1 group for concentric, got {clustered.Count}";
            return false;
        }

        if (Math.Abs(clustered[0].WidthMm - 400) > 0.1 || Math.Abs(clustered[0].DepthMm - 400) > 0.1)
        {
            message = $"pile cluster: expected max D=400, got {clustered[0].WidthMm}x{clustered[0].DepthMm}";
            return false;
        }

        var geom = new CadGeometry();
        geom.Entities.Add(new CadCircle { Layer = "COC", Cx = 5000, Cy = 6000, Radius = 150 });
        geom.Entities.Add(new CadCircle { Layer = "COC", Cx = 5002, Cy = 5998, Radius = 200 });
        geom.Entities.Add(new CadCircle { Layer = "COC", Cx = 8000, Cy = 6000, Radius = 250 });
        var ops = ModelRevitPlanner.Plan(
            geom,
            "COC",
            ModelRevitPartKind.Pile,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                TypeName = "S-FOUN-PHC-Cir : D300",
                LengthMm = 10000,
                AssignMark = false
            });

        if (ops.Count != 2)
        {
            message = $"pile max-D plan: expected 2 piles, got {ops.Count}";
            return false;
        }

        var types = ops
            .Select(o => o.Fields.TryGetProperty("familyType", out var t) ? t.GetString() ?? "" : "")
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
        if (types[0] != "D400" || types[1] != "D500")
        {
            message = $"pile max-D plan: expected D400+D500, got {string.Join(",", types)}";
            return false;
        }

        // Square/rect nested outlines → keep largest b×h (not product of mixed edges).
        var nestedRect = new List<SectionFootprint>
        {
            new(0, 0, 300, 300),
            new(10, -5, 400, 500)
        };
        var rectCluster = ModelRevitPlanner.ClusterLargestPileSections(nestedRect);
        if (rectCluster.Count != 1 ||
            Math.Abs(rectCluster[0].WidthMm - 400) > 0.1 ||
            Math.Abs(rectCluster[0].DepthMm - 500) > 0.1)
        {
            message = rectCluster.Count != 1
                ? $"pile rect cluster: expected 1, got {rectCluster.Count}"
                : $"pile rect cluster: expected 400x500, got {rectCluster[0].WidthMm}x{rectCluster[0].DepthMm}";
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>CadText near pile (K1) → fields.mark when AssignMark.</summary>
    private static bool PileMarkParseCases(out string message)
    {
        var geom = new CadGeometry();
        geom.Entities.Add(new CadCircle { Layer = "COC", Cx = 1000, Cy = 2000, Radius = 200 });
        geom.Entities.Add(new CadText { Layer = "TEXT", X = 1050, Y = 2100, Value = "K1" });
        geom.Entities.Add(new CadText { Layer = "TEXT", X = 9000, Y = 9000, Value = "K99" });
        var ops = ModelRevitPlanner.Plan(
            geom,
            "COC",
            ModelRevitPartKind.Pile,
            "1F",
            "1F",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                BaseLevelName = "1F",
                TypeName = "S-FOUN-PHC-Cir : D400",
                LengthMm = 12000,
                AssignMark = true
            });

        if (ops.Count != 1)
        {
            message = $"pile mark: expected 1 op, got {ops.Count}";
            return false;
        }

        if (!ops[0].Fields.TryGetProperty("mark", out var mark) || mark.GetString() != "K1")
        {
            message = $"pile mark: expected K1, got {mark}";
            return false;
        }

        if (!CadMark.IsLabel("K1") || CadMark.IsLabel("400") || CadMark.IsSizeOnly("K1"))
        {
            message = "pile mark: CadMark.IsLabel(K1) must be true; pure numbers are not labels";
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

        // Letter O in mark (not digit 0): D1O1-200x500 → 200×500 mm as written.
        if (!BeamLabelParser.TryParse("D1O1-200x500", out var blO) ||
            blO.Mark != "D1O1" || blO.WidthMm != 200 || blO.DepthMm != 500)
        {
            message = $"beam label D1O1-200x500: got mark={blO.Mark} {blO.WidthMm}x{blO.DepthMm}";
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

        // Name (WxH) / Name(WxH) — space optional; Mark must never keep trailing '('.
        if (!BeamLabelParser.TryParse("D1Y-2 (60x70)", out var blParen) ||
            blParen.Mark != "D1Y-2" || blParen.WidthMm != 600 || blParen.DepthMm != 700)
        {
            message = $"beam label D1Y-2 (60x70): got mark={blParen.Mark} {blParen.WidthMm}x{blParen.DepthMm}";
            return false;
        }

        if (!BeamLabelParser.TryParse("GTC3(30x40)", out var blGtc) ||
            blGtc.Mark != "GTC3" || blGtc.WidthMm != 300 || blGtc.DepthMm != 400)
        {
            message = $"beam label GTC3(30x40): got mark={blGtc.Mark} {blGtc.WidthMm}x{blGtc.DepthMm}";
            return false;
        }

        if (!BeamLabelParser.TryParse("G1X-2 (50x70)", out var blG1x) ||
            blG1x.Mark != "G1X-2" || blG1x.WidthMm != 500 || blG1x.DepthMm != 700)
        {
            message = $"beam label G1X-2 (50x70): got mark={blG1x.Mark} {blG1x.WidthMm}x{blG1x.DepthMm}";
            return false;
        }

        if (BeamLabelParser.CleanMark("D1Y-2(") != "D1Y-2" ||
            BeamLabelParser.CleanMark("GTC3(") != "GTC3")
        {
            message = "beam CleanMark must strip trailing '('";
            return false;
        }

        // MLINE explode: two offsets ±200 → face pair thickness 400 after BeamCenterlineReader.
        var mlineFaces = MlineFaceReader.ToFaceLines(
            "BEAM",
            new[] { (0.0, 0.0), (5000.0, 0.0) },
            miters: null,
            elementOffsets: new[] { 0.5, -0.5 },
            scale: 400);
        if (mlineFaces.Count != 2)
        {
            message = $"mline faces: expected 2 lines, got {mlineFaces.Count}";
            return false;
        }

        var mlineRuns = BeamCenterlineReader.FromEntities(mlineFaces);
        if (mlineRuns.Count != 1 || Math.Abs(mlineRuns[0].ThicknessMm - 400) > 0.1)
        {
            message = $"mline centerline: expected 1×400, got {mlineRuns.Count} t={mlineRuns.FirstOrDefault().ThicknessMm}";
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
    /// Column-to-column (center); secondary at main centerlines; local text wins, then unlabeled
    /// collinear siblings inherit Mark-WxH from the nearest on-axis labeled piece.
    /// </summary>
    private static bool BeamPlanRulesCase(out string message)
    {
        if (!BeamSpanCases(out message))
            return false;

        // Text on span 1 only → unlabeled abutting span 2 inherits Mark-WxH (no width×2).
        var sharedGeom = BeamAxisGeom(
            new CadText { Layer = "ANN", Value = "D1Y10-40x60", X = 2000, Y = 500, RotationDeg = 0 });
        var sharedOps = ModelRevitPlanner.Plan(
            sharedGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        if (sharedOps.Count != 2)
        {
            message = $"beam per-segment mark plan: expected 2 ops, got {sharedOps.Count}";
            return false;
        }

        var sharedByX = sharedOps
            .Select(op => (
                X: op.Fields.GetProperty("startMm")[0].GetDouble(),
                Mark: op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "",
                W: op.Fields.TryGetProperty("widthMm", out var ww) ? ww.GetInt32() : 0,
                D: op.Fields.TryGetProperty("depthMm", out var dd) ? dd.GetInt32() : 0))
            .OrderBy(t => t.X)
            .ToList();
        if (!string.Equals(sharedByX[0].Mark, "D1Y10", StringComparison.OrdinalIgnoreCase) ||
            sharedByX[0].W != 400 || sharedByX[0].D != 600)
        {
            message = $"beam per-segment: span1 expected D1Y10 400x600, got {sharedByX[0].Mark} {sharedByX[0].W}x{sharedByX[0].D}";
            return false;
        }

        if (!string.Equals(sharedByX[1].Mark, "D1Y10", StringComparison.OrdinalIgnoreCase) ||
            sharedByX[1].W != 400 || sharedByX[1].D != 600)
        {
            message = $"beam inherit: span2 expected D1Y10 400x600, got {sharedByX[1].Mark} {sharedByX[1].W}x{sharedByX[1].D}";
            return false;
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

        // Neighbor 100x65 must NOT win over local D1Y34-40x60 on this segment (Hình 2).
        var localBeatGeom = BeamAxisGeom(
            new CadText { Layer = "ANN", Value = "D1Y34-40x60", X = 2000, Y = 200, CenterX = 2000, CenterY = 200 },
            new CadText { Layer = "ANN", Value = "D1Y30-50x60", X = 2500, Y = 800, CenterX = 5500, CenterY = 200 },
            new CadText { Layer = "ANN", Value = "D1Y29-100x65", X = 6000, Y = 900, CenterX = 6000, CenterY = 900 });
        var localBeatOps = ModelRevitPlanner.Plan(
            localBeatGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var span1 = localBeatOps
            .Select(op => (
                X: op.Fields.GetProperty("startMm")[0].GetDouble(),
                Mark: op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "",
                Type: op.Fields.TryGetProperty("familyType", out var ft) ? ft.GetString() ?? "" : ""))
            .OrderBy(t => t.X)
            .FirstOrDefault();
        if (!string.Equals(span1.Mark, "D1Y34", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(span1.Type, "400x600", StringComparison.OrdinalIgnoreCase))
        {
            message = $"beam local vs neighbor: expected D1Y34 400x600, got mark={span1.Mark} type={span1.Type}";
            return false;
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

        // Large label below a 400 beam: middle 656 mm off the axis (256 mm gap below the face) — Hình 3.
        var belowOps = ModelRevitPlanner.Plan(
            BeamAxisGeom(new CadText { Layer = "ANN", Value = "D1X9-40X60", X = 400, Y = -656, CenterX = 2000, CenterY = -456 }),
            "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var below = FirstSpan(belowOps);
        if (below.Mark != "D1X9" || below.Type != "400x600")
        {
            message = $"beam label below: expected D1X9 400x600, got mark={below.Mark} type={below.Type}";
            return false;
        }

        // D1X10-100x60 above a 600 pair (676 mm off the axis); perpendicular 100x75 beside it is ignored — Hình 4.
        var wideGeom = new CadGeometry();
        wideGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 6000, Y1 = 0 });
        wideGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 600, X1 = 6000, Y1 = 600 });
        wideGeom.Entities.Add(ColRect(-300, 0, 300, 600));
        wideGeom.Entities.Add(ColRect(5700, 0, 6300, 600));
        wideGeom.Entities.Add(new CadText { Layer = "ANN", Value = "D1X10-100X60", X = 1100, Y = 776, CenterX = 3000, CenterY = 976 });
        wideGeom.Entities.Add(new CadText { Layer = "ANN", Value = "100X75", X = 900, Y = 1200, RotationDeg = 90, CenterX = 700, CenterY = 1900 });
        wideGeom.Entities.Add(new CadText { Layer = "ANN", Value = "100X75", X = 5300, Y = 1200, RotationDeg = 90, CenterX = 5100, CenterY = 1900 });
        var wide = FirstSpan(ModelRevitPlanner.Plan(
            wideGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" }));
        if (wide.Mark != "D1X10" || wide.Type != "1000x600")
        {
            message = $"beam label above: expected D1X10 1000x600, got mark={wide.Mark} type={wide.Type}";
            return false;
        }

        // A text between two parallel beams labels only the nearer axis (collinear spans on that
        // axis may share Mark via inheritance; the far parallel axis must stay unmarked).
        var pairGeom = BeamAxisGeom(new CadText { Layer = "ANN", Value = "D1X5-30X60", X = 1000, Y = 700, CenterX = 2000, CenterY = 700 });
        pairGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 1200, X1 = 8000, Y1 = 1200 });
        pairGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 1600, X1 = 8000, Y1 = 1600 });
        var pairOps = ModelRevitPlanner.Plan(
            pairGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var pairFarMarks = pairOps.Count(op =>
        {
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            if (mark != "D1X5")
                return false;
            var sy = op.Fields.GetProperty("startMm")[1].GetDouble();
            var ey = op.Fields.GetProperty("endMm")[1].GetDouble();
            return Math.Abs((sy + ey) / 2 - 1400) < 100;
        });
        var pairNearMarks = pairOps.Count(op =>
        {
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            return mark == "D1X5";
        });
        if (pairFarMarks != 0 || pairNearMarks < 1)
        {
            message = $"beam label owner: expected D1X5 on near axis only, far={pairFarMarks} total={pairNearMarks}";
            return false;
        }

        if (!BeamDedupeAndCompetingTextCases(out message))
            return false;

        if (!BeamAxisMarkAndMergeCases(out message))
            return false;

        message = "ok";
        return true;
    }

    /// <summary>
    /// H1: Mark D1X14 on one bay → other same-axis unlabeled bays inherit Mark (size-only stays size-only source).
    /// H2: two collinear same-section pieces with a gap and no real support → one beam; column between → stay two.
    /// </summary>
    private static bool BeamAxisMarkAndMergeCases(out string message)
    {
        // H1 — three column bays; Mark-WxH only on bay 1; bay 2/3 have size-only → all get Mark D1X14.
        var markGeom = new CadGeometry();
        markGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 12000, Y1 = 0 });
        markGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 400, X1 = 12000, Y1 = 400 });
        markGeom.Entities.Add(ColRect(-200, 0, 200, 400));
        markGeom.Entities.Add(ColRect(3800, 0, 4200, 400));
        markGeom.Entities.Add(ColRect(7800, 0, 8200, 400));
        markGeom.Entities.Add(ColRect(11800, 0, 12200, 400));
        markGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1X14-40x60", X = 2000, Y = 200, CenterX = 2000, CenterY = 200
        });
        markGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "40x60", X = 6000, Y = 200, CenterX = 6000, CenterY = 200
        });
        markGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "40x60", X = 10000, Y = 200, CenterX = 10000, CenterY = 200
        });
        var markOps = ModelRevitPlanner.Plan(
            markGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var markSpans = markOps
            .Select(op => (
                X: op.Fields.GetProperty("startMm")[0].GetDouble(),
                Mark: op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "",
                Type: op.Fields.TryGetProperty("familyType", out var ft) ? ft.GetString() ?? "" : ""))
            .OrderBy(t => t.X)
            .ToList();
        if (markSpans.Count != 3 ||
            markSpans.Any(t => !string.Equals(t.Mark, "D1X14", StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(t.Type, "400x600", StringComparison.OrdinalIgnoreCase)))
        {
            message = "beam axis-mark H1: expected 3× D1X14 400x600, got " +
                      string.Join(",", markSpans.Select(t => $"{t.Mark}:{t.Type}"));
            return false;
        }

        // H2 — two collinear 400x600 CAD pieces with a gap, columns only at outer ends → one beam.
        var mergeGeom = new CadGeometry();
        mergeGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 200, Y0 = 0, X1 = 3500, Y1 = 0 });
        mergeGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 200, Y0 = 400, X1 = 3500, Y1 = 400 });
        mergeGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 4500, Y0 = 0, X1 = 7800, Y1 = 0 });
        mergeGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 4500, Y0 = 400, X1 = 7800, Y1 = 400 });
        mergeGeom.Entities.Add(ColRect(-200, 0, 200, 400));
        mergeGeom.Entities.Add(ColRect(7800, 0, 8200, 400));
        mergeGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1X9-40x60", X = 1800, Y = 200, CenterX = 1800, CenterY = 200
        });
        mergeGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "40x60", X = 6000, Y = 200, CenterX = 6000, CenterY = 200
        });
        var mergeOps = ModelRevitPlanner.Plan(
            mergeGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var mergeHorizontal = mergeOps
            .Where(op =>
            {
                var s = op.Fields.GetProperty("startMm");
                var e = op.Fields.GetProperty("endMm");
                return Math.Abs(s[1].GetDouble() - e[1].GetDouble()) < 50;
            })
            .ToList();
        if (mergeHorizontal.Count != 1)
        {
            message = $"beam merge H2 no-support: expected 1 horizontal op, got {mergeHorizontal.Count}";
            return false;
        }

        var mh = mergeHorizontal[0];
        var mhMark = mh.Fields.TryGetProperty("mark", out var mm) ? mm.GetString() ?? "" : "";
        var mhType = mh.Fields.TryGetProperty("familyType", out var mt) ? mt.GetString() ?? "" : "";
        var mhLen = Math.Abs(
            mh.Fields.GetProperty("endMm")[0].GetDouble() - mh.Fields.GetProperty("startMm")[0].GetDouble());
        if (!string.Equals(mhMark, "D1X9", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mhType, "400x600", StringComparison.OrdinalIgnoreCase) ||
            mhLen < 7000)
        {
            message = $"beam merge H2 no-support: expected D1X9 400x600 L≥7000, got mark={mhMark} type={mhType} L={mhLen:0}";
            return false;
        }

        // H2 / D1Y7 — false mid hatch splits one bay; a parallel bay continues through → merge fragments.
        var falseSplitGeom = new CadGeometry();
        falseSplitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = -200, Y0 = 0, X1 = -200, Y1 = 12000 });
        falseSplitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 200, Y0 = 0, X1 = 200, Y1 = 12000 });
        falseSplitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 2800, Y0 = 0, X1 = 2800, Y1 = 12000 });
        falseSplitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 3200, Y0 = 0, X1 = 3200, Y1 = 12000 });
        falseSplitGeom.Entities.Add(ColRect(-300, -200, 300, 200));
        falseSplitGeom.Entities.Add(ColRect(-300, 11800, 300, 12200));
        falseSplitGeom.Entities.Add(ColRect(2700, -200, 3300, 200));
        falseSplitGeom.Entities.Add(ColRect(2700, 11800, 3300, 12200));
        // Lone hatch on the first bay only (parallel bay has no column here).
        falseSplitGeom.Entities.Add(ColRect(-250, 1900, 250, 2300));
        falseSplitGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1Y7-100x65", X = 50, Y = 6000, RotationDeg = 90,
            CenterX = 50, CenterY = 6000
        });
        falseSplitGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "40x60", X = 3050, Y = 6000, RotationDeg = 90,
            CenterX = 3050, CenterY = 6000
        });
        var falseOps = ModelRevitPlanner.Plan(
            falseSplitGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var falseD1Y7 = falseOps
            .Where(op =>
            {
                var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
                return string.Equals(mark, "D1Y7", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (falseD1Y7.Count != 1)
        {
            message = $"beam merge H2 false-split: expected 1× D1Y7, got {falseD1Y7.Count}";
            return false;
        }

        var f0 = falseD1Y7[0].Fields.GetProperty("startMm");
        var f1 = falseD1Y7[0].Fields.GetProperty("endMm");
        var fLen = Math.Sqrt(
            Math.Pow(f1[0].GetDouble() - f0[0].GetDouble(), 2) +
            Math.Pow(f1[1].GetDouble() - f0[1].GetDouble(), 2));
        if (fLen < 10000)
        {
            message = $"beam merge H2 false-split: expected D1Y7 L≥10000 (not a 2100 fragment), got L={fLen:0}";
            return false;
        }

        // H2 — same pieces but a real middle column that also terminates a perpendicular beam → stay two.
        var splitGeom = new CadGeometry();
        splitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 200, Y0 = 0, X1 = 3800, Y1 = 0 });
        splitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 200, Y0 = 400, X1 = 3800, Y1 = 400 });
        splitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 4200, Y0 = 0, X1 = 7800, Y1 = 0 });
        splitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 4200, Y0 = 400, X1 = 7800, Y1 = 400 });
        splitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 3800, Y0 = 200, X1 = 3800, Y1 = 5000 });
        splitGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 4200, Y0 = 200, X1 = 4200, Y1 = 5000 });
        splitGeom.Entities.Add(ColRect(-200, 0, 200, 400));
        splitGeom.Entities.Add(ColRect(3800, 0, 4200, 400));
        splitGeom.Entities.Add(ColRect(7800, 0, 8200, 400));
        splitGeom.Entities.Add(ColRect(3800, 4800, 4200, 5200));
        splitGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "40x60", X = 2000, Y = 200, CenterX = 2000, CenterY = 200
        });
        splitGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "40x60", X = 6000, Y = 200, CenterX = 6000, CenterY = 200
        });
        var splitOps = ModelRevitPlanner.Plan(
            splitGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Supports = new[]
                {
                    new SectionFootprint(0, 200, 400, 400),
                    new SectionFootprint(4000, 200, 400, 400),
                    new SectionFootprint(8000, 200, 400, 400),
                    new SectionFootprint(4000, 5000, 400, 400)
                }
            });
        var splitHorizontal = splitOps
            .Where(op =>
            {
                var s = op.Fields.GetProperty("startMm");
                var e = op.Fields.GetProperty("endMm");
                return Math.Abs(s[1].GetDouble() - e[1].GetDouble()) < 50 &&
                       string.Equals(
                           op.Fields.TryGetProperty("familyType", out var ft) ? ft.GetString() : "",
                           "400x600",
                           StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (splitHorizontal.Count < 2)
        {
            message = $"beam merge H2 with-column: expected ≥2 horizontal 400x600 ops, got {splitHorizontal.Count}";
            return false;
        }

        // Grids on fractional world mm (x=1000.23855267, y=500.11578539): beam ops stay exact (exactMm) and
        // centerline + faces of a 400 beam land on 5 mm from both grids. World whole-mm rounding broke this.
        const double gx = 1000.23855267, gy = 500.11578539;
        var fracGeom = new CadGeometry();
        fracGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = gx, Y0 = gy + 2000.4 - 200, X1 = gx + 8000, Y1 = gy + 2000.4 - 200 });
        fracGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = gx, Y0 = gy + 2000.4 + 200, X1 = gx + 8000, Y1 = gy + 2000.4 + 200 });
        fracGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1X3-40x60", X = gx + 4000, Y = gy + 2000, CenterX = gx + 4000, CenterY = gy + 2000
        });
        var fracOps = ModelRevitPlanner.Plan(
            fracGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Supports = new[] { new SectionFootprint(gx, gy + 2000, 400, 400), new SectionFootprint(gx + 8000, gy + 2000, 400, 400) }
            },
            gridPoints: new[] { new CadPoint2(gx, gy), new CadPoint2(gx + 8000, gy), new CadPoint2(gx, gy + 6000) },
            gridAnglesDeg: new[] { 0.0, 90.0 });
        static bool On5(double v) => Math.Abs(v / 5 - Math.Round(v / 5)) * 5 < 1e-6;
        if (fracOps.Count != 1 ||
            !fracOps[0].Fields.TryGetProperty("exactMm", out var exact) || exact.ValueKind != System.Text.Json.JsonValueKind.True)
        {
            message = $"beam exactMm: expected 1 op with exactMm=true, got {fracOps.Count}";
            return false;
        }

        var fs = fracOps[0].Fields.GetProperty("startMm");
        var fe = fracOps[0].Fields.GetProperty("endMm");
        var across = fs[1].GetDouble() - gy;
        if (Math.Abs(fs[1].GetDouble() - fe[1].GetDouble()) > 1e-9 || !On5(across) || !On5(across - 200) ||
            !On5(fs[0].GetDouble() - gx) || !On5(fe[0].GetDouble() - gx))
        {
            message = $"beam fractional grid: across={across:0.########} ends=({fs[0].GetDouble() - gx:0.########},{fe[0].GetDouble() - gx:0.########})";
            return false;
        }

        // Odd half width (b=225): the face nearer the grid goes on 5 mm, not the centerline.
        var oddAcross = ModelRevitPlanner.RoundAcross(301.3, 112.5);
        if (!On5(oddAcross - 112.5) || Math.Abs(oddAcross - 301.3) > 2.5 || !On5(ModelRevitPlanner.RoundAcross(301.3, 200)))
        {
            message = $"beam RoundAcross odd half: got {oddAcross:0.###}";
            return false;
        }

        // Edge beam flush with column outer face: 500×500 columns, face y=-250. (a) CAD outer line 3 mm off
        // (y=-247, t=300) → outer face exactly -250. (b) CAD 250 pair flush but type 300 → grows inward, center -100.
        foreach (var (outer, inner, label) in new[] { (-247.0, 53.0, "D1X5-30x60"), (-250.0, 0.0, "D1X5-30x60") })
        {
            var edgeGeom = new CadGeometry();
            edgeGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = outer, X1 = 8000, Y1 = outer });
            edgeGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = inner, X1 = 8000, Y1 = inner });
            edgeGeom.Entities.Add(new CadText
            {
                Layer = "ANN", Value = label, X = 4000, Y = (outer + inner) / 2, CenterX = 4000, CenterY = (outer + inner) / 2
            });
            var edgeOps = ModelRevitPlanner.Plan(
                edgeGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
                new AnnotationSize(null, null, null, ""),
                placement: new ModelRevitPlacement
                {
                    FamilyName = "S-BEAM",
                    Supports = new[] { new SectionFootprint(0, 0, 500, 500), new SectionFootprint(8000, 0, 500, 500) }
                });
            var ey0 = edgeOps.Count == 1 ? edgeOps[0].Fields.GetProperty("startMm")[1].GetDouble() : double.NaN;
            var ey1 = edgeOps.Count == 1 ? edgeOps[0].Fields.GetProperty("endMm")[1].GetDouble() : double.NaN;
            if (Math.Abs(ey0 + 100) > 1e-6 || Math.Abs(ey1 + 100) > 1e-6)
            {
                message = $"beam flush to column face (CAD {outer}/{inner}): expected center y=-100, got {edgeOps.Count} ops y=({ey0:0.###},{ey1:0.###})";
                return false;
            }
        }

        // Flush is edge-only: perimeter beams (CAD face 3 mm off column) still flush; interior beams whose CAD
        // face is 3 mm off a column face stay on CAD centerline (5 mm round) — no ±50 type-width shift.
        {
            var mixGeom = new CadGeometry();
            void PairH(double y0, double y1, string mark)
            {
                mixGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = y0, X1 = 8000, Y1 = y0 });
                mixGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = y1, X1 = 8000, Y1 = y1 });
                mixGeom.Entities.Add(new CadText
                {
                    Layer = "ANN", Value = mark, X = 4000, Y = (y0 + y1) / 2, CenterX = 4000, CenterY = (y0 + y1) / 2
                });
            }
            void PairV(double x0, double x1, string mark)
            {
                mixGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = x0, Y0 = 0, X1 = x0, Y1 = 6000 });
                mixGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = x1, Y0 = 0, X1 = x1, Y1 = 6000 });
                mixGeom.Entities.Add(new CadText
                {
                    Layer = "ANN", Value = mark, X = (x0 + x1) / 2, Y = 3000, CenterX = (x0 + x1) / 2, CenterY = 3000
                });
            }
            // Bottom edge: outer face -247 (3 mm off column face -250), type 300 → flush to center -100.
            PairH(-247, 53, "D1X21-30x140");
            // Top edge (envelope).
            PairH(5750, 6250, "D1X5-30x60");
            // Side edges (envelope).
            PairV(-250, 250, "D1Y1-30x60");
            PairV(7750, 8250, "D1Y2-30x60");
            // Interior on mid columns: CAD face 2753 = column face 2750 + 3 mm, t=500, type 400 → must NOT flush.
            PairH(2753, 3253, "D1X14-40x60");
            var mixLogs = new List<string>();
            var mixOps = ModelRevitPlanner.Plan(
                mixGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
                new AnnotationSize(null, null, null, ""),
                placement: new ModelRevitPlacement
                {
                    FamilyName = "S-BEAM",
                    Log = mixLogs.Add,
                    Supports = new[]
                    {
                        new SectionFootprint(0, 0, 500, 500), new SectionFootprint(8000, 0, 500, 500),
                        new SectionFootprint(0, 3000, 500, 500), new SectionFootprint(8000, 3000, 500, 500),
                        new SectionFootprint(0, 6000, 500, 500), new SectionFootprint(8000, 6000, 500, 500)
                    }
                });
            double? YOf(string markSub)
            {
                foreach (var op in mixOps)
                {
                    var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
                    if (mark.IndexOf(markSub, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    return op.Fields.GetProperty("startMm")[1].GetDouble();
                }
                return null;
            }
            var edgeY = YOf("D1X21");
            var intY = YOf("D1X14");
            var cadInteriorCenter = (2753.0 + 3253.0) / 2;
            var flushedInterior = 2750 + 200; // would-be center if flush applied (face 2750, b=400)
            if (edgeY is not double ey || Math.Abs(ey + 100) > 1e-6)
            {
                message = $"edge flush (D1X21 3 mm off): expected y=-100, got {edgeY?.ToString("0.###") ?? "missing"} ops={mixOps.Count}";
                return false;
            }
            if (intY is not double iy || Math.Abs(iy - flushedInterior) < 1 ||
                Math.Abs(iy - cadInteriorCenter) > 2.5)
            {
                message = $"interior no-flush (CAD face 3 mm off): expected ~{cadInteriorCenter:0.###} (not {flushedInterior}), got {intY?.ToString("0.###") ?? "missing"}";
                return false;
            }
            var flushLogs = mixLogs.Where(l => l.StartsWith("beam flush ", StringComparison.Ordinal)).ToList();
            if (!flushLogs.Any(l => l.Contains("edge=true", StringComparison.Ordinal)))
            {
                message = "edge flush log missing edge=true; logs=" + string.Join(" | ", flushLogs);
                return false;
            }
            if (!flushLogs.Any(l => l.Contains("edge=false skipped", StringComparison.Ordinal)))
            {
                message = "interior flush skip log missing edge=false; logs=" + string.Join(" | ", flushLogs);
                return false;
            }
        }

        // Dedupe keeps own text (log: `dedupe drop seg 153 keep 172 lat=25 a=D1X22-25X140|300 b=D1X21-30X140|250`):
        // bay 2 has a 250 pair (center -125, flush with the column face -250) that inherited D1X21-30x140 from
        // bay 1, and an overlapping 300 pair (center -100) carrying its own D1X22-25x140. Survivor must be the
        // 250 pair with D1X22 250x1400, placed flush (center -125); bay 1 stays D1X21 300.
        var dupPlanned = new List<BeamSegment>
        {
            new(new WallRun(0, -100, 8000, -100, 300), BeamSpanKind.Main),
            new(new WallRun(8000, -125, 16000, -125, 250), BeamSpanKind.Main),
            new(new WallRun(8000, -100, 16000, -100, 300), BeamSpanKind.Main),
            // Mark-only text (D5B box beside a beam) on a dropped pair must not rename an own-size beam.
            new(new WallRun(0, 5000, 8000, 5000, 300), BeamSpanKind.Main),
            new(new WallRun(0, 5150, 8000, 5150, 300), BeamSpanKind.Secondary)
        };
        var dupLabels = new[]
        {
            new BeamLabel("D1X21", 300, 1400, "D1X21-30X140"),
            new BeamLabel("D1X21", 300, 1400, "D1X21-30X140"),
            new BeamLabel("D1X22", 250, 1400, "D1X22-25X140"),
            new BeamLabel("D5PX4", 300, 600, "30X60+D5PX4"),
            new BeamLabel("D5B", null, null, "D5B")
        };
        var dupSources = new[]
        {
            "cands=1 combined=D1X21-30X140@0", "inherit=D1X21-30X140@seg0", "cands=1 combined=D1X22-25X140@25",
            "axisMark=D5PX4@seg9", "cands=1 markText=D5B@100"
        };
        var dupKeep = new[] { true, true, true, true, true };
        ModelRevitPlanner.DeduplicateOverlappingBeams(dupPlanned, dupLabels, dupSources, dupKeep, null);
        var flushShift = ModelRevitPlanner.FlushToSupportFace(
            dupPlanned[1].Run, 8000, -125, 16000, -125, dupLabels[1].WidthMm ?? 0,
            new[] { new SectionFootprint(8000, 0, 500, 500), new SectionFootprint(16000, 0, 500, 500) });
        if (!dupKeep[0] || !dupKeep[1] || dupKeep[2] || !dupKeep[3] || dupKeep[4] || dupLabels[3].Mark != "D5PX4" ||
            dupLabels[0].Mark != "D1X21" || dupLabels[0].WidthMm != 300 ||
            dupLabels[1].Mark != "D1X22" || dupLabels[1].WidthMm != 250 || dupLabels[1].DepthMm != 1400 ||
            flushShift is not null)
        {
            message = $"beam dedupe own text: keep=({dupKeep[0]},{dupKeep[1]},{dupKeep[2]}) " +
                      $"bay1={dupLabels[0].Mark}/{dupLabels[0].WidthMm} bay2={dupLabels[1].Mark}/{dupLabels[1].WidthMm}x{dupLabels[1].DepthMm} " +
                      $"flushShift={flushShift?.ToString("0.###") ?? "none (already flush)"}";
            return false;
        }

        // H1 + merge — secondary D1PY15 crosses the middle main; Mark/size inherited on the upper piece,
        // but it must stay broken at the main centerline (y=6000), not re-merge into one 12 m beam.
        var secGeom = new CadGeometry();
        foreach (var y in new[] { 0.0, 6000, 12000 })
        {
            secGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = y - 200, X1 = 8000, Y1 = y - 200 });
            secGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = y + 200, X1 = 8000, Y1 = y + 200 });
            secGeom.Entities.Add(new CadText
            {
                Layer = "ANN", Value = "D1X2-40x60", X = 2000, Y = y, CenterX = 2000, CenterY = y
            });
        }

        secGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 3850, Y0 = 0, X1 = 3850, Y1 = 12000 });
        secGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 4150, Y0 = 0, X1 = 4150, Y1 = 12000 });
        secGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1PY15-30x60", X = 4000, Y = 3000, RotationDeg = 90,
            CenterX = 4000, CenterY = 3000
        });
        var secOps = ModelRevitPlanner.Plan(
            secGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Supports = new[] { 0.0, 6000, 12000 }
                    .SelectMany(y => new[]
                    {
                        new SectionFootprint(0, y, 400, 400), new SectionFootprint(8000, y, 400, 400)
                    })
                    .ToArray()
            });
        var secVertical = secOps
            .Where(op => Math.Abs(op.Fields.GetProperty("startMm")[0].GetDouble() -
                                  op.Fields.GetProperty("endMm")[0].GetDouble()) < 50)
            .Select(op => (
                Y0: Math.Min(op.Fields.GetProperty("startMm")[1].GetDouble(), op.Fields.GetProperty("endMm")[1].GetDouble()),
                Y1: Math.Max(op.Fields.GetProperty("startMm")[1].GetDouble(), op.Fields.GetProperty("endMm")[1].GetDouble()),
                Mark: op.Fields.TryGetProperty("mark", out var sm) ? sm.GetString() ?? "" : ""))
            .OrderBy(t => t.Y0)
            .ToList();
        if (secVertical.Count != 2 ||
            secVertical.Any(t => !string.Equals(t.Mark, "D1PY15", StringComparison.OrdinalIgnoreCase)) ||
            Math.Abs(secVertical[0].Y1 - 6000) > 1 || Math.Abs(secVertical[1].Y0 - 6000) > 1)
        {
            message = "beam secondary keeps break at main with inherited Mark: expected 2× D1PY15 split at y=6000, got " +
                      string.Join(",", secVertical.Select(t => $"{t.Mark}:{t.Y0:0}-{t.Y1:0}"));
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>
    /// Overlapping parallel face-pairs (500 vs 1000, 250 mm apart) + competing 50x65 / D1Y7-100x65
    /// → one beam Mark-WxH; ultra-short unlabeled stubs suppressed; near-duplicate midlines merge.
    /// </summary>
    private static bool BeamDedupeAndCompetingTextCases(out string message)
    {
        // Double outline of one bay: 500-wide pair at X=0 and 1000-wide at X=250 (shared face),
        // texts 50x65 + D1Y7-100x65 → keep a single Mark-WxH beam (not 1000x2000 + 1000x650).
        var overlapGeom = new CadGeometry();
        overlapGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = -250, Y0 = 0, X1 = -250, Y1 = 8000 });
        overlapGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 250, Y0 = 0, X1 = 250, Y1 = 8000 });
        overlapGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = -500, Y0 = 0, X1 = -500, Y1 = 8000 });
        overlapGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 500, Y0 = 0, X1 = 500, Y1 = 8000 });
        overlapGeom.Entities.Add(ColRect(-400, -200, 400, 200));
        overlapGeom.Entities.Add(ColRect(-400, 7800, 400, 8200));
        overlapGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "50x65", X = 0, Y = 3000, RotationDeg = 90, CenterX = 0, CenterY = 3000
        });
        overlapGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1Y7-100x65", X = 50, Y = 5000, RotationDeg = 90, CenterX = 50, CenterY = 5000
        });
        var overlapOps = ModelRevitPlanner.Plan(
            overlapGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var vertical = overlapOps
            .Where(op =>
            {
                var sx = op.Fields.GetProperty("startMm")[0].GetDouble();
                var ex = op.Fields.GetProperty("endMm")[0].GetDouble();
                return Math.Abs(sx - ex) < 50;
            })
            .ToList();
        if (vertical.Count != 1)
        {
            message = $"beam overlapping bay dedupe: expected 1 vertical op, got {vertical.Count}";
            return false;
        }

        var vo = vertical[0];
        var vw = vo.Fields.TryGetProperty("widthMm", out var vww) ? vww.GetInt32() : 0;
        var vd = vo.Fields.TryGetProperty("depthMm", out var vdd) ? vdd.GetInt32() : 0;
        var vm = vo.Fields.TryGetProperty("mark", out var vmm) ? vmm.GetString() ?? "" : "";
        if (!string.Equals(vm, "D1Y7", StringComparison.OrdinalIgnoreCase) || vw != 1000 || vd != 650)
        {
            message = $"beam overlapping bay: expected D1Y7 1000x650, got mark={vm} {vw}x{vd}";
            return false;
        }

        // Near-duplicate midlines (15 mm apart, same thickness) merge before split.
        var nearDup = BeamCenterlineReader.MergeNearDuplicateCenterlines(new[]
        {
            new WallRun(0, 0, 6000, 0, 400),
            new WallRun(0, 15, 6000, 15, 400)
        });
        if (nearDup.Count != 1 || Math.Abs(nearDup[0].Y0 - 7.5) > 1 || Math.Abs(nearDup[0].ThicknessMm - 400) > 0.1)
        {
            message = $"beam near-dup midlines: expected 1 run ~Y=7.5 t=400, got {nearDup.Count}";
            return false;
        }

        // Ultra-short unlabeled stub between two parallel axes is suppressed; size-labelled short kept.
        var stubGeom = new CadGeometry();
        stubGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 0, Y1 = 4000 });
        stubGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 400, Y0 = 0, X1 = 400, Y1 = 4000 });
        stubGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 2000, X1 = 250, Y1 = 2000 });
        stubGeom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 2300, X1 = 250, Y1 = 2300 });
        stubGeom.Entities.Add(ColRect(-200, -200, 600, 200));
        stubGeom.Entities.Add(ColRect(-200, 3800, 600, 4200));
        stubGeom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "40x60", X = 500, Y = 2000, RotationDeg = 90, CenterX = 200, CenterY = 2000
        });
        var stubOps = ModelRevitPlanner.Plan(
            stubGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var shortUnlabeled = stubOps.Count(op =>
        {
            var s = op.Fields.GetProperty("startMm");
            var e = op.Fields.GetProperty("endMm");
            var dx = e[0].GetDouble() - s[0].GetDouble();
            var dy = e[1].GetDouble() - s[1].GetDouble();
            var len = Math.Sqrt(dx * dx + dy * dy);
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            var w = op.Fields.TryGetProperty("widthMm", out var ww) ? ww.GetInt32() : 0;
            var d = op.Fields.TryGetProperty("depthMm", out var dd) ? dd.GetInt32() : 0;
            return len < 350 && string.IsNullOrEmpty(mark) && !(w == 400 && d == 600);
        });
        if (shortUnlabeled != 0)
        {
            message = $"beam short suppress: expected 0 unlabeled junk stubs, got {shortUnlabeled}";
            return false;
        }

        // Competing texts on one segment: Mark-WxH wins over size-only (50x65 vs D1X4-50x65 → Mark + 500x650).
        var competeGeom = BeamAxisGeom(
            new CadText { Layer = "ANN", Value = "50x65", X = 1800, Y = 100, CenterX = 2000, CenterY = 100 },
            new CadText { Layer = "ANN", Value = "D1X4-50x65", X = 2200, Y = -100, CenterX = 2500, CenterY = -50 });
        var compete = FirstSpan(ModelRevitPlanner.Plan(
            competeGeom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" }));
        if (compete.Mark != "D1X4" || compete.Type != "500x650")
        {
            message = $"beam competing texts: expected D1X4 500x650, got mark={compete.Mark} type={compete.Type}";
            return false;
        }

        // Collinear diagonal: labeled middle + unlabeled ends → both ends inherit 800x1200 (not 800x1600).
        if (!BeamCollinearInheritCase(out message))
            return false;

        // Parallel 500+1000 midlines ~250 mm apart, same span → one beam matching Mark-WxH text width.
        if (!BeamParallelMismatchDedupeCase(out message))
            return false;

        // Wall-top / wall-hatch faces on the beam layer must not suppress or shift labelled beams.
        if (!BeamWallTopAndHatchFaceCases(out message))
            return false;

        message = "ok";
        return true;
    }

    /// <summary>
    /// D1X-15 class: beam faces + collinear wall faces/hatch on the same layer + model wall support
    /// still produce the labelled beam on the mid of the beam faces (not wall thickness, not dropped).
    /// D1Y-11 class: wall face 50 mm inside a true beam face must not shift the centerline by 25 mm.
    /// </summary>
    private static bool BeamWallTopAndHatchFaceCases(out string message)
    {
        message = "";
        // Horizontal wall-top: beam y=0/500, wall hatch y=0/200 sharing the outer face, label D1X-15 (50x70).
        // Model wall support collinear under the bay (long rectangle). Columns at ends.
        var wallTop = new CadGeometry();
        wallTop.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 8000, Y1 = 0 });
        wallTop.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 500, X1 = 8000, Y1 = 500 });
        wallTop.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 200, X1 = 8000, Y1 = 200 }); // wall/hatch face
        wallTop.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1X-15 (50x70)", X = 4000, Y = 250, CenterX = 4000, CenterY = 250
        });
        var wallTopLogs = new List<string>();
        var wallTopOps = ModelRevitPlanner.Plan(
            wallTop, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Log = wallTopLogs.Add,
                // Columns at ends + long structural wall under the beam (plan rectangle along X).
                Supports = new[]
                {
                    new SectionFootprint(0, 250, 400, 400),
                    new SectionFootprint(8000, 250, 400, 400),
                    new SectionFootprint(4000, 100, 8000, 200, 0)
                }
            });
        var wallTopHit = wallTopOps.FirstOrDefault(op =>
        {
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            return mark.Equals("D1X-15", StringComparison.OrdinalIgnoreCase);
        });
        if (wallTopHit is null)
        {
            message = $"wall-top beam kept: expected Mark D1X-15, got {wallTopOps.Count} ops marks=" +
                      string.Join(",", wallTopOps.Select(o =>
                          o.Fields.TryGetProperty("mark", out var m) ? m.GetString() : "?"));
            return false;
        }
        var wtY0 = wallTopHit.Fields.GetProperty("startMm")[1].GetDouble();
        var wtY1 = wallTopHit.Fields.GetProperty("endMm")[1].GetDouble();
        var wtW = wallTopHit.Fields.TryGetProperty("widthMm", out var wtw) ? wtw.GetInt32() : 0;
        var wtD = wallTopHit.Fields.TryGetProperty("depthMm", out var wtd) ? wtd.GetInt32() : 0;
        if (Math.Abs(wtY0 - 250) > 2.5 || Math.Abs(wtY1 - 250) > 2.5 || wtW != 500 || wtD != 700)
        {
            message = $"wall-top beam center/size: expected y=250 500x700, got y=({wtY0:0.###},{wtY1:0.###}) {wtW}x{wtD}";
            return false;
        }

        // Vertical perimeter: true faces x=0/500 + hatch face x=50 → must center at 250 (not 275 = mid of 50..500).
        var offset = new CadGeometry();
        offset.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 0, Y1 = 8000 });
        offset.Entities.Add(new CadLine { Layer = "BEAM", X0 = 500, Y0 = 0, X1 = 500, Y1 = 8000 });
        offset.Entities.Add(new CadLine { Layer = "BEAM", X0 = 50, Y0 = 0, X1 = 50, Y1 = 8000 }); // wall face 50 mm in
        offset.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1Y-11 (50x70)", X = 250, Y = 4000, CenterX = 250, CenterY = 4000, RotationDeg = 90
        });
        // Envelope so this vertical is an edge beam (flush candidate) — outer face near column face x=0.
        offset.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 8000, Y1 = 0 });
        offset.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 500, X1 = 8000, Y1 = 500 });
        offset.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1X5-50x70", X = 4000, Y = 250, CenterX = 4000, CenterY = 250
        });
        offset.Entities.Add(new CadLine { Layer = "BEAM", X0 = 7500, Y0 = 0, X1 = 7500, Y1 = 8000 });
        offset.Entities.Add(new CadLine { Layer = "BEAM", X0 = 8000, Y0 = 0, X1 = 8000, Y1 = 8000 });
        offset.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1Y2-50x70", X = 7750, Y = 4000, CenterX = 7750, CenterY = 4000, RotationDeg = 90
        });
        var offsetOps = ModelRevitPlanner.Plan(
            offset, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Supports = new[]
                {
                    new SectionFootprint(0, 0, 400, 400), new SectionFootprint(0, 8000, 400, 400),
                    new SectionFootprint(8000, 0, 400, 400), new SectionFootprint(8000, 8000, 400, 400),
                    // Long wall along the left bay (outer face at x=0) — flush candidate.
                    new SectionFootprint(100, 4000, 200, 8000, 90)
                }
            });
        var d1y11 = offsetOps.FirstOrDefault(op =>
        {
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            return mark.Equals("D1Y-11", StringComparison.OrdinalIgnoreCase);
        });
        if (d1y11 is null)
        {
            message = $"beam hatch face pair: expected Mark D1Y-11, got marks=" +
                      string.Join(",", offsetOps.Select(o =>
                          o.Fields.TryGetProperty("mark", out var m) ? m.GetString() : "?"));
            return false;
        }
        var ox0 = d1y11.Fields.GetProperty("startMm")[0].GetDouble();
        var ox1 = d1y11.Fields.GetProperty("endMm")[0].GetDouble();
        var ow = d1y11.Fields.TryGetProperty("widthMm", out var oww) ? oww.GetInt32() : 0;
        // True mid of faces 0/500 = 250. Wrong mid of 50/500 = 275 (+25). Allow 5 mm grid round only.
        if (Math.Abs(ox0 - 250) > 2.5 || Math.Abs(ox1 - 250) > 2.5 || ow != 500)
        {
            message = $"beam centerline mid-of-faces: expected x=250 width=500 (not +25 hatch mid), got x=({ox0:0.###},{ox1:0.###}) w={ow}";
            return false;
        }

        // Double outline (true + parallel ghost 25/35 mm): must keep true mid, not average or ghost.
        if (!BeamDoubleOutlineOffsetCases(out message))
            return false;

        // Whole-axis lock: wall hatch on only one bay must not leave the next bay ~100 mm off.
        if (!BeamWholeAxisWallAdjacentCase(out message))
            return false;

        // Wall-top kept when the only model support is the collinear wall (no end columns).
        if (!BeamWallTopWallSupportOnlyCase(out message))
            return false;

        message = "ok";
        return true;
    }

    /// <summary>
    /// Production H1: same vertical axis has a clean 500 outline on the long bay and a wall-hybrid
    /// (outer beam face + wall face) on a short stub → both spans must share mid=250, not ±50/100 mm.
    /// </summary>
    private static bool BeamWholeAxisWallAdjacentCase(out string message)
    {
        message = "";
        var g = new CadGeometry();
        // True beam faces x=0/500 for the full height.
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 0, Y1 = 12000 });
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 500, Y0 = 0, X1 = 500, Y1 = 12000 });
        // Wall/hatch face only on the south bay (pairs with x=500 → mid=350 if not blocked).
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 200, Y0 = 0, X1 = 200, Y1 = 4000 });
        // Parallel wall faces that could form a 200 mm wall mid=100 on the south stub alone.
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 0, Y1 = 4000 });
        g.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1Y-B (50x70)", X = 250, Y = 8000,
            CenterX = 250, CenterY = 8000, RotationDeg = 90
        });
        var logs = new List<string>();
        var ops = ModelRevitPlanner.Plan(
            g, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Log = logs.Add,
                Supports = new[]
                {
                    new SectionFootprint(250, 0, 400, 400),
                    new SectionFootprint(250, 4000, 400, 400),
                    new SectionFootprint(250, 8000, 400, 400),
                    new SectionFootprint(250, 12000, 400, 400),
                    // Long wall along the left face (outer at x=0).
                    new SectionFootprint(100, 2000, 200, 4000, 90)
                }
            });
        var vertical = ops.Where(op =>
        {
            var sx = op.Fields.GetProperty("startMm")[0].GetDouble();
            var ex = op.Fields.GetProperty("endMm")[0].GetDouble();
            return Math.Abs(sx - ex) < 50;
        }).ToList();
        if (vertical.Count == 0)
        {
            message = "whole-axis wall-adjacent: expected vertical beam ops, got 0";
            return false;
        }

        foreach (var op in vertical)
        {
            var x0 = op.Fields.GetProperty("startMm")[0].GetDouble();
            var x1 = op.Fields.GetProperty("endMm")[0].GetDouble();
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            if (Math.Abs(x0 - 250) > 2.5 || Math.Abs(x1 - 250) > 2.5)
            {
                message =
                    $"whole-axis wall-adjacent: expected x=250 on every span (not wall-hybrid), " +
                    $"got x=({x0:0.###},{x1:0.###}) mark={mark} ops={vertical.Count}";
                return false;
            }
        }

        if (!vertical.Any(op =>
        {
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            return mark.Equals("D1Y-B", StringComparison.OrdinalIgnoreCase);
        }))
        {
            message = "whole-axis wall-adjacent: expected Mark D1Y-B, got marks=" +
                      string.Join(",", vertical.Select(o =>
                          o.Fields.TryGetProperty("mark", out var m) ? m.GetString() : "?"));
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>
    /// Production H2: labelled wall-top bay with only a collinear model wall as support (ends on the
    /// wall) must still emit a Main beam — wall stations are the interval ends, not a single mid.
    /// </summary>
    private static bool BeamWallTopWallSupportOnlyCase(out string message)
    {
        message = "";
        var g = new CadGeometry();
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 8000, Y1 = 0 });
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 500, X1 = 8000, Y1 = 500 });
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 200, X1 = 8000, Y1 = 200 });
        g.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1X-B (50x70)", X = 4000, Y = 250, CenterX = 4000, CenterY = 250
        });
        // Crossing vertical mains that would chop a Secondary wall-top into stubs.
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 2000, Y0 = -2000, X1 = 2000, Y1 = 2500 });
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 2500, Y0 = -2000, X1 = 2500, Y1 = 2500 });
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 5000, Y0 = -2000, X1 = 5000, Y1 = 2500 });
        g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 5500, Y0 = -2000, X1 = 5500, Y1 = 2500 });
        g.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1Y1 (50x70)", X = 2250, Y = 0, CenterX = 2250, CenterY = 500, RotationDeg = 90
        });
        g.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1Y2 (50x70)", X = 5250, Y = 0, CenterX = 5250, CenterY = 500, RotationDeg = 90
        });
        var logs = new List<string>();
        var ops = ModelRevitPlanner.Plan(
            g, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Log = logs.Add,
                Supports = new[]
                {
                    // Only the collinear wall under the bay — no end columns.
                    new SectionFootprint(4000, 100, 8000, 200, 0),
                    // Columns for the crossing verticals only.
                    new SectionFootprint(2250, -2000, 400, 400),
                    new SectionFootprint(2250, 2500, 400, 400),
                    new SectionFootprint(5250, -2000, 400, 400),
                    new SectionFootprint(5250, 2500, 400, 400)
                }
            });
        var hit = ops.FirstOrDefault(op =>
        {
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            return mark.Equals("D1X-B", StringComparison.OrdinalIgnoreCase);
        });
        if (hit is null)
        {
            message = "wall-top wall-support-only: expected Mark D1X-B, got marks=" +
                      string.Join(",", ops.Select(o =>
                          o.Fields.TryGetProperty("mark", out var m) ? m.GetString() : "?")) +
                      " logs=" + string.Join(" | ", logs.Where(l => l.Contains("D1X-B", StringComparison.Ordinal) ||
                                                                    l.StartsWith("beam plan", StringComparison.Ordinal)));
            return false;
        }

        var y0 = hit.Fields.GetProperty("startMm")[1].GetDouble();
        var y1 = hit.Fields.GetProperty("endMm")[1].GetDouble();
        var w = hit.Fields.TryGetProperty("widthMm", out var ww) ? ww.GetInt32() : 0;
        var d = hit.Fields.TryGetProperty("depthMm", out var dd) ? dd.GetInt32() : 0;
        if (Math.Abs(y0 - 250) > 2.5 || Math.Abs(y1 - 250) > 2.5 || w != 500 || d != 700)
        {
            message =
                $"wall-top wall-support-only: expected y=250 500x700, got y=({y0:0.###},{y1:0.###}) {w}x{d}";
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>
    /// Production failure after BeamPairRank WxH fix: CAD often carries a second beam outline offset
    /// ~25 mm or ~35 mm (finish / hatch). Cross-pairing true+ghost (t≈525) plus near-dup averaging
    /// shifted the midline; ghost-only would be a full 25 mm face error.
    /// </summary>
    private static bool BeamDoubleOutlineOffsetCases(out string message)
    {
        message = "";
        foreach (var shift in new[] { 25.0, 35.0 })
        {
            var g = new CadGeometry();
            g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 0, Y1 = 8000 });
            g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 500, Y0 = 0, X1 = 500, Y1 = 8000 });
            g.Entities.Add(new CadLine { Layer = "BEAM", X0 = shift, Y0 = 0, X1 = shift, Y1 = 8000 });
            g.Entities.Add(new CadLine { Layer = "BEAM", X0 = 500 + shift, Y0 = 0, X1 = 500 + shift, Y1 = 8000 });
            g.Entities.Add(new CadText
            {
                Layer = "ANN", Value = "D1Y-3 (50x70)", X = 250, Y = 4000,
                CenterX = 250, CenterY = 4000, RotationDeg = 90
            });
            var ops = ModelRevitPlanner.Plan(
                g, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
                new AnnotationSize(null, null, null, ""),
                placement: new ModelRevitPlacement
                {
                    FamilyName = "S-BEAM",
                    Supports = new[]
                    {
                        new SectionFootprint(250, 0, 400, 400),
                        new SectionFootprint(250, 8000, 400, 400)
                    }
                });
            var hit = ops.FirstOrDefault(op =>
            {
                var sx = op.Fields.GetProperty("startMm")[0].GetDouble();
                var ex = op.Fields.GetProperty("endMm")[0].GetDouble();
                return Math.Abs(sx - ex) < 50;
            });
            if (hit is null)
            {
                message = $"double-outline +{shift:0}mm: expected 1 vertical beam, got {ops.Count}";
                return false;
            }

            var x0 = hit.Fields.GetProperty("startMm")[0].GetDouble();
            var x1 = hit.Fields.GetProperty("endMm")[0].GetDouble();
            var w = hit.Fields.TryGetProperty("widthMm", out var ww) ? ww.GetInt32() : 0;
            if (Math.Abs(x0 - 250) > 2.5 || Math.Abs(x1 - 250) > 2.5 || w != 500)
            {
                message =
                    $"double-outline +{shift:0}mm: expected x=250 w=500 (not average/ghost), got x=({x0:0.###},{x1:0.###}) w={w}";
                return false;
            }
        }

        // Wall-top style: beam faces 0/500 + wall 200, label 30x70 → keep mid of beam faces (250), type 300x700.
        var wallTop = new CadGeometry();
        wallTop.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 8000, Y1 = 0 });
        wallTop.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 500, X1 = 8000, Y1 = 500 });
        wallTop.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 200, X1 = 8000, Y1 = 200 });
        wallTop.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D13-B (30x70)", X = 4000, Y = 250, CenterX = 4000, CenterY = 250
        });
        var wallOps = ModelRevitPlanner.Plan(
            wallTop, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Supports = new[]
                {
                    new SectionFootprint(0, 250, 400, 400),
                    new SectionFootprint(8000, 250, 400, 400),
                    new SectionFootprint(4000, 100, 8000, 200, 0)
                }
            });
        var d13 = wallOps.FirstOrDefault(op =>
        {
            var mark = op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "";
            return mark.Equals("D13-B", StringComparison.OrdinalIgnoreCase);
        });
        if (d13 is null)
        {
            message = "D13-B wall-top: expected Mark D13-B on beam outline, got marks=" +
                      string.Join(",", wallOps.Select(o =>
                          o.Fields.TryGetProperty("mark", out var m) ? m.GetString() : "?"));
            return false;
        }

        var wy0 = d13.Fields.GetProperty("startMm")[1].GetDouble();
        var wy1 = d13.Fields.GetProperty("endMm")[1].GetDouble();
        var ww13 = d13.Fields.TryGetProperty("widthMm", out var w13) ? w13.GetInt32() : 0;
        var wd13 = d13.Fields.TryGetProperty("depthMm", out var d13d) ? d13d.GetInt32() : 0;
        // Mid of CAD beam faces 0/500 = 250 (not wall-hybrid 200/500 → 350). Type from label 300x700.
        if (Math.Abs(wy0 - 250) > 2.5 || Math.Abs(wy1 - 250) > 2.5 || ww13 != 300 || wd13 != 700)
        {
            message =
                $"D13-B wall-top: expected y=250 300x700 (beam faces, not wall hybrid), got y=({wy0:0.###},{wy1:0.###}) {ww13}x{wd13}";
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>
    /// Diagonal face-pair broken into three abutting Main spans; only the middle has D1Y36-80x120 →
    /// all three become 800x1200 (no width×2 on the unlabeled ends).
    /// </summary>
    private static bool BeamCollinearInheritCase(out string message)
    {
        // Unit diagonal direction (~33.7°): three abutting 800-wide face pairs + columns at joints.
        const double ux = 0.83205;
        const double uy = 0.55470;
        static (double X, double Y) P(double t) => (t * ux, t * uy);
        var a = P(0);
        var b = P(3000);
        var c = P(7000);
        var d = P(11000);
        var nx = -uy * 400;
        var ny = ux * 400;
        var geom = new CadGeometry();
        void Face(double t0, double t1)
        {
            var p0 = P(t0);
            var p1 = P(t1);
            geom.Entities.Add(new CadLine { Layer = "BEAM", X0 = p0.X, Y0 = p0.Y, X1 = p1.X, Y1 = p1.Y });
            geom.Entities.Add(new CadLine
            {
                Layer = "BEAM", X0 = p0.X + nx, Y0 = p0.Y + ny, X1 = p1.X + nx, Y1 = p1.Y + ny
            });
        }

        Face(0, 3000);
        Face(3000, 7000);
        Face(7000, 11000);
        geom.Entities.Add(ColRect(a.X - 200, a.Y - 200, a.X + 200, a.Y + 200));
        geom.Entities.Add(ColRect(b.X - 200, b.Y - 200, b.X + 200, b.Y + 200));
        geom.Entities.Add(ColRect(c.X - 200, c.Y - 200, c.X + 200, c.Y + 200));
        geom.Entities.Add(ColRect(d.X - 200, d.Y - 200, d.X + 200, d.Y + 200));
        var mid = P(5000);
        geom.Entities.Add(new CadText
        {
            Layer = "ANN",
            Value = "D1Y36-80x120",
            X = mid.X + nx * 0.5,
            Y = mid.Y + ny * 0.5,
            CenterX = mid.X + nx * 0.5,
            CenterY = mid.Y + ny * 0.5,
            RotationDeg = Math.Atan2(uy, ux) * 180.0 / Math.PI
        });

        var ops = ModelRevitPlanner.Plan(
            geom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var diagonal = ops
            .Select(op => (
                W: op.Fields.TryGetProperty("widthMm", out var ww) ? ww.GetInt32() : 0,
                D: op.Fields.TryGetProperty("depthMm", out var dd) ? dd.GetInt32() : 0,
                Mark: op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "",
                Type: op.Fields.TryGetProperty("familyType", out var ft) ? ft.GetString() ?? "" : ""))
            .Where(t => t.W == 800 || t.Type.StartsWith("800x", StringComparison.Ordinal))
            .ToList();
        if (diagonal.Count < 2)
        {
            message = $"beam collinear inherit: expected ≥2 diagonal ops, got {diagonal.Count} (total {ops.Count})";
            return false;
        }

        if (diagonal.Any(t => t.W != 800 || t.D != 1200))
        {
            message = "beam collinear inherit: expected all 800x1200, got " +
                      string.Join(",", diagonal.Select(t => $"{t.Mark}:{t.W}x{t.D}"));
            return false;
        }

        if (diagonal.Count(t => string.Equals(t.Mark, "D1Y36", StringComparison.OrdinalIgnoreCase)) < 1)
        {
            message = "beam collinear inherit: expected Mark D1Y36 on ≥1 segment";
            return false;
        }

        message = "ok";
        return true;
    }

    /// <summary>
    /// Parallel 500 vs 1000 midlines 250 mm apart, almost identical span → keep the Mark-WxH width.
    /// </summary>
    private static bool BeamParallelMismatchDedupeCase(out string message)
    {
        var geom = new CadGeometry();
        // 500-wide at X=0, 1000-wide at X=250 (shared-ish face), midlines 250 mm apart.
        geom.Entities.Add(new CadLine { Layer = "BEAM", X0 = -250, Y0 = 0, X1 = -250, Y1 = 9000 });
        geom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 250, Y0 = 0, X1 = 250, Y1 = 9000 });
        geom.Entities.Add(new CadLine { Layer = "BEAM", X0 = -500, Y0 = 0, X1 = -500, Y1 = 9000 });
        geom.Entities.Add(new CadLine { Layer = "BEAM", X0 = 500, Y0 = 0, X1 = 500, Y1 = 9000 });
        geom.Entities.Add(ColRect(-400, -200, 400, 200));
        geom.Entities.Add(ColRect(-400, 8800, 400, 9200));
        geom.Entities.Add(new CadText
        {
            Layer = "ANN", Value = "D1Y7-100x65", X = 50, Y = 4500, RotationDeg = 90,
            CenterX = 50, CenterY = 4500
        });
        var ops = ModelRevitPlanner.Plan(
            geom, "BEAM", ModelRevitPartKind.Beam, "L1", "L2",
            new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var vertical = ops
            .Where(op =>
            {
                var sx = op.Fields.GetProperty("startMm")[0].GetDouble();
                var ex = op.Fields.GetProperty("endMm")[0].GetDouble();
                return Math.Abs(sx - ex) < 50;
            })
            .ToList();
        if (vertical.Count != 1)
        {
            message = $"beam parallel mismatch dedupe: expected 1 vertical op, got {vertical.Count}";
            return false;
        }

        var vo = vertical[0];
        var vw = vo.Fields.TryGetProperty("widthMm", out var vww) ? vww.GetInt32() : 0;
        var vd = vo.Fields.TryGetProperty("depthMm", out var vdd) ? vdd.GetInt32() : 0;
        var vm = vo.Fields.TryGetProperty("mark", out var vmm) ? vmm.GetString() ?? "" : "";
        if (!string.Equals(vm, "D1Y7", StringComparison.OrdinalIgnoreCase) || vw != 1000 || vd != 650)
        {
            message = $"beam parallel mismatch: expected D1Y7 1000x650, got mark={vm} {vw}x{vd}";
            return false;
        }

        message = "ok";
        return true;
    }

    private static (string Mark, string Type) FirstSpan(IReadOnlyList<Dss.McpContracts.CreateOperation> ops) =>
        ops.Select(op => (
                X: op.Fields.GetProperty("startMm")[0].GetDouble(),
                Mark: op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "",
                Type: op.Fields.TryGetProperty("familyType", out var ft) ? ft.GetString() ?? "" : ""))
            .OrderBy(t => t.X)
            .Select(t => (t.Mark, t.Type))
            .FirstOrDefault();

    /// <summary>Main = support center to support center; secondary breaks at main centerlines.</summary>
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

        // (i) 3 columns on one axis → 2 main spans, column center to center.
        var three = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(0, 200, 8000, 200, 400) },
            new[] { Col(0, 200), Col(4000, 200), Col(8000, 200) });
        if (three.Count != 2 || !Has(three, BeamSpanKind.Main, 0, 200, 4000, 200) ||
            !Has(three, BeamSpanKind.Main, 4000, 200, 8000, 200))
        {
            message = $"beam 3 columns: expected main 0..4000 + 4000..8000, got {Describe(three)}";
            return false;
        }

        // (ii) A structural wall across the axis is a support too (center of wall footprint).
        var wall = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(0, 0, 8000, 0, 300) },
            new[] { Col(0, 0), new SectionFootprint(4000, 0, 200, 3000), Col(8000, 0) });
        if (wall.Count != 2 || !Has(wall, BeamSpanKind.Main, 0, 0, 4000, 0) ||
            !Has(wall, BeamSpanKind.Main, 4000, 0, 8000, 0))
        {
            message = $"beam wall support: expected main 0..4000 + 4000..8000, got {Describe(wall)}";
            return false;
        }

        // (iii) Secondary CAD across 2 mains → 3 pieces ending at main centers; mains stay whole (center→center).
        var grid = BeamCenterlineReader.PrepareRuns(
            new[]
            {
                new WallRun(0, 0, 8000, 0, 400),
                new WallRun(0, 6000, 8000, 6000, 400),
                new WallRun(4000, -2000, 4000, 8000, 300)
            },
            new[] { Col(0, 0), Col(8000, 0), Col(0, 6000), Col(8000, 6000) });
        if (grid.Count(s => s.Kind == BeamSpanKind.Main) != 2 ||
            !Has(grid, BeamSpanKind.Main, 0, 0, 8000, 0) || !Has(grid, BeamSpanKind.Main, 0, 6000, 8000, 6000) ||
            grid.Count(s => s.Kind == BeamSpanKind.Secondary) != 3 ||
            !Has(grid, BeamSpanKind.Secondary, 4000, -2000, 4000, 0) ||
            !Has(grid, BeamSpanKind.Secondary, 4000, 0, 4000, 6000) ||
            !Has(grid, BeamSpanKind.Secondary, 4000, 6000, 4000, 8000))
        {
            message = $"beam secondary split: got {Describe(grid)}";
            return false;
        }

        // Long beam with columns only at its far ends, crossed mid-span by 4 column-to-column mains → 5 pieces.
        var longRuns = new List<WallRun> { new WallRun(0, 0, 20000, 0, 300) };
        var longCols = new List<SectionFootprint> { Col(0, 0), Col(20000, 0) };
        foreach (var x in new[] { 4000.0, 8000, 12000, 16000 })
        {
            longRuns.Add(new WallRun(x, -3000, x, 3000, 500));
            longCols.Add(Col(x, -3000));
            longCols.Add(Col(x, 3000));
        }

        var crossed = BeamCenterlineReader.PrepareRuns(longRuns, longCols);
        if (crossed.Count(s => s.Kind == BeamSpanKind.Main) != 4 ||
            crossed.Count(s => s.Kind == BeamSpanKind.Secondary) != 5 ||
            !Has(crossed, BeamSpanKind.Secondary, 0, 0, 4000, 0) ||
            !Has(crossed, BeamSpanKind.Secondary, 8000, 0, 12000, 0) ||
            !Has(crossed, BeamSpanKind.Secondary, 16000, 0, 20000, 0))
        {
            message = $"beam long span across 4 mains: expected 4 main + 5 secondary, got {Describe(crossed)}";
            return false;
        }

        // Two secondaries crossing between mains: the narrower one breaks, the wider stays whole.
        var twoSecondary = BeamCenterlineReader.PrepareRuns(
            new[]
            {
                new WallRun(0, 0, 8000, 0, 400),
                new WallRun(0, 6000, 8000, 6000, 400),
                new WallRun(4000, 0, 4000, 6000, 300),
                new WallRun(1000, 3000, 7000, 3000, 250)
            },
            new[] { Col(0, 0), Col(8000, 0), Col(0, 6000), Col(8000, 6000) });
        if (!Has(twoSecondary, BeamSpanKind.Secondary, 4000, 0, 4000, 6000) ||
            !Has(twoSecondary, BeamSpanKind.Secondary, 1000, 3000, 4000, 3000) ||
            !Has(twoSecondary, BeamSpanKind.Secondary, 4000, 3000, 7000, 3000))
        {
            message = $"beam secondary × secondary: expected 250 broken at x=4000, got {Describe(twoSecondary)}";
            return false;
        }

        // Fake column (grid bubble) alone on a beam-to-beam axis → still secondary, split at mains.
        var fake = BeamCenterlineReader.PrepareRuns(
            new[]
            {
                new WallRun(0, 0, 8000, 0, 400),
                new WallRun(0, 6000, 8000, 6000, 400),
                new WallRun(4000, 500, 4000, 5500, 300)
            },
            new[] { Col(0, 0), Col(8000, 0), Col(0, 6000), Col(8000, 6000), new SectionFootprint(4000, 3000, 100, 100) });
        if (fake.Any(s => s.Kind == BeamSpanKind.Main && Math.Abs(s.Run.X0 - 4000) < 1 && Math.Abs(s.Run.X1 - 4000) < 1) ||
            fake.Count(s => s.Kind == BeamSpanKind.Secondary) < 1)
        {
            message = $"beam fake support: vertical must stay secondary, got {Describe(fake)}";
            return false;
        }

        // Mid-span false CAD supports on a continuous main must not leave a hole: only real
        // end columns (and joints) should define main centers. Dense mid-span hatches that do
        // not form a second axis stay filtered out upstream; here ClassifyAxis with only the
        // two end columns keeps one continuous main.
        var continuous = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(0, 200, 12000, 200, 500) },
            new[] { Col(0, 200), Col(12000, 200) });
        if (continuous.Count != 1 || !Has(continuous, BeamSpanKind.Main, 0, 200, 12000, 200))
        {
            message = $"beam continuous main: expected one main 0..12000, got {Describe(continuous)}";
            return false;
        }

        // (v) Collinear CAD pieces 2500 apart at a crossing beam → one main span column center to center.
        var bridged = BeamCenterlineReader.PrepareRuns(
            new[]
            {
                new WallRun(200, 0, 3000, 0, 300),
                new WallRun(5500, 0, 9800, 0, 300),
                new WallRun(4250, -3000, 4250, 3000, 400)
            },
            new[] { Col(0, 0), Col(10000, 0) });
        if (bridged.Count(s => s.Kind == BeamSpanKind.Main) != 1 || !Has(bridged, BeamSpanKind.Main, 0, 0, 10000, 0) ||
            bridged.Count(s => s.Kind == BeamSpanKind.Secondary) != 2)
        {
            message = $"beam gap at crossing: expected main 0..10000 + 2 secondary, got {Describe(bridged)}";
            return false;
        }

        // Stub stopping short of both columns reaches their centers.
        var stub = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(800, 200, 3000, 200, 400) },
            new[] { Col(0, 200), Col(4000, 200) });
        if (stub.Count != 1 || !Has(stub, BeamSpanKind.Main, 0, 200, 4000, 200))
        {
            message = $"beam extend to columns: expected main 0..4000, got {Describe(stub)}";
            return false;
        }

        // Near one distant column only (no pair on CAD) → secondary, do not become fake main.
        var nearOne = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(2000, 200, 5000, 200, 300) },
            new[] { Col(0, 200), Col(8000, 5000) });
        if (nearOne.Count != 1 || nearOne[0].Kind != BeamSpanKind.Secondary)
        {
            message = $"beam near one column: expected 1 secondary, got {Describe(nearOne)}";
            return false;
        }

        // Bays drawn at different midlines on one grid keep their own CAD lateral (x=-11950 then -12050).
        var stepped = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(50, 300, 50, 7700, 500), new WallRun(-50, 8300, -50, 15700, 500) },
            new[] { Col(0, 0), Col(0, 8000), Col(0, 16000) });
        if (!Has(stepped, BeamSpanKind.Main, 50, 0, 50, 8000) || !Has(stepped, BeamSpanKind.Main, -50, 8000, -50, 16000))
        {
            message = $"beam stepped bays: expected x=50 then x=-50, got {Describe(stepped)}";
            return false;
        }

        // A crossing wall against a column is one joint: no stub inside them, and the span past it starts at the column.
        var adjacent = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(0, 300, 0, 7700, 500), new WallRun(0, 8650, 0, 13700, 500) },
            new[] { Col(0, 0), new SectionFootprint(0, 8000, 600, 700), new SectionFootprint(0, 8500, 20000, 300), Col(0, 14000) });
        if (adjacent.Count != 2 || !Has(adjacent, BeamSpanKind.Main, 0, 0, 0, 8000) ||
            !Has(adjacent, BeamSpanKind.Main, 0, 8000, 0, 14000))
        {
            message = $"beam column+wall joint: expected 0..8000 + 8000..14000, got {Describe(adjacent)}";
            return false;
        }

        // Two beams whose outer faces are on the border layer: the 680 pair on the beam layer is false.
        var twoEdge = new CadGeometry();
        twoEdge.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = 0, X1 = 8000, Y1 = 0 });
        twoEdge.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = -680, X1 = 8000, Y1 = -680 });
        twoEdge.Entities.Add(new CadLine { Layer = "BORDER", X0 = -300, Y0 = -300, X1 = 8300, Y1 = -300 });
        twoEdge.Entities.Add(new CadLine { Layer = "BORDER", X0 = -300, Y0 = -900, X1 = 8300, Y1 = -900 });
        twoEdge.Entities.Add(new CadText { Layer = "ANN", Value = "GTC1(30x40)", X = 4000, Y = 120, CenterX = 4000, CenterY = 120 });
        twoEdge.Entities.Add(new CadText { Layer = "ANN", Value = "GTC2(22x40)", X = 4000, Y = -1100, CenterX = 4000, CenterY = -1100 });
        var twoEdgeOps = ModelRevitPlanner.Plan(
            twoEdge, "BEAM", ModelRevitPartKind.Beam, "L1", "L2", new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        double EdgeY(string mark) => twoEdgeOps
            .Where(op => op.Fields.TryGetProperty("mark", out var m) && m.GetString() == mark)
            .Select(op => op.Fields.GetProperty("startMm")[1].GetDouble())
            .DefaultIfEmpty(double.NaN).First();
        if (twoEdgeOps.Count != 2 || !(Math.Abs(EdgeY("GTC1") + 150) <= 2.5) || !(Math.Abs(EdgeY("GTC2") + 790) <= 2.5))
        {
            message = $"beam false wide pair: expected GTC1 y=-150, GTC2 y=-790, got {twoEdgeOps.Count} ops y={EdgeY("GTC1")},{EdgeY("GTC2")}";
            return false;
        }

        // A nested bound xref doubles the layer prefix: both names are the picked layer.
        var nested = new CadGeometry();
        nested.Entities.Add(new CadLine { Layer = "X$0$BEAM", X0 = 0, Y0 = -150, X1 = 8000, Y1 = -150 });
        nested.Entities.Add(new CadLine { Layer = "X$0$X$0$BEAM", X0 = 0, Y0 = 150, X1 = 8000, Y1 = 150 });
        var nestedOps = ModelRevitPlanner.Plan(
            nested, "X$0$BEAM", ModelRevitPartKind.Beam, "L1", "L2", new AnnotationSize(300, 600, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        if (nestedOps.Count != 1)
        {
            message = $"beam nested xref layer: expected 1 beam from faces on both names, got {nestedOps.Count}";
            return false;
        }

        // Beam groups on two layers of one drawing, grid lines 400 apart and a grid bubble must not become beams.
        var groups = new CadGeometry();
        for (var i = 0; i < 6; i++)
        {
            var y = i * 6000.0;
            var layer = "X$0$DG";
            groups.Entities.Add(new CadLine { Layer = layer, X0 = 0, Y0 = y - 250, X1 = 8000, Y1 = y - 250 });
            groups.Entities.Add(new CadLine { Layer = layer, X0 = 0, Y0 = y + 250, X1 = 8000, Y1 = y + 250 });
            groups.Entities.Add(new CadText { Layer = "0", Value = $"DGX{i + 1}-50x60", X = 4000, Y = y + 400, CenterX = 4000, CenterY = y + 400 });
        }

        groups.Entities.Add(new CadLine { Layer = "X$0$D1", X0 = 10000, Y0 = -150, X1 = 18000, Y1 = -150 });
        groups.Entities.Add(new CadLine { Layer = "X$0$D1", X0 = 10000, Y0 = 150, X1 = 18000, Y1 = 150 });
        groups.Entities.Add(new CadLine { Layer = "X$0$Tim Truc", X0 = -5000, Y0 = 40000, X1 = 30000, Y1 = 40000 });
        groups.Entities.Add(new CadLine { Layer = "X$0$Tim Truc", X0 = -5000, Y0 = 40400, X1 = 30000, Y1 = 40400 });
        groups.Entities.Add(new CadText { Layer = "0", Value = "30x60", X = 14000, Y = 300, CenterX = 14000, CenterY = 300 });
        groups.Entities.Add(new CadText { Layer = "0", Value = "40x60", X = 5000, Y = 40600, CenterX = 5000, CenterY = 40600 });
        groups.Entities.Add(new CadText { Layer = "0", Value = "C5", X = 14000, Y = -300, CenterX = 14000, CenterY = -300 });
        var groupOps = ModelRevitPlanner.Plan(
            groups, "X$0$D1", ModelRevitPartKind.Beam, "L1", "L2", new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement { FamilyName = "S-BEAM" });
        var groupMarks = groupOps.Select(op => op.Fields.TryGetProperty("mark", out var m) ? m.GetString() ?? "" : "").ToList();
        if (groupOps.Count != 7 || groupMarks.Contains("C5") || groupMarks.Count(m => m == "DGX1") != 1)
        {
            message = $"beam labelled layers: expected 7 beams without grid marks, got {groupOps.Count} [{string.Join(",", groupMarks)}]";
            return false;
        }

        // A face broken by a crossing beam is one face; the 500 bay past the column keeps its CAD width.
        var wideRow = new CadGeometry();
        wideRow.Entities.Add(new CadLine { Layer = "BEAM", X0 = 300, Y0 = -500, X1 = 4000, Y1 = -500 });
        wideRow.Entities.Add(new CadLine { Layer = "BEAM", X0 = 4300, Y0 = -500, X1 = 7700, Y1 = -500 });
        wideRow.Entities.Add(new CadLine { Layer = "BEAM", X0 = 300, Y0 = 500, X1 = 3700, Y1 = 500 });
        wideRow.Entities.Add(new CadLine { Layer = "BEAM", X0 = 4300, Y0 = 500, X1 = 7700, Y1 = 500 });
        wideRow.Entities.Add(new CadLine { Layer = "BEAM", X0 = 8300, Y0 = -250, X1 = 15700, Y1 = -250 });
        wideRow.Entities.Add(new CadLine { Layer = "BEAM", X0 = 8300, Y0 = 250, X1 = 15700, Y1 = 250 });
        wideRow.Entities.Add(new CadText { Layer = "ANN", Value = "DGX14-100x60", X = 2000, Y = 700, CenterX = 2000, CenterY = 700 });
        var wideOps = ModelRevitPlanner.Plan(
            wideRow, "BEAM", ModelRevitPartKind.Beam, "L1", "L2", new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Supports = new[] { new SectionFootprint(0, 0, 600, 600), new SectionFootprint(8000, 0, 600, 600), new SectionFootprint(16000, 0, 600, 600) }
            });
        var wideSpans = wideOps
            .Select(op => $"{op.Fields.GetProperty("startMm")[0].GetDouble():0}-{op.Fields.GetProperty("endMm")[0].GetDouble():0}:{op.Fields.GetProperty("widthMm").GetInt32()}")
            .OrderBy(t => t.Length).ThenBy(t => t, StringComparer.Ordinal).ToList();
        if (wideSpans.Count != 2 || !wideSpans.Contains("0-8000:1000") || !wideSpans.Contains("8000-16000:500"))
        {
            message = $"beam broken face / CAD width: expected 0-8000:1000 + 8000-16000:500, got [{string.Join(" ", wideSpans)}]";
            return false;
        }

        // A deep column ahead of the face still closes the span (D5Y9: 1000 deep, 1650 to its center).
        var deep = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(0, 300, 0, 6000, 500) },
            new[] { Col(0, 0), new SectionFootprint(0, 7650, 500, 1000) });
        if (!Has(deep, BeamSpanKind.Main, 0, 0, 0, 7650))
        {
            message = $"beam deep column: expected main 0..7650, got {Describe(deep)}";
            return false;
        }

        // A span labelled off its axis width follows its own lines, one of them on another layer.
        var drawnSpan = BeamCenterlineReader.CenterOnDrawnFaces(
            new WallRun(0, 0, 0, 6500, 500), 400,
            new CadEntity[]
            {
                new CadLine { Layer = "A", X0 = -250, Y0 = 250, X1 = -250, Y1 = 6250 },
                new CadLine { Layer = "A", X0 = 50, Y0 = 4200, X1 = 50, Y1 = 6250 },
                new CadLine { Layer = "B", X0 = 50, Y0 = 250, X1 = 50, Y1 = 3900 }
            });
        if (Math.Abs(drawnSpan.X0 + 50) > 1 || Math.Abs(drawnSpan.X1 - drawnSpan.X0) > 1)
        {
            message = $"drawn span: expected flush with the axis face at x=-50, got x={drawnSpan.X0:0}";
            return false;
        }

        // A column on another layer joins the picked ones when it sits at a grid crossing and fits their sizes.
        static CadPolyline Outline(string layer, double x, double y, double w, double d)
        {
            var outline = new CadPolyline { Layer = layer, Closed = true };
            outline.Points.Add(new CadPoint2(x - w / 2, y - d / 2));
            outline.Points.Add(new CadPoint2(x + w / 2, y - d / 2));
            outline.Points.Add(new CadPoint2(x + w / 2, y + d / 2));
            outline.Points.Add(new CadPoint2(x - w / 2, y + d / 2));
            return outline;
        }

        var mixedColumns = new CadGeometry();
        mixedColumns.Entities.Add(Outline("COT", 0, 0, 500, 600));
        mixedColumns.Entities.Add(Outline("COT", 6000, 0, 500, 600));
        mixedColumns.Entities.Add(Outline("COT MOI", 0, 6000, 500, 600));
        mixedColumns.Entities.Add(Outline("COT MOI", 6000, 0, 500, 600));
        mixedColumns.Entities.Add(Outline("CUA", 3000, 3000, 500, 600));
        mixedColumns.Entities.Add(Outline("MONG", 6000, 6000, 2000, 2000));
        var mixedPlace = new ModelRevitPlacement { FamilyName = "C" };
        var mixedOps = ModelRevitPlanner.Plan(
            mixedColumns, "COT", ModelRevitPartKind.Column, "L1", "L2", new AnnotationSize(null, null, null, ""),
            gridPoints: new[] { new CadPoint2(0, 0), new CadPoint2(6000, 0), new CadPoint2(0, 6000), new CadPoint2(6000, 6000) },
            placement: mixedPlace);
        if (mixedOps.Count != 3 || mixedPlace.OtherLayerColumns.Count != 1 || mixedPlace.OtherLayerColumns[0] != "COT MOI")
        {
            message = $"columns on other layers: expected 3 with 1 from COT MOI, got {mixedOps.Count} ({string.Join(",", mixedPlace.OtherLayerColumns)})";
            return false;
        }

        // A beam stops at a lift shaft: along its wall only one CAD edge goes on, and that is the wall's.
        var shaft = BeamCenterlineReader.PrepareRuns(
            new[] { new WallRun(300, 0, 3700, 0, 400) },
            new[] { Col(0, 0), Col(4000, 0), Col(8000, 0), new SectionFootprint(6000, -75, 3400, 250) },
            new CadEntity[]
            {
                new CadLine { Layer = "BEAM", X0 = 300, Y0 = -200, X1 = 7700, Y1 = -200 },
                new CadLine { Layer = "BEAM", X0 = 300, Y0 = 200, X1 = 3700, Y1 = 200 }
            });
        if (shaft.Count != 1 || !Has(shaft, BeamSpanKind.Main, 0, 0, 4000, 0))
        {
            message = $"beam at lift shaft: expected main 0..4000 only, got {Describe(shaft)}";
            return false;
        }

        // Wall-top beam with one face on the beam layer: labelled width, on the wall side of the face.
        var oneFace = new CadGeometry();
        oneFace.Entities.Add(new CadLine { Layer = "BEAM", X0 = 0, Y0 = -500, X1 = 16000, Y1 = -500 });
        oneFace.Entities.Add(new CadLine { Layer = "BEAM", X0 = 7750, Y0 = -8000, X1 = 7750, Y1 = -1000 });
        oneFace.Entities.Add(new CadLine { Layer = "BEAM", X0 = 8250, Y0 = -8000, X1 = 8250, Y1 = -1000 });
        oneFace.Entities.Add(new CadText { Layer = "ANN", Value = "D1X-B (50x70)", X = 4000, Y = -800, CenterX = 4000, CenterY = -800 });
        var oneFaceOps = ModelRevitPlanner.Plan(
            oneFace, "BEAM", ModelRevitPartKind.Beam, "L1", "L2", new AnnotationSize(null, null, null, ""),
            placement: new ModelRevitPlacement
            {
                FamilyName = "S-BEAM",
                Supports = new[]
                {
                    new SectionFootprint(0, -650, 600, 700), new SectionFootprint(8000, -650, 600, 700),
                    new SectionFootprint(16000, -650, 600, 700), new SectionFootprint(8000, -150, 16000, 300)
                }
            });
        var tops = oneFaceOps
            .Where(op => op.Fields.TryGetProperty("mark", out var m) && m.GetString() == "D1X-B")
            .Select(op => op.Fields.GetProperty("startMm")[1].GetDouble())
            .ToList();
        if (tops.Count != 2 || tops.Any(y => Math.Abs(y + 250) > 2.5))
        {
            message = $"beam one-face wall top: expected 2 spans at y=-250, got [{string.Join(",", tops)}]";
            return false;
        }

        // (vi) 30° beam in two CAD pieces → one main span between rotated column centers.
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
        if (diagonal.Count != 1 || !Has(diagonal, BeamSpanKind.Main, c0.X, c0.Y, c1.X, c1.Y))
        {
            message = $"beam 30° merge: expected one main ({c0.X:0},{c0.Y:0})-({c1.X:0},{c1.Y:0}), got {Describe(diagonal)}";
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
