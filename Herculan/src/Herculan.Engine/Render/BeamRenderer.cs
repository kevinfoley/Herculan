using System.Numerics;
using System.Runtime.InteropServices;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// Draws the live <see cref="BeamTracer"/>s — the GPU counterpart of <c>BeamTracer_Draw</c>
/// (<c>0040bc14</c>), the tracer class's own paint method.
///
/// <para><b>What the original does.</b> It brings the tracer's two stored points into view space,
/// clips the pair against the near plane (<c>Beam_ClipSegmentToNearPlane</c>, <c>0040bb4c</c>), projects both to screen, and then
/// builds a four-vertex poly by stepping each screen point along the 2D perpendicular of the segment
/// by that end's own projected half-width — <c>(halfWidth &lt;&lt; shift) / viewZ</c>, floored at two
/// pixels, which floors the <i>half</i>-width and so keeps a beam four pixels across at worst. The
/// quad's UVs put the profile frame's u across the beam's <i>length</i> and its v across the width,
/// and nothing writes a z, so it is drawn flat over what is already there.</para>
///
/// <para><b>The fill is a plain texture copy.</b> The poly goes to <c>Raster_DrawPolygon</c> (<c>00468310</c>) as mode
/// <c>0</c> with its last argument zero, which selects <c>Raster_SpanTextured</c> (<c>0046ab10</c>)'s opaque half: fetch
/// <c>atlasPage[v][u]</c>, store that palette byte, step the fixed-point u/v, repeat. No shade level,
/// no colour lookup, no transparency test, no blending of any kind — so the fragment shader here is
/// the same single texture fetch, and the profile is uploaded fully opaque. See
/// <see cref="BeamAppearance"/> for why the record's colour index does not enter into it.</para>
///
/// <para><b>What this does instead, and why.</b> The expansion happens in the vertex shader in clip
/// space: the perpendicular is <c>cross(axis, toCamera)</c> so the quad faces the viewer in three
/// dimensions rather than two, and the offset is applied at each vertex's own depth, which is the
/// same construction the original approximates and is exact rather than interpolated. The half-width
/// floor is kept literally, measured in the framebuffer the panel is drawn into. Depth testing is on
/// with depth writes off: the original has no depth at all here, but a beam already stops at
/// whatever it hit, so testing costs nothing visible and stops a shot fired past a ridge from being
/// painted over it.</para>
///
/// <para><b>The jagged form — ELF and ELF2.</b> A chain tracer (see <see cref="BeamTracer"/>) is
/// painted one quad at a time through the shape renderer's point-list path instead, and that fill is
/// <b>flat-coloured with no texture</b>: the record's colour index is the rasterizer's fill brush.
/// The quads are not turned to face the viewer either — the width is a z offset baked into the
/// geometry, so an ELF seen from directly above is edge-on. See docs/retail/simulation/beam-visuals.md,
/// "ELF and ELF2 — the jagged branch".</para>
///
/// <para><b>The muzzle stub is retail's, not a bug here.</b> The jagged branch falls through into the
/// straight-beam code, which draws the tracer's first two points — for a chain, a stub one half-width
/// long at the muzzle — once per chain quad. This draws it once, which is pixel-identical because the
/// fill is opaque. Logged in KNOWN_ISSUES.md.</para>
/// </summary>
public sealed class BeamRenderer : IDisposable {
	/// <summary>
	/// <c>BeamTracer_Draw</c>'s <c>if (halfWidth &lt; 2) halfWidth = 2</c>, applied to each end
	/// independently. It floors the <i>half</i>-width — the value is what each screen point is stepped
	/// by in both directions along the perpendicular — so a beam never draws narrower than four
	/// pixels, not two.
	/// </summary>
	private const float MinimumHalfPixels = 2f;

	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly ShaderProgram _jaggedShader;
	private readonly BeamAppearance _appearance;
	private readonly Dictionary<int, GpuTexture> _profiles = new();
	private readonly uint _vertexArray;
	private readonly uint _vertexBuffer;
	private readonly uint _jaggedVertexArray;
	private readonly uint _jaggedVertexBuffer;
	private readonly BeamVertex[] _quad = new BeamVertex[6];
	private readonly List<Vector3> _chain = new();

