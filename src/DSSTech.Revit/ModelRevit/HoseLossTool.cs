using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>«Tổn thất ống mềm»: each sprinkler hose's friction loss, from its maker's equivalent length.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class HoseLossCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
        HoseLossTool.Run(data.Application);
}

/// <summary>
/// One catalogue row: a maker's hose (model, listing) of a length to a head outlet (1/2", 3/4"), rated at its most
/// bends as so many metres of 1" Schedule 40 pipe (C = 120).
/// </summary>
internal sealed class HoseRating
{
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string Listing { get; set; } = "UL";
    public string Outlet { get; set; } = "1/2";
    public double LengthMm { get; set; }
    public int Bends { get; set; }
    public double? EquivalentM { get; set; }
    public string Source { get; set; } = "";

    public string Product => $"{Brand} {Model} ({Listing})";
}

internal sealed class HoseCatalogue
{
    public string Note { get; set; } = "";
    public List<HoseRating> Hoses { get; set; } = new();
}

/// <summary>
/// The selected flexible hoses (else those of the active view): the stock length from Comments (written by «Nối đầu
/// phun»), the head's outlet (1/2" or 3/4") from the head the hose feeds, the equivalent length from the catalogue
/// (built in from the makers' sheets, the company file adding to or replacing rows), the loss at the head's flow
/// (K, P) by Hazen–Williams, written into Comments after the length. A row the catalogue lacks is reported, never guessed.
/// </summary>
internal static class HoseLossTool
{
    public static string CatalogueFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DSSTech", "sprinkler-hose.json");

    private const double Ft = 0.3048;

    /// <summary>Rows of one table: the five stock lengths, their most bends and equivalent lengths (ft) to each outlet.</summary>
    private static IEnumerable<HoseRating> Table(string brand, string model, string listing, string source, int[] bends,
        double[] halfFt, double[] threeQuarterFt, double[]? lengths = null) =>
        (lengths ?? SprinklerHose.LengthsMm).SelectMany((l, i) => new[]
        {
            new HoseRating { Brand = brand, Model = model, Listing = listing, Outlet = "1/2", LengthMm = l, Bends = bends[i], EquivalentM = Math.Round(halfFt[i] * Ft, 2), Source = source },
            new HoseRating { Brand = brand, Model = model, Listing = listing, Outlet = "3/4", LengthMm = l, Bends = bends[i], EquivalentM = Math.Round(threeQuarterFt[i] * Ft, 2), Source = source }
        });

    /// <summary>From the makers' catalogues anh Nguyên sent (GS Fire Safety 09.2024 tables 3–5; DAEJIN friction loss data).</summary>
    private static readonly List<HoseRating> BuiltIn = new[]
    {
        Table("GS Fire Safety", "G-FLEX 25N", "UL", "GS catalogue 09.2024, bảng 3", new[] { 2, 3, 3, 3, 3 },
            new[] { 33.0, 57, 68, 82, 98 }, new[] { 38.0, 62, 73, 89, 104 }),
        Table("GS Fire Safety", "G-FLEX 25B", "UL", "GS catalogue 09.2024, bảng 4", new[] { 2, 3, 3, 3, 3 },
            new[] { 33.0, 57, 68, 82, 98 }, new[] { 38.0, 62, 73, 89, 104 }),
        Table("GS Fire Safety", "G-FLEX 25B", "FM", "GS catalogue 09.2024, bảng 5", new[] { 1, 2, 3, 3, 4 },
            new[] { 26.0, 46.1, 59.6, 79.7, 100 }, new[] { 23.7, 44.4, 58.3, 79, 99.9 }),
        Table("DAEJIN", "DJ28B", "UL", "DAEJIN catalogue, DJ28B UL friction loss", new[] { 3, 5, 7, 9, 12 },
            new[] { 18.0, 23, 29, 39, 50 }, new[] { 20.0, 24, 31, 40, 70 }),
        Table("DAEJIN", "DJ28B", "FM", "DAEJIN catalogue, DJ28B FM friction loss", new[] { 1, 2, 3, 4, 4 },
            new[] { 22.9, 34.6, 41.0, 50.4, 58.5 }, new[] { 15.7, 26.9, 34.8, 43.8, 53.4 }),
        Table("DAEJIN", "DJ25UB", "UL", "DAEJIN catalogue, DJ25UB UL friction loss", new[] { 2, 3, 3, 3, 3 },
            new[] { 32.0, 55, 64, 75, 81 }, new[] { 31.0, 50, 57, 68, 79 }),
        // DJ25B is listed at 610 / 1200 / 1500 / 1800 mm only.
        Table("DAEJIN", "DJ25B", "UL", "DAEJIN catalogue, DJ25B UL friction loss", new[] { 2, 3, 3 },
            new[] { 29.0, 68, 94 }, new[] { 36.0, 73, 99 }, new[] { 1200.0, 1500, 1800 })
    }.SelectMany(t => t).ToList();

