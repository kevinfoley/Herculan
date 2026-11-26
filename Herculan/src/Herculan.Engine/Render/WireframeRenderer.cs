using System.Numerics;
using Herculan.Engine.Gl;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// Draws flat-coloured line geometry — currently just the editor's selection box — with its own
/// minimal shader rather than reusing <see cref="SceneRenderer"/>'s, which expects a normal/UV
/// vertex layout this has no use for.
/// </summary>
public sealed class WireframeRenderer : IDisposable {
	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly GpuLineMesh _unitCube;
	private readonly GpuLineMesh _scratch;

	public WireframeRenderer(GL gl) {
		_gl = gl;
		_shader = ShaderProgram.Load(gl, "Wireframe.glsl");
		_unitCube = GpuLineMesh.CreateUnitCube(gl);
		_scratch = new GpuLineMesh(gl, ReadOnlySpan<Vector3>.Empty, dynamic: true);
	}

	/// <summary>
	/// Draws a cube centered at a point with the given half-extent, both in
	/// render units.
	/// </summary>
	/// <param name="camera">The camera.</param>
	/// <param name="centerRender">The cube's center in render units.</param>
	/// <param name="halfExtentRender">The cube's half-extent in render units.</param>
	/// <param name="color">The cube's color.</param>
	/// <param name="aspectRatio">The viewport's width over its height.</param>
	public void DrawBox(Camera camera, Vector3 centerRender, float halfExtentRender, Vector3 color,
			float aspectRatio) {
		_shader.Use();
		_shader.SetMatrix("uModel",
			Matrix4x4.CreateScale(halfExtentRender) * Matrix4x4.CreateTranslation(centerRender));
		_shader.SetMatrix("uView", camera.ViewMatrix);
		_shader.SetMatrix("uProjection", camera.ProjectionMatrix(aspectRatio));
		_shader.SetVector3("uColor", color);

		_unitCube.Draw();
	}

	/// <summary>
	/// Draws a line list already in world (render) space — the segments are read two
	/// vertices at a time, so an odd trailing vertex is ignored. Rebuilt geometry goes through one
	/// shared buffer, so a caller drawing several colours issues several calls rather than holding
	/// several meshes.
	/// </summary>
	/// <param name="camera">The camera.</param>
	/// <param name="segments">The vertices, two to a line.</param>
	/// <param name="color">The lines' color.</param>
	/// <param name="aspectRatio">The viewport's width over its height.</param>
	/// <param name="throughGeometry">
	/// Draw with depth testing off, so the lines show through solid geometry. On by default because
	/// a skeleton sits inside the model it belongs to and would otherwise be invisible from outside
	/// it. <see cref="SceneRenderer.Render"/> turns depth testing back on for the next thing drawn.
	/// </param>
	public void DrawLines(Camera camera, ReadOnlySpan<Vector3> segments, Vector3 color,
			float aspectRatio, bool throughGeometry = true) {
		if (segments.Length < 2) {
			return;
		}

		if (throughGeometry) {
			_gl.Disable(EnableCap.DepthTest);
		}

		_scratch.SetVertices(segments);

		_shader.Use();
		_shader.SetMatrix("uModel", Matrix4x4.Identity);
		_shader.SetMatrix("uView", camera.ViewMatrix);
		_shader.SetMatrix("uProjection", camera.ProjectionMatrix(aspectRatio));
		_shader.SetVector3("uColor", color);

		_scratch.Draw();

		if (throughGeometry) {
			_gl.Enable(EnableCap.DepthTest);
		}
	}

	public void Dispose() {
		_shader.Dispose();
		_unitCube.Dispose();
		_scratch.Dispose();
	}
}
