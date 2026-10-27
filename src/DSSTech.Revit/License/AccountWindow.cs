using System.Windows;
using System.Windows.Controls;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.License;

internal sealed class AccountWindow : DssDialog
{
    private readonly TextBox _key;
    private readonly TextBlock _expiry;
    private readonly TextBlock _status;
    private readonly TextBlock _bootstrap;
    private readonly Button _manage;
    private LicenseFile _file;

    public AccountWindow()
        : base("DSSTech — Tài khoản", "Kích hoạt trên máy này chỉ đối chiếu file trong %AppData%\\DSSTech. Chưa gọi server.", 760, 460)
    {
        ResizeMode = ResizeMode.NoResize;
        _file = LicenseStore.Load();
        _key = DssTheme.Input("");
        _expiry = Value("");
        _status = Value("");
        _bootstrap = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = DssTheme.Muted,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _manage = DssTheme.Quiet("Quản lý license");
        _manage.Click += (_, _) =>
        {
            var admin = new LicenseAdminWindow(_file) { Owner = this };
            admin.ShowDialog();
            _file = LicenseStore.Load();
            Refresh();
        };

        var brand = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new TextBlock
        {
            Text = "DSS",
            FontSize = 42,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.White
        });
        brand.Children.Add(new TextBlock
        {
            Text = "DSSTech",
            FontSize = 18,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 4, 0, 0)
        });
        brand.Children.Add(new TextBlock
        {
            Text = "Bản quyền trên máy này",
            Foreground = System.Windows.Media.Brushes.White,
            Opacity = 0.85,
            Margin = new Thickness(0, 12, 0, 0)
        });
        var left = new Border
        {
            Background = DssTheme.Green,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 12, 0),
            Child = brand
        };

        var right = new StackPanel();
        right.Children.Add(DssTheme.Field("Phiên bản", Value(LicenseStore.VersionLabel())));
        right.Children.Add(DssTheme.Field("HWID", Readonly(LicenseStore.HardwareId())));
        right.Children.Add(DssTheme.Field("Hạn", _expiry));
        right.Children.Add(DssTheme.Field("Trạng thái", _status));
        right.Children.Add(DssTheme.Field("Key", _key));
        right.Children.Add(_bootstrap);
        right.Children.Add(_manage);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);
        AddSection(1, "Tài khoản", columns);

        PrimaryButton.Content = "Kích hoạt";
        CancelButton.Content = "Đóng";
        PrimaryButton.Click += (_, _) => Activate();
        Refresh();
    }

    private void Activate()
    {
        var error = LicenseStore.Activate(_file, _key.Text);
        if (!string.IsNullOrEmpty(error))
        {
            MessageBox.Show(this, error, "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _file = LicenseStore.Load();
        Refresh();
    }

    private void Refresh()
    {
        var active = LicenseStore.Activated(_file);
        if (active is null)
        {
            _expiry.Text = "—";
            _status.Text = "Chưa kích hoạt";
            var admin = _file.Keys.FirstOrDefault(k => k.IsAdmin && !k.IsExpired);
            _bootstrap.Text = admin is null
                ? ""
                : "Key quản trị cục bộ (dán vào ô Key rồi bấm Kích hoạt): " + admin.Id;
            _manage.Visibility = Visibility.Collapsed;
            return;
        }

        _key.Text = active.Id;
        _expiry.Text = active.ExpiresUtc.ToLocalTime().ToString("dd/MM/yyyy");
        _status.Text = active.IsExpired
            ? "Hết hạn"
            : active.IsAdmin ? "Đang dùng — quản trị" : "Đang dùng";
        _bootstrap.Text = "";
        _manage.Visibility = active.IsAdmin && !active.IsExpired ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock Value(string text) => new()
    {
        Text = text,
        Foreground = DssTheme.Ink,
        FontSize = 14,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBox Readonly(string text)
    {
        var box = DssTheme.Input(text);
        box.IsReadOnly = true;
        return box;
    }
}
