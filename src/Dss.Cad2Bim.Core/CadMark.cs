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
		if (Regex.IsMatch(text, "^\\d+(\\.\\d+)?\\s*[xX×*]\\s*\\d+(\\.\\d+)?$"))
		{
			return false;
		}
		return Regex.IsMatch(text, "[A-Za-z]");
	}
}
