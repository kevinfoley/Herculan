using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// A roster record's claim: the group that places it, by its index in file order, and the record's position within that
/// group's member slots — the "member index" each formation table's <c>OffsetFor</c> takes, which is the slot position, not a
/// compacted count of live members (see <see cref="MissionPlacementRules.ClaimMembers{TKey}"/>).
/// </summary>
public readonly record struct MissionMemberClaim(int Group, int MemberIndex);

/// <summary>
/// Where a group member stands and which way it faces, and the formation slot it spread by.
/// </summary>
/// <param name="FormationOffset"><inheritdoc cref="MissionPlacement.FormationOffset"/></param>
/// <param name="FlyerFormationOffset"><inheritdoc cref="MissionPlacement.FlyerFormationOffset"/></param>
/// <param name="GroundPoint"><inheritdoc cref="MissionPlacement.GroundPoint"/></param>
public readonly record struct MissionMemberPlace(Vec3i Position, int Heading, (int X, int Y)? FormationOffset = null,
	Vec3i? FlyerFormationOffset = null, Vec3i? GroundPoint = null);

/// <summary>
/// Where a mission puts its groups and the objects they place, over plain values: <see cref="MissionLoader"/> calls it with
/// what it resolves from <c>script.dat</c> and <c>player.mec</c>, and it reads no file and takes no block index. Where identity
/// matters (<see cref="ClaimMembers{TKey}"/>, <see cref="PadLead{TKey}"/>) the caller's own key stands for a roster record.
/// The rule and its RE are docs/retail/formats/script-dat.md#placement--the-actual-rule; in brief:
/// <list type="number">
/// <item><b>Which objects exist.</b> Every group past the squad's names a roster (herc, flyer or structure) and up to
/// <see cref="GroupMemberSlots"/> records of it, and each one it names is live. A record no group names never spawns, which
/// is why a mission's roster is routinely larger than its live object count. <see cref="ClaimMembers{TKey}"/>.</item>
/// <item><b>Where they stand.</b> <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) builds each group record with its point,
/// heading and route, and <c>Mech_AttachToGroup</c> (<c>00417aa8</c>), <c>Flyer_AttachToGroup</c> (<c>00421ee8</c>) and
/// <c>Base_AttachToGroup</c> (<c>00405c3c</c>) attach each member,
/// filling in the member's position <i>only if it does not already have one of its own</i>: a roster record's own point
/// wins where set, and the group's, spread by the member's formation slot, is the fallback. <see cref="PlaceHerc"/>,
/// <see cref="PlaceFlyer"/>, <see cref="PlaceStructure"/>.</item>
/// <item><b>A group with no point of its own</b> stands on its route's first waypoint (<c>Route_WaypointAt</c>,
/// <c>00423b0c</c>) and faces along its first leg. Patrol squads are placed this way. <see cref="GroupSetup"/>.</item>
/// <item><b>The player's squad</b> is not in <c>script.dat</c>'s roster at all. Block 11's record 0 exists only to hold its
/// spawn point: <c>DBSim_LoadScriptDat</c> (<c>00424308</c>) skips it during activation, and
/// <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) overwrites its member list with the entries read from
/// <c>player.mec</c>. <see cref="PlaceSquadMember"/>.</item>
/// </list>
///
/// <para><b>Formation spread.</b> The original spreads every member but a group's first off the group's point through a
/// per-kind vtable <c>+0x78</c> call — <c>Mech_ApplyFormationOffset</c> (<c>00417898</c>), <c>Flyer_ApplyFormationOffset</c>
/// (<c>00421e98</c>) and <c>Base_ApplyFormationOffset</c> (<c>00405c04</c>), the same slot and the same "member index 0
/// takes no offset" rule — over <see cref="MechFormationTable"/>, <see cref="FlyerFormationTable"/> and
/// <see cref="BaseFormationTable"/>; each class's doc comment has its load site.</para>
/// </summary>
public static class MissionPlacementRules {
	/// <summary>The group reserved for the player's squad, block 11's record 0; it claims nothing.</summary>
	public const int SquadGroupIndex = 0;

	/// <summary>Members a group can name.</summary>
	public const int GroupMemberSlots = 20;

	/// <summary>
	/// Degrees-to-binary-angle constant. DBSIM applies this to every block-2 value as it reads them
	/// (<c>*(short *)local_7c = local_102[0] * 0xb6</c>), which is what identifies that block as
	/// headings in degrees rather than an opaque discrete field.
	/// </summary>
	private const int DegreesToBinaryAngle = 0xb6;

