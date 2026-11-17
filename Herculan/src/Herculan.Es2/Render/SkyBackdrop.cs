using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Es2.Content;
using Silk.NET.OpenGL;

namespace Herculan.Es2.Render;

/// <summary>
/// ES2's backdrop: the theater's banded sky, painted before any geometry goes into a pass, as
/// <c>Scene_DrawTerrainPass</c> paints the <c>hzline</c> before the terrain. Depth-testing and depth-writing are
/// both off, so this is a background fill rather than something at the far plane — the scene draws straight over it
/// and nothing needs the far plane to sit beyond the sky. The line and the band rule are
/// <see cref="SkyGradient"/>'s; this hands them to Sky.glsl.
/// </summary>
public sealed class SkyBackdrop : ISceneBackdrop, IDisposable {
	private const string ShaderFolder = "Herculan.Es2.Render.Shaders.";

	private static readonly string[] BandNames = Enumerable
		.Range(0, SkyGradient.MaxBands)
		.Select(i => $"uBands[{i}]").ToArray();

	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly uint _vertexArray;

	public SkyBackdrop(GL gl) {
		_gl = gl;
		_shader = ShaderProgram.Load(gl, typeof(SkyBackdrop).Assembly, ShaderFolder, "Sky.glsl");
		_vertexArray = gl.GenVertexArray();
	}

	/// <summary>
	/// The theater's sky, banded out of its own palette. Null paints nothing, which leaves the renderer's flat
	/// <see cref="SceneRenderer.SkyColor"/> the frame is cleared to.
	/// </summary>
	public SkyGradient? Sky { get; set; }

	/// <inheritdoc />
	public void Draw(Camera camera, int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		if (Sky is not { } sky) {
			return;
		}

		float focalPixels = camera.FocalPixelsFor(viewportHeight);
		var centre = camera.PrincipalPointInWindow(viewportX, viewportY, viewportWidth, viewportHeight);
		var line = sky.Place(unchecked((short)camera.Pitch), unchecked((short)camera.Roll), centre, focalPixels);

		_shader.Use();
		for (int band = 0; band < sky.Bands.Length; band++) {
			_shader.SetVector3(BandNames[band], sky.Bands[band]);
		}

		_shader.SetInt("uBandCount", sky.Bands.Length);
		_shader.SetFloat("uBandHeight", sky.BandHeight);
		_shader.SetFloat("uGap", sky.HorizonGap);
		_shader.SetVector2("uLineMid", line.Mid);
		_shader.SetVector2("uLineUp", line.Up);
		_shader.SetFloat("uScale", line.Scale);
		_shader.SetInt("uRolled", line.Rolled ? 1 : 0);
		_shader.SetFloat("uCosRoll", line.CosRoll);

		_gl.Disable(EnableCap.DepthTest);
		_gl.DepthMask(false);
		_gl.BindVertexArray(_vertexArray);
		_gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
		_gl.BindVertexArray(0);
		_gl.DepthMask(true);
		_gl.Enable(EnableCap.DepthTest);
	}

	public void Dispose() {
		_shader.Dispose();
		_gl.DeleteVertexArray(_vertexArray);
	}
}
