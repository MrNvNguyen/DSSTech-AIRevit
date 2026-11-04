using System.Security.Cryptography;
using System.Text;
using DSSTech.Core.Licensing;

// DSSTech license keys for the publisher only. The private key never leaves this machine's folder below;
// the add-in carries the matching public key (printed by "init").
//
//   DSSTech.KeyGen                     interactive: asks holder, machine code, months
//   DSSTech.KeyGen issue --name "Cty A - Nguyen" --machine XXXX-XXXX-XXXX-XXXX-XXXX --months 6
//   DSSTech.KeyGen verify <key>        shows what a key says
//   DSSTech.KeyGen init                makes the key pair once (refuses to overwrite)
//   DSSTech.KeyGen machine             this machine's code (same as the add-in shows)
//   DSSTech.KeyGen selftest

Console.OutputEncoding = Encoding.UTF8;
var folder = Environment.GetEnvironmentVariable("DSSTECH_LICENSE_DIR") ??
             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DSSTech-License");
var privatePath = Path.Combine(folder, "dsstech-private-key.pem");
var publicPath = Path.Combine(folder, "dsstech-public-key.txt");
var logPath = Path.Combine(folder, "issued-keys.csv");

string Arg(string name) =>
    args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? "";

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "";
switch (command)
{
    case "init":
        if (File.Exists(privatePath))
        {
            Console.WriteLine($"Đã có khoá: {privatePath}\nKhông tạo lại (key cũ sẽ mất hiệu lực).");
            return 1;
        }

        Directory.CreateDirectory(folder);
        using (var rsa = RSA.Create(2048))
        {
            File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem());
            var pub = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            File.WriteAllText(publicPath, pub);
            Console.WriteLine($"Khoá bí mật: {privatePath}  (SAO LƯU, KHÔNG gửi ai)");
            Console.WriteLine($"Khoá công khai (dán vào add-in):\n{pub}");
        }

        return 0;

    case "verify":
    {
        using var pub = LoadPublic();
        if (!LicenseCodec.TryRead(args.Skip(1).FirstOrDefault(), pub, out var info, out var error))
        {
            Console.WriteLine(error);
            return 1;
        }

        Console.WriteLine($"Hợp lệ. Người dùng: {info.Holder}; mã máy: {info.Machine}; cấp {info.Issued}; hạn {info.Expires}; id {info.Id}");
        return 0;
    }

    case "machine":
    {
        var raw = OperatingSystem.IsWindows()
            ? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")?.GetValue("MachineGuid") as string
            : null;
        Console.WriteLine(LicenseCodec.MachineCode(string.IsNullOrWhiteSpace(raw) ? Environment.MachineName : raw!));
        return 0;
    }

    case "selftest":
        return SelfTest();

    default:
    {
        var name = Arg("--name");
        var machine = Arg("--machine");
        var monthsText = Arg("--months");
        if (command != "issue")
        {
            Console.WriteLine("DSSTech — cấp key bản quyền");
            Console.Write("Tên người dùng / công ty: ");
            name = Console.ReadLine() ?? "";
            Console.Write("Mã máy (người dùng gửi, dạng XXXX-XXXX-XXXX-XXXX-XXXX): ");
            machine = Console.ReadLine() ?? "";
            Console.Write("Số tháng sử dụng [6]: ");
            monthsText = Console.ReadLine() ?? "";
        }

        var months = int.TryParse(monthsText, out var m) && m > 0 ? m : 6;
        machine = LicenseCodec.NormalizeMachine(machine);
        if (string.IsNullOrWhiteSpace(name) || machine.Length != 24)
        {
            Console.WriteLine("Thiếu tên hoặc mã máy sai (20 ký tự, 5 nhóm 4).");
            return 1;
        }

        using var rsa = LoadPrivate();
        var info = new LicenseInfo
        {
            Id = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Holder = name.Trim(),
            Machine = machine,
            Issued = DateTime.Today.ToString("yyyy-MM-dd"),
            Expires = DateTime.Today.AddMonths(months).ToString("yyyy-MM-dd")
        };
        var key = LicenseCodec.Issue(info, rsa);
        if (!File.Exists(logPath))
            File.WriteAllText(logPath, "id,holder,machine,issued,expires,key\n", Encoding.UTF8);
        File.AppendAllText(logPath, $"{info.Id},\"{info.Holder.Replace("\"", "'")}\",{info.Machine},{info.Issued},{info.Expires},{key}\n", Encoding.UTF8);
        Console.WriteLine($"\nKey cho {info.Holder} (hạn {DateTime.Today.AddMonths(months):dd/MM/yyyy}):\n\n{key}\n");
        Console.WriteLine($"Đã ghi sổ: {logPath}");
        if (command != "issue")
        {
            Console.WriteLine("Nhấn Enter để đóng.");
            Console.ReadLine();
        }

        return 0;
    }
}

RSA LoadPrivate()
{
    if (!File.Exists(privatePath))
        throw new FileNotFoundException($"Chưa có khoá bí mật {privatePath}. Chạy: DSSTech.KeyGen init");
    var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(privatePath));
    return rsa;
}

RSA LoadPublic()
{
    var rsa = RSA.Create();
    if (File.Exists(privatePath))
        rsa.ImportFromPem(File.ReadAllText(privatePath));
    else
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(File.ReadAllText(publicPath).Trim()), out _);
    return rsa;
}

static int SelfTest()
{
    using var signer = RSA.Create(2048);
    using var checker = RSA.Create();
    checker.ImportSubjectPublicKeyInfo(signer.ExportSubjectPublicKeyInfo(), out _);
    var machine = LicenseCodec.MachineCode("{1234-ABCD}");
    var info = new LicenseInfo { Id = "T1", Holder = "Thử", Machine = machine, Issued = "2026-11-04", Expires = "2027-05-04" };
    var key = LicenseCodec.Issue(info, signer);
    var failures = new List<string>();
    if (!LicenseCodec.TryRead(key, checker, out var back, out var err) || back.Holder != "Thử")
        failures.Add("read: " + err);
    if (LicenseCodec.Check(back, machine, new DateTime(2027, 5, 4)) != "")
        failures.Add("last day should work");
    if (LicenseCodec.Check(back, machine, new DateTime(2027, 5, 5)) == "")
        failures.Add("expired key passed");
    if (LicenseCodec.Check(back, LicenseCodec.MachineCode("other"), new DateTime(2027, 1, 1)) == "")
        failures.Add("other machine passed");
    // Change one character of the payload: the signature must fail.
    var parts = key.Split('.');
    var tampered = $"{parts[0]}.{(parts[1][5] == 'A' ? parts[1].Remove(5, 1).Insert(5, "B") : parts[1].Remove(5, 1).Insert(5, "A"))}.{parts[2]}";
    if (LicenseCodec.TryRead(tampered, checker, out _, out _))
        failures.Add("tampered key passed");
    using var stranger = RSA.Create(2048);
    if (LicenseCodec.TryRead(LicenseCodec.Issue(info, stranger), checker, out _, out _))
        failures.Add("key from another private key passed");
    if (!LicenseCodec.TryRead(" " + key.Insert(40, "\r\n") + " ", checker, out _, out _))
        failures.Add("pasted key with line break failed");
    if (LicenseCodec.NormalizeMachine(machine.Replace("-", "").ToLowerInvariant()) != machine)
        failures.Add("machine normalize");
    Console.WriteLine(failures.Count == 0 ? "PASS: license" : "FAIL: " + string.Join("; ", failures));
    return failures.Count == 0 ? 0 : 1;
}
