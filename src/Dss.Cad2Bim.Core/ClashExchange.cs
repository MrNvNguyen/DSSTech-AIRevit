using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Dss.Cad2Bim.Core;

/// <summary>One clash between two elements (B may sit in a linked model).</summary>
public sealed class ClashRecord
{
    public int Index { get; set; }
    public string Name => $"Clash {Index:000}";
    public string Status { get; set; } = "Mới";
    public long AId { get; set; }
    public string ACategory { get; set; } = "";
    public string AName { get; set; } = "";
    public long BId { get; set; }
    public string BCategory { get; set; } = "";
    public string BName { get; set; } = "";
    /// <summary>Model the B element lives in: empty for this model, else the link's name.</summary>
    public string BModel { get; set; } = "";
    public string Level { get; set; } = "";
    /// <summary>Middle of the overlap, internal coordinates in mm.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    /// <summary>The same point in shared coordinates (survey), in metres: what Navisworks and BCF tools use.</summary>
    public double SharedX { get; set; }
    public double SharedY { get; set; }
    public double SharedZ { get; set; }
    public double VolumeCm3 { get; set; }
    public string Note { get; set; } = "";

    public string Title => $"{Name}: {ACategory} × {BCategory}";
    public string Description =>
        $"{ACategory} {AName} [id {AId}] × {BCategory} {BName} [id {BId}{(BModel.Length > 0 ? $", link {BModel}" : "")}]; " +
        $"giao {VolumeCm3:0} cm³; level {Level}";

    /// <summary>Same clash, same GUID on every export: Navisworks and BCF tools update it instead of adding one.</summary>
    public string Guid
    {
        get
        {
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes($"DSSTech.Clash|{AId}|{BModel}|{BId}"));
            return new Guid(bytes).ToString();
        }
    }
}

/// <summary>
/// Clash exchange files: BCF 2.1 (BIMcollab, Revizto, ACC, Navisworks with a BCF add-in) and Navisworks
/// viewpoints XML (File ▸ Import ▸ Viewpoints XML). Cameras look at the clash from above at 45°, in shared
/// coordinates — export the Navisworks model (NWC) with shared coordinates so the viewpoints land on it.
/// </summary>
public static class ClashExchange
{
    private const double CameraDistanceM = 4;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Eye and direction of the camera looking at a clash point (shared metres).</summary>
    public static (double[] Eye, double[] Dir, double[] Up) Camera(ClashRecord c)
    {
        var dir = Normalize(new[] { 1.0, 1.0, -1.0 });
        var eye = new[] { c.SharedX - dir[0] * CameraDistanceM, c.SharedY - dir[1] * CameraDistanceM, c.SharedZ - dir[2] * CameraDistanceM };
        // Up: perpendicular to the view, toward +Z.
        var up = Normalize(Sub(new[] { 0.0, 0, 1 }, Scale(dir, Dot(dir, new[] { 0.0, 0, 1 }))));
        return (eye, dir, up);
    }

