using System.Numerics;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Data.File.Dts.Poly;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;

namespace Herculan.Engine.Render;

/// <summary>
/// One group's polys, each resolved per side to what it draws — a texture frame, a shade ramp or a
/// pair of palette colours, after the <c>TS*Poly_Render</c> functions — and fanned into triangles,
/// outline edges and points.
/// </summary>
public static partial class DtsMeshBuilder {
	private static readonly Vector3 FallbackColor = new(0.72f, 0.72f, 0.75f);

	/// <summary>
	/// Stand-in colour for a <see cref="TSTexture4Poly"/> whose frame could not be resolved. Distinct
	/// from <see cref="FallbackColor"/> so an unresolved texture poly is identifiable on screen
	/// instead of blending in with genuinely untextured geometry.
	/// </summary>
	private static readonly Vector3 TextureFallbackColor = new(0.47f, 0.59f, 0.75f);

	/// <summary>
	/// Vertex-order UV corners for a textured poly, as fractions of the frame's own rect.
	/// RE-confirmed order (top-left, top-right, bottom-right, bottom-left) — the exe builds
	/// <c>[(F0,F1), (F2,F1), (F2,F3), (F0,F3)]</c> from a per-frame descriptor, see
	/// docs/retail/formats/dts-texture-binding.md's "Render path and UV generation".
	///
	/// <para>The exe fills all four unconditionally and then hands the rasterizer the poly's own
	/// vertex count, which walks this array one entry per vertex — so a three-vertex
	/// <see cref="TSTexture4Poly"/> takes the first three and the fourth is simply never read.</para>
	/// </summary>
	private static readonly Vector2[] QuadCorners = {
		new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)
	};

	private static void AppendGroup(TSGroup group, ANAnimList? animList, Collector sink, TextureAtlas? atlas, SurfaceShading? shading) {
		if (group.Points == null || group.Indexes == null || group.Polys == null) {
			return;
		}

		Vector3 offset = ResolveGroupOffset(group, animList);

		// Point shorts go straight through as world coordinates — see WorldScale.WorldUnitsPerDtsUnit
		// for the measurements behind that. Each point is kept twice: once at the rest pose the flat
		// mesh bakes, and once in the group's own node space, which is where a segment draws from.
		var points = new Vector3[group.Points.Length];
		var localPoints = new Vector3[group.Points.Length];
		for (int i = 0; i < group.Points.Length; i++) {
			var point = group.Points[i];
			localPoints[i] = WorldScale.DtsToRender(point.X, point.Y, point.Z);
			points[i] = WorldScale.DtsToRender(
				point.X + offset.X,
				point.Y + offset.Y,
				point.Z + offset.Z);
		}

		foreach (var polyObject in group.Polys) {
			// Two vertices is a line, not a degenerate face: retail shapes carry TSSolidPolys with
			// VertexCount 2 whose whole contribution is a one-pixel run in the surface's line colour
			// — 92 of them in MECHWPNS.DTS alone, which is what draws the struts between a Particle
			// Beam Weapon's housing and its barrel. The fan below emits nothing for them (it runs
			// VertexCount - 2 times) and the outline pass emits their single segment. One vertex is a
			// single pixel, by the same rule: see AppendPolySide.
			//
			// A plain TSPoly — the exact base type — carries no colour field of any kind: the surface
			// index lives on TSSolidPoly, which the three flat renderers the original ships
			// (TSSolidPoly_Render 00474db4, TSShadedPoly_Render 0047542c, TSTexture4Poly_Render
			// 00474e9c) all resolve their fill through. There is no TSPoly_Render, and nothing for one
			// to fill with, so the base class's render slot draws nothing and neither does this.
			//
			// It is not a curiosity: the blank third cell of every body part of every retail chassis
			// is exactly one of these, and it is what a destroyed component is stepped to. Emitting it
			// would leave a grey shard standing where the part came off.
			if (polyObject is not TSPoly poly || poly.VertexCount < 1
					|| polyObject.GetType() == typeof(TSPoly)) {
				continue;
			}

			int listStart = poly.VertexList;
			if (listStart < 0 || listStart + poly.VertexCount > group.Indexes.Length) {
				continue;
			}

			int firstIndex = group.Indexes[listStart];
			if (firstIndex < 0 || firstIndex >= points.Length) {
				continue;
			}

			// The front/back decision is the poly's own stored normal against its own stored centre,
			// both point indices; a centre that does not resolve falls back to the first corner.
			int centerIndex = poly.Center >= 0 && poly.Center < points.Length ? poly.Center : firstIndex;
			var face = new PolyFace(ResolveFaceNormal(poly, group), points[centerIndex], localPoints[centerIndex]);
			var surface = SurfaceOf(poly, group.Surfaces);

			// Each side resolves its own surface pair, and either can be "do not draw". A poly whose
			// two sides come out alike — every two-sided poly in the retail files — goes up once and
			// draws from both; otherwise once per side it draws. See ResolveSide.
			SideLook? front = ResolveSide(polyObject, surface, true, shading);
			SideLook? back = ResolveSide(polyObject, surface, false, shading);
			if (front == back) {
				if (front is { } both) {
					AppendPolySide(polyObject, poly, both, 0, face, group, points, localPoints, sink, atlas);
				}
			} else {
				if (front is { } frontLook) {
					AppendPolySide(polyObject, poly, frontLook, 1, face, group, points, localPoints, sink, atlas);
				}

				if (back is { } backLook) {
					AppendPolySide(polyObject, poly, backLook, -1, face, group, points, localPoints, sink, atlas);
				}
			}
		}
	}

	/// <summary>
	/// The surface record a poly names, or null when its index is out of range — see
	/// <see cref="ResolveFrame"/> for the <c>/ 4</c>.
	/// </summary>
	private static TSSurfaceEntry? SurfaceOf(TSPoly poly, TSSurfaceEntry[]? surfaces) {
		if (surfaces == null || poly is not TSSolidPoly solid) {
			return null;
		}

		int index = solid.ColorIndexId / 4;
		return index >= 0 && index < surfaces.Length ? surfaces[index] : null;
	}

	/// <summary>
	/// What one side of a poly draws — the values <see cref="ResolveSide"/> takes from that side's
	/// surface pair, compared whole to tell whether the two sides draw alike.
	/// </summary>
	/// <param name="Frame">A <see cref="TSTexture4Poly"/>'s <c>.DBA</c> frame index, or -1.</param>
	/// <param name="Solid">A plain <see cref="TSSolidPoly"/>'s two colours — see <see cref="ResolveSolidColors"/>.</param>
	/// <param name="ShadeRamp">A lit flat poly's material ramp — see <see cref="ResolveShadeRamp"/>.</param>
	/// <param name="LitAsBack">
	/// Whether this side is lit with the poly's normals negated even though the poly faces the eye: a
	/// texture poly whose front value is <c>-1</c> draws its back frame from the front too, lit as the
	/// back. Only ever set on a front side.
	/// </param>
	/// <param name="LineRamp">
	/// A <see cref="TSShadedPoly"/>'s line entry as a material ramp, or -1 when it names the fill's own
	/// ramp and so can never resolve apart from it — see <see cref="MeshVertex.OutlineFillRamp"/>.
	/// </param>
	private readonly record struct SideLook(int Frame, SolidColors? Solid, int ShadeRamp, bool LitAsBack,
		int LineRamp = -1);

	/// <summary>
	/// What one side of a poly draws, or null when that side draws nothing — the original's per-poly
	/// choice of surface pair after <c>TSPoly_FrontBackVisibilityTest</c> (<c>0048c620</c>), one
	/// side at a time so that the shader can make the choice per frame (see <see cref="MeshVertex.Side"/>).
	/// docs/retail/formats/dts-texture-binding.md, "Poly types and their colour mechanisms", has the rules:
	/// <list type="bullet">
	/// <item>The flat types (<c>TSSolidPoly_Render</c> <c>00474db4</c>, <c>TSShadedPoly_Render</c>
	/// <c>0047542c</c>, <c>TSGouraudPoly_Render</c> <c>004755c8</c>) take the side's fill and line
	/// entries, and draw nothing when both carry <c>0x14</c> in their flag's high byte.</item>
	/// <item><c>TSTexture4Poly_Render</c> (<c>00474e9c</c>) takes the side's fill value as a frame,
	/// draws nothing for a back value of <c>-1</c>, and draws a front value of <c>-1</c> as the
	/// back.</item>
	/// </list>
	/// A poly with no surface record resolves alike on both sides, to the fallback colour.
	/// </summary>
	private static SideLook? ResolveSide(TSObject polyObject, TSSurfaceEntry? surface, bool front,
			SurfaceShading? shading) {
		if (surface == null) {
			return new SideLook(-1, null, -1, false);
		}

		if (polyObject is TSTexture4Poly) {
			bool frontMissing = surface.FrontColor == -1 && surface.FrontFlag == -1;
			if (front && !frontMissing) {
				return new SideLook(surface.FrontColor, null, -1, false);
			}

			return surface.BackColor == -1 && surface.BackFlag == -1
				? null
				: new SideLook(surface.BackColor, null, -1, LitAsBack: front);
		}

		var (value, flag, line, lineFlag) = front
			? (surface.FrontColor, surface.FrontFlag, surface.FrontLineColor, surface.FrontLineFlag)
			: (surface.BackColor, surface.BackFlag, surface.BackLineColor, surface.BackLineFlag);
		if (IsDoNotDraw(flag) && IsDoNotDraw(lineFlag)) {
			return null;
		}

		// A plain TSSolidPoly — the exact type, not one of the three subclasses that inherit its
		// fields — is the one poly kind the original draws through the theater ramp instead of the
		// bound texture bank, and the one it does not light. See ResolveSolidColors.
		SolidColors? solid = polyObject.GetType() == typeof(TSSolidPoly)
			? ResolveSolidColors(value, flag, line, lineFlag, shading)
			: null;

		// The lit flat types name a material ramp rather than a colour, and the ramp is only
		// half a colour until the face's own light level picks a step along it — which happens
		// per instance, at draw time. See MeshVertex.ShadeRamp.
		int shadeRamp = IsRampShaded(polyObject) && shading is { HasShadeRamps: true }
			? ResolveShadeRamp(polyObject, value)
			: -1;

		// TSShadedPoly_Render resolves its line entry through the same two lookups as its fill, and
		// the outline pass draws it when the two bytes differ. Two entries naming one ramp always
		// resolve alike, so only a different ramp can outline. TSGouraudPoly's outline pass is not
		// ported: it tests the raw entries, and no retail Gouraud surface has a line apart from its
		// fill.
		int lineRamp = polyObject is TSShadedPoly && shadeRamp >= 0
			? ResolveShadeRamp(polyObject, line)
			: -1;

		return new SideLook(-1, solid, shadeRamp, false, lineRamp != shadeRamp ? lineRamp : -1);
	}

	/// <summary>
	/// Whether a surface entry's flag puts <c>0x14</c> in the high byte of the int32 it shares with
	/// its value — "do not draw this face", flag 5120 in every retail file that uses it.
	/// </summary>
	private static bool IsDoNotDraw(short flag) => ((ushort)flag >> 8) == 0x14;

	/// <summary>
	/// One side of one poly, as triangles, outline edges or a point under that <paramref name="side"/>
	/// — see <see cref="MeshVertex.Side"/>.
	/// </summary>
	private static void AppendPolySide(TSObject polyObject, TSPoly poly, SideLook look, int side,
			PolyFace face, TSGroup group, Vector3[] points, Vector3[] localPoints, Collector sink,
			TextureAtlas? atlas) {
		int listStart = poly.VertexList;
		int firstIndex = group.Indexes![listStart];

		// The name says quad, but the type also ships as a triangle — 40 of them across the
		// fleet, six on APOCA alone — and the original textures those too: see
		// docs/retail/formats/dts-texture-binding.md's "Three-vertex texture polys". A count outside
		// [3, 4] would run off the end of the exe's own 4-corner UV array, so it still falls
		// back rather than guessing; no retail shape has one.
		AtlasRect? rect = poly is TSTexture4Poly && poly.VertexCount is 3 or 4
			? ResolveFrame(look.Frame, atlas)
			: null;

		int rank = poly is TSTexture4Poly
			? (rect.HasValue ? Ranks.Textured : Ranks.UnresolvedTexture)
			: Ranks.FlatShaded;

		SolidColors? solid = look.Solid;
		Vector3[]? vertexNormals = ResolveVertexNormals(polyObject, group);

		// Every poly type that resolves at all has resolved by here: a textured one samples the
		// atlas (rank Textured, which ignores this colour), a plain solid one carries its ramped
		// fill, and a lit flat one gets its colour per fragment from its shade ramp. FallbackColor is
		// what is left — a lit flat poly in a theater whose palette has no shade-ramp table, which
		// no retail theater is. SceneModelLibrary warns when that happens.
		Vector3 color = solid?.Fill
			?? (rank == Ranks.UnresolvedTexture ? TextureFallbackColor : FallbackColor);
		Vector3 first = points[firstIndex];
		Vector3 localFirst = localPoints[firstIndex];
		int polyId = sink.NextPolyId();

		// A textured quad is mapped as a quad by the original, not as two triangles — see
		// QuadUvWeights. A textured triangle needs none of that: the affine map taking three
		// corners to three UVs is already the only one there is.
		float[]? quadWeights = rect.HasValue && poly.VertexCount == 4
			? QuadUvWeights(points, group.Indexes, listStart)
			: null;

		// Polys are convex fans, so a triangle fan from the first vertex reproduces them.
		for (int i = 0; i < poly.VertexCount - 2; i++) {
			int i1 = group.Indexes[listStart + 1 + i];
			int i2 = group.Indexes[listStart + 2 + i];
			if (i1 < 0 || i1 >= points.Length || i2 < 0 || i2 >= points.Length) {
				continue;
			}

			if (rect is { } frame) {
				// With weights the corner's UV goes to the GPU premultiplied by its own weight and
				// is divided back per fragment; without them (a quad too degenerate to solve) the
				// mapping stays affine per triangle, as it was.
				var weights = quadWeights == null
					? (1f, 1f, 1f)
					: (quadWeights[0], quadWeights[i + 1], quadWeights[i + 2]);

				// The original's back-face case swaps a quad's corners 1 and 3 in position and in
				// frame corner together, which reverses the winding and leaves each corner's UV where
				// it was, so the back draws through the same corner map as the front. What it does to
				// a three-vertex poly is Open in docs/retail/formats/dts-texture-binding.md.
				sink.Triangles.Add(new Triangle(first, points[i1], points[i2], color, rank, polyId,
					localFirst, localPoints[i1], localPoints[i2], group.Transform, sink.Gate,
					face, side, look.LitAsBack,
					UvAt(frame, 0) * weights.Item1,
					UvAt(frame, i + 1) * weights.Item2,
					UvAt(frame, i + 2) * weights.Item3,
					faceNormal: face.Normal,
					uvWeights: quadWeights == null ? default : weights) { Leaf = sink.Leaf });
			} else {
				// The fan's corners are vertex-list slots 0, i+1 and i+2, and the normal list is
				// parallel to it, so the same three slots index it.
				var corners = vertexNormals == null
					? ((Vector3, Vector3, Vector3)?)null
					: (vertexNormals[0], vertexNormals[i + 1], vertexNormals[i + 2]);

				sink.Triangles.Add(new Triangle(first, points[i1], points[i2], color, rank, polyId,
					localFirst, localPoints[i1], localPoints[i2], group.Transform, sink.Gate,
					face, side, look.LitAsBack,
					unlit: solid.HasValue, shadeRamp: look.ShadeRamp, vertexNormals: corners,
					faceNormal: face.Normal,
					solidPaletteIndex: solid?.FillIndex ?? -1) { Leaf = sink.Leaf });
			}
		}

		// The original's second pass over the same poly: its whole edge loop, re-drawn in the
		// side's line colour, whenever that resolves to something other than the fill. See
		// MeshBuild.
		// A line or point poly has nothing to be an outline OVER: Raster_DrawPolygonDispatch
		// (00483dac) draws a ring of fewer than three points as a line or a pixel in whatever colour
		// the pass carries, so the fill pass itself draws it in the fill, and the outline pass then
		// redraws it in the line colour when that differs. What ends on screen is the line entry when
		// it resolves apart from the fill, and the fill when it does not.
		bool standalone = poly.VertexCount <= 2;
		Vector3? edgeColor = standalone ? solid?.Line ?? solid?.Fill : solid?.Line;
		int edgeIndex = standalone && solid is { Line: null }
			? solid?.FillIndex ?? -1
			: solid?.LineIndex ?? -1;

		// A shaded poly's outline: its colour and whether it shows depend on the face's light, so the
		// edge loop goes up with both ramps and the shader decides per fragment.
		bool shadedOutline = look.LineRamp >= 0 && poly.VertexCount >= 3;
		if (shadedOutline) {
			edgeColor = FallbackColor;
		}

		if (edgeColor is not { } lineColor) {
			return;
		}

		if (poly.VertexCount == 1) {
			sink.Points.Add(new OutlineEdge(first, first, localFirst, localFirst, lineColor,
				group.Transform, sink.Gate, polyId, face, side, standalone: true,
				solidPaletteIndex: edgeIndex) { Leaf = sink.Leaf });
			return;
		}

		// A line poly's edge loop would run 0->1 and then 1->0, the same segment drawn twice,
		// so it contributes one edge instead of VertexCount of them.
		int edgeCount = poly.VertexCount == 2 ? 1 : poly.VertexCount;

		for (int i = 0; i < edgeCount; i++) {
			int from = group.Indexes[listStart + i];
			int to = group.Indexes[listStart + (i + 1) % poly.VertexCount];
			if (from < 0 || from >= points.Length || to < 0 || to >= points.Length) {
				continue;
			}

			// Whichever entry supplied lineColor above supplied its index too, so the outline
			// resolves through the same table its fill does.
			sink.Outlines.Add(new OutlineEdge(points[from], points[to],
				localPoints[from], localPoints[to], lineColor, group.Transform, sink.Gate, polyId,
				face, side, standalone: standalone,
				solidPaletteIndex: edgeIndex,
				shadeRamp: shadedOutline ? look.LineRamp : -1,
				outlineFillRamp: shadedOutline ? look.ShadeRamp : -1) { Leaf = sink.Leaf });
		}
	}

	/// <summary>
	/// The four corners' homogeneous UV weights for a textured quad, or null when the quad is too
	/// degenerate to solve (its diagonals do not cross inside it, or one of them has no length).
	///
	/// <para>With the quad's diagonals crossing at fraction <c>s</c> along <c>p0→p2</c> and <c>t</c>
	/// along <c>p1→p3</c>, the corners take <c>1/(1-s), 1/(1-t), 1/s, 1/t</c>.</para>
	///
	/// <para>Why: the original hands its rasterizer the whole quad with the frame rect's corners on
	/// the poly's corners (docs/retail/formats/dts-texture-binding.md, "Render path and UV generation"), so
	/// one map covers the face. A GPU splits the quad, and two triangles with plain UVs each get
	/// their own affine map; the two agree only on a parallelogram, and everywhere else the texture
	/// kinks along the shared diagonal — plainly on base type 37's trapezoidal pyramid faces. The map
	/// taking a quad's corners to a rect's corners is projective, and interpolating
	/// <c>(u·w, v·w)</c> against <c>w</c> and dividing per fragment produces exactly that. A
	/// parallelogram gives <c>s = t = ½</c> and equal weights, the affine map this replaces, so a quad
	/// that was already right is untouched; a degenerate or non-convex quad stays affine rather than
	/// being guessed at. Retail's own fill is neither this map nor the GPU's perspective-correct
	/// one: it steps u and v linearly in screen space down the quad's edges and across each row
	/// (docs/retail/formats/dts-texture-binding.md, "Screen-linear and perspective-correct fills"). That
	/// is free of the kink too, but differs from this map wherever the quad's depth varies across
	/// it; KNOWN_ISSUES.md lists the divergence.</para>
	///
	/// <para>The crossing is solved as a least-squares one rather than a planar intersection because
	/// a DTS quad is not guaranteed to be planar.</para>
	/// </summary>
	private static float[]? QuadUvWeights(Vector3[] points, short[] indexes, int listStart) {
		var corner = new Vector3[4];
		for (int i = 0; i < 4; i++) {
			int index = indexes[listStart + i];
			if (index < 0 || index >= points.Length) {
				return null;
			}
			corner[i] = points[index];
		}

		// p0 + s·d = p1 + (1-t)·(p1 - p3)  rearranged as  s·d + t·e = f.
		Vector3 d = corner[2] - corner[0];
		Vector3 e = corner[1] - corner[3];
		Vector3 f = corner[1] - corner[0];

		float dd = Vector3.Dot(d, d);
		float de = Vector3.Dot(d, e);
		float ee = Vector3.Dot(e, e);
		float determinant = dd * ee - de * de;
		if (MathF.Abs(determinant) < 1e-9f) {
			return null;
		}

		float df = Vector3.Dot(d, f);
		float ef = Vector3.Dot(e, f);
		float s = (df * ee - ef * de) / determinant;
		float t = (dd * ef - de * df) / determinant;

		// Outside that range the diagonals meet beyond the quad — a non-convex or self-crossing poly,
		// which has no projective map onto the rect. Left affine rather than guessed at.
		const float margin = 1e-3f;
		if (s <= margin || s >= 1f - margin || t <= margin || t >= 1f - margin) {
			return null;
		}

		return new[] { 1f / (1f - s), 1f / (1f - t), 1f / s, 1f / t };
	}

	/// <summary>
	/// Maps one of the four RE-confirmed corners (<see cref="QuadCorners"/>) onto a frame's rect
	/// inside the atlas.
	/// </summary>
	private static Vector2 UvAt(AtlasRect frame, int corner) {
		Vector2 unit = QuadCorners[corner];
		return new Vector2(
			frame.U0 + unit.X * (frame.U1 - frame.U0),
			frame.V0 + unit.Y * (frame.V1 - frame.V0));
	}

	/// <summary>
	/// Resolves a textured poly's side to its frame in the atlas. The frame index is the side's value
	/// in <c>Surfaces[ColorIndexId / 4]</c> (<see cref="ResolveSide"/>) — the <c>/ 4</c> because
	/// <c>ColorIndexId</c> is stored on disk as <c>surfaceIndex * 4</c> rather than a plain surface
	/// index, confirmed two independent ways: from VSHELL's own texture-poly render code, and from the
	/// DTS reader's <c>colorCount / 4</c> read convention. Every other poly type indexes its surface
	/// the same way (<see cref="ResolveShadeRamp"/>, <see cref="ResolveSolidColors"/>) — it is what
	/// the value <i>means</i> that differs.
	/// </summary>
	private static AtlasRect? ResolveFrame(int frame, TextureAtlas? atlas) => atlas?.Frame(frame);

	/// <summary>
	/// Whether a poly is one of the two <b>lit</b> flat types, whose surface value names a material
	/// ramp in the theater palette.
	///
	/// <para><see cref="TSGouraudPoly"/> names its ramp in the same field as
	/// <see cref="TSShadedPoly"/>: in <c>BASES.DGS</c> a shape's Gouraud and shaded polys share
	/// surface records and values (shape 5's groups mix both against ramps 0 and 12). They differ in
	/// how the ramp entry is spent — see <see cref="SurfaceShading.GouraudColor"/> — and in per-vertex
	/// versus per-face light, which <see cref="ResolveVertexNormals"/> supplies.</para>
	///
	/// <para>Excluded: <see cref="TSTexture4Poly"/>, also a <c>TSSolidPoly</c> subclass but whose
	/// value is a frame index (<see cref="ResolveFrame"/>), and plain <c>TSSolidPoly</c>, whose value
	/// is a palette index and which is never lit (<see cref="ResolveSolidColors"/>).</para>
	/// </summary>
	private static bool IsRampShaded(TSObject poly) =>
		poly is TSShadedPoly or TSGouraudPoly;

	/// <summary>
	/// A poly's own stored normal, in render space, or null when its index does not resolve.
	///
	/// <para><see cref="TSPoly.Normal"/> is a <b>point index</b>, dereferenced with the same 6-byte
	/// Vec3Short stride as a corner — <c>*(ushort *)(poly + 4) * 6 + DAT_006c696c</c> in every one of
	/// <c>TSSolidPoly_Render</c>, <c>TSShadedPoly_Render</c> and <c>TSTexture4Poly_Render</c>. It is
	/// what <c>TSPoly_FrontBackVisibilityTest</c> (<c>0048c620</c>) tests with, so it is what the
	/// front/back sign has to be derived from here.</para>
	///
	/// <para>It is <i>not</i> interchangeable with the winding — see <see cref="EmitTriangle"/> for
	/// the measurement and for what using the winding instead did to Gouraud polys.</para>
	/// </summary>
	private static Vector3? ResolveFaceNormal(TSPoly poly, TSGroup group) {
		if (group.Points == null || poly.Normal < 0 || poly.Normal >= group.Points.Length) {
			return null;
		}

		var point = group.Points[poly.Normal];
		var rendered = WorldScale.DtsToRender(point.X, point.Y, point.Z);
		return rendered.LengthSquared() > 1e-12f ? Vector3.Normalize(rendered) : null;
	}

	/// <summary>
	/// A <see cref="TSGouraudPoly"/>'s own per-corner normals, in render space, or null for any other
	/// poly — which is what makes it Gouraud rather than flat.
	///
	/// <para><b>The shape stores normals as extra entries in its point list.</b> Every poly carries
	/// <see cref="TSPoly.Normal"/>, a <i>point index</i> that DBSIM's renderers dereference as
	/// <c>points[index]</c> with the 6-byte Vec3Short stride (<c>*(ushort *)(poly + 4) * 6 +
	/// DAT_006c696c</c>, in all three of <c>TSSolidPoly_Render</c>, <c>TSShadedPoly_Render</c> and
	/// <c>TSTexture4Poly_Render</c>). <c>TSGouraudPoly.NormalList</c> is the per-vertex form of the
	/// same thing: an offset into the group's index array running parallel to
	/// <see cref="TSPoly.VertexList"/>, whose entries are point indices of normals rather than of
	/// corners.</para>
	///
	/// <para>Confirmed on <c>BASES.DGS</c> shape 11's eight side panels: every entry the list reaches
	/// has length <b>2048</b> — <see cref="MissionSun.NormalLength"/>, the <c>0x800</c> the shade
	/// calculation is scaled around — and adjacent panels share the normal at the edge between them,
	/// which is what wraps a smooth gradient around the mass instead of stepping it.</para>
	///
	/// <para>Returns null rather than a partial set if any entry is out of range, so a malformed list
	/// falls back to flat shading instead of half-smooth shading.</para>
	/// </summary>
	private static Vector3[]? ResolveVertexNormals(TSObject polyObject, TSGroup group) {
		if (polyObject is not TSGouraudPoly gouraud || group.Indexes == null || group.Points == null) {
			return null;
		}

		var normals = new Vector3[gouraud.VertexCount];
		for (int i = 0; i < normals.Length; i++) {
			int at = gouraud.NormalList + i;
			if (at < 0 || at >= group.Indexes.Length) {
				return null;
			}

			int pointIndex = group.Indexes[at];
			if (pointIndex < 0 || pointIndex >= group.Points.Length) {
				return null;
			}

			var normal = group.Points[pointIndex];
			var rendered = WorldScale.DtsToRender(normal.X, normal.Y, normal.Z);
			if (rendered.LengthSquared() <= 1e-12f) {
				return null;
			}

			normals[i] = Vector3.Normalize(rendered);
		}

		return normals;
	}

	/// <summary>
	/// The material ramp a lit flat surface names, or -1 when it names none.
	///
	/// <para>The value is the side's fill entry in <c>Surfaces[ColorIndexId / 4]</c>, the same field
	/// and the same <c>/ 4</c> every other poly type reads (see <see cref="ResolveFrame"/>) — it is
	/// what the value <i>means</i> that differs. <c>TSShadedPoly_Render</c> hands it to
	/// <c>Palette_ShadeRampLookup</c>, which treats its low byte as a slot in the palette's own ramp
	/// table; see <see cref="SurfaceShading.ShadedColor"/>.</para>
	///
	/// <para>Unlike the solid path this does <b>not</b> reject a nonzero flag: every shaded surface in
	/// the retail files carries flag 1024 on its front pair, and the lookup masks the flag off.</para>
	/// </summary>
	private static int ResolveShadeRamp(TSObject polyObject, short ramp) {
		if (ramp < 0) {
			return -1;
		}

		// The two lit types spend the ramp differently — TSShadedPoly through the theater .RMP at a
		// fixed row, TSGouraudPoly straight through the palette — so they address different halves of
		// the lookup table. See SurfaceShading.GouraudColor.
		return (ramp & 0xff) + (polyObject is TSGouraudPoly ? SurfaceRampTable.GouraudRowOffset : 0);
	}

	/// <summary>
	/// The two colours of a plain <see cref="TSSolidPoly"/>'s side, both <b>palette indices run
	/// through the theater's own ramp at the fixed unlit shade</b> — never lit, whichever way the face
	/// points:
	/// <code>
	/// fill = rampRow(0x80)[value];   line = rampRow(0x80)[line];
	/// </code>
	/// <para>and the outline is drawn only when the two <b>ramped</b> bytes differ, so two palette
	/// indices that resolve to the same output draw no outline. <c>TSSolidPoly_Render</c>
	/// (<c>00474db4</c>) is traced in docs/retail/formats/dts-texture-binding.md's "<c>TSSolidPoly</c> —
	/// palette index, unlit, fill plus outline"; <see cref="Content.ShadeRamp"/> is the table.</para>
	///
	/// <para>This is the plain type only. Its lit siblings <c>TSShadedPoly</c> and
	/// <c>TSGouraudPoly</c>, which are almost every surface of a HERC or a building, go through
	/// <see cref="ResolveShadeRamp"/> and the renderer's own lighting instead.</para>
	///
	/// <para>Returns null when there is no ramp loaded or when the fill entry carries a nonzero flag —
	/// the flag occupies the high half of the same int32 the original indexes the ramp row with, so a
	/// value that has one is not a plain colour and is left to the fallback. Every retail plain solid
	/// surface carries flag 0 on each pair it draws.</para>
	/// </summary>
	private static SolidColors? ResolveSolidColors(short value, short flag, short lineValue, short lineFlag,
			SurfaceShading? shading) {
		if (shading == null || flag != 0 || value < 0) {
			return null;
		}

		if (shading.Ramp.Resolve(value, ShadeRamp.UnlitShade, shading.Palette) is not { } fill) {
			return null;
		}

		int lineIndex = -1;

		// The line colour is guarded exactly as the fill is — a nonzero flag means the entry is not a
		// plain colour. Past that, the original's test is on the ramp's output, so this one is too.
		Vector3? line = null;
		if (lineFlag == 0 && lineValue >= 0
			&& shading.Ramp.Lookup(lineValue, ShadeRamp.UnlitShade)
				!= shading.Ramp.Lookup(value, ShadeRamp.UnlitShade)) {
			line = shading.Ramp.Resolve(lineValue, ShadeRamp.UnlitShade, shading.Palette);
			lineIndex = lineValue;
		}

		return new SolidColors(fill, line, value, lineIndex);
	}

	/// <summary>
	/// A flat solid face's two resolved colours and the palette indices they came from — see
	/// <see cref="ResolveSolidColors"/>. <paramref name="Line"/> is null when the surface draws no
	/// outline. The indices travel to the GPU so the lookup can be redone there against whichever
	/// table the damage flash has bound; the colours remain the fallback for a theater with no
	/// palette ramp. See <see cref="Gl.MeshVertex.SolidPaletteIndex"/>.
	/// </summary>
	private readonly record struct SolidColors(Vector3 Fill, Vector3? Line, int FillIndex, int LineIndex);
}
