using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DSSTech.Revit.License;

internal sealed class LicenseFile
{
    public string? ActivatedKeyId { get; set; }
    public List<LicenseKey> Keys { get; set; } = new();
}

internal sealed class LicenseKey
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "user";
    public string Holder { get; set; } = "";
    public string Hwid { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string Status { get; set; } = "active";

    [JsonIgnore]
    public bool IsAdmin => string.Equals(Kind, "admin", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsExpired => Status == "revoked" || ExpiresUtc.ToUniversalTime() < DateTime.UtcNow;
}

internal static class LicenseStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DSSTech",
        "license.json");

    public static string HardwareId()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = key?.GetValue("MachineGuid") as string;
            if (!string.IsNullOrWhiteSpace(guid))
                return guid;
        }
        catch (Exception)
        {
            // The account window still opens when the registry key is unreadable.
        }

        return Environment.MachineName;
    }

    public static string VersionLabel()
    {
        var version = typeof(LicenseStore).Assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public static LicenseFile Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<LicenseFile>(File.ReadAllText(FilePath), Json);
                if (file is not null)
                    return file;
            }
        }
        catch (Exception)
        {
            // A broken local file is replaced by a fresh book on the next save.
        }

        var created = new LicenseFile();
        created.Keys.Add(NewKey("Quản trị máy này", admin: true, days: 3650));
        Save(created);
        return created;
    }

    public static void Save(LicenseFile file)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(file, Json));
    }

    public static LicenseKey? Activated(LicenseFile file) =>
        file.Keys.FirstOrDefault(k => string.Equals(k.Id, file.ActivatedKeyId, StringComparison.OrdinalIgnoreCase));

    public static string Activate(LicenseFile file, string keyId)
    {
        var id = (keyId ?? "").Trim();
        var key = file.Keys.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
        if (key is null)
            return "Key không có trong sổ máy này.";
        if (string.Equals(key.Status, "revoked", StringComparison.OrdinalIgnoreCase))
            return "Key đã thu hồi.";
        if (key.ExpiresUtc.ToUniversalTime() < DateTime.UtcNow)
            return "Key đã hết hạn.";

        var hwid = HardwareId();
        if (string.IsNullOrWhiteSpace(key.Hwid))
            key.Hwid = hwid;
        else if (!string.Equals(key.Hwid, hwid, StringComparison.OrdinalIgnoreCase))
            return "Key đã gắn một máy khác.";

        file.ActivatedKeyId = key.Id;
        Save(file);
        return "";
    }

    public static LicenseKey NewKey(string holder, bool admin, int days)
    {
        var bytes = new byte[8];
        RandomNumberGenerator.Create().GetBytes(bytes);
        var body = BitConverter.ToString(bytes).Replace("-", "");
        var prefix = admin ? "DSS-ADM-" : "DSS-";
        return new LicenseKey
        {
            Id = prefix + body[..4] + "-" + body[4..],
            Kind = admin ? "admin" : "user",
            Holder = string.IsNullOrWhiteSpace(holder) ? "Người dùng" : holder.Trim(),
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddDays(days),
            Status = "active"
        };
    }

    public static void Extend(LicenseKey key, int days)
    {
        var start = key.ExpiresUtc.ToUniversalTime() > DateTime.UtcNow
            ? key.ExpiresUtc.ToUniversalTime()
            : DateTime.UtcNow;
        key.ExpiresUtc = start.AddDays(days);
        if (!string.Equals(key.Status, "revoked", StringComparison.OrdinalIgnoreCase))
            key.Status = "active";
    }
}
