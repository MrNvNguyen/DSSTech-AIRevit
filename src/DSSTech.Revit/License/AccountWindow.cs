using System.Windows;
using System.Windows.Controls;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.License;

/// <summary>Version, this machine's code (sent to DSSTech for a key) and key activation.</summary>
internal sealed class AccountWindow : DssDialog
{
    private readonly TextBox _key;
    private readonly TextBlock _holder;
    private readonly TextBlock _expiry;
    private readonly TextBlock _status;

    /// <summary>Set when a key was activated while the window was open.</summary>
    public bool Activated { get; private set; }

    public AccountWindow()
        : base("DSSTech — Tài khoản", "Gửi mã máy cho DSSTech để nhận key, dán key vào ô dưới rồi bấm Kích hoạt. Không cần mạng.", 760, 520)
    {
        _key = DssTheme.Input("");
        _key.TextWrapping = TextWrapping.Wrap;
        _key.AcceptsReturn = true;
        _key.Height = 90;
        _key.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _holder = Value("");
        _expiry = Value("");
        _status = Value("");

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

        var machine = Readonly(LicenseService.MachineCode);
        var copy = DssTheme.Quiet("Chép mã máy");
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(LicenseService.MachineCode);
                copy.Content = "Đã chép";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Clipboard busy: the code can still be selected in the box.
            }
        };

        var right = new StackPanel();
        right.Children.Add(DssTheme.Field("Phiên bản", Value(VersionLabel())));
        right.Children.Add(DssTheme.Field("Mã máy", DssTheme.Pair(machine, copy)));
        right.Children.Add(DssTheme.Field("Người dùng", _holder));
        right.Children.Add(DssTheme.Field("Hạn", _expiry));
        right.Children.Add(DssTheme.Field("Trạng thái", _status));
        right.Children.Add(DssTheme.Field("Key", _key));

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
        var error = LicenseService.Activate(_key.Text);
        if (error.Length > 0)
        {
            MessageBox.Show(this, error, "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Activated = true;
        _key.Text = "";
        Refresh();
        MessageBox.Show(this, "Đã kích hoạt. Các nút DSSTech dùng được ngay.", "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Refresh()
    {
        var valid = LicenseService.IsValid;
        var info = LicenseService.Info;
        _holder.Text = string.IsNullOrWhiteSpace(info?.Holder) ? "—" : info!.Holder;
        _expiry.Text = info is null ? "—" : info.ExpiresDate.ToString("dd/MM/yyyy");
        if (valid && info is not null)
        {
            var left = (info.ExpiresDate - DateTime.Today).Days;
            _status.Text = left <= 14 ? $"Đang dùng — còn {left + 1} ngày, liên hệ DSSTech để gia hạn" : "Đang dùng";
        }
        else
            _status.Text = "Chưa kích hoạt — " + LicenseService.Reason;
    }

    private static string VersionLabel()
    {
        var version = typeof(AccountWindow).Assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static TextBlock Value(string text) => new()
    {
        Text = text,
        Foreground = DssTheme.Ink,
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBox Readonly(string text)
    {
        var box = DssTheme.Input(text);
        box.IsReadOnly = true;
        return box;
    }
}
