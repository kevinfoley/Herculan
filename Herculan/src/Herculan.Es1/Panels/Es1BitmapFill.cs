using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Numerics;
using Herculan.Es1.Shell.Map;

namespace Herculan.Es1.Panels;

/// <summary>
/// Brush modes 5 and 7 on <c>VGA_8</c>: <c>VGA8_FillBitmapPolygon</c>, which stretches a bitmap over a polygon, and
/// <c>Bitmap_BlitRotatedScaled</c>, which fills a turned rect with a whole bitmap through it
/// (docs/es1/rendering/polygon-shading.md#bitmap-fills-brush-modes-5-and-7). The fill is the simulator's
/// (<c>ES1DBSIM 000534ac</c>) and the shell's (<c>00074fcc</c>) alike: <c>es2_naming.py diff</c> finds them the same but
/// for where the top cut's buffers live, and their edge, span and cut routines are the same code. A canvas with
/// <see cref="Es1PanelCanvas.Regions"/> is clip mode 2 and walks rows; any other is clip mode 1 and walks columns.
/// </summary>
public static class Es1BitmapFill {
	/// <summary>
	/// <c>Bitmap_BlitRotatedUnpacked</c>(<paramref name="bitmap"/>, <paramref name="angle"/>, <paramref name="size"/>,
	/// <paramref name="flip"/>, <paramref name="position"/>) and the <c>Bitmap_BlitRotatedScaled</c> it calls
	/// (docs/es1/shell/map.md#the-shapes): the brush {5, 4, source quad, the bitmap} filled over the destination quad
	/// (0, 0), (<i>w</i>, 0), (<i>w</i>, <i>h</i>), (0, <i>h</i>), each corner turned by <see cref="Rotation2D"/> through
	/// <see cref="Rotate2DPoint"/> and moved by the position. The source quad is (0, 0), (<i>c</i>, 0), (<i>c</i>,
	/// <i>r</i>), (0, <i>r</i>): with <paramref name="shell"/>, the shell's <c>00065ce4</c>, <i>c</i> and <i>r</i> are the
	/// bitmap's columns and rows; otherwise, <c>ES1DBSIM</c>'s <c>00076630</c>, one less each. Flip bit 2 swaps corners 0
	/// and 1 and corners 2 and 3 of the source, then bit 1 swaps 0 and 3 and 1 and 2. The bitmap's pixels are those its
	/// load decoded, where retail unpacks a packed bitmap for the call with <c>Bitmap_UnpackToScratch</c> (the shell's
	/// <c>00050544</c>, <c>ES1DBSIM</c>'s <c>000672cc</c>).
	/// </summary>
	public static void BlitRotatedScaled(Es1PanelCanvas canvas, DynamixBitmap? bitmap, short angle, (int W, int H) size,
			int flip, (int X, int Y) position, bool shell) {
		if (bitmap == null) {
			return;
		}

		int columns = shell ? bitmap.Cols : bitmap.Cols - 1, rows = shell ? bitmap.Rows : bitmap.Rows - 1;
		var source = new (int U, int V)[] { (0, 0), (columns, 0), (columns, rows), (0, rows) };
		if ((flip & 2) != 0) {
			(source[0], source[1]) = (source[1], source[0]);
			(source[3], source[2]) = (source[2], source[3]);
		}

		if ((flip & 1) != 0) {
			(source[0], source[3]) = (source[3], source[0]);
			(source[1], source[2]) = (source[2], source[1]);
		}

		var matrix = Rotation2D(angle, shell);
		var corners = new (int X, int Y)[] { (0, 0), (size.W, 0), (size.W, size.H), (0, size.H) };
		var destination = corners.Select(corner => {
			var (x, y) = Rotate2DPoint(corner, matrix);
			return unchecked((x + position.X, y + position.Y));
		}).ToArray();
		FillPolygon(canvas, destination, source, bitmap, table: null);
	}

	/// <summary>
	/// <c>Math_BuildRotation2D</c>(<paramref name="angle"/>): with <paramref name="shell"/> the shell's
	/// (<see cref="Es1ShellTrig.Rotation2D"/>), otherwise <c>ES1DBSIM</c>'s (<c>000694b8</c>), {cos <i>a</i>, sin <i>a</i>,
	/// −sin <i>a</i>, cos <i>a</i>} through <see cref="SimTrig.Cos"/>.
	/// </summary>
	public static (short M0, short M1, short M2, short M3) Rotation2D(short angle, bool shell) {
		if (shell) {
			return Es1ShellTrig.Rotation2D(angle);
		}

		short cos = SimTrig.Cos(angle), sin = SimTrig.Sin(angle);
		return (cos, sin, (short)-sin, cos);
	}

