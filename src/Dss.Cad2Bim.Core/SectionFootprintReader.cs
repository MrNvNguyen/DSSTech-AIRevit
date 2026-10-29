using System;
using System.Collections.Generic;

namespace Dss.Cad2Bim.Core;

public static class SectionFootprintReader
{
	private readonly struct Seg
	{
		public double X0 { get; }

		public double Y0 { get; }

		public double X1 { get; }

		public double Y1 { get; }

		public Seg(double x0, double y0, double x1, double y1)
		{
			X0 = x0;
			Y0 = y0;
			X1 = x1;
			Y1 = y1;
		}
	}

	internal const double AxisAlignTolMm = 2.0;

	internal const double MinEdgeMm = 80.0;

	internal const double CoverTolMm = 5.0;

	public static List<SectionFootprint> FromEntities(IEnumerable<CadEntity> entities)
	{
		List<SectionFootprint> list = new List<SectionFootprint>();
		List<Seg> list2 = new List<Seg>();
		foreach (CadEntity entity in entities)
		{
			if (!(entity is CadCircle circle))
			{
				if (!(entity is CadPolyline poly))
				{
					if (entity is CadLine cadLine)
					{
						list2.Add(new Seg(cadLine.X0, cadLine.Y0, cadLine.X1, cadLine.Y1));
					}
				}
				else if (!TryAddPolyline(poly, list))
				{
					AddPolylineEdges(poly, list2);
				}
			}
			else
			{
				TryAddCircle(circle, list);
			}
		}
		foreach (SectionFootprint item in Cluster(list2))
		{
			if (!NearExisting(list, item.CenterX, item.CenterY))
			{
				list.Add(item);
			}
		}
		return list;
	}

	private static void TryAddCircle(CadCircle circle, List<SectionFootprint> result)
	{
		double num = Round10(circle.Radius * 2.0);
		if (Accept(num, num))
		{
			result.Add(new SectionFootprint(circle.Cx, circle.Cy, num, num));
		}
	}

	private static bool TryAddPolyline(CadPolyline poly, List<SectionFootprint> result)
	{
		if (!poly.Closed || poly.Points.Count < 3)
		{
			return false;
		}
		if (!TryBox(poly.Points, out var footprint))
		{
			return false;
		}
		result.Add(footprint);
		return true;
	}

	private static void AddPolylineEdges(CadPolyline poly, List<Seg> lines)
	{
		if (poly.Points.Count >= 2)
		{
			for (int i = 1; i < poly.Points.Count; i++)
			{
				lines.Add(new Seg(poly.Points[i - 1].X, poly.Points[i - 1].Y, poly.Points[i].X, poly.Points[i].Y));
			}
			if (poly.Closed)
			{
				CadPoint2 cadPoint = poly.Points[poly.Points.Count - 1];
				CadPoint2 cadPoint2 = poly.Points[0];
				lines.Add(new Seg(cadPoint.X, cadPoint.Y, cadPoint2.X, cadPoint2.Y));
			}
		}
	}

