using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;
using DSSTech.Revit.Ui;
using Color = Autodesk.Revit.DB.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Duct and pipe system types of the company colour rules. Before creating anything it lists, for each system, the
/// one already in the model that is the same (same code) or looks the same (same name in other words), so the
/// project keeps one type per system: use it (and sync its code and colour), create a new one, or skip.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class MepSystemSetupCommand : IExternalCommand
{
    private const string Title = "DSSTech — Hệ thống MEP";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc is null)
        {
            TaskDialog.Show("DSSTech", "Mở project Revit trước.");
            return Result.Failed;
        }

        var existing = Existing(doc);
        var rows = MepSystemCatalog.All
            .Where(s => s.Domain != MepDomain.CableTray)
            .Select(s =>
            {
                var (kind, match) = MepSystemCatalog.Match(s, existing.Select(e => e.Info));
                var twins = existing.Where(e => e.Info.Domain == s.Domain &&
                                                MepSystemCatalog.Match(s, new[] { e.Info }).Kind == SystemMatchKind.Same).ToList();
                return new SystemRow(s, kind, match, twins.Select(t => t.Info.Name).ToList());
            })
            .ToList();

        var dlg = new MepSystemSetupWindow(rows, existing.Select(e => e.Info).ToList());
        dlg.SetOwner(commandData.Application.MainWindowHandle);
        if (dlg.ShowDialog() != true)
            return Result.Cancelled;

        int created = 0, synced = 0, skipped = 0;
        var failed = new List<string>();
        var renamed = new List<string>();
        using (var tx = new Transaction(doc, "DSSTech MEP systems"))
        {
            tx.Start();
            foreach (var row in rows)
            {
                try
                {
                    MEPSystemType? type;
                    if (row.UseId is string id)
                    {
                        type = doc.GetElement(new ElementId(long.Parse(id))) as MEPSystemType;
                        if (type is null || !dlg.SyncExisting)
                        {
                            skipped++;
                            continue;
                        }

                        synced++;
                        // Overwrite the old code in the name too (M-KEA-… → M-KEAD-…), unless the name is taken.
                        var oldAbbr = type.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.AsString();
                        var newName = MepSystemCatalog.SyncName(type.Name, oldAbbr, row.Spec.Abbr);
                        if (newName != type.Name)
                        {
                            if (new FilteredElementCollector(doc).OfClass(typeof(MEPSystemType)).Any(t => t.Id != type.Id && t.Name == newName))
                                failed.Add($"{row.Spec.Abbr}: không đổi tên «{type.Name}» vì đã có hệ «{newName}»");
                            else
                            {
                                renamed.Add($"{type.Name} → {newName}");
                                type.Name = newName;
                            }
                        }
                    }
                    else if (row.Create)
                    {
                        if (!Enum.TryParse<MEPSystemClassification>(row.Spec.Classification, out var cls))
                        {
                            failed.Add($"{row.Spec.Abbr}: classification {row.Spec.Classification}");
                            continue;
                        }

                        type = row.Spec.Domain == MepDomain.Duct
                            ? MechanicalSystemType.Create(doc, cls, row.Spec.TypeName)
                            : PipingSystemType.Create(doc, cls, row.Spec.TypeName);
                        created++;
                    }
                    else
                    {
                        skipped++;
                        continue;
                    }

                    Style(doc, type, row.Spec);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
                {
                    failed.Add($"{row.Spec.Abbr}: {ex.Message}");
                }
            }

            tx.Commit();
        }

        var summary = new StringBuilder();
        summary.AppendLine($"Tạo mới {created}; dùng hệ có sẵn {synced}" + (dlg.SyncExisting ? " (ghi đè mã vào tên hệ, mã viết tắt và màu)" : "") + $"; bỏ qua {skipped}.");
        summary.AppendLine("Màu theo quy định: màu nét (Graphic Overrides) và vật liệu đặt tên Bộ môn-Loại-Mã (EM-MAT-SAD Supply Air, EP-MAT-CWS Cold Water Supply, EF-MAT-SP Sprinkler) cho hiển thị 3D.");
        summary.AppendLine("Hệ điện (máng cáp) không có system type trong Revit: màu sẽ gán theo Service Type khi dựng máng.");
        if (renamed.Count > 0)
            summary.AppendLine($"Ghi đè mã vào tên hệ ({renamed.Count}): " + string.Join("; ", renamed));
        if (failed.Count > 0)
            summary.AppendLine("Lỗi: " + string.Join("; ", failed));
        TaskDialog.Show(Title, summary.ToString());
        return Result.Succeeded;
    }

    private static void Style(Document doc, MEPSystemType type, MepSystemSpec spec)
    {
        type.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.Set(spec.Abbr);
        var color = new Color((byte)spec.R, (byte)spec.G, (byte)spec.B);
        type.LineColor = color;
        // Material name by the BIM rule (EM-MAT-SAD Supply Air); materials of earlier runs are renamed to it.
        var name = MepSystemCatalog.MaterialName(spec);
        var materials = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().ToList();
        var material = materials.FirstOrDefault(m => m.Name == name);
        if (material is null && materials.FirstOrDefault(m => MepSystemCatalog.OldMaterialNames(spec).Contains(m.Name)) is { } old)
        {
            old.Name = name;
            material = old;
        }

        material ??= doc.GetElement(Material.Create(doc, name)) as Material;
        if (material is null)
            return;
        material.Color = color;
        type.MaterialId = material.Id;
    }

    private static List<(MEPSystemType Type, ExistingSystem Info)> Existing(Document doc)
    {
        var list = new List<(MEPSystemType, ExistingSystem)>();
        foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType)).Cast<MechanicalSystemType>())
            list.Add((t, Info(t, MepDomain.Duct)));
        foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>())
            list.Add((t, Info(t, MepDomain.Pipe)));
        return list;
    }

    private static ExistingSystem Info(MEPSystemType t, MepDomain domain) => new()
    {
        Id = t.Id.Value.ToString(),
        Name = t.Name,
        Abbreviation = t.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.AsString() ?? "",
        Domain = domain,
        Classification = t.SystemClassification.ToString()
    };
}

