using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSSTech.Revit.License;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit;

/// <summary>Nút "Giới thiệu · About": phiên bản, bản quyền trên máy này, mở Tài khoản / Hướng dẫn.</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class AboutCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var app = commandData.Application;
        var version = typeof(AboutCommand).Assembly.GetName().Version;
        var info = LicenseService.Info;
        var dlg = new DssDialog("DSSTech — Giới thiệu", "Bộ công cụ dựng mô hình từ CAD, phối hợp MEPF và khối lượng cho Revit.", 560, 460);
        dlg.SetOwner(app.MainWindowHandle);
        var body = new StackPanel();
        void Row(string label, string value) =>
            body.Children.Add(DssTheme.Field(label, new TextBlock { Text = value, Foreground = DssTheme.Ink, TextWrapping = TextWrapping.Wrap }));
        Row("Sản phẩm", "DSSTech for Revit");
        Row("Phiên bản", version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}");
        Row("Revit", app.Application.VersionName);
        Row("Bản quyền", LicenseService.IsValid && info is not null
            ? $"{info.Holder} — hạn {info.ExpiresDate:dd/MM/yyyy}"
            : "Chưa kích hoạt — " + LicenseService.Reason);
        Row("Mã máy", LicenseService.MachineCode);
        dlg.AddSection(1, "Thông tin", body);

        var account = DssTheme.Quiet("Tài khoản / kích hoạt");
        account.Margin = new Thickness(0, 0, 8, 0);
        account.Click += (_, _) =>
        {
            var w = new AccountWindow { Owner = dlg };
            w.ShowDialog();
            if (w.Activated)
                RibbonState.RefreshAi();
        };
        var help = DssTheme.Quiet("Hướng dẫn sử dụng");
        help.Click += (_, _) =>
        {
            var file = HelpCommand.HelpUri("");
            if (file is not null)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HelpCommand.HelpFile) { UseShellExecute = true });
        };
        var links = new WrapPanel();
        links.Children.Add(account);
        links.Children.Add(help);
        dlg.AddSection(2, "Liên kết", links);
        dlg.PrimaryButton.Content = "Đóng";
        dlg.PrimaryButton.Click += (_, _) => dlg.Close();
        dlg.CancelButton.Visibility = System.Windows.Visibility.Collapsed;
        dlg.ShowDialog();
        return Result.Succeeded;
    }
}
