using System.Numerics;
using Herculan.Engine.Gl;
using Herculan.Engine.Terrain;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// The terrain's paint rank at every pixel of a view: which cell's ground is showing there, as that
/// cell's place in the original's cell walk (<see cref="TerrainPaintOrder"/>). A ground shape is
/// painted in the original straight after its own cell's ground, so every later cell's ground
/// covers it and every earlier cell's is covered by it; with this target, the shape's fragment
/// shader keeps a pixel exactly when the ground there ranks no later than the shape's cell. See
/// <see cref="SceneRenderer"/>, which draws with it, and docs/retail/simulation/ground-shapes.md, "The draw
/// pass".
///
/// <para>This is how this engine reproduces a painter's order it does not otherwise have: the
/// scene is depth-buffered, the original is painted a cell at a time. The terrain mesh is drawn
/// once more per pass, into an unsigned-integer colour target with a depth buffer of its own, so
/// the rank recorded at a pixel is that of the nearest ground, the ground the scene itself shows
/// there. Clearing to <see cref="TerrainPaintOrder.NoTerrain"/> leaves the sky below every cell,
/// so a shape draws over it as in the original.</para>
///
/// <para>The per-cell ranks are a texture the size of the grid, re-uploaded only when the walk
/// changes — when the viewer crosses into another cell or turns between walking by row and by
/// column.</para>
/// </summary>
public sealed class TerrainPaintRankBuffer : IDisposable {
	private readonly GL _gl;
	private readonly ShaderProgram _program;
	private uint _framebuffer;
	private uint _rankTexture;
	private uint _depthBuffer;
	private int _width;
	private int _height;

	private uint _cellRankTexture;
	private HeightGrid? _rankedGrid;
	private TerrainPaintOrder _rankedOrder;
	private uint[] _cellRanks = Array.Empty<uint>();

	public TerrainPaintRankBuffer(GL gl) {
		_gl = gl;
		_program = ShaderProgram.Load(gl, "TerrainPaintRank.glsl");
	}

	/// <summary>The rank target's texture, to sample with <c>texelFetch</c> at the fragment's window position.</summary>
	public uint RankTexture => _rankTexture;

	/// <summary>
	/// Draws <paramref name="terrain"/>'s ranks under <paramref name="order"/> into the target, over
	/// the viewport the scene pass is about to use and under whatever scissor the caller has set.
	/// Leaves the caller's framebuffer bound again, and the viewport as given.
	/// </summary>
	public void Draw(SceneItem terrain, HeightGrid grid, TerrainPaintOrder order,
			Matrix4x4 view, Matrix4x4 projection,
			int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		UploadCellRanks(grid, order);
		EnsureTarget(Math.Max(viewportX + viewportWidth, 1), Math.Max(viewportY + viewportHeight, 1));

		_gl.GetInteger(GetPName.DrawFramebufferBinding, out int previousFramebuffer);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.Viewport(viewportX, viewportY, (uint)Math.Max(viewportWidth, 1), (uint)Math.Max(viewportHeight, 1));

		_gl.Enable(EnableCap.DepthTest);
		_gl.DepthMask(true);
		uint noTerrain = TerrainPaintOrder.NoTerrain;
		float farDepth = 1f;
		_gl.ClearBuffer(BufferKind.Color, 0, in noTerrain);
		_gl.ClearBuffer(BufferKind.Depth, 0, in farDepth);

		_program.Use();
		_program.SetMatrix("uModel", terrain.Transform);
		_program.SetMatrix("uView", view);
		_program.SetMatrix("uProjection", projection);
		_program.SetFloat("uCellSize", grid.CellSize / WorldScale.WorldUnitsPerMeter);
		_program.SetSamplerTexture("uCellRanks", _cellRankTexture, 0);
		terrain.Mesh.Draw();

		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
	}

	private void UploadCellRanks(HeightGrid grid, TerrainPaintOrder order) {
		bool resized = !ReferenceEquals(grid, _rankedGrid);
		if (!resized && order.Equals(_rankedOrder)) {
			return;
		}

		int width = grid.Width;
		int height = grid.Height;
		if (_cellRanks.Length != width * height) {
			_cellRanks = new uint[width * height];
		}

		for (int cellY = 0; cellY < height; cellY++) {
			for (int cellX = 0; cellX < width; cellX++) {
				_cellRanks[cellX + cellY * width] = order.Rank(cellX, cellY);
			}
		}

		if (_cellRankTexture == 0) {
			_cellRankTexture = _gl.GenTexture();
		}

		_gl.BindTexture(TextureTarget.Texture2D, _cellRankTexture);
		unsafe {
			fixed (uint* ranks = _cellRanks) {
				_gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R32ui, (uint)width, (uint)height, 0,
					PixelFormat.RedInteger, PixelType.UnsignedInt, ranks);
			}
		}

		SetNearestClamp();
		_gl.BindTexture(TextureTarget.Texture2D, 0);

		_rankedGrid = grid;
		_rankedOrder = order;
	}

	/// <summary>
	/// Grows the target to cover every pixel of a viewport reaching <paramref name="width"/> across
	/// and <paramref name="height"/> up from the framebuffer's origin. Never shrinks: a cockpit pass
	/// and a full-window pass in the same frame then share one allocation.
	/// </summary>
	private void EnsureTarget(int width, int height) {
		if (_framebuffer != 0 && width <= _width && height <= _height) {
			return;
		}

		_width = Math.Max(width, _width);
		_height = Math.Max(height, _height);
		DeleteTarget();

		_rankTexture = _gl.GenTexture();
		_gl.BindTexture(TextureTarget.Texture2D, _rankTexture);
		unsafe {
			_gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R32ui, (uint)_width, (uint)_height, 0,
				PixelFormat.RedInteger, PixelType.UnsignedInt, null);
		}

		SetNearestClamp();
		_gl.BindTexture(TextureTarget.Texture2D, 0);

		_depthBuffer = _gl.GenRenderbuffer();
		_gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
		_gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
			(uint)_width, (uint)_height);
		_gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

		_gl.GetInteger(GetPName.DrawFramebufferBinding, out int previousFramebuffer);
		_framebuffer = _gl.GenFramebuffer();
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
		_gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
			TextureTarget.Texture2D, _rankTexture, 0);
		_gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
			RenderbufferTarget.Renderbuffer, _depthBuffer);

		var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
		_gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
		if (status != GLEnum.FramebufferComplete) {
			throw new InvalidOperationException($"The terrain paint-rank target is incomplete: {status}.");
		}
	}

	private void SetNearestClamp() {
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
		_gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
	}

	private void DeleteTarget() {
		if (_framebuffer != 0) {
			_gl.DeleteFramebuffer(_framebuffer);
			_gl.DeleteTexture(_rankTexture);
			_gl.DeleteRenderbuffer(_depthBuffer);
			_framebuffer = _rankTexture = _depthBuffer = 0;
		}
	}

	public void Dispose() {
		DeleteTarget();
		if (_cellRankTexture != 0) {
			_gl.DeleteTexture(_cellRankTexture);
		}

		_program.Dispose();
	}
}
