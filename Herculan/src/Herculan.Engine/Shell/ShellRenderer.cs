using System.Numerics;
using System.Runtime.InteropServices;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Shell;

/// <summary>
/// Draws a shell screen: the tiled backdrop, then every widget's sprite and caption, all placed
/// through <see cref="ShellScreenLayout"/> so what is drawn and what is clicked agree.
///
/// <para>It shares <see cref="Render.Overlay2DRenderer"/>'s shader and vertex format rather than
/// having its own — both draw textured and flat-coloured quads in pixel space with the origin at the
/// top left, which is the whole of what either needs. Like it, this disables depth test and enables
/// alpha blending for its own draw and restores both, since the 3D pass assumes depth test is always
/// on.</para>
///
/// <para>Two texture binds per frame at most: the backdrop is a single <c>.DBM</c> of its own, and
/// everything else — button plates, icons and every glyph of every caption — is packed into the one
/// atlas <see cref="ShellArt.Sprites"/> holds.</para>
/// </summary>
public sealed class ShellRenderer : IDisposable {
	private readonly GL _gl;
	private readonly ShellArt _art;
	private readonly ShaderProgram _shader;
	private readonly GpuOverlayMesh _mesh;
	private readonly GpuTexture _backdrop;
	private readonly GpuTexture? _sprites;
	private readonly List<Overlay2DVertex> _vertices = new();
	private GpuTexture? _content;
	private int _contentWidth;
	private int _contentHeight;
	private GpuTexture? _backdropOverride;
	private ShellImage? _backdropOverrideImage;

	public ShellRenderer(GL gl, ShellArt art) {
		_gl = gl;
		_art = art;
		_shader = ShaderProgram.Load(gl, "Overlay2D.glsl");
		_mesh = new GpuOverlayMesh(gl);
		_backdrop = new GpuTexture(gl, art.Backdrop.Pixels, art.Backdrop.Width, art.Backdrop.Height);
		_sprites = art.Sprites is { } sheet ? sheet.Atlas.Upload(gl) : null;
	}

	/// <summary>
	/// Hands over the tab content to draw between the backdrop and the strip, or null to draw none.
	/// The surface is resolved through the art's palette and uploaded here, so this is the expensive
	/// call and belongs on a state change rather than in the frame loop — which is also how the
	/// original works, repainting a widget only when something it shows moves.
	///
	/// <para>Whatever the surface left as index 0 comes out fully transparent, so the backdrop shows
	/// through it. A tab screen covers only part of the canvas and relies on that.</para>
	/// </summary>
	public void SetContent(ShellSurface? surface) {
		_content?.Dispose();
		_content = null;

		if (surface == null || surface.Width <= 0 || surface.Height <= 0) {
			return;
		}

		var image = surface.ToImage(_art.Palette);
		_contentWidth = image.Width;
		_contentHeight = image.Height;
		_content = new GpuTexture(_gl, image.Pixels, image.Width, image.Height);
	}

	/// <summary>
	/// Draws <paramref name="image"/> in the backdrop's place, or the backdrop again for null — what the
	/// startup sequence's full-window widget shows while nothing else is up
	/// (<see cref="ShellStartupSequence"/>).
	/// </summary>
	public void SetBackdropOverride(ShellImage? image) {
		if (ReferenceEquals(image, _backdropOverrideImage)) {
			return;
		}

		_backdropOverride?.Dispose();
		_backdropOverride = image == null ? null : new GpuTexture(_gl, image.Pixels, image.Width, image.Height);
		_backdropOverrideImage = image;
	}

