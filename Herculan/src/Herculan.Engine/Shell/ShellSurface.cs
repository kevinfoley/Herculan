using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;

namespace Herculan.Engine.Shell;

/// <summary>
/// An 8-bit indexed drawing surface, the size of the shell canvas — what VSHELL's widget paints draw
/// into, ported as it stands rather than translated into quads.
///
/// <para><b>Why indexed and not RGBA.</b> Every shell paint works in palette indices and one of them
/// depends on it: both <c>Text</c> and the edit field colour their glyphs by installing an identity
/// 256-entry lookup table with entry <c>0x29</c> replaced, then re-blitting the area through it
/// (<see cref="Remap"/>) — the font's ink index recoloured after the fact. A surface that had already
/// resolved indices to colours could not do that. Working in indices also makes the checkerboard
/// dither and the title bar's diagonal hatch exact instead of approximated, and costs one texture
/// upload per repaint instead of tens of thousands of quads.</para>
///
/// <para><b>Index 0 is transparent</b>, the same sentinel the sprite banks use, and the surface starts
/// filled with it. That is load-bearing rather than a convenience: a <c>TitledPanel</c> whose body
/// fill is switched off dithers its body in a single colour and leaves the alternate pixels
/// untouched, so the backdrop showing through at 50% is the effect the original gets from not
/// painting them. See docs/retail/shell/screen-layout.md.</para>
///
/// <para>Every primitive clips to the surface, so a widget may draw outside its own rect or off the
/// canvas without a bounds check of its own — which the title bar's hatch does, starting five pixels
/// left of the panel.</para>
/// </summary>
public sealed class ShellSurface {
	/// <summary>The palette index that means "leave this pixel alone", in the banks and here.</summary>
	public const byte Transparent = 0;

	private readonly byte[] _indices;

	public ShellSurface(int width = ShellLayout.CanvasWidth, int height = ShellLayout.CanvasHeight) {
		Width = Math.Max(width, 0);
		Height = Math.Max(height, 0);
		_indices = new byte[Width * Height];
		ClipRect = new ShellRect(0, 0, Width - 1, Height - 1);
	}

	public int Width { get; }

	public int Height { get; }

	/// <summary>
	/// What every primitive is clipped to. It starts as the whole surface and each widget's paint
	/// narrows it to that widget for the duration — <c>Window_BeginPaint</c> (<c>0041f585</c>), the first call in all five
	/// paints, which binds the drawing context to the widget being painted.
	///
	/// <para><b>The paints depend on this rather than measuring.</b> The title bar's diagonal hatch is
	/// the clearest case: it lays down 26 bands on a 28-pixel pitch starting five pixels left of the
	/// widget, which is over 700 pixels of hatch for a panel 357 wide, and it is the clip and nothing
	/// else that stops the surplus reaching the canvas.</para>
	/// </summary>
	public ShellRect ClipRect { get; private set; }

	/// <summary>
	/// Narrows the clip to the intersection of <paramref name="rect"/> and the current one, and returns
	/// what it was so the caller can put it back with <see cref="PopClip"/>.
	/// </summary>
	public ShellRect PushClip(ShellRect rect) {
		var previous = ClipRect;
		ClipRect = new ShellRect(
			Math.Max(rect.X0, previous.X0), Math.Max(rect.Y0, previous.Y0),
			Math.Min(rect.X1, previous.X1), Math.Min(rect.Y1, previous.Y1));
		return previous;
	}

	/// <summary>Restores a clip <see cref="PushClip"/> returned.</summary>
	public void PopClip(ShellRect previous) => ClipRect = previous;

	/// <summary>Whether nothing has been drawn since the last <see cref="Clear"/>.</summary>
	public bool IsBlank {
		get {
			foreach (byte index in _indices) {
				if (index != Transparent) {
					return false;
				}
			}

			return true;
		}
	}

	/// <summary>Returns every pixel to <see cref="Transparent"/>.</summary>
	public void Clear() => Array.Clear(_indices);

	/// <summary>The index at a pixel, or <see cref="Transparent"/> when it is off-surface.</summary>
	public byte At(int x, int y) =>
		x >= 0 && y >= 0 && x < Width && y < Height ? _indices[y * Width + x] : Transparent;

	/// <summary>One pixel — <c>Gfx_PlotPixel</c> (<c>0045999c</c>), which is what every dither and hatch is built from.</summary>
	public void Plot(int x, int y, byte index) {
		if (ClipRect.Contains(x, y)) {
			_indices[y * Width + x] = index;
		}
	}

	/// <summary>
	/// A solid rectangle, corners inclusive — <c>Gfx_FillRect</c> (<c>00457364</c>). The corners are normalized first,
	/// as the callers do for themselves before every call: each one sorts its two x and its two y so a
	/// widget too small for its own inset still fills something rather than nothing.
	/// </summary>
	public void Fill(int x0, int y0, int x1, int y1, byte index) {
		int left = Math.Max(Math.Min(x0, x1), ClipRect.X0);
		int top = Math.Max(Math.Min(y0, y1), ClipRect.Y0);
		int right = Math.Min(Math.Max(x0, x1), ClipRect.X1);
		int bottom = Math.Min(Math.Max(y0, y1), ClipRect.Y1);
		if (right < left || bottom < top) {
			return;
		}

		for (int y = top; y <= bottom; y++) {
			_indices.AsSpan(y * Width + left, right - left + 1).Fill(index);
		}
	}

