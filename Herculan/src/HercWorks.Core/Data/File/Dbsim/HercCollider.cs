namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/COL/&lt;herc&gt;.COL — a unit's hit-sphere model, one file per HERC plus SKIMMER
/// (22 in a retail install). This is the geometry a shot is actually tested against: DBSIM never
/// tests a shot against a unit's polygons.
///
/// <para>The layout is a header-less walk of nodes, clusters and spheres — <see cref="Nodes"/> is
/// its result. Structures use the identical record shape, 65 of them back to back in
/// <c>dat\BASECOL.DAT</c>, read through
/// <see cref="Io.Transform.Dbsim.HercColliderTransformer.ReadNodes"/>, which is the same walk with
/// an offset. The layout, the readers, the evidence that the elements are spheres and the retail
/// verification are in <c>docs/retail/formats/collision-spheres.md</c>; the 22 retail files round-trip
/// byte-exact through <c>HercColliderTransformer</c>.</para>
///
/// <para>The node/cluster/sphere types are top-level (see <see cref="ColliderNode"/>) rather than
/// nested, so that the engine can consume the parsed model without taking a dependency on
/// <see cref="DataFile"/>.</para>
/// </summary>
public class HercCollider {
	/// <summary>The model's nodes, in file order.</summary>
	public ColliderNode[]? Nodes { get; set; }
}
