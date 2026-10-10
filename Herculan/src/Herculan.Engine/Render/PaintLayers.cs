using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// Where the original's painting of a shape's polys one after another has to be reproduced rather than
/// left to the depth test: a poly lying over an earlier one in the same plane — a decal such as the TDF
/// logo on a structure's wall, which is a quad of its own drawn after the wall — and the polys painted
/// after it.
///
/// <para>The original draws a group's polys in file order and a part list's children in order, with no
/// depth test between them, so a later poly covers an earlier one wherever the two overlap on screen
/// (docs/retail/rendering/dts-texture-binding.md, "Poly order within a group"). The depth buffer cannot
/// say which of two coplanar faces is in front, and some decals sit a little behind their wall's plane
/// over part of their area, so either would flicker or sink. Instead each <i>slot</i> — the children of
/// a <c>TSBSPPart</c> one by one, and the geometry outside every part as one more — has its paint order
/// cut into runs: a new run starts at each poly that lies over a poly of the run it would join. A
/// run is a paint layer, drawn as a piece of its own, and the renderer gives each layer of a slot its own
/// stencil value as it does each child of a part (<see cref="BspDrawGroup"/>), so a layer claims its
/// pixels from every earlier one. Within a layer the depth test decides.</para>
///
/// <para>The cut is this engine's: retail paints every poly over the ones before it, and only "lies
/// over" (<see cref="Overlays"/>) is tested here, because a stencil value and a draw per poly would cost
/// far more than the coplanar cases need. Polys never drawn together — two cells of one sequence, two
/// levels of one detail part, the two sides of one poly — never cut a run.</para>
/// </summary>
public sealed class PaintLayers {
	/// <summary>
	/// One triangle of one poly, as the mesh builder holds it at the rest pose.
	/// </summary>
	/// <param name="PolyId">The poly's place in the shape's paint order, unique per drawn side copy.</param>
	/// <param name="Slot">The <c>TSBSPPart</c> child it is drawn in, or null outside every part.</param>
	/// <param name="TransformId">The node that places it.</param>
	/// <param name="Gate">The cell and detail level it stands on.</param>
	/// <param name="Side">Which side of the poly this copy draws — see <see cref="Gl.MeshVertex.Side"/>.</param>
	/// <param name="Dependency">The other poly's facing it is drawn under, if any.</param>
	/// <param name="Normal">The poly's stored normal, or its winding's where none resolves.</param>
	internal readonly record struct Face(int PolyId, BspLeaf? Slot, int TransformId, CellGate Gate, int Side,
		FacingGate Dependency, Vector3 Normal, Vector3 A, Vector3 B, Vector3 C);

	/// <summary>
	/// How far, in DTS units, a later poly's corners may stand off an earlier poly's plane and still lie
	/// over it. The logo quads on <c>BASES.DGS</c> and <c>BASES_AN.DTS</c> stand off their walls by up to 47.3.
	/// </summary>
	private const float PlaneToleranceDts = 64f;

	/// <summary>The overlap, in DTS units, below which two faces only touch along an edge.</summary>
	private const float OverlapEpsilonDts = 1f;

	/// <summary>How closely two polys' normals must agree, as a cosine, to face the same way.</summary>
	private const float SameFacingCosine = 0.99f;

	private static readonly float RenderUnitsPerDtsUnit = WorldScale.WorldUnitsPerDtsUnit / WorldScale.WorldUnitsPerMeter;
	private static readonly float PlaneTolerance = PlaneToleranceDts * RenderUnitsPerDtsUnit;
	private static readonly float OverlapEpsilon = OverlapEpsilonDts * RenderUnitsPerDtsUnit;

	/// <summary>Per slot, the poly ids that start a run, ascending. A slot with one run is absent.</summary>
	private readonly Dictionary<BspLeaf, int[]> _runStarts;

	private PaintLayers(Dictionary<BspLeaf, int[]> runStarts) {
		_runStarts = runStarts;
	}

	/// <summary>Cuts every slot's paint order into runs — see the type's summary.</summary>
	internal static PaintLayers Assign(IEnumerable<Face> faces) {
		var runStarts = new Dictionary<BspLeaf, int[]>();
		foreach (var slot in faces.GroupBy(face => face.Slot ?? default)) {
			var polys = slot.GroupBy(face => face.PolyId).OrderBy(poly => poly.Key)
				.Select(poly => new Poly(poly.ToArray()));
			var run = new List<Poly>();
			var starts = new List<int>();
			foreach (var poly in polys) {
				if (run.Any(earlier => Overlays(poly, earlier))) {
					starts.Add(poly.Id);
					run.Clear();
				}

				run.Add(poly);
			}

			if (starts.Count > 0) {
				runStarts[slot.Key] = starts.ToArray();
			}
		}

		return new PaintLayers(runStarts);
	}

	/// <summary>Whether <paramref name="slot"/> is painted in more than one layer.</summary>
	public bool Layered(BspLeaf? slot) => _runStarts.ContainsKey(slot ?? default);

	/// <summary>
	/// The layer the poly <paramref name="polyId"/> is painted in, counted from 0: how many runs of its
	/// slot start at or before it. Any id has one, so an outline or a line poly, which cuts no run,
	/// takes the run it falls in.
	/// </summary>
	public int LayerOf(BspLeaf? slot, int polyId) {
		if (!_runStarts.TryGetValue(slot ?? default, out var starts)) {
			return 0;
		}

		int at = Array.BinarySearch(starts, polyId);
		return at >= 0 ? at + 1 : ~at;
	}

