using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn.Es1;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A mission group, the <c>0x28</c>-byte record <c>DBSim_BuildGroupRecord</c> (<c>ES1DBSIM 0001cff4</c>) fills from a
/// block-9 record (docs/es1/simulation/mission-groups.md#the-group-record).
/// </summary>
public sealed class Es1MissionGroup {
	/// <summary>The order a squad's <c>travelling</c> becomes, <c>patrolling</c>.</summary>
	public const short SquadPatrolOrder = 8;

	/// <summary>The order <see cref="SquadPatrolOrder"/> replaces in the first record.</summary>
	public const short TravellingOrder = 11;

	/// <summary><c>+0x00</c>, the members' kind: 0 mechs, 1 flyers, 2 bases.</summary>
	public short MemberKind { get; init; }

	/// <summary><c>+0x02</c>, the formation.</summary>
	public short Formation { get; init; }

	/// <summary><c>+0x04</c>, the order.</summary>
	public short Order { get; init; }

	/// <summary><c>+0x06</c>, the route cursor's index, 0 from the element constructor.</summary>
	public int RouteCursor { get; set; }

	/// <summary><c>+0x08</c>, the route: its waypoints' points, or null.</summary>
	public Vec3i[]? Route { get; init; }

	/// <summary>
	/// The block-1 point each waypoint of <see cref="Route"/> names. A waypoint group holds a pointer to each of its points
	/// (docs/es1/formats/script-dat.md), so two waypoints hold the same pointer exactly when they name the same point,
	/// which is what <see cref="AdvanceCursor"/> and <see cref="NextWaypointClosesRoute"/> compare.
	/// </summary>
	public int[]? RoutePointRefs { get; init; }

	/// <summary><c>+0x0c</c>, the heading.</summary>
	public short Heading { get; init; }

	/// <summary><c>+0x0e</c> and <c>+0x12</c>: the members, a pointer per non-negative ref, null where the pool had no object.</summary>
	public List<object?> Members { get; } = new();

	/// <summary><c>+0x14</c>, the side, the low byte of <c>.MSN</c> <c>0x4a</c>.</summary>
	public byte Side { get; init; }

	/// <summary><c>+0x16</c>, the action that holds the group out of the mission until it fires, or null.</summary>
	public Es1Action? Action { get; set; }

	/// <summary><c>+0x1a</c>, the target's kind: 0 a group, 1 a mech, 2 a flyer, 3 a base.</summary>
	public short TargetKind { get; init; }

	/// <summary><c>+0x1c</c>, the target: a group, a machine, or null.</summary>
	public object? Target { get; set; }

	/// <summary><c>+0x20</c>, the group's point, or null.</summary>
	public Vec3i? Point { get; init; }

	/// <summary><c>+0x24</c> and <c>+0x26</c>, the out-of-action (flag, operation) pair.</summary>
	public (short Flag, short Operation) OutOfAction { get; init; }

	/// <summary><see cref="WaypointAt"/> the cursor.</summary>
	public Vec3i? WaypointAtCursor => WaypointAt(RouteCursor);

	/// <summary>
	/// <c>Route_WaypointAt</c> (<c>0001cfb8</c>)(the cursor at <c>+6</c>, <paramref name="index"/>): null without a route or
	/// with the index not below its count, otherwise the waypoint's point
	/// (docs/es1/simulation/mission-groups.md#the-groups-target). A negative index, which no cursor reaches, answers null
	/// here: an engine guard.
	/// </summary>
	public Vec3i? WaypointAt(int index) => Route is { } route && index >= 0 && index < route.Length ? route[index] : null;

	/// <summary>
	/// <c>Route_AdvanceCursor</c> (<c>0001c8c0</c>): the cursor's index up by 1, and back to 0 when the route has more than
	/// one waypoint, the new index is its last, and that waypoint names the first's point
	/// (docs/es1/simulation/mission-groups.md#the-groups-target).
	/// </summary>
	public void AdvanceCursor() {
		RouteCursor++;
		if (RoutePointRefs is { Length: > 1 } refs && RouteCursor == refs.Length - 1 && refs[^1] == refs[0]) {
			RouteCursor = 0;
		}
	}

	/// <summary>
	/// <c>Route_NextWaypointClosesRoute</c> (<c>0001c908</c>): whether the waypoint after the cursor is the route's last and
	/// names the first's point (docs/es1/simulation/mission-groups.md#the-groups-target).
	/// </summary>
	public bool NextWaypointClosesRoute =>
		RoutePointRefs is { Length: > 0 } refs && RouteCursor + 1 == refs.Length - 1 && refs[^1] == refs[0];

