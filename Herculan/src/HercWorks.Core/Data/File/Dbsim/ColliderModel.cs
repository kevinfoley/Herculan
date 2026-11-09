namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// One sphere of a collision model, in the frame its node names — four <c>int16</c>s on disk, and
/// the smallest unit of hit geometry the simulation has. Retail radii run 40-600 world units.
/// </summary>
public readonly record struct ColliderSphere(short X, short Y, short Z, short Radius);

/// <summary>One destructible component's hit volume: a group of spheres that all belong to it.</summary>
/// <param name="ComponentIndex">
/// Which of the unit's components these spheres belong to — an index into the 29-slot component
/// array in the matching <c>dmg\&lt;herc&gt;.DMG</c> (see <see cref="HercSimDamage"/>), or into a
/// structure type's own component list. A shot that strikes this cluster damages that component,
/// and a component already destroyed has its clusters skipped, so a unit loses hit volume as it
/// comes apart. Retail values run 0-28.
/// </param>
/// <param name="Spheres">The cluster's spheres.</param>
/// <param name="Bound">
/// The sphere around all of them — the cheap first test, and <b>not on disk</b>.
/// <c>Collision_ComputeBoundingSphere</c> (<c>0040c5d0</c>) derives it at load from the AABB of the
/// children each inflated by its own radius. The derivation needs DBSIM's own distance
/// approximation, which lives in the engine's <c>SimMath</c>, so this field is filled by
/// <c>Herculan.Es2.World.CollisionModelReader</c> and left default by
/// <see cref="Io.Transform.Dbsim.HercColliderTransformer"/>. The write path ignores it either way.
/// </param>
public readonly record struct ColliderCluster(
	short ComponentIndex, ColliderSphere[] Spheres, ColliderSphere Bound = default);

/// <summary>One node's worth of clusters.</summary>
/// <param name="NodeIndex">
/// The shape part id whose posed transform places this node's spheres, or <c>-1</c> for the unit's
/// own frame. DBSIM resolves it through the shape to a transform slot, falling back on identity for
/// a part the shape does not have.
///
/// <para>Every mech file places every cluster on a node, so a HERC's hit volume walks with its legs.
/// <c>SKIMMER.COL</c> is the only retail file with an object-frame cluster; a structure's model is
/// the opposite, almost all of it in the object frame.</para>
/// </param>
/// <param name="Clusters">The clusters placed by that node.</param>
public readonly record struct ColliderNode(short NodeIndex, ColliderCluster[] Clusters);
