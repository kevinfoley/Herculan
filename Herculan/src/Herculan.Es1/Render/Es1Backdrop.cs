using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Scene;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// ES1's backdrop as <c>Scene_DrawTerrainPass</c> (<c>ES1DBSIM 00025b68</c>) paints it before any shape: the sky, the
/// ground below the horizon line and the skyline on it (docs/es1/rendering/ground-and-sky.md). Backdrop.glsl runs each
/// rule per pixel at the window's resolution; this sets it up from the camera each frame.
///
/// <para>The clouds and the scattered points, which the same pass draws, are not drawn: what draws them is Open
/// (docs/es1/rendering/ground-and-sky.md#open), as is what draws with the palette entries the pitch gradient
/// rewrites, so that is not run either.</para>
/// </summary>
public sealed class Es1Backdrop : ISceneBackdrop, IDisposable {
	private const string ShaderFolder = "Herculan.Es1.Render.Shaders.";

	/// <summary>The bitmap column the view's projection centre stands on: <c>GndTex_Draw</c> and the skyline take it as 160.</summary>
	private const int CentreColumn = 160;

	/// <summary>
	/// The span's step over an even column step: <c>du</c> is the row's two ends' difference, which spans 319 columns,
	/// times 204 / 256, in 8.16 fixed point — 319 · 204 / 65536 of a column's lateral offset a pixel.
	/// </summary>
	private const float ColumnStep = 319f * 204f / 65536f;

	private const float Q14 = SimTrig.One;

	private static readonly string[] SkyNames = Names("uSky", Es1Horizon.MaxBands);
	private static readonly string[] GroundBandNames = Names("uGroundBands", Es1Horizon.MaxBands);
	private static readonly string[] DistanceNames = Names("uDistances", Es1TexturedGround.ComparedDistances);
	private static readonly string[] SkylineRowNames = Names("uSkylineRows", Es1Skyline.MaxFrames);

	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly uint _vertexArray;
	private readonly Es1Horizon _horizon;
	private readonly Es1TexturedGround? _ground;
	private readonly Es1Skyline? _skyline;
	private readonly GpuTexture? _groundTexture;
	private readonly GpuTexture? _fadeColours;
	private readonly GpuTexture? _skylineTexture;

	/// <summary>Uploads the scene's backdrop tables; <paramref name="ground"/> and <paramref name="skyline"/> are null where the theater has none.</summary>
	public Es1Backdrop(GL gl, Es1Horizon horizon, Es1TexturedGround? ground, Es1Skyline? skyline, DynamixPalette palette) {
		_gl = gl;
		_horizon = horizon;
		_shader = ShaderProgram.Load(gl, typeof(Es1Backdrop).Assembly, ShaderFolder, "Backdrop.glsl");
		_vertexArray = gl.GenVertexArray();

		if (ground != null) {
			_ground = ground;
			var texels = new byte[ground.Texels.Length * 4];
			for (int i = 0; i < ground.Texels.Length; i++) {
				texels[i * 4] = ground.Texels[i];
				texels[i * 4 + 3] = 255;
			}

			_groundTexture = new GpuTexture(gl, texels, Es1GroundTexture.Size, Es1GroundTexture.Size);
			_fadeColours = new GpuTexture(gl, ground.TableColors(palette), Es1TexturedGround.TableLength,
				Es1TexturedGround.TableCount);
		}

		if (skyline != null) {
			_skyline = skyline;
			_skylineTexture = new GpuTexture(gl, skyline.Pixels, skyline.Width, skyline.Height);
		}
	}

	/// <summary>
	/// Whether the ground is textured, as it is when the preferences' option 5 is above 240 (<c>Hzline_FillGround</c>,
	/// docs/es1/rendering/ground-and-sky.md#the-ground); off, or without a ground texture, it is banded.
	/// </summary>
	public bool TexturedGround { get; set; } = true;

