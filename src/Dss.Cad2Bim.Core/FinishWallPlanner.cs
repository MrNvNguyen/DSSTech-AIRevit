using System;
using System.Collections.Generic;
using System.Linq;

namespace Dss.Cad2Bim.Core;

/// <summary>A face bounding a room (rough wall, structural column or wall, linked or not), model mm.</summary>
public sealed class RoomFace
{
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    /// <summary>Unit normal pointing into the room.</summary>
    public double NormalX { get; set; }
    public double NormalY { get; set; }
}

/// <summary>One finish wall: its centerline, and window holes (along-range from the start, sill and head).</summary>
public sealed class FinishWallLine
{
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public List<(double From, double To, double SillMm, double HeadMm)> Holes { get; } = new();
    public double Length => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
}

/// <summary>
/// Finish walls laid on the room side of each face: centerline half a finish thickness off the face, broken at doors
/// (the finish stops at the frame) and holed at windows (finish below the sill and above the head stays).
/// </summary>
public static class FinishWallPlanner
{
    /// <summary>Pieces shorter than this between doors or corners are left out.</summary>
    public const double MinLengthMm = 50;
    /// <summary>An opening belongs to a face when its center lies within its wall's half thickness of the face plus this.</summary>
    private const double FaceReachMm = 30;
    private const double ParallelSin = 0.02;

    public static List<FinishWallLine> Plan(
        IReadOnlyList<RoomFace> faces, double finishThicknessMm, IReadOnlyList<HostedOpening> doors,
        IReadOnlyList<(HostedOpening Opening, double SillMm)> windows)
    {
        var result = new List<FinishWallLine>();
        var half = finishThicknessMm / 2;
        foreach (var face in faces)
        {
            var dx = face.X1 - face.X0;
            var dy = face.Y1 - face.Y0;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < MinLengthMm)
                continue;
            var ux = dx / len;
            var uy = dy / len;
            var cuts = new List<(double A, double B)>();
            foreach (var d in doors)
            {
                if (Along(face, ux, uy, d, out var c))
                    cuts.Add((c - d.WidthMm / 2, c + d.WidthMm / 2));
            }

            var pieces = Subtract(0, len, cuts);
            foreach (var (a, b) in pieces)
            {
                if (b - a < MinLengthMm)
                    continue;
                var line = new FinishWallLine
                {
                    X0 = face.X0 + ux * a + face.NormalX * half,
                    Y0 = face.Y0 + uy * a + face.NormalY * half,
                    X1 = face.X0 + ux * b + face.NormalX * half,
                    Y1 = face.Y0 + uy * b + face.NormalY * half
                };
                foreach (var (w, sill) in windows)
                {
                    if (!Along(face, ux, uy, w, out var c))
                        continue;
                    var from = Math.Max(a, c - w.WidthMm / 2) - a;
                    var to = Math.Min(b, c + w.WidthMm / 2) - a;
                    if (to - from > 1)
                        line.Holes.Add((from, to, sill, w.HeadMm));
                }

                result.Add(line);
            }
        }

        return result;
    }

    /// <summary>Opening sits in the wall behind this face: parallel, its center within the wall's band.</summary>
    private static bool Along(RoomFace face, double ux, double uy, HostedOpening o, out double c)
    {
        c = 0;
        if (Math.Abs(ux * o.DirY - uy * o.DirX) > ParallelSin)
            return false;
        var off = (o.X - face.X0) * face.NormalX + (o.Y - face.Y0) * face.NormalY;
        // The wall lies behind the face (against the normal).
        if (off > FaceReachMm || off < -(o.WallThicknessMm + FaceReachMm))
            return false;
        c = (o.X - face.X0) * ux + (o.Y - face.Y0) * uy;
        var len = Math.Sqrt((face.X1 - face.X0) * (face.X1 - face.X0) + (face.Y1 - face.Y0) * (face.Y1 - face.Y0));
        return c + o.WidthMm / 2 > 0 && c - o.WidthMm / 2 < len;
    }

    private static List<(double A, double B)> Subtract(double a, double b, List<(double A, double B)> cuts)
    {
        var pieces = new List<(double, double)>();
        var at = a;
        foreach (var cut in cuts.OrderBy(c => c.A))
        {
            if (cut.B <= at || cut.A >= b)
                continue;
            if (cut.A > at)
                pieces.Add((at, cut.A));
            at = Math.Max(at, cut.B);
        }

        if (at < b)
            pieces.Add((at, b));
        return pieces;
    }
}
