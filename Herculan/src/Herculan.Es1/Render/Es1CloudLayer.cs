using System.Numerics;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Scene;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// Draws <see cref="Es1Clouds"/>' panels as <c>TexPoly_Render</c> (<c>ES1DBSIM 000279a4</c>) does: each a frame of the
/// cloud bank mapped onto its four corners, cut at the near plane and filled through brush mode 5 by Clouds.glsl, with
/// no light and no distance colour (docs/es1/rendering/ground-and-sky.md#the-clouds).
/// </summary>
public sealed class Es1CloudLayer : IDisposable {
	/// <summary>The most points a panel has once cut at the near plane, as Clouds.glsl's arrays hold.</summary>
	public const int MaxPoints = 8;

	private const string ShaderFolder = "Herculan.Es1.Render.Shaders.";

	private static readonly string[] PointNames = Names("uPoints");
	private static readonly string[] TexelNames = Names("uTexels");

	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly uint _vertexArray;
	private readonly Es1Clouds _clouds;
	private readonly GpuTexture?[] _frames;
	private readonly Vector2[] _frameTexels;

	/// <summary>Uploads the cloud bank's frames in the theater's palette, index 0 left transparent.</summary>
	public Es1CloudLayer(GL gl, Es1Clouds clouds, DynamixPalette palette) {
		_gl = gl;
		_clouds = clouds;
		_shader = ShaderProgram.Load(gl, typeof(Es1CloudLayer).Assembly, ShaderFolder, "Clouds.glsl");
		_vertexArray = gl.GenVertexArray();

		var images = clouds.Bank.Images ?? [];
		_frames = new GpuTexture?[images.Length];
		_frameTexels = new Vector2[images.Length];
		for (int f = 0; f < images.Length; f++) {
			var image = images[f];
			if (image?.ImageData is not { } data || image.Cols < 1 || image.Rows < 1
					|| data.Length < image.Cols * image.Rows) {
				continue;
			}

			var rgba = new byte[image.Cols * image.Rows * 4];
			for (int i = 0; i < image.Cols * image.Rows; i++) {
				if (data[i] != 0 && Es1Horizon.ColorOf(palette, data[i]) is { } colour) {
					rgba[i * 4] = (byte)(colour.X * 255f);
					rgba[i * 4 + 1] = (byte)(colour.Y * 255f);
					rgba[i * 4 + 2] = (byte)(colour.Z * 255f);
					rgba[i * 4 + 3] = 255;
				}
			}

			_frames[f] = new GpuTexture(gl, rgba, image.Cols, image.Rows);

			// The far texture corner: the last column rounded down to a multiple of 8, and the last row.
			_frameTexels[f] = new Vector2((image.Cols - 1) & ~7, image.Rows - 1);
		}
	}

	/// <summary>
	/// The clouds for this frame, after the skyline and before the scattered points. <paramref name="detail"/> is the
	/// preferences byte <c>000bd566</c> the number drawn scales by.
	/// </summary>
	public void Draw(Camera camera, int detail, Random random, Vector2 centre, float focal, int viewportX,
			int viewportY, int viewportWidth, int viewportHeight) {
		var positions = _clouds.Frame(camera.Position, detail, random);
		if (positions.IsEmpty) {
			return;
		}

		var view = camera.ViewMatrix;
		Span<Vector3> inView = stackalloc Vector3[MaxPoints];
		Span<Vector2> texels = stackalloc Vector2[MaxPoints];
		Span<Vector3> cutView = stackalloc Vector3[MaxPoints];
		Span<Vector2> cutTexels = stackalloc Vector2[MaxPoints];

		_shader.Use();
		_shader.SetVector2("uViewportOrigin", new Vector2(viewportX, viewportY));
		_shader.SetVector2("uViewportSize", new Vector2(Math.Max(viewportWidth, 1), Math.Max(viewportHeight, 1)));
		_gl.Disable(EnableCap.DepthTest);
		_gl.DepthMask(false);
		_gl.BindVertexArray(_vertexArray);

		foreach (var position in positions) {
			// Raster_FrontFacing of the normal (0, 0, -1) at corner 0: from below the panel its front is seen.
			int frame = camera.Position.Z < position.Z + Es1Clouds.Height ? _clouds.FrameBelow : _clouds.FrameAbove;
			if (frame >= _frames.Length || _frames[frame] is not { } texture) {
				continue;
			}

			var far = _frameTexels[frame];
			for (int k = 0; k < Es1Clouds.Corners.Length; k++) {
				inView[k] = Vector3.Transform(WorldScale.ToRender(position + Es1Clouds.Corners[k]), view);
			}

			texels[0] = Vector2.Zero;
			texels[1] = new Vector2(far.X, 0f);
			texels[2] = far;
			texels[3] = new Vector2(0f, far.Y);

			int count = CutAtNearPlane(inView[..4], texels[..4], camera.NearPlane, cutView, cutTexels);
			if (count < 3) {
				continue;
			}

			for (int k = 0; k < count; k++) {
				float depth = -cutView[k].Z;
				_shader.SetVector2(PointNames[k], new Vector2(
					centre.X + focal * cutView[k].X / depth, centre.Y + focal * cutView[k].Y / depth));
				_shader.SetVector2(TexelNames[k], cutTexels[k]);
			}

			_shader.SetInt("uCount", count);
			_shader.SetSamplerTexture("uFrame", texture.Handle, 0);
			_gl.DrawArrays(PrimitiveType.TriangleFan, 0, (uint)count);
		}

		_gl.BindVertexArray(0);
		_gl.DepthMask(true);
		_gl.Enable(EnableCap.DepthTest);
	}

	/// <summary>
	/// The panel's part in front of the near plane, its texture points cut along with its corners, as
	/// <c>Poly_ClipRingToNearPlaneWithUV</c> cuts a ring. Returns the number of points written.
	/// </summary>
	private static int CutAtNearPlane(ReadOnlySpan<Vector3> points, ReadOnlySpan<Vector2> texels, float near,
			Span<Vector3> outPoints, Span<Vector2> outTexels) {
		int count = 0;
		for (int i = 0; i < points.Length; i++) {
			int j = i + 1 < points.Length ? i + 1 : 0;
			float depthI = -points[i].Z;
			float depthJ = -points[j].Z;
			if (depthI > near) {
				outPoints[count] = points[i];
				outTexels[count++] = texels[i];
			}

			if ((depthI > near) != (depthJ > near)) {
				float t = (near - depthI) / (depthJ - depthI);
				outPoints[count] = Vector3.Lerp(points[i], points[j], t);
				outTexels[count++] = Vector2.Lerp(texels[i], texels[j], t);
			}
		}

		return count;
	}

	public void Dispose() {
		_shader.Dispose();
		foreach (var frame in _frames) {
			frame?.Dispose();
		}

		_gl.DeleteVertexArray(_vertexArray);
	}

	private static string[] Names(string array) =>
		Enumerable.Range(0, MaxPoints).Select(i => $"{array}[{i}]").ToArray();
}
