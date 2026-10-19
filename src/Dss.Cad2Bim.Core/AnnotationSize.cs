namespace Dss.Cad2Bim.Core;

public readonly struct AnnotationSize
{
	public double? WidthMm { get; }

	public double? HeightMm { get; }

	public double? DiameterMm { get; }

	public string Raw { get; }

	public AnnotationSize(double? widthMm, double? heightMm, double? diameterMm, string raw)
	{
		WidthMm = widthMm;
		HeightMm = heightMm;
		DiameterMm = diameterMm;
		Raw = raw;
	}
}