	private static List<SectionFootprint> Cluster(List<Seg> lines)
	{
		List<SectionFootprint> list = new List<SectionFootprint>();
		int count = lines.Count;
		if (count == 0)
		{
			return list;
		}
		int[] parent = new int[count];
		for (int j = 0; j < count; j++)
		{
			parent[j] = j;
		}
		Dictionary<(long X, long Y), List<int>> buckets = new Dictionary<(long, long), List<int>>();
		for (int k = 0; k < count; k++)
		{
			if (!(SegLength(lines[k]) > 2500.0))
			{
				Touch(k, lines[k].X0, lines[k].Y0);
				Touch(k, lines[k].X1, lines[k].Y1);
			}
		}
		Dictionary<int, List<int>> dictionary = new Dictionary<int, List<int>>();
		for (int l = 0; l < count; l++)
		{
			if (!(SegLength(lines[l]) > 2500.0))
			{
				int key = Find(l);
				if (!dictionary.TryGetValue(key, out var value))
				{
					value = (dictionary[key] = new List<int>());
				}
				value.Add(l);
			}
		}
		foreach (List<int> value4 in dictionary.Values)
		{
			if (TryFootprintFromSegments(value4, lines, out var footprint))
			{
				list.Add(footprint);
				continue;
			}
			foreach (SectionFootprint item in RectanglesInCluster(value4, lines))
			{
				list.Add(item);
			}
			foreach (SectionFootprint item2 in RotatedRectangles(value4, lines))
			{
				if (!NearExisting(list, item2.CenterX, item2.CenterY))
				{
					list.Add(item2);
				}
			}
		}
		return list;
		int Find(int i)
		{
			while (parent[i] != i)
			{
				parent[i] = parent[parent[i]];
				i = parent[i];
			}
			return i;
		}
		void Touch(int index, double x, double y)
		{
			long num = (long)Math.Round(x / 40.0);
			long num2 = (long)Math.Round(y / 40.0);
			for (int m = -1; m <= 1; m++)
			{
				for (int n = -1; n <= 1; n++)
				{
					if (buckets.TryGetValue((num + m, num2 + n), out List<int> value2))
					{
						for (int num3 = 0; num3 < value2.Count; num3++)
						{
							Union(index, value2[num3]);
						}
					}
				}
			}
			if (!buckets.TryGetValue((num, num2), out List<int> value3))
			{
				value3 = new List<int>();
				buckets[(num, num2)] = value3;
			}
			value3.Add(index);
		}
		void Union(int a, int b)
		{
			a = Find(a);
			b = Find(b);
			if (a != b)
			{
				parent[b] = a;
			}
		}
	}