	/// <summary>
	/// <c>Group_OrderTargetObject</c> (<c>0001cda0</c>), by <see cref="TargetKind"/>: 0, the target group's first member;
	/// 1 to 3, the target; otherwise null (docs/es1/simulation/mission-groups.md#the-groups-target). Retail tests no
	/// pointer; a target group with no first member, or none, answers null here: an engine guard.
	/// </summary>
	public IEs1SimObject? OrderTargetObject() => TargetKind switch {
		0 => Target is Es1MissionGroup { Members: [IEs1SimObject first, ..] } ? first : null,
		>= 1 and <= 3 => Target as IEs1SimObject,
		_ => null,
	};

	/// <summary>
	/// <c>Group_OrderTargetPosition</c> (<c>0001cde0</c>), by <see cref="TargetKind"/>: 0, the position of the target group's
	/// first member; 1 to 3, the target's position; otherwise the point of the route's first waypoint
	/// (docs/es1/simulation/mission-groups.md#the-groups-target). Retail tests no pointer; where it would read through a
	/// null one (no target object, no route), null here: an engine guard.
	/// </summary>
	public Vec3i? OrderTargetPosition() => TargetKind switch {
		>= 0 and <= 3 => OrderTargetObject()?.Position,
		_ => WaypointAt(0),
	};

	/// <summary>
	/// <c>Group_IsOrderTarget</c> (<c>0001ce3c</c>)(group, <paramref name="candidate"/>), by <see cref="TargetKind"/>: 0,
	/// whether the candidate's group is the target; 1 to 3, whether the candidate is the target; otherwise false
	/// (docs/es1/simulation/mission-groups.md#the-groups-target).
	/// </summary>
	public bool IsOrderTarget(IEs1SimObject candidate) => TargetKind switch {
		0 => ReferenceEquals(candidate.Group, Target),
		>= 1 and <= 3 => ReferenceEquals(candidate, Target),
		_ => false,
	};

	/// <summary>
	/// <c>Squad_BroadcastOrder</c> (<c>0001c950</c>)(group, <paramref name="order"/>)
	/// (docs/es1/simulation/mission-groups.md#the-squad-order-broadcast): each pass scores the members not yet told, not
	/// destroyed and not the issuer (<see cref="OrderScore"/>), keeps the highest, a tie going to the nearer to the issuer
	/// (<c>Math_DistanceBetweenPoints</c>), and sends it the order through its slot <c>+0x2c</c>
	/// (<see cref="Es1Mech.ReceiveSquadOrder"/>) with the reply so far: an acceptance makes the reply 2, a refusal a reply
	/// of 0 into 1. Verbs 0 and 2 stop at a reply of 2, the others once no member is left to tell. Answers whether the
	/// reply is 2, someone accepted. Retail reads every member as a machine; a member that is not a HERC, and an order
	/// with no issuer, are passed over here, engine guards.
	/// </summary>
	/// <param name="globals">The simulation's globals, which each receiver posts its reply through.</param>
	public bool BroadcastOrder(Es1SquadOrder order, Es1SimGlobals globals) {
		if (order.Issuer is not { } issuer) {
			return false;
		}

		var told = new bool[Members.Count];
		short reply = 0;
		while (true) {
			int nearest = int.MaxValue, chosenIndex = 0;
			short best = -1;
			Es1Mech? chosen = null;
			for (int i = 0; i < Members.Count; i++) {
				if (told[i] || Members[i] is not Es1Mech member || member.Destroyed || ReferenceEquals(member, issuer)
						|| OrderScore(member, order.Verb) is not { } score || best > score) {
					continue;
				}

				int distance = issuer.Position.ApproxDistanceTo(member.Position);
				if (distance < nearest || best < score) {
					best = score;
					chosenIndex = i;
					chosen = member;
					nearest = distance;
				}
			}

			if (chosen == null) {
				break;
			}

			if (chosen.ReceiveSquadOrder(reply, order, globals) != 0) {
				reply = 2;
			} else if (reply == 0) {
				reply = 1;
			}

			told[chosenIndex] = true;
			if (order.Verb is 0 or 2 && reply == 2) {
				break;
			}
		}

		return reply == 2;
	}

	/// <summary>
	/// The broadcast's score of <paramref name="member"/> for <paramref name="verb"/>, 1 or 0: for verbs 0, 2 and 5 its
	/// behaviour state's flag 1 (the descriptor's byte <c>+9</c>) clear, for 1, 8 and <c>0xf</c> set; for 4 its radar
	/// <c>+0x90</c> off, for 7 on. PLACEHOLDER: for any other verb retail assigns no score, and every member carries the one
	/// leftover stack value, told in range order when it is −1 or more and not at all below; the stack value is not known,
	/// so here no member is scored and none is told.
	/// </summary>
	private static short? OrderScore(Es1Mech member, short verb) {
		bool committed = member.Behaviour.Descriptor?.Flags[1] ?? false;
		return verb switch {
			0 or 2 or 5 => committed ? (short)0 : (short)1,
			1 or 8 or 0xf => committed ? (short)1 : (short)0,
			Es1SquadOrder.RadarOnVerb => member.RadarOn ? (short)0 : (short)1,
			Es1SquadOrder.RadarOffVerb => member.RadarOn ? (short)1 : (short)0,
			_ => null,
		};
	}

