using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// A condition on another poly's facing that a copy of geometry is drawn under: the copy draws only
/// while the poly whose plane this is faces <see cref="Side"/> (<c>+1</c> the eye, <c>-1</c> away),
/// decided exactly as the shader decides a copy's own side (<see cref="Gl.MeshVertex.Side"/>).
/// <see cref="None"/>, side 0, is no condition.
///
/// <para>The plane is the poly's stored normal and the offset of its stored centre along it, in the
/// two spaces a mesh is built in (the rest pose and the node's own), since
/// <c>TSPoly_FrontBackVisibilityTest</c> (<c>0048c620</c>) depends on the centre only through that
/// offset. A poly whose normal does not resolve carries a zero normal, which answers "back" as its own
/// test would.</para>
/// </summary>
internal readonly record struct FacingGate(Vector3 Normal, float Distance, float LocalDistance, int Side) {
	public static FacingGate None => default;
}

/// <summary>
/// Which point a back-facing three-vertex <c>TSTexture4Poly</c> borrows for its second corner, as far
/// as its own group decides it.
///
/// <para><c>TSTexture4Poly_Render</c> (<c>00474e9c</c>) copies a poly's corners into a static
/// four-slot position array (<c>006b7a9c</c>) and, for a poly it draws as its back, swaps slots 1 and
/// 3. A triangle fills slots 0-2 only, so its swap draws whatever slot 3 still holds from the last
/// texture poly that wrote it: corner 3 of a quad drawn as its front, or corner 1 of anything drawn
/// as its back. docs/retail/rendering/dts-texture-binding.md, "Three-vertex texture polys", has the
/// trace. A group draws its polys in file order (<c>TSGroup_RenderPolys</c>, <c>004758c8</c>), and
/// every reference to the array <c>es2_xref.py</c> finds sits in <c>TSTexture4Poly_Render</c>, so
/// within a group the slot follows the group's own earlier texture polys and which way each faces
/// this frame.</para>
///
/// <para>This tracks those polys while the mesh builder walks a group. A borrowed corner it can name
/// depends on one earlier poly whose two facings both write the slot; the triangle then goes up once
/// per facing of that poly, each copy gated on it (<see cref="FacingGate"/>), and the shader keeps the
/// one the eye selects. Both retail triangles that draw a back (<c>CERBERUS.DTS</c> root 0) follow a
/// two-sided textured quad in their group, which is this case.</para>
/// </summary>
internal sealed class TextureCornerSlot {
	private readonly List<Write> _writes = new();

	/// <summary>
	/// One earlier texture poly: the plane its facing is decided by, and the point index it leaves in
	/// slot 3 when drawn as its front and as its back, null where that facing leaves the slot alone.
	/// <paramref name="Known"/> is false for a vertex count the array cannot hold, whose effect on the
	/// slot this does not model.
	/// </summary>
	private readonly record struct Write(FacingGate Plane, int? Front, int? Back, bool Known);

	/// <summary>
	/// Records a texture poly drawn after the ones already recorded.
	/// </summary>
	/// <param name="plane">Its facing plane, with <see cref="FacingGate.Side"/> unused.</param>
	/// <param name="corners">Its corners' point indices in vertex-list order.</param>
	/// <param name="frontDraws">Whether it draws at all while facing the eye.</param>
	/// <param name="frontSwapped">Whether, facing the eye, it draws its back pair — a front value of <c>-1</c> — and so swaps.</param>
	/// <param name="backDraws">Whether it draws at all while facing away.</param>
	public void Add(FacingGate plane, IReadOnlyList<int> corners, bool frontDraws, bool frontSwapped, bool backDraws) {
		if (corners.Count is not (3 or 4)) {
			_writes.Add(new Write(plane, null, null, Known: false));
			return;
		}

		// Unswapped, slot 3 receives corner 3, which a triangle does not have. Swapped, it receives
		// corner 1 either way.
		int? Left(bool swapped) => swapped ? corners[1] : corners.Count == 4 ? corners[3] : null;

		_writes.Add(new Write(plane,
			frontDraws ? Left(frontSwapped) : null,
			backDraws ? Left(swapped: true) : null,
			Known: true));
	}

	/// <summary>
	/// The points slot 3 can hold when the next texture poly reads it, each with the facing that puts it
	/// there, or null when the group's own polys do not settle it: no earlier texture poly in the group
	/// writes the slot (it then holds what an earlier draw left), or the nearest one that does writes
	/// it on one facing only (it then depends on more than one poly). No retail shape reaches null.
	/// </summary>
	public (int Point, FacingGate Gate)[]? Candidates() {
		for (int k = _writes.Count - 1; k >= 0; k--) {
			var write = _writes[k];
			if (!write.Known) {
				return null;
			}

			if (write.Front == null && write.Back == null) {
				continue;
			}

			if (write.Front is not { } front || write.Back is not { } back) {
				return null;
			}

			return front == back
				? new[] { (front, FacingGate.None) }
				: new[] { (front, write.Plane with { Side = 1 }), (back, write.Plane with { Side = -1 }) };
		}

		return null;
	}
}
