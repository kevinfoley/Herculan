using System.Numerics;
using HercWorks.Core.Data.File.Dts.Bsp;
using HercWorks.Core.Data.File.Dts.Poly;
using Herculan.Engine.Gl;

namespace Herculan.Engine.Render;

/// <summary>
/// What the walk collected, turned into vertex arrays: the coincident-twin pass, the outlines that
/// survive it, and the three output shapes — one flat mesh, pieces by node and cell
/// (<see cref="MeshSegment"/>), and pieces by cell alone (<see cref="MeshCell"/>).
/// </summary>
public static partial class DtsMeshBuilder {
	private static MeshBuild Emit(Collector sink) {
		var kept = DropCoincidentTwins(sink.Triangles, leavesApart: false);
		var edges = SurvivingOutlines(kept, sink.Outlines);
		var points = sink.Points;

		int triangleVertices = kept.Count * 3;
		int lineVertices = edges.Count * 2;
		var vertices = new MeshVertex[triangleVertices + lineVertices + points.Count];

		for (int i = 0; i < kept.Count; i++) {
			EmitTriangle(kept[i], local: false, vertices, i * 3);
		}

		for (int i = 0; i < edges.Count; i++) {
			EmitEdge(edges[i], local: false, vertices, triangleVertices + i * 2);
		}

		for (int i = 0; i < points.Count; i++) {
			EmitPoint(points[i], local: false, vertices, triangleVertices + lineVertices + i);
		}

		return new MeshBuild(vertices, triangleVertices, points.Count);
	}

	/// <summary>
	/// The outline edges whose poly still has geometry after <see cref="DropCoincidentTwins"/>, each
	/// narrowed to the sides its poly's triangles still draw. An outline is a second pass over a poly
	/// the original has just filled, so it has no business outliving one that lost its tie, from
	/// either side.
	///
	/// <para>A <see cref="OutlineEdge.Standalone"/> edge is exempt: a line poly fills nothing, so
	/// there is no triangle for it to outlive and dropping it would discard the only thing that poly
	/// draws.</para>
	/// </summary>
	private static List<OutlineEdge> SurvivingOutlines(List<Triangle> kept, List<OutlineEdge> outlines) {
		if (outlines.Count == 0) {
			return outlines;
		}

		var drawn = new Dictionary<int, int>();
		foreach (var triangle in kept) {
			drawn[triangle.PolyId] = drawn.GetValueOrDefault(triangle.PolyId) | SideMask(triangle.Side);
		}

		var surviving = new List<OutlineEdge>(outlines.Count);
		foreach (var edge in outlines) {
			if (edge.Standalone) {
				surviving.Add(edge);
			} else if (drawn.TryGetValue(edge.PolyId, out int mask)) {
				surviving.Add(edge with { Side = SideOfMask(mask) });
			}
		}

		return surviving;
	}

	/// <summary>The sides a <see cref="Triangle.Side"/> draws, as bits: 1 the front, 2 the back.</summary>
	private static int SideMask(int side) => side switch { > 0 => 1, < 0 => 2, _ => 3 };

	/// <summary>The <see cref="Triangle.Side"/> that draws exactly the sides <paramref name="mask"/> names.</summary>
	private static int SideOfMask(int mask) => mask switch { 1 => 1, 2 => -1, _ => 0 };

	/// <summary>
	/// Writes one outline edge's two endpoints. Unlit and untextured by construction — the line
	/// colour came out of the ramp already resolved, exactly as the fill colour did. It carries its
	/// poly's face so the shader drops it with the face — see <see cref="MeshVertex.Side"/>.
	/// </summary>
	private static void EmitEdge(in OutlineEdge edge, bool local, MeshVertex[] vertices, int at) {
		vertices[at] = EdgeVertex(edge, local ? edge.LocalA : edge.A, local);
		vertices[at + 1] = EdgeVertex(edge, local ? edge.LocalB : edge.B, local);
	}

	/// <summary>Writes a one-vertex poly's single point — see <see cref="EmitEdge"/>.</summary>
	private static void EmitPoint(in OutlineEdge point, bool local, MeshVertex[] vertices, int at) =>
		vertices[at] = EdgeVertex(point, local ? point.LocalA : point.A, local);

