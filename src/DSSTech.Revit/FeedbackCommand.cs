using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSSTech.Revit.License;
using DSSTech.Revit.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace DSSTech.Revit;

/// <summary>
/// Nút "Góp ý · Feedback": người dùng ghi lỗi / đề xuất, add-in gói kèm log + file đọc CAD + ảnh
/// thành một file zip trên Desktop, gửi thẳng lên web app Apps Script (mail + Drive); không gửi được thì mở
/// ứng dụng mail soạn sẵn gửi về DSSTech.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class FeedbackCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var app = commandData.Application;
        var dlg = new FeedbackWindow(app.Application.VersionName, app.ActiveUIDocument?.Document?.Title ?? "");
        dlg.SetOwner(app.MainWindowHandle);
        dlg.ShowDialog();
        return Result.Succeeded;
    }
}

internal sealed class FeedbackWindow : DssDialog
{
    /// <summary>Hộp thư nhận góp ý.</summary>
    private const string Recipient = "eng.vnnguyen@gmail.com";

    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSSTech");

    private readonly string _revit;
    private readonly string _model;
    private readonly ComboBox _kind;
    /// <summary>Các nút trên ribbon, theo thứ tự panel (giữ khớp với App.cs / docs/RIBBON_UI.md).</summary>
    private static readonly string[] Tools =
    {
        "Chung / nhiều tool",
        "Thiết lập ▸ Tài khoản", "Thiết lập ▸ Tham số QS",
        "Kiến trúc ▸ Trục", "Kiến trúc ▸ Level", "Kiến trúc ▸ Tường", "Kiến trúc ▸ Cửa", "Kiến trúc ▸ Cửa sổ",
        "Kiến trúc ▸ Khớp kết cấu", "Kiến trúc ▸ Lanh tô", "Kiến trúc ▸ Lỗ mở",
        "Kiến trúc ▸ Phòng", "Kiến trúc ▸ Hoàn thiện", "Kiến trúc ▸ Trần",
        "Kết cấu ▸ Cột", "Kết cấu ▸ Vách", "Kết cấu ▸ Dầm", "Kết cấu ▸ Sàn", "Kết cấu ▸ Móng bè", "Kết cấu ▸ Móng băng",
        "Kết cấu ▸ Cọc", "Kết cấu ▸ Đài / móng đơn",
        "MEPF ▸ ĐHKK ▸ Ống gió", "MEPF ▸ ĐHKK ▸ Ống Chiller", "MEPF ▸ ĐHKK ▸ Thoát nước ngưng",
        "MEPF ▸ ĐHKK ▸ Thiết bị ĐHKK", "MEPF ▸ ĐHKK ▸ Nối ống mềm",
        "MEPF ▸ Điện ▸ Máng / thang cáp", "MEPF ▸ Điện ▸ Busway",
        "MEPF ▸ Nước ▸ Cấp nước", "MEPF ▸ Nước ▸ Thoát nước",
        "MEPF ▸ PCCC ▸ Sprinkler / chữa cháy", "MEPF ▸ PCCC ▸ Tăng áp / hút khói", "MEPF ▸ PCCC ▸ Tủ chữa cháy & đầu phun",
        "MEPF ▸ Hệ thống", "MEPF ▸ Thiết bị tường", "MEPF ▸ Thiết bị trần",
        "Combine ▸ Combine", "Combine ▸ Clash Check", "Combine ▸ So sánh", "Combine ▸ Nâng sát dầm", "Combine ▸ Xung đột",
        "Khối lượng ▸ Chọn định mức", "Khối lượng ▸ Ván khuôn", "Khối lượng ▸ Xuất BOQ", "Khối lượng ▸ Bảng khối lượng",
        "Tài sản ▸ Asset Data", "Trợ lý AI"
    };

    private readonly ComboBox _tool;
    private readonly TextBox _text;
    private readonly TextBox _contact;
    private readonly CheckBox _logs;
    private readonly TextBlock _images;
    private readonly List<string> _imageFiles = new();

