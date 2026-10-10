using System.Drawing;
using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Data.File.Dts.Bsp;
using HercWorks.Core.Data.File.Dts.Part;
using HercWorks.Core.Data.File.Dts.Poly;
using HercWorks.Core.Data.File.Dyn;

namespace HercWorks.UI;

/// <summary>
/// A single decoded DBA frame's pixels, pre-unpacked to ARGB for fast per-pixel sampling in the
/// rasterizer. A texel with alpha 0 is a cutout and draws nothing.
/// </summary>
public readonly struct DtsTexture {
	public int[] Pixels { get; }
	public int Width { get; }
	public int Height { get; }

	public DtsTexture(int[] pixels, int width, int height) {
		Pixels = pixels;
		Width = width;
		Height = height;
	}
}

/// <summary>
/// A <c>TSGouraudPoly</c> triangle's fill: the palette index each corner's light picks from the
/// material ramp, and the palette the bands between them are drawn in
/// (<see cref="ShapeShading.BandPalette"/>).
/// </summary>
public readonly record struct DtsContour(int IndexA, int IndexB, int IndexC, int[] Palette);

/// <summary>
/// What one side of a poly draws: a flat colour, a texture, or contour bands. <see cref="Lit"/> says
/// the colours are already the original's final ones; a side built without a palette carries a
/// placeholder the viewer lights itself.
/// </summary>
public readonly record struct DtsFace(bool Draw, Color Color, DtsTexture? Texture, bool Lit, DtsContour? Contour = null) {
	public static DtsFace Flat(Color color, bool lit) => new(true, color, null, lit);
	public static DtsFace Contoured(DtsContour contour) => new(true, Color.Empty, null, true, contour);
	public static DtsFace Hidden { get; } = new(false, Color.Empty, null, true);
}

/// <summary>
/// A single triangle in render space with both of its poly's sides resolved. The rasterizer picks
/// one per frame the way <c>TSPoly_FrontBackVisibilityTest</c> does — by the poly's stored normal
/// against the eye — and draws the back only when it is not the format's "do not draw" pair.
/// </summary>
public readonly struct DtsTriangle {
	public Vector3 A { get; init; }
	public Vector3 B { get; init; }
	public Vector3 C { get; init; }

	/// <summary>The poly's stored normal in render space (unit), or its winding's opposite when unresolvable.</summary>
	public Vector3 FaceNormal { get; init; }

	/// <summary>
	/// The point the front/back test measures the eye from: the poly's first corner, the same for
	/// every triangle of the poly, so that a poly drawn as separate front and back copies shows
	/// exactly one of them.
	/// </summary>
	public Vector3 FacingPoint { get; init; }

	/// <summary>
	/// 1 or -1 for a copy that draws only its poly's front or back while the other side goes up as
	/// a copy of its own (a <c>TSGouraudPoly</c> drawing both sides), else 0. Keeps the two copies of
	/// a triangle out of each other's <see cref="DtsGeometryBuilder"/> coincident-twin bucket.
	/// </summary>
	public int SideCopy { get; init; }

	public DtsFace Front { get; init; }
	public DtsFace Back { get; init; }

	public Vector2 UvA { get; init; }
	public Vector2 UvB { get; init; }
	public Vector2 UvC { get; init; }

	/// <summary>
	/// Homogeneous weights for the three corners' UVs — one quad-wide projective map instead of two
	/// affine ones. (1, 1, 1) for a triangle or an unsolvable quad.
	/// </summary>
	public Vector3 UvWeights { get; init; }

	/// <summary>Coincident-twin preference: 2 textured, 1 a real flat colour, 0 a placeholder.</summary>
	public int Rank { get; init; }
}

/// <summary>A <c>TSSolidPoly</c>'s outline edge, or the whole of a two-vertex line poly.</summary>
public readonly struct DtsLine {
	public Vector3 A { get; init; }
	public Vector3 B { get; init; }
	public Vector3 FaceNormal { get; init; }
	public Color? Front { get; init; }
	public Color? Back { get; init; }
}

/// <summary>One top-level entry from DynamixThreeSpaceModel.Roots, flattened to triangles and lines.</summary>
public sealed class DtsRootMesh {
	public string Label { get; }
	public List<DtsTriangle> Triangles { get; }
	public List<DtsLine> Lines { get; }

	public DtsRootMesh(string label, List<DtsTriangle> triangles, List<DtsLine> lines) {
		Label = label;
		Triangles = triangles;
		Lines = lines;
	}
}