	/// <summary>
	/// One end of an edge or a point. A poly whose stored normal does not resolve goes up with a
	/// zero face normal, which the shader's front/back test answers "back" — the original's answer
	/// for a zero normal (docs/retail/formats/dts-texture-binding.md, "<c>TSPoly_FrontBackVisibilityTest</c>").
	///
	/// <para>A shaded poly's outline is lit as its fill is, by the face normal, and goes up with both
	/// ramps — see <see cref="MeshVertex.OutlineFillRamp"/>.</para>
	/// </summary>
	private static MeshVertex EdgeVertex(in OutlineEdge edge, Vector3 position, bool local) =>
		new(position, edge.ShadeRamp >= 0 ? edge.Face.Normal ?? Vector3.UnitY : Vector3.UnitY,
			edge.Color, unlit: edge.ShadeRamp < 0, shadeRamp: edge.ShadeRamp,
			solidPaletteIndex: edge.SolidPaletteIndex, faceNormal: edge.Face.Normal ?? Vector3.Zero,
			faceCenter: local ? edge.Face.LocalCenter : edge.Face.Center, side: edge.Side,
			outlineFillRamp: edge.OutlineFillRamp);

	/// <summary>
	/// The surviving triangles grouped by the node that places them, the cell they stand on and the
	/// <see cref="TSBSPPart"/> child they belong to, each in that node's own space. Pieces come back
	/// in ascending transform id, then sequence, then frame, then child, which is only for stable
	/// output — nothing reads the order.
	/// </summary>
	private static MeshSegment[] EmitSegments(Collector sink) =>
		Partition(sink, local: true, (key, vertices, triangleVertices, pointVertices) =>
			new MeshSegment(key.TransformId, key.Gate, vertices, triangleVertices, pointVertices, key.Leaf));

	/// <summary>
	/// The same split by cell and <see cref="TSBSPPart"/> child alone, at the baked rest pose
	/// <see cref="Emit"/> writes — for a shape whose cells the simulation drives but whose nodes
	/// nothing poses. See <see cref="MeshCell"/>.
	/// </summary>
	private static MeshCell[] EmitCells(Collector sink) =>
		Partition(sink, local: false, (key, vertices, triangleVertices, pointVertices) =>
				new MeshCell(key.Gate, vertices, triangleVertices, pointVertices, key.Leaf))
			.GroupBy(cell => (cell.Gate, cell.Leaf))
			.Select(MergeCells)
			.ToArray();

	/// <summary>
	/// One cell's geometry from however many nodes carried it. <see cref="Partition"/> keys on the
	/// node as well because a segment needs it; a cell placed at the rest pose does not, so the
	/// node's share of one cell is folded back together into a single piece. A BSP part's children
	/// stay apart, because the renderer orders them.
	/// </summary>
	private static MeshCell MergeCells(IGrouping<(CellGate Gate, BspLeaf? Leaf), MeshCell> pieces) {
		var parts = pieces.ToArray();
		if (parts.Length == 1) {
			return parts[0];
		}

		int triangleVertices = parts.Sum(part => part.TriangleVertexCount);
		int pointVertices = parts.Sum(part => part.PointVertexCount);
		var vertices = new MeshVertex[parts.Sum(part => part.Vertices.Length)];

		// Triangles first, outlines after and points last, across the whole merged piece, because
		// each count is one boundary rather than one per part.
		int atTriangle = 0;
		int atEdge = triangleVertices;
		int atPoint = vertices.Length - pointVertices;
		foreach (var part in parts) {
			Array.Copy(part.Vertices, 0, vertices, atTriangle, part.TriangleVertexCount);
			atTriangle += part.TriangleVertexCount;

			int edgeVertices = part.Vertices.Length - part.TriangleVertexCount - part.PointVertexCount;
			Array.Copy(part.Vertices, part.TriangleVertexCount, vertices, atEdge, edgeVertices);
			atEdge += edgeVertices;

			Array.Copy(part.Vertices, part.Vertices.Length - part.PointVertexCount, vertices, atPoint,
				part.PointVertexCount);
			atPoint += part.PointVertexCount;
		}

		return new MeshCell(pieces.Key.Gate, vertices, triangleVertices, pointVertices, pieces.Key.Leaf);
	}

