using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dss.McpContracts;

namespace Dss.Cad2Bim.Core;

public static class MepfIntentEngine
{
    public static IReadOnlyList<CreateOperation> Plan(CadGeometry geom, LayerMap map, string levelName)
    {
        var ops = new List<CreateOperation>();

        foreach (var entity in geom.Entities)
        {
            var rule = map.Match(entity.Layer ?? "");
            if (rule is null)
                continue;

            switch (rule.Kind)
            {
                case "Pipe":
                    if (entity is CadLine pipe)
                    {
                        var diameter = ParseInt(entity.Layer, @"D(\d+)") ?? 50;
                        var system = ParseSystem(entity.Layer);
                        ops.Add(Op("Pipes", new
                        {
                            startMm = new[] { pipe.X0, pipe.Y0, 300 },
                            endMm = new[] { pipe.X1, pipe.Y1, 300 },
                            level = levelName,
                            diameterMm = diameter,
                            systemType = system,
                            pipeType = "Standard"
                        }));
                    }
                    break;
                case "Duct":
                    if (entity is CadLine duct)
                    {
                        var w = ParseInt(entity.Layer, @"(\d+)x") ?? 400;
                        var h = ParseInt(entity.Layer, @"x(\d+)") ?? 200;
                        var system = entity.Layer.IndexOf("RET", StringComparison.OrdinalIgnoreCase) >= 0 ? "Return Air"
                            : entity.Layer.IndexOf("EXH", StringComparison.OrdinalIgnoreCase) >= 0 ? "Exhaust Air"
                            : "Supply Air";
                        ops.Add(Op("Ducts", new
                        {
                            startMm = new[] { duct.X0, duct.Y0, 2700 },
                            endMm = new[] { duct.X1, duct.Y1, 2700 },
                            level = levelName,
                            widthMm = w,
                            heightMm = h,
                            systemType = system,
                            ductType = "Rectangular Duct"
                        }));
                    }
                    break;
                case "CableTray":
                    if (entity is CadLine tray)
                    {
                        var width = ParseInt(entity.Layer, @"E-TRAY-(\d+)") ?? 300;
                        ops.Add(Op("CableTrays", new
                        {
                            startMm = new[] { tray.X0, tray.Y0, 2800 },
                            endMm = new[] { tray.X1, tray.Y1, 2800 },
                            level = levelName,
                            widthMm = width
                        }));
                    }
                    break;
                case "MechanicalEquipment":
                case "PlumbingEquipment":
                case "ElectricalEquipment":
                case "FireEquipment":
                case "Sprinkler":
                case "LightingFixture":
                    if (entity is CadInsert ins)
                    {
                        var category = rule.Kind switch
                        {
                            "LightingFixture" => "LightingFixtures",
                            "Sprinkler" => "Sprinklers",
                            "ElectricalEquipment" => "ElectricalEquipment",
                            "PlumbingEquipment" => "PlumbingFixtures",
                            _ => "MechanicalEquipment"
                        };
                        ops.Add(Op(category, new
                        {
                            xyzMm = new[] { ins.X, ins.Y, 0 },
                            familyType = ins.BlockName,
                            level = levelName,
                            rotationDeg = ins.RotationDeg
                        }));
                    }
                    break;
            }
        }

        return ops;
    }

    private static string ParseSystem(string layer)
    {
        if (layer.IndexOf("CW", StringComparison.OrdinalIgnoreCase) >= 0) return "Domestic Cold Water";
        if (layer.IndexOf("HW", StringComparison.OrdinalIgnoreCase) >= 0) return "Domestic Hot Water";
        if (layer.IndexOf("SAN", StringComparison.OrdinalIgnoreCase) >= 0) return "Sanitary";
        if (layer.IndexOf("DR", StringComparison.OrdinalIgnoreCase) >= 0) return "Drainage";
        if (layer.IndexOf("GAS", StringComparison.OrdinalIgnoreCase) >= 0) return "Gas";
        if (layer.StartsWith("F-", StringComparison.OrdinalIgnoreCase)) return "Fire Protection";
        return "Domestic Cold Water";
    }

    private static int? ParseInt(string layer, string pattern)
    {
        var m = Regex.Match(layer ?? "", pattern, RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : null;
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
