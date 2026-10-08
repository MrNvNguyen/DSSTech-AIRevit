using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Minimal ASCII DXF reader for LINE / LWPOLYLINE / CIRCLE / INSERT / TEXT + INSUNITS.
/// </summary>
public static class DxfParser
{
    public static CadGeometry Parse(string path)
    {
        var geom = new CadGeometry { SourcePath = path };
        var lines = File.ReadAllLines(path);
        var pairs = new List<(int Code, string Value)>();
        for (var i = 0; i + 1 < lines.Length; i += 2)
        {
            if (!int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                continue;
            pairs.Add((code, lines[i + 1].Trim()));
        }

        string section = "";
        for (var i = 0; i < pairs.Count; i++)
        {
            var (code, value) = pairs[i];
            if (code == 0 && value == "SECTION" && i + 1 < pairs.Count && pairs[i + 1].Code == 2)
            {
                section = pairs[i + 1].Value;
                i++;
                continue;
            }

            if (code == 0 && value == "ENDSEC")
            {
                section = "";
                continue;
            }

            if (section == "HEADER" && code == 9 && value == "$INSUNITS" && i + 1 < pairs.Count && pairs[i + 1].Code == 70)
            {
                if (int.TryParse(pairs[i + 1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var units))
                    geom.InsUnits = units;
                i++;
                continue;
            }

            if (section != "ENTITIES" || code != 0)
                continue;

            switch (value)
            {
                case "LINE":
                    i = ReadLine(pairs, i, geom);
                    break;
                case "CIRCLE":
                    i = ReadCircle(pairs, i, geom);
                    break;
                case "LWPOLYLINE":
                    i = ReadLwPolyline(pairs, i, geom);
                    break;
                case "INSERT":
                    i = ReadInsert(pairs, i, geom);
                    break;
                case "TEXT":
                case "MTEXT":
                    i = ReadText(pairs, i, geom);
                    break;
            }
        }

        return geom;
    }

    private static int ReadLine(List<(int Code, string Value)> pairs, int i, CadGeometry geom)
    {
        var e = new CadLine();
        i++;
        while (i < pairs.Count && pairs[i].Code != 0)
        {
            switch (pairs[i].Code)
            {
                case 8: e.Layer = pairs[i].Value; break;
                case 10: e.X0 = D(pairs[i].Value); break;
                case 20: e.Y0 = D(pairs[i].Value); break;
                case 11: e.X1 = D(pairs[i].Value); break;
                case 21: e.Y1 = D(pairs[i].Value); break;
            }
            i++;
        }

        geom.Entities.Add(e);
        return i - 1;
    }

    private static int ReadCircle(List<(int Code, string Value)> pairs, int i, CadGeometry geom)
    {
        var e = new CadCircle();
        i++;
        while (i < pairs.Count && pairs[i].Code != 0)
        {
            switch (pairs[i].Code)
            {
                case 8: e.Layer = pairs[i].Value; break;
                case 10: e.Cx = D(pairs[i].Value); break;
                case 20: e.Cy = D(pairs[i].Value); break;
                case 40: e.Radius = D(pairs[i].Value); break;
            }
            i++;
        }

        geom.Entities.Add(e);
        return i - 1;
    }

    private static int ReadLwPolyline(List<(int Code, string Value)> pairs, int i, CadGeometry geom)
    {
        var e = new CadPolyline();
        double? pendingX = null;
        i++;
        while (i < pairs.Count && pairs[i].Code != 0)
        {
            switch (pairs[i].Code)
            {
                case 8: e.Layer = pairs[i].Value; break;
                case 70:
                    if (int.TryParse(pairs[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
                        e.Closed = (flags & 1) == 1;
                    break;
                case 10:
                    pendingX = D(pairs[i].Value);
                    break;
                case 20:
                    if (pendingX.HasValue)
                    {
                        e.Points.Add(new CadPoint2(pendingX.Value, D(pairs[i].Value)));
                        pendingX = null;
                    }
                    break;
            }
            i++;
        }

        if (!e.Closed && e.Points.Count >= 3)
        {
            var a = e.Points[0];
            var b = e.Points[e.Points.Count - 1];
            if (Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01)
                e.Closed = true;
        }

        geom.Entities.Add(e);
        return i - 1;
    }

    private static int ReadInsert(List<(int Code, string Value)> pairs, int i, CadGeometry geom)
    {
        var e = new CadInsert();
        i++;
        while (i < pairs.Count && pairs[i].Code != 0)
        {
            switch (pairs[i].Code)
            {
                case 8: e.Layer = pairs[i].Value; break;
                case 2: e.BlockName = pairs[i].Value; break;
                case 10: e.X = D(pairs[i].Value); break;
                case 20: e.Y = D(pairs[i].Value); break;
                case 50: e.RotationDeg = D(pairs[i].Value); break;
            }
            i++;
        }

        geom.Entities.Add(e);
        return i - 1;
    }

    private static int ReadText(List<(int Code, string Value)> pairs, int i, CadGeometry geom)
    {
        var e = new CadText();
        i++;
        while (i < pairs.Count && pairs[i].Code != 0)
        {
            switch (pairs[i].Code)
            {
                case 8: e.Layer = pairs[i].Value; break;
                case 1: e.Value = pairs[i].Value; break;
                case 10: e.X = D(pairs[i].Value); break;
                case 20: e.Y = D(pairs[i].Value); break;
            }
            i++;
        }

        geom.Entities.Add(e);
        return i - 1;
    }

    private static double D(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
