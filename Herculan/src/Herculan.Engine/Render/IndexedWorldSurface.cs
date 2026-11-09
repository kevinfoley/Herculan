using Herculan.Engine.Gl;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// A software display's video memory as this engine shows it: a store of cells, each a palette index or
/// <see cref="WorldCell"/>, over a world layer the 3D renderer draws into at a whole scale <i>k</i> of the store's
/// resolution. Whatever owns the store keeps the cells on the CPU and hands them over with <see cref="UploadCells"/>; a
/// cell marked <see cref="WorldCell"/> shows the world layer's <i>k</i> × <i>k</i> pixels at the same place, so the
/// 3D scene keeps the window's resolution while the 2D drawing around it stays a palette index until it is shown, and
/// a palette change recolours it as a DAC would. None of it is specific to one game.
///
/// <para>The world layer has the store's shape, <see cref="Width"/> × <see cref="Rows"/> cells, so whatever the owner
/// copies between two places of the store it copies here too (<see cref="CopyWorld"/>), and a world pixel follows the
/// cell that names it. <see cref="Present"/> shows a window of the store from a start address, reading the cells in
/// the store's linear order as a CRT controller scans memory.</para>
/// </summary>
public sealed class IndexedWorldSurface : IDisposable {
	/// <summary>The cell value that shows the world layer; 0 to 255 are palette indices.</summary>
	public const ushort WorldCell = 0x100;

	/// <summary>The largest scale the world layer is drawn at.</summary>
	public const int MaxScale = 8;

	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly uint _vertexArray;
	private readonly uint _cells;
	private GpuTexture? _palette;
	private uint _framebuffer;
	private uint _worldColor;
	private uint _worldDepthStencil;
	private uint _cursor;
	private int _cursorWidth, _cursorRows;
	private bool _cursorOpaque;

	// KeepWorld's runs, the store row its first row of kept pixels holds, and the kept pixels' target.
	private IReadOnlyList<(int X, int Y, int Width)> _kept = [];
	private int _keptTop;
	private uint _keptFramebuffer, _keptColor;
	private (int Width, int Height) _keptSize;

	/// <summary>A store of <paramref name="width"/> × <paramref name="rows"/> cells, all palette index 0, and a world layer at scale 1.</summary>
	public IndexedWorldSurface(GL gl, int width, int rows) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
		_gl = gl;
		Width = width;
		Rows = rows;
		_shader = ShaderProgram.Load(gl, "IndexedWorldSurface.glsl");
		_vertexArray = gl.GenVertexArray();

