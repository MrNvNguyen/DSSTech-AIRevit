using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

public static class AnnotationSizeParser
{
	public static bool TryParse(string? raw, out AnnotationSize size)
	{
		size = default(AnnotationSize);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}
		string input = raw.Trim().Replace(" ", "").Replace("×", "x")
			.Replace("*", "x");
		input = Regex.Replace(input, "mm", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		Match match = Regex.Match(input, "(?<w>\\d+(?:\\.\\d+)?)[xX](?<h>\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant);
		if (match.Success && Num(match.Groups["w"].Value, out var value) && Num(match.Groups["h"].Value, out var value2))
		{
			size = new AnnotationSize(value, value2, null, input);
			return true;
		}
		Match match2 = Regex.Match(input, "(?<![A-Za-z0-9])[dDØ](?<d>\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant);
		if (match2.Success && Num(match2.Groups["d"].Value, out var value3))
		{
			size = new AnnotationSize(value3, value3, value3, input);
			return true;
		}
		Match match3 = Regex.Match(input, "(?<![A-Za-z0-9])[tT](?<t>\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant);
		if (match3.Success && Num(match3.Groups["t"].Value, out var value4))
		{
			size = new AnnotationSize(value4, null, null, input);
			return true;
		}
		if (Num(input, out var value5))
		{
			size = new AnnotationSize(value5, null, null, input);
			return true;
		}
		MatchCollection matchCollection = Regex.Matches(input, "\\d+(?:\\.\\d+)?", RegexOptions.CultureInvariant);
		if (matchCollection.Count == 1 && Num(matchCollection[0].Value, out var value6))
		{
			size = new AnnotationSize(value6, null, null, input);
			return true;
		}
		size = new AnnotationSize(null, null, null, raw.Trim());
		return true;
	}

	public static string ColumnFamilyTypeHint(AnnotationSize size)
	{
		double? diameterMm = size.DiameterMm;
		if (diameterMm.HasValue)
		{
			double valueOrDefault = diameterMm.GetValueOrDefault();
			return $"D{(int)Math.Round(valueOrDefault)}";
		}
		diameterMm = size.WidthMm;
		if (diameterMm.HasValue)
		{
			double valueOrDefault2 = diameterMm.GetValueOrDefault();
			diameterMm = size.HeightMm;
			if (diameterMm.HasValue)
			{
				double valueOrDefault3 = diameterMm.GetValueOrDefault();
				return $"{(int)Math.Round(valueOrDefault2)} x {(int)Math.Round(valueOrDefault3)}";
			}
		}
		return size.Raw;
	}

	public static string WallTypeHint(AnnotationSize size, string fallback = "Generic - 200mm")
	{
		double? widthMm = size.WidthMm;
		if (widthMm.HasValue)
		{
			double valueOrDefault = widthMm.GetValueOrDefault();
			return $"Generic - {(int)Math.Round(valueOrDefault)}mm";
		}
		return fallback;
	}

	public static string FloorTypeHint(AnnotationSize size, string fallback = "Generic - 150mm")
	{
		double? widthMm = size.WidthMm;
		if (widthMm.HasValue)
		{
			double valueOrDefault = widthMm.GetValueOrDefault();
			return $"Generic - {(int)Math.Round(valueOrDefault)}mm";
		}
		return fallback;
	}

	public static string BeamFamilyTypeHint(AnnotationSize size)
	{
		double? widthMm = size.WidthMm;
		if (widthMm.HasValue)
		{
			double valueOrDefault = widthMm.GetValueOrDefault();
			widthMm = size.HeightMm;
			if (widthMm.HasValue)
			{
				double valueOrDefault2 = widthMm.GetValueOrDefault();
				return $"{(int)Math.Round(valueOrDefault)}x{(int)Math.Round(valueOrDefault2)}";
			}
		}
		return size.Raw;
	}

	private static bool Num(string s, out double value)
	{
		return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}
}