	public void Draw(Camera camera, int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		float scale = camera.FocalPixelsFor(viewportHeight) / Es1Horizon.FocalPixels;
		var centre = camera.PrincipalPointInWindow(viewportX, viewportY, viewportWidth, viewportHeight);
		short pitch = unchecked((short)camera.Pitch);
		short roll = unchecked((short)camera.Roll);
		short heading = camera.SimHeading;
		bool rolled = roll != 0;
		float cosRoll = rolled ? SimTrig.Cos(roll) / Q14 : 1f;
		float sinRoll = rolled ? SimTrig.Sin(roll) / Q14 : 0f;

		// The line's midpoint lies the horizon's drop along the rolled vertical below the centre, then LineDrop rows
		// straight down; the ground and the skyline take the line one row further down.
		float drop = Es1Horizon.HorizonDrop(pitch);
		float lineRow = MathF.Floor(drop * cosRoll + _horizon.LineDrop);
		float lineDown = drop + _horizon.LineDrop * cosRoll;
		int t = _horizon.HorizonGap;

		_shader.Use();
		_shader.SetVector2("uCentre", centre);
		_shader.SetFloat("uScale", scale);
		_shader.SetInt("uRolled", rolled ? 1 : 0);
		_shader.SetFloat("uCosRoll", cosRoll);
		_shader.SetFloat("uSinRoll", sinRoll);

		for (int k = 0; k < _horizon.SkyColors.Length; k++) {
			_shader.SetVector3(SkyNames[k], _horizon.SkyColors[k]);
		}

		_shader.SetInt("uSkyCount", _horizon.SkyColors.Length);
		_shader.SetFloat("uSkyBandHeight", _horizon.SkyBandHeight);
		_shader.SetFloat("uGap", t);
		_shader.SetFloat("uLineRow", lineRow);
		_shader.SetFloat("uLineDown", lineDown);
		_shader.SetFloat("uFirstSkyEdge", _horizon.SkyBandHeight * sinRoll * sinRoll + t * cosRoll);

		for (int k = 0; k < _horizon.GroundColors.Length; k++) {
			_shader.SetVector3(GroundBandNames[k], _horizon.GroundColors[k]);
		}

		_shader.SetInt("uGroundBandCount", _horizon.GroundColors.Length);
		_shader.SetFloat("uGroundBandHeight", Math.Max(_horizon.GroundBandHeight, 1));
		_shader.SetFloat("uFirstGroundEdge", _horizon.GroundBandHeight * sinRoll * sinRoll + t * cosRoll);

		bool textured = TexturedGround && _ground != null;
		_shader.SetInt("uTextured", textured ? 1 : 0);
		if (textured) {
			SetTexturedGround(camera, _ground!, pitch, roll, heading, lineRow, scale, centre,
				viewportY, viewportHeight);
		}

		SetSkyline(heading, rolled, lineRow, lineDown, cosRoll, sinRoll);

		_gl.Disable(EnableCap.DepthTest);
		_gl.DepthMask(false);
		_gl.BindVertexArray(_vertexArray);
		_gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
		_gl.BindVertexArray(0);
		_gl.DepthMask(true);
		_gl.Enable(EnableCap.DepthTest);
		_gl.ActiveTexture(TextureUnit.Texture0);
	}

	private void SetTexturedGround(Camera camera, Es1TexturedGround ground, short pitch, short roll, short heading,
			float lineRow, float scale, Vector2 centre, int viewportY, int viewportHeight) {
		// start: the moved line's row, raised to the view's top row; the rows run from it to the view's bottom row.
		int topRow = -(int)MathF.Round((viewportY + viewportHeight - centre.Y) / scale);
		int bottomRow = (int)MathF.Round((centre.Y - viewportY) / scale) - 1;
		int start = Math.Max((int)lineRow + 1, topRow);
		int rows = Math.Max(bottomRow - start + 1, 0);
		int groundStart = ground.UpdateSpanWidth(roll, start);
		int z = camera.Position.Z;
		int tableFirst = ground.RowTableFirst(z, pitch, rows, groundStart);

		_shader.SetSamplerTexture("uGroundTexture", _groundTexture!.Handle, 0);
		_shader.SetSamplerTexture("uFadeColours", _fadeColours!.Handle, 1);
		_shader.SetFloat("uGroundStart", groundStart);
		_shader.SetFloat("uRayShift", tableFirst - groundStart);
		_shader.SetInt("uFullFadeRows", ground.FullFadeRows);

		int compared = Math.Min(Es1TexturedGround.ComparedDistances, ground.Distances.Length);
		for (int i = 0; i < compared; i++) {
			_shader.SetFloat(DistanceNames[i], ground.Distances[i]);
		}

		_shader.SetInt("uDistanceCount", compared);
		_shader.SetFloat("uFocal", Es1Horizon.FocalPixels);
		_shader.SetFloat("uHeight", z);
		_shader.SetFloat("uCosPitch", SimTrig.Cos(pitch) / Q14);
		_shader.SetFloat("uSinPitch", SimTrig.Sin(pitch) / Q14);
		_shader.SetFloat("uCosHeading", SimTrig.Cos(heading) / Q14);
		_shader.SetFloat("uSinHeading", SimTrig.Sin(heading) / Q14);
		_shader.SetVector2("uGroundOrigin", new Vector2(camera.Position.X & 0xffff, camera.Position.Y & 0xffff));
		_shader.SetFloat("uColumnOrigin", roll == 0 ? CentreColumn : ground.SpanWidth >> 1);
		_shader.SetFloat("uColumnStep", ColumnStep);
	}

	private void SetSkyline(short heading, bool rolled, float lineRow, float lineDown, float cosRoll, float sinRoll) {
		if (_skyline is not { } skyline) {
			_shader.SetInt("uSkylineFrames", 0);
			return;
		}

		_shader.SetInt("uSkylineFrames", skyline.FrameCount);
		_shader.SetSamplerTexture("uSkyline", _skylineTexture!.Handle, 2);
		_shader.SetFloat("uSkylineWidth", skyline.Width);
		_shader.SetFloat("uSkylineSlot", skyline.SlotRows);
		for (int k = 0; k < skyline.FrameCount; k++) {
			_shader.SetFloat(SkylineRowNames[k], skyline.Rows[k]);
		}

		_shader.SetFloat("uSkylineStart", skyline.StartFor(heading) - CentreColumn);
		_shader.SetFloat("uSkylineMidX", -(_horizon.LineDrop + 1) * sinRoll);
		_shader.SetFloat("uSkylineLine", rolled ? lineDown + cosRoll : lineRow + 1);
		_shader.SetFloat("uLineOffset", _horizon.LineOffset);
	}

	public void Dispose() {
		_shader.Dispose();
		_groundTexture?.Dispose();
		_fadeColours?.Dispose();
		_skylineTexture?.Dispose();
		_gl.DeleteVertexArray(_vertexArray);
	}

	private static string[] Names(string array, int count) =>
		Enumerable.Range(0, count).Select(i => $"{array}[{i}]").ToArray();
}