	/// <summary>A heading record's degrees as the binary angle every heading here takes.</summary>
	public static int HeadingFromDegrees(short degrees) => degrees * DegreesToBinaryAngle;

	/// <summary>
	/// A group's own place: its own point, else the first waypoint of its route, else the origin, with where that came from;
	/// its own heading, else the bearing of its route's first leg (<see cref="RouteBearing"/>); and, for a structure group
	/// with a <see cref="PadLayout"/>, the point moved onto the pad's fixed spot in its terrain tile.
	///
	/// <para>A structure group that paints its ground also stands at a fixed spot on it. The mission's own point only picks
	/// the tile; the formation supplies the position within it. Machines never do this — <c>Mech_AttachToGroup</c>
	/// (<c>00417aa8</c>) takes no such flag and has no equivalent arithmetic.</para>
	/// </summary>
	/// <param name="kind">The roster its members come from.</param>
	/// <param name="ownPoint">The point the group's own record names, or null when it names none.</param>
	/// <param name="ownHeading">The heading its own record names, as a binary angle (<see cref="HeadingFromDegrees"/>), or null.</param>
	/// <param name="route">
	/// The waypoints of the route its order slot 0 names, and only slot 0's (rule 3 of the doc section above); empty for none.
	/// </param>
	/// <param name="formationId">Its formation, an index into the formation table of its own kind.</param>
	/// <param name="paintsGround">Whether its record sets <c>PaintsGround</c>.</param>
	/// <param name="baseFormations">The structure formations, for the snap.</param>
	public static MissionGroupSetup GroupSetup(MissionUnitKind kind, Vec3i? ownPoint, int? ownHeading,
			IReadOnlyList<Vec3i> route, int formationId, bool paintsGround, BaseFormationTable baseFormations) {
		var authored = ownPoint
			?? (route.Count > 0 ? route[0] : (Vec3i?)null)
			?? Vec3i.Zero;
		var source = ownPoint != null ? MissionGroupPointSource.OwnPoint
			: route.Count > 0 ? MissionGroupPointSource.RouteStart
			: MissionGroupPointSource.None;

		var point = authored;
		if (PadLayout(kind, paintsGround, formationId, baseFormations) is { } layout) {
			var (snappedX, snappedY) = layout.SnapAnchor(authored.X, authored.Y);
			point = new Vec3i(snappedX, snappedY, authored.Z);
		}

		return new MissionGroupSetup(point, authored, source, ownHeading ?? RouteBearing(route), ownHeading == null,
			formationId, paintsGround);
	}

	/// <summary>
	/// The terrain layout a group paints as its pad, or null when it paints none: a structure group whose record sets
	/// <c>PaintsGround</c> (raw msn offset <c>0x06</c>, set on 39 of the 61 retail block-11 records) and whose formation
	/// declares a layout. It gates both halves of the base-formation terrain pass: the anchor move in
	/// <see cref="GroupSetup"/>, and the repaint of the same tile — see <see cref="MissionBasePad"/> and
	/// <see cref="BaseFormationLayout"/>.
	/// </summary>
	public static BaseFormationLayout? PadLayout(MissionUnitKind kind, bool paintsGround, int formationId,
			BaseFormationTable baseFormations) =>
		kind == MissionUnitKind.Base && paintsGround ? baseFormations.LayoutFor(formationId) : null;

	/// <summary>
	/// Which way a group faces when its own record names no heading: along the first leg of its
	/// route. <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) takes the route's first two
	/// waypoints and calls <c>Math_HeadingToward</c> (<c>00492828</c>) with the second one first, which is
	/// <c>atan2(dy, dx) - 0x4000</c> — the same quarter turn every bearing in the simulation carries,
	/// since a machine's forward axis is model Y rather than model X.
	///
	/// <para>A route with fewer than two waypoints leaves the heading at zero, which is the branch
	/// the original guards with its <c>1 &lt; waypointCount</c> test. Retail missions reach this for
	/// every patrolling group <i>and</i> for the player's own squad — none of those records carry a
	/// heading ref — so without it a whole mission faced due north and every formation spread was
	/// rotated by the wrong angle.</para>
	/// </summary>
	public static int RouteBearing(IReadOnlyList<Vec3i> route) {
		if (route.Count < 2) {
			return 0;
		}

		int dx = route[1].X - route[0].X;
		int dy = route[1].Y - route[0].Y;

		// Math_Atan2Guarded (00492800)'s degenerate guard, which nudges x — not y — so a zero-length first leg reads
		// as a bearing of zero rather than a quarter turn off it.
		if (dx == 0 && dy == 0) {
			dx = 1;
		}

		return (SimTrig.Atan2(dy, dx) - BinaryAngle.QuarterTurn) & 0xffff;
	}

