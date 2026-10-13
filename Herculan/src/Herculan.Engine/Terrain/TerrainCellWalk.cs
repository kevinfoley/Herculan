using Herculan.Engine.Render;

namespace Herculan.Engine.Terrain;

/// <summary>
/// Which cells <c>Terrain_DrawVisibleCells</c> (<c>0046d0a4</c>)'s walk visits: the cells
/// <c>CellWalk_Polygon</c> (<c>00471e38</c>) or <c>CellWalk_PolygonByColumn</c> (<c>004723f8</c>) hands
/// the run callback for the visible region's polygon about the viewer's cell (docs/retail/rendering/polygon-fill.md,
/// "Walking a polygon's cells"; docs/retail/rendering/terrain-drawing.md, "The cell walk"). A cell the walk
/// visits has its objects drawn; one it does not keeps them undrawn — see
/// <see cref="Render.ObjectDrawTable"/>.
///
/// <para>Only the set is reproduced, not the order: <see cref="Render.TerrainPaintOrder"/> ranks the
/// cells. The set is the four quadrants' scan-converted spans, so it is the original's own integer
/// edge rules that decide which cells along the polygon's border are in it, not a point-in-polygon
/// test.</para>
/// </summary>
public static class TerrainCellWalk {
	/// <summary>
	/// Calls <paramref name="visit"/> once for each cell the walk of <paramref name="polygon"/> (the
	/// region in cell coordinates, <c>grid+0xc8</c>) about cell (<paramref name="centreX"/>,
	/// <paramref name="centreY"/>) visits — by column when <paramref name="byColumn"/>, as the view's
	/// heading chooses. A cell two quadrants' spans both reach is visited twice.
	/// </summary>
	public static void Visit(IReadOnlyList<(int X, int Y)> polygon, int centreX, int centreY,
			bool byColumn, Action<int, int> visit) {
		var points = new List<(int X, int Y)>(polygon.Count);
		foreach (var point in polygon) {
			// Point_RotateQuarterAbout (004723ac): CellWalk_PolygonByColumn turns the polygon a quarter
			// about the centre and walks rows of that.
			points.Add(byColumn
				? (centreX + (point.Y - centreY), centreY - (point.X - centreX))
				: point);
		}

		Action<int, int> emit = byColumn
			// Point_UnrotateQuarterAbout (004723d4) on each run's ends: a rotated row is a column.
			? (x, row) => visit(centreX + (centreY - row), centreY + (x - centreX))
			: visit;

		// The rows past the centre's, y >= cy + 1: quadrant 0 (x >= cx + 1) and quadrant 1 (x <= cx).
		var half = ClipToHalfPlane(keepHigh: true, alongX: false, centreY + 1, points);
		WalkHalf(ClipToHalfPlane(keepHigh: true, alongX: true, centreX + 1, half),
			ClipToHalfPlane(keepHigh: false, alongX: true, centreX, half), emit);

		// The rest, y <= cy: quadrant 2 (x <= cx - 1) and quadrant 3 (x >= cx), the centre among them.
		half = ClipToHalfPlane(keepHigh: false, alongX: false, centreY, points);
		WalkHalf(ClipToHalfPlane(keepHigh: false, alongX: true, centreX - 1, half),
			ClipToHalfPlane(keepHigh: true, alongX: true, centreX, half), emit);
	}

	/// <summary>
	/// One half of the walk: both quadrants' span lists, every row of each, every cell of each row's
	/// span. The half is skipped when the larger of its two bottom rows is negative — the walk's
	/// <c>-1 &lt; row</c> test.
	/// </summary>
	private static void WalkHalf(List<(int X, int Y)> first, List<(int X, int Y)> second,
			Action<int, int> emit) {
		SpanRegion? a = first.Count != 0 ? ScanConvertEitherWinding(first) : null;
		SpanRegion? b = second.Count != 0 ? ScanConvertEitherWinding(second) : null;

		int bottom = System.Math.Max(a is { } spansA ? spansA.Top + spansA.Spans.Length - 1 : -1,
			b is { } spansB ? spansB.Top + spansB.Spans.Length - 1 : -1);
		if (bottom <= -1) {
			return;
		}

		Emit(a, emit);
		Emit(b, emit);

		static void Emit(SpanRegion? list, Action<int, int> emit) {
			if (list is not { } spans) {
				return;
			}

			for (int i = 0; i < spans.Spans.Length; i++) {
				var (x0, x1) = spans.Spans[i];

				// A run's callback steps from one end to the other until it passes the far end, so an
				// inverted span would never end in the original. A convex outline cannot make one.
				for (int x = x0; x <= x1; x++) {
					emit(x, spans.Top + i);
				}
			}
		}
	}

