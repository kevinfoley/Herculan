namespace Herculan.Engine.Render;

/// <summary>
/// A span region: the top row and one inclusive <c>{x0, x1}</c> per row from it, <c>x0</c> the left end — what the
/// original's scan converter writes and its span routines fill. <see cref="ScanConvert"/> builds one by the rules in
/// docs/retail/rendering/polygon-fill.md, "Scan conversion", which DBSIM's two copies share:
/// <c>Poly_ScanConvertToSpanRegion</c> (<c>00484614</c>), the <c>int16</c> one <c>Raster_DrawPolygonDispatch</c>
/// fills from, and <c>Poly_ScanConvert</c> (<c>00493086</c>), the <c>int32</c> one the terrain cell walk uses.
/// </summary>
public readonly record struct SpanRegion(int Top, (int X0, int X1)[] Spans) {
	/// <summary>
	/// Scan-converts a convex outline wound clockwise on screen (y down): the right chain from the top vertex fills
	/// each row's <c>x1</c>, the left chain from the bottom vertex its <c>x0</c>, one Bresenham edge at a time, and
	/// where two edges share a row the later one's value stands. An outline wound the other way comes out with
	/// <c>x0</c> right of <c>x1</c>, which a span fill skips.
	/// </summary>
	public static SpanRegion ScanConvert(IReadOnlyList<(int X, int Y)> points) {
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
			return new SpanRegion(topY, new[] { (points[bottomIndex].X, points[topIndex].X) });
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

		return new SpanRegion(topY, spans);

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
			int dx = Math.Abs(p.X - q.X);
			int dy = Math.Abs(p.Y - q.Y);

			if (dx == 0) {
				for (int row = Math.Min(p.Y, q.Y); row <= Math.Max(p.Y, q.Y); row++) {
					Write(row, p.X, rightChain);
				}

				return;
			}

			if (dy == 0) {
				Write(p.Y, rightChain ? Math.Max(p.X, q.X) : Math.Min(p.X, q.X), rightChain);
				return;
			}

			if (dx == dy) {
				// From the upper endpoint, a step of one per row toward the lower.
				var (upper, lower) = p.Y < q.Y ? (p, q) : (q, p);
				int step = Math.Sign(lower.X - upper.X);
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