    public static Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument;
        var doc = uidoc?.Document;
        var view = doc?.ActiveView;
        if (doc is null || uidoc is null || view is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        const string title = "DSSTech — Tổn thất ống mềm";
        var hoses = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<FlexPipe>().ToList();
        if (hoses.Count == 0)
            hoses = new FilteredElementCollector(doc, view.Id).OfClass(typeof(FlexPipe)).Cast<FlexPipe>().ToList();
        if (hoses.Count == 0)
        {
            TaskDialog.Show(title, "Không có ống mềm nào (chọn ống mềm, hoặc mở view có ống mềm đầu phun).");
            return Result.Cancelled;
        }

        var catalogue = Load();
        var products = catalogue.Select(h => h.Product).Distinct().ToList();
        var dlg = new ArchModelSetupWindow(title,
            $"{hoses.Count} ống mềm. Tổn thất = chiều dài tương đương (m ống thép 1\" Sch 40, C=120, ở số lần uốn tối đa theo catalogue) " +
            $"× Hazen–Williams tại lưu lượng đầu phun Q = K·√P; đầu ra 1/2\" / 3/4\" theo cỡ đầu phun. Số liệu GS Fire Safety và DAEJIN có sẵn; " +
            $"thêm / sửa trong {CatalogueFile}.", 480);
        var product = dlg.AddCombo("Hãng — model (chứng nhận)", products, products.FirstOrDefault());
        var k = dlg.AddInput("Hệ số K đầu phun (L/min/bar^½; K80 = K5.6)", "80");
        var p = dlg.AddInput("Áp lực tại đầu phun (bar)", "0.5");
        if (!dlg.ShowFor(uiApp.MainWindowHandle))
            return Result.Cancelled;
        var kValue = double.TryParse(k.Text.Trim(), out var kv) && kv > 0 ? kv : 80;
        var pValue = double.TryParse(p.Text.Trim(), out var pv) && pv > 0 ? pv : 0.5;
        var chosen = catalogue.Where(h => h.Product == (string?)product.SelectedItem).ToList();
        DoneReport.Show(title, Compute(doc, hoses, chosen, (string?)product.SelectedItem ?? "", kValue, pValue));
        return Result.Succeeded;
    }

    private static string Key(HoseRating r) => $"{r.Brand}|{r.Model}|{r.Listing}|{r.Outlet}|{r.LengthMm:0}".ToUpperInvariant();

