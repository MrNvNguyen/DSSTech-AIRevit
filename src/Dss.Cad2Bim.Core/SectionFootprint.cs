namespace Dss.Cad2Bim.Core;

public readonly struct SectionFootprint
{
	public double CenterX { get; }

	public double CenterY { get; }

	public double WidthMm { get; }

	public double DepthMm { get; }

	public double RotationDeg { get; }

	/// <summary>Middle of the outline's axis-aligned box; differs from the centroid on clipped outlines.</summary>
	public double BoxCenterX { get; }

	public double BoxCenterY { get; }

	public SectionFootprint(double centerX, double centerY, double widthMm, double depthMm, double rotationDeg = 0.0,
		double? boxCenterX = null, double? boxCenterY = null)
	{
		CenterX = centerX;
		CenterY = centerY;
		WidthMm = widthMm;
		DepthMm = depthMm;
		RotationDeg = rotationDeg;
		BoxCenterX = boxCenterX ?? centerX;
		BoxCenterY = boxCenterY ?? centerY;
	}
}
