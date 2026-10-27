using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DSSTech.Revit.Ui;

internal static class DssTheme
{
    public static readonly SolidColorBrush Page = Freeze(Color.FromRgb(0xF4, 0xF6, 0xF9));
    public static readonly SolidColorBrush Card = Freeze(Colors.White);
    public static readonly SolidColorBrush Ink = Freeze(Color.FromRgb(0x10, 0x23, 0x1F));
    public static readonly SolidColorBrush Green = Freeze(Color.FromRgb(0x08, 0x7C, 0x49));
    public static readonly SolidColorBrush Muted = Freeze(Color.FromRgb(0x5C, 0x67, 0x70));
    public static readonly SolidColorBrush CancelFill = Freeze(Color.FromRgb(0xE4, 0xE8, 0xED));
    public static readonly SolidColorBrush OptionFill = Freeze(Color.FromRgb(0xE7, 0xF4, 0xED));
    public static readonly SolidColorBrush Line = Freeze(Color.FromRgb(0xD5, 0xDB, 0xE1));
    public static readonly FontFamily Font = new("Segoe UI");

    public static void Paint(Window window)
    {
        window.FontFamily = Font;
        window.Background = Page;
        window.Foreground = Ink;
    }

    public static TextBlock Subtitle(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Muted,
        Margin = new Thickness(0, 0, 0, 12)
    };

    public static Border Section(int number, string heading, params UIElement[] rows)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = number + "   " + heading,
            Foreground = Green,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var row in rows)
            stack.Children.Add(row);
        return new Border
        {
            Background = Card,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = stack
        };
    }

    public static Grid Pair(UIElement left, UIElement right)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        if (right is FrameworkElement rightElement)
            rightElement.Margin = new Thickness(8, 0, 0, 0);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    public static StackPanel Field(string label, UIElement editor)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Muted,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });
        stack.Children.Add(editor);
        return stack;
    }

    public static ComboBox Combo(IEnumerable<string>? items = null, string? selected = null)
    {
        var box = new ComboBox { MinHeight = 28, IsEditable = false };
        if (items is not null)
            FillCombo(box, items, selected);
        return box;
    }

    public static void FillCombo(ComboBox box, IEnumerable<string> items, string? selected)
    {
        box.Items.Clear();
        foreach (var item in items)
            box.Items.Add(item);
        if (!string.IsNullOrEmpty(selected))
        {
            foreach (var item in box.Items)
            {
                if (string.Equals(item as string, selected, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = item;
                    return;
                }
            }
        }

        if (box.Items.Count > 0 && box.SelectedIndex < 0)
            box.SelectedIndex = 0;
    }

    public static string ComboValue(ComboBox box) =>
        box.SelectedItem as string ?? box.Text ?? "";

    public static TextBox Input(string text = "") => new()
    {
        Text = text,
        MinHeight = 28,
        Padding = new Thickness(6, 4, 6, 4),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public static CheckBox Option(string text, bool isChecked = false) => new()
    {
        Content = text,
        IsChecked = isChecked,
        FontWeight = FontWeights.SemiBold,
        Foreground = Ink,
        Background = OptionFill,
        Padding = new Thickness(8, 6, 8, 6),
        Margin = new Thickness(0, 0, 0, 8)
    };

    public static Button Primary(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 36,
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontWeight = FontWeights.SemiBold,
            IsDefault = true
        };
        StylePrimary(button);
        return button;
    }

    public static Button Cancel(string text = "Huỷ")
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 120,
            MinHeight = 36,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        };
        StyleCancel(button);
        return button;
    }

    public static Button Quiet(string text)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6),
            MinHeight = 32
        };
        StyleQuiet(button);
        return button;
    }

    public static void StylePrimary(Button button)
    {
        button.Background = Green;
        button.Foreground = Brushes.White;
        button.BorderBrush = Green;
        button.FontFamily = Font;
        button.FontWeight = FontWeights.SemiBold;
    }

    public static void StyleCancel(Button button)
    {
        button.Background = CancelFill;
        button.Foreground = Ink;
        button.BorderBrush = Line;
        button.FontFamily = Font;
    }

    public static void StyleQuiet(Button button)
    {
        button.Background = Card;
        button.Foreground = Ink;
        button.BorderBrush = Line;
        button.FontFamily = Font;
    }

    public static Border CardWrap(UIElement child) => new()
    {
        Background = Card,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(8),
        Child = child
    };

    public static DockPanel Footer(Button primary, Button cancel, UIElement? extras = null)
    {
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };
        DockPanel.SetDock(cancel, Dock.Right);
        footer.Children.Add(cancel);
        if (extras is not null)
        {
            DockPanel.SetDock(extras, Dock.Right);
            footer.Children.Add(extras);
        }

        primary.HorizontalAlignment = HorizontalAlignment.Stretch;
        footer.Children.Add(primary);
        return footer;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
