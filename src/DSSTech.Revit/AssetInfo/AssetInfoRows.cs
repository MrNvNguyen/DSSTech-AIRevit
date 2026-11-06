using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Dss.AssetInfo.Core;

namespace DSSTech.Revit.AssetInfo;

internal abstract class RowBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void RefreshAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>Excel/Revit record ↔ matched Revit type(s).</summary>
internal sealed class MatchRow : RowBase
{
    private bool _include;
    private RevitTypeInfo? _selected;

    public MatchRow(AssetRecord record, TypeMatch match)
    {
        Record = record;
        Match = match;
        _selected = match.Targets.FirstOrDefault();
        _include = match.IsStrong && match.Targets.Count > 0;
    }

    public AssetRecord Record { get; }
    public TypeMatch Match { get; }

    public bool Include
    {
        get => _include;
        set { if (_include == value) return; _include = value; Notify(); }
    }

    /// <summary>Changing the combo = manual mapping to exactly that type.</summary>
    public RevitTypeInfo? SelectedType
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            Match.Targets.Clear();
            if (value is not null) Match.Targets.Add(value);
            Match.Method = value is null ? MatchMethod.None : MatchMethod.Manual;
            Match.CandidateCount = Match.Targets.Count;
            _include = value is not null;
            RefreshAll();
        }
    }

    public string Key => Record.Key;
    public string Sheet => Record.Sheet;
    public string Name => Record.Name;
    public string Model => Record.Model;
    public string ExpectedType => Record.TypeName;
    public string MethodText => TypeMatch.Describe(Match.Method) +
                               (Match.Method == MatchMethod.ModelInName && Match.CandidateCount > 1 ? $" – {Match.CandidateCount} ứng viên" : "");
    public int TargetCount => Match.Targets.Count;
    public int InstanceCount => Match.Targets.Sum(t => t.InstanceCount);
    public int ParamCount => Record.Values.Keys.Count(AssetParams.IsAssetParam);
    public bool IsMatched => Match.Targets.Count > 0;
    public bool NeedsReview => Match.Method == MatchMethod.ModelInName;
}

/// <summary>Model found in a PDF → candidate TS_Catalogue value.</summary>
internal sealed class LinkRow : RowBase
{
    private bool _include;
    public LinkRow(AssetRecord record, PdfPageLink link)
    {
        Record = record;
        Link = link;
        _include = string.IsNullOrWhiteSpace(record.Get(AssetParams.Catalogue));
    }
    public AssetRecord Record { get; }
    public PdfPageLink Link { get; }
    public bool Include { get => _include; set { if (_include == value) return; _include = value; Notify(); } }
    public string Key => Record.Key;
    public string Model => Link.Model;
    public string File => Path.GetFileName(Link.FilePath);
    public string Pages => string.Join(", ", Link.Pages);
    public string Current => Record.Get(AssetParams.Catalogue);
}

/// <summary>Value read next to a model in a PDF — never pre-ticked.</summary>
internal sealed class ProposalRow : RowBase
{
    public ProposalRow(AssetRecord record, ValueProposal p) { Record = record; Proposal = p; }
    public AssetRecord Record { get; }
    public ValueProposal Proposal { get; }
    public bool Include { get => Proposal.Accepted; set { if (Proposal.Accepted == value) return; Proposal.Accepted = value; Notify(); } }
    public string Key => Record.Key;
    public string Param => Proposal.Param;
    public string Value => Proposal.Value;
    public string Confidence => Proposal.Confidence switch
    {
        ProposalConfidence.High => "Cao (1 model/trang)",
        ProposalConfidence.Medium => "Trung bình",
        _ => "Thấp (bảng nhiều model)"
    };
    public string File => Path.GetFileName(Proposal.FilePath);
    public int Page => Proposal.Page;
    public string Snippet => Proposal.Snippet.Replace("\n", " ⏎ ");
}