/// <summary>
/// Everything a build can colour with. All optional: a missing palette leaves placeholders, a
/// missing <c>.RMP</c> leaves the solid, shaded and textured chains unresolved (the Gouraud chain
/// needs only the palette). <see cref="TransparentIndex0"/> mirrors the engine, which decodes every
/// bank but the seven mech skins with palette index 0 as a cutout.
/// </summary>
public sealed record ShapeRenderContext(
	DynamixBitmapArray? TextureBank, DynamixPalette? Palette, TerrainRampFile? Ramp, bool TransparentIndex0);

/// <summary>
/// Extracts renderable geometry from a parsed DTS model tree, coloured the way DBSIM colours it:
/// docs/retail/rendering/dts-texture-binding.md's "Poly types and their colour mechanisms", through
/// <see cref="ShapeShading"/>.
///
/// <list type="bullet">
/// <item>plain <c>TSSolidPoly</c> — a palette index through the <c>.RMP</c> at the fixed unlit
/// shade, never lit, then outlined in its line colour when that ramps to a different byte; a
/// two-vertex one is a line and nothing else, a one-vertex one is not drawn;</item>
/// <item><c>TSShadedPoly</c> — a material ramp at the face's sun shade, then the <c>.RMP</c> at the
/// fixed row;</item>
/// <item><c>TSGouraudPoly</c> — the same ramp at each corner's own shade, giving a palette index per
/// corner, filled in contour bands between them straight through the palette (see
/// AppendGouraud);</item>
/// <item><c>TSTexture4Poly</c> — a <c>.DBA</c> frame, lit per texel through the <c>.RMP</c> row the
/// face's shade picks, a quad mapped projectively.</item>
/// </list>
///
/// <para>Both sides of every poly are resolved: the back pair (<c>BackColor</c>/<c>BackLineColor</c>)
/// at the negated normal's shade, and a pair whose two values both carry flag <c>0x14xx</c> — the
/// format's back-face culling — is not drawn. Normals are the stored point-list ones, which oppose
/// the corner winding.</para>
///
/// <para>Not reproduced: <c>TSBitmapPart</c> billboards (<c>docs/retail/rendering/dts-billboards.md</c>), the
/// <c>TSBSPPart</c> tree walk (every child is shown, including the few retail children no node
/// reaches and DBSIM never draws — docs/retail/rendering/dts-texture-binding.md's "<c>TSBSPPart</c> child
/// selection"), and distance fog. <c>TSCellAnimPart</c> shows its first cell.</para>
///
/// <para>Multi-part placement is the translation-only transform chain: no retail shape's rest pose
/// carries a rotation (docs/retail/rendering/dts-node-posing.md). Each root is an independent top-level
/// object; the real in-file LOD mechanism is <c>TSDetailPart</c>, one level down (see
/// CollectDetailLevel).</para>
/// </summary>
public static class DtsGeometryBuilder {
	// TSGroup point/translation shorts are fixed-point tenths (see Vec3Short's FormatFixedPoint).
	private const float Unit = 1f / 10f;
	private const int MaxTransformChainSteps = 64;
	private static readonly Color TextureFallbackColor = Color.FromArgb(255, 120, 150, 190);
	private static readonly Color FlatFallbackColor = Color.Gainsboro;

	/// <summary>The top byte a surface value's flag carries for "do not draw this face" — flag 5120.</summary>
	private const int HiddenFlagHighByte = 0x14;