	/// <summary>
	/// The shared split behind <see cref="EmitSegments"/> and <see cref="EmitCells"/>: survivors
	/// bucketed by node, cell and <see cref="TSBSPPart"/> child, each bucket emitted as triangles,
	/// then the outline edges and then the points belonging to the same bucket.
	/// </summary>
	private static T[] Partition<T>(Collector sink, bool local,
			Func<PieceKey, MeshVertex[], int, int, T> make) {
		var kept = DropCoincidentTwins(sink.Triangles, leavesApart: true);
		var edges = SurvivingOutlines(kept, sink.Outlines);

		var byNode = new Dictionary<PieceKey, List<Triangle>>();
		foreach (var triangle in kept) {
			var key = new PieceKey(triangle.TransformId, triangle.Gate, triangle.Leaf);
			if (!byNode.TryGetValue(key, out var list)) {
				byNode[key] = list = new List<Triangle>();
			}
			list.Add(triangle);
		}

		// An outline rides the same node and the same cell its poly does, so it goes into that
		// bucket, and so does a point. A bucket whose only geometry is line or point polys carries no
		// triangles, so the list below is the union of all three keyings rather than the triangles'
		// alone.
		var edgesByNode = ByNode(edges);
		var pointsByNode = ByNode(sink.Points);

		var keys = byNode.Keys.Concat(edgesByNode.Keys).Concat(pointsByNode.Keys).Distinct()
			.OrderBy(key => key.TransformId).ThenBy(key => key.Gate.Sequence).ThenBy(key => key.Gate.Frame)
			.ThenBy(key => key.Leaf?.Index ?? -1)
			.ToArray();
		var pieces = new T[keys.Length];
		int next = 0;
		foreach (var key in keys) {
			var list = byNode.TryGetValue(key, out var triangles) ? triangles : new List<Triangle>();
			var nodeEdges = edgesByNode.TryGetValue(key, out var found) ? found : new List<OutlineEdge>();
			var nodePoints = pointsByNode.TryGetValue(key, out var foundPoints) ? foundPoints : new List<OutlineEdge>();

			int triangleVertices = list.Count * 3;
			int lineVertices = nodeEdges.Count * 2;
			var vertices = new MeshVertex[triangleVertices + lineVertices + nodePoints.Count];
			for (int i = 0; i < list.Count; i++) {
				EmitTriangle(list[i], local, vertices, i * 3);
			}

			for (int i = 0; i < nodeEdges.Count; i++) {
				EmitEdge(nodeEdges[i], local, vertices, triangleVertices + i * 2);
			}

			for (int i = 0; i < nodePoints.Count; i++) {
				EmitPoint(nodePoints[i], local, vertices, triangleVertices + lineVertices + i);
			}

			pieces[next++] = make(key, vertices, triangleVertices, nodePoints.Count);
		}

		return pieces;
	}

	/// <summary>What <see cref="Partition"/> buckets by.</summary>
	private readonly record struct PieceKey(int TransformId, CellGate Gate, BspLeaf? Leaf);

	private static Dictionary<PieceKey, List<OutlineEdge>> ByNode(List<OutlineEdge> edges) {
		var byNode = new Dictionary<PieceKey, List<OutlineEdge>>();
		foreach (var edge in edges) {
			var key = new PieceKey(edge.TransformId, edge.Gate, edge.Leaf);
			if (!byNode.TryGetValue(key, out var list)) {
				byNode[key] = list = new List<OutlineEdge>();
			}
			list.Add(edge);
		}

		return byNode;
	}

