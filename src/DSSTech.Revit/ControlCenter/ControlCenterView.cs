using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DSSTech.Revit.License;
using DSSTech.Revit.Ui;
using WpfColor = System.Windows.Media.Color;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DSSTech.Revit;

internal sealed class ControlCenterView : UserControl
{
    private static ControlCenterView? _instance;
    public static ControlCenterView Instance => _instance ??= new ControlCenterView();

    private readonly TextBlock _paramStatus;
    private readonly ListBox _issues;
    private readonly TextBlock _qcCount;
    private readonly WpfTextBox _prompt;
    private readonly WpfTextBox _promptResult;

    private static readonly SolidColorBrush PageBrush = DssTheme.Page;
    private static readonly SolidColorBrush PrimaryBrush = DssTheme.Green;
    private static readonly SolidColorBrush CardBrush = DssTheme.Card;

    private ControlCenterView()
    {
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Background = PageBrush;

        _paramStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        _issues = new ListBox { MinHeight = 120, MaxHeight = 220, Margin = new Thickness(0, 8, 0, 0) };
        _qcCount = new TextBlock { Margin = new Thickness(0, 4, 0, 0), Foreground = new SolidColorBrush(WpfColor.FromRgb(0x5C, 0x67, 0x70)) };
        _prompt = new WpfTextBox { AcceptsReturn = true, MinHeight = 72, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        _promptResult = new WpfTextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            MaxHeight = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var setup = Primary("Chuẩn bị tham số");
        setup.Click += (_, _) => ControlCenterHost.Post(app =>
        {
            if (ParamSetupRunner.PromptAndApply(app, announce: true, out _))
                RefreshStatus(app.ActiveUIDocument?.Document);
        });

        var scan = Primary("Quét lỗi");
        scan.Click += (_, _) => ControlCenterHost.Post(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null)
            {
                TaskDialog.Show("DSSTech", "Mở project Revit trước.");
                return;
            }

            ShowIssues(ModelQcScanner.Scan(doc));
        });

        var autoFix = Primary("Auto-Fix");
        autoFix.IsEnabled = false;
        ToolTipService.SetShowOnDisabled(autoFix, true);
        autoFix.ToolTip = "Khóa đến khi tờ quy tắc và danh sách lỗi chạy ổn.";

        var excel = Primary("Xuất Excel");
        excel.Click += (_, _) => ControlCenterHost.Post(app => BoqExcelExport.Run(app));

        var model = Primary("Dựng từ CAD");
        model.Click += (_, _) => ControlCenterHost.Post(app => ModelRevitCommand.Run(app));

        var addin = Primary("Theo add-in");
        addin.Click += (_, _) => ControlCenterHost.Post(app => ControlCenterPrompt.RunAddin(app));
        var describe = Primary("Theo mô tả");
        describe.Click += (_, _) =>
        {
            var text = _prompt.Text ?? "";
            ControlCenterHost.Post(app => ControlCenterPrompt.RunDescription(app, text));
        };
        var promptButtons = new StackPanel { Orientation = Orientation.Horizontal };
        promptButtons.Children.Add(addin);
        describe.Margin = new Thickness(8, 6, 0, 0);
        promptButtons.Children.Add(describe);

        _issues.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_issues.SelectedItem is not QcIssue issue || issue.ElementId <= 0)
                return;
            var id = issue.ElementId;
            ControlCenterHost.Post(app => Zoom(app, id));
        };

        var tools = new StackPanel { Margin = new Thickness(12) };
        tools.Children.Add(Group("Chuẩn bị tham số", _paramStatus, setup));
        tools.Children.Add(Group("Kiểm tra mô hình", scan, autoFix, _issues, _qcCount));
        tools.Children.Add(Group("Khối lượng", excel));
        tools.Children.Add(Group("CAD / Model Revit", model));
        tools.Children.Add(Group("AI",
            new TextBlock
            {
                Text = "Theo add-in: gắn tham số, pick layer, pick Text/Dimension, tạo. Theo mô tả: tạo từ câu lệnh, không pick layer.",
                TextWrapping = TextWrapping.Wrap
            },
            _prompt,
            promptButtons,
            _promptResult));

        var toolsTab = new TabItem
        {
            Header = "Tools",
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = tools }
        };

        var license = new StackPanel { Margin = new Thickness(12) };
        license.Children.Add(Group("License",
            new TextBlock { Text = "HWID", FontWeight = FontWeights.SemiBold },
            new WpfTextBox
            {
                Text = ReadHardwareId(),
                IsReadOnly = true,
                Margin = new Thickness(0, 6, 0, 8),
                TextWrapping = TextWrapping.Wrap
            },
            DisabledActivate()));
        var licenseTab = new TabItem { Header = "License", Content = license };

        var tabs = new TabControl { Background = PageBrush };
        tabs.Items.Add(toolsTab);
        tabs.Items.Add(licenseTab);
        Content = tabs;
        _paramStatus.Text = "Chưa chọn gói. Mặc định khi gắn: theo phần đang dựng.";
        _qcCount.Text = "Chưa quét.";
    }

    public void RefreshStatus(Document? doc)
    {
        if (doc is null)
        {
            _paramStatus.Text = "Chưa mở project.";
            return;
        }

        if (!Dss.Cad2Bim.Core.CadProjectProfileStore.TryGetParamPackage(
                ProjectKeys.For(doc), out var package, out var part, out var discipline))
        {
            _paramStatus.Text = ParamPackageCatalog.Describe(null, null, null);
            return;
        }

        _paramStatus.Text = ParamPackageCatalog.Describe(package, part, discipline);
    }

    public void ShowIssues(IReadOnlyList<QcIssue> issues)
    {
        _issues.Items.Clear();
        foreach (var issue in issues)
            _issues.Items.Add(issue);
        _qcCount.Text = issues.Count == 0 ? "Không thấy lỗi trong phạm vi quét." : $"{issues.Count} dòng.";
    }

    public void SetPromptResult(string text) => _promptResult.Text = text;

    private static void Zoom(UIApplication app, long id)
    {
        var uidoc = app.ActiveUIDocument;
        if (uidoc is null)
            return;
        try
        {
            var elementId = new ElementId(id);
            if (uidoc.Document.GetElement(elementId) is null)
                return;
            uidoc.Selection.SetElementIds(new List<ElementId> { elementId });
            uidoc.ShowElements(elementId);
        }
        catch (Exception ex)
        {
            TaskDialog.Show("DSSTech", ex.Message);
        }
    }

    private static Button DisabledActivate()
    {
        var button = Primary("Tài khoản");
        button.Click += (_, _) =>
        {
            var dlg = new AccountWindow();
            dlg.ShowDialog();
        };
        return button;
    }

    private static Border Group(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = DssTheme.Green,
            Margin = new Thickness(0, 0, 0, 4)
        });
        foreach (var child in children)
            stack.Children.Add(child);
        return new Border
        {
            Background = CardBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = stack
        };
    }

    private static Button Primary(string text) => new()
    {
        Content = text,
        Margin = new Thickness(0, 6, 0, 0),
        Padding = new Thickness(12, 6, 12, 6),
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = PrimaryBrush,
        Foreground = Brushes.White,
        BorderBrush = PrimaryBrush,
        FontFamily = new FontFamily("Segoe UI")
    };

    private static string ReadHardwareId()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = key?.GetValue("MachineGuid") as string;
            if (!string.IsNullOrWhiteSpace(guid))
                return guid;
        }
        catch (Exception)
        {
            // Pane still opens if the registry key is unreadable.
        }

        return Environment.MachineName;
    }
}