	/// <summary>
	/// A one-pixel line between two inclusive endpoints — <c>Gfx_DrawLine</c> (<c>004552e4</c>). The shell asks for
	/// horizontal, vertical and 45-degree runs only; Bresenham covers all three without a special case.
	/// </summary>
	public void Line(int x0, int y0, int x1, int y1, byte index) {
		int dx = Math.Abs(x1 - x0);
		int dy = Math.Abs(y1 - y0);
		int stepX = x0 < x1 ? 1 : -1;
		int stepY = y0 < y1 ? 1 : -1;
		int error = dx - dy;

		while (true) {
			Plot(x0, y0, index);
			if (x0 == x1 && y0 == y1) {
				return;
			}

			int doubled = error * 2;
			if (doubled > -dy) {
				error -= dy;
				x0 += stepX;
			}

			if (doubled < dx) {
				error += dx;
				y0 += stepY;
			}
		}
	}

	/// <summary>
	/// Replaces one index with another over a rectangle — the blit mode 6 both text paints finish with,
	/// where the table they install is the identity apart from a single entry.
	///
	/// <para>This is how a shell widget picks its text colour. The fonts carry one ink index each (see
	/// <see cref="Content.HudFont"/>) and the shell's is <c>0x29</c>, so a widget draws its string in
	/// whatever the font has and then remaps that one index to the colour it actually wants — which is
	/// why the same font serves a label, a value and a highlighted row.</para>
	/// </summary>
	public void Remap(int x0, int y0, int x1, int y1, byte from, byte to) {
		if (from == to) {
			return;
		}

		int left = Math.Max(Math.Min(x0, x1), ClipRect.X0);
		int top = Math.Max(Math.Min(y0, y1), ClipRect.Y0);
		int right = Math.Min(Math.Max(x0, x1), ClipRect.X1);
		int bottom = Math.Min(Math.Max(y0, y1), ClipRect.Y1);

		for (int y = top; y <= bottom; y++) {
			for (int x = left; x <= right; x++) {
				int at = y * Width + x;
				if (_indices[at] == from) {
					_indices[at] = to;
				}
			}
		}
	}

	/// <summary>
	/// One glyph, or any other indexed bitmap, at a top-left corner. Source index 0 is left alone, so a
	/// glyph shows only its ink and whatever is under it stays.
	/// </summary>
	/// <param name="flags">
	/// The blitter's flag word. 2 mirrors left to right, as DBSIM's glance view uses it
	/// (docs/retail/formats/cockpit-views.md), and every left/right pair in <c>gam\rpr_*.dat</c> is one frame
	/// placed twice with 0 and 2. 1 is taken as the top-to-bottom mirror, which no shell layout record
	/// uses.
	/// </param>
	public void Blit(DynamixBitmap bitmap, int left, int top, int flags = 0) {
		byte[] source = bitmap.ImageData ?? Array.Empty<byte>();
		int cols = bitmap.Cols;
		int rows = bitmap.Rows;
		bool mirrorX = (flags & MirrorXFlag) != 0;
		bool mirrorY = (flags & MirrorYFlag) != 0;

		for (int y = 0; y < rows; y++) {
			for (int x = 0; x < cols; x++) {
				int at = y * cols + x;
				if (at < source.Length && source[at] != Transparent) {
					Plot(left + (mirrorX ? cols - 1 - x : x), top + (mirrorY ? rows - 1 - y : y), source[at]);
				}
			}
		}
	}

	private const int MirrorYFlag = 1;
	private const int MirrorXFlag = 2;

	/// <summary>
	/// A bitmap drawn to fill a rectangle <paramref name="width"/> by <paramref name="height"/> from
	/// <paramref name="left"/>, <paramref name="top"/> — the blitter's scaled path, <c>Bitmap_BlitScaledClipDispatch</c> (<c>00458e78</c>),
	/// sampled nearest. Source index 0 is left alone, as in <see cref="Blit"/>.
	/// </summary>
	public void ScaledBlit(DynamixBitmap bitmap, int left, int top, int width, int height) {
		byte[] source = bitmap.ImageData ?? Array.Empty<byte>();
		int cols = bitmap.Cols;
		int rows = bitmap.Rows;
		if (width <= 0 || height <= 0 || cols <= 0 || rows <= 0) {
			return;
		}

		for (int y = 0; y < height; y++) {
			int row = y * rows / height;
			for (int x = 0; x < width; x++) {
				int at = row * cols + x * cols / width;
				if (at < source.Length && source[at] != Transparent) {
					Plot(left + x, top + y, source[at]);
				}
			}
		}
	}