	// Vertex-order UV corners for a TSTexture4Poly — top-left/top-right/bottom-right/bottom-left, per
	// docs/retail/rendering/dts-texture-binding.md's "Render path and UV generation". A three-vertex poly takes
	// the first three.
	private static readonly Vector2[] QuadUvCorners = {
		new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)
	};

	/// <summary>
	/// Decodes DBA frames on demand for one build, lit and unlit, cached per (frame, ramp row).
	/// </summary>
	private sealed class TextureContext {
		private readonly DynamixBitmapArray _bank;
		private readonly DynamixPalette? _palette;
		private readonly ShapeShading? _shading;
		private readonly bool _transparentIndex0;
		private readonly Dictionary<(int Frame, int Row), DtsTexture?> _cache = new();

		public TextureContext(DynamixBitmapArray bank, DynamixPalette? palette, ShapeShading? shading, bool transparentIndex0) {
			_bank = bank;
			_palette = palette;
			_shading = shading;
			_transparentIndex0 = transparentIndex0;
		}

		/// <summary>Whether frames come back lit through the ramp.</summary>
		public bool Lit => _shading is { HasRamp: true };

		public DtsTexture? Resolve(int frameIndex, int shade) {
			if (_bank.Images is not { } images || frameIndex < 0 || frameIndex >= images.Length) {
				return null;
			}

			int row = _shading?.RampRow(shade) ?? -1;
			if (_cache.TryGetValue((frameIndex, row), out var cached)) {
				return cached;
			}

			DtsTexture? texture = Decode(images[frameIndex], row);
			_cache[(frameIndex, row)] = texture;
			return texture;
		}

		private DtsTexture? Decode(DynamixBitmap frame, int row) {
			if (frame.Cols <= 0 || frame.Rows <= 0) {
				return null;
			}

			byte[] data = frame.ImageData ?? Array.Empty<byte>();
			var pixels = new int[frame.Cols * frame.Rows];
			for (int i = 0; i < pixels.Length && i < data.Length; i++) {
				byte index = data[i];
				if (_transparentIndex0 && index == 0) {
					continue;
				}

				Color? color = row >= 0 && _shading != null
					? _shading.TexelAtRow(index, row)
					: _palette != null && _palette.Colors.TryGetValue(index, out var entry)
						? Color.FromArgb(255, entry.GetColor().R, entry.GetColor().G, entry.GetColor().B)
						: Color.FromArgb(255, index, index, index);
				pixels[i] = (color ?? Color.Magenta).ToArgb();
			}

			return new DtsTexture(pixels, frame.Cols, frame.Rows);
		}
	}

	/// <summary>What one build carries down the tree.</summary>
	private sealed record BuildState(ShapeShading? Shading, TextureContext? Textures);

	public static List<DtsRootMesh> Build(DynamixThreeSpaceModel model) => Build(model, null);

	/// <summary>Builds every top-level root at its highest detail level.</summary>
	public static List<DtsRootMesh> Build(DynamixThreeSpaceModel model, ShapeRenderContext? context) {
		var roots = new List<DtsRootMesh>();
		if (model.Roots == null) {
			return roots;
		}

		var state = StateFor(context);
		int index = 0;
		foreach (var mesh in model.Roots) {
			string label = $"{mesh.Header?.Id() ?? mesh.GetType().Name} #{index}";
			roots.Add(BuildRootInternal(mesh, label, null, state));
			index++;
		}

		return roots;
	}

	/// <summary>
	/// Rebuilds a single top-level root — at a requested detail level (clamped per
	/// <c>TSDetailPart</c>), or at the highest when <paramref name="detailLevelIndex"/> is null.
	/// </summary>
	public static DtsRootMesh BuildRoot(TSObject root, string label, int? detailLevelIndex, ShapeRenderContext? context) =>
		BuildRootInternal(root, label, detailLevelIndex, StateFor(context));

	private static BuildState StateFor(ShapeRenderContext? context) {
		var shading = context?.Palette is { } palette ? new ShapeShading(palette, context.Ramp) : null;
		var textures = context?.TextureBank is { } bank
			? new TextureContext(bank, context.Palette, shading, context.TransparentIndex0)
			: null;
		return new BuildState(shading, textures);
	}

	/// <summary>
	/// Highest TSDetailPart.Parts.Length found anywhere in this root's tree (0 if none) — how many
	/// Detail Level choices to offer for this Part. TSCellAnimPart is only descended into at its
	/// first frame, matching what actually gets rendered (see CollectFirstFrame).
	/// </summary>
	public static int GetDetailLevelCount(TSObject root) {
		int max = 0;
		CountDetailLevels(root, ref max);
		return max;
	}

	private static void CountDetailLevels(TSObject? node, ref int max) {
		switch (node) {
			case null:
				return;

			case TSDetailPart detailPart:
				if (detailPart.Parts != null) {
					max = Math.Max(max, detailPart.Parts.Length);
					foreach (var part in detailPart.Parts) {
						CountDetailLevels(part, ref max);
					}
				}
				break;

			case TSCellAnimPart cellAnimPart:
				if (cellAnimPart.Parts is { Length: > 0 } frames) {
					CountDetailLevels(frames[0], ref max);
				}
				break;

			case TSPartList partList:
				if (partList.Parts != null) {
					foreach (var part in partList.Parts) {
						CountDetailLevels(part, ref max);
					}
				}
				break;
		}
	}

	private static DtsRootMesh BuildRootInternal(TSObject root, string label, int? detailLevelIndex, BuildState state) {
		var triangles = new List<DtsTriangle>();
		var lines = new List<DtsLine>();
		CollectGroups(root, null, triangles, lines, detailLevelIndex, state);
		return new DtsRootMesh(label, DropCoincidentTwins(triangles), lines);
	}

	/// <summary>
	/// Real DTS meshes stack a textured poly exactly on a flat-shaded twin — 186 pairs in SAMSON's
	/// first root — and the depth tie between them flickers. Keep one per coincident group by
	/// <see cref="DtsTriangle.Rank"/>: an in-range texture beats a real flat colour, which beats a
	/// placeholder. docs/retail/rendering/dts-texture-binding.md's "Coincident twins".
	/// </summary>
	private static List<DtsTriangle> DropCoincidentTwins(List<DtsTriangle> triangles) {
		var best = new Dictionary<(int, int, int, int, int, int, int), int>();
		var order = new List<(int, int, int, int, int, int, int)>();

		for (int i = 0; i < triangles.Count; i++) {
			var t = triangles[i];
			Vector3 centroid = (t.A + t.B + t.C) / 3f;
			Vector3 normal = Vector3.Cross(t.B - t.A, t.C - t.A);
			if (normal.LengthSquared() > 1e-8f) {
				normal = Vector3.Normalize(normal);
			}

			// Abs() on the normal so opposite-winding duplicates still land in the same bucket; coarse
			// rounding tolerates float noise from the transform chain.
			var key = (
				(int)MathF.Round(centroid.X * 4f), (int)MathF.Round(centroid.Y * 4f), (int)MathF.Round(centroid.Z * 4f),
				(int)MathF.Round(MathF.Abs(normal.X) * 100f), (int)MathF.Round(MathF.Abs(normal.Y) * 100f),
				(int)MathF.Round(MathF.Abs(normal.Z) * 100f), t.SideCopy);

			if (!best.TryGetValue(key, out int kept)) {
				best[key] = i;
				order.Add(key);
			} else if (t.Rank > triangles[kept].Rank) {
				best[key] = i;
			}
		}

		return order.Select(key => triangles[best[key]]).ToList();
	}

	/// <summary>
	/// DTS model space is Z-up; the viewer's camera is Y-up. A proper rotation (determinant +1), the
	/// same mapping the engine's <c>WorldScale.DtsToRender</c> uses.
	/// </summary>
	private static Vector3 ToRenderSpace(Vector3 dtsSpace) => new(dtsSpace.X, dtsSpace.Z, -dtsSpace.Y);

	public static (Vector3 Center, float Radius) ComputeBounds(IEnumerable<DtsTriangle> triangles) {
		Vector3 min = new(float.MaxValue);
		Vector3 max = new(float.MinValue);
		bool any = false;

		foreach (var tri in triangles) {
			min = Vector3.Min(min, Vector3.Min(tri.A, Vector3.Min(tri.B, tri.C)));
			max = Vector3.Max(max, Vector3.Max(tri.A, Vector3.Max(tri.B, tri.C)));
			any = true;
		}

		if (!any) {
			return (Vector3.Zero, 1f);
		}

		Vector3 center = (min + max) * 0.5f;
		float radius = Vector3.Distance(min, max) * 0.5f;
		return (center, MathF.Max(radius, 1f));
	}

	/// <summary>
	/// Recursively walks the TSObject tree looking for geometry-bearing TSGroup/TSBSPGroup nodes.
	/// Container types (TSPartList and its subtypes TSShape/ANShape/TSBSPPart/TSDetailPart/
	/// TSCellAnimPart) are walked into via Parts; ANShape additionally swaps in its own
	/// AnimationList for everything beneath it. TSBitmapPart is a billboard and is not built.
	/// </summary>
	private static void CollectGroups(TSObject? node, ANAnimList? animList, List<DtsTriangle> triangles,
			List<DtsLine> lines, int? detailLevelIndex, BuildState state) {
		switch (node) {
			case null:
				return;

			case ANShape anShape:
				CollectFromParts(anShape.Parts, anShape.AnimationList ?? animList, triangles, lines, detailLevelIndex, state);
				break;

			case TSDetailPart detailPart:
				CollectDetailLevel(detailPart, animList, triangles, lines, detailLevelIndex, state);
				break;

			case TSCellAnimPart cellAnimPart:
				if (cellAnimPart.Parts is { Length: > 0 } cells) {
					CollectGroups(cells[0], animList, triangles, lines, detailLevelIndex, state);
				}
				break;

			case TSGroup group:
				AppendGroup(group, animList, triangles, lines, state);
				break;

			case TSPartList partList:
				CollectFromParts(partList.Parts, animList, triangles, lines, detailLevelIndex, state);
				break;
		}
	}

	private static void CollectFromParts(TSObject[]? parts, ANAnimList? animList, List<DtsTriangle> triangles,
			List<DtsLine> lines, int? detailLevelIndex, BuildState state) {
		if (parts == null) {
			return;
		}

		foreach (var part in parts) {
			CollectGroups(part, animList, triangles, lines, detailLevelIndex, state);
		}
	}

	/// <summary>
	/// <c>TSDetailPart.Parts</c> is one piece of the shape at several levels of detail, index-aligned
	/// with the ascending <c>Details</c> thresholds, so part 0 is the coarsest
	/// (docs/retail/rendering/dts-texture-binding.md's "<c>TSDetailPart</c> level selection"). With no level
	/// requested the finest — the one paired with the largest threshold — is shown; a requested
	/// level is clamped to this part's own range.
	/// </summary>
	private static void CollectDetailLevel(TSDetailPart detailPart, ANAnimList? animList, List<DtsTriangle> triangles,
			List<DtsLine> lines, int? detailLevelIndex, BuildState state) {
		if (detailPart.Parts is not { Length: > 0 } parts) {
			return;
		}

		int chosenIndex = detailLevelIndex is int requested
			? Math.Clamp(requested, 0, parts.Length - 1)
			: FinestDetailIndex(detailPart, parts);

		CollectGroups(parts[chosenIndex], animList, triangles, lines, detailLevelIndex, state);
	}

	private static int FinestDetailIndex(TSDetailPart detailPart, TSObject[] parts) {
		if (detailPart.Details is not { Length: > 0 } details) {
			return parts.Length - 1;
		}

		int count = Math.Min(details.Length, parts.Length);
		int bestIndex = 0;
		for (int i = 1; i < count; i++) {
			if (details[i] > details[bestIndex]) {
				bestIndex = i;
			}
		}
		return bestIndex;
	}

	private static void AppendGroup(TSGroup group, ANAnimList? animList, List<DtsTriangle> triangles,
			List<DtsLine> lines, BuildState state) {
		if (group.Points == null || group.Indexes == null || group.Polys == null) {
			return;
		}

		Vector3 offset = ResolveGroupOffset(group, animList);
		var points = new Vector3[group.Points.Length];
		for (int i = 0; i < group.Points.Length; i++) {
			var p = group.Points[i];
			points[i] = ToRenderSpace(new Vector3(p.X, p.Y, p.Z) * Unit + offset);
		}

		foreach (var polyObject in group.Polys) {
			// A plain TSPoly carries no colour field and has no renderer of its own; the original draws
			// nothing for it (docs/retail/rendering/dts-texture-binding.md). One-vertex polys are single pixels
			// in the original and are left out here.
			if (polyObject is not TSPoly poly || poly.VertexCount < 2 || polyObject.GetType() == typeof(TSPoly)) {
				continue;
			}

			int listStart = poly.VertexList;
			if (listStart < 0 || listStart + poly.VertexCount > group.Indexes.Length) {
				continue;
			}

			var corners = new int[poly.VertexCount];
			bool valid = true;
			for (int i = 0; i < corners.Length; i++) {
				corners[i] = group.Indexes[listStart + i];
				valid &= corners[i] >= 0 && corners[i] < points.Length;
			}
			if (!valid) {
				continue;
			}

			Vector3 faceNormal = ResolveFaceNormal(poly, group, points, corners);
			TSSurfaceEntry? surface = SurfaceOf(poly, group.Surfaces);

			if (polyObject.GetType() == typeof(TSSolidPoly)) {
				AppendSolid(poly, surface, points, corners, faceNormal, triangles, lines, state);
			} else if (poly.VertexCount >= 3 && polyObject is TSGouraudPoly) {
				AppendGouraud(poly, surface, group, points, corners, faceNormal, triangles, state);
			} else if (poly.VertexCount >= 3) {
				AppendLitOrTextured(polyObject, poly, surface, points, corners, faceNormal, triangles, state);
			}
		}
	}

	private static TSSurfaceEntry? SurfaceOf(TSPoly poly, TSSurfaceEntry[]? surfaces) {
		if (poly is not TSSolidPoly solid || surfaces == null) {
			return null;
		}

		int index = solid.ColorIndexId / 4;
		return index >= 0 && index < surfaces.Length ? surfaces[index] : null;
	}

	/// <summary>
	/// One side of a surface: its value and flag, and its line value and flag. <c>Hidden</c> when both
	/// flags put <c>0x14</c> in their top byte.
	/// </summary>
	private readonly record struct SurfacePair(short Value, short Flag, short Line, short LineFlag) {
		public bool Hidden => ((ushort)Flag >> 8) == HiddenFlagHighByte && ((ushort)LineFlag >> 8) == HiddenFlagHighByte;

		public static SurfacePair Front(TSSurfaceEntry s) => new(s.FrontColor, s.FrontFlag, s.FrontLineColor, s.FrontLineFlag);
		public static SurfacePair Back(TSSurfaceEntry s) => new(s.BackColor, s.BackFlag, s.BackLineColor, s.BackLineFlag);
	}

	/// <summary>
	/// A plain <c>TSSolidPoly</c>: unlit fill plus outline, or a line alone for two vertices. A value
	/// carrying a flag other than the hidden one is not a plain colour and gets the placeholder, as
	/// the engine does.
	/// </summary>
	private static void AppendSolid(TSPoly poly, TSSurfaceEntry? surface, Vector3[] points, int[] corners,
			Vector3 faceNormal, List<DtsTriangle> triangles, List<DtsLine> lines, BuildState state) {
		(DtsFace Fill, Color? Line) Side(SurfacePair? pair) {
			if (pair is not { } p) {
				return (DtsFace.Flat(FlatFallbackColor, false), null);
			}
			if (p.Hidden) {
				return (DtsFace.Hidden, null);
			}
			if (p.Flag != 0 || p.Value < 0 || state.Shading?.Solid(p.Value) is not { } fill) {
				return (DtsFace.Flat(FlatFallbackColor, false), null);
			}

			Color? line = p.LineFlag == 0 && p.Line >= 0 && !state.Shading.SolidSame(p.Line, p.Value)
				? state.Shading.Solid(p.Line)
				: null;
			// A line poly has no fill for a matching outline to vanish into, so it draws its fill
			// colour when it names no distinct line, as the original's fill pass does — docs/retail/formats/
			// dts-texture-binding.md's "TSSolidPoly — palette index, unlit, fill plus outline".
			if (poly.VertexCount == 2) {
				line ??= fill;
			}
			return (DtsFace.Flat(fill, true), line);
		}

		var front = Side(surface is { } sf ? SurfacePair.Front(sf) : null);
		var back = Side(surface is { } sb ? SurfacePair.Back(sb) : null);

		for (int i = 0; i + 2 < corners.Length; i++) {
			triangles.Add(new DtsTriangle {
				A = points[corners[0]], B = points[corners[i + 1]], C = points[corners[i + 2]],
				FaceNormal = faceNormal, FacingPoint = points[corners[0]], Front = front.Fill, Back = back.Fill,
				UvWeights = Vector3.One, Rank = front.Fill.Lit ? 1 : 0
			});
		}

		if (front.Line == null && back.Line == null) {
			return;
		}

		int edges = corners.Length == 2 ? 1 : corners.Length;
		for (int i = 0; i < edges; i++) {
			lines.Add(new DtsLine {
				A = points[corners[i]], B = points[corners[(i + 1) % corners.Length]],
				FaceNormal = faceNormal, Front = front.Line, Back = back.Line
			});
		}
	}

	/// <summary>
	/// A <c>TSGouraudPoly</c>: each corner's shade picks a palette index from the side's material ramp,
	/// and each triangle of the fan is filled in contour bands between its corners' indices
	/// (docs/retail/rendering/dts-texture-binding.md, "<c>TSGouraudPoly</c> — same ramp number,
	/// per-vertex light, no <c>.RMP</c> row"; the viewer draws the bands). The fan runs from the first
	/// slot of the ring the side is filled from. The back's ring is the corners reversed with the
	/// corner normals left in list order, so slot <c>j</c> is corner <c>n-1-j</c> lit with corner
	/// <c>j</c>'s negated normal and the fan runs from the last corner. A poly that draws both sides
	/// therefore goes up as two copies, each drawn only from its own side.
	/// </summary>
	private static void AppendGouraud(TSPoly poly, TSSurfaceEntry? surface, TSGroup group, Vector3[] points,
			int[] corners, Vector3 faceNormal, List<DtsTriangle> triangles, BuildState state) {
		Vector3[]? cornerNormals = ResolveVertexNormals(poly, group);
		int count = corners.Length;

		// One side's palette index per ring slot, or, when the side does not band, what it draws whole.
		(int[]? Slots, DtsFace Whole) Side(SurfacePair? pair, float sign) {
			if (pair is not { } p) {
				return (null, DtsFace.Flat(FlatFallbackColor, false));
			}
			if (p.Hidden) {
				return (null, DtsFace.Hidden);
			}
			if (p.Value < 0 || state.Shading == null) {
				return (null, DtsFace.Flat(FlatFallbackColor, false));
			}

			int faceShade = ShapeShading.ShadeForFace(faceNormal * sign);
			var slots = new int[count];
			for (int j = 0; j < count; j++) {
				int shade = cornerNormals != null ? ShapeShading.ShadeForFace(cornerNormals[j] * sign) : faceShade;
				if (state.Shading.RampedPaletteIndex(p.Value, shade) is not { } index) {
					return (null, DtsFace.Flat(FlatFallbackColor, false));
				}
				slots[j] = index;
			}
			return (slots, default);
		}

		var front = Side(surface is { } sf ? SurfacePair.Front(sf) : null, 1f);
		var back = Side(surface is { } sb ? SurfacePair.Back(sb) : null, -1f);
		bool frontDraws = front.Slots != null || front.Whole.Draw;
		bool backDraws = back.Slots != null || back.Whole.Draw;

		void Emit((int[]? Slots, DtsFace Whole) side, bool isBack, int sideCopy) {
			int Ring(int slot) => corners[isBack ? count - 1 - slot : slot];

			for (int i = 0; i + 2 < count; i++) {
				DtsFace face = side.Slots is { } s
					? DtsFace.Contoured(new DtsContour(s[0], s[i + 1], s[i + 2], state.Shading!.BandPalette))
					: side.Whole;

				triangles.Add(new DtsTriangle {
					A = points[Ring(0)], B = points[Ring(i + 1)], C = points[Ring(i + 2)],
					FaceNormal = faceNormal, FacingPoint = points[corners[0]],
					Front = isBack ? DtsFace.Hidden : face, Back = isBack ? face : DtsFace.Hidden,
					SideCopy = sideCopy, UvWeights = Vector3.One, Rank = face.Lit ? 1 : 0
				});
			}
		}

		bool both = frontDraws && backDraws;
		if (frontDraws) {
			Emit(front, false, both ? 1 : 0);
		}
		if (backDraws) {
			Emit(back, true, both ? -1 : 0);
		}
	}

	/// <summary><c>TSShadedPoly</c> and <c>TSTexture4Poly</c>, both sides.</summary>
	private static void AppendLitOrTextured(TSObject polyObject, TSPoly poly, TSSurfaceEntry? surface,
			Vector3[] points, int[] corners, Vector3 faceNormal, List<DtsTriangle> triangles, BuildState state) {
		bool textured = poly is TSTexture4Poly && poly.VertexCount is 3 or 4;

		// The back is lit with its normal negated, as the original does once the visibility test
		// answers "back".
		DtsFace Face(SurfacePair? pair, float sign) {
			if (pair is not { } p) {
				return DtsFace.Flat(poly is TSTexture4Poly ? TextureFallbackColor : FlatFallbackColor, false);
			}
			if (p.Hidden) {
				return DtsFace.Hidden;
			}

			int faceShade = ShapeShading.ShadeForFace(faceNormal * sign);

			if (textured) {
				if (p.Value >= 0 && state.Textures?.Resolve(p.Value, faceShade) is { } texture) {
					return new DtsFace(true, Color.White, texture, state.Textures.Lit);
				}
				return DtsFace.Flat(TextureFallbackColor, false);
			}

			if (p.Value < 0 || state.Shading == null) {
				return DtsFace.Flat(FlatFallbackColor, false);
			}

			return polyObject is TSShadedPoly && state.Shading.Shaded(p.Value, faceShade) is { } shaded
				? DtsFace.Flat(shaded, true)
				: DtsFace.Flat(FlatFallbackColor, false);
		}

		float[]? quadWeights = textured && corners.Length == 4 ? QuadUvWeights(points, corners) : null;

		for (int i = 0; i + 2 < corners.Length; i++) {
			var front = Face(surface is { } sf ? SurfacePair.Front(sf) : null, 1f);
			var back = Face(surface is { } sb ? SurfacePair.Back(sb) : null, -1f);
			Vector3 weights = quadWeights == null
				? Vector3.One
				: new Vector3(quadWeights[0], quadWeights[i + 1], quadWeights[i + 2]);

			triangles.Add(new DtsTriangle {
				A = points[corners[0]], B = points[corners[i + 1]], C = points[corners[i + 2]],
				FaceNormal = faceNormal, FacingPoint = points[corners[0]], Front = front, Back = back,
				// Only a textured poly has UVs, and it has at most four corners; a lit poly can have more.
				UvA = textured ? QuadUvCorners[0] : default,
				UvB = textured ? QuadUvCorners[i + 1] : default,
				UvC = textured ? QuadUvCorners[i + 2] : default,
				UvWeights = weights,
				Rank = front.Texture != null ? 2 : front.Lit ? 1 : 0
			});
		}
	}

	/// <summary>
	/// The poly's stored normal — <see cref="TSPoly.Normal"/> is a point index into the same list as
	/// the corners (docs/retail/rendering/dts-texture-binding.md's "Normals live in the point list"). When it
	/// does not resolve, the winding stands in, negated, since the two are exactly opposed.
	/// </summary>
	private static Vector3 ResolveFaceNormal(TSPoly poly, TSGroup group, Vector3[] points, int[] corners) {
		if (group.Points != null && poly.Normal >= 0 && poly.Normal < group.Points.Length) {
			var p = group.Points[poly.Normal];
			var rendered = ToRenderSpace(new Vector3(p.X, p.Y, p.Z));
			if (rendered.LengthSquared() > 1e-12f) {
				return Vector3.Normalize(rendered);
			}
		}

		if (corners.Length >= 3) {
			var winding = Vector3.Cross(points[corners[1]] - points[corners[0]], points[corners[2]] - points[corners[0]]);
			if (winding.LengthSquared() > 1e-12f) {
				return -Vector3.Normalize(winding);
			}
		}

		return Vector3.UnitY;
	}

	/// <summary>
	/// A <c>TSGouraudPoly</c>'s per-corner normals — <c>NormalList</c> is an offset into the index
	/// array parallel to the vertex list, whose entries are point indices of normals. Null for any
	/// other poly, or if any entry is out of range (flat shading rather than half-smooth).
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

			var p = group.Points[pointIndex];
			var rendered = ToRenderSpace(new Vector3(p.X, p.Y, p.Z));
			if (rendered.LengthSquared() <= 1e-12f) {
				return null;
			}
			normals[i] = Vector3.Normalize(rendered);
		}

		return normals;
	}

	/// <summary>
	/// The four corners' homogeneous UV weights for a textured quad, or null when its diagonals do
	/// not cross inside it. With the crossing at fraction <c>s</c> along <c>p0→p2</c> and <c>t</c>
	/// along <c>p1→p3</c> the corners take <c>1/(1-s), 1/(1-t), 1/s, 1/t</c> — the engine's
	/// <c>DtsMeshBuilder.QuadUvWeights</c>. A quad fanned into two triangles carrying plain UVs maps each
	/// triangle affinely and independently; the two agree only on a parallelogram, and every other quad
	/// kinks along the diagonal, which retail's one-polygon fill does not
	/// (docs/retail/rendering/dts-texture-binding.md, "Screen-linear and perspective-correct fills").
	/// Solved least-squares, since a DTS quad need not be planar.
	/// </summary>
	private static float[]? QuadUvWeights(Vector3[] points, int[] corners) {
		Vector3 d = points[corners[2]] - points[corners[0]];
		Vector3 e = points[corners[1]] - points[corners[3]];
		Vector3 f = points[corners[1]] - points[corners[0]];

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

		const float margin = 1e-3f;
		if (s <= margin || s >= 1f - margin || t <= margin || t >= 1f - margin) {
			return null;
		}

		return new[] { 1f / (1f - s), 1f / (1f - t), 1f / s, 1f / t };
	}

	/// <summary>
	/// Walks the transform-id parent chain (group.Transform -> ANAnimList.Relations, a list of
	/// (parent, child) pairs) summing translations. No retail rest pose carries a rotation.
	/// </summary>
	private static Vector3 ResolveGroupOffset(TSGroup group, ANAnimList? animList) {
		if (animList?.Relations == null || animList.Transforms == null || animList.DefaultTransforms == null) {
			return Vector3.Zero;
		}

		var parentOf = new Dictionary<int, int>();
		foreach (var rel in animList.Relations) {
			parentOf[rel.Y] = rel.X;
		}

		Vector3 offset = Vector3.Zero;
		int tid = group.Transform;
		int steps = 0;

		while (tid != -1 && steps < MaxTransformChainSteps) {
			if (tid < 0 || tid >= animList.DefaultTransforms.Length) {
				break;
			}

			int transformIndex = animList.DefaultTransforms[tid];
			if (transformIndex < 0 || transformIndex >= animList.Transforms.Length) {
				break;
			}

			var translation = animList.Transforms[transformIndex].Translation;
			if (translation != null) {
				offset += new Vector3(translation.X, translation.Y, translation.Z) * Unit;
			}

			if (!parentOf.TryGetValue(tid, out int parentId)) {
				break;
			}
			tid = parentId;
			steps++;
		}

		return offset;
	}
}
