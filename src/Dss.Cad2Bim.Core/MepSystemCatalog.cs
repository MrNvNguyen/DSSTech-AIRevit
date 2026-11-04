using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

public enum MepDomain
{
    Duct,
    Pipe,
    CableTray
}

/// <summary>One system of the company colour rules (QUY ĐỊNH VỀ MÀU SẮC HỆ THỐNG KỸ THUẬT).</summary>
public sealed class MepSystemSpec
{
    public MepSystemSpec(string abbr, string nameVi, string nameEn, int r, int g, int b, MepDomain domain, string classification, string group)
    {
        Abbr = abbr;
        NameVi = nameVi;
        NameEn = nameEn;
        R = r;
        G = g;
        B = b;
        Domain = domain;
        Classification = classification;
        Group = group;
    }

    public string Abbr { get; }
    public string NameVi { get; }
    public string NameEn { get; }
    public int R { get; }
    public int G { get; }
    public int B { get; }
    public MepDomain Domain { get; }
    /// <summary>Revit MEPSystemClassification name (SupplyAir, DomesticColdWater …).</summary>
    public string Classification { get; }
    /// <summary>Discipline tool the system belongs to: Cơ, Điện, Nước, PCCC.</summary>
    public string Group { get; }
    public string TypeName => $"{Abbr} - {NameEn}";
}

/// <summary>An existing system type in the model, as the matcher sees it.</summary>
public sealed class ExistingSystem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Abbreviation { get; set; } = "";
    public MepDomain Domain { get; set; }
    public string Classification { get; set; } = "";
}

public enum SystemMatchKind
{
    /// <summary>Same abbreviation (or the abbreviation as a word of the name): it is this system.</summary>
    Same,
    /// <summary>Name says the same system in other words (Supply Air, gió cấp): very likely this system.</summary>
    Similar,
    None
}

public static class MepSystemCatalog
{
    public const string Mech = "Cơ";
    public const string Elec = "Điện";
    public const string Plumb = "Nước";
    public const string Fire = "PCCC";

