using System.Windows;
using System.Windows.Controls;
using Dss.Cad2Bim.Core;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit;

internal sealed class ParamSetupWindow : DssDialog
{
    private readonly RadioButton _builtin;
    private readonly RadioButton _byPart;
    private readonly RadioButton _byDiscipline;
    private readonly RadioButton _full;
    private readonly ComboBox _parts;
    private readonly ComboBox _disciplines;
    public ParamPackageChoice Choice { get; private set; }

    private ParamSetupWindow(string? package, string? part, string? discipline)
        : base(
            "DSSTech — Chuẩn bị tham số",
            "Chỉ gắn trường. Không ghi QS_NormCode.",
            520,
            560)
    {
        ResizeMode = ResizeMode.NoResize;
        _builtin = Radio("Tham số sẵn có của Revit");
        _byPart = Radio("Theo phần đang dựng");
        _byDiscipline = Radio("Theo bộ môn");
        _full = Radio("Đủ bộ QS_*");
        _byPart.IsChecked = true;

        _parts = DssTheme.Combo();
        foreach (var item in ParamPackageCatalog.Parts)
            _parts.Items.Add(new ComboItem(item.Label, item.Kind.ToString()));
        _disciplines = DssTheme.Combo();
        foreach (var item in ParamPackageCatalog.Disciplines)
            _disciplines.Items.Add(new ComboItem(item.Label, item.Id));

        Select(_parts, ParamPackageCatalog.ParsePart(part).ToString());
        Select(_disciplines, ParamPackageCatalog.ParseDiscipline(discipline));
        ApplyPackage(string.IsNullOrWhiteSpace(package) ? ParamPackageCatalog.ByPart : package);

        _builtin.Checked += (_, _) => ShowPickers();
        _byPart.Checked += (_, _) => ShowPickers();
        _byDiscipline.Checked += (_, _) => ShowPickers();
        _full.Checked += (_, _) => ShowPickers();

        AddSection(1, "Tham số sẵn có của Revit", _builtin);
        AddSection(2, "Theo phần đang dựng", _byPart, _parts);
        AddSection(3, "Theo bộ môn", _byDiscipline, _disciplines);
        AddSection(4, "Đủ bộ QS_*", _full);

        PrimaryButton.Content = "Gắn trường";
        PrimaryButton.Click += (_, _) => Accept();
        ShowPickers();
    }

    private void ShowPickers()
    {
        _parts.Visibility = _byPart.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _disciplines.Visibility = _byDiscipline.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyPackage(string package)
    {
        _builtin.IsChecked = package == ParamPackageCatalog.Builtin;
        _byPart.IsChecked = package == ParamPackageCatalog.ByPart;
        _byDiscipline.IsChecked = package == ParamPackageCatalog.ByDiscipline;
        _full.IsChecked = package == ParamPackageCatalog.Full;
        if (_builtin.IsChecked != true && _byPart.IsChecked != true &&
            _byDiscipline.IsChecked != true && _full.IsChecked != true)
            _byPart.IsChecked = true;
        ShowPickers();
    }

    private void Accept()
    {
        var package = _builtin.IsChecked == true ? ParamPackageCatalog.Builtin
            : _byDiscipline.IsChecked == true ? ParamPackageCatalog.ByDiscipline
            : _full.IsChecked == true ? ParamPackageCatalog.Full
            : ParamPackageCatalog.ByPart;
        var part = (_parts.SelectedItem as ComboItem)?.Value ?? ModelRevitPartKind.Column.ToString();
        var discipline = (_disciplines.SelectedItem as ComboItem)?.Value ?? "Structure";
        Choice = new ParamPackageChoice(package, part, discipline);
        DialogResult = true;
        Close();
    }

    private static RadioButton Radio(string label) =>
        new() { Content = label, Margin = new Thickness(0, 4, 0, 4), Foreground = DssTheme.Ink };

    private static void Select(ComboBox box, string value)
    {
        foreach (ComboItem item in box.Items)
        {
            if (item.Value == value)
            {
                box.SelectedItem = item;
                return;
            }
        }

        if (box.Items.Count > 0)
            box.SelectedIndex = 0;
    }

    public static bool TryPick(IntPtr owner, string? package, string? part, string? discipline, out ParamPackageChoice choice)
    {
        choice = default;
        var dlg = new ParamSetupWindow(package, part, discipline);
        dlg.SetOwner(owner);
        if (dlg.ShowDialog() != true)
            return false;
        choice = dlg.Choice;
        return true;
    }

    private sealed class ComboItem
    {
        public ComboItem(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public string Value { get; }
        public override string ToString() => Label;
    }
}
