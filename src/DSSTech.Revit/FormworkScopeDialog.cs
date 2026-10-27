using System.Windows;
using System.Windows.Controls;
using DSSTech.Revit.Ui;

namespace DSSTech.Revit;

internal sealed class FormworkScopeDialog : DssDialog
{
    private readonly RadioButton _sel;
    private readonly RadioButton _view;
    private readonly RadioButton _model;

    public FormworkScopeMode SelectedMode { get; private set; } = FormworkScopeMode.Selection;
    public bool Confirmed { get; private set; }

    public FormworkScopeDialog()
        : base("DSSTech — Phạm vi ván khuôn", "Chọn phạm vi đo trước khi mở lưới kết quả.", 460, 380)
    {
        ResizeMode = ResizeMode.NoResize;
        _sel = Radio("Selection (mặc định)", true);
        _view = Radio("Active view", false);
        _model = Radio("Whole model", false);
        AddSection(1, "Phạm vi", _sel, _view, _model);
        PrimaryButton.Content = "Đo ván khuôn";
        PrimaryButton.Click += (_, _) =>
        {
            SelectedMode = _view.IsChecked == true ? FormworkScopeMode.ActiveView
                : _model.IsChecked == true ? FormworkScopeMode.WholeModel
                : FormworkScopeMode.Selection;
            Confirmed = true;
            DialogResult = true;
            Close();
        };
    }

    private static RadioButton Radio(string label, bool isChecked) =>
        new() { Content = label, IsChecked = isChecked, Margin = new Thickness(0, 4, 0, 4), Foreground = DssTheme.Ink };

    public static FormworkScopeMode? ShowModal(IntPtr owner)
    {
        var dlg = new FormworkScopeDialog();
        dlg.SetOwner(owner);
        return dlg.ShowDialog() == true && dlg.Confirmed ? dlg.SelectedMode : null;
    }
}