    public FeedbackWindow(string revit, string model)
        : base("DSSTech — Góp ý", "Báo lỗi hoặc đề xuất tính năng. Góp ý được gửi qua email về DSSTech để xem xét và sửa.", 640, 680)
    {
        _revit = revit;
        _model = model;
        _kind = DssTheme.Combo(new[] { "Lỗi — tool chạy sai hoặc báo lỗi", "Đề xuất — tính năng mới / cải tiến", "Khác" });
        // Chọn trong danh sách hoặc gõ tên khác.
        _tool = DssTheme.Combo(Tools);
        _tool.IsEditable = true;
        _tool.MaxDropDownHeight = 360;
        _text = DssTheme.Input("");
        _text.TextWrapping = TextWrapping.Wrap;
        _text.AcceptsReturn = true;
        _text.Height = 160;
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _contact = DssTheme.Input(LicenseService.Info?.Holder ?? "");
        _logs = DssTheme.Option("Kèm nhật ký DSSTech, file đọc CAD gần nhất và journal Revit (giúp tìm lỗi nhanh)", true);
        _images = new TextBlock { Foreground = DssTheme.Muted, TextWrapping = TextWrapping.Wrap, Text = "Chưa có ảnh", VerticalAlignment = VerticalAlignment.Center };

        var addImages = DssTheme.Quiet("Thêm ảnh chụp…");
        addImages.Click += (_, _) => PickImages();

        AddSection(1, "Nội dung",
            DssTheme.Field("Loại góp ý", _kind),
            DssTheme.Field("Tool / nút đang dùng (chọn trong danh sách hoặc gõ)", _tool),
            DssTheme.Field("Mô tả: đã làm gì, kết quả sai thế nào, mong muốn ra sao", _text));
        AddSection(2, "Đính kèm",
            DssTheme.Pair(addImages, _images),
            _logs);
        AddSection(3, "Người gửi",
            DssTheme.Field("Tên / số điện thoại để DSSTech liên hệ lại", _contact));

        PrimaryButton.Content = "Gửi góp ý";
        CancelButton.Content = "Đóng";
        PrimaryButton.Click += (_, _) => Send();
    }

    private string ToolName => DssTheme.ComboValue(_tool).Trim();

    private void PickImages()
    {
        var pick = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn ảnh chụp màn hình",
            Filter = "Ảnh|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tất cả|*.*",
            Multiselect = true
        };
        if (pick.ShowDialog(this) != true)
            return;
        foreach (var file in pick.FileNames)
        {
            if (!_imageFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                _imageFiles.Add(file);
        }

        _images.Text = string.Join(", ", _imageFiles.Select(Path.GetFileName));
    }