	/// <summary>One poly's triangles, with what <see cref="Overlays"/> reads off them.</summary>
	private sealed class Poly {
		public Poly(Face[] faces) {
			Faces = faces;
			var first = faces[0];
			Id = first.PolyId;
			Normal = first.Normal.LengthSquared() > 0f ? Vector3.Normalize(first.Normal) : Vector3.Zero;
			Min = new Vector3(float.MaxValue);
			Max = new Vector3(float.MinValue);
			foreach (var face in faces) {
				Min = Vector3.Min(Min, Vector3.Min(face.A, Vector3.Min(face.B, face.C)));
				Max = Vector3.Max(Max, Vector3.Max(face.A, Vector3.Max(face.B, face.C)));
			}
		}

		public Face[] Faces { get; }
		public int Id { get; }
		public Vector3 Normal { get; }
		public Vector3 Min { get; }
		public Vector3 Max { get; }
		public Face Head => Faces[0];
	}

	/// <summary>
	/// Whether <paramref name="later"/> lies over <paramref name="earlier"/>: drawn with it, placed by the
	/// same node, facing the same way, every corner of one of its triangles within
	/// <see cref="PlaneToleranceDts"/> of the plane of one of the earlier poly's, and the two triangles
	/// sharing area in that plane.
	/// </summary>
	private static bool Overlays(Poly later, Poly earlier) {
		if (later.Head.TransformId != earlier.Head.TransformId || NeverDrawnTogether(later.Head, earlier.Head)
				|| Vector3.Dot(later.Normal, earlier.Normal) < SameFacingCosine) {
			return false;
		}

		if (later.Max.X < earlier.Min.X - PlaneTolerance || later.Min.X > earlier.Max.X + PlaneTolerance
				|| later.Max.Y < earlier.Min.Y - PlaneTolerance || later.Min.Y > earlier.Max.Y + PlaneTolerance
				|| later.Max.Z < earlier.Min.Z - PlaneTolerance || later.Min.Z > earlier.Max.Z + PlaneTolerance) {
			return false;
		}

		foreach (var under in earlier.Faces) {
			var normal = Vector3.Cross(under.B - under.A, under.C - under.A);
			if (normal.LengthSquared() < 1e-12f) {
				continue;
			}

			normal = Vector3.Normalize(normal);
			foreach (var over in later.Faces) {
				if (MathF.Abs(Vector3.Dot(normal, over.A - under.A)) <= PlaneTolerance
						&& MathF.Abs(Vector3.Dot(normal, over.B - under.A)) <= PlaneTolerance
						&& MathF.Abs(Vector3.Dot(normal, over.C - under.A)) <= PlaneTolerance
						&& ShareArea(over, under, normal)) {
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Two cells of one sequence, two levels of one detail part, the two sides of one facing, or two
	/// copies gated on opposite facings of one other poly: never on screen together.
	/// </summary>
	private static bool NeverDrawnTogether(in Face a, in Face b) =>
		(a.Gate.Sequence >= 0 && a.Gate.Sequence == b.Gate.Sequence && a.Gate.Frame != b.Gate.Frame)
		|| (a.Gate.Detail != null && ReferenceEquals(a.Gate.Detail, b.Gate.Detail) && a.Gate.Level != b.Gate.Level)
		|| a.Side * b.Side < 0
		|| (a.Dependency.Side * b.Dependency.Side < 0 && a.Dependency.Normal == b.Dependency.Normal
			&& a.Dependency.Distance == b.Dependency.Distance);

	/// <summary>
	/// Whether two triangles, projected into the plane of <paramref name="normal"/>, share more than an
	/// edge: no axis of either triangle's edges separates them by less than
	/// <see cref="OverlapEpsilonDts"/> of overlap.
	/// </summary>
	private static bool ShareArea(in Face a, in Face b, Vector3 normal) {
		var u = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
		var v = Vector3.Cross(normal, u);
		Span<Vector2> first = stackalloc Vector2[3];
		Span<Vector2> second = stackalloc Vector2[3];
		Project(a, u, v, first);
		Project(b, u, v, second);
		return !Separated(first, second) && !Separated(second, first);
	}

	private static void Project(in Face face, Vector3 u, Vector3 v, Span<Vector2> into) {
		into[0] = new Vector2(Vector3.Dot(face.A, u), Vector3.Dot(face.A, v));
		into[1] = new Vector2(Vector3.Dot(face.B, u), Vector3.Dot(face.B, v));
		into[2] = new Vector2(Vector3.Dot(face.C, u), Vector3.Dot(face.C, v));
	}

	/// <summary>Whether an edge normal of <paramref name="edges"/> separates the two triangles.</summary>
	private static bool Separated(ReadOnlySpan<Vector2> edges, ReadOnlySpan<Vector2> other) {
		for (int i = 0; i < 3; i++) {
			var edge = edges[(i + 1) % 3] - edges[i];
			if (edge.LengthSquared() < 1e-12f) {
				continue;
			}

			var axis = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
			var (minA, maxA) = Extent(edges, axis);
			var (minB, maxB) = Extent(other, axis);
			if (MathF.Min(maxA, maxB) - MathF.Max(minA, minB) <= OverlapEpsilon) {
				return true;
			}
		}

		return false;
	}

	private static (float Min, float Max) Extent(ReadOnlySpan<Vector2> points, Vector2 axis) {
		float min = float.MaxValue;
		float max = float.MinValue;
		foreach (var point in points) {
			float along = Vector2.Dot(point, axis);
			min = MathF.Min(min, along);
			max = MathF.Max(max, along);
		}

		return (min, max);
	}
}