		_cells = gl.GenTexture();
		gl.BindTexture(TextureTarget.Texture2D, _cells);
		gl.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
		var zero = new ushort[width * rows];
		unsafe {
			fixed (ushort* pixels = zero) {
				gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R16ui, (uint)width, (uint)rows, 0,
					PixelFormat.RedInteger, PixelType.UnsignedShort, pixels);
			}
		}

		gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
		// An integer texture is complete only with nearest filtering.
		gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
		gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
		gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
		gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
		gl.BindTexture(TextureTarget.Texture2D, 0);

		CreateWorld(1);
	}

	/// <summary>
	/// The cursor sprite <see cref="Present"/> draws over the window when given a place for it: <paramref name="rows"/>
	/// rows of <paramref name="width"/> palette indices, an index 0 leaving the window's pixel unless
	/// <paramref name="opaque"/>. An empty sprite draws nothing.
	/// </summary>
	public void SetCursor(ReadOnlySpan<byte> pixels, int width, int rows, bool opaque) {
		if (width <= 0 || rows <= 0 || pixels.Length < width * rows) {
			_cursorWidth = _cursorRows = 0;
			return;
		}

		if (_cursor == 0) {
			_cursor = _gl.GenTexture();
		}

		_gl.BindTexture(TextureTarget.Texture2D, _cursor);
		_gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
		unsafe {
			fixed (byte* data = pixels) {
				_gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8ui, (uint)width, (uint)rows, 0,
					PixelFormat.RedInteger, PixelType.UnsignedByte, data);
			}
		}

		_gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
		_gl.BindTexture(TextureTarget.Texture2D, 0);
		_cursorWidth = width;
		_cursorRows = rows;
		_cursorOpaque = opaque;
	}

	/// <summary>The cells a row holds.</summary>
	public int Width { get; }

	/// <summary>The store's rows.</summary>
	public int Rows { get; }

	/// <summary>The world layer's pixels to a cell, each way.</summary>
	public int Scale { get; private set; }

	/// <summary>
	/// Draws the world layer at <paramref name="scale"/> from now on, held to 1–<see cref="MaxScale"/>: the layer is made
	/// again at the new size with what it held scaled into it, so the world pixels already drawn stay shown.
	/// </summary>
	public void SetScale(int scale) {
		scale = Math.Clamp(scale, 1, MaxScale);
		if (scale == Scale) {
			return;
		}

		uint oldFramebuffer = _framebuffer, oldColor = _worldColor, oldDepth = _worldDepthStencil;
		int oldScale = Scale;
		CreateWorld(scale);

		_gl.Disable(EnableCap.ScissorTest);
		_gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, oldFramebuffer);
		_gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _framebuffer);
		_gl.BlitFramebuffer(0, 0, Width * oldScale, Rows * oldScale, 0, 0, Width * scale, Rows * scale,
			ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

		_gl.DeleteFramebuffer(oldFramebuffer);
		_gl.DeleteTexture(oldColor);
		_gl.DeleteRenderbuffer(oldDepth);
	}

	/// <summary>
	/// Sends rows <paramref name="firstRow"/> to <paramref name="firstRow"/> + <paramref name="rowCount"/> − 1 of
	/// <paramref name="cells"/>, the whole store row by row, to the GPU.
	/// </summary>
	public void UploadCells(ReadOnlySpan<ushort> cells, int firstRow, int rowCount) {
		if (cells.Length < Width * Rows) {
			throw new ArgumentException($"{cells.Length} cells cannot hold a {Width}x{Rows} store.", nameof(cells));
		}

		firstRow = Math.Clamp(firstRow, 0, Rows);
		rowCount = Math.Clamp(rowCount, 0, Rows - firstRow);
		if (rowCount == 0) {
			return;
		}

		_gl.BindTexture(TextureTarget.Texture2D, _cells);
		_gl.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
		unsafe {
			fixed (ushort* pixels = cells.Slice(firstRow * Width, rowCount * Width)) {
				_gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, firstRow, (uint)Width, (uint)rowCount,
					PixelFormat.RedInteger, PixelType.UnsignedShort, pixels);
			}
		}

		_gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
		_gl.BindTexture(TextureTarget.Texture2D, 0);
	}

	/// <summary>The live palette, 256 RGBA colours, that the cells' indices show through from the next <see cref="Present"/>.</summary>
	public void SetPalette(ReadOnlySpan<byte> rgba) {
		if (_palette == null) {
			_palette = new GpuTexture(_gl, rgba, 256, 1);
		} else {
			_palette.Update(rgba, 256, 1);
		}
	}

	/// <summary>
	/// Binds the world layer for drawing the rect of <paramref name="width"/> × <paramref name="height"/> cells at
	/// (<paramref name="x"/>, <paramref name="y"/>), rows counted down from the store's top: the viewport and the scissor
	/// set to it and its depth and stencil cleared. Returns the GL viewport, in the layer's pixels from its bottom-left,
	/// that the 3D pass is drawn with. <see cref="EndWorld"/> puts the window back.
	/// </summary>
	public (int X, int Y, int Width, int Height) BeginWorld(int x, int y, int width, int height) {
		var rect = (X: x * Scale, Y: (Rows - (y + height)) * Scale, Width: width * Scale, Height: height * Scale);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.Viewport(rect.X, rect.Y, (uint)Math.Max(rect.Width, 1), (uint)Math.Max(rect.Height, 1));
		_gl.Enable(EnableCap.ScissorTest);
		_gl.Scissor(rect.X, rect.Y, (uint)Math.Max(rect.Width, 0), (uint)Math.Max(rect.Height, 0));
		_gl.DepthMask(true);
		_gl.StencilMask(0xff);
		_gl.ClearStencil(0);
		_gl.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
		return rect;
	}

	/// <summary>
	/// Binds the world layer, its depth and stencil as the last pass left them, with the scissor on the one pixel at the
	/// middle of the cell (<paramref name="x"/>, <paramref name="y"/>)'s <see cref="Scale"/> × <see cref="Scale"/> block,
	/// so the pass's geometry drawn again reaches that pixel alone. Returns the pixel, from the layer's bottom-left.
	/// <see cref="EndWorld"/> puts the window back.
	/// </summary>
	public (int X, int Y) BeginWorldPixel(int x, int y) {
		var pixel = (X: x * Scale + Scale / 2, Y: (Rows - 1 - y) * Scale + Scale / 2);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.Enable(EnableCap.ScissorTest);
		_gl.Scissor(pixel.X, pixel.Y, 1, 1);
		return pixel;
	}

	/// <summary>
	/// Draws the rect of <paramref name="width"/> × <paramref name="height"/> cells at (<paramref name="x"/>,
	/// <paramref name="y"/>) into the world layer as <see cref="Present"/> shows a cell holding a palette index, through
	/// the live palette, so the layer under the rect starts as the picture the cells make; a cell holding
	/// <see cref="WorldCell"/> keeps the layer's pixels it has. The cells are read as <see cref="UploadCells"/> last sent
	/// them. With no palette set nothing is drawn. Leaves the world layer bound.
	/// </summary>
	public void PaintCells(int x, int y, int width, int height) {
		if (width <= 0 || height <= 0 || _palette == null) {
			return;
		}

		var rect = (X: x * Scale, Y: (Rows - (y + height)) * Scale, Width: width * Scale, Height: height * Scale);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.Viewport(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height);
		_gl.Enable(EnableCap.ScissorTest);
		_gl.Scissor(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height);
		_gl.Disable(EnableCap.DepthTest);
		_shader.Use();
		_shader.SetSamplerTexture("uCells", _cells, 0);
		_shader.SetSamplerTexture("uPalette", _palette.Handle, 1);
		// The layer is the target: it is not sampled, a world cell discarding its pixel.
		_shader.SetSamplerTexture("uWorld", 0u, 2);
		_shader.SetInt("uStart", y * Width + x);
		_shader.SetInt("uWidth", Width);
		_shader.SetInt("uCellCount", Width * Rows);
		_shader.SetInt("uScale", Scale);
		_shader.SetInt("uWorldHeight", Rows * Scale);
		_shader.SetUInt("uWorldCell", WorldCell);
		_shader.SetInt("uKeepWorld", 1);
		_shader.SetInt("uWindowWidth", width);
		_shader.SetInt("uWindowRows", height);
		_shader.SetInt("uCursorShown", 0);
		_shader.SetInt("uCursorOnly", 0);
		_gl.BindVertexArray(_vertexArray);
		_gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
		_gl.BindVertexArray(0);
		for (int unit = 1; unit >= 0; unit--) {
			_gl.ActiveTexture(TextureUnit.Texture0 + unit);
			_gl.BindTexture(TextureTarget.Texture2D, 0);
		}

		_gl.Enable(EnableCap.DepthTest);
	}

	/// <summary>
	/// After a <see cref="BeginWorld"/> pass into the rect of <paramref name="width"/> × <paramref name="height"/> cells
	/// at (<paramref name="x"/>, <paramref name="y"/>), the cells it drew into, row by row from the rect's top-left: a
	/// cell is drawn into when any of its <see cref="Scale"/> × <see cref="Scale"/> pixels holds a depth nearer than
	/// the far plane's 1, to which <see cref="BeginWorld"/> clears it. So it answers for a pass whose every fragment
	/// writes its depth, as the depth-tested geometry of a 3D pass does, and not for one that paints without it, such as
	/// a backdrop. Read back at once, waiting for the pass to finish. Leaves the world layer bound.
	/// </summary>
	public bool[] ReadWorldCoverage(int x, int y, int width, int height) {
		var covered = new bool[Math.Max(width, 0) * Math.Max(height, 0)];
		if (covered.Length == 0) {
			return covered;
		}

		int k = Scale, pixelsWide = width * k, pixelsHigh = height * k;
		var depths = new float[pixelsWide * pixelsHigh];
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
		unsafe {
			fixed (float* pixels = depths) {
				_gl.ReadPixels(x * k, (Rows - (y + height)) * k, (uint)pixelsWide, (uint)pixelsHigh,
					PixelFormat.DepthComponent, PixelType.Float, pixels);
			}
		}

		// The read starts at the rect's bottom row of pixels; the cells' rows run down from its top.
		for (int row = 0; row < pixelsHigh; row++) {
			int cellRow = height - 1 - row / k;
			for (int column = 0; column < pixelsWide; column++) {
				if (depths[row * pixelsWide + column] < 1f) {
					covered[cellRow * width + column / k] = true;
				}
			}
		}

		return covered;
	}

	/// <summary>
	/// Sets the world layer's pixels under <paramref name="runs"/> aside, each a run of <c>Width</c> cells along row
	/// <c>Y</c> from column <c>X</c>, for <see cref="RestoreWorld"/> to put back once a pass that draws over them has
	/// run; the runs replace any kept before. The scale must not change between the two.
	/// </summary>
	public void KeepWorld(IReadOnlyList<(int X, int Y, int Width)> runs) {
		_kept = runs;
		if (runs.Count == 0) {
			return;
		}

		_keptTop = runs.Min(run => run.Y);
		int k = Scale, width = Width * k, height = (runs.Max(run => run.Y) - _keptTop + 1) * k;
		if (_keptFramebuffer == 0 || _keptSize.Width != width || _keptSize.Height < height) {
			DeleteKept();
			CreateKept(width, height);
		}

		BlitRuns(toKept: true);
	}

	/// <summary>Puts back the pixels <see cref="KeepWorld"/> last set aside, and forgets them.</summary>
	public void RestoreWorld() {
		if (_kept.Count > 0) {
			BlitRuns(toKept: false);
		}

		_kept = [];
	}

	/// <summary>Each kept run blitted between its place in the world layer and its row of the kept pixels, the scissor off.</summary>
	private void BlitRuns(bool toKept) {
		int k = Scale;
		_gl.Disable(EnableCap.ScissorTest);
		_gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, toKept ? _framebuffer : _keptFramebuffer);
		_gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, toKept ? _keptFramebuffer : _framebuffer);
		foreach (var (x, y, width) in _kept) {
			int layerBottom = (Rows - 1 - y) * k, keptBottom = (y - _keptTop) * k;
			int fromBottom = toKept ? layerBottom : keptBottom, toBottom = toKept ? keptBottom : layerBottom;
			_gl.BlitFramebuffer(x * k, fromBottom, (x + width) * k, fromBottom + k, x * k, toBottom, (x + width) * k,
				toBottom + k, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
		}

		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
	}

	private void CreateKept(int width, int height) {
		_keptColor = _gl.GenTexture();
		_gl.BindTexture(TextureTarget.Texture2D, _keptColor);
		unsafe {
			_gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba,
				PixelType.UnsignedByte, null);
		}

		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
		_gl.BindTexture(TextureTarget.Texture2D, 0);
		_keptFramebuffer = _gl.GenFramebuffer();
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _keptFramebuffer);
		_gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
			TextureTarget.Texture2D, _keptColor, 0);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		_keptSize = (width, height);
	}

	private void DeleteKept() {
		if (_keptFramebuffer != 0) {
			_gl.DeleteFramebuffer(_keptFramebuffer);
			_gl.DeleteTexture(_keptColor);
			_keptFramebuffer = _keptColor = 0;
		}
	}

	/// <summary>Ends <see cref="BeginWorld"/>'s pass or <see cref="BeginWorldPixel"/>'s binding: the scissor off and the window's framebuffer bound.</summary>
	public void EndWorld() {
		_gl.Disable(EnableCap.ScissorTest);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
	}

	/// <summary>
	/// Copies the world layer's pixels of the rect of <paramref name="width"/> × <paramref name="height"/> cells at
	/// (<paramref name="sourceX"/>, <paramref name="sourceY"/>) to the rect at (<paramref name="targetX"/>,
	/// <paramref name="targetY"/>). The two rects must not overlap, as one GL blit within a framebuffer requires; a copy
	/// whose ends overlap is split by its owner.
	/// </summary>
	public void CopyWorld(int sourceX, int sourceY, int targetX, int targetY, int width, int height) {
		if (width <= 0 || height <= 0) {
			return;
		}

		int k = Scale;
		int sourceBottom = (Rows - (sourceY + height)) * k;
		int targetBottom = (Rows - (targetY + height)) * k;
		_gl.Disable(EnableCap.ScissorTest);
		_gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
		_gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _framebuffer);
		_gl.BlitFramebuffer(sourceX * k, sourceBottom, (sourceX + width) * k, sourceBottom + height * k,
			targetX * k, targetBottom, (targetX + width) * k, targetBottom + height * k,
			ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
	}

	/// <summary>
	/// Copies the world pixels of <paramref name="source"/>'s rect of <paramref name="width"/> × <paramref name="height"/>
	/// cells at (<paramref name="sourceX"/>, <paramref name="sourceY"/>) into this surface's rect at
	/// (<paramref name="targetX"/>, <paramref name="targetY"/>), each at its own surface's scale.
	/// </summary>
	public void CopyWorldFrom(IndexedWorldSurface source, int sourceX, int sourceY, int targetX, int targetY, int width, int height) {
		if (width <= 0 || height <= 0) {
			return;
		}

		int k = source.Scale, sourceBottom = (source.Rows - (sourceY + height)) * k;
		int t = Scale, targetBottom = (Rows - (targetY + height)) * t;
		_gl.Disable(EnableCap.ScissorTest);
		_gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, source._framebuffer);
		_gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _framebuffer);
		_gl.BlitFramebuffer(sourceX * k, sourceBottom, (sourceX + width) * k, sourceBottom + height * k,
			targetX * t, targetBottom, (targetX + width) * t, targetBottom + height * t,
			ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
	}

	/// <summary>
	/// Shows the window of <paramref name="windowWidth"/> × <paramref name="windowRows"/> cells read from the linear
	/// address <paramref name="start"/>, each row <see cref="Width"/> cells on from the last and every address taken
	/// modulo <paramref name="cellCount"/>, at <paramref name="fit"/>'s placement in the window over black. Leaves the
	/// window's framebuffer bound with its whole viewport and depth testing on. With <paramref name="cursorAt"/>, the
	/// <see cref="SetCursor"/> sprite's top-left lies at that cell of the window, over the cells.
	/// </summary>
	public void Present(FrameFit fit, int start, int windowWidth, int windowRows, int cellCount, (int X, int Y)? cursorAt = null) {
		Clear(fit.Window);
		Draw(fit.Window, fit.Placement, start, windowWidth, windowRows, cellCount, cursorAt);
	}

	/// <summary>The window's framebuffer of <paramref name="window"/> pixels cleared to black, as <see cref="Present"/> clears it before it draws.</summary>
	public void Clear(Vector2D<int> window) {
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		_gl.Disable(EnableCap.ScissorTest);
		_gl.Viewport(0, 0, (uint)Math.Max(window.X, 1), (uint)Math.Max(window.Y, 1));
		_gl.ColorMask(true, true, true, true);
		_gl.ClearColor(0f, 0f, 0f, 1f);
		_gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
	}

	/// <summary>
	/// <see cref="Present"/>'s draw without its clear, at <paramref name="placement"/> (left, top, width, height from
	/// the top-left of a window of <paramref name="window"/> pixels), which may reach past the window's edges, over
	/// whatever the window already shows: so several windows onto stores can be laid side by side or stretched, a window
	/// one cell wide repeating its cells across the placement's width.
	/// </summary>
	public void Draw(Vector2D<int> window, (int Left, int Top, int Width, int Height) placement, int start,
			int windowWidth, int windowRows, int cellCount, (int X, int Y)? cursorAt = null) =>
		DrawWindow(window, placement, start, windowWidth, windowRows, cellCount, cursorAt, false);

	/// <summary>
	/// The <see cref="SetCursor"/> sprite alone, its top-left at cell <paramref name="cursorAt"/> of a window of
	/// <paramref name="windowWidth"/> × <paramref name="windowRows"/> cells at <paramref name="placement"/>, as
	/// <see cref="Draw"/> would draw it there, over whatever the window already shows.
	/// </summary>
	public void DrawCursor(Vector2D<int> window, (int Left, int Top, int Width, int Height) placement, int windowWidth,
			int windowRows, (int X, int Y) cursorAt) =>
		DrawWindow(window, placement, 0, windowWidth, windowRows, 1, cursorAt, true);

	private void DrawWindow(Vector2D<int> window, (int Left, int Top, int Width, int Height) placement, int start,
			int windowWidth, int windowRows, int cellCount, (int X, int Y)? cursorAt, bool cursorOnly) {
		if (cursorOnly && _cursorWidth <= 0) {
			return;
		}

		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		_gl.Disable(EnableCap.ScissorTest);
		var (left, top, width, height) = placement;
		if (width <= 0 || height <= 0 || windowWidth <= 0 || windowRows <= 0 || _palette == null) {
			return;
		}

		// GL's window origin is the bottom-left corner.
		_gl.Viewport(left, window.Y - top - height, (uint)width, (uint)height);
		_gl.Disable(EnableCap.DepthTest);
		_shader.Use();
		_shader.SetSamplerTexture("uCells", _cells, 0);
		_shader.SetSamplerTexture("uPalette", _palette.Handle, 1);
		_shader.SetSamplerTexture("uWorld", _worldColor, 2);
		_shader.SetInt("uStart", start);
		_shader.SetInt("uWidth", Width);
		_shader.SetInt("uCellCount", Math.Max(cellCount, 1));
		_shader.SetInt("uScale", Scale);
		_shader.SetInt("uWorldHeight", Rows * Scale);
		_shader.SetUInt("uWorldCell", WorldCell);
		_shader.SetInt("uKeepWorld", 0);
		_shader.SetInt("uWindowWidth", windowWidth);
		_shader.SetInt("uWindowRows", windowRows);
		bool cursor = cursorAt != null && _cursorWidth > 0;
		_shader.SetInt("uCursorShown", cursor ? 1 : 0);
		_shader.SetInt("uCursorOnly", cursorOnly ? 1 : 0);
		if (cursor) {
			_shader.SetSamplerTexture("uCursor", _cursor, 3);
			_shader.SetInt("uCursorX", cursorAt!.Value.X);
			_shader.SetInt("uCursorY", cursorAt.Value.Y);
			_shader.SetInt("uCursorWidth", _cursorWidth);
			_shader.SetInt("uCursorRows", _cursorRows);
			_shader.SetInt("uCursorOpaque", _cursorOpaque ? 1 : 0);
		}
		_gl.BindVertexArray(_vertexArray);
		_gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
		_gl.BindVertexArray(0);

		// Nothing is left bound for a later pass to sample, the world layer least of all, which that pass may draw into.
		for (int unit = 3; unit >= 0; unit--) {
			_gl.ActiveTexture(TextureUnit.Texture0 + unit);
			_gl.BindTexture(TextureTarget.Texture2D, 0);
		}

		_gl.Enable(EnableCap.DepthTest);
		_gl.Viewport(0, 0, (uint)Math.Max(window.X, 1), (uint)Math.Max(window.Y, 1));
	}

	private void CreateWorld(int scale) {
		Scale = scale;
		uint width = (uint)(Width * scale), height = (uint)(Rows * scale);

		_worldColor = _gl.GenTexture();
		_gl.BindTexture(TextureTarget.Texture2D, _worldColor);
		unsafe {
			_gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba,
				PixelType.UnsignedByte, null);
		}

		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
		_gl.BindTexture(TextureTarget.Texture2D, 0);

		_worldDepthStencil = _gl.GenRenderbuffer();
		_gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _worldDepthStencil);
		_gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, width, height);
		_gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

		_framebuffer = _gl.GenFramebuffer();
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
			TextureTarget.Texture2D, _worldColor, 0);
		_gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
			RenderbufferTarget.Renderbuffer, _worldDepthStencil);
		var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
		if (status != GLEnum.FramebufferComplete) {
			_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			throw new InvalidOperationException($"The {width}x{height} world layer is incomplete: {status}.");
		}

		_gl.Disable(EnableCap.ScissorTest);
		_gl.ClearColor(0f, 0f, 0f, 1f);
		_gl.Clear(ClearBufferMask.ColorBufferBit);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
	}

	public void Dispose() {
		_shader.Dispose();
		_palette?.Dispose();
		_gl.DeleteVertexArray(_vertexArray);
		_gl.DeleteTexture(_cells);
		_gl.DeleteFramebuffer(_framebuffer);
		_gl.DeleteTexture(_worldColor);
		_gl.DeleteRenderbuffer(_worldDepthStencil);
		DeleteKept();
		if (_cursor != 0) {
			_gl.DeleteTexture(_cursor);
		}
	}
}