	/// <summary>
	/// Another surface drawn stretched over the inclusive rectangle from <paramref name="left"/>,
	/// <paramref name="top"/> to <paramref name="left"/> + <paramref name="width"/>, <paramref name="top"/>
	/// + <paramref name="height"/> — <c>GLBitmap_DrawStretched</c> (<c>0045330c</c>), a texture-mapped quad whose corners carry the
	/// source's corner texels, sampled nearest. Source index 0 is left alone.
	/// </summary>
	public void StretchBlit(ShellSurface source, int left, int top, int width, int height) {
		if (width <= 0 || height <= 0 || source.Width <= 0 || source.Height <= 0) {
			return;
		}

		int x0 = Math.Max(left, ClipRect.X0);
		int x1 = Math.Min(left + width, ClipRect.X1);
		int y0 = Math.Max(top, ClipRect.Y0);
		int y1 = Math.Min(top + height, ClipRect.Y1);
		for (int y = y0; y <= y1; y++) {
			int v = (int)((long)(y - top) * (source.Height - 1) / height);
			for (int x = x0; x <= x1; x++) {
				byte index = source.At((int)((long)(x - left) * (source.Width - 1) / width), v);
				if (index != Transparent) {
					_indices[y * Width + x] = index;
				}
			}
		}
	}

	/// <summary>
	/// A filled convex polygon, vertices as alternating x and y — the solid path of the polygon filler,
	/// <c>Gfx_FillPolygon</c> (<c>00455798</c>). Each row from the topmost vertex to the bottommost is filled between the
	/// leftmost and rightmost points of the outline on that row, both included.
	/// </summary>
	public void FillConvex(ReadOnlySpan<int> xy, byte index) {
		int count = xy.Length / 2;
		if (count < 3) {
			return;
		}

		int top = int.MaxValue;
		int bottom = int.MinValue;
		for (int i = 0; i < count; i++) {
			top = Math.Min(top, xy[i * 2 + 1]);
			bottom = Math.Max(bottom, xy[i * 2 + 1]);
		}

		for (int y = Math.Max(top, ClipRect.Y0); y <= Math.Min(bottom, ClipRect.Y1); y++) {
			int left = int.MaxValue;
			int right = int.MinValue;
			for (int i = 0; i < count; i++) {
				int ax = xy[i * 2];
				int ay = xy[i * 2 + 1];
				int bx = xy[(i + 1) % count * 2];
				int by = xy[(i + 1) % count * 2 + 1];
				if (y < Math.Min(ay, by) || y > Math.Max(ay, by)) {
					continue;
				}

				if (ay == by) {
					left = Math.Min(left, Math.Min(ax, bx));
					right = Math.Max(right, Math.Max(ax, bx));
					continue;
				}

				int x = ax + (int)((long)(bx - ax) * (y - ay) / (by - ay));
				left = Math.Min(left, x);
				right = Math.Max(right, x);
			}

			left = Math.Max(left, ClipRect.X0);
			right = Math.Min(right, ClipRect.X1);
			if (left <= right) {
				_indices.AsSpan(y * Width + left, right - left + 1).Fill(index);
			}
		}
	}

	/// <summary>A filled ellipse inside the inclusive box around (<paramref name="cx"/>, <paramref name="cy"/>) — <c>Gfx_FillEllipse</c> (<c>0045852c</c>)'s solid path.</summary>
	public void FillEllipse(int cx, int cy, int radiusX, int radiusY, byte index) {
		if (radiusX <= 0 || radiusY <= 0) {
			Fill(cx - radiusX, cy - radiusY, cx + radiusX, cy + radiusY, index);
			return;
		}

		for (int dy = -radiusY; dy <= radiusY; dy++) {
			int span = (int)(radiusX * Math.Sqrt(1.0 - (double)dy * dy / ((double)radiusY * radiusY)) + 0.5);
			Fill(cx - span, cy + dy, cx + span, cy + dy, index);
		}
	}

	/// <summary>
	/// Resolves the surface through <paramref name="palette"/> into RGBA8, top row first, with
	/// <see cref="Transparent"/> becoming alpha 0 so the backdrop behind it shows through. An index the
	/// palette does not carry comes out as opaque grey of that index's value, the same fallback
	/// <see cref="ShellArt"/> uses for a bitmap, so a wrong index reads as a wrong shade rather than as
	/// nothing at all.
	/// </summary>
	public ShellImage ToImage(DynamixPalette palette) {
		var pixels = new byte[Width * Height * 4];
		for (int i = 0; i < _indices.Length; i++) {
			byte index = _indices[i];
			if (index == Transparent) {
				continue;
			}

			var color = palette.Colors.TryGetValue(index, out var entry)
				? entry.GetColor()
				: new RgbaColor(255, index, index, index);

			pixels[i * 4] = color.R;
			pixels[i * 4 + 1] = color.G;
			pixels[i * 4 + 2] = color.B;
			pixels[i * 4 + 3] = 255;
		}

		return new ShellImage(pixels, Width, Height);
	}
}
