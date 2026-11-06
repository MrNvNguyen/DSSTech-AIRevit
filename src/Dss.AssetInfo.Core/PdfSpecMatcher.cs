namespace Dss.AssetInfo.Core;

/// <summary>
/// Finds each record's model code in a PDF and returns:
///   • Links (always): file + pages where the model appears → for TS_Catalogue;
///   • Proposals (only if pdf.proposeValues): "label: value" pairs near the model, for params still empty.
/// Same algorithm as tools/validate_rules.py <c>match_pdf()</c>.
///
/// Why proposals are off by default: on the project catalogues (multi-model tables, English, marketing pages)
/// they produced plausible but wrong values, e.g. genset fuel consumption read as "power".
/// </summary>
public sealed class PdfSpecMatcher
{
    private readonly SpecRuleSet _rules;
    private readonly SpecTextExtractor _extractor;

    public PdfSpecMatcher(SpecRuleSet rules)
    {
        _rules = rules;
        _extractor = new SpecTextExtractor(rules);
    }

    public PdfScanResult Scan(string pdfPath, IReadOnlyList<AssetRecord> records)
    {
        var cfg = _rules.Pdf;
        var pdf = PdfTextReader.Read(pdfPath, cfg.MaxPagesPerFile);
        if (!string.IsNullOrEmpty(pdf.Error))
            return new PdfScanResult { FilePath = pdfPath, Error = pdf.Error };
        if (pdf.LooksScanned(cfg.MinTextCharsPerPage))
            return new PdfScanResult { FilePath = pdfPath, PageCount = pdf.PageCount, IsScanned = true };

        var result = new PdfScanResult { FilePath = pdfPath, PageCount = pdf.PageCount };
        var tokens = records
            .Select(r => (Record: r, Tokens: ModelTokens.From(ModelOf(r), cfg.MinModelTokenLength)))
            .Where(x => x.Tokens.Count > 0)
            .ToList();
        if (tokens.Count == 0) return result;

        var pages = pdf.Pages.Select(p => p.Split('\n')).ToList();
        var normPages = pages.Select(lines => lines.Select(ModelTokens.Normalize).ToArray()).ToList();
        var hitsByRecord = new Dictionary<AssetRecord, List<int>>();

        for (var pi = 0; pi < pages.Count; pi++)
        {
            var pageNorm = string.Concat(normPages[pi]);
            var onPage = tokens.Where(t => ModelTokens.AnyIn(t.Tokens, pageNorm)).ToList();
            if (onPage.Count == 0) continue;

            var confidence = onPage.Select(t => ModelOf(t.Record)).Distinct(StringComparer.OrdinalIgnoreCase).Count() switch
            {
                1 => ProposalConfidence.High,
                var n when n >= cfg.TableModelThreshold => ProposalConfidence.Low,
                _ => ProposalConfidence.Medium
            };

            foreach (var (rec, toks) in onPage)
            {
                if (!hitsByRecord.TryGetValue(rec, out var list)) hitsByRecord[rec] = list = new List<int>();
                list.Add(pi + 1);
                if (!cfg.ProposeValues) continue;

                var hit = Array.FindIndex(normPages[pi], l => ModelTokens.AnyIn(toks, l));
                if (hit < 0) continue; // model split across lines: link only, no window
                var from = Math.Max(0, hit - cfg.LinesBefore);
                var to = Math.Min(pages[pi].Length, hit + cfg.LinesAfter);
                var window = string.Join("\n", pages[pi][from..to]);

                foreach (var (param, value) in _extractor.Extract(window, rec.Family, pdf: true))
                {
                    if (!string.IsNullOrEmpty(rec.Get(param))) continue; // never propose over existing data
                    result.Proposals.Add(new ValueProposal
                    {
                        RecordKey = rec.Key, Param = param, Value = value, FilePath = pdfPath, Page = pi + 1,
                        Confidence = confidence, Snippet = window.Length > 400 ? window[..400] : window
                    });
                }
            }
        }

        foreach (var (rec, pagesHit) in hitsByRecord)
            result.Links.Add(new PdfPageLink(rec.Key, ModelOf(rec), pdfPath, pagesHit.Distinct().ToList()));
        return result;
    }

    /// <summary>Model code to search for: TS_MaSanPham, else the Revit/TS type name.</summary>
    private static string ModelOf(AssetRecord r) => !string.IsNullOrWhiteSpace(r.Model) ? r.Model : r.TypeName;
}
