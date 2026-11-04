using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dss.McpContracts;

namespace Dss.Cad2Bim.Core;

/// <summary>A door or window already in the model, with what its lintel and stiffeners depend on (model mm).</summary>
public sealed class HostedOpening
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Unit direction of the host wall.</summary>
    public double DirX { get; set; }
    public double DirY { get; set; }
    public double WidthMm { get; set; }
    /// <summary>Head height above the level (top of the opening).</summary>
    public double HeadMm { get; set; }
    public string WallId { get; set; } = "";
    public double WallThicknessMm { get; set; }
    /// <summary>Host wall ends along its direction, measured from the opening center (start &lt; 0 &lt; end).</summary>
    public double WallStartMm { get; set; }
    public double WallEndMm { get; set; }
}

/// <summary>
/// Lintels over openings in masonry walls and stiffener columns beside wide ones (TCVN practice):
/// lintel B + 2×200 long, 200 deep, wall-thick, its bottom on the opening head; stiffeners 200 × wall thickness on
/// both sides of openings from 1500 wide, full storey height. Lintels of openings close together become one.
/// </summary>
public static class LintelPlanner
{
    public const double BearingMm = 200;
    public const double LintelDepthMm = 200;
    public const double StiffenerMinOpeningMm = 1500;
    public const double StiffenerSizeMm = 200;

    private sealed class Lintel
    {
        public Lintel(HostedOpening o, double a, double b, double top)
        {
            Opening = o;
            A = a;
            B = b;
            Top = top;
        }

        public HostedOpening Opening { get; }
        /// <summary>Ends along the wall line, from the first opening's center.</summary>
        public double A { get; set; }
        public double B { get; set; }
        public double Top { get; set; }
    }

    public static IReadOnlyList<CreateOperation> Plan(
        IReadOnlyList<HostedOpening> openings,
        string baseLevel,
        string topLevel,
        string? beamFamily,
        string? columnFamily,
        Action<string>? log = null)
    {
        var ops = new List<CreateOperation>();
        var lintels = new List<Lintel>();
        foreach (var o in openings.Where(o => o.WidthMm > 0 && o.WallThicknessMm > 0))
        {
            var half = o.WidthMm / 2 + BearingMm;
            // A lintel bears on the wall: it stops at the wall's ends.
            var a = Math.Max(-half, o.WallStartMm);
            var b = Math.Min(half, o.WallEndMm);
            var top = o.HeadMm + LintelDepthMm;
            var same = lintels.FirstOrDefault(l => l.Opening.WallId == o.WallId && Math.Abs(l.Top - top) < 1 &&
                                                   Overlaps(l, o, a, b));
            if (same is not null)
            {
                var shift = Along(same.Opening, o);
                same.A = Math.Min(same.A, shift + a);
                same.B = Math.Max(same.B, shift + b);
                log?.Invoke($"lintel {o.Id} joins the lintel of {same.Opening.Id}");
                continue;
            }

            lintels.Add(new Lintel(o, a, b, top));
        }

        foreach (var l in lintels)
        {
            var o = l.Opening;
            var width = Math.Round(o.WallThicknessMm / 10) * 10;
            ops.Add(Op("StructuralFraming", new
            {
                startMm = new[] { o.X + o.DirX * l.A, o.Y + o.DirY * l.A, 0 },
                endMm = new[] { o.X + o.DirX * l.B, o.Y + o.DirY * l.B, 0 },
                level = baseLevel,
                topOffsetMm = l.Top,
                familyType = $"{width:0}x{LintelDepthMm:0}",
                widthMm = width,
                depthMm = LintelDepthMm,
                sourceFamily = beamFamily ?? "",
                widthParam = "",
                heightParam = "",
                material = "",
                mark = "LT",
                exactMm = true
            }, skipIfExists: true));
        }

        var placed = new List<(double X, double Y)>();
        foreach (var o in openings.Where(o => o.WidthMm >= StiffenerMinOpeningMm && o.WallThicknessMm > 0))
        {
            var depth = Math.Round(o.WallThicknessMm / 10) * 10;
            var rotation = Math.Atan2(o.DirY, o.DirX) * 180 / Math.PI;
            foreach (var side in new[] { -1, 1 })
            {
                var s = side * (o.WidthMm / 2 + StiffenerSizeMm / 2);
                // No room past the wall's end: a column or the next wall takes the load there.
                if (s - StiffenerSizeMm / 2 < o.WallStartMm - 1 || s + StiffenerSizeMm / 2 > o.WallEndMm + 1)
                {
                    log?.Invoke($"stiffener skip {o.Id} side {side}: past the wall end");
                    continue;
                }

                var x = o.X + o.DirX * s;
                var y = o.Y + o.DirY * s;
                if (placed.Any(p => Math.Abs(p.X - x) < StiffenerSizeMm && Math.Abs(p.Y - y) < StiffenerSizeMm))
                    continue;
                placed.Add((x, y));
                ops.Add(Op("StructuralColumns", new
                {
                    xyzMm = new[] { x, y, 0 },
                    familyType = $"{StiffenerSizeMm:0}x{depth:0}",
                    widthMm = StiffenerSizeMm,
                    depthMm = depth,
                    rotationDeg = rotation,
                    baseLevel,
                    topLevel,
                    baseOffsetMm = 0,
                    topOffsetMm = 0,
                    sourceFamily = columnFamily ?? "",
                    widthParam = "",
                    heightParam = "",
                    material = "",
                    sectionShape = "",
                    mark = "BT",
                    rotatedFrame = Math.Abs(Math.IEEERemainder(rotation, 90)) > 0.01
                }, skipIfExists: true));
            }
        }

        return ops;
    }

    private static double Along(HostedOpening from, HostedOpening to) =>
        (to.X - from.X) * from.DirX + (to.Y - from.Y) * from.DirY;

    private static bool Overlaps(Lintel l, HostedOpening o, double a, double b)
    {
        var shift = Along(l.Opening, o);
        return shift + a <= l.B + 1 && shift + b >= l.A - 1;
    }

    private static CreateOperation Op(string category, object fields, bool skipIfExists)
    {
        var json = JsonSerializer.Serialize(fields);
        using var doc = JsonDocument.Parse(json);
        return new CreateOperation
        {
            OpId = Guid.NewGuid().ToString("N").Substring(0, 8),
            Category = category,
            Fields = doc.RootElement.Clone(),
            SkipIfExists = skipIfExists
        };
    }
}
