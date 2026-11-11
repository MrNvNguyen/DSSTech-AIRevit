using System;

namespace Dss.Cad2Bim.Core;

/// <summary>
/// Friction loss of a flexible sprinkler hose, the way the makers rate it: as an equivalent length of 1" Schedule 40
/// steel pipe (C = 120) from the catalogue (it depends on the hose's length and bends; corrugated hose loses far more
/// than its own length of pipe), taken through Hazen–Williams at the head's flow Q = K·√P.
/// </summary>
public static class HoseLoss
{
    /// <summary>Inside diameter of 1" Schedule 40 steel pipe.</summary>
    public const double PipeInnerMm = 26.6;

    public const double PipeC = 120;

    /// <summary>The head's flow (L/min) at its pressure (bar), K in L/min/bar^½ (K80 = K5.6).</summary>
    public static double Flow(double k, double pressureBar) => k * Math.Sqrt(Math.Max(0, pressureBar));

    /// <summary>Hazen–Williams loss per metre (bar/m): Q L/min, d mm.</summary>
    public static double PerMetreBar(double flowLpm, double innerMm = PipeInnerMm, double c = PipeC) =>
        6.05e5 * Math.Pow(flowLpm, 1.85) / (Math.Pow(c, 1.85) * Math.Pow(innerMm, 4.87));

    /// <summary>The hose's loss (bar) from its catalogue equivalent length (m of 1" Sch 40).</summary>
    public static double Bar(double flowLpm, double equivalentM) => PerMetreBar(flowLpm) * equivalentM;
}
