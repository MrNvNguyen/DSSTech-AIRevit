using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit.License;

internal sealed class LicenseAdminWindow : DssDialog
{
    private readonly LicenseFile _file;
    private readonly ObservableCollection<LicenseKey> _rows;
    private readonly DataGrid _grid;
    private readonly TextBlock _total;
    private readonly TextBlock _active;
    private readonly TextBlock _soon;
    private readonly TextBlock _expired;

    public LicenseAdminWindow(LicenseFile file)
        : base("DSSTech — Quản lý license", "Sổ key nằm trong file JSON trên máy này. Gia hạn không gọi server.", 860, 620)
    {
        _file = file;
        _rows = new ObservableCollection<LicenseKey>(file.Keys);
        _total = Stat("0");
        _active = Stat("0");
        _soon = Stat("0");
        _expired = Stat("0");
        _grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            MinHeight = 220,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        _grid.Columns.Add(Col("Key", nameof(LicenseKey.Id), 180));
        _grid.Columns.Add(Col("Người dùng", nameof(LicenseKey.Holder), 140));
        _grid.Columns.Add(Col("Loại", nameof(LicenseKey.Kind), 70));
        _grid.Columns.Add(Col("HWID", nameof(LicenseKey.Hwid), 160));
        _grid.Columns.Add(Col("Hạn", nameof(LicenseKey.ExpiresUtc), 140));
        _grid.Columns.Add(Col("Trạng thái", nameof(LicenseKey.Status), 80));

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        for (var i = 0; i < 4; i++)
            stats.ColumnDefinitions.Add(new ColumnDefinition());
        AddStat(stats, 0, "Tổng key", _total);
        AddStat(stats, 1, "Đang dùng", _active);
        AddStat(stats, 2, "Sắp hết hạn", _soon);
        AddStat(stats, 3, "Hết hạn", _expired);

        var create = DssTheme.Quiet("Tạo key");
        var month = DssTheme.Quiet("Gia hạn 30 ngày");
        var year = DssTheme.Quiet("Gia hạn 1 năm");
        create.Click += (_, _) => CreateKey();
        month.Click += (_, _) => ExtendSelected(30);
        year.Click += (_, _) => ExtendSelected(365);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(create);
        actions.Children.Add(month);
        actions.Children.Add(year);

        AddSection(1, "Thống kê", stats);
        AddSection(2, "Người dùng", _grid, actions);

        PrimaryButton.Content = "Đóng";
        CancelButton.Visibility = Visibility.Collapsed;
        PrimaryButton.Click += (_, _) => { DialogResult = true; Close(); };
        RefreshStats();
    }

    private void CreateKey()
    {
        var dlg = new DssDialog("DSSTech — Tạo key", "Key mới chỉ ghi vào sổ cục bộ.", 460, 380)
        {
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var holder = DssTheme.Input("");
        var kind = DssTheme.Combo(new[] { "Người dùng", "Quản trị" }, "Người dùng");
        var term = DssTheme.Combo(new[] { "30 ngày", "1 năm" }, "1 năm");
        dlg.AddSection(1, "Key mới",
            DssTheme.Field("Người giữ", holder),
            DssTheme.Pair(DssTheme.Field("Loại", kind), DssTheme.Field("Hạn", term)));
        dlg.PrimaryButton.Content = "Tạo";
        dlg.PrimaryButton.Click += (_, _) => { dlg.DialogResult = true; };
        if (dlg.ShowDialog() != true)
            return;

        var days = DssTheme.ComboValue(term) == "30 ngày" ? 30 : 365;
        var key = LicenseStore.NewKey(holder.Text, DssTheme.ComboValue(kind) == "Quản trị", days);
        _file.Keys.Add(key);
        _rows.Add(key);
        LicenseStore.Save(_file);
        RefreshStats();
    }

    private void ExtendSelected(int days)
    {
        if (_grid.SelectedItem is not LicenseKey key)
        {
            MessageBox.Show(this, "Chọn một dòng trong bảng.", "DSSTech", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LicenseStore.Extend(key, days);
        LicenseStore.Save(_file);
        _grid.Items.Refresh();
        RefreshStats();
    }

    private void RefreshStats()
    {
        var now = DateTime.UtcNow;
        var soon = now.AddDays(14);
        _total.Text = _file.Keys.Count.ToString();
        _active.Text = _file.Keys.Count(k => !k.IsExpired).ToString();
        _soon.Text = _file.Keys.Count(k => !k.IsExpired && k.ExpiresUtc.ToUniversalTime() <= soon).ToString();
        _expired.Text = _file.Keys.Count(k => k.IsExpired).ToString();
    }

    private static void AddStat(Grid grid, int column, string label, TextBlock value)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Foreground = DssTheme.Muted, FontSize = 12 });
        stack.Children.Add(value);
        var card = DssTheme.CardWrap(stack);
        card.Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0);
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private static TextBlock Stat(string text) => new()
    {
        Text = text,
        FontSize = 22,
        FontWeight = FontWeights.SemiBold,
        Foreground = DssTheme.Green
    };

    private static DataGridTextColumn Col(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new System.Windows.Data.Binding(path),
        Width = width
    };
}
