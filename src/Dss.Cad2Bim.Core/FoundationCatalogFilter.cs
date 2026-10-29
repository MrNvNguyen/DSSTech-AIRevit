using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Name/keyword filters for Structural Foundation Family : Type lists in Model Revit.
/// Does not invent types — only filters names already loaded in the project.
/// </summary>
public static class FoundationCatalogFilter
{
    public const string PileSubtypeBored = "Bored";
    public const string PileSubtypePressed = "Pressed";
    public const string PileSubtypeDriven = "Driven";

    public const string KindPileCap = "PileCap";
    public const string KindIsolated = "Isolated";
    public const string KindStrip = "Strip";
    public const string KindRaft = "Raft";

    /// <summary>
    /// Revit free-host offset for pile top when derived from pile-cap bottom.
    /// Same Level for cả đài và cọc; offset dương lên trên Level.
    /// Đỉnh cọc = đáy đài + ngàm (cọc ngàm vào trong đài).
    /// </summary>
    public static double PileTopOffsetFromCapBottomMm(double capBottomOffsetMm, double embedMm) =>
        capBottomOffsetMm + embedMm;

    public static bool LooksLikePile(string familyType)
    {
        var s = Norm(familyType);
        if (ContainsAny(s, "pilecap", "pile-cap", "pile_cap", "dai coc", "daicoc", "footing", "raft", "mat foundation"))
            return false;
        return ContainsAny(s,
            "pile", "coc", "cọc",
            "phc", "phc-", "s-foun-phc",
            "bored", "khoan", "nhoi", "nhồi", "barrette", "cfa",
            "driven", "dong", "đóng",
            "ep coc", "epcoc", "ép", "pressed", "press pile", "prestressed pile");
    }

    public static bool MatchesPileSubtype(string familyType, string subtype)
    {
        var s = Norm(familyType);
        if (!LooksLikePile(familyType))
            return false;
        return subtype switch
        {
            PileSubtypeBored => ContainsAny(s, "bored", "khoan", "nhoi", "nhồi", "barrette", "cfa", "s-foun-bor"),
            // PHC / PC spun piles are ép cọc in VN practice unless name also says driven/đóng.
            PileSubtypePressed =>
                ContainsAny(s, "press", "pressed", "ep ", "ép", "epcoc", "ep coc", "phc", "s-foun-phc", "pc pile", "prestress")
                && !ContainsAny(s, "driven", "dong", "đóng", "bored", "khoan", "nhoi", "nhồi"),
            PileSubtypeDriven => ContainsAny(s, "driven", "drive", "dong", "đóng", "s-foun-drv", "precast driven"),
            _ => true
        };
    }

    public static bool MatchesFoundationKind(string familyType, string kind)
    {
        var s = Norm(familyType);
        if (LooksLikePile(familyType))
            return false;
        return kind switch
        {
            KindPileCap => ContainsAny(s,
                "pilecap", "pile-cap", "pile_cap", "pile cap",
                "dai", "đài", "daicoc", "dai coc",
                "foun-cap", "foun_cap", "pcap", "cap footing"),
            KindStrip => ContainsAny(s,
                "strip", "continuous", "bang", "băng",
                "foun-str", "foun_str", "wall footing", "grade beam", "gradebeam"),
            KindRaft => ContainsAny(s,
                "raft", "mat", "be ", "bè", "mong be", "móng bè",
                "foun-raft", "foun_raft", "mat foundation"),
            KindIsolated =>
                ContainsAny(s, "isolated", "pad", "don", "đơn", "footing", "foun-iso", "foun_iso", "foun-pad")
                || (!ContainsAny(s, "pilecap", "pile-cap", "dai", "đài", "strip", "bang", "băng", "raft", "mat", "bè")
                    && ContainsAny(s, "foun", "foot", "mong", "móng")),
            _ => !LooksLikePile(familyType)
        };
    }

    public static IReadOnlyList<string> FilterPileTypes(IReadOnlyList<string> all, string subtype)
    {
        var bySub = all.Where(t => MatchesPileSubtype(t, subtype)).ToList();
        if (bySub.Count > 0)
            return bySub;
        var piles = all.Where(LooksLikePile).ToList();
        return piles.Count > 0 ? piles : all;
    }

    public static IReadOnlyList<string> FilterFoundationTypes(IReadOnlyList<string> all, string kind)
    {
        var byKind = all.Where(t => MatchesFoundationKind(t, kind)).ToList();
        if (byKind.Count > 0)
            return byKind;
        var nonPile = all.Where(t => !LooksLikePile(t)).ToList();
        return nonPile.Count > 0 ? nonPile : all;
    }

    /// <summary>
    /// Type Mark compare for Đài cọc: trim + case-insensitive; Vietnamese Đ/đ kept as the same letter.
    /// Does not fold Đ→D (CAD "Đ4" must match Revit Type Mark "Đ4", not "D4").
    /// </summary>
    public static string NormalizeTypeMark(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value.Trim().ToUpperInvariant();
    }

    public static bool TypeMarkEquals(string? a, string? b)
    {
        var na = NormalizeTypeMark(a);
        var nb = NormalizeTypeMark(b);
        return na.Length > 0 && na == nb;
    }

    /// <summary>
    /// Match a CAD pile-cap label to a known Revit Type Mark list (from project types).
    /// Also accepts type-name tokens already collected into <paramref name="availableMarks"/>
    /// (e.g. leaf <c>Đ4A-1000x…</c> stored as <c>Đ4A</c>).
    /// Returns the catalog string as stored (not re-cased).
    /// </summary>
    public static bool TryMatchTypeMark(string? cadLabel, IEnumerable<string>? availableMarks, out string matched)
    {
        matched = "";
        var wantRaw = (cadLabel ?? "").Trim();
        if (wantRaw.Length == 0 || availableMarks is null)
            return false;

        // Prefer exact CAD label; also allow CAD text that embeds the id.
        var candidates = new List<string> { wantRaw };
        if (CadMark.TryExtractPileCapId(wantRaw, out var extracted) &&
            !TypeMarkEquals(extracted, wantRaw))
            candidates.Add(extracted);

        foreach (var candidate in candidates)
        {
            var want = NormalizeTypeMark(candidate);
            if (want.Length == 0)
                continue;
            foreach (var mark in availableMarks)
            {
                if (!TypeMarkEquals(want, mark))
                    continue;
                matched = mark.Trim();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when a Structural Foundation type name leaf encodes the CAD mark
    /// (<c>Đ4A-2500x2500x1100mm</c> matches <c>Đ4A</c>; <c>Đ4</c> does not match <c>Đ4A</c>).
    /// </summary>
    public static bool TypeNameMatchesMark(string? familyOrTypeName, string? typeMark)
    {
        if (!CadMark.TryExtractPileCapId(familyOrTypeName, out var fromName))
            return false;
        return TypeMarkEquals(fromName, typeMark);
    }

    private static string Norm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value.Trim().ToLowerInvariant().Replace('đ', 'd');
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.IndexOf(n, StringComparison.Ordinal) >= 0)
                return true;
        }

        return false;
    }
}
