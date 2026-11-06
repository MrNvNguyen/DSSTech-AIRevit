using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dss.AssetInfo.Core;

public sealed class PdfText
{
    public string FilePath { get; init; } = "";
    public int PageCount { get; init; }
    /// <summary>Text per page (index 0 = page 1), lines rebuilt from word positions.</summary>
    public List<string> Pages { get; } = new();
    public string Error { get; init; } = "";

    public bool LooksScanned(int minCharsPerPage) =>
        Pages.Count == 0 || Pages.Sum(p => p.Trim().Length) / (double)Pages.Count < minCharsPerPage;
}

/// <summary>Text from born-digital PDFs via PdfPig. Scanned PDFs have no text layer → reported, never guessed.</summary>
public static class PdfTextReader
{
    public static PdfText Read(string path, int maxPages)
    {
        try
        {
            using var doc = PdfDocument.Open(path);
            var n = Math.Min(doc.NumberOfPages, Math.Max(1, maxPages));
            var result = new PdfText { FilePath = path, PageCount = doc.NumberOfPages };
            for (var i = 1; i <= n; i++)
                result.Pages.Add(PageLines(doc.GetPage(i)));
            return result;
        }
        catch (Exception ex)
        {
            // Encrypted / damaged files: report and continue with the next file.
            return new PdfText { FilePath = path, Error = ex.Message };
        }
    }

    /// <summary>Group words into lines by baseline so "Label   value" stays on one line.</summary>
    private static string PageLines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();
        if (words.Count == 0) return "";

        var lines = new List<List<Word>>();
        foreach (var w in words)
        {
            var current = lines.Count > 0 ? lines[^1] : null;
            var tolerance = Math.Max(1.0, w.BoundingBox.Height) * 0.5;
            if (current is not null && Math.Abs(current[0].BoundingBox.Bottom - w.BoundingBox.Bottom) <= tolerance)
                current.Add(w);
            else
                lines.Add(new List<Word> { w });
        }
        return string.Join("\n", lines.Select(l => string.Join(" ", l.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text))));
    }
}
