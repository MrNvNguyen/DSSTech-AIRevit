using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dss.McpContracts;

namespace Dss.Cad2Bim.Core;

public static class ArchIntentEngine
{
    public static IReadOnlyList<CreateOperation> Plan(CadGeometry geom, LayerMap map, string levelName)
    {
        var walls = new List<CreateOperation>();
        var doors = new List<CreateOperation>();
        var windows = new List<CreateOperation>();
        var rooms = new List<CreateOperation>();
        var floors = new List<CreateOperation>();

        foreach (var entity in geom.Entities)
        {
            var rule = map.Match(entity.Layer ?? "");
            if (rule is null)
                continue;

            switch (rule.Kind)
            {
                case "ArchWall":
                    if (entity is CadLine line)
                        walls.Add(Op("Walls", new
                        {
                            startMm = new[] { line.X0, line.Y0, 0 },
                            endMm = new[] { line.X1, line.Y1, 0 },
                            wallType = ParseWallType(entity.Layer),
                            level = levelName,
                            heightMm = 3000,
                            structural = false
                        }));
                    break;
                case "Door":
                    if (entity is CadInsert ins)
                        doors.Add(Op("Doors", new
                        {
                            xyzMm = new[] { ins.X, ins.Y, 0 },
                            familyType = entity.Layer,
                            level = levelName,
                            rotationDeg = ins.RotationDeg
                        }));
                    break;
                case "Window":
                    if (entity is CadInsert win)
                        windows.Add(Op("Windows", new
                        {
                            xyzMm = new[] { win.X, win.Y, 0 },
                            familyType = entity.Layer,
                            level = levelName
                        }));
                    break;
                case "Room":
                    if (entity is CadPolyline pl && pl.Closed && pl.Points.Count >= 3)
                    {
                        var cx = pl.Points.Average(p => p.X);
                        var cy = pl.Points.Average(p => p.Y);
                        rooms.Add(Op("Rooms", new { xyzMm = new[] { cx, cy, 0 }, level = levelName, name = "Room" }));
                    }
                    break;
                case "FinishFloor":
                    if (entity is CadPolyline fl && fl.Closed && fl.Points.Count >= 3)
                    {
                        var loops = fl.Points.Select(p => new[] { p.X, p.Y, 0.0 }).ToList();
                        floors.Add(Op("Floors", new
                        {
                            loops = new[] { loops },
                            level = levelName,
                            floorType = "Generic - 150mm",
                            structural = false
                        }));
                    }
                    break;
            }
        }

        var ops = new List<CreateOperation>();
        ops.AddRange(walls);
        ops.AddRange(doors);
        ops.AddRange(windows);
        ops.AddRange(rooms);
        ops.AddRange(floors);
        return ops;
    }

    private static string ParseWallType(string layer)
    {
        var m = Regex.Match(layer ?? "", @"A-WALL-(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? $"Generic - {m.Groups[1].Value}mm" : "Generic - 200mm";
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
