using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Dss.Cad2Bim.Core;

public enum DeviceMount
{
    Wall,
    Ceiling,
    /// <summary>Air-conditioning devices: at the ceiling (or a typed height), joined to the ducts by flex duct.</summary>
    Hvac
}

public enum DeviceKind
{
    Socket,
    Switch,
    WallLight,
    SplitAc,
    CeilingLight,
    Sprinkler,
    FireAlarm,
    AirTerminal,
    FanCoil,
    AirBox
}

/// <summary>One kind of device: where it mounts, its Revit category and the default height.</summary>
public sealed class DeviceSpec
{
    public DeviceSpec(DeviceKind kind, DeviceMount mount, string label, string category, double defaultMm, string[] words, string[] tokens)
    {
        Kind = kind;
        Mount = mount;
        Label = label;
        Category = category;
        DefaultMm = defaultMm;
        Words = words;
        Tokens = tokens;
    }

    public DeviceKind Kind { get; }
    public DeviceMount Mount { get; }
    public string Label { get; }
    /// <summary>Revit BuiltInCategory name (OST_…).</summary>
    public string Category { get; }
    /// <summary>Wall: mounting height above the level. Ceiling, Hvac: distance below the ceiling (Hvac: negative = above it).</summary>
    public double DefaultMm { get; }
    /// <summary>Phrases found anywhere in a block or layer name (unaccented, upper case).</summary>
    public string[] Words { get; }
    /// <summary>Short codes matched only as a whole token (OC1 → OC, 1).</summary>
    public string[] Tokens { get; }
}

/// <summary>The blocks of one name on the plan, with the device kind its name suggests.</summary>
public sealed class DeviceBlockGroup
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public List<string> Layers { get; } = new List<string>();
    public DeviceKind? Guess { get; set; }
}

/// <summary>Where a wall device lands: on the wall face on its side of the wall, facing out.</summary>
public sealed class WallSnap
{
    public string WallId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Unit normal of the face, pointing away from the wall toward the room.</summary>
    public double NormalX { get; set; }
    public double NormalY { get; set; }
    /// <summary>Gap from the block point to the face (negative inside the wall).</summary>
    public double GapMm { get; set; }
}

/// <summary>
/// Devices drawn as blocks on the CAD plan: the kind from the block (or layer) name, Vietnamese or English, and
/// the wall face a wall device sits on. Heights follow common Vietnamese practice; the dialog lets them change.
/// </summary>
public static class DevicePlanner
{
    public const double SnapMm = 5;

    // Most specific first: "ĐÈN ỐP TƯỜNG" is a wall light before it is a light; a detector is not a sprinkler.
    public static readonly IReadOnlyList<DeviceSpec> Specs = new[]
    {
        new DeviceSpec(DeviceKind.Sprinkler, DeviceMount.Ceiling, "Đầu phun sprinkler", "OST_Sprinklers", 0,
            new[] { "SPRINKLER", "SPRINK", "DAU PHUN", "DAUPHUN", "PENDENT", "UPRIGHT" }, new[] { "SPK", "SPR" }),
        new DeviceSpec(DeviceKind.FireAlarm, DeviceMount.Ceiling, "Đầu báo cháy", "OST_FireAlarmDevices", 0,
            new[] { "BAO KHOI", "BAOKHOI", "BAO NHIET", "BAONHIET", "BAO CHAY", "BAOCHAY", "DAU BAO", "SMOKE", "HEAT", "DETECTOR", "DETECT" },
            new[] { "FA", "SD", "HD", "BK", "BN" }),
        // Air conditioning: a box and a fan coil before the grilles they feed.
        new DeviceSpec(DeviceKind.AirBox, DeviceMount.Hvac, "Box gió (VAV / CAV / plenum)", "OST_MechanicalEquipment", -200,
            new[] { "BOX GIO", "BOXGIO", "HOP GIO", "HOPGIO", "PLENUM", "VAV BOX", "CAV BOX", "VARIABLE AIR" }, new[] { "VAV", "CAV", "PB" }),
        new DeviceSpec(DeviceKind.FanCoil, DeviceMount.Hvac, "FCU / AHU treo trần", "OST_MechanicalEquipment", -100,
            new[] { "FAN COIL", "FANCOIL", "CASSETTE", "DAN LANH AM TRAN", "AIR HANDLING" }, new[] { "FCU", "AHU", "PAU", "FCB", "FC" }),
        new DeviceSpec(DeviceKind.AirTerminal, DeviceMount.Hvac, "Miệng gió", "OST_DuctTerminal", 0,
            new[] { "MIENG GIO", "MIENGGIO", "CUA GIO", "CUAGIO", "DIFFUSER", "DIFFUSOR", "GRILLE", "LOUVER", "LOUVRE", "REGISTER", "KHUECH TAN", "SLOT DIFF", "LINEAR SLOT" },
            new[] { "SAD", "RAD", "EAD", "FAD", "SAG", "RAG", "EAG", "OAG", "DIF", "MG", "CG" }),
        new DeviceSpec(DeviceKind.Switch, DeviceMount.Wall, "Công tắc", "OST_LightingDevices", 1250,
            new[] { "CONG TAC", "CONGTAC", "SWITCH" }, new[] { "CT", "SW" }),
        new DeviceSpec(DeviceKind.Socket, DeviceMount.Wall, "Ổ cắm", "OST_ElectricalFixtures", 400,
            new[] { "O CAM", "OCAM", "SOCKET", "OUTLET", "RECEPTACLE", "POWER POINT" }, new[] { "OC", "GPO" }),
        new DeviceSpec(DeviceKind.SplitAc, DeviceMount.Wall, "Điều hòa cục bộ", "OST_MechanicalEquipment", 2500,
            new[] { "DIEU HOA", "DIEUHOA", "MAY LANH", "SPLIT", "AIR CON", "AIRCON", "WALL MOUNTED", "TREO TUONG" }, new[] { "AC", "DH", "IDU" }),
        new DeviceSpec(DeviceKind.WallLight, DeviceMount.Wall, "Đèn ốp tường", "OST_LightingFixtures", 2200,
            new[] { "DEN TUONG", "DEN OP TUONG", "DEN GAN TUONG", "DEN HAT", "WALL LIGHT", "WALL LAMP", "WALLLIGHT", "SCONCE", "BRACKET" }, new[] { "WL" }),
        new DeviceSpec(DeviceKind.CeilingLight, DeviceMount.Ceiling, "Đèn trần", "OST_LightingFixtures", 0,
            new[] { "DEN", "LIGHT", "LAMP", "LUMINAIRE", "DOWNLIGHT", "LED", "HUYNH QUANG", "FLUORESCENT" }, new[] { "DL" })
    };

