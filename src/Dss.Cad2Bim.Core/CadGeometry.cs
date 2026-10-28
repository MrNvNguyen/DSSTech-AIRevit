using System.Collections.Generic;

namespace Dss.Cad2Bim.Core;

public sealed class CadGeometry
{
    public int? InsUnits { get; set; }
    public string SourcePath { get; set; } = "";
    public List<CadEntity> Entities { get; } = new List<CadEntity>();
}

public abstract class CadEntity
{
    public string Layer { get; set; } = "0";
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
