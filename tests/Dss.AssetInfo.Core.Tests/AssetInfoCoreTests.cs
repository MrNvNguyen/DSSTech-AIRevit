using System.Text.Json;
using ClosedXML.Excel;
using Dss.AssetInfo.Core;
using Xunit;

namespace Dss.AssetInfo.Core.Tests;

/// <summary>
/// Parity with tools/validate_rules.py: Fixtures/reference_cases.json was produced by the Python reference
/// on the same asset-spec-rules.json. If a rule changes, regenerate the fixture and re-run both.
/// </summary>
public class AssetInfoCoreTests
{
    private static readonly string Base = AppContext.BaseDirectory;
    private static readonly SpecRuleSet Rules = SpecRuleSet.Load(Path.Combine(Base, "Config", "asset-spec-rules.json"));

    public static IEnumerable<object[]> SpecCases() => Cases("specCases");
    public static IEnumerable<object[]> PdfCases() => Cases("pdfCases");

    private static IEnumerable<object[]> Cases(string key)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Base, "Fixtures", "reference_cases.json")));
        var i = 0;
        foreach (var c in doc.RootElement.GetProperty(key).EnumerateArray())
        {
            var expected = c.GetProperty("expected").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            yield return new object[] { i++, c.GetProperty("family").GetString()!, c.GetProperty("text").GetString()!, expected };
        }
    }

    [Theory]
    [MemberData(nameof(SpecCases))]
    public void Extract_matches_python_reference(int index, string family, string text, Dictionary<string, string> expected)
    {
        var got = new SpecTextExtractor(Rules).Extract(text, family);
        Assert.True(expected.Count == got.Count && expected.All(kv => got.TryGetValue(kv.Key, out var v) && v == kv.Value),
            $"case {index}: expected {Fmt(expected)} got {Fmt(got)}");
    }

    [Theory]
    [MemberData(nameof(PdfCases))]
    public void Extract_pdf_mode_matches_python_reference(int index, string family, string text, Dictionary<string, string> expected)
    {
        var got = new SpecTextExtractor(Rules).Extract(text, family, pdf: true);
        Assert.True(expected.Count == got.Count && expected.All(kv => got.TryGetValue(kv.Key, out var v) && v == kv.Value),
            $"pdf case {index}: expected {Fmt(expected)} got {Fmt(got)}");
    }

    [Fact]
    public void Model_tokens_match_python_reference()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Base, "Fixtures", "reference_cases.json")));
        foreach (var c in doc.RootElement.GetProperty("tokenCases").EnumerateArray())
        {
            var expected = c.GetProperty("expected").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            Assert.Equal(expected, ModelTokens.From(c.GetProperty("model").GetString(), 4));
        }
    }

    [Fact]
    public void Model_tokens_need_letter_and_digit()
    {
        // "UPS SDU 4000" must not produce "4000" — it falsely hit a genset catalogue ("16V4000") in testing.
        var t = ModelTokens.From("UPS SDU 4000 A5");
        Assert.DoesNotContain("4000", t);
        Assert.DoesNotContain("UPS", t);
        Assert.Contains("SDU4000", t);
    }

    [Theory]
    [InlineData("GT01-H-VRF", "HVAC")]
    [InlineData("GT01-C-BOM", "BOM")]
    [InlineData("GT01-D-NL", "BOM")]
    [InlineData("GT02-D-DMD", "DEN")]
    [InlineData("DN-B-BMS", "DIENNHE")]
    public void Family_from_sheet_name(string sheet, string family) => Assert.Equal(family, Rules.FamilyForSheet(sheet));

    [Fact]
    public void Form_reader_uses_TS_row_and_fills_empty_from_spec()
    {
        var path = Path.Combine(Path.GetTempPath(), $"asset-form-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                wb.Worksheets.Add("MUC_LUC").Cell(1, 1).Value = "index sheet, must be skipped";
                var ws = wb.Worksheets.Add("GT01-H-QTG");
                ws.Cell(3, 1).Value = "ID"; ws.Cell(3, 2).Value = "Tên vật tư, thiết bị"; ws.Cell(3, 3).Value = "Quy cách, Thông số kỹ thuật";
                ws.Cell(3, 4).Value = "Công suất"; ws.Cell(3, 5).Value = "Mã sản phẩm";
                ws.Cell(5, 1).Value = "TS_ID"; ws.Cell(5, 2).Value = "TS_TenVatTu"; ws.Cell(5, 3).Value = "TS_QuyCach";
                ws.Cell(5, 4).Value = "TS_CongSuat"; ws.Cell(5, 5).Value = "TS_MaSanPham";
                ws.Cell(6, 1).Value = "GT01-H-QTG-001"; ws.Cell(6, 2).Value = "Quạt hút";
                ws.Cell(6, 3).Value = "- Lưu lượng: 2466CMH - Công suất: 0.162kW\n- Tốc độ: 1405RPM";
                ws.Cell(6, 5).Value = "APL 355";
                ws.Cell(7, 1).Value = "GT01-H-QTG-002"; ws.Cell(7, 4).Value = 0.35; // number cell → invariant text
                wb.SaveAs(path);
            }

            var res = new AssetFormReader(Rules).Read(path);
            Assert.Equal(1, res.SheetsRead);
            Assert.Equal(2, res.Records.Count);
            var r1 = res.Records[0];
            Assert.Equal("HVAC", r1.Family);
            Assert.Equal("0.162kW", r1.Get("TS_CongSuat"));     // empty cell filled from Quy cách
            Assert.Equal("2466CMH", r1.Get("TS_LuuLuongGio"));
            Assert.StartsWith("Tự tách", r1.ValueSources["TS_CongSuat"]);
            Assert.Equal("0.35", res.Records[1].Get("TS_CongSuat"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_set_respects_overwrite_flag()
    {
        var r = new AssetRecord();
        Assert.True(r.Set("TS_CongSuat", "1 kW", "a"));
        Assert.False(r.Set("TS_CongSuat", "2 kW", "b", overwrite: false));
        Assert.Equal("1 kW", r.Get("TS_CongSuat"));
        Assert.False(r.Set("NotTs", "x", "c"));
    }

    private static string Fmt(IReadOnlyDictionary<string, string> d) => "{" + string.Join(", ", d.Select(kv => $"{kv.Key}={kv.Value}")) + "}";
}
