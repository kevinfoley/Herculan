using System.Numerics;
using System.Runtime.InteropServices;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// One tracer object as the original allocates and files it: span <paramref name="Span"/> of a straight beam, or a chain
/// tracer whole when <paramref name="Span"/> is -1 — see <see cref="BeamTracer.Span"/>. <paramref name="Filing"/> files it
/// by the span's start point with no radius, a <see cref="ObjectTypeTag.Projectile"/>, as <c>Scene_SubmitObject</c>
/// (<c>004282d8</c>) files every object of the tracer pool (docs/retail/simulation/beam-visuals.md, "Chain").
/// </summary>
public readonly record struct BeamSpanDraw(BeamTracer Tracer, int Span, DrawEntry Filing);

/// <summary>
/// Draws the live <see cref="BeamTracer"/>s — the GPU counterpart of <c>BeamTracer_Draw</c>
/// (<c>0040bc14</c>). What the original draws is docs/retail/simulation/beam-visuals.md#drawing--beamtracer_draw-0040bc14-vtable-slot-0.
///
/// <para><b>A straight beam is an opaque textured ribbon.</b> The fragment shader is one fetch of the
/// subtype's cross-section profile, uploaded fully opaque, as the original's fill is a plain texture
/// copy; <see cref="BeamAppearance"/> has why the record's color index plays no part.</para>
///
/// <para><b>This engine builds the ribbon in 3D, where the original builds it on screen.</b> The
/// original steps each projected end along the segment's 2D perpendicular; the vertex shader here
/// offsets each end along <c>cross(axis, toCamera)</c> in clip space, at that end's own depth, so the
/// ribbon faces the viewer. The four-pixel minimum width is kept (<see cref="MinimumHalfPixels"/>),
/// measured in the framebuffer the panel is drawn into. In the per-pixel draw, depth testing is on with depth writes
/// off, where the original has no depth here at all: a beam already ends at whatever it hit, so the test changes
/// nothing visible except that a shot fired past a ridge is not painted over it. Depth
/// clamping is on for the straight beams, so the GPU's near plane does not cut a ribbon that
/// <see cref="ClipToNearPlane"/> has already clipped nearer.</para>
///
/// <para><b>In the painter's-order draw a span is an entry.</b> <see cref="WorldDrawPass"/> draws each
/// <see cref="BeamSpanDraw"/> in its entry's turn through <see cref="DrawSpan"/>, with that entry's depth slice and the
/// terrain's paint-rank test in place of the depth test against the world. So a span covers what was painted before
/// its cell and is covered by every cell painted after: fired past a ridge, the spans that start beyond it are hidden,
/// and the one that starts in front of it, an ELF's whole chain among them, is painted over it.</para>
///
/// <para><b>ELF and ELF2 are flat-colored zigzags.</b> A chain tracer's quads are drawn as
/// <see cref="BeamTracer"/> built them, untextured, in the record's color, and not turned toward the
/// viewer — see docs/retail/simulation/beam-visuals.md#elf-and-elf2--the-jagged-branch.</para>
///
/// <para><b>The muzzle stub a chain tracer leaves is drawn once.</b> The stub is retail's
/// (docs/retail/simulation/beam-visuals.md#the-muzzle-stub-is-a-retail-fall-through, logged in
/// KNOWN_ISSUES.md), and the original redraws it once per chain quad; once per tracer is
/// pixel-identical because the fill is opaque.</para>
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
	private readonly Dictionary<(int SubtypeId, bool HueTurned), GpuTexture> _profiles = new();
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

		// Their own unit for good, as SceneRenderer keeps it: a usampler2D on the profile's unit fails every draw.
		foreach (var program in new[] { _shader, _jaggedShader }) {
			program.Use();
			program.SetInt("uPaintRank", SceneRenderer.PaintRankUnit);
		}

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
		var projection = camera.ProjectionMatrix(aspect);

		// Opaque, and no depth write: the original submits these polys with no z at all, and nothing
		// is drawn into the 3D view after them.
		_gl.DepthMask(false);

		DrawChains(camera, projection, tracers, filing);

		UseStraight(camera, projection, projection, viewportWidth, viewportHeight, rank: null);

		// The segments are clipped at the view's own near plane, nearer than the GPU's, so the GPU's
		// near clip is turned off for them rather than cutting the muzzle end a second time.
		_gl.Enable(EnableCap.DepthClamp);

		foreach (var tracer in tracers) {
			Draw(tracer, camera.ViewMatrix, camera.ViewNearPlane, filing);
		}

		_gl.Disable(EnableCap.DepthClamp);
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
				UseJagged(camera, projection, projection, rank: null);
				started = true;
			}

			DrawChain(tracer);
		}

		if (started) {
			_gl.BindVertexArray(0);
		}
	}

	// The straight beams' program and buffers, readied for a pass: placed through `entryProjection`, measured through
	// `projection`, and with the paint-rank test against `rank` when there is one.
	private void UseStraight(Camera camera, in Matrix4x4 projection, in Matrix4x4 entryProjection,
			int viewportWidth, int viewportHeight, uint? rank) {
		_shader.Use();
		_shader.SetMatrix("uView", camera.ViewMatrix);
		_shader.SetMatrix("uProjection", projection);
		_shader.SetMatrix("uEntryProjection", entryProjection);
		_shader.SetVector3("uCameraPosition", WorldScale.ToRender(camera.Position));
		_shader.SetVector2("uViewport", new Vector2(viewportWidth, Math.Max(viewportHeight, 1)));
		_shader.SetFloat("uMinimumHalfPixels", MinimumHalfPixels);
		_shader.SetInt("uPaintRankTest", rank.HasValue ? 1 : 0);
		_shader.SetUInt("uEntryRank", rank ?? 0);

		_gl.BindVertexArray(_vertexArray);
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
	}

	// The chains' program and buffers, the same way.
	private void UseJagged(Camera camera, in Matrix4x4 projection, in Matrix4x4 entryProjection, uint? rank) {
		_jaggedShader.Use();
		_jaggedShader.SetMatrix("uView", camera.ViewMatrix);
		_jaggedShader.SetMatrix("uProjection", projection);
		_jaggedShader.SetMatrix("uEntryProjection", entryProjection);
		_jaggedShader.SetInt("uPaintRankTest", rank.HasValue ? 1 : 0);
		_jaggedShader.SetUInt("uEntryRank", rank ?? 0);
		_gl.BindVertexArray(_jaggedVertexArray);
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _jaggedVertexBuffer);
	}

	// One chain tracer's quads, a flat fill in its subtype's colour, through the program UseJagged readied.
	private void DrawChain(BeamTracer tracer) {
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

	/// <summary>
	/// Draws one tracer object as its entry's turn in <see cref="WorldDrawPass"/>'s painter's order: a straight beam's
	/// span, or a chain and the muzzle stub its tracer leaves (see the class remarks). Placed through
	/// <paramref name="entryProjection"/>, measured through <paramref name="projection"/> (the camera's own), and kept only
	/// where the ground showing ranks no later than <paramref name="rank"/>. The caller has bound the paint ranks, set the
	/// entry's depth range and turned depth clamping on, which the straight segment needs as <see cref="Render"/> gives it.
	/// </summary>
	internal void DrawSpan(in BeamSpanDraw span, Camera camera, in Matrix4x4 projection, in Matrix4x4 entryProjection,
			uint rank, int viewportWidth, int viewportHeight) {
		var tracer = span.Tracer;
		_gl.DepthMask(false);

		if (span.Span < 0 && tracer.IsJagged && tracer.QuadCount > 0) {
			UseJagged(camera, projection, entryProjection, rank);
			DrawChain(tracer);
		}

		if (Appearance(tracer) is { } look) {
			UseStraight(camera, projection, entryProjection, viewportWidth, viewportHeight, rank);
			if (tracer.IsJagged) {
				if (span.Span < 0 && tracer.Points.Count >= 2) {
					DrawSegment(tracer.Points[0], tracer.Points[1], look.HalfWidth, look.Profile, camera.ViewMatrix,
						camera.ViewNearPlane);
				}
			} else if (span.Span >= 0 && span.Span < tracer.SpanCount) {
				var (from, to) = tracer.Span(span.Span);
				DrawSegment(from, to, look.HalfWidth, look.Profile, camera.ViewMatrix, camera.ViewNearPlane);
			}
		}

		_gl.BindVertexArray(0);
		_gl.DepthMask(true);
	}

	/// <summary>
	/// Widens [<paramref name="nearest"/>, <paramref name="farthest"/>] to the view depths <see cref="DrawSpan"/> would
	/// draw <paramref name="span"/> at: its points' depths, each no nearer than the view's near plane the segment is
	/// clipped at, give or take the ribbon's half-width.
	/// </summary>
	internal void ExtendDepth(in BeamSpanDraw span, Camera camera, ref float nearest, ref float farthest) {
		var tracer = span.Tracer;
		var view = camera.ViewMatrix;
		float margin = WorldScale.DistanceToRender(System.Math.Max(_appearance.HalfWidth(tracer.SubtypeId), 0));
		float near = camera.ViewNearPlane;
		float least = float.PositiveInfinity;
		float most = float.NegativeInfinity;

		if (tracer.IsJagged) {
			foreach (var point in tracer.Points) {
				Take(point);
			}
		} else if (span.Span >= 0 && span.Span < tracer.SpanCount) {
			var (from, to) = tracer.Span(span.Span);
			Take(from);
			Take(to);
		}

		if (most >= near) {
			nearest = MathF.Min(nearest, MathF.Max(least - margin, near));
			farthest = MathF.Max(farthest, most + margin);
		}

		void Take(Numerics.Vec3i point) {
			float depth = -Vector3.Transform(WorldScale.ToRender(point), view).Z;
			least = MathF.Min(least, depth);
			most = MathF.Max(most, depth);
		}
	}

	/// <summary>Whether the tracer filed at <paramref name="start"/> is drawn this pass — true with no table.</summary>
	private static bool SpanDrawn(ObjectDrawTable? filing, Numerics.Vec3i start) =>
		filing?.WouldDraw(start, 0, ObjectTypeTag.Projectile) ?? true;

	// A straight segment's half-width and cross-section, or null when its subtype draws none.
	private (int HalfWidth, GpuTexture Profile)? Appearance(BeamTracer tracer) {
		int halfWidth = _appearance.HalfWidth(tracer.SubtypeId);

		// Read every frame, so toggling the tweak recolours the next shot at once.
		bool hueTurned = BeamAppearance.IsParticleBeam(tracer.SubtypeId)
			&& TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ChangePbwBeamHue);
		return halfWidth > 0 && Profile(tracer.SubtypeId, hueTurned) is { } profile
			? (halfWidth, profile)
			: null;
	}

	private void Draw(BeamTracer tracer, Matrix4x4 view, float nearPlane, ObjectDrawTable? filing) {
		if (Appearance(tracer) is not { } look) {
			return;
		}

		var (halfWidth, profile) = look;

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
	///
	/// <para>The plane is the view's own (<see cref="Camera.ViewNearPlane"/>), not the GPU's farther
	/// <see cref="Camera.NearPlane"/>: the farther plane puts the cut end deeper, and so nearer the
	/// screen's centre, than the original draws it.</para>
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
	/// wide and as tall as the source frame's row count, so it is sampled purely by v. With
	/// <paramref name="hueTurned"/>, the profile through <see cref="BeamAppearance.ParticleBeamHue"/>.
	/// </summary>
	private GpuTexture? Profile(int subtypeId, bool hueTurned) {
		if (_profiles.TryGetValue((subtypeId, hueTurned), out var cached)) {
			return cached;
		}

		var texels = _appearance.Profile(subtypeId);
		if (texels.IsEmpty) {
			return null;
		}

		var texture = hueTurned
			? new GpuTexture(_gl, BeamAppearance.ParticleBeamHue(subtypeId, texels), 1, texels.Length / 4)
			: new GpuTexture(_gl, texels, 1, texels.Length / 4);
		_profiles[(subtypeId, hueTurned)] = texture;
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