	public BeamRenderer(GL gl, BeamAppearance appearance) {
		_gl = gl;
		_appearance = appearance;
		_shader = ShaderProgram.Load(gl, "Beam.glsl");
		_jaggedShader = ShaderProgram.Load(gl, "BeamJagged.glsl");

		_vertexArray = _gl.GenVertexArray();
		_gl.BindVertexArray(_vertexArray);
		_vertexBuffer = _gl.GenBuffer();
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

		unsafe {
			uint stride = (uint)sizeof(BeamVertex);
			_gl.EnableVertexAttribArray(0);
			_gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
			_gl.EnableVertexAttribArray(1);
			_gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
			_gl.EnableVertexAttribArray(2);
			_gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, (void*)24);
			_gl.EnableVertexAttribArray(3);
			_gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)28);
		}

		// The chain's vertices arrive final — no expansion, no profile coordinate — so this one is a
		// bare position stream.
		_jaggedVertexArray = _gl.GenVertexArray();
		_gl.BindVertexArray(_jaggedVertexArray);
		_jaggedVertexBuffer = _gl.GenBuffer();
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _jaggedVertexBuffer);

		unsafe {
			_gl.EnableVertexAttribArray(0);
			_gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false,
				(uint)sizeof(Vector3), (void*)0);
		}

		_gl.BindVertexArray(0);
	}

	/// <summary>
	/// Draws every tracer in <paramref name="tracers"/> into the viewport already set by whoever drew
	/// the world — call this straight after <see cref="SceneRenderer.Render"/> for the same panel,
	/// passing that panel's own camera and pixel size.
	///
	/// <para>With <paramref name="filing"/>, the table that pass filed its objects in, each span of a
	/// straight beam is drawn only where its own tracer would be — filed by its start point with no
	/// radius, as <c>Scene_SubmitObject</c> (<c>004282d8</c>) files every object of the tracer pool — and
	/// a chain only where its one tracer would be. See <see cref="BeamTracer.Span"/>.</para>
	/// </summary>
	public void Render(Camera camera, IReadOnlyList<BeamTracer> tracers,
			int viewportWidth, int viewportHeight, ObjectDrawTable? filing = null) {
		if (tracers.Count == 0) {
			return;
		}

		float aspect = (float)viewportWidth / Math.Max(viewportHeight, 1);
		var cameraPosition = WorldScale.ToRender(camera.Position);
		var projection = camera.ProjectionMatrix(aspect);

		// Opaque, and no depth write: the original submits these polys with no z at all, and nothing
		// is drawn into the 3D view after them.
		_gl.DepthMask(false);

		DrawChains(camera, projection, tracers, filing);

		_shader.Use();
		_shader.SetMatrix("uView", camera.ViewMatrix);
		_shader.SetMatrix("uProjection", projection);
		_shader.SetVector3("uCameraPosition", cameraPosition);
		_shader.SetVector2("uViewport", new Vector2(viewportWidth, Math.Max(viewportHeight, 1)));
		_shader.SetFloat("uMinimumHalfPixels", MinimumHalfPixels);

		_gl.BindVertexArray(_vertexArray);
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

		foreach (var tracer in tracers) {
			Draw(tracer, camera.ViewMatrix, camera.NearPlane, filing);
		}

		_gl.BindVertexArray(0);
		_gl.DepthMask(true);
	}

	/// <summary>
	/// The jagged pass: every chain tracer's quads, each a flat fill in that subtype's
	/// <c>BEAM.DAT</c> colour. One draw call per tracer, since the colour is a uniform and a frame
	/// never holds many.
	/// </summary>
	private void DrawChains(Camera camera, Matrix4x4 projection, IReadOnlyList<BeamTracer> tracers,
			ObjectDrawTable? filing) {
		bool started = false;

		foreach (var tracer in tracers) {
			if (!tracer.IsJagged || tracer.QuadCount == 0 || !SpanDrawn(filing, tracer.Start)) {
				continue;
			}

			if (!started) {
				_jaggedShader.Use();
				_jaggedShader.SetMatrix("uView", camera.ViewMatrix);
				_jaggedShader.SetMatrix("uProjection", projection);
				_gl.BindVertexArray(_jaggedVertexArray);
				_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _jaggedVertexBuffer);
				started = true;
			}

			_chain.Clear();
			for (int quad = 0; quad < tracer.QuadCount; quad++) {
				var (a, b, c, d) = tracer.Quad(quad);
				var pa = WorldScale.ToRender(a);
				var pb = WorldScale.ToRender(b);
				var pc = WorldScale.ToRender(c);
				var pd = WorldScale.ToRender(d);
				_chain.Add(pa);
				_chain.Add(pb);
				_chain.Add(pc);
				_chain.Add(pa);
				_chain.Add(pc);
				_chain.Add(pd);
			}

			_jaggedShader.SetVector3("uColor", _appearance.Color(tracer.SubtypeId));
			_gl.BufferData<Vector3>(BufferTargetARB.ArrayBuffer, CollectionsMarshal.AsSpan(_chain),
				BufferUsageARB.DynamicDraw);
			_gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_chain.Count);
		}

		if (started) {
			_gl.BindVertexArray(0);
		}
	}

	/// <summary>Whether the tracer filed at <paramref name="start"/> is drawn this pass — true with no table.</summary>
	private static bool SpanDrawn(ObjectDrawTable? filing, Numerics.Vec3i start) =>
		filing?.WouldDraw(start, 0, ObjectTypeTag.Projectile) ?? true;

	private void Draw(BeamTracer tracer, Matrix4x4 view, float nearPlane, ObjectDrawTable? filing) {
		int halfWidth = _appearance.HalfWidth(tracer.SubtypeId);
		if (halfWidth <= 0 || Profile(tracer.SubtypeId) is not { } profile) {
			return;
		}

		// A chain tracer reaches here too, because the original's jagged branch falls through into
		// this code rather than returning — and it draws the tracer's first two points, which for a
		// chain are node zero's pair and not the muzzle and the hit. See the class remarks.
		if (tracer.IsJagged && tracer.Points.Count >= 2) {
			if (SpanDrawn(filing, tracer.Start)) {
				DrawSegment(tracer.Points[0], tracer.Points[1], halfWidth, profile, view, nearPlane);
			}

			return;
		}

		for (int span = 0; span < tracer.SpanCount; span++) {
			var (from, to) = tracer.Span(span);
			if (SpanDrawn(filing, from)) {
				DrawSegment(from, to, halfWidth, profile, view, nearPlane);
			}
		}
	}

	private void DrawSegment(Numerics.Vec3i from, Numerics.Vec3i to, int halfWidth, GpuTexture profile,
			Matrix4x4 view, float nearPlane) {
		var start = WorldScale.ToRender(from);
		var end = WorldScale.ToRender(to);
		var axis = end - start;
		if (axis.LengthSquared() <= 0f) {
			return;
		}

		axis = Vector3.Normalize(axis);

		if (!ClipToNearPlane(view, nearPlane, ref start, ref end)) {
			return;
		}

		// v runs across the width, from one edge of the profile to the other; nothing varies along
		// the beam's length, which is why there is no u.
		_quad[0] = new BeamVertex(start, axis, -1f, 0f);
		_quad[1] = new BeamVertex(start, axis, 1f, 1f);
		_quad[2] = new BeamVertex(end, axis, 1f, 1f);
		_quad[3] = _quad[0];
		_quad[4] = _quad[2];
		_quad[5] = new BeamVertex(end, axis, -1f, 0f);

		_shader.SetFloat("uHalfWidth", WorldScale.DistanceToRender(halfWidth));
		_shader.SetSamplerTexture("uProfileTexture", profile.Handle, 0);

		_gl.BufferData<BeamVertex>(BufferTargetARB.ArrayBuffer, _quad, BufferUsageARB.DynamicDraw);
		_gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
	}

	/// <summary>
	/// <c>Beam_ClipSegmentToNearPlane</c> (<c>0040bb4c</c>), the tracer's own near-plane clip, which the original runs on the pair of
	/// view-space points before it projects either. It matters more here than anywhere else in the
	/// renderer: a beam starts at a hardpoint that is often <i>behind</i> the eye node, and an endpoint
	/// behind the plane projects with a negative w — which puts the muzzle end of the quad on the
	/// wrong side of the screen rather than off it.
	///
	/// <para>The original clips whichever end is short and leaves the other, which is what this does;
	/// a pair with both ends behind the plane is dropped, as its <c>return 0</c> does.</para>
	/// </summary>
	private static bool ClipToNearPlane(Matrix4x4 view, float nearPlane, ref Vector3 start, ref Vector3 end) {
		// Render space is right-handed with the camera looking down -Z, so a point's depth in front
		// of the eye is the negated view-space z.
		float startDepth = -Vector3.Transform(start, view).Z;
		float endDepth = -Vector3.Transform(end, view).Z;

		if (startDepth < nearPlane && endDepth < nearPlane) {
			return false;
		}

		if (startDepth < nearPlane) {
			start = Vector3.Lerp(start, end, (nearPlane - startDepth) / (endDepth - startDepth));
		} else if (endDepth < nearPlane) {
			end = Vector3.Lerp(end, start, (nearPlane - endDepth) / (startDepth - endDepth));
		}

		return true;
	}

	/// <summary>
	/// The uploaded cross-section for one subtype id, built on first use. The profile is one texel
	/// wide and as tall as the source frame's row count, so it is sampled purely by v.
	/// </summary>
	private GpuTexture? Profile(int subtypeId) {
		if (_profiles.TryGetValue(subtypeId, out var cached)) {
			return cached;
		}

		var texels = _appearance.Profile(subtypeId);
		if (texels.IsEmpty) {
			return null;
		}

		var texture = new GpuTexture(_gl, texels, 1, texels.Length / 4);
		_profiles[subtypeId] = texture;
		return texture;
	}

	public void Dispose() {
		foreach (var texture in _profiles.Values) {
			texture.Dispose();
		}

		_profiles.Clear();
		_gl.DeleteBuffer(_vertexBuffer);
		_gl.DeleteVertexArray(_vertexArray);
		_gl.DeleteBuffer(_jaggedVertexBuffer);
		_gl.DeleteVertexArray(_jaggedVertexArray);
		_shader.Dispose();
		_jaggedShader.Dispose();
	}

	/// <param name="Position">The endpoint this vertex sits on, in render space.</param>
	/// <param name="Axis">The beam's unit direction, so the shader can build the perpendicular.</param>
	/// <param name="Side">-1 or +1: which way along that perpendicular this vertex is pushed.</param>
	/// <param name="Profile">Where across the cross-section this vertex samples, 0 or 1.</param>
	[StructLayout(LayoutKind.Sequential)]
	private readonly record struct BeamVertex(Vector3 Position, Vector3 Axis, float Side, float Profile);
}
