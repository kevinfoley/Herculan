using Herculan.Engine.Gl;
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

	/// <summary>Ends <see cref="BeginWorld"/>'s pass: the scissor off and the window's framebuffer bound.</summary>
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
	/// Shows the window of <paramref name="windowWidth"/> × <paramref name="windowRows"/> cells read from the linear
	/// address <paramref name="start"/>, each row <see cref="Width"/> cells on from the last and every address taken
	/// modulo <paramref name="cellCount"/>, at <paramref name="fit"/>'s placement in the window over black. Leaves the
	/// window's framebuffer bound with its whole viewport and depth testing on. With <paramref name="cursorAt"/>, the
	/// <see cref="SetCursor"/> sprite's top-left lies at that cell of the window, over the cells.
	/// </summary>
	public void Present(FrameFit fit, int start, int windowWidth, int windowRows, int cellCount, (int X, int Y)? cursorAt = null) {
		var window = fit.Window;
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		_gl.Disable(EnableCap.ScissorTest);
		_gl.Viewport(0, 0, (uint)Math.Max(window.X, 1), (uint)Math.Max(window.Y, 1));
		_gl.ColorMask(true, true, true, true);
		_gl.ClearColor(0f, 0f, 0f, 1f);
		_gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

		var (left, top, width, height) = fit.Placement;
		if (width <= 0 || height <= 0 || _palette == null) {
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
		_shader.SetInt("uWindowWidth", windowWidth);
		_shader.SetInt("uWindowRows", windowRows);
		bool cursor = cursorAt != null && _cursorWidth > 0;
		_shader.SetInt("uCursorShown", cursor ? 1 : 0);
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
		if (_cursor != 0) {
			_gl.DeleteTexture(_cursor);
		}
	}
}
