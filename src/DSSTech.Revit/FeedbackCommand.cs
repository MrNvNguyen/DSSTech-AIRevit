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
/// thành một file zip trên Desktop và mở ứng dụng mail soạn sẵn gửi về DSSTech.
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
    private readonly TextBox _tool;
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
        _tool = DssTheme.Input("");
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
            DssTheme.Field("Tool / nút đang dùng (VD: MEPF ▸ Ống gió, Thiết bị ĐHKK)", _tool),
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

    private void Send()
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

        var subject = $"[DSSTech góp ý] {Short(DssTheme.ComboValue(_kind))} — {(_tool.Text.Trim().Length > 0 ? _tool.Text.Trim() : "chung")}";
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

    private string Report()
    {
        var version = typeof(FeedbackWindow).Assembly.GetName().Version;
        var info = LicenseService.Info;
        var sb = new StringBuilder();
        sb.AppendLine($"Loại: {DssTheme.ComboValue(_kind)}");
        sb.AppendLine($"Tool: {_tool.Text.Trim()}");
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
