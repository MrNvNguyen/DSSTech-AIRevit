using System.Windows;
using System.Windows.Controls;

namespace DSSTech.Revit.Ui;

/// <summary>
/// Shared DSS window: numbered sections, parameter rows, wide primary button and gray Cancel.
/// </summary>
internal class DssDialog : Window
{
    private readonly StackPanel _sections = new();

    public Button PrimaryButton { get; }
    public Button CancelButton { get; }

    public DssDialog(string title, string subtitle, double width = 560, double height = 640)
    {
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        DssTheme.Paint(this);

        PrimaryButton = DssTheme.Primary("Đồng ý");
        CancelButton = DssTheme.Cancel();
        var footer = DssTheme.Footer(PrimaryButton, CancelButton);

        var body = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);

        var header = DssTheme.Subtitle(subtitle);
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);

        body.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _sections
        });
        Content = body;
    }

    public Border AddSection(int number, string heading, params UIElement[] rows)
    {
        var section = DssTheme.Section(number, heading, rows);
        _sections.Children.Add(section);
        return section;
    }

    public void SetOwner(IntPtr owner)
    {
        if (owner != IntPtr.Zero)
            new System.Windows.Interop.WindowInteropHelper(this).Owner = owner;
    }
}