internal sealed class SystemRow
{
    public SystemRow(MepSystemSpec spec, SystemMatchKind kind, ExistingSystem? match, List<string> twins)
    {
        Spec = spec;
        Kind = kind;
        Match = match;
        Twins = twins;
        UseId = match?.Id;
        Create = match is null;
    }

    public MepSystemSpec Spec { get; }
    public SystemMatchKind Kind { get; }
    public ExistingSystem? Match { get; }
    /// <summary>Several types of the model already are this system: the project has a duplicate to clean up.</summary>
    public List<string> Twins { get; }
    public string? UseId { get; set; }
    public bool Create { get; set; }
}

/// <summary>One line per system: what the model already has, and what to do with it.</summary>
internal sealed class MepSystemSetupWindow : DssDialog
{
    private const string CreateItem = "Tạo mới";
    private const string SkipItem = "Bỏ qua";
    private readonly CheckBox _sync;

    public bool SyncExisting { get; private set; }

    public MepSystemSetupWindow(List<SystemRow> rows, List<ExistingSystem> existing)
        : base("DSSTech — Hệ thống MEP",
            "Hệ theo quy định màu. Hệ đã có trong model (cùng mã, hoặc tên tương tự) được chọn sẵn để dùng lại, không tạo trùng. Kiểm tra dòng «tương tự» và «trùng».",
            860, 760)
    {
        var lines = new StackPanel();
        lines.Children.Add(Line(Text("Mã", true), Text("Hệ", true), Text("", true), Text("Trong model", true), Text("Xử lý", true)));
        var pickers = new List<(SystemRow Row, ComboBox Box, List<ExistingSystem?> Items)>();
        foreach (var group in rows.GroupBy(r => r.Spec.Group))
        {
            lines.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Foreground = DssTheme.Green, Margin = new Thickness(0, 10, 0, 4) });
            foreach (var row in group)
            {
                var status = row.Kind switch
                {
                    SystemMatchKind.Same => $"Đã có: {row.Match!.Name}",
                    SystemMatchKind.Similar => $"Tương tự: {row.Match!.Name} — kiểm tra",
                    _ => "Chưa có"
                };
                if (row.Twins.Count > 1)
                    status += $" · TRÙNG {row.Twins.Count}: {string.Join(", ", row.Twins)}";
                var items = new List<ExistingSystem?> { null };
                var labels = new List<string> { row.Kind == SystemMatchKind.None ? CreateItem : $"{CreateItem} (đã có tương tự)" };
                foreach (var e in existing.Where(e => e.Domain == row.Spec.Domain)
                             .OrderByDescending(e => e.Id == row.Match?.Id).ThenBy(e => e.Name))
                {
                    items.Add(e);
                    labels.Add($"Dùng «{e.Name}»");
                }

                items.Add(null);
                labels.Add(SkipItem);
                var box = DssTheme.Combo(labels, row.Match is not null ? $"Dùng «{row.Match.Name}»" : labels[0]);
                pickers.Add((row, box, items));
                var swatch = new Border
                {
                    Width = 28, Height = 16, CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)row.Spec.R, (byte)row.Spec.G, (byte)row.Spec.B)),
                    BorderBrush = DssTheme.Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
                };
                var statusText = Text(status, false);
                if (row.Kind != SystemMatchKind.Same || row.Twins.Count > 1)
                    statusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB4, 0x53, 0x09));
                lines.Children.Add(Line(Text(row.Spec.Abbr, true), Text($"{row.Spec.NameVi} / {row.Spec.NameEn}", false), swatch, statusText, box));
            }
        }

        _sync = DssTheme.Option("Hệ có sẵn được dùng lại: ghi đè mã (tên hệ và mã viết tắt) và màu theo quy định", true);
        AddSection(1, "Hệ thống", lines);
        AddSection(2, "Đồng bộ", _sync);
        PrimaryButton.Content = "Áp dụng";
        PrimaryButton.Click += (_, _) =>
        {
            foreach (var (row, box, items) in pickers)
            {
                var index = box.SelectedIndex;
                var label = DssTheme.ComboValue(box);
                row.UseId = index > 0 && index < items.Count - 1 ? items[index]?.Id : null;
                row.Create = label.StartsWith(CreateItem, StringComparison.Ordinal);
            }

            SyncExisting = _sync.IsChecked == true;
            DialogResult = true;
            Close();
        };
    }

    private static TextBlock Text(string text, bool strong) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = strong ? DssTheme.Ink : DssTheme.Muted,
        FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal
    };

    private static Grid Line(params UIElement[] cells)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var w in new[] { 0.7, 2.4, 0.5, 2.6, 2.4 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is FrameworkElement fe)
                fe.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        return grid;
    }
}