	/// <summary>
	/// <c>Math_HeadingToward</c> (<c>ES1DBSIM 00063cc8</c>), ES2's: the ground bearing from <paramref name="from"/> to
	/// <paramref name="to"/> less the quarter turn every bearing carries (docs/es1/simulation/fixed-point-math.md#helpers-that-match).
	/// </summary>
	public static short HeadingToward(Vec3i from, Vec3i to) =>
		(short)(SimTrig.Atan2Guarded(to.Y - from.Y, to.X - from.X) - BinaryAngle.QuarterTurn);

	/// <summary>
	/// The heading a block-9 record gives its group: its heading ref's, or with none the bearing of its route's first
	/// leg at full precision when the route has two waypoints or more, otherwise 0 (docs/es1/formats/script-dat.md#the-second-pass).
	/// </summary>
	public static short HeadingFor(Es1ScriptGroup record, Es1MissionScript script, Vec3i[]? route) =>
		record.HeadingRef >= 0 && record.HeadingRef < script.Headings.Length ? script.Headings[record.HeadingRef]
		: route is { Length: >= 2 } ? HeadingToward(route[0], route[1])
		: (short)0;
}

/// <summary>
/// <c>Mech_AttachToGroup</c> (<c>ES1DBSIM 00013124</c>) and <c>Mech_ApplyFormationOffset</c> (<c>00012ed0</c>): a member put
/// on its group's heading and point and spread by its formation slot (docs/es1/simulation/mech.md#construction,
/// docs/es1/formats/formations-dat.md#where-a-member-stands).
/// </summary>
public static class Es1Formation {
	/// <summary>
	/// <c>Mech_AttachToGroup</c>(<paramref name="mech"/>, <paramref name="slot"/>, <paramref name="group"/>): the group and
	/// slot stored; a rotation holding the sentinel becomes the group's heading; a position whose Z is negative becomes the
	/// group's point, or the route's waypoint at the cursor, raised by the record's ride height and spread by the formation.
	/// A group with neither leaves it, where the original reads the waypoint of no route.
	/// </summary>
	public static void Attach(Es1Mech mech, int slot, Es1MissionGroup group, Es1MechFormations formations) {
		mech.Group = group;
		mech.MemberSlot = slot;
		if (mech.Rotation.X == Es1Mech.RotationSentinel) {
			mech.Rotation = (0, 0, group.Heading);
		}

		if (mech.Position.Z < 0 && (group.Point ?? group.WaypointAtCursor) is { } point) {
			var position = new Vec3i(point.X, point.Y, point.Z + mech.Type.Chassis.RideHeight);
			mech.Position = ApplyOffset(mech, position, formations);
		}
	}

	/// <summary>
	/// <c>Mech_ApplyFormationOffset</c>: nothing for member slot 0; otherwise the slot's offset in the group's formation,
	/// rotated by the group's first member's frame and added (<c>Formation_RotateAndAddOffset</c>, <c>0000d9a0</c>). The
	/// original checks neither the formation nor the slot against the table; a formation outside it takes no offset here,
	/// which is this port's choice.
	/// </summary>
	public static Vec3i ApplyOffset(Es1Mech mech, Vec3i position, Es1MechFormations formations) {
		if (mech.MemberSlot == 0 || mech.Group is not { } group) {
			return position;
		}

		if (group.Formation < 0 || group.Formation >= formations.Formations.Length
				|| mech.MemberSlot - 1 >= formations.Formations[group.Formation].Slots.Length) {
			return position;
		}

		var offset = formations.Formations[group.Formation].Slots[mech.MemberSlot - 1];
		var leader = group.Members.Count > 0 && group.Members[0] is Es1Mech first ? first : mech;
		var (x, y) = Rotate2D(leader.Frame, offset.X, offset.Y);
		return new Vec3i(position.X + x, position.Y + y, position.Z + offset.Z);
	}

	/// <summary>
	/// <c>Math_Rotate2DPoint</c>, ES2's byte for byte (docs/es1/simulation/fixed-point-math.md#the-assembly-library): the
	/// point through the matrix's XY block, each sum rounded at Q14.
	/// </summary>
	public static (int X, int Y) Rotate2D(in Transform3 frame, int x, int y) => (
		(int)(((long)x * frame.M[0] + (long)y * frame.M[2] + 0x2000) >> 14),
		(int)(((long)x * frame.M[1] + (long)y * frame.M[3] + 0x2000) >> 14));
}
