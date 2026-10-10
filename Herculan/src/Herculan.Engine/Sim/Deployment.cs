using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// Where an arriving mission group turns up — <c>Deployment_PickPointNearPlayer</c>
/// (<c>0042354c</c>), the one function both arrival paths and the drop pod share.
///
/// <para>Every reinforcement in the game is placed <b>relative to the player</b>, never at a point
/// the mission names. That is why an undeployed group's placed position is meaningless: it is never
/// read.</para>
/// </summary>
public static class Deployment {
	/// <summary>How far the search steps outward each time the point it has is refused.</summary>
	public const int SearchStep = 2000;

	/// <summary>
	/// The clearance an already-deployed object is given: its own collision radius plus this. Only
	/// applied when the caller asks for the object test at all.
	/// </summary>
	public const int ObjectClearance = 5000;

	/// <summary>The radius the structure sweep is asked about, which it uses as its own reach.</summary>
	public const int StructureClearance = 5000;

	/// <summary>
	/// A ceiling on the outward search, which is this engine's own: the original steps until a point
	/// clears, however long that takes. Every point past the grid's north or south end is refused, so
	/// a search that runs off that way would hang the original; this one stops and hands back the last
	/// point tried, which is off the heightmap. A search running off the west or east edge ends without the
	/// cap, at the first walkable cell of the neighbouring rows it reads there. The cap also lets a
	/// headless test on a stub terrain terminate. See
	/// docs/retail/simulation/mission-deployment.md#picking-the-point--deployment_pickpointnearplayer-0042354c.
	/// </summary>
	public const int MaxSearchSteps = 512;

	/// <summary>
	/// Picks a clear point at <paramref name="distance"/> from the player along
	/// (player heading + <paramref name="bearing"/>), stepping outward in
	/// <see cref="SearchStep"/> increments until it clears everything that could be standing there.
	///
	/// <para>Three tests, in the original's order and with the original's asymmetry:</para>
	/// <list type="number">
	/// <item><b>Deployed objects</b>, and <i>only when <paramref name="avoidObjects"/></i> — which is
	/// set for the two walk-on verbs and clear for the drop pod. A pod is allowed to come down on top
	/// of a machine, which is exactly the case its own landing blast latch exists to handle. Objects
	/// still awaiting deployment are skipped, so a group never lands on one that has not arrived, and
	/// an object with no collision radius is skipped too.</item>
	/// <item><b>Structures</b>, through the same volume sweep a walking machine is stopped by.</item>
	/// <item><b>The ground</b>, through the terrain slope walk's face test — anything too steep to
	/// stand on, or past the grid's north or south end. A point past the west or east edge reads a
	/// cell of the neighbouring row (<see cref="Terrain.HeightGrid.BlocksMovementAt"/>) and can clear,
	/// and what arrives there is stranded: see
	/// docs/retail/simulation/mission-deployment.md#a-pod-aimed-off-the-heightmap.</item>
	/// </list>
	///
	/// <para>The offset is built as the vector <c>(0, distance, 0)</c> rotated by the bearing, which
	/// is the sim's forward axis; the player's own position is then added, Z included, so the point
	/// comes back at the player's height rather than the ground's.</para>
	/// </summary>
	/// <param name="world">The running world — the player, the object list and the terrain.</param>
	/// <param name="distance">How far out to start, in world units.</param>
	/// <param name="bearing">Offset from the player's heading, as a binary angle.</param>
	/// <param name="avoidObjects">Whether the deployed-object test is applied.</param>
	public static Vec3i PickPointNearPlayer(SimWorld world, int distance, short bearing,
			bool avoidObjects) {
		var player = world.PlayerMech;
		var origin = player?.Position ?? Vec3i.Zero;
		int heading = (player?.Heading ?? 0) + bearing;

		short cos = BinaryAngle.Cos(heading);
		short sin = BinaryAngle.Sin(heading);

		var point = origin;

		for (int step = 0; step < MaxSearchSteps; step++) {
			int reach = distance + step * SearchStep;

			// The rotated (0, reach, 0): x picks up -sin and y picks up cos.
			point = new Vec3i(
				origin.X + (int)(((long)-reach * sin + 0x2000) >> 14),
				origin.Y + (int)(((long)reach * cos + 0x2000) >> 14),
				origin.Z);

			if (avoidObjects && ObjectInTheWay(world, point)) {
				continue;
			}

			if (StructureInTheWay(world, point)) {
				continue;
			}

			if (world.Terrain.BlocksMovementAt(point.X, point.Y)) {
				continue;
			}

			return point;
		}

		return point;
	}

	/// <summary>
	/// Whether a deployed object stands within its own collision radius plus
	/// <see cref="ObjectClearance"/> of the point. The radius is the same "zero means walk through
	/// me" figure the walking collision test reads, so a flyer and a static structure are both
	/// invisible here — the structure by the sweep below instead.
	/// </summary>
	private static bool ObjectInTheWay(SimWorld world, Vec3i point) {
		var objects = world.Objects;

		for (int i = 0; i < objects.Count; i++) {
			var candidate = objects[i];

			if (candidate.Removed || candidate.AwaitingDeployment || candidate.CollisionRadius == 0) {
				continue;
			}

			if (candidate.Position.ApproxDistanceTo(point) < candidate.CollisionRadius + ObjectClearance) {
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>Structure_GatherWalkCandidates</c> (<c>00404ae4</c>) at the point — the same structure
	/// sweep <c>Mech_CollisionTest</c> makes, which is what that function collects for. It is not a
	/// separate obstacle list: it gathers every structure that blocks by volume rather than by radius
	/// and hands the lot to <see cref="BaseObject.BlocksWalker"/>.
	///
	/// <para>Shared, because three sweeps in the original make this same call: the deployment probe
	/// here, <c>Mech_CollisionTest</c> (<c>00418f74</c>) and <c>GroundVehicle_CollisionTest</c>
	/// (<c>0046a510</c>).</para>
	///
	/// <para>A structure whose group has not arrived is gathered like any other, as in the original,
	/// whose gather makes no group-action test: it blocks while it is not drawn. See
	/// docs/retail/simulation/mission-deployment.md ("The deployment gate") and KNOWN_ISSUES.md.</para>
	/// </summary>
	/// <param name="excluded">
	/// The gather's third argument, one structure it passes over: a ground vehicle names itself, so it
	/// is not stopped by its own volume. The other two callers pass none.
	/// </param>
	internal static bool StructureInTheWay(SimWorld world, Vec3i point, SimObject? excluded = null) {
		var objects = world.Objects;

		for (int i = 0; i < objects.Count; i++) {
			if (objects[i] is BaseObject structure && !ReferenceEquals(structure, excluded)
					&& !structure.Removed && structure.BlocksWalker(point)) {
				return true;
			}
		}

		return false;
	}
}
