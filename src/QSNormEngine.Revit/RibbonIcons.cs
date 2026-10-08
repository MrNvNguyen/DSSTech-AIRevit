using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace QSNormEngine.Revit;

internal static class RibbonIcons
{
    public static BitmapImage Load(string baseName, int size)
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var path = Path.Combine(dir, "Assets", $"{baseName}-{size}.png");
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
