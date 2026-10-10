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
		SpanList? a = first.Count != 0 ? ScanConvertEitherWinding(first) : null;
		SpanList? b = second.Count != 0 ? ScanConvertEitherWinding(second) : null;

		int bottom = System.Math.Max(a is { } spansA ? spansA.Top + spansA.Spans.Length - 1 : -1,
			b is { } spansB ? spansB.Top + spansB.Spans.Length - 1 : -1);
		if (bottom <= -1) {
			return;
		}

		Emit(a, emit);
		Emit(b, emit);

		static void Emit(SpanList? list, Action<int, int> emit) {
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

	/// <summary>A span region: the top row and one <c>{x0, x1}</c> per row from it, <c>x0</c> the left end.</summary>
	private readonly record struct SpanList(int Top, (int X0, int X1)[] Spans);

	/// <summary>
	/// <c>Poly_BuildSpanList</c> (<c>00472a08</c>) through <c>Poly_ScanConvertEitherWinding</c>
	/// (<c>00492fb5</c>): an outline of three or more points whose signed area
	/// <c>Σ x[i]·y[i+1] − x[i+1]·y[i]</c> is zero or less is scan-converted reversed
	/// (<c>Poly_ScanConvertReversed</c>, <c>00493035</c>). <c>Poly_BuildSpanList</c> moves the points to
	/// the origin first and the spans back after, which changes nothing here: every step below works
	/// on differences.
	/// </summary>
	private static SpanList ScanConvertEitherWinding(List<(int X, int Y)> points) {
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
				return ScanConvert(reversed);
			}
		}

		return ScanConvert(points);
	}

	/// <summary>
	/// <c>Poly_ScanConvert</c> (<c>00493086</c>), the <c>int32</c> scan converter, by the rules in
	/// docs/retail/rendering/polygon-fill.md, "Scan conversion": the right chain from the top vertex fills each row's
	/// <c>x1</c>, the left chain from the bottom vertex its <c>x0</c>, one Bresenham edge at a time, and
	/// where two edges share a row the later one's value stands.
	/// </summary>
	private static SpanList ScanConvert(List<(int X, int Y)> points) {
		int n = points.Count;

		// The top vertex: smallest y, the rightmost of those. The bottom: largest y, the leftmost.
		int topIndex = 0;
		int bottomIndex = 0;
		for (int i = n - 1; i >= 1; i--) {
			var p = points[i];
			var top = points[topIndex];
			if (p.Y < top.Y || (p.Y == top.Y && p.X >= top.X)) {
				topIndex = i;
			}

			var bottom = points[bottomIndex];
			if (p.Y > bottom.Y || (p.Y == bottom.Y && p.X <= bottom.X)) {
				bottomIndex = i;
			}
		}

		int topY = points[topIndex].Y;
		int bottomY = points[bottomIndex].Y;
		if (topY == bottomY) {
			return new SpanList(topY, new[] { (points[bottomIndex].X, points[topIndex].X) });
		}

		var spans = new (int X0, int X1)[bottomY - topY + 1];

		// The right chain runs forward from the top vertex to the first one at the bottom; the left
		// chain forward from the bottom vertex to the first one back at the top.
		var right = Chain(topIndex, p => p.Y >= bottomY);
		var left = Chain(bottomIndex, p => p.Y <= topY);

		for (int i = 0; i + 1 < right.Count; i++) {
			Edge(right[i], right[i + 1], rightChain: true);
		}

		for (int i = 0; i + 1 < left.Count; i++) {
			Edge(left[i], left[i + 1], rightChain: false);
		}

		return new SpanList(topY, spans);

		List<(int X, int Y)> Chain(int start, Func<(int X, int Y), bool> last) {
			var chain = new List<(int X, int Y)>();
			for (int i = start; ; i = (i + 1) % n) {
				chain.Add(points[i]);
				if (last(points[i])) {
					return chain;
				}
			}
		}

		void Write(int row, int x, bool rightChain) {
			ref var span = ref spans[row - topY];
			if (rightChain) {
				span.X1 = x;
			} else {
				span.X0 = x;
			}
		}

		void Edge((int X, int Y) p, (int X, int Y) q, bool rightChain) {
			int dx = System.Math.Abs(p.X - q.X);
			int dy = System.Math.Abs(p.Y - q.Y);

			if (dx == 0) {
				for (int row = System.Math.Min(p.Y, q.Y); row <= System.Math.Max(p.Y, q.Y); row++) {
					Write(row, p.X, rightChain);
				}

				return;
			}

			if (dy == 0) {
				Write(p.Y, rightChain ? System.Math.Max(p.X, q.X) : System.Math.Min(p.X, q.X), rightChain);
				return;
			}

			if (dx == dy) {
				// From the upper endpoint, a step of one per row toward the lower.
				var (upper, lower) = p.Y < q.Y ? (p, q) : (q, p);
				int step = System.Math.Sign(lower.X - upper.X);
				for (int i = 0; i <= dy; i++) {
					Write(upper.Y + i, upper.X + i * step, rightChain);
				}

				return;
			}

			if (dy > dx) {
				// From the endpoint with the smaller x, error 2dx - dy, stepping x while it is not
				// negative. Both chains write the same value.
				var (start, end) = q.X <= p.X ? (q, p) : (p, q);
				int rowStep = end.Y < start.Y ? -1 : 1;
				int error = 2 * dx - dy;
				int x = start.X;
				for (int i = 0; i <= dy; i++) {
					Write(start.Y + i * rowStep, x, rightChain);
					if (error >= 0) {
						x++;
						error += 2 * (dx - dy);
					} else {
						error += 2 * dx;
					}
				}

				return;
			}

			// Shallow: from the outer endpoint — the larger x on the right chain, the smaller on the
			// left — each row takes the outermost x its line puts there.
			var (outer, inner) = rightChain == (p.X > q.X) ? (p, q) : (q, p);
			int toward = rightChain ? -1 : 1;
			int direction = inner.Y < outer.Y ? -1 : 1;
			int err = 2 * dy - dx;
			int along = outer.X + toward;
			Write(outer.Y, outer.X, rightChain);

			for (int r = 1; r <= dy; r++) {
				while (err < 0) {
					along += toward;
					err += 2 * dy;
				}

				Write(outer.Y + r * direction, along, rightChain);
				err += 2 * (dy - dx);
				along += toward;
			}
		}
	}
}