	/// <summary>
	/// <c>Math_Rotate2DPoint</c> (the shell's <c>0005628c</c>, <c>ES1DBSIM</c>'s <c>00064f7c</c>), and the rotation of
	/// <c>Transform2D_ApplyToPoint</c>: (<i>x</i>·<i>m0</i> + <i>y</i>·<i>m2</i>, <i>x</i>·<i>m1</i> + <i>y</i>·<i>m3</i>),
	/// each summed in 64 bits with <c>0x2000</c> and shifted right 14.
	/// </summary>
	public static (int X, int Y) Rotate2DPoint((int X, int Y) point, (short M0, short M1, short M2, short M3) m) => (
		unchecked((int)(((long)point.X * m.M0 + (long)point.Y * m.M2 + 0x2000) >> 14)),
		unchecked((int)(((long)point.X * m.M1 + (long)point.Y * m.M3 + 0x2000) >> 14)));

	/// <summary>
	/// <c>Raster_DrawPolygonDispatch</c> of <paramref name="points"/> under a brush of mode 5 (no
	/// <paramref name="table"/>) or 7, which hands the points, moved by the canvas's translation, to the driver's
	/// <c>VGA8_FillBitmapPolygon</c> with <paramref name="texture"/>, one texture point in texels for each point
	/// (docs/es1/rendering/polygon-shading.md#bitmap-fills-brush-modes-5-and-7). A texel the walk reads outside the
	/// bitmap, where retail reads past its pixels, leaves the pixel as it was, an engine guard.
	/// </summary>
	public static void FillPolygon(Es1PanelCanvas canvas, IReadOnlyList<(int X, int Y)> points,
			IReadOnlyList<(int U, int V)> texture, DynamixBitmap? bitmap, byte[]? table) {
		if (bitmap?.ImageData is not { } pixels || points.Count < 3 || texture.Count < points.Count) {
			return;
		}

		var (dx, dy) = canvas.Translation;
		var moved = points.Select(p => unchecked((p.X + dx, p.Y + dy))).ToArray();
		new Fill(canvas, pixels, bitmap.Cols, table).Run(moved, texture.Take(points.Count).ToArray());
	}

	/// <summary>
	/// One edge of the walk, the 13 dwords <c>VGA8_BitmapEdgeSetupColumns</c> and <c>VGA8_BitmapEdgeSetupRows</c> fill:
	/// the direction round the ring <c>+0</c>, the steps left <c>+4</c>, the vertex it runs to <c>+8</c>, <i>u</i> and
	/// <i>v</i> in 16.16 <c>+0xc</c>/<c>+0x10</c> and their steps <c>+0x14</c>/<c>+0x18</c>, the other coordinate
	/// <c>+0x1c</c>, its whole step <c>+0x20</c>, carry <c>+0x24</c>, error <c>+0x28</c>, remainder <c>+0x2c</c> and
	/// denominator <c>+0x30</c>.
	/// </summary>
	private sealed class Edge {
		public int Direction, Count, Next, U, V, DU, DV, S, Whole, Carry, Error, Remainder, Denominator;
	}

	/// <summary>One call of <c>VGA8_FillBitmapPolygon</c>: the points in screen coordinates and its globals.</summary>
	private sealed class Fill(Es1PanelCanvas canvas, byte[] pixels, int stride, byte[]? table) {
		/// <summary>Whether the walk goes by columns, clip mode 1, rather than by rows, clip mode 2.</summary>
		private readonly bool _byColumns = canvas.Regions == null;

		private (int X, int Y)[] _points = [];
		private (int U, int V)[] _texture = [];

		/// <summary>The far vertex, the first with the largest walked coordinate (<c>000aa0ec</c>).</summary>
		private int _far;

		/// <summary>
		/// The body: the extremes, the first smallest and first largest of the walked coordinate giving the start and the
		/// far vertex; nothing for a polygon with no width or no height or wholly outside the canvas's rect; a polygon whose
		/// top is above the rect's cut with <see cref="ClipTop"/> and taken again; otherwise the walk.
		/// </summary>
		public void Run((int X, int Y)[] points, (int U, int V)[] texture) {
			var rect = canvas.Rect;
			_points = points;
			_texture = texture;
			while (_points.Length > 2) {
				int minX = 0x7fff, minY = 0x7fff, maxX = -0x8000, maxY = -0x8000, start = 0;
				for (int i = 0; i < _points.Length; i++) {
					var (x, y) = _points[i];
					if (y < minY) {
						minY = y;
						start = _byColumns ? start : i;
					}

					if (y > maxY) {
						maxY = y;
						_far = _byColumns ? _far : i;
					}

					if (x < minX) {
						minX = x;
						start = _byColumns ? i : start;
					}

					if (x > maxX) {
						maxX = x;
						_far = _byColumns ? i : _far;
					}
				}

				if (maxY <= minY || maxX <= minX || maxY < rect.Y0 || rect.Y1 < minY || maxX < rect.X0 || rect.X1 < minX) {
					return;
				}

				if (minY < rect.Y0) {
					(_points, _texture) = ClipTop(rect.Y0, points, texture);
					continue;
				}

				Walk(start, _byColumns ? minX : minY);
				return;
			}
		}