    public static DeviceSpec Spec(DeviceKind kind) => Specs.First(s => s.Kind == kind);

    /// <summary>The device kind a block name says (else its layer's); null when neither names a device.</summary>
    public static DeviceKind? Guess(string blockName, string layer)
    {
        foreach (var name in new[] { blockName, layer })
        {
            var text = Plain(name);
            if (text.Length == 0)
                continue;
            var tokens = new HashSet<string>(Tokens(text), StringComparer.Ordinal);
            var padded = " " + text + " ";
            foreach (var spec in Specs)
            {
                if (spec.Words.Any(w => Has(text, padded, w) || tokens.Contains(w)) || spec.Tokens.Any(tokens.Contains))
                    return spec.Kind;
            }
        }

        return null;
    }

    /// <summary>Blocks grouped by name, most numerous first.</summary>
    public static List<DeviceBlockGroup> Groups(IEnumerable<CadInsert> inserts)
    {
        var groups = new Dictionary<string, DeviceBlockGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var ins in inserts)
        {
            var name = ins.BlockName ?? "";
            if (!groups.TryGetValue(name, out var g))
            {
                g = new DeviceBlockGroup { Name = name };
                groups[name] = g;
            }

            g.Count++;
            if (!g.Layers.Contains(ins.Layer))
                g.Layers.Add(ins.Layer);
        }

        foreach (var g in groups.Values)
            g.Guess = Guess(g.Name, g.Layers.FirstOrDefault() ?? "");
        return groups.Values.OrderBy(g => g.Guess is null ? 1 : 0).ThenByDescending(g => g.Count).ThenBy(g => g.Name).ToList();
    }

    /// <summary>
    /// The wall face nearest the block, within <paramref name="reachMm"/> of it, on the block's side of the wall.
    /// The point is the block's foot on that face.
    /// </summary>
    public static WallSnap? SnapToWall(double x, double y, IEnumerable<HostWallAxis> walls, double reachMm)
    {
        WallSnap? best = null;
        foreach (var w in walls)
        {
            var dx = w.X1 - w.X0;
            var dy = w.Y1 - w.Y0;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1)
                continue;
            var ux = dx / len;
            var uy = dy / len;
            var t = (x - w.X0) * ux + (y - w.Y0) * uy;
            if (t < 0 || t > len)
                continue;
            // Normal to the left of the axis; the block's side decides the face.
            double nx = -uy, ny = ux;
            var d = (x - w.X0) * nx + (y - w.Y0) * ny;
            var side = d >= 0 ? 1 : -1;
            var half = w.ThicknessMm / 2;
            var gap = Math.Abs(d) - half;
            if (gap > reachMm || (best is not null && Math.Abs(gap) >= Math.Abs(best.GapMm)))
                continue;
            best = new WallSnap
            {
                WallId = w.Id,
                X = w.X0 + ux * t + nx * side * half,
                Y = w.Y0 + uy * t + ny * side * half,
                NormalX = nx * side,
                NormalY = ny * side,
                GapMm = gap
            };
        }

        return best;
    }

    public static double Snap(double mm) => Math.Round(mm / SnapMm) * SnapMm;

    /// <summary>Upper case without Vietnamese accents; anything not a letter or digit becomes a space.</summary>
    public static string Plain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var sb = new StringBuilder();
        foreach (var c in text!.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// A long single word may sit inside another ("DOWNLIGHT" in "DL_DOWNLIGHT2"); short words and phrases must
    /// stand alone, so "VIDEO CAMERA" is not "O CAM" and "DENSITY" is not "DEN".
    /// </summary>
    private static bool Has(string text, string padded, string word) =>
        word.Length >= 6 && word.IndexOf(' ') < 0 ? text.Contains(word) : padded.Contains(" " + word + " ");

    /// <summary>Words split again where letters meet digits: "OC1A" → OC, 1, A.</summary>
    private static IEnumerable<string> Tokens(string plain)
    {
        foreach (var word in plain.Split(' '))
        {
            var start = 0;
            for (var i = 1; i <= word.Length; i++)
            {
                if (i == word.Length || char.IsDigit(word[i]) != char.IsDigit(word[i - 1]))
                {
                    yield return word.Substring(start, i - start);
                    start = i;
                }
            }
        }
    }
}