    public static readonly IReadOnlyList<MepSystemSpec> All = new List<MepSystemSpec>
    {
        // ACMV
        new("SAD", "Ống gió cấp", "Supply Air", 0, 127, 255, MepDomain.Duct, "SupplyAir", Mech),
        new("RAD", "Ống gió hồi", "Return Air", 0, 255, 0, MepDomain.Duct, "ReturnAir", Mech),
        new("OAD", "Ống gió ngoài trời", "Outside Air", 255, 128, 128, MepDomain.Duct, "SupplyAir", Mech),
        new("FAD", "Ống gió tươi", "Fresh Air", 128, 255, 255, MepDomain.Duct, "SupplyAir", Mech),
        new("EAD", "Ống gió thải chung", "Exhaust Air", 128, 0, 255, MepDomain.Duct, "ExhaustAir", Mech),
        new("KEAD", "Ống gió thải bếp", "Kitchen Exhaust Air", 210, 105, 30, MepDomain.Duct, "ExhaustAir", Mech),
        new("TEAD", "Ống gió thải toilet", "Toilet Exhaust Air", 129, 64, 0, MepDomain.Duct, "ExhaustAir", Mech),
        new("SEAD", "Ống hút khói", "Smoke Exhaust Air", 190, 100, 0, MepDomain.Duct, "ExhaustAir", Mech),
        new("PAD", "Ống gió tạo áp", "Pressure Air", 0, 104, 78, MepDomain.Duct, "SupplyAir", Mech),
        new("CHWS", "Cấp nước Chiller", "Chilled Water Supply", 63, 255, 0, MepDomain.Pipe, "SupplyHydronic", Mech),
        new("CHWR", "Hồi nước Chiller", "Chilled Water Return", 0, 255, 191, MepDomain.Pipe, "ReturnHydronic", Mech),
        new("CTS", "Cấp nước tháp giải nhiệt", "Cooling Tower Supply", 125, 0, 255, MepDomain.Pipe, "SupplyHydronic", Mech),
        new("CTR", "Hồi nước tháp giải nhiệt", "Cooling Tower Return", 115, 115, 185, MepDomain.Pipe, "ReturnHydronic", Mech),
        new("EP", "Ống cân bằng tháp giải nhiệt", "Equalizer Pipe", 64, 128, 128, MepDomain.Pipe, "OtherPipe", Mech),
        new("CD", "Thoát nước ngưng", "Condensate Drain", 191, 255, 0, MepDomain.Pipe, "Sanitary", Mech),
        new("GAS", "Ống gas cấp và hồi", "Refrigerant Gas", 193, 193, 0, MepDomain.Pipe, "OtherPipe", Mech),
        // Điện (máng cáp): no Revit system type; colour goes with the tray's Service Type.
        new("BW", "Busway", "Busway", 255, 191, 0, MepDomain.CableTray, "", Elec),
        new("MV", "Nguồn trung thế", "Medium Voltage", 255, 85, 0, MepDomain.CableTray, "", Elec),
        new("LV", "Nguồn hạ thế", "Low Voltage", 192, 192, 192, MepDomain.CableTray, "", Elec),
        new("ELV", "Điện nhẹ, TTLL", "Extra Low Voltage", 175, 238, 238, MepDomain.CableTray, "", Elec),
        new("FA", "Báo cháy", "Fire Alarm", 255, 127, 80, MepDomain.CableTray, "", Elec),
        // Cấp thoát nước
        new("CWS", "Cấp nước lạnh sinh hoạt", "Cold Water Supply", 0, 128, 255, MepDomain.Pipe, "DomesticColdWater", Plumb),
        new("HWS", "Cấp nước nóng sinh hoạt", "Hot Water Supply", 255, 0, 128, MepDomain.Pipe, "DomesticHotWater", Plumb),
        new("HWR", "Hồi nước nóng sinh hoạt", "Hot Water Return", 255, 100, 255, MepDomain.Pipe, "DomesticHotWater", Plumb),
        new("BWS", "Thoát thải đen", "Black Sewage", 160, 0, 8, MepDomain.Pipe, "Sanitary", Plumb),
        new("GWS", "Thoát thải xám", "Grey Sewage", 255, 127, 0, MepDomain.Pipe, "Sanitary", Plumb),
        new("KP", "Thoát nước bếp", "Kitchen Drainage", 128, 0, 128, MepDomain.Pipe, "Sanitary", Plumb),
        new("RWP", "Thoát nước mưa", "Rainwater", 0, 255, 255, MepDomain.Pipe, "Sanitary", Plumb),
        new("VP", "Thông hơi", "Vent", 135, 206, 235, MepDomain.Pipe, "Vent", Plumb),
        // PCCC
        new("SP", "Chữa cháy Sprinkler", "Sprinkler", 255, 0, 0, MepDomain.Pipe, "FireProtectWet", Fire),
        new("FFH", "Chữa cháy vách tường", "Fire Hydrant", 255, 0, 0, MepDomain.Pipe, "FireProtectWet", Fire),
        new("FDR", "Chữa cháy màn ngăn", "Firefighting Drencher", 255, 0, 191, MepDomain.Pipe, "FireProtectPreaction", Fire),
        new("FFD", "Chữa cháy khô", "Firefighting Dry", 255, 170, 234, MepDomain.Pipe, "FireProtectDry", Fire),
        new("FFP", "Chữa cháy dạng khí", "Firefighting Pneumatic", 189, 0, 141, MepDomain.Pipe, "FireProtectOther", Fire),
        new("FDS", "Thoát nước chữa cháy", "Fire Drainage", 64, 0, 128, MepDomain.Pipe, "Sanitary", Fire)
    };

    /// <summary>
    /// The existing system type that already is this system, best first: same abbreviation (or the abbreviation as
    /// a word of the name) beats a name that says the same thing (Supply Air, Cấp gió). Only the same domain counts.
    /// </summary>
    public static (SystemMatchKind Kind, ExistingSystem? System) Match(MepSystemSpec spec, IEnumerable<ExistingSystem> existing)
    {
        var pool = existing.Where(e => e.Domain == spec.Domain).ToList();
        var same = pool.FirstOrDefault(e => Words(e.Abbreviation).Contains(spec.Abbr) || Words(e.Name).Contains(spec.Abbr));
        if (same is not null)
            return (SystemMatchKind.Same, same);
        var en = Plain(spec.NameEn);
        var vi = Plain(spec.NameVi);
        var similar = pool.FirstOrDefault(e =>
        {
            var name = Plain(e.Name);
            return name.Contains(en) || name.Contains(vi) || Reordered(vi).Any(name.Contains);
        });
        return similar is not null ? (SystemMatchKind.Similar, similar) : (SystemMatchKind.None, null);
    }

