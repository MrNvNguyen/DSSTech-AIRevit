using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace DSSTech.Revit;

internal static class RibbonIcons
{
    /// <summary>True khi Revit đang dùng giao diện tối (Revit 2024+). Lỗi/không hỗ trợ → coi như sáng.</summary>
    public static bool IsDarkTheme
    {
        get
        {
            try { return UIThemeManager.CurrentTheme == UITheme.Dark; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Nạp Assets/{baseName}-{size}.png; khi giao diện tối thì ưu tiên Assets/{baseName}-{size}-dark.png
    /// (nét sáng trên nền tối), không có bản tối thì dùng bản sáng.
    /// </summary>
    public static BitmapImage Load(string baseName, int size)
    {
        var dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Assets");
        var path = Path.Combine(dir, $"{baseName}-{size}.png");
        if (IsDarkTheme)
        {
            var dark = Path.Combine(dir, $"{baseName}-{size}-dark.png");
            if (File.Exists(dark))
                path = dark;
        }

        if (!File.Exists(path))
            return new BitmapImage();
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri(path, UriKind.Absolute);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