	/// <summary>
	/// Draws <paramref name="screen"/> into the whole window. The caller clears first: the shell fills
	/// the canvas but not the letterbox either side of it, and what shows there is the host's call.
	/// </summary>
	public void Draw(ShellScreenLayout layout, ShellScreen screen) {
		if (layout.Scale <= 0f) {
			return;
		}

		_gl.Viewport(0, 0, (uint)Math.Max(layout.WindowWidth, 1), (uint)Math.Max(layout.WindowHeight, 1));
		_gl.Disable(EnableCap.DepthTest);
		_gl.Enable(EnableCap.Blend);
		_gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

		_shader.Use();
		_shader.SetVector2("uViewportSize", new Vector2(layout.WindowWidth, layout.WindowHeight));

		_vertices.Clear();
		AddBackdrop(layout);
		_shader.SetSamplerTexture("uTexture", (_backdropOverride ?? _backdrop).Handle, 0);
		_mesh.SubmitAndDraw(CollectionsMarshal.AsSpan(_vertices));

		// The tab's content, over the backdrop and under the strip. It is one quad at canvas scale
		// whatever is on it, because a whole screen's chrome was rasterized into it — see ShellSurface.
		if (_content != null) {
			_vertices.Clear();
			AddQuad(layout, 0f, 0f, _contentWidth, _contentHeight, new AtlasRect(0f, 0f, 1f, 1f));
			_shader.SetSamplerTexture("uTexture", _content.Handle, 0);
			_mesh.SubmitAndDraw(CollectionsMarshal.AsSpan(_vertices));
		}

		if (screen.StripVisible && _art.Sprites is { } sheet && _sprites != null) {
			_vertices.Clear();
			foreach (var button in screen.Buttons) {
				AddButton(layout, sheet, button);
			}

			if (_vertices.Count > 0) {
				_shader.SetSamplerTexture("uTexture", _sprites.Handle, 0);
				_mesh.SubmitAndDraw(CollectionsMarshal.AsSpan(_vertices));
			}
		}

		_gl.Disable(EnableCap.Blend);
		_gl.Enable(EnableCap.DepthTest);
	}

	/// <summary>
	/// Draws <paramref name="texture"/> stretched over a canvas rect, on top of whatever
	/// <see cref="Draw"/> put there — a movie in its window, or a picture over the whole canvas. Its top
	/// row is its first, as a <c>VideoFrame</c>'s and a <see cref="ShellImage"/>'s are.
	/// </summary>
	public void DrawTexture(ShellScreenLayout layout, GpuTexture texture, int x, int y, int width, int height) {
		if (layout.Scale <= 0f) {
			return;
		}

		_gl.Viewport(0, 0, (uint)Math.Max(layout.WindowWidth, 1), (uint)Math.Max(layout.WindowHeight, 1));
		_gl.Disable(EnableCap.DepthTest);
		_shader.Use();
		_shader.SetVector2("uViewportSize", new Vector2(layout.WindowWidth, layout.WindowHeight));

		_vertices.Clear();
		AddQuad(layout, x, y, x + width, y + height, new AtlasRect(0f, 0f, 1f, 1f));
		_shader.SetSamplerTexture("uTexture", texture.Handle, 0);
		_mesh.SubmitAndDraw(CollectionsMarshal.AsSpan(_vertices));
		_gl.Enable(EnableCap.DepthTest);
	}

	/// <summary>
	/// The backdrop, tiled across the canvas. Retail's is exactly canvas-sized and so comes out of this
	/// loop as the single quad it should be; the tiling is here because the root widget's texture is a
	/// parameter of the screen rather than a fixed asset, and a smaller one has to cover the canvas
	/// somehow.
	/// </summary>
	private void AddBackdrop(ShellScreenLayout layout) {
		var image = _backdropOverrideImage ?? _art.Backdrop;
		for (int y = 0; y < ShellLayout.CanvasHeight; y += image.Height) {
			for (int x = 0; x < ShellLayout.CanvasWidth; x += image.Width) {
				// The last tile on each axis is cut short rather than overhanging, so the canvas edge is
				// the image's edge and not a clipped-off remainder.
				int width = Math.Min(image.Width, ShellLayout.CanvasWidth - x);
				int height = Math.Min(image.Height, ShellLayout.CanvasHeight - y);
				AddQuad(layout, x, y, x + width, y + height,
					new AtlasRect(0f, 0f, width / (float)image.Width, height / (float)image.Height));
			}
		}
	}

