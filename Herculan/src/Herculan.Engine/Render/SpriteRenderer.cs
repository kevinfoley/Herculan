using System.Numerics;
using System.Runtime.InteropServices;
using Herculan.Engine.Gl;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// One shape's worth of billboards to draw this frame: which flipbook, which frame of it, and where
/// the shape stands.
/// </summary>
/// <param name="Sprites">The shape's flipbook — see <see cref="DtsSpriteBuilder.Build"/>.</param>
/// <param name="Atlas">The bank the flipbook's frames come out of, for their rects and sizes.</param>
/// <param name="TextureHandle">That bank, uploaded.</param>
/// <param name="Transform">
/// Model-to-world in render space. Its up axis is the sprite's up axis, which is what the
/// foreshortening and the on-screen rotation are both measured from — see
/// <see cref="SpriteRenderer"/>.
/// </param>
/// <param name="Frame">
/// The shape's cell-animation frame counter. Taken modulo the flipbook's length here, exactly as
/// <c>TSCellAnimPart_Render</c> takes it modulo the cell part's own child count, so a caller may
/// simply count up.
/// </param>
/// <param name="Filing">
/// How the object the billboards belong to is filed by terrain cell — see <see cref="SceneItem.Filing"/>.
/// A batch whose entry the pass leaves undrawn is skipped.
/// </param>
public readonly record struct SpriteBatch(
	SpriteQuad[][] Sprites, TextureAtlas Atlas, uint TextureHandle, Matrix4x4 Transform, int Frame,
	DrawEntry? Filing = null);

/// <summary>
/// Draws <see cref="SpriteQuad"/> billboards, the flat camera-facing bitmaps that impact effects and
/// the EMP cannons' rounds are made of — the GPU counterpart of <c>TSBitmapPart_Render</c>
/// (<c>004762e8</c>), whose scale, rotation, squash and anchor are
/// docs/retail/rendering/dts-billboards.md#tsbitmappart_render-004762e8.
///
/// <para>The original blits in screen pixels; this builds the same quad in view space, in the plane
/// parallel to the image plane at the sprite's depth, from a right/down basis derived from the
/// projected model up axis and sized in <see cref="WorldUnitsPerPixelPerRadius"/>. Perspective then
/// reproduces the <c>1 / depth</c> scaling exactly. The squash and the anchor are the original's
/// formulas; only the rotation's own perspective skew differs, which the original does not model
/// either.</para>
///
/// <para>Transparency is palette index 0, which the bank is decoded with — see
/// <c>Scene.SceneModelLibrary</c>. Alpha is tested rather than blended, matching a rasterizer that
/// either wrote a palette byte or skipped the pixel. Depth writes are off.</para>
///
/// <para>The original paints each impact effect, fire or shot as one entry of its per-cell draw list
/// (docs/retail/rendering/terrain-drawing.md, "Objects in the walk"). <see cref="WorldDrawPass"/> draws a batch in its
/// entry's turn through <see cref="DrawEntry"/>, with that entry's depth slice and the terrain's paint-rank test, so a
/// puff is painted whole over what came before it and the ridge in front of it still covers it. The per-pixel depth draw
/// instead draws every batch after the world through <see cref="Render"/>, depth-tested against it.</para>
/// </summary>
public sealed class SpriteRenderer : IDisposable {
	/// <summary>
	/// World units one bitmap pixel spans, per world unit of the part's radius: the
	/// <c>radius * 4 ... / 256</c> the original's Q8 scale works out to, whatever the field of view —
	/// docs/retail/rendering/dts-billboards.md#tsbitmappart_render-004762e8.
	/// </summary>
	private const float WorldUnitsPerPixelPerRadius = 1f / 64f;

	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly uint _vertexArray;
	private readonly uint _vertexBuffer;
	private readonly SpriteVertex[] _quad = new SpriteVertex[6];