    public static void WriteBcf(string path, IReadOnlyList<ClashRecord> clashes, string projectName,
        IReadOnlyDictionary<int, byte[]>? snapshots = null)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        Entry(zip, "bcf.version", new XDocument(new XElement("Version", new XAttribute("VersionId", "2.1"),
            new XElement("DetailedVersion", "2.1"))));
        Entry(zip, "project.bcfp", new XDocument(new XElement("ProjectExtension",
            new XElement("Project", new XAttribute("ProjectId", StableGuid(projectName)), new XElement("Name", projectName)),
            new XElement("ExtensionSchema", ""))));
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv);
        foreach (var c in clashes)
        {
            var topic = c.Guid;
            var viewpointGuid = StableGuid("vp|" + topic);
            var hasSnapshot = snapshots is not null && snapshots.ContainsKey(c.Index);
            var markup = new XElement("Markup",
                new XElement("Topic", new XAttribute("Guid", topic), new XAttribute("TopicType", "Clash"),
                    new XAttribute("TopicStatus", c.Status),
                    new XElement("Title", c.Title),
                    new XElement("CreationDate", now),
                    new XElement("CreationAuthor", "DSSTech"),
                    new XElement("Description", c.Description)),
                new XElement("Viewpoints", new XAttribute("Guid", viewpointGuid),
                    new XElement("Viewpoint", "viewpoint.bcfv"),
                    hasSnapshot ? new XElement("Snapshot", "snapshot.png") : null));
            Entry(zip, $"{topic}/markup.bcf", new XDocument(markup));
            var (eye, dir, up) = Camera(c);
            var vp = new XElement("VisualizationInfo", new XAttribute("Guid", viewpointGuid),
                new XElement("Components",
                    new XElement("Selection",
                        Component(c.AId, ""),
                        Component(c.BId, c.BModel))),
                new XElement("PerspectiveCamera",
                    Point("CameraViewPoint", eye), Vector("CameraDirection", dir), Vector("CameraUpVector", up),
                    new XElement("FieldOfView", "60")));
            Entry(zip, $"{topic}/viewpoint.bcfv", new XDocument(vp));
            if (hasSnapshot)
            {
                var entry = zip.CreateEntry($"{topic}/snapshot.png");
                using var s = entry.Open();
                var png = snapshots![c.Index];
                s.Write(png, 0, png.Length);
            }
        }
    }

    public static void WriteNavisworksViewpoints(string path, IReadOnlyList<ClashRecord> clashes)
    {
        var views = new XElement("viewpoints");
        foreach (var c in clashes)
        {
            var (eye, dir, up) = Camera(c);
            var q = Quaternion(dir, up);
            views.Add(new XElement("view", new XAttribute("name", c.Title), new XAttribute("guid", c.Guid),
                new XElement("viewpoint", new XAttribute("tool", "orbit"), new XAttribute("render", "shaded"),
                    new XAttribute("lighting", "headlight"), new XAttribute("focal", F(CameraDistanceM)),
                    new XAttribute("linear", "1"), new XAttribute("angular", "0.7853981634"),
                    new XElement("camera", new XAttribute("projection", "persp"), new XAttribute("near", "0.1"),
                        new XAttribute("far", "1000"), new XAttribute("aspect", "1.5"), new XAttribute("height", "0.7853981634"),
                        new XElement("position", new XElement("pos3f", new XAttribute("x", F(eye[0])), new XAttribute("y", F(eye[1])), new XAttribute("z", F(eye[2])))),
                        new XElement("rotation", new XElement("quaternion", new XAttribute("a", F(q[0])), new XAttribute("b", F(q[1])),
                            new XAttribute("c", F(q[2])), new XAttribute("d", F(q[3])))))),
                new XElement("comments", new XElement("comment", new XAttribute("status", "new"),
                    new XElement("body", c.Description)))));
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement("exchange", new XAttribute("units", "m"), new XAttribute("filename", ""), new XAttribute("filepath", ""), views));
        doc.Save(path);
    }

    /// <summary>
    /// Navisworks camera rotation: the camera looks down its −Z with +Y up. Rows of the rotation are its right, up
    /// and back axes; returned as (x, y, z, w).
    /// </summary>
    public static double[] Quaternion(double[] dir, double[] up)
    {
        var back = Scale(dir, -1);
        var right = Normalize(Cross(up, back));
        var trueUp = Cross(back, right);
        // Matrix with columns right, up, back (camera axes in world).
        double m00 = right[0], m01 = trueUp[0], m02 = back[0];
        double m10 = right[1], m11 = trueUp[1], m12 = back[1];
        double m20 = right[2], m21 = trueUp[2], m22 = back[2];
        double x, y, z, w;
        var trace = m00 + m11 + m22;
        if (trace > 0)
        {
            var s = Math.Sqrt(trace + 1) * 2;
            w = 0.25 * s;
            x = (m21 - m12) / s;
            y = (m02 - m20) / s;
            z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            var s = Math.Sqrt(1 + m00 - m11 - m22) * 2;
            w = (m21 - m12) / s;
            x = 0.25 * s;
            y = (m01 + m10) / s;
            z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            var s = Math.Sqrt(1 + m11 - m00 - m22) * 2;
            w = (m02 - m20) / s;
            x = (m01 + m10) / s;
            y = 0.25 * s;
            z = (m12 + m21) / s;
        }
        else
        {
            var s = Math.Sqrt(1 + m22 - m00 - m11) * 2;
            w = (m10 - m01) / s;
            x = (m02 + m20) / s;
            y = (m12 + m21) / s;
            z = 0.25 * s;
        }

        return new[] { x, y, z, w };
    }

    private static XElement Component(long id, string model) =>
        new("Component", new XAttribute("IfcGuid", ""),
            new XElement("OriginatingSystem", model.Length > 0 ? $"Revit link {model}" : "Revit"),
            new XElement("AuthoringToolId", id.ToString(Inv)));

    private static XElement Point(string name, double[] p) =>
        new(name, new XElement("X", F(p[0])), new XElement("Y", F(p[1])), new XElement("Z", F(p[2])));

    private static XElement Vector(string name, double[] v) => Point(name, v);

    private static void Entry(ZipArchive zip, string name, XDocument doc)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        using var w = new StreamWriter(s, new UTF8Encoding(false));
        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + doc.Root);
    }

    private static string StableGuid(string key)
    {
        using var md5 = MD5.Create();
        return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("DSSTech.Bcf|" + key))).ToString();
    }

    private static string F(double v) => v.ToString("0.######", Inv);
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    private static double[] Scale(double[] a, double k) => new[] { a[0] * k, a[1] * k, a[2] * k };
    private static double[] Cross(double[] a, double[] b) =>
        new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

    private static double[] Normalize(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return l < 1e-12 ? a : Scale(a, 1 / l);
    }
}