		/// <summary>
		/// The two edges from <paramref name="start"/>, by columns A forward and B backward, by rows A backward and B
		/// forward; from <paramref name="first"/>, each line not before the rect's near side drawn from A's other coordinate
		/// to B's, then both edges stepped, until an edge ends, the line passes the rect's far side, or B is before A.
		/// </summary>
		private void Walk(int start, int first) {
			var rect = canvas.Rect;
			var a = new Edge { Direction = _byColumns ? 1 : -1 };
			var b = new Edge { Direction = _byColumns ? -1 : 1 };
			Setup(start, a);
			Setup(start, b);
			int near = _byColumns ? rect.X0 : rect.Y0, far = _byColumns ? rect.X1 : rect.Y1;
			if (far < first || b.S < a.S) {
				return;
			}

			for (int line = first; ; line++) {
				if (near <= line) {
					if (_byColumns) {
						SpanColumn(line, a, b);
					} else {
						SpanRow(line, a, b);
					}
				}

				if (!Step(a) || !Step(b) || far < line + 1 || b.S < a.S) {
					return;
				}
			}
		}

		/// <summary>The walked coordinate of vertex <paramref name="i"/>: <i>x</i> by columns, <i>y</i> by rows.</summary>
		private int Walked(int i) => _byColumns ? _points[i].X : _points[i].Y;

		/// <summary>The other coordinate of vertex <paramref name="i"/>.</summary>
		private int Other(int i) => _byColumns ? _points[i].Y : _points[i].X;

		/// <summary>
		/// <c>VGA8_BitmapEdgeSetupColumns</c> (<c>000757cc</c>) or <c>VGA8_BitmapEdgeSetupRows</c> (<c>00075648</c>): from
		/// <paramref name="vertex"/>, the next vertex in the edge's direction, wrapping round the ring, past those with the
		/// same walked coordinate; false on reaching the far vertex. Otherwise the steps are the walked difference,
		/// <i>u</i> and <i>v</i> start at the vertex's texture point in 16.16 and step by <c>Math_Q16Divide</c> of their
		/// differences over the steps, and the other coordinate steps by the quotient with a carry when the error turns
		/// positive, so step <i>k</i> is at its start plus ⌈<i>k</i> · difference / steps⌉.
		/// </summary>
		private bool Setup(int vertex, Edge edge) {
			unchecked {
				while (vertex != _far) {
					int next = vertex + edge.Direction;
					next = next >= _points.Length ? 0 : next < 0 ? _points.Length - 1 : next;
					int count = Walked(next) - Walked(vertex);
					if (count != 0) {
						edge.Count = count;
						edge.Next = next;
						edge.U = _texture[vertex].U << 16;
						edge.V = _texture[vertex].V << 16;
						edge.DU = SimMath.Q16DivideRounded((_texture[next].U << 16) - edge.U, count << 16);
						edge.DV = SimMath.Q16DivideRounded((_texture[next].V << 16) - edge.V, count << 16);
						edge.S = Other(vertex);
						int delta = Other(next) - Other(vertex);
						if (delta < 0) {
							edge.Carry = -1;
							delta = -delta;
							edge.Error = 1 - count;
							edge.Whole = -(delta / count);
						} else {
							edge.Carry = 1;
							edge.Error = 0;
							edge.Whole = delta / count;
						}

						edge.Remainder = delta % count;
						edge.Denominator = count;
						return true;
					}

					vertex = next;
				}

				return false;
			}
		}

		/// <summary>
		/// <c>VGA8_BitmapEdgeStepColumns</c> (<c>000755f0</c>) or <c>VGA8_BitmapEdgeStepRows</c> (<c>00075598</c>): one
		/// step, or at the edge's last step the next edge set up from its vertex.
		/// </summary>
		private bool Step(Edge edge) {
			if (--edge.Count == 0) {
				return Setup(edge.Next, edge);
			}

			unchecked {
				edge.U += edge.DU;
				edge.V += edge.DV;
				edge.S += edge.Whole;
				edge.Error += edge.Remainder;
				if (edge.Error > 0) {
					edge.S += edge.Carry;
					edge.Error -= edge.Denominator;
				}
			}

			return true;
		}