    /// <summary>The built-in rows, with the company file's filled rows added or put in their place; the file written when missing.</summary>
    private static List<HoseRating> Load()
    {
        var rows = BuiltIn.ToDictionary(Key);
        try
        {
            if (File.Exists(CatalogueFile) && JsonSerializer.Deserialize<HoseCatalogue>(File.ReadAllText(CatalogueFile)) is { } read)
            {
                foreach (var r in read.Hoses.Where(r => r.EquivalentM is > 0))
                    rows[Key(r)] = r;
                // A file with no figure at all (the blank template of the first version) is replaced by the built-in rows.
                if (read.Hoses.Any(r => r.EquivalentM is > 0))
                    return rows.Values.ToList();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CatalogueFile)!);
            File.WriteAllText(CatalogueFile, JsonSerializer.Serialize(new HoseCatalogue
            {
                Note = "EquivalentM: chiều dài tương đương (m ống thép 1\" Sch 40, C=120) ở số lần uốn tối đa (Bends) theo catalogue / " +
                       "chứng nhận UL-FM. Outlet: đầu ra 1/2 hoặc 3/4. Thêm hãng / model bằng cách thêm dòng; dòng trùng hãng-model-chứng nhận-đầu ra-chiều dài thay số có sẵn.",
                Hoses = BuiltIn
            }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DssTechLog.Write($"hose loss: catalogue {CatalogueFile}: {ex.Message}");
        }

        return rows.Values.ToList();
    }

    private static readonly Regex LengthNote = new(@"L=(\d+)");
    private static readonly Regex LossNote = new(@"\s*·\s*ΔP=[^·]*");

    public static string Compute(Document doc, List<FlexPipe> hoses, List<HoseRating> ratings, string product, double k, double pressureBar)
    {
        var flow = HoseLoss.Flow(k, pressureBar);
        int done = 0;
        var missing = new SortedSet<string>();
        double worst = 0;
        using var tx = new Transaction(doc, "DSSTech hose loss");
        tx.Start();
        foreach (var hose in hoses)
        {
            var comments = hose.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            var text = comments?.AsString() ?? "";
            // The stock length «Nối đầu phun» wrote, else the hose's own length up to the next stock one.
            var lengthMm = LengthNote.Match(text) is { Success: true } m ? double.Parse(m.Groups[1].Value)
                : SprinklerHose.Pick(UnitConv.ToMm(hose.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0), SprinklerHose.LengthsMm) ?? 0;
            var outlet = Outlet(hose);
            if (ratings.FirstOrDefault(r => r.Outlet == outlet && Math.Abs(r.LengthMm - lengthMm) < 1) is not { EquivalentM: double equivalent } rating)
            {
                missing.Add(lengthMm > 0 ? $"{lengthMm:0} mm × {outlet}\"" : "ống không rõ chiều dài");
                continue;
            }

            var loss = HoseLoss.Bar(flow, equivalent);
            worst = Math.Max(worst, loss);
            comments?.Set(LossNote.Replace(text, "") +
                          $" · ΔP={loss:0.000} bar ({rating.Brand} {rating.Model} {rating.Listing}, {outlet}\", tối đa {rating.Bends} lần uốn 90°, Q={flow:0} L/min)");
            done++;
        }

        tx.Commit();
        var sb = new StringBuilder();
        sb.AppendLine($"Tổn thất ống mềm {product}: {done}/{hoses.Count} ống, Q = {flow:0.0} L/min (K{k:0}, {pressureBar:0.##} bar), lớn nhất {worst:0.000} bar; ghi vào Comments.");
        if (missing.Count > 0)
            sb.AppendLine($"Lỗi: chưa tính được {hoses.Count - done} ống — catalogue {product} không có {string.Join(", ", missing)}; " +
                          $"chọn model khác hoặc nhập vào {CatalogueFile}.");
        return sb.ToString();
    }

    /// <summary>The outlet the hose's head takes: 3/4" for a DN20 head, else 1/2" (the head a few elements on).</summary>
    private static string Outlet(FlexPipe hose)
    {
        var seen = new HashSet<ElementId> { hose.Id };
        var front = new List<Element> { hose };
        for (var d = 0; d < 4 && front.Count > 0; d++)
        {
            var next = new List<Element>();
            foreach (var e in front)
            {
                var cm = e is MEPCurve c ? c.ConnectorManager : (e as FamilyInstance)?.MEPModel?.ConnectorManager;
                if (cm is null)
                    continue;
                foreach (Connector c1 in cm.Connectors)
                {
                    foreach (Connector r in c1.AllRefs)
                    {
                        if (r.Owner is FamilyInstance { Category: { } cat } && cat.Id.Value == (long)BuiltInCategory.OST_Sprinklers)
                            return UnitConv.ToMm(r.Radius * 2) > 17 ? "3/4" : "1/2";
                        if (r.Owner is { } o && o is not Pipe { Location: LocationCurve { Curve: Line { Length: > 1.0 } } } && seen.Add(o.Id))
                            next.Add(o);
                    }
                }
            }

            front = next;
        }

        return "1/2";
    }
}
