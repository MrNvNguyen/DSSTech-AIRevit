using System.Text.RegularExpressions;

namespace Dss.Cad2Bim.Core;

public static class CadMark
{
	/// <summary>
	/// Pile-cap Type Mark style: Đ + digits + optional letter(s), e.g. Đ4, Đ2A, Đ2F, đ3.
	/// Prefers these over beam/grid labels when associating CAD text to an outline.
	/// </summary>
	private static readonly Regex PileCapId = new(
		@"^[Đđ]\d+[A-Za-zĐđ]*$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	/// <summary>
	/// Leading pile-cap id in a Revit type name leaf, e.g. <c>Đ4A-1000x1000x1100mm</c> → Đ4A.
	/// </summary>
	private static readonly Regex PileCapIdPrefix = new(
		@"^[Đđ]\d+[A-Za-zĐđ]*",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
		// \p{L}: Latin + Vietnamese (Đ4, Đ2F) — ASCII [A-Za-z] alone drops Đ.
		return Regex.IsMatch(text, @"\p{L}");
	}

	/// <summary>CAD / Type Mark pile-cap id (Đ4, Đ2A…) — not beam marks or grid letters.</summary>
	public static bool LooksLikePileCapId(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return false;
		return PileCapId.IsMatch(value.Trim());
	}

	/// <summary>
	/// Extract pile-cap id from a Type Mark value or type name leaf
	/// (<c>Đ4A</c>, <c>Đ4A-2500x2500x1100mm</c>, <c>S-FOUN : Đ4A-…</c>).
	/// </summary>
	public static bool TryExtractPileCapId(string? value, out string id)
	{
		id = "";
		if (string.IsNullOrWhiteSpace(value))
			return false;
		var leaf = value.Trim();
		var colon = leaf.LastIndexOf(':');
		if (colon >= 0 && colon + 1 < leaf.Length)
			leaf = leaf.Substring(colon + 1).Trim();
		if (LooksLikePileCapId(leaf))
		{
			id = leaf;
			return true;
		}

		var m = PileCapIdPrefix.Match(leaf);
		if (!m.Success)
			return false;
		// Require end or a separator so Đ4 does not steal Đ4A.
		var end = m.Length;
		if (end < leaf.Length)
		{
			var next = leaf[end];
			if (next is not ('-' or '_' or ' ' or 'x' or 'X' or '×'))
				return false;
		}

		id = m.Value;
		return LooksLikePileCapId(id);
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