		/// <summary>
		/// <c>VGA8_SpanBitmapColumn</c> (<c>0007bd6e</c>): column <paramref name="x"/> from A's <i>y</i> to B's, both
		/// included, cut to the rect's rows, the pixels cut off at the top still stepping.
		/// </summary>
		private void SpanColumn(int x, Edge a, Edge b) {
			var rect = canvas.Rect;
			if (a.S > rect.Y1 || b.S < rect.Y0) {
				return;
			}

			var (u, v, du, dv) = Steps(a, b);
			int y = a.S;
			if (y < rect.Y0) {
				u = unchecked(u + SimMath.Q16MultiplyRounded(du, (rect.Y0 - y) << 16));
				v = unchecked(v + SimMath.Q16MultiplyRounded(dv, (rect.Y0 - y) << 16));
				y = rect.Y0;
			}

			for (int last = Math.Min(b.S, rect.Y1); y <= last; y++) {
				Store(x, y, u, v);
				u = unchecked(u + du);
				v = unchecked(v + dv);
			}
		}

		/// <summary>
		/// <c>VGA8_SpanBitmapRow</c> (<c>0007b9c8</c>) in clip mode 2: row <paramref name="y"/> from A's <i>x</i> to B's,
		/// when that run meets the rect's columns, each pixel drawn when a region of the list holds it. Retail draws the
		/// run's cut to each region holding the row in turn; a cut to an empty run, as a rect region with no columns gives,
		/// is a 16-bit count of 0 that its loop takes as 65,536 pixels, which this per-pixel test does not draw, an engine
		/// guard.
		/// </summary>
		private void SpanRow(int y, Edge a, Edge b) {
			var rect = canvas.Rect;
			if (a.S > rect.X1 || b.S < rect.X0) {
				return;
			}

			var (u, v, du, dv) = Steps(a, b);
			for (int x = a.S; x <= b.S; x++) {
				if (canvas.Regions!.Contains(x, y)) {
					Store(x, y, u, v);
				}

				u = unchecked(u + du);
				v = unchecked(v + dv);
			}
		}

		/// <summary>
		/// A span's first sample and steps: over <i>n</i> = B − A + 1 pixels, <i>du</i> = <c>Math_Q16Divide</c>(B's
		/// <i>u</i> − A's, <i>n</i> &lt;&lt; 16) and <i>v</i> likewise, the first pixel at A's plus half a step.
		/// </summary>
		private static (int U, int V, int DU, int DV) Steps(Edge a, Edge b) {
			unchecked {
				int n = (b.S - a.S + 1) << 16;
				int du = SimMath.Q16DivideRounded(b.U - a.U, n), dv = SimMath.Q16DivideRounded(b.V - a.V, n);
				return (a.U + (du >> 1), a.V + (dv >> 1), du, dv);
			}
		}

		/// <summary>
		/// One pixel: the texel ⌊<i>v</i> + ½⌋ · columns + ⌊<i>u</i> + ½⌋, written through the table when there is one,
		/// otherwise written unless it is 0.
		/// </summary>
		private void Store(int x, int y, int u, int v) {
			int index = unchecked(Round(v) * stride + Round(u));
			if ((uint)index >= (uint)pixels.Length) {
				return;
			}

			byte texel = pixels[index];
			if (table != null) {
				canvas.Display.Plot(x, y, table[texel]);
			} else if (texel != 0) {
				canvas.Display.Plot(x, y, texel);
			}

			static int Round(int q) => (q >> 16) + ((q & 0xffff) >= 0x8000 ? 1 : 0);
		}

		/// <summary>
		/// <c>VGA8_BitmapPolyClipTop</c> (<c>00075434</c>): the ring from vertex 0, each vertex with <i>y</i> at or below
		/// <paramref name="top"/> kept, and for each edge crossing it the crossing of <i>x</i>, <i>u</i> and <i>v</i> from
		/// the vertex the edge leaves, each division truncating.
		/// </summary>
		private static ((int X, int Y)[] Points, (int U, int V)[] Texture) ClipTop(int top, (int X, int Y)[] points,
				(int U, int V)[] texture) {
			var outPoints = new List<(int X, int Y)>();
			var outTexture = new List<(int U, int V)>();
			unchecked {
				for (int i = 0; i < points.Length; i++) {
					int n = (i + 1) % points.Length;
					var (xi, yi) = points[i];
					var (xn, yn) = points[n];
					if (top <= yi) {
						outPoints.Add(points[i]);
						outTexture.Add(texture[i]);
					}

					if (top <= yi != top <= yn) {
						int span = yi - yn;
						var (ui, vi) = texture[i];
						var (un, vn) = texture[n];
						outPoints.Add(((yi - top) * (xn - xi) / span + xi, top));
						outTexture.Add(((yi - top) * (un - ui) / span + ui, vi - (top - yi) * (vn - vi) / span));
					}
				}
			}

			return (outPoints.ToArray(), outTexture.ToArray());
		}
	}
}
