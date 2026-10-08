using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

public sealed class LayerMap
{
    public string Discipline { get; set; } = "";
    public List<LayerRule> Rules { get; set; } = new List<LayerRule>();
    public List<string> IgnoreLayerPatterns { get; set; } = new List<string>();

    public static LayerMap Load(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var map = new LayerMap
        {
            Discipline = root.TryGetProperty("discipline", out var d) ? d.GetString() ?? "" : ""
        };

        if (root.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rules.EnumerateArray())
            {
                map.Rules.Add(new LayerRule
                {
                    Pattern = r.TryGetProperty("pattern", out var p) ? p.GetString() ?? "" : "",
                    Kind = r.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "",
                    RequireBlock = r.TryGetProperty("requireBlock", out var rb) && rb.ValueKind == JsonValueKind.True
                });
            }
        }

        if (root.TryGetProperty("ignoreLayerPatterns", out var ignore) && ignore.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in ignore.EnumerateArray())
            {
                var s = i.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    map.IgnoreLayerPatterns.Add(s);
            }
        }

        return map;
    }

    public LayerRule? Match(string layer)
    {
        foreach (var rule in Rules)
        {
            if (Regex.IsMatch(layer ?? "", rule.Pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
                return rule;
        }

        return null;
    }

    public bool IsIgnored(string layer)
    {
        if (string.Equals(layer, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer, "Defpoints", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer, "G-GRID", StringComparison.OrdinalIgnoreCase))
            return true;

        if (Regex.IsMatch(layer ?? "", @"-ANNO-", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            return true;

        return IgnoreLayerPatterns.Any(p =>
            Regex.IsMatch(layer ?? "", p, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
    }
}

public sealed class LayerRule
{
    public string Pattern { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool RequireBlock { get; set; }
}