	/// <summary>
	/// Writes one triangle's three vertices, either at the rest pose <see cref="Collect"/> baked or
	/// in its node's own space. The normal is taken from whichever corners are being written, so a
	/// segment's normals rotate with the node matrix that draws it.
	/// </summary>
	private static void EmitTriangle(in Triangle triangle, bool local, MeshVertex[] vertices, int at) {
		Vector3 a = local ? triangle.LocalA : triangle.A;
		Vector3 b = local ? triangle.LocalB : triangle.B;
		Vector3 c = local ? triangle.LocalC : triangle.C;

		// The poly's OWN stored normal, not one derived from the winding — the two point opposite
		// ways. Measured across every poly of BASES.DGS, BASES_AN.DTS and APOCA.DTS (12,656 of them,
		// no exceptions): dot(normalize(cross(b - a, c - a)), storedNormal) == -1. The files wind
		// their corners the other way round from the normal they carry.
		//
		// This has to be the stored one because the front/back sign the shader derives from it is
		// then applied to the CORNER normals, which come out of the same point list and so share the
		// stored convention (ResolveVertexNormals). Deriving the sign from the winding instead turns
		// every Gouraud poly's light term inside out: lit on the side facing away from the sun.
		// A flat poly is insensitive to the choice — its corner normal is this same vector, so the
		// sign cancels — which is why the mistake was invisible until Gouraud shading went in.
		Vector3 winding = Vector3.Cross(c - a, b - a);
		Vector3 normal = triangle.FaceNormal
			?? (winding.LengthSquared() > 1e-12f ? Vector3.Normalize(winding) : Vector3.UnitY);

		// A Gouraud poly carries the shape's own normal per corner, and interpolating between them is
		// the whole difference between the type and its flat sibling. The corners' normals are
		// direction-only, so they are the same in the node's space and the rest pose's — the offset
		// between those is a translation (see ResolveGroupOffset).
		var (normalA, normalB, normalC) = triangle.VertexNormals ?? (normal, normal, normal);

		// A copy lit as the poly's back while the poly faces the eye: the shader turns the normals
		// toward the eye's side, so they go up already turned the other way. The face normal stays as
		// it is, since the front/back decision is still made from it.
		if (triangle.LitAsBack) {
			(normalA, normalB, normalC) = (-normalA, -normalB, -normalC);
		}

		// Only a triangle that actually resolved to an atlas frame samples the texture; the rest
		// keep their colour, which is what makes the placeholder colour on an unresolved texture
		// poly visible instead of it sampling whatever sits at the atlas origin.
		bool textured = triangle.Rank == Ranks.Textured;
		Vector3 center = local ? triangle.Face.LocalCenter : triangle.Face.Center;

		vertices[at] = new MeshVertex(a, normalA, triangle.Color, triangle.UvA, textured, triangle.Unlit,
			shadeRamp: triangle.ShadeRamp, faceNormal: normal, uvWeight: triangle.UvWeights.A,
			solidPaletteIndex: triangle.SolidPaletteIndex, faceCenter: center, side: triangle.Side);
		vertices[at + 1] = new MeshVertex(b, normalB, triangle.Color, triangle.UvB, textured, triangle.Unlit,
			shadeRamp: triangle.ShadeRamp, faceNormal: normal, uvWeight: triangle.UvWeights.B,
			solidPaletteIndex: triangle.SolidPaletteIndex, faceCenter: center, side: triangle.Side);
		vertices[at + 2] = new MeshVertex(c, normalC, triangle.Color, triangle.UvC, textured, triangle.Unlit,
			shadeRamp: triangle.ShadeRamp, faceNormal: normal, uvWeight: triangle.UvWeights.C,
			solidPaletteIndex: triangle.SolidPaletteIndex, faceCenter: center, side: triangle.Side);
	}