	private static List<SectionFootprint> RectanglesInCluster(List<int> group, List<Seg> lines)
	{
		List<Seg> list = new List<Seg>();
		List<Seg> list2 = new List<Seg>();
		for (int i = 0; i < group.Count; i++)
		{
			Seg seg = lines[group[i]];
			if (IsAxisAligned(seg))
			{
				if (Math.Abs(seg.Y1 - seg.Y0) <= 2.0)
				{
					list.Add(seg);
				}
				else
				{
					list2.Add(seg);
				}
			}
		}
		List<SectionFootprint> list3 = new List<SectionFootprint>();
		if (list.Count < 2 || list2.Count < 2)
		{
			return list3;
		}
		Dictionary<int, List<int>> dictionary = new Dictionary<int, List<int>>();
		for (int j = 0; j < list2.Count; j++)
		{
			int key = (int)Math.Round((list2[j].X0 + list2[j].X1) / 2.0 / 50.0);
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = (dictionary[key] = new List<int>());
			}
			value.Add(j);
		}
		for (int k = 0; k < list2.Count; k++)
		{
			double num = (list2[k].X0 + list2[k].X1) / 2.0;
			int num2 = (int)Math.Floor((num + 80.0) / 50.0);
			int num3 = (int)Math.Ceiling((num + 2500.0) / 50.0);
			for (int l = num2; l <= num3; l++)
			{
				if (!dictionary.TryGetValue(l, out var value2))
				{
					continue;
				}
				for (int m = 0; m < value2.Count; m++)
				{
					int index = value2[m];
					double num4 = (list2[index].X0 + list2[index].X1) / 2.0;
					double num5 = num4 - num;
					if (num5 < 80.0 || num5 > 2500.0)
					{
						continue;
					}
					List<double> list5 = new List<double>();
					for (int n = 0; n < list.Count; n++)
					{
						if (CoversSpan(MinX(list[n]), MaxX(list[n]), num, num4))
						{
							list5.Add((list[n].Y0 + list[n].Y1) / 2.0);
						}
					}
					list5.Sort();
					for (int num6 = 1; num6 < list5.Count; num6++)
					{
						double num7 = list5[num6] - list5[num6 - 1];
						if (!(num7 < 80.0) && CoversSpan(MinY(list2[k]), MaxY(list2[k]), list5[num6 - 1], list5[num6]) && CoversSpan(MinY(list2[index]), MaxY(list2[index]), list5[num6 - 1], list5[num6]))
						{
							double num8 = Round10(num5);
							double num9 = Round10(num7);
							if (Accept(num8, num9))
							{
								list3.Add(new SectionFootprint((num + num4) / 2.0, (list5[num6 - 1] + list5[num6]) / 2.0, num8, num9));
							}
						}
					}
				}
			}
		}
		list3.Sort((SectionFootprint p, SectionFootprint q) => (p.WidthMm * p.DepthMm).CompareTo(q.WidthMm * q.DepthMm));
		List<SectionFootprint> list6 = new List<SectionFootprint>();
		foreach (SectionFootprint item in list3)
		{
			bool flag = false;
			foreach (SectionFootprint item2 in list6)
			{
				if (Math.Abs(item.CenterX - item2.CenterX) < (item.WidthMm + item2.WidthMm) / 2.0 && Math.Abs(item.CenterY - item2.CenterY) < (item.DepthMm + item2.DepthMm) / 2.0)
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				list6.Add(item);
			}
		}
		return list6;
	}

	private static bool CoversSpan(double segMin, double segMax, double start, double end)
	{
		double num = end - start;
		if (num < 80.0)
		{
			return false;
		}
		if (Math.Min(segMax, end) - Math.Max(segMin, start) < num * 0.55)
		{
			return false;
		}
		double num2 = ((segMin <= start) ? 0.0 : (segMin - start));
		double num3 = ((segMax >= end) ? 0.0 : (end - segMax));
		double num4 = num * 0.45;
		if (num2 <= num4)
		{
			return num3 <= num4;
		}
		return false;
	}

	private static double SegLength(Seg s)
	{
		double num = s.X1 - s.X0;
		double num2 = s.Y1 - s.Y0;
		return Math.Sqrt(num * num + num2 * num2);
	}

	private static double MinX(Seg s)
	{
		return Math.Min(s.X0, s.X1);
	}

	private static double MaxX(Seg s)
	{
		return Math.Max(s.X0, s.X1);
	}

	private static double MinY(Seg s)
	{
		return Math.Min(s.Y0, s.Y1);
	}

	private static double MaxY(Seg s)
	{
		return Math.Max(s.Y0, s.Y1);
	}

	private static bool TryFootprintFromSegments(List<int> group, List<Seg> lines, out SectionFootprint footprint)
	{
		footprint = default(SectionFootprint);
		List<Seg> list = new List<Seg>();
		for (int i = 0; i < group.Count; i++)
		{
			Seg seg = lines[group[i]];
			if (IsAxisAligned(seg))
			{
				list.Add(seg);
			}
		}
		if (list.Count == 0)
		{
			return false;
		}
		if (TryRectFromAxisAligned(list, out footprint))
		{
			return true;
		}
		double num = double.MaxValue;
		double num2 = double.MaxValue;
		double num3 = double.MinValue;
		double num4 = double.MinValue;
		for (int j = 0; j < list.Count; j++)
		{
			Seg seg2 = list[j];
			num = Math.Min(num, Math.Min(seg2.X0, seg2.X1));
			num2 = Math.Min(num2, Math.Min(seg2.Y0, seg2.Y1));
			num3 = Math.Max(num3, Math.Max(seg2.X0, seg2.X1));
			num4 = Math.Max(num4, Math.Max(seg2.Y0, seg2.Y1));
		}
		double num5 = Round10(num3 - num);
		double num6 = Round10(num4 - num2);
		if (!Accept(num5, num6))
		{
			return false;
		}
		footprint = new SectionFootprint((num + num3) / 2.0, (num2 + num4) / 2.0, num5, num6);
		return true;
	}

	private static bool IsAxisAligned(Seg seg)
	{
		double num = Math.Abs(seg.X1 - seg.X0);
		double num2 = Math.Abs(seg.Y1 - seg.Y0);
		if (Math.Sqrt(num * num + num2 * num2) < 40.0)
		{
			return false;
		}
		if (!(num <= 2.0))
		{
			return num2 <= 2.0;
		}
		return true;
	}

	private static bool TryRectFromAxisAligned(List<Seg> aligned, out SectionFootprint footprint)
	{
		footprint = default(SectionFootprint);
		List<Seg> list = new List<Seg>();
		List<Seg> list2 = new List<Seg>();
		for (int i = 0; i < aligned.Count; i++)
		{
			Seg item = aligned[i];
			double num = Math.Abs(item.X1 - item.X0);
			if (Math.Abs(item.Y1 - item.Y0) <= 2.0)
			{
				list.Add(item);
			}
			else if (num <= 2.0)
			{
				list2.Add(item);
			}
		}
		if (list.Count < 2 || list2.Count < 2)
		{
			return false;
		}
		double num2 = double.MaxValue;
		double num3 = double.MinValue;
		double num4 = double.MaxValue;
		double num5 = double.MinValue;
		for (int j = 0; j < list2.Count; j++)
		{
			double val = (list2[j].X0 + list2[j].X1) / 2.0;
			num2 = Math.Min(num2, val);
			num3 = Math.Max(num3, val);
		}
		for (int k = 0; k < list.Count; k++)
		{
			double val2 = (list[k].Y0 + list[k].Y1) / 2.0;
			num4 = Math.Min(num4, val2);
			num5 = Math.Max(num5, val2);
		}
		double num6 = Round10(num3 - num2);
		double num7 = Round10(num5 - num4);
		if (!Accept(num6, num7))
		{
			return false;
		}
		footprint = new SectionFootprint((num2 + num3) / 2.0, (num4 + num5) / 2.0, num6, num7);
		return true;
	}

	private static bool TryBox(IReadOnlyList<CadPoint2> points, out SectionFootprint footprint)
	{
		if (TryOrientedBox(points, out footprint))
		{
			return true;
		}
		double num = double.MaxValue;
		double num2 = double.MaxValue;
		double num3 = double.MinValue;
		double num4 = double.MinValue;
		for (int i = 0; i < points.Count; i++)
		{
			num = Math.Min(num, points[i].X);
			num2 = Math.Min(num2, points[i].Y);
			num3 = Math.Max(num3, points[i].X);
			num4 = Math.Max(num4, points[i].Y);
		}
		double num5 = Round10(num3 - num);
		double num6 = Round10(num4 - num2);
		if (!Accept(num5, num6))
		{
			footprint = default(SectionFootprint);
			return false;
		}
		footprint = new SectionFootprint((num + num3) / 2.0, (num2 + num4) / 2.0, num5, num6);
		return true;
	}

	private static bool TryOrientedBox(IReadOnlyList<CadPoint2> points, out SectionFootprint footprint)
	{
		footprint = default(SectionFootprint);
		if (points.Count < 3)
		{
			return false;
		}
		double num = 0.0;
		double num2 = 1.0;
		double num3 = 0.0;
		for (int i = 0; i < points.Count; i++)
		{
			int index = (i + 1) % points.Count;
			double num4 = points[index].X - points[i].X;
			double num5 = points[index].Y - points[i].Y;
			double num6 = Math.Sqrt(num4 * num4 + num5 * num5);
			if (!(num6 < num))
			{
				num = num6;
				num2 = num4 / num6;
				num3 = num5 / num6;
			}
		}
		if (num < 80.0 || FoldedAngle(num2, num3) < 3.0)
		{
			return false;
		}
		double num7 = double.MaxValue;
		double num8 = double.MinValue;
		double num9 = double.MaxValue;
		double num10 = double.MinValue;
		double num11 = 0.0 - num3;
		double num12 = num2;
		for (int j = 0; j < points.Count; j++)
		{
			double val = points[j].X * num2 + points[j].Y * num3;
			double val2 = points[j].X * num11 + points[j].Y * num12;
			num7 = Math.Min(num7, val);
			num8 = Math.Max(num8, val);
			num9 = Math.Min(num9, val2);
			num10 = Math.Max(num10, val2);
		}
		double num13 = Round10(num8 - num7);
		double num14 = Round10(num10 - num9);
		if (!Accept(num13, num14))
		{
			return false;
		}
		double num15 = (num7 + num8) / 2.0;
		double num16 = (num9 + num10) / 2.0;
		double degrees = Math.Atan2(num3, num2) * 180.0 / Math.PI;
		footprint = new SectionFootprint(num15 * num2 + num16 * num11, num15 * num3 + num16 * num12, num13, num14, NormHalfTurn(degrees));
		return true;
	}

	private static List<SectionFootprint> RotatedRectangles(List<int> group, List<Seg> lines)
	{
		List<SectionFootprint> list = new List<SectionFootprint>();
		if (!TryFrameAngle(group, lines, out var degrees))
		{
			return list;
		}
		double num = degrees * Math.PI / 180.0;
		double num2 = Math.Cos(num);
		double num3 = Math.Sin(num);
		List<Seg> list2 = new List<Seg>();
		List<int> list3 = new List<int>();
		for (int i = 0; i < group.Count; i++)
		{
			if (TrySnapFrame(lines[group[i]], num2, num3, out var snapped))
			{
				list3.Add(list2.Count);
				list2.Add(snapped);
			}
		}
		foreach (SectionFootprint item in RectanglesInCluster(list3, list2))
		{
			double centerX = item.CenterX * num2 - item.CenterY * num3;
			double centerY = item.CenterX * num3 + item.CenterY * num2;
			list.Add(new SectionFootprint(centerX, centerY, item.WidthMm, item.DepthMm, NormHalfTurn(degrees)));
		}
		return list;
	}

	private static bool TryFrameAngle(List<int> group, List<Seg> lines, out double degrees)
	{
		degrees = 0.0;
		double[] array = new double[46];
		double[] array2 = new double[46];
		for (int i = 0; i < group.Count; i++)
		{
			Seg s = lines[group[i]];
			double num = SegLength(s);
			if (!(num < 80.0))
			{
				double num2 = FoldedAngle(s.X1 - s.X0, s.Y1 - s.Y0);
				int num3 = (int)Math.Round(num2 / 2.0);
				if (num3 < 0)
				{
					num3 = 0;
				}
				if (num3 > 45)
				{
					num3 = 45;
				}
				array[num3] += num2 * num;
				array2[num3] += num;
			}
		}
		int num4 = -1;
		double num5 = 0.0;
		for (int j = 0; j < array.Length; j++)
		{
			if (array2[j] > num5)
			{
				num5 = array2[j];
				num4 = j;
			}
		}
		if (num4 < 0 || num5 < 160.0)
		{
			return false;
		}
		degrees = array[num4] / array2[num4];
		if (degrees >= 3.0)
		{
			return degrees <= 87.0;
		}
		return false;
	}

	private static bool TrySnapFrame(Seg src, double cos, double sin, out Seg snapped)
	{
		snapped = default(Seg);
		double num = src.X0 * cos + src.Y0 * sin;
		double num2 = (0.0 - src.X0) * sin + src.Y0 * cos;
		double num3 = src.X1 * cos + src.Y1 * sin;
		double num4 = (0.0 - src.X1) * sin + src.Y1 * cos;
		double num5 = num3 - num;
		double num6 = num4 - num2;
		double num7 = Math.Sqrt(num5 * num5 + num6 * num6);
		if (num7 < 40.0)
		{
			return false;
		}
		if (Math.Abs(num5) / num7 >= 0.97)
		{
			double num8 = (num2 + num4) / 2.0;
			snapped = new Seg(num, num8, num3, num8);
			return true;
		}
		if (Math.Abs(num6) / num7 >= 0.97)
		{
			double num9 = (num + num3) / 2.0;
			snapped = new Seg(num9, num2, num9, num4);
			return true;
		}
		return false;
	}

	private static double FoldedAngle(double dx, double dy)
	{
		double num = Math.Atan2(dy, dx) * 180.0 / Math.PI;
		num %= 90.0;
		if (num < 0.0)
		{
			num += 90.0;
		}
		return num;
	}

	private static double NormHalfTurn(double degrees)
	{
		while (degrees <= -90.0)
		{
			degrees += 180.0;
		}
		while (degrees > 90.0)
		{
			degrees -= 180.0;
		}
		return degrees;
	}

	/// <summary>
	/// Column section from outline. L/b &gt; 4 is a wall (vách) on shared CAD layers — see WallCenterlineReader.
	/// </summary>
	internal const double MaxColumnAspect = 4.0;

	/// <summary>Pile caps / pads / raft outlines — larger than column Accept (max 2500).</summary>
	internal const double MinFoundationEdgeMm = 200.0;

	internal const double MaxFoundationEdgeMm = 50000.0;

	internal const double MaxFoundationAspect = 50.0;

	/// <summary>
	/// Closed polylines and circles sized for footings/caps/rafts (not column Accept limits).
	/// Line-pair clustering stays column-only; large pads are expected as closed outlines.
	/// </summary>
	public static List<SectionFootprint> FromEntitiesFoundation(IEnumerable<CadEntity> entities)
	{
		List<SectionFootprint> list = new List<SectionFootprint>();
		foreach (CadEntity entity in entities)
		{
			if (entity is CadCircle circle)
			{
				double dia = Round10(circle.Radius * 2.0);
				if (AcceptFoundation(dia, dia))
					list.Add(new SectionFootprint(circle.Cx, circle.Cy, dia, dia));
			}
			else if (entity is CadPolyline poly && poly.Points.Count >= 3
			         && (poly.Closed || FoundationPolylineNearlyClosed(poly))
			         && TryBoxFoundation(poly.Points, out var footprint))
			{
				if (!NearExisting(list, footprint.CenterX, footprint.CenterY))
					list.Add(footprint);
			}
		}

		return list;
	}

	/// <summary>Treat first≈last (within 5 mm) as closed — DWG explode often drops the Closed flag.</summary>
	private static bool FoundationPolylineNearlyClosed(CadPolyline poly)
	{
		if (poly.Points.Count < 3)
			return false;
		var a = poly.Points[0];
		var b = poly.Points[poly.Points.Count - 1];
		var dx = a.X - b.X;
		var dy = a.Y - b.Y;
		return dx * dx + dy * dy <= 25.0;
	}

	/// <summary>
	/// Foundation outline → polygon centroid + AABB size + rotation from longest edge.
	/// <see cref="SectionFootprint.RotationDeg"/> = CAD long-edge angle (world +X = 0°).
	/// Irregular / clipped: tip = vertex farthest from centroid; orient so tip lies on +perp of long axis
	/// (full 0–360° — 180° matters for Tamgiac/clipped families). Revit instance rotation may add −90°
	/// when the family long side is local +Y — see <see cref="ModelRevitPlanner.FoundationInstanceRotationDeg"/>.
	/// </summary>
	private static bool TryBoxFoundation(IReadOnlyList<CadPoint2> points, out SectionFootprint footprint)
	{
		footprint = default;
		if (points.Count < 3)
			return false;

		int n = FoundationVertexCount(points);
		if (n < 3)
			return false;

		double minX = points[0].X, maxX = points[0].X, minY = points[0].Y, maxY = points[0].Y;
		for (int i = 1; i < n; i++)
		{
			if (points[i].X < minX) minX = points[i].X;
			if (points[i].X > maxX) maxX = points[i].X;
			if (points[i].Y < minY) minY = points[i].Y;
			if (points[i].Y > maxY) maxY = points[i].Y;
		}

		double w = Round10(maxX - minX);
		double d = Round10(maxY - minY);
		if (!AcceptFoundation(w, d))
			return false;

		PolygonCentroid(points, n, out double cx, out double cy);
		double rotationDeg = FoundationRotationDeg(points, n, cx, cy);
		footprint = new SectionFootprint(cx, cy, w, d, rotationDeg);
		return true;
	}

	private static int FoundationVertexCount(IReadOnlyList<CadPoint2> points)
	{
		int n = points.Count;
		if (n >= 2)
		{
			var a = points[0];
			var b = points[n - 1];
			var dx = a.X - b.X;
			var dy = a.Y - b.Y;
			if (dx * dx + dy * dy <= 25.0)
				n--;
		}

		return n;
	}

	/// <summary>Shoelace centroid; falls back to vertex average for degenerate rings.</summary>
	private static void PolygonCentroid(IReadOnlyList<CadPoint2> points, int n, out double cx, out double cy)
	{
		double area2 = 0, cxAcc = 0, cyAcc = 0;
		for (int i = 0; i < n; i++)
		{
			int j = (i + 1) % n;
			double cross = points[i].X * points[j].Y - points[j].X * points[i].Y;
			area2 += cross;
			cxAcc += (points[i].X + points[j].X) * cross;
			cyAcc += (points[i].Y + points[j].Y) * cross;
		}

		if (Math.Abs(area2) < 1e-6)
		{
			cx = 0;
			cy = 0;
			for (int i = 0; i < n; i++)
			{
				cx += points[i].X;
				cy += points[i].Y;
			}

			cx /= n;
			cy /= n;
			return;
		}

		cx = cxAcc / (3.0 * area2);
		cy = cyAcc / (3.0 * area2);
	}

	private static double FoundationRotationDeg(IReadOnlyList<CadPoint2> points, int n, double cx, double cy)
	{
		if (n < 3)
			return 0;

		double bestLen = 0;
		double ux = 1, uy = 0;
		double lx0 = points[0].X, ly0 = points[0].Y;
		for (int i = 0; i < n; i++)
		{
			int j = (i + 1) % n;
			double ex = points[j].X - points[i].X;
			double ey = points[j].Y - points[i].Y;
			double len = Math.Sqrt(ex * ex + ey * ey);
			if (len < 1.0)
				continue;
			if (len > bestLen)
			{
				bestLen = len;
				ux = ex / len;
				uy = ey / len;
				lx0 = points[i].X;
				ly0 = points[i].Y;
			}
		}

		if (bestLen < MinFoundationEdgeMm * 0.5)
			return 0;

		// Quad = rectangle (incl. elongated 2:1 Đ2A). Tip logic is for n≥5 clipped caps only —
		// elongated quads used to hit "short < 0.55×long" and return ±180° for horizontal boxes.
		if (n == 4)
			return NormHalfTurn(Math.Atan2(uy, ux) * 180.0 / Math.PI);

		// Tip = vertex farthest from the long-edge line (clip of Đ3), not from centroid
		// (base corners of a wide pad are farther from the centroid than the tip).
		double tipX = cx, tipY = cy, tipAbs = -1;
		double px = -uy;
		double py = ux;
		for (int i = 0; i < n; i++)
		{
			double s = (points[i].X - lx0) * px + (points[i].Y - ly0) * py;
			double abs = Math.Abs(s);
			if (abs > tipAbs)
			{
				tipAbs = abs;
				tipX = points[i].X;
				tipY = points[i].Y;
			}
		}

		// Orient long axis so tip lies on +perpendicular — fixes 180° vs CAD for Đ3.
		double tipDot = (tipX - cx) * px + (tipY - cy) * py;
		if (tipDot < 0)
		{
			ux = -ux;
			uy = -uy;
		}

		return Math.Atan2(uy, ux) * 180.0 / Math.PI;
	}

	private static bool AcceptFoundation(double width, double depth)
	{
		double min = Math.Min(width, depth);
		double max = Math.Max(width, depth);
		return min >= MinFoundationEdgeMm
		       && max <= MaxFoundationEdgeMm
		       && max / Math.Max(min, 1.0) <= MaxFoundationAspect;
	}

	private static bool Accept(double width, double depth)
	{
		double num = Math.Min(width, depth);
		double num2 = Math.Max(width, depth);
		if (num >= 80.0 && num2 <= 2500.0)
		{
			return num2 / Math.Max(num, 1.0) <= MaxColumnAspect;
		}
		return false;
	}

	private static bool NearExisting(List<SectionFootprint> existing, double x, double y)
	{
		for (int i = 0; i < existing.Count; i++)
		{
			double num = existing[i].CenterX - x;
			double num2 = existing[i].CenterY - y;
			if (num * num + num2 * num2 < 22500.0)
			{
				return true;
			}
		}
		return false;
	}

	private static double Round10(double value)
	{
		// Prefer 50 mm stations when CAD noise is within 8 mm (912 → 900, not 910).
		var r50 = Math.Round(value / 50.0, MidpointRounding.AwayFromZero) * 50.0;
		if (Math.Abs(value - r50) <= 8.0)
			return r50;
		return Math.Round(value / 10.0, MidpointRounding.AwayFromZero) * 10.0;
	}
}
