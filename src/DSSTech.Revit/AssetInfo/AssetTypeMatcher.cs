using Dss.AssetInfo.Core;

namespace DSSTech.Revit.AssetInfo;

internal enum MatchMethod
{
    None,
    /// <summary>Type already carries the same TS_ID (re-import / update).</summary>
    TsId,
    /// <summary>Revit type name == TS_TenTaiSan (normalised).</summary>
    TypeName,
    /// <summary>Record came from the model itself (PDF-only import).</summary>
    RevitSource,
    /// <summary>Model code found inside family+type name — weaker, not ticked by default.</summary>
    ModelInName,
    /// <summary>Picked by the user in the grid.</summary>
    Manual
}

internal sealed class TypeMatch
{
    public List<RevitTypeInfo> Targets { get; } = new();
    public MatchMethod Method { get; set; }
    public int CandidateCount { get; set; }

    /// <summary>Strong matches are pre-ticked for writing; ModelInName needs a human look.</summary>
    public bool IsStrong => Method is MatchMethod.TsId or MatchMethod.TypeName or MatchMethod.RevitSource or MatchMethod.Manual;

    public static string Describe(MatchMethod m) => m switch
    {
        MatchMethod.TsId => "Theo TS_ID đã có trên Type",
        MatchMethod.TypeName => "Tên Type = Tên tài sản",
        MatchMethod.RevitSource => "Type trong mô hình",
        MatchMethod.ModelInName => "Model nằm trong tên Type (cần xác nhận)",
        MatchMethod.Manual => "Chọn tay",
        _ => "Chưa khớp"
    };
}

internal sealed class AssetTypeMatcher
{
    private readonly ILookup<string, RevitTypeInfo> _byTsId;
    private readonly ILookup<string, RevitTypeInfo> _byName;
    private readonly Dictionary<long, RevitTypeInfo> _byElementId;
    private readonly IReadOnlyList<RevitTypeInfo> _types;
    private readonly int _minToken;

    public AssetTypeMatcher(IReadOnlyList<RevitTypeInfo> types, int minModelTokenLength)
    {
        _types = types;
        _minToken = minModelTokenLength;
        _byTsId = types.Where(t => !string.IsNullOrWhiteSpace(t.TsId)).ToLookup(t => t.TsId.Trim(), StringComparer.OrdinalIgnoreCase);
        _byName = types.Where(t => t.NormName.Length > 0).ToLookup(t => t.NormName, StringComparer.Ordinal);
        _byElementId = types.GroupBy(t => t.Id.Value).ToDictionary(g => g.Key, g => g.First());
    }

    public TypeMatch Match(AssetRecord r)
    {
        var m = new TypeMatch();

        if (r.SourceFile == "Revit" && r.Id.StartsWith("REVIT-", StringComparison.Ordinal)
            && long.TryParse(r.Id["REVIT-".Length..], out var eid) && _byElementId.TryGetValue(eid, out var self))
            return With(m, MatchMethod.RevitSource, new[] { self });

        if (!string.IsNullOrWhiteSpace(r.Id) && _byTsId.Contains(r.Id.Trim()))
            return With(m, MatchMethod.TsId, _byTsId[r.Id.Trim()]);

        var wanted = ModelTokens.Normalize(r.TypeName);
        if (wanted.Length > 0 && _byName.Contains(wanted))
            return With(m, MatchMethod.TypeName, _byName[wanted]);

        var tokens = ModelTokens.From(r.Model, _minToken);
        if (tokens.Count > 0)
        {
            // Prefer the full model (first token); fall back to parts only if the full code is not found.
            var full = _types.Where(t => t.NormFamilyAndType.Contains(tokens[0], StringComparison.Ordinal)).ToList();
            var hits = full.Count > 0 ? full : _types.Where(t => ModelTokens.AnyIn(tokens, t.NormFamilyAndType)).ToList();
            if (hits.Count > 0)
            {
                // Several types share the model token → keep them as candidates, write none until the user picks.
                m.Method = MatchMethod.ModelInName;
                m.CandidateCount = hits.Count;
                m.Targets.Add(hits[0]);
                return m;
            }
        }
        return m;
    }

    private static TypeMatch With(TypeMatch m, MatchMethod method, IEnumerable<RevitTypeInfo> targets)
    {
        m.Method = method;
        m.Targets.AddRange(targets);
        m.CandidateCount = m.Targets.Count;
        return m;
    }
}
