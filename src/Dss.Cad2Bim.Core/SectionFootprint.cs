namespace Dss.Cad2Bim.Core;

public readonly struct SectionFootprint
{
	public double CenterX { get; }

	public double CenterY { get; }

	public double WidthMm { get; }

	public double DepthMm { get; }

	public double RotationDeg { get; }

	public SectionFootprint(double centerX, double centerY, double widthMm, double depthMm, double rotationDeg = 0.0)
	{
		CenterX = centerX;
		CenterY = centerY;
		WidthMm = widthMm;
		DepthMm = depthMm;
		RotationDeg = rotationDeg;
	}
}