	/// <summary>
	/// <c>Poly_ClipToHalfPlane</c> (<c>00472ac0</c>): one Sutherland–Hodgman pass keeping the part of
	/// the polygon with X (<paramref name="alongX"/>) or Y at or above <paramref name="value"/>
	/// (<paramref name="keepHigh"/>) or at or below it, points on the line kept. What each edge emits is
	/// the function's 19-byte selector table, indexed by the previous and current points' sides, whose
	/// byte picks one of five cases through its jump table (docs/retail/rendering/polygon-fill.md,
	/// "Walking a polygon's cells").
	/// </summary>
	private static List<(int X, int Y)> ClipToHalfPlane(bool keepHigh, bool alongX, int value,
			List<(int X, int Y)> polygon) {
		var output = new List<(int X, int Y)>(polygon.Count + 2);
		if (polygon.Count < 1) {
			return output;
		}

		var previous = polygon[^1];
		int previousSide = Side(previous);

		foreach (var current in polygon) {
			int side = Side(current);
			switch (Action(previousSide, side)) {
				case 2:
					output.Add(current);
					break;

				case 3:
					output.Add(Crossing(previous, current));
					output.Add(current);
					break;

				case 4:
					output.Add(Crossing(previous, current));
					break;
			}

			previous = current;
			previousSide = side;
		}

		return output;

		int Side((int X, int Y) point) => System.Math.Sign((alongX ? point.X : point.Y) - value);

		// The selector bytes at 00472bdd: 1 emits nothing, 2 the current point, 3 the crossing and then
		// the current point, 4 the crossing alone.
		int Action(int from, int to) => SelectorBytes[(keepHigh ? 0 : 10) + from * 3 + to + 4];

		// Measured from the previous point, the product in 32 bits and the division truncating.
		(int X, int Y) Crossing((int X, int Y) from, (int X, int Y) to) => alongX
			? (value, from.Y + unchecked((value - from.X) * (to.Y - from.Y)) / (to.X - from.X))
			: (from.X + unchecked((value - from.Y) * (to.X - from.X)) / (to.Y - from.Y), value);
	}

	private static readonly byte[] SelectorBytes = {
		1, 2, 3, 1, 2, 2, 4, 2, 2, 0, 2, 2, 4, 2, 2, 1, 3, 2, 1,
	};

	/// <summary>
	/// <c>Poly_BuildSpanList</c> (<c>00472a08</c>) through <c>Poly_ScanConvertEitherWinding</c>
	/// (<c>00492fb5</c>): an outline of three or more points whose signed area
	/// <c>Σ x[i]·y[i+1] − x[i+1]·y[i]</c> is zero or less is scan-converted reversed
	/// (<c>Poly_ScanConvertReversed</c>, <c>00493035</c>). <c>Poly_BuildSpanList</c> moves the points to
	/// the origin first and the spans back after, which changes nothing here: every step of
	/// <see cref="SpanRegion.ScanConvert"/> works on differences.
	/// </summary>
	private static SpanRegion ScanConvertEitherWinding(List<(int X, int Y)> points) {
		if (points.Count > 2) {
			long sum = 0;
			for (int i = 0; i < points.Count; i++) {
				var p = points[i];
				var q = points[(i + 1) % points.Count];
				sum += (long)p.X * q.Y - (long)q.X * p.Y;
			}

			if (sum <= 0) {
				var reversed = new List<(int X, int Y)>(points);
				reversed.Reverse();
				return SpanRegion.ScanConvert(reversed);
			}
		}

		return SpanRegion.ScanConvert(points);
	}
}
