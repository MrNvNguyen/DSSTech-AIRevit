using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dss.McpContracts;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Structure intent → CreateOperation[]. Does not call Revit API.
/// </summary>
public static class StructureIntentEngine
{
    public static IReadOnlyList<CreateOperation> Plan(CadGeometry geom, LayerMap map, string levelName)
    {
        var ops = new List<CreateOperation>();
        var columns = new List<CreateOperation>();
        var walls = new List<CreateOperation>();
        var beams = new List<CreateOperation>();
        var slabs = new List<CreateOperation>();

        foreach (var entity in geom.Entities)
        {
            var rule = map.Match(entity.Layer ?? "");
            if (rule is null)
                continue;

            switch (rule.Kind)
            {
                case "Column":
                    if (TryColumn(entity, rule, levelName, out var col))
                        columns.Add(col!);
                    break;
                case "StructuralWall":
                    if (entity is CadLine line && TryWall(line, rule, levelName, out var wall))
                        walls.Add(wall!);
                    break;
                case "Beam":
                    // Pilot: beams need Host StructuralFraming — emit as note via failed-friendly wall skip for now
                    // Keep as CreateOperation Category StructuralFraming for later Host catalog.
                    if (entity is CadLine beamLine)
                        beams.Add(Op("StructuralFraming", new
                        {
                            startMm = new[] { beamLine.X0, beamLine.Y0, 0 },
                            endMm = new[] { beamLine.X1, beamLine.Y1, 0 },
                            level = levelName,
                            familyType = ParseBeamType(entity.Layer)
                        }));
                    break;
                case "Floor":
                    if (entity is CadPolyline pl && pl.Closed && pl.Points.Count >= 3)
                    {
                        var ring = pl.Points.Select(p => new[] { p.X, p.Y, 0.0 }).ToList();
                        slabs.Add(Op("Floors", new
                        {
                            loops = new[] { ring },
                            level = levelName,
                            floorType = ParseSlabType(entity.Layer),
                            structural = true
                        }));
                    }
                    break;
            }
        }

        ops.AddRange(columns);
        ops.AddRange(walls);
        ops.AddRange(beams);
        ops.AddRange(slabs);
        return ops;
    }

    private static bool TryColumn(CadEntity entity, LayerRule rule, string levelName, out CreateOperation? op)
    {
        op = null;
        double x, y;
        if (entity is CadCircle c)
        {
            x = c.Cx;
            y = c.Cy;
        }
        else if (entity is CadInsert ins)
        {
            x = ins.X;
            y = ins.Y;
        }
        else if (entity is CadPolyline pl && pl.Closed && pl.Points.Count >= 3)
        {
            x = pl.Points.Average(p => p.X);
            y = pl.Points.Average(p => p.Y);
        }
        else
            return false;

        op = Op("StructuralColumns", new
        {
            xyzMm = new[] { x, y, 0 },
            familyType = ParseColumnType(entity.Layer),
            baseLevel = levelName
        });
        return true;
    }

    private static bool TryWall(CadLine line, LayerRule rule, string levelName, out CreateOperation? op)
    {
        op = Op("Walls", new
        {
            startMm = new[] { line.X0, line.Y0, 0 },
            endMm = new[] { line.X1, line.Y1, 0 },
            wallType = "Generic - 200mm",
            level = levelName,
            heightMm = 3000,
            structural = true
        });
        return true;
    }

    private static string ParseColumnType(string layer)
    {
        var m = Regex.Match(layer ?? "", @"S-COL-(\d+)x(\d+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return $"{m.Groups[1].Value} x {m.Groups[2].Value}";
        return layer ?? "Column";
    }

    private static string ParseBeamType(string layer)
    {
        var m = Regex.Match(layer ?? "", @"S-BEAM-(\d+)x(\d+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return $"{m.Groups[1].Value}x{m.Groups[2].Value}";
        return layer ?? "Beam";
    }

    private static string ParseSlabType(string layer)
    {
        var m = Regex.Match(layer ?? "", @"S-SLAB-(\d+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return $"Generic - {m.Groups[1].Value}mm";
        return "Generic - 150mm";
    }

    private static CreateOperation Op(string category, object fields)
    {
        var json = JsonSerializer.Serialize(fields);
        using var doc = JsonDocument.Parse(json);
        return new CreateOperation
        {
            OpId = Guid.NewGuid().ToString("N").Substring(0, 8),
            Category = category,
            Fields = doc.RootElement.Clone(),
            SkipIfExists = true,
            Units = "mm"
        };
    }
}