	/// <summary>One button: its current face, then its caption centred on that face's rect.</summary>
	private void AddButton(ShellScreenLayout layout, HudSpriteSheet sheet, ShellButton button) {
		var sprite = button.Sprite();
		if (sheet.Sprite(sprite.Bank, sprite.Frame) is { Width: > 0, Height: > 0 } plate) {
			AddSprite(layout, plate, button.Rect.X0, button.Rect.Y0);
		}

		if (button.Caption is { Length: > 0 } caption && sheet.Font(button.FontName) is { } font) {
			// Horizontally centred, with the font's ink band centred vertically rather than its full
			// cell — the descender rows would otherwise sit the text high. VSHELL centres vertically too
			// but does it by baseline against the font's own height, which needs Font_CellHeight (00453fa8) and the
			// Text widget's +0xb1 identified; this is the same intent through DBSIM's ink-height rule
			// (see HudFont.Place), and is Herculan's arithmetic rather than the original's.
			int textX = button.Rect.X0 + (button.Rect.Width - font.Measure(caption)) / 2;
			int textY = button.Rect.Y0 + (button.Rect.Height - font.InkHeight) / 2;

			// The pressed nudge, which is the original's own and is exact: the paint offsets the caption
			// one pixel above its centred row when the button is idle and one below when it is lit and
			// enabled, so a lit button's text sits two pixels lower than an idle one's.
			AddText(layout, sheet, font, button.FontName, caption, textX,
				textY + (button.CaptionNudged ? 1 : -1));
		}
	}

	/// <summary>One run of glyphs left to right from a canvas-pixel top-left.</summary>
	private void AddText(ShellScreenLayout layout, HudSpriteSheet sheet, HudFont font, string fontName,
			string text, int left, int top) {
		int pen = left;
		foreach (char c in text) {
			if (font.GlyphIndex(c) is { } glyph
				&& sheet.Sprite(fontName, glyph) is { Width: > 0, Height: > 0 } sprite) {
				AddSprite(layout, sprite, pen, top);
			}

			pen += font.Width(c);
		}
	}

	/// <summary>One atlas frame at its own size, anchored to a canvas-pixel top-left.</summary>
	private void AddSprite(ShellScreenLayout layout, HudSprite sprite, float left, float top) {
		AddQuad(layout, left, top,
			left + sprite.Width * sprite.Scale, top + sprite.Height * sprite.Scale, sprite.Rect);
	}

	/// <summary>One textured quad, given canvas-pixel corners — the only place canvas becomes window.</summary>
	private void AddQuad(ShellScreenLayout layout, float x0, float y0, float x1, float y1, AtlasRect uv) {
		var (wx0, wy0) = layout.CanvasToWindow(x0, y0);
		var (wx1, wy1) = layout.CanvasToWindow(x1, y1);

		var a = new Overlay2DVertex(new Vector2(wx0, wy0), new Vector2(uv.U0, uv.V0));
		var b = new Overlay2DVertex(new Vector2(wx1, wy0), new Vector2(uv.U1, uv.V0));
		var c = new Overlay2DVertex(new Vector2(wx1, wy1), new Vector2(uv.U1, uv.V1));
		var d = new Overlay2DVertex(new Vector2(wx0, wy1), new Vector2(uv.U0, uv.V1));

		_vertices.Add(a);
		_vertices.Add(b);
		_vertices.Add(c);
		_vertices.Add(a);
		_vertices.Add(c);
		_vertices.Add(d);
	}

	public void Dispose() {
		_mesh.Dispose();
		_shader.Dispose();
		_backdrop.Dispose();
		_backdropOverride?.Dispose();
		_sprites?.Dispose();
		_content?.Dispose();
	}
}
