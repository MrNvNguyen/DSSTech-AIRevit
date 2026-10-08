using System.Windows;
using System.Windows.Controls;

namespace QSNormEngine.Revit;

internal sealed class FormworkScopeDialog : Window
{
    private readonly RadioButton _sel;
    private readonly RadioButton _view;
    private readonly RadioButton _model;

    public FormworkScopeMode SelectedMode { get; private set; } = FormworkScopeMode.Selection;
    public bool Confirmed { get; private set; }

    public FormworkScopeDialog()
    {
        Title = "DSSTech — Formwork scope";
        Width = 360;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        _sel = new RadioButton { Content = "Selection (mặc định)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
        _view = new RadioButton { Content = "Active view", Margin = new Thickness(0, 4, 0, 4) };
        _model = new RadioButton { Content = "Whole model", Margin = new Thickness(0, 4, 0, 4) };

        var ok = new Button { Content = "OK", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 88, IsCancel = true };
        ok.Click += (_, _) =>
        {
            SelectedMode = _view.IsChecked == true ? FormworkScopeMode.ActiveView
                : _model.IsChecked == true ? FormworkScopeMode.WholeModel
                : FormworkScopeMode.Selection;
            Confirmed = true;
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) =>
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Chọn phạm vi đo ván khuôn:",
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(_sel);
        root.Children.Add(_view);
        root.Children.Add(_model);
        root.Children.Add(buttons);
        Content = root;
    }

    public static FormworkScopeMode? ShowModal(IntPtr owner)
    {
        var dlg = new FormworkScopeDialog();
        new System.Windows.Interop.WindowInteropHelper(dlg).Owner = owner;
        return dlg.ShowDialog() == true && dlg.Confirmed ? dlg.SelectedMode : null;
    }
}
