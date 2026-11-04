using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DSSTech.Core.Licensing;

/// <summary>What a key grants: who, on which machine, until when.</summary>
public sealed class LicenseInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("h")]
    public string Holder { get; set; } = "";

    /// <summary>Machine code the key is bound to (<see cref="LicenseCodec.MachineCode"/>).</summary>
    [JsonPropertyName("m")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("i")]
    public string Issued { get; set; } = "";

    /// <summary>Last day the key works (yyyy-MM-dd, local date).</summary>
    [JsonPropertyName("e")]
    public string Expires { get; set; } = "";

    [JsonIgnore]
    public DateTime ExpiresDate =>
        DateTime.TryParseExact(Expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
}

/// <summary>
/// Offline license keys: "DSS1.&lt;payload&gt;.&lt;signature&gt;", the payload signed with the publisher's RSA private
/// key. The add-in holds only the public key, so it can check a key but not make one.
/// </summary>
public static class LicenseCodec
{
    private const string Prefix = "DSS1";

    public static string Issue(LicenseInfo info, RSA privateKey)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info));
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{Prefix}.{B64(payload)}.{B64(signature)}";
    }

    /// <summary>Reads a key and checks its signature; the dates and machine are checked by <see cref="Check"/>.</summary>
    public static bool TryRead(string? key, RSA publicKey, out LicenseInfo info, out string error)
    {
        info = new LicenseInfo();
        error = "";
        var text = new string((key ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        var parts = text.Split('.');
        if (parts.Length != 3 || parts[0] != Prefix)
        {
            error = "Key không đúng định dạng (bắt đầu bằng DSS1.).";
            return false;
        }

        try
        {
            var payload = FromB64(parts[1]);
            if (!publicKey.VerifyData(payload, FromB64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                error = "Key không hợp lệ (sai chữ ký).";
                return false;
            }

            info = JsonSerializer.Deserialize<LicenseInfo>(payload) ?? new LicenseInfo();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            error = "Key không hợp lệ (dữ liệu hỏng).";
            return false;
        }
    }

    /// <summary>Empty when the key works on this machine today; else the reason in Vietnamese.</summary>
    public static string Check(LicenseInfo info, string machine, DateTime today)
    {
        if (!string.Equals(info.Machine, machine, StringComparison.OrdinalIgnoreCase))
            return "Key cấp cho máy khác (mã máy không khớp).";
        if (today.Date > info.ExpiresDate)
            return $"Key đã hết hạn ngày {info.ExpiresDate:dd/MM/yyyy}.";
        return "";
    }

    /// <summary>Short machine code from a raw machine id: 20 hex digits in groups of 4.</summary>
    public static string MachineCode(string raw)
    {
        using var sha = SHA256.Create();
        var hex = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes("DSSTech.Machine|" + raw.Trim().ToUpperInvariant())));
        return string.Join("-", Enumerable.Range(0, 5).Select(i => hex.Substring(i * 4, 4)));
    }

    /// <summary>Accepts a machine code typed with or without dashes / spaces.</summary>
    public static string NormalizeMachine(string code)
    {
        var hex = new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return hex.Length == 20 ? string.Join("-", Enumerable.Range(0, 5).Select(i => hex.Substring(i * 4, 4))) : (code ?? "").Trim().ToUpperInvariant();
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
    }
}