	/// <summary>
	/// The first-claim map of one roster: each record a group of <paramref name="kind"/> names, to the group that places it
	/// and the record's position within that group's member slots. Groups are walked in file order and the first to name a
	/// record wins, matching <c>Mech_AttachToGroup</c> (<c>00417aa8</c>)'s "only if it has no position yet" test. The
	/// squad's group (<see cref="SquadGroupIndex"/>) is skipped, since it activates nothing.
	///
	/// <para>The member index recorded here is the raw slot position (0-19), <b>not</b> a count of
	/// live members seen so far — <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) passes the loop
	/// index itself as the attach-time slot, gaps and all, and that is what the formation tables
	/// index by.</para>
	/// </summary>
	/// <param name="kind">The roster to claim for; a group of another kind, or of none of the three, claims nothing in it.</param>
	/// <param name="groups">Every group in file order, its index its position here: its kind and its member slots, null where a slot names nothing. Slots past <see cref="GroupMemberSlots"/> are not read.</param>
	public static IReadOnlyDictionary<TKey, MissionMemberClaim> ClaimMembers<TKey>(MissionUnitKind kind,
			IReadOnlyList<(MissionUnitKind Kind, IReadOnlyList<TKey?> Members)> groups) where TKey : struct {
		var claims = new Dictionary<TKey, MissionMemberClaim>();

		for (int group = SquadGroupIndex + 1; group < groups.Count; group++) {
			var (groupKind, members) = groups[group];
			if (groupKind != kind) {
				continue;
			}

			for (int slot = 0; slot < GroupMemberSlots && slot < members.Count; slot++) {
				if (members[slot] is { } member) {
					claims.TryAdd(member, new MissionMemberClaim(group, slot));
				}
			}
		}

		return claims;
	}

	/// <summary>
	/// The record a structure group's pad is read from: its first-attached member, the one of
	/// <paramref name="structureClaims"/> that <paramref name="group"/> claims at the lowest member index — the lowest
	/// populated slot of its member array, not necessarily slot 0, though every retail structure group that paints does
	/// populate slot 0. Null when it claims none. <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) passes that member to
	/// <c>Base_ApplyFormationTerrain</c> (<c>00405db0</c>) for a group with a <see cref="PadLayout"/>.
	/// </summary>
	public static TKey? PadLead<TKey>(IReadOnlyDictionary<TKey, MissionMemberClaim> structureClaims, int group)
			where TKey : struct {
		TKey? lead = null;
		int leadMember = int.MaxValue;
		foreach (var (key, claim) in structureClaims) {
			if (claim.Group == group && claim.MemberIndex < leadMember) {
				leadMember = claim.MemberIndex;
				lead = key;
			}
		}

		return lead;
	}

	/// <summary>
	/// A herc's place in <paramref name="group"/> at <paramref name="memberIndex"/>: its own point, else the group's point
	/// spread by its <see cref="MechFormationTable"/> slot and rotated by the group's heading
	/// (<c>Mech_ApplyFormationOffset</c>'s <c>Formation_RotateAndAddOffset</c>, <c>00411d64</c>); its own heading, else the
	/// group's. With no point of its own its spawn height is read at the group's point.
	/// </summary>
	public static MissionMemberPlace PlaceHerc(MissionGroupSetup group, MechFormationTable mechFormations, int memberIndex,
			Vec3i? ownPoint, int? ownHeading) {
		var offset = mechFormations.OffsetFor(group.FormationId, memberIndex);
		var position = ownPoint
			?? (offset is { } spread ? FormationSpread(group.Point, group.Heading, spread.X, spread.Y) : group.Point);

		return new MissionMemberPlace(position, ownHeading ?? group.Heading,
			FormationOffset: offset is { } o ? (o.X, o.Y) : null,
			GroundPoint: ownPoint == null ? group.Point : null);
	}