    private async void Send()
    {
        if (string.IsNullOrWhiteSpace(_text.Text))
        {
            MessageBox.Show(this, "Anh/chị ghi nội dung góp ý trước khi gửi.", "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var report = Report();
        string zip;
        try
        {
            zip = Pack(report);
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"feedback pack: {ex.Message}");
            MessageBox.Show(this, "Không tạo được file đính kèm: " + ex.Message, "DSSTech", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Clipboard.SetText(report);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard busy: the report is also inside the zip.
        }

        var subject = $"[DSSTech góp ý] {Short(DssTheme.ComboValue(_kind))} — {(ToolName.Length > 0 ? ToolName : "chung")}";

        // Gửi thẳng lên web app (Apps Script) nếu có; lỗi thì soạn mail như cũ.
        var endpoint = EndpointUrl();
        if (endpoint is not null)
        {
            PrimaryButton.IsEnabled = false;
            PrimaryButton.Content = "Đang gửi…";
            var error = await Upload(endpoint, subject, report, zip);
            PrimaryButton.IsEnabled = true;
            PrimaryButton.Content = "Gửi góp ý";
            if (error is null)
            {
                MessageBox.Show(this, $"Đã gửi góp ý về DSSTech. Cảm ơn anh/chị!\n\nBản lưu: {zip}",
                    "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                return;
            }

            DssTechLog.Write($"feedback upload: {error}");
            MessageBox.Show(this, $"Chưa gửi thẳng được ({error}).\n\nDSSTech sẽ soạn sẵn email để anh/chị gửi thay.",
                "DSSTech", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var body = report.Length > 1500 ? report.Substring(0, 1500) + "\n…(xem đầy đủ trong file zip)" : report;
        body += $"\n\nĐính kèm file: {zip}";
        var mail = $"mailto:{Recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        var opened = Open(mail);
        Open("explorer.exe", $"/select,\"{zip}\"");

        MessageBox.Show(this, opened
                ? $"Đã soạn sẵn email gửi {Recipient}.\n\nKéo file {Path.GetFileName(zip)} (đang mở trong Explorer) vào email rồi bấm Gửi."
                : $"Máy chưa có ứng dụng email mặc định.\n\nNội dung góp ý đã chép vào clipboard. Anh/chị gửi email tới {Recipient} và đính kèm file:\n{zip}",
            "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    /// <summary>
    /// Web app Apps Script nhận góp ý (tools/feedback-apps-script). %LOCALAPPDATA%\DSSTech\feedback-url.txt ghi đè
    /// để thử; trống thì chỉ soạn mail.
    /// </summary>
    private const string Endpoint =
        "https://script.google.com/macros/s/AKfycbwhxLA5WCFU_O7ptJJ9FcjOtvVh87R0iYyyX5qHJhzSWYUO4P8i_ogJmlv9-Mey-atI0w/exec";

    /// <summary>Zip lớn hơn mức này không gửi lên (Apps Script nhận tối đa ~50 MB, base64 phình thêm 1/3).</summary>
    private const long MaxUploadBytes = 20 * 1024 * 1024;

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private static string? EndpointUrl()
    {
        try
        {
            var file = Path.Combine(DataDir, "feedback-url.txt");
            var url = File.Exists(file) ? File.ReadAllText(file).Trim() : Endpoint;
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null;
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"feedback url: {ex.Message}");
            return null;
        }
    }

    /// <summary>POST nội dung + zip (base64) lên web app; null khi thành công, ngược lại là lý do lỗi.</summary>
    private static async Task<string?> Upload(string endpoint, string subject, string report, string zip)
    {
        try
        {
            var size = new FileInfo(zip).Length;
            if (size > MaxUploadBytes)
                return $"file đính kèm {size / 1024 / 1024} MB, quá {MaxUploadBytes / 1024 / 1024} MB";
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                app = "DSSTech",
                subject,
                body = report,
                zipName = Path.GetFileName(zip),
                zipBase64 = Convert.ToBase64String(await Task.Run(() => File.ReadAllBytes(zip)))
            });
            using var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
            // Apps Script trả 302 sang googleusercontent; HttpClient tự theo và đọc kết quả JSON.
            using var response = await Http.PostAsync(endpoint, content);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return $"HTTP {(int)response.StatusCode}";
            using var json = System.Text.Json.JsonDocument.Parse(text);
            if (json.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == System.Text.Json.JsonValueKind.True)
                return null;
            return json.RootElement.TryGetProperty("error", out var err) ? err.ToString() : "máy chủ không xác nhận";
        }
        catch (Exception ex)
        {
            return ex is System.Text.Json.JsonException ? "máy chủ trả về không phải JSON" : ex.Message;
        }
    }

    private string Report()
    {
        var version = typeof(FeedbackWindow).Assembly.GetName().Version;
        var info = LicenseService.Info;
        var sb = new StringBuilder();
        sb.AppendLine($"Loại: {DssTheme.ComboValue(_kind)}");
        sb.AppendLine($"Tool: {ToolName}");
        sb.AppendLine($"Người gửi: {_contact.Text.Trim()}");
        sb.AppendLine();
        sb.AppendLine(_text.Text.Trim());
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"DSSTech: {(version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}")}");
        sb.AppendLine($"Revit: {_revit}");
        sb.AppendLine($"Model: {_model}");
        sb.AppendLine($"Bản quyền: {(info is null ? "chưa kích hoạt" : $"{info.Holder}, hạn {info.ExpiresDate:dd/MM/yyyy}")}");
        sb.AppendLine($"Mã máy: {LicenseService.MachineCode}");
        sb.AppendLine($"Thời điểm: {DateTime.Now:dd/MM/yyyy HH:mm}");
        return sb.ToString();
    }

    /// <summary>Desktop\DSSTech-GopY-yyyyMMdd-HHmmss.zip: nội dung góp ý, ảnh, log (nếu chọn).</summary>
    private string Pack(string report)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
            desktop = DataDir;
        Directory.CreateDirectory(desktop);
        var zip = Path.Combine(desktop, $"DSSTech-GopY-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("gop-y.txt").Open(), new UTF8Encoding(true)))
            writer.Write(report);

        var n = 0;
        foreach (var image in _imageFiles)
            Add(archive, image, $"anh/{++n:00}-{Path.GetFileName(image)}");

        if (_logs.IsChecked == true)
        {
            foreach (var name in new[] { "mcp-host.log", "mep-cad-input.txt", "devices.json" })
                Add(archive, Path.Combine(DataDir, name), $"log/{name}");
            var journal = LatestJournal();
            if (journal is not null)
                Add(archive, journal, $"log/{Path.GetFileName(journal)}");
        }

        return zip;
    }

    /// <summary>Journal của phiên Revit đang chạy (file mới nhất trong thư mục Journals).</summary>
    private string? LatestJournal()
    {
        try
        {
            var year = new string(_revit.Where(char.IsDigit).ToArray());
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Autodesk", "Revit", $"Autodesk Revit {year}", "Journals");
            if (!Directory.Exists(dir))
                return null;
            return new DirectoryInfo(dir).GetFiles("journal.*.txt")
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"feedback journal: {ex.Message}");
            return null;
        }
    }

    /// <summary>Chép file vào zip; đọc chung để lấy được file Revit/add-in đang ghi.</summary>
    private static void Add(ZipArchive archive, string path, string entry)
    {
        if (!File.Exists(path))
            return;
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = archive.CreateEntry(entry, CompressionLevel.Optimal).Open();
            source.CopyTo(target);
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"feedback attach {path}: {ex.Message}");
        }
    }

    private static bool Open(string target, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? "" });
            return true;
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"feedback open {target}: {ex.Message}");
            return false;
        }
    }

    private static string Short(string kind)
    {
        var dash = kind.IndexOf(" — ", StringComparison.Ordinal);
        return dash > 0 ? kind.Substring(0, dash) : kind;
    }
}
