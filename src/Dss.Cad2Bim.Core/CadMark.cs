using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

public static class CadMark
{
	public static bool IsLabel(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string text = value.Trim();
		if (text.Length > 24)
		{
			return false;
		}
		if (Regex.IsMatch(text, "^\\d+(\\.\\d+)?$"))
		{
			return false;
		}
		if (IsSizeOnly(text))
		{
			return false;
		}
		return Regex.IsMatch(text, "[A-Za-z]");
	}

	/// <summary>Size-only annotation such as <c>30x60</c> / <c>300X600</c> (not a Mark).</summary>
	public static bool IsSizeOnly(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return false;
		return Regex.IsMatch(value.Trim(), "^\\d+(\\.\\d+)?\\s*[xX×*]\\s*\\d+(\\.\\d+)?$", RegexOptions.CultureInvariant);
	}

	/// <summary>Mark, mark+size, or size-only — anything ModelRevit may assign to a beam.</summary>
	public static bool IsAnnotation(string? value) =>
		IsLabel(value) || IsSizeOnly(value);
}
