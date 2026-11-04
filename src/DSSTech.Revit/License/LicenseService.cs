using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Autodesk.Revit.UI;
using DSSTech.Core.Licensing;

namespace DSSTech.Revit.License;

/// <summary>
/// This machine's license: the signed key in %AppData%\DSSTech\license.key, checked against the publisher's public
/// key, this machine's code and today's date. Every DSSTech button but «Tài khoản» and «Hướng dẫn» needs it.
/// </summary>
internal static class LicenseService
{
    /// <summary>Publisher public key (DSSTech.KeyGen init). Keys signed by any other private key fail.</summary>
    private const string PublicKey =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAvHqWlboo5We1ZnGlfjUhucwIZG1wyJKHQtZ8ZBHkUirFEwkMtcK87oyZXuEJqu/id2m9oY7Zbv3+" +
        "NvK+LQPWKSi8fCnMQ6agwedCZAHNKca+oHz77mCe2rCP61L6MnpAa+l2d7ODXjvT7gPyW/PMeyxvnwXLpGouFWqXhi3d4AaO8oMas2uxlxMPGaSrDUKGnxdr" +
        "WC6BWfPQe9eUd82yrh+We8qZGBFZ7FQL6m1Y3NRfI+cMUVvHATMUYlbm+7YAYs/vT1umvgNwtny2FG3j/OEAsMMn36tGL/hfxCy536qe3Fdq5gL/mK0V53pS" +
        "DBNSc3OTLOuqmddfeYhSn9u3nQIDAQAB";

    /// <summary>The clock may run this far behind the last day seen before it counts as turned back.</summary>
    private const int ClockSlackDays = 2;

    private static readonly object Gate = new();
    private static DateTime _checkedAt = DateTime.MinValue;
    private static bool _valid;
    private static LicenseInfo? _info;
    private static string _reason = "";

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DSSTech");
    private static string KeyPath => Path.Combine(Folder, "license.key");
    private static string SeenPath => Path.Combine(Folder, "license.seen");

    public static string MachineCode { get; } = LicenseCodec.MachineCode(RawMachineId());

    /// <summary>Cheap enough for ribbon availability: the key is read again at most every 10 minutes.</summary>
    public static bool IsValid
    {
        get
        {
            lock (Gate)
            {
                if ((DateTime.Now - _checkedAt).TotalMinutes > 10)
                    Refresh();
                return _valid;
            }
        }
    }

    public static LicenseInfo? Info
    {
        get
        {
            _ = IsValid;
            return _info;
        }
    }

    /// <summary>Why the license does not work (empty when it does).</summary>
    public static string Reason
    {
        get
        {
            _ = IsValid;
            return _reason;
        }
    }

    /// <summary>Checks a pasted key and keeps it when it works on this machine; returns the error, else empty.</summary>
    public static string Activate(string key)
    {
        var (info, error) = Evaluate(key);
        if (error.Length > 0)
            return error;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(KeyPath, new string(key.Where(c => !char.IsWhiteSpace(c)).ToArray()));
        lock (Gate)
        {
            _info = info;
            _valid = true;
            _reason = "";
            _checkedAt = DateTime.Now;
        }

        Remember(DateTime.Today);
        return "";
    }

    /// <summary>Shown when a DSSTech action runs without a license (e.g. from the Control Center pane).</summary>
    public static bool Require()
    {
        if (IsValid)
            return true;
        TaskDialog.Show("DSSTech", "DSSTech chưa được kích hoạt trên máy này.\n" + Reason +
                                   "\nVào Thiết lập ▸ Tài khoản: gửi mã máy để nhận key, dán key rồi bấm Kích hoạt.");
        return false;
    }

    private static void Refresh()
    {
        _checkedAt = DateTime.Now;
        string key;
        try
        {
            key = File.Exists(KeyPath) ? File.ReadAllText(KeyPath) : "";
        }
        catch (IOException)
        {
            key = "";
        }

        if (key.Trim().Length == 0)
        {
            (_info, _valid, _reason) = (null, false, "Chưa có key.");
            return;
        }

        var (info, error) = Evaluate(key);
        _info = info;
        _valid = error.Length == 0;
        _reason = error;
        if (_valid)
            Remember(DateTime.Today);
    }

    private static (LicenseInfo? Info, string Error) Evaluate(string key)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        if (!LicenseCodec.TryRead(key, rsa, out var info, out var error))
            return (null, error);
        var today = DateTime.Today;
        if (LastSeen() is DateTime seen && today < seen.AddDays(-ClockSlackDays))
            return (info, $"Ngày trên máy ({today:dd/MM/yyyy}) lùi so với lần dùng trước ({seen:dd/MM/yyyy}). Chỉnh lại đồng hồ máy.");
        var check = LicenseCodec.Check(info, MachineCode, today);
        return (info, check);
    }

    private static DateTime? LastSeen()
    {
        try
        {
            return File.Exists(SeenPath) &&
                   DateTime.TryParseExact(File.ReadAllText(SeenPath).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void Remember(DateTime day)
    {
        try
        {
            if (LastSeen() is DateTime seen && seen >= day)
                return;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(SeenPath, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DssTechLog.Write("license seen: " + ex.Message);
        }
    }

    private static string RawMachineId()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid))
                return guid;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            DssTechLog.Write("machine id: " + ex.Message);
        }

        return Environment.MachineName;
    }
}

/// <summary>«Tài khoản» and «Hướng dẫn»: usable before a key and before any project is open.</summary>
public sealed class AlwaysAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, Autodesk.Revit.DB.CategorySet selectedCategories) => true;
}

/// <summary>Ribbon gate: DSSTech buttons work only with a license, and (as before) only with a project open.</summary>
public sealed class LicensedAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, Autodesk.Revit.DB.CategorySet selectedCategories) =>
        applicationData.ActiveUIDocument is not null && LicenseService.IsValid;
}
