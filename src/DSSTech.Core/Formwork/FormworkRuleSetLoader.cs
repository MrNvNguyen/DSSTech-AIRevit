using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DSSTech.Core.Formwork;

public static class FormworkRuleSetLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static FormworkRuleSet Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Formwork rules JSON not found.", path);

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<FormworkRulesFile>(json, Options)
                  ?? throw new InvalidDataException("Formwork rules JSON invalid.");

        return new FormworkRuleSet
        {
            Settings = new FormworkSettings
            {
                ProbeEpsilonMm = dto.ProbeEpsilonMm,
                VerticalToleranceDeg = dto.VerticalToleranceDeg,
                UseRevitJoinOrder = dto.UseRevitJoinOrder,
                GroundContactAutoDetect = dto.GroundContactAutoDetect,
                GroundContactToleranceMm = dto.GroundContactToleranceMm,
                SlabOnGradeSkipsBottom = dto.SlabOnGradeSkipsBottom,
                SlabOpeningThresholdM2 = dto.SlabOpeningThresholdM2 > 0 ? dto.SlabOpeningThresholdM2 : 1.0,
                SlabDeductSupportContact = dto.SlabDeductSupportContact
            },
            Rules = dto.Rules ?? []
        };
    }

    public static string Sha256Hex(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>N1-parity defaults when no file is available (tests / fallback).</summary>
    public static FormworkRuleSet CreateN1ParityDefaults() => new()
    {
        Settings = new FormworkSettings
        {
            ProbeEpsilonMm = 5,
            VerticalToleranceDeg = 15,
            GroundContactAutoDetect = false,
            GroundContactToleranceMm = 50,
            SlabOnGradeSkipsBottom = true,
            SlabDeductSupportContact = true
        },
        Rules =
        [
            new FormworkRule
            {
                Kind = "Column",
                Categories = ["OST_StructuralColumns"],
                Faces = ["Side"],
                Priority = 1
            },
            new FormworkRule
            {
                Kind = "Wall",
                Categories = ["OST_Walls"],
                Faces = ["Side"],
                Priority = 1,
                StructuralOnly = true
            },
            new FormworkRule
            {
                Kind = "Beam",
                Categories = ["OST_StructuralFraming"],
                Faces = ["Side", "Bottom"],
                Priority = 3,
                DropBottomWhenGroundContact = true
            },
            new FormworkRule
            {
                Kind = "Slab",
                Categories = ["OST_Floors"],
                Faces = ["Side", "Bottom"],
                Priority = 4,
                DropBottomWhenGroundContact = true
            },
            new FormworkRule
            {
                Kind = "Pile",
                Categories = ["OST_StructuralFoundation"],
                TypePattern = "pile|cọc|coc",
                Skip = true
            },
            new FormworkRule
            {
                Kind = "Footing",
                Categories = ["OST_StructuralFoundation"],
                Faces = ["Side"],
                Priority = 5,
                DropBottomWhenGroundContact = true
            }
        ]
    };

    private sealed class FormworkRulesFile
    {
        public double ProbeEpsilonMm { get; set; } = 5;
        public double VerticalToleranceDeg { get; set; } = 15;
        public bool UseRevitJoinOrder { get; set; }
        public bool GroundContactAutoDetect { get; set; }
        public double GroundContactToleranceMm { get; set; } = 50;
        public bool SlabOnGradeSkipsBottom { get; set; } = true;
        public double SlabOpeningThresholdM2 { get; set; } = 1.0;
        public bool SlabDeductSupportContact { get; set; } = true;
        public List<FormworkRule>? Rules { get; set; }
    }
}