	/// <summary>
	/// A squad machine's place: <paramref name="entryIndex"/> is its entry in <c>player.mec</c>, and it spreads from the
	/// squad's group as a herc of that group's member slot with no point or heading of its own.
	///
	/// <para>The squad spreads exactly as any other group does, because in the original it <i>is</i>
	/// one: <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) gives every <c>player.mec</c> entry
	/// the unset-position sentinel and writes the entries into record 0's member array in file
	/// order, so <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) attaches entry <i>i</i> as member
	/// slot <i>i</i> and <c>Mech_ApplyFormationOffset</c> (<c>00417898</c>) spreads every slot past
	/// the first. The same offset is the squadmate's post every tick it keeps formation on the
	/// player (<see cref="Sim.MechObject.FormationOffset"/>); without it the post is the player's own
	/// position, and <c>Mech_CollisionTest</c> refuses a position that overlaps another machine.</para>
	/// </summary>
	public static MissionMemberPlace PlaceSquadMember(MissionGroupSetup squadGroup, MechFormationTable mechFormations,
			int entryIndex) =>
		PlaceHerc(squadGroup, mechFormations, entryIndex, ownPoint: null, ownHeading: null);

	/// <summary>
	/// A flyer's place: its own point, else <c>Flyer_AttachToGroup</c> (<c>00421ee8</c>)'s unset-position branch, which
	/// takes the group's point and runs it through the flyer's own <c>+0x78</c> formation slot. The Z rides along, which is
	/// what staggers a flight vertically; the ground classes' offsets have none. Its own heading, else the group's.
	/// </summary>
	public static MissionMemberPlace PlaceFlyer(MissionGroupSetup group, FlyerFormationTable flyerFormations,
			int memberIndex, Vec3i? ownPoint, int? ownHeading) {
		var offset = flyerFormations.OffsetFor(group.FormationId, memberIndex);
		Vec3i position;
		if (ownPoint is { } own) {
			position = own;
		} else if (offset is { } slot) {
			var spread = FormationSpread(group.Point, group.Heading, slot.X, slot.Y);
			position = new Vec3i(spread.X, spread.Y, spread.Z + slot.Z);
		} else {
			position = group.Point;
		}

		return new MissionMemberPlace(position, ownHeading ?? group.Heading, FlyerFormationOffset: offset);
	}

	/// <summary>
	/// A structure's place: its own point, else the group's point spread by its <see cref="BaseFormationTable"/> slot
	/// (member 0, the group's first-claimed slot, always takes no offset — see that class's doc comment) and rotated by the
	/// group's own heading, mirroring <c>Formation_RotateAndAddOffset</c> (<c>00411d64</c>).
	///
	/// <para>Its own heading, else its group's heading plus its formation slot's own turn — see
	/// <see cref="BaseFormationTable.HeadingNudgeFor"/> for the trace and for the evidence that this is a real per-slot field
	/// and not a spread artefact. The cast is the original's: <c>Base_AttachToGroup</c> accumulates into a <c>short</c>, so
	/// the sum wraps rather than running past a full turn.</para>
	/// </summary>
	public static MissionMemberPlace PlaceStructure(MissionGroupSetup group, BaseFormationTable baseFormations,
			int memberIndex, Vec3i? ownPoint, int? ownHeading) {
		var offset = baseFormations.OffsetFor(group.FormationId, memberIndex);
		var position = ownPoint
			?? (offset is { } spread ? FormationSpread(group.Point, group.Heading, spread.X, spread.Y) : group.Point);
		int heading = ownHeading
			?? (short)(group.Heading + baseFormations.HeadingNudgeFor(group.FormationId, memberIndex));

		return new MissionMemberPlace(position, heading, FormationOffset: offset is { } o ? (o.X, o.Y) : null);
	}

	/// <summary>
	/// Rotates a formation offset by the group's heading and adds it to <paramref name="anchor"/> —
	/// the fixed-point 2D rotation <c>Formation_RotateAndAddOffset</c> (<c>00411d64</c>) reduces to
	/// for a heading-only object (no pitch/roll), shared by every formation kind.
	/// </summary>
	public static Vec3i FormationSpread(Vec3i anchor, int heading, int dx, int dy) {
		short cos = BinaryAngle.Cos(heading);
		short sin = BinaryAngle.Sin(heading);
		int worldDx = (int)(((long)dx * cos - (long)dy * sin + 0x2000) >> 14);
		int worldDy = (int)(((long)dx * sin + (long)dy * cos + 0x2000) >> 14);

		return new Vec3i(anchor.X + worldDx, anchor.Y + worldDy, anchor.Z);
	}
}
