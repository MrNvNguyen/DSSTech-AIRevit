using System.Collections.Generic;

namespace Dss.Cad2Bim.Core;

public sealed class CadGeometry
{
    public int? InsUnits { get; set; }
    public string SourcePath { get; set; } = "";
    public List<CadEntity> Entities { get; } = new List<CadEntity>();
    /// <summary>
    /// Short number texts (1, 12, 1.1): grid bubble names. Kept apart from <see cref="Entities"/> because the
    /// other readers take a bare number for a dimension, not a mark.
    /// </summary>
    public List<CadText> NumberTexts { get; } = new List<CadText>();
    /// <summary>
    /// Longer notes with an MEP size or elevation ("THANG CÁP 1200X100, H=2800"): for the MEP tools only, apart from
    /// <see cref="Entities"/> where the other readers would take them for marks.
    /// </summary>
    public List<CadText> NoteTexts { get; } = new List<CadText>();
}

public abstract class CadEntity
{
    public string Layer { get; set; } = "0";
    /// <summary>The block reference the stroke was drawn in (all strokes of one insert share it); 0 in model space.</summary>
    public int BlockRef { get; set; }
}

public sealed class CadLine : CadEntity
{
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
}

public sealed class CadCircle : CadEntity
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double Radius { get; set; }
}

public sealed class CadPolyline : CadEntity
{
    public bool Closed { get; set; }
    /// <summary>An arc cut into chords (a bend drawn round), not a polyline drawn as such.</summary>
    public bool FromArc { get; set; }
    /// <summary>
    /// Bulge of the segment from each point to the next (tan of a quarter of its arc, + counter-clockwise), as a DWG
    /// polyline stores its round parts; null when read without them.
    /// </summary>
    public List<double>? Bulges { get; set; }
    /// <summary>Constant (global) width the polyline is drawn with (mm); 0 for a plain stroke. A busway is one wide stroke.</summary>
    public double WidthMm { get; set; }
    public List<CadPoint2> Points { get; } = new List<CadPoint2>();
}

public sealed class CadInsert : CadEntity
{
    public string BlockName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double RotationDeg { get; set; }
}

public sealed class CadText : CadEntity
{
    public string Value { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Text direction in degrees (0 = +X). Missing / unknown stays 0.</summary>
    public double RotationDeg { get; set; }
    /// <summary>Estimated middle of the text box; null when only the insertion point is known.</summary>
    public double? CenterX { get; set; }
    public double? CenterY { get; set; }
}

public readonly struct CadPoint2
{
    public CadPoint2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}
