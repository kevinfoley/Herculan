using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// The <see cref="Settings.TweakSettingDefinitions.SquadmateClearOfTransport"/> tweak — <b>this engine's own</b>, a fix
/// for one retail mission. <c>C5_05</c> stands the player's squad at the first waypoint of its route, inside the lunar
/// outpost it defends, and formation slot 1's offset puts the first squadmate's centre inside the collision volume of
/// the outpost's SUPPLY TRANSPORT (<c>BASES.DAT</c> type <c>0x37</c>): 1918 units from the vehicle, within its 3422-unit
/// bounding radius, on a column the volume marks solid. <see cref="Sim.BaseObject.BlocksWalker"/> refuses every step
/// it tries from there until the transport is destroyed. See docs/retail/campaign-continuity.md#chapter-5--the-moon.
///
/// <para>With the tweak on, that one placement starts 1700 units further from the vehicle, along the line from its
/// centre, which is past the bounding radius and so clear of the volume whatever its shape. The new point is 3574 units
/// from the outpost's MISSILE TOWER, outside the 2350 that tower blocks a machine at, and over 9000 from the rest of the
/// squad. Its formation offset, and so the post it keeps on the player, is unchanged, as is its height, which every
/// squad machine reads at the group's point. The tweak acts at mission load.</para>
///
/// <para><b>The mission is recognised by its data, not its name</b>, because <c>script.dat</c> names no mission: a
/// squad machine at the blocked point and a type <c>0x37</c> structure at the vehicle's. Neither the squad's route nor
/// the outpost carries a random table key, so every load places both the same, and v1.0 and v1.10 place them alike.</para>
/// </summary>
internal static class StuckSquadmateFix {
	/// <summary>Formation slot 1 of the squad, spread from route 48's first waypoint.</summary>
	private static readonly (int X, int Y) Blocked = (1013554, 1013358);

	/// <summary>Group 111's member 8, the outpost's supply transport.</summary>
	private static readonly (int X, int Y) Transport = (1015456, 1013612);

	private const int TransportType = 0x37;

	/// <summary>1700 units on from <see cref="Blocked"/>, away from <see cref="Transport"/>.</summary>
	private static readonly (int X, int Y) Clear = (1011869, 1013133);

	/// <summary>
	/// <paramref name="placement"/> moved clear of the transport when it is the blocked squad machine of a mission that
	/// places the transport where <c>C5_05</c> does, and <paramref name="placement"/> itself otherwise.
	/// </summary>
	public static MissionPlacement Adjust(MissionPlacement placement, IReadOnlyList<MissionPlacement> placements) {
		if (!placement.IsPlayerSquad || (placement.Position.X, placement.Position.Y) != Blocked) {
			return placement;
		}

		foreach (var other in placements) {
			if (other.Kind == MissionUnitKind.Base && other.TypeIndex == TransportType
					&& (other.Position.X, other.Position.Y) == Transport) {
				return placement with { Position = new Vec3i(Clear.X, Clear.Y, placement.Position.Z) };
			}
		}

		return placement;
	}
}
