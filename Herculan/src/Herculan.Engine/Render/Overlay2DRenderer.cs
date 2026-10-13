using System.Numerics;
using Herculan.Engine.Gl;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// The 2D overlay's drawing plumbing: one vertex batch of textured and flat-coloured triangles in
/// viewport pixels, drawn against one texture at a time. The cockpit painters in
/// <c>Render.Cockpit</c> fill it, and none of it is specific to one game.
///
/// <para>Orthographic, own minimal shader (position/UV/color, no lighting) — same precedent as
/// <see cref="WireframeRenderer"/> using its own shader rather than forcing 2D content through
/// <see cref="SceneRenderer"/>'s lit-3D layout. Disables depth test and enables alpha blending for
/// its own draw, then restores both — the 3D pass (<see cref="SceneRenderer"/>) assumes depth test is
/// always on, and nothing else in the engine uses blending.</para>
/// </summary>
public sealed class Overlay2DRenderer : IDisposable {
	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly GpuOverlayMesh _mesh;
	private readonly List<Overlay2DVertex> _vertices = new();

	public Overlay2DRenderer(GL gl) {
		_gl = gl;
		_shader = ShaderProgram.Load(gl, "Overlay2D.glsl");
		_mesh = new GpuOverlayMesh(gl);
	}

	/// <summary>
	/// Sets the viewport to the given sub-rect, turns depth testing off and alpha blending on, and binds
	/// the program with that viewport's size — the state every overlay draw runs under. Positions are
	/// pixels from the viewport's top-left.
	/// </summary>
	public void Begin(int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		_gl.Viewport(viewportX, viewportY, (uint)Math.Max(viewportWidth, 1), (uint)Math.Max(viewportHeight, 1));
		_gl.Disable(EnableCap.DepthTest);
		_gl.Enable(EnableCap.Blend);
		_gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

		_shader.Use();
		_shader.SetVector2("uViewportSize", new Vector2(viewportWidth, viewportHeight));
	}

	/// <summary>Puts back the state <see cref="Begin"/> changed: blending off, depth testing on.</summary>
	public void End() {
		_gl.Disable(EnableCap.Blend);
		_gl.Enable(EnableCap.DepthTest);
	}

	/// <summary>
	/// Draws the batched vertices against <paramref name="texture"/>. The batch is not cleared, so a
	/// caller that goes on adding calls <see cref="Clear"/> first.
	/// </summary>
	public void Submit(GpuTexture texture) {
		_shader.SetSamplerTexture("uTexture", texture.Handle, 0);
		_mesh.SubmitAndDraw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices));
	}

	/// <summary>Empties the batch.</summary>
	public void Clear() => _vertices.Clear();

	/// <summary>How many vertices the batch holds.</summary>
	public int VertexCount => _vertices.Count;

	/// <summary>
	/// Clips every following draw to a framebuffer rect — pixels from the bottom-left, the frame the
	/// viewport is set in — until <see cref="ClearScissor"/>.
	/// </summary>
	public void SetScissor(int x, int y, int width, int height) {
		_gl.Enable(EnableCap.ScissorTest);
		_gl.Scissor(x, y, (uint)width, (uint)height);
	}

	/// <summary>Turns <see cref="SetScissor"/>'s clip off.</summary>
	public void ClearScissor() => _gl.Disable(EnableCap.ScissorTest);

	public void AddTexturedQuad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1) {
		var a = new Overlay2DVertex(new Vector2(x0, y0), new Vector2(u0, v0));
		var b = new Overlay2DVertex(new Vector2(x1, y0), new Vector2(u1, v0));
		var c = new Overlay2DVertex(new Vector2(x1, y1), new Vector2(u1, v1));
		var d = new Overlay2DVertex(new Vector2(x0, y1), new Vector2(u0, v1));
		_vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
		_vertices.Add(a); _vertices.Add(c); _vertices.Add(d);
	}

	/// <summary>
	/// The same quad with its four corners given explicitly, for art that is rotated rather than
	/// axis-aligned. Corners run top-left, top-right, bottom-right, bottom-left in the <i>source</i>
	/// bitmap's own order, so the UVs pair with them regardless of where the rotation puts them.
	/// </summary>
	public void AddTexturedQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d,
			float u0, float v0, float u1, float v1) {
		var va = new Overlay2DVertex(a, new Vector2(u0, v0));
		var vb = new Overlay2DVertex(b, new Vector2(u1, v0));
		var vc = new Overlay2DVertex(c, new Vector2(u1, v1));
		var vd = new Overlay2DVertex(d, new Vector2(u0, v1));
		_vertices.Add(va); _vertices.Add(vb); _vertices.Add(vc);
		_vertices.Add(va); _vertices.Add(vc); _vertices.Add(vd);
	}

	public void AddFilledRect(float x0, float y0, float x1, float y1, Vector3 color) {
		var a = new Overlay2DVertex(new Vector2(x0, y0), color);
		var b = new Overlay2DVertex(new Vector2(x1, y0), color);
		var c = new Overlay2DVertex(new Vector2(x1, y1), color);
		var d = new Overlay2DVertex(new Vector2(x0, y1), color);
		_vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
		_vertices.Add(a); _vertices.Add(c); _vertices.Add(d);
	}

	/// <summary>One flat-coloured triangle — the only thing on the HUD that is neither a sprite nor a rect.</summary>
	public void AddFilledTriangle(Vector2 a, Vector2 b, Vector2 c, Vector3 color) {
		_vertices.Add(new Overlay2DVertex(a, color));
		_vertices.Add(new Overlay2DVertex(b, color));
		_vertices.Add(new Overlay2DVertex(c, color));
	}

	/// <summary>
	/// A one-device-pixel frame round a rect, drawn as four filled edges — the fill brush's style 4,
	/// which <c>Raster_FillRect</c> (<c>004865f8</c>) implements as four line draws round the rect it is handed.
	/// </summary>
	public void AddRectOutline(float x0, float y0, float x1, float y1, float scale, Vector3 color) {
		float thickness = Math.Max(scale, 1f);
		AddFilledRect(x0, y0, x1, y0 + thickness, color);
		AddFilledRect(x0, y1 - thickness, x1, y1, color);
		AddFilledRect(x0, y0, x0 + thickness, y1, color);
		AddFilledRect(x1 - thickness, y0, x1, y1, color);
	}

	/// <summary>
	/// A line between two arbitrary points, as a quad along its own normal: one framebuffer pixel thick, or
	/// <paramref name="thickness"/> pixels, at least one.
	/// </summary>
	public void AddLine(float x0, float y0, float x1, float y1, Vector3 color, float thickness = 1f) {
		float dx = x1 - x0;
		float dy = y1 - y0;
		float length = MathF.Sqrt(dx * dx + dy * dy);
		if (length < 0.5f) {
			return;
		}

		float half = Math.Max(thickness, 1f) * 0.5f;
		float nx = -dy / length * half;
		float ny = dx / length * half;
		_vertices.Add(new Overlay2DVertex(new Vector2(x0 + nx, y0 + ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x1 + nx, y1 + ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x1 - nx, y1 - ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x0 + nx, y0 + ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x1 - nx, y1 - ny), color));
		_vertices.Add(new Overlay2DVertex(new Vector2(x0 - nx, y0 - ny), color));
	}

	public void Dispose() {
		_shader.Dispose();
		_mesh.Dispose();
	}
}
