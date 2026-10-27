using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Per-project layer pick profile (Wave 2). Stored under %LOCALAPPDATA%\DSSTech\cad-profiles.
/// </summary>
public sealed class CadProjectProfile
{
    public string ProjectKey { get; set; } = "";
    public string? ParamPackage { get; set; }
    public string? ParamPart { get; set; }
    public string? ParamDiscipline { get; set; }
    public List<CadLayerProfileEntry> Layers { get; set; } = new();
}

public sealed class CadLayerProfileEntry
{
    public string LayerName { get; set; } = "";
    public string PartKind { get; set; } = "";
    public string? RevitTypeHint { get; set; }
    public string? AnnotationSample { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public static class CadProjectProfileStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string ProfilesRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSSTech", "cad-profiles");

    public static CadProjectProfile Load(string projectKey)
    {
        var path = ProfilePath(projectKey);
        if (!File.Exists(path))
            return new CadProjectProfile { ProjectKey = projectKey };

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CadProjectProfile>(json, Json) ?? new CadProjectProfile { ProjectKey = projectKey };
        }
        catch
        {
            return new CadProjectProfile { ProjectKey = projectKey };
        }
    }

    public static bool TryResolvePart(string projectKey, string layerName, out ModelRevitPartKind part)
    {
        part = default;
        var profile = Load(projectKey);
        var entry = profile.Layers
            .Where(l => string.Equals(l.LayerName, layerName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.UpdatedUtc)
            .FirstOrDefault();
        if (entry is null || string.IsNullOrWhiteSpace(entry.PartKind))
            return false;
        return Enum.TryParse(entry.PartKind, ignoreCase: true, out part);
    }

    public static void RememberLayer(
        string projectKey,
        string layerName,
        ModelRevitPartKind part,
        AnnotationSize size)
    {
        var profile = Load(projectKey);
        profile.ProjectKey = projectKey;
        var entry = profile.Layers.FirstOrDefault(l =>
            string.Equals(l.LayerName, layerName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.PartKind, part.ToString(), StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new CadLayerProfileEntry();
            profile.Layers.Add(entry);
        }

        entry.LayerName = layerName;
        entry.PartKind = part.ToString();
        entry.RevitTypeHint = part switch
        {
            ModelRevitPartKind.Column => AnnotationSizeParser.ColumnFamilyTypeHint(size),
            ModelRevitPartKind.StructuralWall => AnnotationSizeParser.WallTypeHint(size),
            ModelRevitPartKind.Floor => AnnotationSizeParser.FloorTypeHint(size),
            ModelRevitPartKind.Beam => AnnotationSizeParser.BeamFamilyTypeHint(size),
            _ => size.Raw
        };
        entry.AnnotationSample = size.Raw;
        entry.UpdatedUtc = DateTime.UtcNow;

        Directory.CreateDirectory(ProfilesRoot);
        File.WriteAllText(ProfilePath(projectKey), JsonSerializer.Serialize(profile, Json));
    }

    public static void RememberParamPackage(string projectKey, string package, string? part, string? discipline)
    {
        var profile = Load(projectKey);
        profile.ProjectKey = projectKey;
        profile.ParamPackage = package;
        profile.ParamPart = part;
        profile.ParamDiscipline = discipline;
        Directory.CreateDirectory(ProfilesRoot);
        File.WriteAllText(ProfilePath(projectKey), JsonSerializer.Serialize(profile, Json));
    }

    public static bool TryGetParamPackage(string projectKey, out string package, out string? part, out string? discipline)
    {
        package = "";
        part = null;
        discipline = null;
        var profile = Load(projectKey);
        if (string.IsNullOrWhiteSpace(profile.ParamPackage))
            return false;
        package = profile.ParamPackage ?? "";
        part = profile.ParamPart;
        discipline = profile.ParamDiscipline;
        return true;
    }

    private static string ProfilePath(string projectKey)
    {
        var safe = string.Join("_", projectKey.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "default";
        return Path.Combine(ProfilesRoot, $"{safe}.json");
    }
}