	/// <summary>
	/// Real DTS meshes stack a textured poly precisely on top of a flat-shaded twin occupying the
	/// exact same surface — 186 such pairs in <c>SAMSON.DTS</c>'s first root alone, with identical
	/// centroid and normal. Both drawn, they land at identical depth, so which one is visible comes
	/// down to draw order rather than anything meaningful. Exactly one survives per coincident group
	/// and per side it is seen from, picked by <see cref="Ranks"/>.
	///
	/// <para><b>That preference is the inverse of what it was before texturing existed.</b> While
	/// <see cref="TSTexture4Poly"/> could only render as a placeholder colour, the flat-shaded twin
	/// was the only one that could look right and deliberately won every tie. Now that a texture poly
	/// resolves to real atlas pixels it is the one the original actually draws, so it has to win —
	/// leaving the old preference in place would load and pack every texture and then systematically
	/// hide it behind the untextured twin.</para>
	///
	/// <para>A texture poly that did <i>not</i> resolve (no atlas supplied, or a frame index outside
	/// the bank) still loses to the flat-shaded twin, which is why this is a three-way rank rather
	/// than a flipped boolean: the no-bank path keeps behaving exactly as it did before.</para>
	///
	/// <para>Grouping uses a coarsely-rounded centroid plus the absolute normal, so opposite-winding
	/// duplicates of one surface land together while genuinely distinct nearby triangles do not.</para>
	///
	/// <para><b>The cell gate is part of the key</b>, because two cells of one part are usually the
	/// same surface twice on purpose: a machine's body part carries its intact geometry in cell 0 and
	/// the identical geometry moved to one dark ramp in cell 1, and those are alternatives rather
	/// than a coincident pair. Only one of them is ever on screen, so neither hides the other and
	/// discarding either would lose a state the part can be in. Two levels of one detail part are
	/// alternatives in the same way, so the detail level is in the key too.</para>
	///
	/// <para><b>So is the <see cref="TSBSPPart"/> child</b>, with <paramref name="leavesApart"/>:
	/// twins in two children of one part are ordered by the part's walk, which the renderer
	/// reproduces for pieces split by child, so neither is dropped there. The single flat mesh
	/// <see cref="Emit"/> builds keeps no children apart and nothing orders them, so it still keeps
	/// one twin.</para>
	///
	/// <para><b>So is the side each twin is seen from.</b> A copy that draws one side only
	/// (<see cref="Triangle.Side"/>) never meets a twin drawn only from the other, and most coincident
	/// pairs in the retail files are exactly that — two one-sided faces back to back, one per side of a
	/// thin plate. Each copy competes once per side it draws, on the direction its drawn side faces,
	/// and keeps the sides it wins: a two-sided copy that wins one side and loses the other goes on
	/// drawing that one side alone.</para>
	/// </summary>
	private static List<Triangle> DropCoincidentTwins(List<Triangle> triangles, bool leavesApart) {
		var winners = new Dictionary<((int, int, int, int, int, int), CellGate, BspLeaf?, int), int>();
		var keys = new ((int, int, int, int, int, int) Surface, int FrontFacing)[triangles.Count];

		for (int i = 0; i < triangles.Count; i++) {
			var triangle = triangles[i];
			Vector3 centroid = (triangle.A + triangle.B + triangle.C) / 3f;
			Vector3 normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
			if (normal.LengthSquared() > 1e-12f) {
				normal = Vector3.Normalize(normal);
			}

			// The surface's axis, its sign fixed by the first component that is not zero, so twins
			// wound either way share it; the direction a copy's drawn side faces is measured along it.
			// The stored normal opposes the winding (see EmitTriangle), which the fallback keeps.
			Vector3 axis = normal;
			if (axis.X < -1e-3f || (MathF.Abs(axis.X) <= 1e-3f
					&& (axis.Y < -1e-3f || (MathF.Abs(axis.Y) <= 1e-3f && axis.Z < 0f)))) {
				axis = -axis;
			}

			keys[i] = ((
				(int)MathF.Round(centroid.X * 40f), (int)MathF.Round(centroid.Y * 40f), (int)MathF.Round(centroid.Z * 40f),
				(int)MathF.Round(MathF.Abs(normal.X) * 100f), (int)MathF.Round(MathF.Abs(normal.Y) * 100f),
				(int)MathF.Round(MathF.Abs(normal.Z) * 100f)),
				Vector3.Dot(triangle.FaceNormal ?? -normal, axis) >= 0f ? 1 : -1);

			foreach (int side in SidesOf(triangle.Side)) {
				var key = (keys[i].Surface, triangle.Gate, leavesApart ? triangle.Leaf : null, keys[i].FrontFacing * side);

				// A strictly better-ranked twin replaces the one already kept; ties go to the first seen.
				if (!winners.TryGetValue(key, out int existing) || triangle.Rank > triangles[existing].Rank) {
					winners[key] = i;
				}
			}
		}

		var result = new List<Triangle>(triangles.Count);
		for (int i = 0; i < triangles.Count; i++) {
			var triangle = triangles[i];
			int won = 0;
			foreach (int side in SidesOf(triangle.Side)) {
				if (winners[(keys[i].Surface, triangle.Gate, leavesApart ? triangle.Leaf : null, keys[i].FrontFacing * side)] == i) {
					won |= SideMask(side);
				}
			}

			if (won != 0) {
				result.Add(triangle with { Side = SideOfMask(won) });
			}
		}

		return result;
	}

	/// <summary>The one or two sides a <see cref="Triangle.Side"/> draws, as <c>+1</c>/<c>-1</c>.</summary>
	private static int[] SidesOf(int side) => side == 0 ? new[] { 1, -1 } : new[] { side };
}