	public SpriteRenderer(GL gl) {
		_gl = gl;
		_shader = ShaderProgram.Load(gl, "Sprite.glsl");

		// Its own unit for good, as SceneRenderer keeps it: a usampler2D on uTexture's unit fails every draw.
		_shader.Use();
		_shader.SetInt("uPaintRank", SceneRenderer.PaintRankUnit);

		_vertexArray = _gl.GenVertexArray();
		_gl.BindVertexArray(_vertexArray);
		_vertexBuffer = _gl.GenBuffer();
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

		unsafe {
			uint stride = (uint)sizeof(SpriteVertex);
			_gl.EnableVertexAttribArray(0);
			_gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
			_gl.EnableVertexAttribArray(1);
			_gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)12);
		}

		_gl.BindVertexArray(0);
	}

	/// <summary>
	/// Draws every batch into the viewport already set by whoever drew the world — call this straight
	/// after <see cref="SceneRenderer.Render"/> for the same panel, passing that panel's own camera
	/// and pixel size.
	/// </summary>
	public void Render(Camera camera, IReadOnlyList<SpriteBatch> batches, int viewportWidth, int viewportHeight) {
		if (batches.Count == 0) {
			return;
		}

		float aspect = (float)viewportWidth / Math.Max(viewportHeight, 1);
		var view = camera.ViewMatrix;
		var projection = camera.ProjectionMatrix(aspect);

		_shader.Use();
		_shader.SetMatrix("uProjection", projection);
		_shader.SetMatrix("uEntryProjection", projection);
		_shader.SetInt("uPaintRankTest", 0);

		_gl.DepthMask(false);
		_gl.BindVertexArray(_vertexArray);
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

		foreach (var batch in batches) {
			Draw(batch, view, camera.NearPlane);
		}

		_gl.BindVertexArray(0);
		_gl.DepthMask(true);
	}

	/// <summary>
	/// Draws one batch as its entry's turn in <see cref="WorldDrawPass"/>'s painter's order: placed through
	/// <paramref name="entryProjection"/>, clipped by <paramref name="projection"/> (the camera's own), and kept only where
	/// the ground showing ranks no later than <paramref name="rank"/>. The caller has bound the paint ranks and set the
	/// entry's depth range.
	/// </summary>
	internal void DrawEntry(in SpriteBatch batch, Camera camera, in Matrix4x4 projection, in Matrix4x4 entryProjection,
			uint rank) {
		_shader.Use();
		_shader.SetMatrix("uProjection", projection);
		_shader.SetMatrix("uEntryProjection", entryProjection);
		_shader.SetInt("uPaintRankTest", 1);
		_shader.SetUInt("uEntryRank", rank);

		_gl.DepthMask(false);
		_gl.BindVertexArray(_vertexArray);
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
		Draw(batch, camera.ViewMatrix, camera.NearPlane);
		_gl.BindVertexArray(0);
		_gl.DepthMask(true);
	}

	/// <summary>
	/// Widens [<paramref name="nearest"/>, <paramref name="farthest"/>] to the view depths <see cref="DrawEntry"/> would
	/// draw <paramref name="batch"/>'s quads at. Each quad lies in the plane square to the view axis at its anchor's depth,
	/// so the anchors are the whole of it.
	/// </summary>
	internal static void ExtendDepth(in SpriteBatch batch, in Matrix4x4 view, float nearPlane,
			ref float nearest, ref float farthest) {
		foreach (var quad in Quads(batch)) {
			float depth = -Vector3.Transform(Vector3.Transform(quad.Center, batch.Transform), view).Z;
			if (depth >= nearPlane) {
				nearest = MathF.Min(nearest, depth);
				farthest = MathF.Max(farthest, depth);
			}
		}
	}

	// The quads of the flipbook frame the batch shows, empty when it has none.
	private static SpriteQuad[] Quads(in SpriteBatch batch) {
		if (batch.Sprites.Length == 0) {
			return Array.Empty<SpriteQuad>();
		}

		int frame = ((batch.Frame % batch.Sprites.Length) + batch.Sprites.Length) % batch.Sprites.Length;
		return batch.Sprites[frame];
	}

	private void Draw(in SpriteBatch batch, in Matrix4x4 view, float nearPlane) {
		if (batch.Filing is { Drawn: false }) {
			return;
		}

		var quads = Quads(batch);
		if (quads.Length == 0) {
			return;
		}

		// The model's own up axis, brought into view space. Row 1 of a row-vector model matrix is
		// where model +Y goes, and render +Y is the simulation's +Z — the axis the original probes.
		var upModel = new Vector3(batch.Transform.M21, batch.Transform.M22, batch.Transform.M23);
		var upView = Vector3.TransformNormal(upModel, view);
		if (upView.LengthSquared() > 0f) {
			upView = Vector3.Normalize(upView);
		} else {
			upView = Vector3.UnitY;
		}

		// How much of the up axis survives in the view plane: 1 across the view, 0 pointing straight
		// at the viewer. The original measures it as a length against the 0x800 probe; normalised,
		// it is the same fraction.
		var planar = new Vector2(upView.X, upView.Y);
		float squash = Math.Clamp(planar.Length(), 0f, 1f);

		// The quad's own axes, in view space. "Down" is the sprite's +y, which is the screen's, so it
		// runs against the projected up axis; "right" completes the pair in the same handedness the
		// unrotated blit has (right = +x, down = -y in a y-up view space).
		Vector2 unitUp = squash > 1e-4f ? planar / planar.Length() : new Vector2(0f, 1f);
		var right = new Vector3(unitUp.Y, -unitUp.X, 0f);
		var down = new Vector3(-unitUp.X, -unitUp.Y, 0f);

		_shader.SetSamplerTexture("uTexture", batch.TextureHandle, 0);

		foreach (var quad in quads) {
			DrawQuad(quad, batch.Atlas, batch.Transform, view, nearPlane, squash, right, down);
		}
	}

	private void DrawQuad(in SpriteQuad quad, TextureAtlas atlas, in Matrix4x4 transform,
			in Matrix4x4 view, float nearPlane, float squash, in Vector3 right, in Vector3 down) {
		if (atlas.Frame(quad.FrameIndex) is not { } rect) {
			return;
		}

		var (columns, rows) = atlas.FrameSize(quad.FrameIndex);
		if (columns <= 0 || rows <= 0) {
			return;
		}

		var anchorWorld = Vector3.Transform(quad.Center, transform);
		var anchor = Vector3.Transform(anchorWorld, view);

		// Render space is right-handed with the camera down -Z, so depth in front of the eye is the
		// negated view z. The original's own near test is `if (nearLimit < depth)`.
		if (-anchor.Z < nearPlane) {
			return;
		}

		// The squash: the drawn height runs from the bitmap's width (axis fully foreshortened) to its
		// height (axis across the view) — the original's `cols + (rows - cols) * measured / 0x800`,
		// with `measured / 0x800` being exactly the normalised fraction computed by the caller.
		float heightPixels = columns + (rows - columns) * squash;
		float unitsPerPixel = WorldScale.DistanceToRender(quad.Radius) * WorldUnitsPerPixelPerRadius;
		float width = unitsPerPixel * columns;
		float height = unitsPerPixel * heightPixels;

		// OfsY is stated against the bitmap's natural height, so it is squashed with it; OfsX is not,
		// because the width never changes.
		float offsetY = quad.OffsetY * heightPixels / rows;
		Vector3 topLeft = anchor
			- right * (quad.OffsetX * unitsPerPixel)
			- down * (offsetY * unitsPerPixel);

		Vector3 topRight = topLeft + right * width;
		Vector3 bottomRight = topRight + down * height;
		Vector3 bottomLeft = topLeft + down * height;

		_quad[0] = new SpriteVertex(topLeft, new Vector2(rect.U0, rect.V0));
		_quad[1] = new SpriteVertex(topRight, new Vector2(rect.U1, rect.V0));
		_quad[2] = new SpriteVertex(bottomRight, new Vector2(rect.U1, rect.V1));
		_quad[3] = _quad[0];
		_quad[4] = _quad[2];
		_quad[5] = new SpriteVertex(bottomLeft, new Vector2(rect.U0, rect.V1));

		_gl.BufferData<SpriteVertex>(BufferTargetARB.ArrayBuffer, _quad, BufferUsageARB.DynamicDraw);
		_gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
	}

	public void Dispose() {
		_gl.DeleteBuffer(_vertexBuffer);
		_gl.DeleteVertexArray(_vertexArray);
		_shader.Dispose();
	}

	/// <param name="ViewPosition">The corner in the camera's own space, ready for the projection alone.</param>
	/// <param name="Uv">Where in the atlas that corner samples.</param>
	[StructLayout(LayoutKind.Sequential)]
	private readonly record struct SpriteVertex(Vector3 ViewPosition, Vector2 Uv);
}
