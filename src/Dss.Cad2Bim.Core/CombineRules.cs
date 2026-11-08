using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dss.Cad2Bim.Core;

/// <summary>The MEPF systems Combine tells apart (docs/COMBINE_PLAN.md).</summary>
public enum MepSystemCode
{
    Unknown,
    DR,
    DCT,
    ECT,
    FP,
    CHW,
    CW,
    GAS,
    CND
}

/// <summary>Outcome of a check: PASS, WARNING, FAIL (breaks a mandatory rule) or REVIEW (data missing, needs a person).</summary>
public enum CombineStatus
{
    Pass,
    Warning,
    Fail,
    Review
}

/// <summary>Required gap between two systems, surface to surface (mm).</summary>
public sealed class PairClearance
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public double ParallelMm { get; set; }
    public double CrossingMm { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>
/// Fittings a system may use. Angles are deflections: how far the run turns (a 135° bend in the trade's naming turns
/// the run by 45°); a branch angle is the acute angle between the branch's axis and the main's.
/// </summary>
public sealed class FittingRule
{
    public string RuleId { get; set; } = "";
    public string System { get; set; } = "";
    /// <summary>BEND (elbow in a level run), BRANCH (branch into a level main), DETOUR (bends Combine makes).</summary>
    public string Role { get; set; } = "";
    /// <summary>Largest deflection (BEND, DETOUR) or branch angle (BRANCH) one fitting may make (degrees).</summary>
    public double MaxAngle { get; set; } = 90;
    public double ToleranceDeg { get; set; } = 5;
    public bool Mandatory { get; set; }
    public string Source { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Combine settings: system priority (higher in the list keeps its place, lower gives way), systems never moved,
/// pair clearances, fitting rules. Saved as JSON; default → company → project, each file overriding the one before.
/// </summary>
public sealed class CombineProfile
{
    public string Name { get; set; } = "DSSTech mặc định";
    public string Version { get; set; } = "2.0";
    /// <summary>
    /// Highest priority first: the company system codes (MepSystemCatalog: BWS, SAD, CWS…), plus LPG (fuel gas) and
    /// CND (conduit) outside the list. A group code (DR, DCT…) stands for every system of the group not listed.
    /// </summary>
    public List<string> Priority { get; set; } = new();
    /// <summary>Systems Combine never moves (fuel gas keeps its own corridor).</summary>
    public List<string> Locked { get; set; } = new();
    public double DefaultClearanceMm { get; set; } = 50;
    /// <summary>Gap kept under beams and slabs by «Nâng sát dầm».</summary>
    public double StructureClearanceMm { get; set; } = 50;
    /// <summary>Insulation added round pipes with none modelled (mm per side), by system.</summary>
    public Dictionary<string, double> InsulationMm { get; set; } = new();
    /// <summary>Nothing but sprinkler pipe within this distance under a sprinkler head, nor this far beside it (mm).</summary>
    public double SprinklerClearBelowMm { get; set; } = 450;
    public double SprinklerClearSideMm { get; set; } = 300;
    /// <summary>Room kept clear over a cable tray to pull cables (mm), when stacking a corridor.</summary>
    public double TrayAccessMm { get; set; } = 150;
    /// <summary>Drainage built from CAD: branches join the main with a 45° wye, right-angle bends as two 45° bends.</summary>
    public bool DrainWyes { get; set; } = true;
    public List<PairClearance> Pairs { get; set; } = new();
    public List<FittingRule> Fittings { get; set; } = new();

    public static CombineProfile Default()
    {
        var p = new CombineProfile
        {
            Priority = new List<string>(DefaultPriority),
            Locked = new List<string> { MepSystemClassifier.FuelGas },
            InsulationMm = new Dictionary<string, double> { ["CHWS"] = 25, ["CHWR"] = 25, ["GAS"] = 25 },
            Pairs = new List<PairClearance>
            {
                new() { A = "LPG", B = "ECT", ParallelMm = 400, CrossingMm = 100, Source = "TCVN 9207:2012 mục 6.16 (song song); giao cắt: đề xuất phối hợp BIM" },
                new() { A = "LPG", B = "CND", ParallelMm = 400, CrossingMm = 100, Source = "TCVN 9207:2012 mục 6.16 (song song); giao cắt: đề xuất phối hợp BIM" },
                new() { A = "LPG", B = "*", ParallelMm = 150, CrossingMm = 100, Source = "Đề xuất phối hợp BIM, cần duyệt" },
                new() { A = "DR", B = "ECT", ParallelMm = 150, CrossingMm = 100, Source = "Đề xuất phối hợp BIM, cần duyệt" },
                new() { A = "CW", B = "ECT", ParallelMm = 150, CrossingMm = 100, Source = "Đề xuất phối hợp BIM, cần duyệt" },
                new() { A = "CHW", B = "ECT", ParallelMm = 150, CrossingMm = 100, Source = "Đề xuất phối hợp BIM, cần duyệt" },
                new() { A = "FP", B = "ECT", ParallelMm = 150, CrossingMm = 100, Source = "Đề xuất phối hợp BIM, cần duyệt" },
                new() { A = "DCT", B = "*", ParallelMm = 100, CrossingMm = 50, Source = "Đề xuất phối hợp BIM, cần duyệt" },
                new() { A = "*", B = "*", ParallelMm = 50, CrossingMm = 50, Source = "Đề xuất phối hợp BIM, cần duyệt" }
            },
            Fittings = new List<FittingRule>
            {
                new()
                {
                    RuleId = "DR-BEND-001", System = "DR", Role = "BEND", MaxAngle = 45, Mandatory = true,
                    Source = "Thực hành thoát nước tự chảy; TCVN 4474:1987", Description = "Ống thoát nằm ngang đổi hướng bằng cút 45° (co 135°), không dùng cút 90°"
                },
                new()
                {
                    RuleId = "DR-BRANCH-001", System = "DR", Role = "BRANCH", MaxAngle = 45, Mandatory = true,
                    Source = "TCVN 4474:1987 điều 5.14", Description = "Nhánh thoát nằm ngang nối vào ống chính bằng chữ Y / tê chếch 45°, không dùng tê vuông"
                },
                new()
                {
                    RuleId = "DR-DETOUR-001", System = "DR", Role = "DETOUR", MaxAngle = 0, Mandatory = true,
                    Source = "Giữ độ dốc tự chảy", Description = "Ống thoát không tự đổi tuyến lên xuống (tạo võng, mất dốc)"
                },
                new() { RuleId = "CHW-DETOUR-001", System = "CHW", Role = "DETOUR", MaxAngle = 45, Source = "Đề xuất: giảm tổn thất, dễ bọc bảo ôn", Description = "Né bằng cút 45°" },
                new() { RuleId = "DCT-DETOUR-001", System = "DCT", Role = "DETOUR", MaxAngle = 45, Source = "Đề xuất: giảm tổn thất áp, ồn", Description = "Ống gió né bằng cút 45°" },
                new() { RuleId = "*-DETOUR-001", System = "*", Role = "DETOUR", MaxAngle = 90, Source = "Mặc định", Description = "Né bằng cút 90°" }
            }
        };
        return p;
    }

    /// <summary>
    /// Company systems by group: drainage, ducts, trays, fire, chilled water and refrigerant, domestic water, then
    /// the two outside the company list.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultPriority = new[]
    {
        "BWS", "GWS", "KP", "RWP", "FDS", "CD", "VP",
        "SAD", "RAD", "OAD", "FAD", "EAD", "KEAD", "TEAD", "SEAD", "PAD",
        "BW", "MV", "LV", "ELV", "FA",
        "SP", "FFH", "FDR", "FFD", "FFP",
        "CHWS", "CHWR", "CTS", "CTR", "EP", "GAS",
        "CWS", "HWS", "HWR",
        MepSystemClassifier.ConduitCode, MepSystemClassifier.FuelGas
    };

    /// <summary>
    /// A version 1 file listed groups (DR, DCT…) and called fuel gas "GAS", the company code for refrigerant: each
    /// group becomes its systems in the default order, fuel gas LPG.
    /// </summary>
    private static void Upgrade(CombineProfile p)
    {
        string Fuel(string s) => s == "GAS" ? MepSystemClassifier.FuelGas : s;
        var priority = new List<string>();
        foreach (var entry in p.Priority.Select(Fuel))
        {
            var group = MepSystemClassifier.Parse(entry);
            var codes = group is MepSystemCode.Unknown or MepSystemCode.GAS or MepSystemCode.CND
                ? new[] { entry }
                : DefaultPriority.Where(c => MepSystemClassifier.Family(c) == group).ToArray();
            priority.AddRange(codes.Where(c => !priority.Contains(c)));
        }

        p.Priority = priority;
        p.Locked = p.Locked.Select(Fuel).ToList();
        foreach (var r in p.Pairs)
        {
            r.A = Fuel(r.A);
            r.B = Fuel(r.B);
        }

        foreach (var f in p.Fittings)
            f.System = Fuel(f.System);
        p.Version = "2.0";
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static CombineProfile FromJson(string json) =>
        JsonSerializer.Deserialize<CombineProfile>(json, Json) ?? Default();

    /// <summary>
    /// The default, then each file that exists in turn (company, then project): a file's lists replace the ones
    /// before it, its numbers too. A file that can't be read is skipped and named in <paramref name="errors"/>.
    /// </summary>
    public static CombineProfile Load(IEnumerable<string> files, List<string>? errors = null)
    {
        var profile = Default();
        foreach (var path in files)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                continue;
            try
            {
                var p = FromJson(File.ReadAllText(path, Encoding.UTF8));
                if (p.Version.StartsWith("1.", StringComparison.Ordinal))
                    Upgrade(p);
                profile.Name = p.Name;
                profile.Version = p.Version;
                if (p.Priority.Count > 0)
                    profile.Priority = p.Priority;
                profile.Locked = p.Locked;
                profile.DefaultClearanceMm = p.DefaultClearanceMm;
                profile.StructureClearanceMm = p.StructureClearanceMm;
                profile.InsulationMm = p.InsulationMm;
                profile.SprinklerClearBelowMm = p.SprinklerClearBelowMm;
                profile.SprinklerClearSideMm = p.SprinklerClearSideMm;
                profile.TrayAccessMm = p.TrayAccessMm;
                profile.DrainWyes = p.DrainWyes;
                if (p.Pairs.Count > 0)
                    profile.Pairs = p.Pairs;
                if (p.Fittings.Count > 0)
                    profile.Fittings = p.Fittings;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                errors?.Add($"{path}: {ex.Message}");
            }
        }

        // Systems missing from a hand-edited priority go last, in the default order.
        // (a group code listed stands for its systems).
        foreach (var code in DefaultPriority.Where(c => !profile.Priority.Contains(c) && !profile.Priority.Contains(Key(MepSystemClassifier.Family(c)))))
            profile.Priority.Add(code);
        return profile;
    }

    /// <summary>How a group is written in the rules: fuel gas is LPG (GAS is the company code for refrigerant).</summary>
    public static string Key(MepSystemCode s) => s == MepSystemCode.GAS ? MepSystemClassifier.FuelGas : s.ToString();

    /// <summary>Insulation added to a run with none modelled: by its system code, else its group.</summary>
    public double Insulation(MepSystemCode s, string? code = null) =>
        !string.IsNullOrEmpty(code) && InsulationMm.TryGetValue(code!, out var c) ? c
        : InsulationMm.TryGetValue(Key(s), out var g) ? g : 0;

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
    }

    /// <summary>
    /// 0 is the highest priority: the run's system code where listed, else the first entry of its group (a run
    /// whose system type carries no company code); not listed at all ranks last.
    /// </summary>
    public int Rank(MepSystemCode s, string? code = null)
    {
        var i = string.IsNullOrEmpty(code) ? -1 : Priority.IndexOf(code!);
        if (i < 0)
            i = Priority.FindIndex(p => MepSystemClassifier.Family(p) == s);
        return i < 0 ? Priority.Count : i;
    }

    public bool IsLocked(MepSystemCode s, string? code = null) =>
        (!string.IsNullOrEmpty(code) && Locked.Contains(code!)) || Locked.Contains(Key(s));

    /// <summary>The rule for the pair, exact pair first, then one side with "*", then "*"/"*".</summary>
    public PairClearance Pair(MepSystemCode a, MepSystemCode b)
    {
        string sa = Key(a), sb = Key(b);
        bool Is(PairClearance r, string x, string y) => (r.A == x && r.B == y) || (r.A == y && r.B == x);
        return Pairs.FirstOrDefault(r => Is(r, sa, sb))
               ?? Pairs.FirstOrDefault(r => (r.A == sa || r.A == sb) && r.B == "*")
               ?? Pairs.FirstOrDefault(r => (r.B == sa || r.B == sb) && r.A == "*" && r.B != "*")
               ?? Pairs.FirstOrDefault(r => r.A == "*" && r.B == "*")
               ?? new PairClearance { A = "*", B = "*", ParallelMm = DefaultClearanceMm, CrossingMm = DefaultClearanceMm, Source = "Mặc định" };
    }

    public double Clearance(MepSystemCode a, MepSystemCode b, bool parallel)
    {
        var r = Pair(a, b);
        return parallel ? r.ParallelMm : r.CrossingMm;
    }

    public FittingRule? Fitting(MepSystemCode s, string role) =>
        Fittings.FirstOrDefault(r => r.System == Key(s) && r.Role == role)
        ?? Fittings.FirstOrDefault(r => r.System == "*" && r.Role == role);

    /// <summary>
    /// Which of two clashing runs gives way: the lower priority, unless it is locked (then the other); null when
    /// neither may move. Ties: the smaller run.
    /// </summary>
    public int? Yielder(MepSystemCode a, bool aLocked, double aSizeMm, MepSystemCode b, bool bLocked, double bSizeMm,
        string? aCode = null, string? bCode = null)
    {
        var aFixed = aLocked || IsLocked(a, aCode) || !CanDetour(a);
        var bFixed = bLocked || IsLocked(b, bCode) || !CanDetour(b);
        if (aFixed && bFixed)
            return null;
        if (aFixed)
            return 1;
        if (bFixed)
            return 0;
        var ra = Rank(a, aCode);
        var rb = Rank(b, bCode);
        if (ra != rb)
            return ra > rb ? 0 : 1;
        return aSizeMm <= bSizeMm ? 0 : 1;
    }

    /// <summary>False where the system's DETOUR rule allows no bend (drainage keeps its fall).</summary>
    public bool CanDetour(MepSystemCode s) => (Fitting(s, "DETOUR")?.MaxAngle ?? 90) > 0;

    /// <summary>The bend Combine makes when this system goes under or over (degrees: 45 or 90).</summary>
    public double DetourAngle(MepSystemCode s)
    {
        var a = Fitting(s, "DETOUR")?.MaxAngle ?? 90;
        return a <= 0 ? 0 : a < 67.5 ? 45 : 90;
    }

    /// <summary>A bend in a level run of this system turning by <paramref name="deflectionDeg"/>.</summary>
    public (CombineStatus Status, string Reason) CheckBend(MepSystemCode s, double deflectionDeg)
    {
        var rule = Fitting(s, "BEND");
        if (rule is null || s == MepSystemCode.Unknown)
            return (CombineStatus.Pass, "");
        if (deflectionDeg <= rule.MaxAngle + rule.ToleranceDeg)
            return (CombineStatus.Pass, "");
        return (rule.Mandatory ? CombineStatus.Fail : CombineStatus.Warning,
            $"Cút đổi hướng {deflectionDeg:0}° > {rule.MaxAngle:0}° ({rule.RuleId}: {rule.Description})");
    }

    /// <summary>A branch joining a level main of this system at <paramref name="branchAngleDeg"/> (acute, 0–90).</summary>
    public (CombineStatus Status, string Reason) CheckBranch(MepSystemCode s, double branchAngleDeg)
    {
        var rule = Fitting(s, "BRANCH");
        if (rule is null || s == MepSystemCode.Unknown)
            return (CombineStatus.Pass, "");
        if (branchAngleDeg <= rule.MaxAngle + rule.ToleranceDeg)
            return (CombineStatus.Pass, "");
        return (rule.Mandatory ? CombineStatus.Fail : CombineStatus.Warning,
            $"Nhánh nối {branchAngleDeg:0}° vào ống chính, cần ≤ {rule.MaxAngle:0}° ({rule.RuleId}: {rule.Description})");
    }
}

/// <summary>Tells the systems apart from what Revit knows of a run (C1): classification, system type, names, category.</summary>
public static class MepSystemClassifier
{
    public static readonly MepSystemCode[] All =
    {
        MepSystemCode.DR, MepSystemCode.DCT, MepSystemCode.ECT, MepSystemCode.FP, MepSystemCode.CHW, MepSystemCode.CW,
        MepSystemCode.GAS, MepSystemCode.CND
    };

    public static string Label(MepSystemCode s) => s switch
    {
        MepSystemCode.DR => "Thoát nước",
        MepSystemCode.DCT => "Ống gió",
        MepSystemCode.ECT => "Máng cáp",
        MepSystemCode.FP => "PCCC",
        MepSystemCode.CHW => "Nước lạnh / gas lạnh ĐHKK",
        MepSystemCode.CW => "Cấp nước",
        MepSystemCode.GAS => "Gas đốt (LPG)",
        MepSystemCode.CND => "Ống luồn điện",
        _ => "Chưa rõ hệ"
    };

    public static string Display(MepSystemCode s) => $"{Label(s)} ({s})";

    /// <summary>Codes outside the company list: fuel gas (the company's GAS is refrigerant) and conduit.</summary>
    public const string FuelGas = "LPG";
    public const string ConduitCode = "CND";

    /// <summary>The group a priority entry belongs to: a company code, LPG, CND or a group code itself.</summary>
    public static MepSystemCode Family(string? code) =>
        code is null ? MepSystemCode.Unknown
        : CatalogCodes.TryGetValue(code, out var f) ? f
        : code == FuelGas ? MepSystemCode.GAS
        : Parse(code);

    /// <summary>"SAD - Ống gió cấp" as in the company list; "LPG - Gas đốt"; a group code as "DR - nhóm Thoát nước".</summary>
    public static string CodeLabel(string code)
    {
        var spec = MepSystemCatalog.All.FirstOrDefault(s => s.Abbr == code);
        if (spec is not null)
            return $"{spec.Abbr} - {spec.NameVi}";
        if (code == FuelGas)
            return "LPG - Gas đốt (ngoài danh mục)";
        if (code == ConduitCode)
            return "CND - Ống luồn điện (ngoài danh mục)";
        var group = Parse(code);
        return group == MepSystemCode.Unknown ? code : $"{code} - nhóm {Label(group)}";
    }

    /// <summary>The run's company system where known, else its group.</summary>
    public static string Label(MepSystemCode s, string? code) => string.IsNullOrEmpty(code) ? Label(s) : CodeLabel(code!);

    /// <summary>
    /// The company system code a run carries: the abbreviation, or the first word of a system / type name
    /// ("SAD - Supply Air", "M-SAD-…"); LPG for fuel gas, CND for conduit; "" when it has none.
    /// </summary>
    public static string Code(string category, params string?[] names)
    {
        foreach (var n in names)
        {
            if (string.IsNullOrWhiteSpace(n))
                continue;
            var words = Fold(n!).Split(new[] { ' ', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
            var first = words.FirstOrDefault() ?? "";
            if (first.Length == 1 && words.Length > 1)
                first = words[1];
            if (CatalogCodes.ContainsKey(first))
                return first;
        }

        if ((category ?? "").IndexOf("conduit", StringComparison.OrdinalIgnoreCase) >= 0)
            return ConduitCode;
        return Classify(category ?? "", null, names) == MepSystemCode.GAS ? FuelGas : "";
    }

    public static MepSystemCode Parse(string? code) =>
        Enum.TryParse<MepSystemCode>(code ?? "", true, out var s) ? s : MepSystemCode.Unknown;

    /// <summary>
    /// <paramref name="category"/>: Duct, Pipe, CableTray, Conduit (and their fittings: DuctFitting, PipeFitting…).
    /// <paramref name="classification"/>: Revit's system classification (Sanitary, DomesticColdWater,
    /// FireProtectWet, SupplyHydronic…). The names (system type, system, abbreviation, run type) settle what the
    /// classification leaves open: gas and chilled water are "Other" or hydronic in most templates.
    /// </summary>
    public static MepSystemCode Classify(string category, string? classification, params string?[] names)
    {
        var cat = (category ?? "").ToLowerInvariant();
        if (cat.Contains("cabletray") || cat.Contains("cable tray"))
            return MepSystemCode.ECT;
        if (cat.Contains("conduit"))
            return MepSystemCode.CND;
        if (cat.Contains("duct"))
            return MepSystemCode.DCT;
        if (!cat.Contains("pipe") && !cat.Contains("sprinkler") && !cat.Contains("plumbing"))
            return MepSystemCode.Unknown;

        // The company system codes (MepSystemCatalog, "SAD - Supply Air"): the abbreviation, or a name's first word.
        foreach (var n in names)
        {
            if (string.IsNullOrWhiteSpace(n))
                continue;
            var words = Fold(n!).Split(new[] { ' ', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
            var first = words.FirstOrDefault() ?? "";
            if (first.Length == 1 && words.Length > 1)
                first = words[1];
            if (CatalogCodes.TryGetValue(first, out var known))
                return known;
        }

        var text = Fold(string.Join(" ", names.Where(n => !string.IsNullOrWhiteSpace(n))));
        // Names first: a sprinkler line typed as "Other" or a gas line typed as domestic water still reads right.
        // Fuel gas only by name (LPG, khí đốt): a plain "GAS" is the refrigerant line (company code GAS).
        if (Has(text, "LPG", "LNG", "KHI DOT", "GAS DOT", "FUEL GAS", "NATURAL GAS", "GAS BEP"))
            return MepSystemCode.GAS;
        if (Has(text, "GAS", "GAS LANH", "MOI CHAT", "REFRIG", "REF"))
            return MepSystemCode.CHW;
        if (Has(text, "PCCC", "SPRINKLER", "CHUA CHAY", "CUU HOA", "FIRE", "FP", "HYDRANT", "DRENCHER"))
            return MepSystemCode.FP;
        if (Has(text, "CAP NUOC", "DOMESTIC", "SINH HOAT", "WATER SUPPLY"))
            return MepSystemCode.CW;
        if (Has(text, "CHW", "CHWS", "CHWR", "CHILL", "NUOC LANH", "CWR", "CONDENSER", "GIAI NHIET", "REFRIG", "MOI CHAT", "GAS LANH", "HYDRONIC"))
            return MepSystemCode.CHW;
        if (Has(text, "THOAT", "DRAIN", "SANITARY", "SOIL", "WASTE", "VENT", "THONG HOI", "NUOC THAI", "NUOC MUA", "RAIN", "STORM", "NUOC NGUNG", "CONDENSATE", "XI", "SW", "WW", "RWP"))
            return MepSystemCode.DR;
        if (Has(text, "COLD WATER", "HOT WATER", "NUOC NONG", "CW", "HW", "DCW", "DHW"))
            return MepSystemCode.CW;

        var cls = (classification ?? "").ToLowerInvariant();
        if (cls.Contains("fireprotect"))
            return MepSystemCode.FP;
        if (cls.Contains("sanitary") || cls.Contains("vent") || cls.Contains("storm"))
            return MepSystemCode.DR;
        if (cls.Contains("hydronic"))
            return MepSystemCode.CHW;
        if (cls.Contains("domestic"))
            return MepSystemCode.CW;
        return MepSystemCode.Unknown;
    }

    /// <summary>Company system codes (MepSystemCatalog) to Combine systems.</summary>
    public static readonly IReadOnlyDictionary<string, MepSystemCode> CatalogCodes = new Dictionary<string, MepSystemCode>
    {
        ["SAD"] = MepSystemCode.DCT, ["RAD"] = MepSystemCode.DCT, ["OAD"] = MepSystemCode.DCT, ["FAD"] = MepSystemCode.DCT,
        ["EAD"] = MepSystemCode.DCT, ["KEAD"] = MepSystemCode.DCT, ["TEAD"] = MepSystemCode.DCT, ["SEAD"] = MepSystemCode.DCT,
        ["PAD"] = MepSystemCode.DCT,
        ["CHWS"] = MepSystemCode.CHW, ["CHWR"] = MepSystemCode.CHW, ["CTS"] = MepSystemCode.CHW, ["CTR"] = MepSystemCode.CHW,
        ["EP"] = MepSystemCode.CHW, ["GAS"] = MepSystemCode.CHW,
        ["CD"] = MepSystemCode.DR,
        ["BW"] = MepSystemCode.ECT, ["MV"] = MepSystemCode.ECT, ["LV"] = MepSystemCode.ECT, ["ELV"] = MepSystemCode.ECT,
        ["FA"] = MepSystemCode.ECT,
        ["CWS"] = MepSystemCode.CW, ["HWS"] = MepSystemCode.CW, ["HWR"] = MepSystemCode.CW,
        ["BWS"] = MepSystemCode.DR, ["GWS"] = MepSystemCode.DR, ["KP"] = MepSystemCode.DR, ["RWP"] = MepSystemCode.DR,
        ["VP"] = MepSystemCode.DR,
        ["SP"] = MepSystemCode.FP, ["FFH"] = MepSystemCode.FP, ["FDR"] = MepSystemCode.FP, ["FFD"] = MepSystemCode.FP,
        ["FFP"] = MepSystemCode.FP,
        // Fire drainage falls like any drain.
        ["FDS"] = MepSystemCode.DR
    };

    private static bool Has(string text, params string[] words)
    {
        foreach (var w in words)
        {
            var i = 0;
            while ((i = text.IndexOf(w, i, StringComparison.Ordinal)) >= 0)
            {
                var before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                var after = i + w.Length >= text.Length || !char.IsLetter(text[i + w.Length]);
                // Short codes (CW, FP…) only as whole words; longer words may sit inside a name (SPRINKLERS).
                if (before && (after || w.Length > 3))
                    return true;
                i += w.Length;
            }
        }

        return false;
    }

    /// <summary>Upper case without Vietnamese marks, so "Thoát nước" reads THOAT NUOC.</summary>
    public static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(c is 'đ' or 'Đ' ? 'D' : char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }
}

/// <summary>A straight run as Combine sees it (mm): axis ends and the outside section, insulation included.</summary>
public sealed class CombineRun
{
    public long Id { get; set; }
    public MepSystemCode System { get; set; }
    /// <summary>Company system code (SAD, CWS…; LPG, CND), "" when the run carries none.</summary>
    public string Code { get; set; } = "";
    public string Label => MepSystemClassifier.Label(System, Code);
    public double Ax { get; set; }
    public double Ay { get; set; }
    public double Az { get; set; }
    public double Bx { get; set; }
    public double By { get; set; }
    public double Bz { get; set; }
    /// <summary>Across in plan (diameter when round), insulation included.</summary>
    public double WidthMm { get; set; }
    /// <summary>Up and down (diameter when round), insulation included.</summary>
    public double DepthMm { get; set; }
    /// <summary>Pinned in Revit, or its system is locked: never moved.</summary>
    public bool Locked { get; set; }
    public string Name { get; set; } = "";

    public double PlanLength => Math.Sqrt(Sq(Bx - Ax) + Sq(By - Ay));
    public bool IsLevel => PlanLength > 1 && Math.Abs(Bz - Az) / PlanLength < 0.2;
    public bool IsSloped => IsLevel && Math.Abs(Bz - Az) > 1;
    private static double Sq(double v) => v * v;
}

/// <summary>Two runs closer than their pair allows (C2).</summary>
public sealed class CombineConflict
{
    public CombineRun A { get; set; } = null!;
    public CombineRun B { get; set; } = null!;
    public bool Parallel { get; set; }
    /// <summary>Gap surface to surface (mm); negative where they cut into each other.</summary>
    public double GapMm { get; set; }
    public double RequiredMm { get; set; }
    /// <summary>0: A gives way, 1: B, null: neither may move.</summary>
    public int? Yielder { get; set; }
    /// <summary>Where they cross (or the middle of the overlap), plan mm.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public CombineStatus Status { get; set; }
    public string Issue { get; set; } = "";
    public string Action { get; set; } = "";
}

/// <summary>Finds the runs closer than their pair clearance and decides who gives way (C2, C4, C6).</summary>
public static class CombineAnalyzer
{
    private const double ParallelSin = 0.0872; // 5°

    public static List<CombineConflict> Analyze(IReadOnlyList<CombineRun> runs, CombineProfile profile)
    {
        var result = new List<CombineConflict>();
        // Grid of 3 m cells on the plan box of each run, to test only runs that come near.
        const double cell = 3000;
        var grid = new Dictionary<(long, long), List<int>>();
        var margin = Math.Max(500, profile.Pairs.Select(p => Math.Max(p.ParallelMm, p.CrossingMm)).DefaultIfEmpty(0).Max());
        for (var i = 0; i < runs.Count; i++)
        {
            var r = runs[i];
            var half = Math.Max(r.WidthMm, r.DepthMm) / 2 + margin;
            long x0 = (long)Math.Floor((Math.Min(r.Ax, r.Bx) - half) / cell), x1 = (long)Math.Floor((Math.Max(r.Ax, r.Bx) + half) / cell);
            long y0 = (long)Math.Floor((Math.Min(r.Ay, r.By) - half) / cell), y1 = (long)Math.Floor((Math.Max(r.Ay, r.By) + half) / cell);
            for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++)
            {
                if (!grid.TryGetValue((x, y), out var list))
                    grid[(x, y)] = list = new List<int>();
                list.Add(i);
            }
        }

        var seen = new HashSet<(int, int)>();
        foreach (var list in grid.Values)
        {
            for (var p = 0; p < list.Count; p++)
            for (var q = p + 1; q < list.Count; q++)
            {
                var i = Math.Min(list[p], list[q]);
                var j = Math.Max(list[p], list[q]);
                if (i == j || !seen.Add((i, j)))
                    continue;
                var c = Check(runs[i], runs[j], profile);
                if (c is not null)
                    result.Add(c);
            }
        }

        return result;
    }

    /// <summary>The conflict between two runs, or null when they keep their pair's clearance (or are the same system).</summary>
    public static CombineConflict? Check(CombineRun a, CombineRun b, CombineProfile profile)
    {
        // A system's own runs meet at its fittings; Combine coordinates one system against another (SAD against
        // RAD too, both ducts).
        if (a.System == b.System && a.System != MepSystemCode.Unknown && (a.Code == b.Code || a.Code.Length == 0 || b.Code.Length == 0))
            return null;
        var g = Gap(a, b, profile);
        if (g is null)
            return null;
        var (gap, parallel, x, y) = g.Value;
        var required = profile.Clearance(a.System, b.System, parallel);
        if (gap >= required - 0.5)
            return null;

        var c = new CombineConflict { A = a, B = b, Parallel = parallel, GapMm = gap, RequiredMm = required, X = x, Y = y };
        var what = gap < 0 ? $"va chạm (chồng {-gap:0} mm)" : $"khe hở {gap:0} mm < {required:0} mm";
        c.Issue = $"{(parallel ? "Song song" : "Giao cắt")}: {what}";
        if (a.System == MepSystemCode.Unknown || b.System == MepSystemCode.Unknown)
        {
            c.Status = CombineStatus.Review;
            c.Action = "Chưa rõ hệ thống: đặt tên / loại hệ thống rồi phân tích lại";
            return c;
        }

        c.Yielder = profile.Yielder(a.System, a.Locked, Math.Max(a.WidthMm, a.DepthMm), b.System, b.Locked, Math.Max(b.WidthMm, b.DepthMm), a.Code, b.Code);
        if (c.Yielder is null)
        {
            c.Status = CombineStatus.Review;
            c.Action = "Cả hai không được dời (khóa / gas / thoát nước giữ dốc): sửa tay";
            return c;
        }

        var mover = c.Yielder == 0 ? a : b;
        var other = c.Yielder == 0 ? b : a;
        if (parallel)
        {
            c.Status = CombineStatus.Warning;
            c.Action = $"Dời ngang {mover.Label} thêm {required - gap:0} mm (sửa tay)";
            return c;
        }

        if (!mover.IsLevel || mover.IsSloped)
        {
            c.Status = CombineStatus.Review;
            c.Action = $"{mover.Label} đứng / dốc: không tự đổi tuyến";
            return c;
        }

        var under = (mover.Az + mover.Bz) / 2 <= Mid(other, x, y);
        c.Status = CombineStatus.Fail;
        c.Action = $"{mover.Label} đi {(under ? "dưới" : "trên")} {other.Label}, cút {profile.DetourAngle(mover.System):0}°";
        return c;
    }

    /// <summary>Centre height of <paramref name="r"/> at the plan point nearest (x, y).</summary>
    public static double Mid(CombineRun r, double x, double y)
    {
        var len2 = Sq(r.Bx - r.Ax) + Sq(r.By - r.Ay);
        if (len2 < 1)
            return (r.Az + r.Bz) / 2;
        var t = Clamp(((x - r.Ax) * (r.Bx - r.Ax) + (y - r.Ay) * (r.By - r.Ay)) / len2, 0, 1);
        return r.Az + (r.Bz - r.Az) * t;
    }

    /// <summary>
    /// Surface gap between two runs where they come near (mm), whether they run side by side, and where. Null when
    /// they never come within reach of each other along their lengths.
    /// </summary>
    public static (double Gap, bool Parallel, double X, double Y)? Gap(CombineRun a, CombineRun b, CombineProfile profile)
    {
        var reach = profile.Clearance(a.System, b.System, true) + Math.Max(a.WidthMm, b.WidthMm);
        if (!a.IsLevel || !b.IsLevel)
            return BoxGap(a, b, reach);

        double ux = a.Bx - a.Ax, uy = a.By - a.Ay, la = Math.Sqrt(ux * ux + uy * uy);
        double vx = b.Bx - b.Ax, vy = b.By - b.Ay, lb = Math.Sqrt(vx * vx + vy * vy);
        ux /= la;
        uy /= la;
        vx /= lb;
        vy /= lb;
        var cross = ux * vy - uy * vx;
        if (Math.Abs(cross) < ParallelSin)
        {
            // Side by side: overlap along A, then the gap across and the gap up/down; apart if either is open.
            double s0 = (b.Ax - a.Ax) * ux + (b.Ay - a.Ay) * uy, s1 = (b.Bx - a.Ax) * ux + (b.By - a.Ay) * uy;
            var lo = Math.Max(0, Math.Min(s0, s1));
            var hi = Math.Min(la, Math.Max(s0, s1));
            if (hi - lo < 1)
                return null;
            var across = Math.Abs((b.Ax - a.Ax) * -uy + (b.Ay - a.Ay) * ux);
            var sideGap = across - (a.WidthMm + b.WidthMm) / 2;
            var s = (lo + hi) / 2;
            double mx = a.Ax + ux * s, my = a.Ay + uy * s;
            var upGap = Math.Abs(Mid(a, mx, my) - Mid(b, mx, my)) - (a.DepthMm + b.DepthMm) / 2;
            if (sideGap > reach)
                return null;
            return (Math.Max(sideGap, upGap), true, mx, my);
        }

        // Crossing: where the axes meet in plan; each must reach that point (with half the other's width and the
        // clearance to spare), then the gap is up/down.
        double dx = b.Ax - a.Ax, dy = b.Ay - a.Ay;
        var ta = (dx * vy - dy * vx) / cross;
        var tb = (dx * uy - dy * ux) / cross;
        var sin = Math.Abs(cross);
        var spareA = b.WidthMm / 2 / sin + reach;
        var spareB = a.WidthMm / 2 / sin + reach;
        if (ta < -spareA || ta > la + spareA || tb < -spareB || tb > lb + spareB)
            return null;
        // Beyond an end, the runs only meet if the end still reaches across the other's side.
        var outA = Math.Max(-ta, ta - la);
        var outB = Math.Max(-tb, tb - lb);
        double px = a.Ax + ux * ta, py = a.Ay + uy * ta;
        var planGap = Math.Max(Math.Max(outA - b.WidthMm / 2 / sin, outB - a.WidthMm / 2 / sin), 0);
        var vertical = Math.Abs(Mid(a, px, py) - Mid(b, px, py)) - (a.DepthMm + b.DepthMm) / 2;
        var gap = planGap > 0 ? Math.Max(planGap, vertical) : vertical;
        return (gap, false, px, py);
    }

    /// <summary>Risers and steep runs: the gap between their boxes.</summary>
    private static (double Gap, bool Parallel, double X, double Y)? BoxGap(CombineRun a, CombineRun b, double reach)
    {
        (double X0, double Y0, double Z0, double X1, double Y1, double Z1) Box(CombineRun r)
        {
            double hw = r.WidthMm / 2, hd = r.DepthMm / 2;
            var vertical = !r.IsLevel;
            // A riser's section lies flat: width both ways in plan.
            return (Math.Min(r.Ax, r.Bx) - hw, Math.Min(r.Ay, r.By) - hw, Math.Min(r.Az, r.Bz) - (vertical ? 0 : hd),
                Math.Max(r.Ax, r.Bx) + hw, Math.Max(r.Ay, r.By) + hw, Math.Max(r.Az, r.Bz) + (vertical ? 0 : hd));
        }

        var p = Box(a);
        var q = Box(b);
        var gx = Math.Max(p.X0 - q.X1, q.X0 - p.X1);
        var gy = Math.Max(p.Y0 - q.Y1, q.Y0 - p.Y1);
        var gz = Math.Max(p.Z0 - q.Z1, q.Z0 - p.Z1);
        var gap = Math.Max(gx, Math.Max(gy, gz));
        if (gap > reach)
            return null;
        return (gap, false, (Math.Max(p.X0, q.X0) + Math.Min(p.X1, q.X1)) / 2, (Math.Max(p.Y0, q.Y0) + Math.Min(p.Y1, q.Y1)) / 2);
    }

    /// <summary>
    /// Plan offset of each sloped leg of a detour: a 45° detour drops <paramref name="dropMm"/> over as much run;
    /// a 90° one goes straight down.
    /// </summary>
    public static double LegRun(double detourAngleDeg, double dropMm) =>
        detourAngleDeg >= 89 ? 0 : Math.Abs(dropMm) / Math.Tan(detourAngleDeg * Math.PI / 180);

    /// <summary>Deflection of a bend whose two connectors face out along these directions (0 straight, 90 square).</summary>
    public static double Deflection(double ax, double ay, double az, double bx, double by, double bz)
    {
        var dot = (ax * bx + ay * by + az * bz) / (Norm(ax, ay, az) * Norm(bx, by, bz));
        // Outward faces of a straight piece point opposite ways (180° apart): no turn.
        return 180 - Math.Acos(Clamp(dot, -1, 1)) * 180 / Math.PI;
    }

    /// <summary>Acute angle between two axes (0–90°).</summary>
    public static double AxisAngle(double ax, double ay, double az, double bx, double by, double bz)
    {
        var dot = Math.Abs(ax * bx + ay * by + az * bz) / (Norm(ax, ay, az) * Norm(bx, by, bz));
        return Math.Acos(Clamp(dot, 0, 1)) * 180 / Math.PI;
    }

    private static double Norm(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);
    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    private static double Sq(double v) => v * v;
}

/// <summary>A connected network in a corridor: it moves up or down as one.</summary>
public sealed class StackNet
{
    public int Index { get; set; }
    public MepSystemCode System { get; set; }
    /// <summary>Company system code of most of its runs, "" when they carry none.</summary>
    public string Code { get; set; } = "";
    public string Label => MepSystemClassifier.Label(System, Code);
    public List<CombineRun> Runs { get; set; } = new();
    /// <summary>Never moved: locked, pinned, sloped (drainage), or what Revit can't move.</summary>
    public bool Fixed { get; set; }
    public string FixedReason { get; set; } = "";
    /// <summary>Lowest beam or slab underside over the network (mm, absolute); null where none is above.</summary>
    public double? CeilingMm { get; set; }
    /// <summary>Result: how far to move it (mm, + up).</summary>
    public double ShiftMm { get; set; }
    /// <summary>Result: 1 is the layer nearest the structure.</summary>
    public int Layer { get; set; }
    public CombineStatus Status { get; set; }
    public string Note { get; set; } = "";

    public double Top => Runs.Count == 0 ? 0 : Runs.Max(r => Math.Max(r.Az, r.Bz) + r.DepthMm / 2);
    public double Bottom => Runs.Count == 0 ? 0 : Runs.Min(r => Math.Min(r.Az, r.Bz) - r.DepthMm / 2);
    public double Size => Runs.Count == 0 ? 0 : Runs.Max(r => Math.Max(r.WidthMm, r.DepthMm));
}

/// <summary>
/// Combine by corridor (C5): the networks are stacked in height by priority. The first goes as high as the
/// structure lets it (less the structure clearance); each next one as high as it can without coming within its
/// pair clearance of a network already placed that it meets in plan. Fixed networks (drainage on its fall,
/// locked, pinned) stay put and the others stack round them. A network that would end below
/// <c>minBottomMm</c> is left where it is and marked REVIEW with the shortfall.
/// </summary>
public static class CorridorStacker
{
    public static void Stack(List<StackNet> nets, CombineProfile profile, double? minBottomMm)
    {
        // Which networks meet in plan, and the clearance they need there.
        var meet = new Dictionary<(int, int), double>();
        for (var i = 0; i < nets.Count; i++)
        for (var j = i + 1; j < nets.Count; j++)
        {
            var c = PlanClearance(nets[i], nets[j], profile);
            if (c is double need)
                meet[(i, j)] = meet[(j, i)] = need;
        }

        var placed = new List<int>();
        var order = Enumerable.Range(0, nets.Count)
            .OrderByDescending(i => nets[i].Fixed)
            .ThenBy(i => profile.Rank(nets[i].System, nets[i].Code))
            .ThenByDescending(i => nets[i].Size)
            .ToList();
        foreach (var i in order)
        {
            var n = nets[i];
            n.ShiftMm = 0;
            if (n.Fixed)
            {
                n.Status = CombineStatus.Pass;
                n.Note = "Giữ nguyên: " + n.FixedReason;
                placed.Add(i);
                continue;
            }

            var access = n.System == MepSystemCode.ECT ? profile.TrayAccessMm : 0;
            var top = n.Top + access;
            var bottom = n.Bottom;
            // As high as the structure allows; with nothing above, no higher than it is now.
            var s = n.CeilingMm is double ceiling ? ceiling - profile.StructureClearanceMm - top : 0;
            // Under every higher priority network it meets: priority sets the layers. A fixed one (drainage on its
            // fall) may have this one over it where there is room.
            foreach (var p in placed.Where(p => !nets[p].Fixed && meet.ContainsKey((i, p))))
                s = Math.Min(s, nets[p].Bottom + nets[p].ShiftMm - meet[(i, p)] - top);
            for (var guard = 0; guard < 200; guard++)
            {
                var moved = false;
                foreach (var p in placed)
                {
                    if (!meet.TryGetValue((i, p), out var need))
                        continue;
                    var o = nets[p];
                    var oTop = o.Top + o.ShiftMm + (o.System == MepSystemCode.ECT ? profile.TrayAccessMm : 0);
                    var oBottom = o.Bottom + o.ShiftMm;
                    if (bottom + s < oTop + need - 0.5 && top + s > oBottom - need + 0.5)
                    {
                        // Under it.
                        s = oBottom - need - top;
                        moved = true;
                    }
                }

                if (!moved)
                    break;
            }

            s = Math.Floor(s / 5) * 5;
            if (minBottomMm is double floor && bottom + s < floor - 0.5)
            {
                n.Status = CombineStatus.Review;
                n.Note = $"Không đủ chiều cao: đáy xuống {bottom + s:0}, thấp hơn giới hạn {floor - (bottom + s):0} mm. Giữ nguyên, cần đổi tuyến / đổi kích thước";
                n.ShiftMm = 0;
                placed.Add(i);
                continue;
            }

            n.ShiftMm = Math.Abs(s) < 1 ? 0 : s;
            n.Status = n.ShiftMm == 0 ? CombineStatus.Pass : CombineStatus.Fail;
            n.Note = n.ShiftMm == 0 ? "Đã đúng tầng" : $"{(n.ShiftMm > 0 ? "Nâng" : "Hạ")} {Math.Abs(n.ShiftMm):0} mm";
            placed.Add(i);
        }

        // Layers counted from the structure down, by the height each network ends at.
        var layer = 0;
        var last = double.MaxValue;
        foreach (var n in nets.OrderByDescending(x => x.Top + x.ShiftMm))
        {
            if (last - (n.Top + n.ShiftMm) > 1)
                layer++;
            last = n.Top + n.ShiftMm;
            n.Layer = layer;
        }
    }

    /// <summary>The clearance two networks need where they meet in plan (the largest over their runs), or null.</summary>
    public static double? PlanClearance(StackNet a, StackNet b, CombineProfile profile)
    {
        double? need = null;
        foreach (var ra in a.Runs.Where(r => r.IsLevel))
        foreach (var rb in b.Runs.Where(r => r.IsLevel))
        {
            // Flattened to one height and made very deep, the gap left is the gap in plan.
            var fa = Flat(ra);
            var fb = Flat(rb);
            var g = CombineAnalyzer.Gap(fa, fb, profile);
            if (g is null)
                continue;
            var (gap, parallel, _, _) = g.Value;
            var c = profile.Clearance(ra.System, rb.System, parallel);
            if (gap < c)
                need = Math.Max(need ?? 0, c);
        }

        return need;
    }

    private static CombineRun Flat(CombineRun r) => new()
    {
        Id = r.Id, System = r.System, Code = r.Code, Ax = r.Ax, Ay = r.Ay, Bx = r.Bx, By = r.By, Az = 0, Bz = 0, WidthMm = r.WidthMm,
        DepthMm = 1e6
    };
}