    /// <summary>
    /// Material name by the BIM rule «Vai trò/Bộ môn – Loại/Category – Thông tin bổ sung»: the discipline code of
    /// table 1.8 (EM điều hòa thông gió, EP cấp thoát nước, EF chữa cháy, EE điện, EC thông tin liên lạc), MAT,
    /// then the system code and English name: "EM-MAT-SAD Supply Air", "EF-MAT-SP Sprinkler".
    /// </summary>
    public static string MaterialName(MepSystemSpec spec) => $"{Role(spec)}-MAT-{spec.Abbr} {spec.NameEn}";

    /// <summary>Names this tool gave the system's material before: renamed to <see cref="MaterialName"/>.</summary>
    public static IEnumerable<string> OldMaterialNames(MepSystemSpec spec)
    {
        yield return $"DSS-{spec.Abbr}";
        yield return $"{Role(spec)}-{spec.Domain}-{spec.Abbr}";
    }

    private static string Role(MepSystemSpec spec) => spec.Group switch
    {
        Mech => "EM",
        Plumb => "EP",
        Fire => "EF",
        _ => spec.Abbr switch { "ELV" => "EC", "FA" => "EF", _ => "EE" }
    };

    /// <summary>
    /// An existing system's name with its code overwritten by the standard one, so the model reads the same as the
    /// rules: "M-KEA-Kitchen Exhaust Air" → "M-KEAD-Kitchen Exhaust Air". The old code is the system's abbreviation
    /// where it stands as a word of the name, else the code in a "M-XXX-…" prefix. Names without a code, or already
    /// holding the standard one, stay as they are.
    /// </summary>
    public static string SyncName(string name, string? oldAbbr, string newAbbr)
    {
        if (string.IsNullOrWhiteSpace(name) || Words(name).Contains(Plain(newAbbr)))
            return name;
        if (!string.IsNullOrWhiteSpace(oldAbbr))
        {
            var word = new Regex($@"(?<![A-Za-z0-9]){Regex.Escape(oldAbbr!.Trim())}(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
            if (word.IsMatch(name))
                return word.Replace(name, newAbbr, 1);
        }

        var prefix = Regex.Match(name, @"^([A-Za-z])-([A-Za-z0-9]+)-");
        return prefix.Success
            ? name.Substring(0, prefix.Groups[2].Index) + newAbbr + name.Substring(prefix.Groups[2].Index + prefix.Groups[2].Length)
            : name;
    }

    /// <summary>"ONG GIO CAP" also read as "CAP GIO" (Vietnamese names come in both orders).</summary>
    private static IEnumerable<string> Reordered(string plainVi)
    {
        var words = plainVi.Split(' ').Where(w => w.Length > 0 && w != "ONG" && w != "HE").ToList();
        if (words.Count >= 2)
        {
            yield return string.Join(" ", words);
            yield return string.Join(" ", Enumerable.Reverse(words));
        }
    }

    private static HashSet<string> Words(string? value) =>
        new(Regex.Split(Plain(value ?? ""), "[^A-Z0-9]+").Where(w => w.Length > 0));

    /// <summary>Upper case, no accents, single spaces: "Ống gió cấp" → "ONG GIO CAP".</summary>
    public static string Plain(string value)
    {
        var d = value.ToUpperInvariant().Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>The system a CAD layer names by its abbreviation (M-SAD-600x400 → SAD), longest code first.</summary>
    public static MepSystemSpec? FromLayer(string? layer, MepDomain domain)
    {
        var words = Words(layer);
        return All.Where(s => s.Domain == domain && words.Contains(s.Abbr)).OrderByDescending(s => s.Abbr.Length).FirstOrDefault();
    }
}
