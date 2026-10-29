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

	/// <summary>The condition of a target wholly lost, the top of the 0–4 scale.</summary>
	public const short ConditionLost = 4;

	/// <summary><see cref="ConditionTier"/>'s shares of the members lost, out of 1024, for tiers 3 and 2.</summary>
	public const short TierThreeLost = 500, TierTwoLost = 0xfa;

	/// <summary><see cref="ConditionTier"/>'s mean overall damages for tiers 3, 2 and 1.</summary>
	public const short TierThreeDamage = 0xc1, TierTwoDamage = 0x81, TierOneDamage = 0x33;

	/// <summary><see cref="TargetCondition"/>'s bands of a target object's overall damage, for conditions 1, 2 and 3.</summary>
	public const short ObjectBandOne = 0x32, ObjectBandTwo = 0x80, ObjectBandThree = 0xc0;

	/// <summary>
	/// <c>Group_ConditionTier</c> (<c>0000ecc0</c>)(group) (docs/es1/simulation/mission-status.md#the-groups-target-and-its-condition):
	/// with <i>lost</i> the members immobilized or destroyed and <i>mean</i> the <c>int16</c> sum of every member's overall
	/// damage (slot <c>+0x44</c>) over the count, <see cref="ConditionLost"/> when every member is lost; 3 when
	/// (<i>lost</i> &lt;&lt; 10) / count reaches <see cref="TierThreeLost"/> or <i>mean</i> <see cref="TierThreeDamage"/>; 2
	/// at <see cref="TierTwoLost"/> or <see cref="TierTwoDamage"/>; 1 when <i>mean</i> reaches <see cref="TierOneDamage"/>;
	/// otherwise 0. Retail reads every member unchecked and divides by the count; a missing member counts as neither lost
	/// nor damaged here, and a group with no members, where retail divides by 0, answers <see cref="ConditionLost"/>, as
	/// the all-lost test reads it, engine guards.
	/// </summary>
	public short ConditionTier() {
		short lost = 0, damage = 0;
		foreach (var entry in Members) {
			if (entry is not IEs1SimObject member) {
				continue;
			}

			if (member.Immobilized || member.Destroyed) {
				lost++;
			}

			damage += member.OverallDamage;
		}

		short count = (short)Members.Count;
		if (count == 0 || lost == count) {
			return ConditionLost;
		}

		short mean = (short)(damage / count);
		short share = (short)((lost << 10) / count);
		if (share >= TierThreeLost || mean >= TierThreeDamage) {
			return 3;
		}

		if (share >= TierTwoLost || mean >= TierTwoDamage) {
			return 2;
		}

		return mean >= TierOneDamage ? (short)1 : (short)0;
	}

	/// <summary>
	/// <c>Group_OrderSubjectCondition</c> (<c>0000ee84</c>)(group): for target kind 0 the target group's
	/// <see cref="ConditionTier"/>; otherwise <see cref="ConditionLost"/> for a target object destroyed or collapsed, and
	/// its overall damage banded at <see cref="ObjectBandOne"/>, <see cref="ObjectBandTwo"/> and
	/// <see cref="ObjectBandThree"/> as 0 to 3 (docs/es1/simulation/mission-status.md#the-groups-target-and-its-condition).
	/// Retail reads the target <c>+0x1c</c> unchecked; with none, or one of the wrong kind, 0 here, an engine guard.
	/// </summary>
	public short TargetCondition() {
		if (TargetKind == 0) {
			return Target is Es1MissionGroup group ? group.ConditionTier() : (short)0;
		}

		if (Target is not IEs1SimObject target) {
			return 0;
		}

		if (target.Destroyed || target.Collapsed) {
			return ConditionLost;
		}

		short damage = target.OverallDamage;
		return damage < ObjectBandOne ? (short)0 : damage < ObjectBandTwo ? (short)1 : damage < ObjectBandThree ? (short)2 : (short)3;
	}

	/// <summary>
	/// <c>Group_AllMembersClearOfThreats</c> (<c>0000f8b4</c>)(group): false at the first member not destroyed that is not
	/// clear of threats (<see cref="Es1MechDamage.IsClearOfThreats"/>), otherwise true
	/// (docs/es1/simulation/mission-status.md#clear-of-threats). A missing member is passed over here.
	/// </summary>
	public bool AllMembersClearOfThreats(Es1SimGlobals globals) {
		foreach (var entry in Members) {
			if (entry is IEs1SimObject { Destroyed: false } member && !Es1MechDamage.IsClearOfThreats(member, globals)) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <c>Group_OrderSubjectRouteExhausted</c> (<c>0000f930</c>)(group): whether the route of the target group, for target
	/// kind 0, or of the target object's group, has no waypoint after its cursor (<see cref="WaypointAt"/>)
	/// (docs/es1/simulation/mission-status.md#clear-of-threats). Retail reads the target and its group unchecked; with
	/// either missing, false here, an engine guard.
	/// </summary>
	public bool TargetRouteExhausted() {
		var group = TargetKind == 0 ? Target as Es1MissionGroup : (Target as IEs1SimObject)?.Group;
		return group != null && group.WaypointAt(group.RouteCursor + 1) == null;
	}

	/// <summary>
	/// <c>Group_OrderSubjectArrivedAndClear</c> (<c>0000f99c</c>)(group): for target kind 0, the target group holding no
	/// action (<c>+0x16</c>) and <see cref="AllMembersClearOfThreats"/>; otherwise the target object's group holding no
	/// action and the object clear of threats (docs/es1/simulation/mission-status.md#clear-of-threats). Retail reads the
	/// target and its group unchecked; with either missing, false here, an engine guard.
	/// </summary>
	public bool TargetArrivedAndClear(Es1SimGlobals globals) {
		if (TargetKind == 0) {
			return Target is Es1MissionGroup { Action: null } group && group.AllMembersClearOfThreats(globals);
		}

		return Target is IEs1SimObject { Group.Action: null } target && Es1MechDamage.IsClearOfThreats(target, globals);
	}

	/// <summary>
	/// <c>Group_IsWipedOut</c> (<c>0000ec24</c>)(group): whether every member is disarmed, immobilized or destroyed
	/// (docs/es1/simulation/mission-status.md#the-groups-target-and-its-condition). Retail reads every member unchecked; a
	/// missing one is passed over here.
	/// </summary>
	public bool IsWipedOut() =>
		Members.All(entry => entry is not IEs1SimObject member || member.Disarmed || member.Immobilized || member.Destroyed);

	/// <summary>
	/// <c>Group_NoRivalOrderOnSubject</c> (<c>0000ef2c</c>)(group): false when another of <paramref name="groups"/>
	/// (<c>MissionGroups</c>) has a different side, the same <see cref="Target"/> and is not <see cref="IsWipedOut"/>;
	/// otherwise true (docs/es1/simulation/mission-status.md#the-groups-target-and-its-condition). The targets are compared
	/// as pointers whatever their kind, so two groups with none match. This port has no caller, as <c>es2_xref.py</c> finds none
	/// in retail (docs/es1/simulation/mission-status.md#open).
	/// </summary>
	public bool NoRivalOnTarget(IReadOnlyList<Es1MissionGroup> groups) {
		foreach (var other in groups) {
			if (!ReferenceEquals(other, this) && other.Side != Side && ReferenceEquals(other.Target, Target) && !other.IsWipedOut()) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <c>Group_OrderSubjectEngaged</c> (<c>0000edd8</c>)(group): for target kind 0 the target group's
	/// <see cref="AnyMemberEngaged"/>, otherwise the target object's <c>+0x98</c>, engaged
	/// (docs/es1/simulation/mission-status.md#the-groups-target-and-its-condition). Retail reads the target unchecked; with
	/// none, or one of the wrong kind, false here, an engine guard. This port has no caller, as <c>es2_xref.py</c> finds none in
	/// retail (docs/es1/simulation/mission-status.md#open).
	/// </summary>
	public bool TargetEngaged() => TargetKind == 0
		? Target is Es1MissionGroup group && group.AnyMemberEngaged()
		: Target is IEs1SimObject { Engaged: true };

	/// <summary><c>Group_AnyMemberEngaged</c> (<c>0000ee18</c>): any member's <c>+0x98</c>, engaged.</summary>
	public bool AnyMemberEngaged() => Members.Any(entry => entry is IEs1SimObject { Engaged: true });

	/// <summary><c>Group_AnyMemberObjectiveSighted</c> (<c>0000efac</c>): any member's <c>+0x99</c> (<see cref="Es1Mech.ObjectiveReached"/>).</summary>
	public bool AnyMemberObjectiveSighted() => Members.Any(entry => entry is Es1Mech { ObjectiveReached: true });

	/// <summary><c>Group_AnyMemberDataLinked</c> (<c>0000f018</c>): any member's <c>+0x9a</c> (<see cref="Es1Mech.DataLinkComplete"/>).</summary>
	public bool AnyMemberDataLinked() => Members.Any(entry => entry is Es1Mech { DataLinkComplete: true });

	/// <summary><c>Group_AnyMemberScoredAKill</c> (<c>0000f084</c>): any member's <c>+0xa0</c> (<see cref="IEs1SimObject.ScoredAKill"/>).</summary>
	public bool AnyMemberScoredAKill() => Members.Any(entry => entry is IEs1SimObject { ScoredAKill: true });

	/// <summary>
	/// <c>Group_StatusLineIndex</c> (<c>0000f0f0</c>)(group, <paramref name="verb"/>), the group report, which
	/// <c>Mission_WriteResults</c> writes as the mission's outcome
	/// (docs/es1/simulation/mission-status.md#the-group-report--group_statuslineindex-0000f0f0): with <i>tier</i>
	/// <see cref="ConditionTier"/> held to at most 3, <i>c</i> <see cref="TargetCondition"/> and <i>a</i>
	/// <see cref="TargetArrivedAndClear"/>, a line by the verb, less 1; a verb outside 0–9 answers −2. Verb 3 reads the
	/// first member unchecked; with none it takes the arm for a destroyed one here, an engine guard.
	/// </summary>
	public short StatusLineIndex(short verb, Es1SimGlobals globals) {
		short tierRaw = ConditionTier();
		short tier = tierRaw == ConditionLost ? (short)3 : tierRaw;
		int line;
		switch (verb) {
			case 0:
				line = tierRaw == ConditionLost || !AnyMemberObjectiveSighted() ? tier + 6
					: !AnyMemberEngaged() ? 1
					: tier + 2;
				break;
			case 1:
				line = WaypointAt(RouteCursor + 1) != null ? tier + 6
					: tier == 0 && AnyMemberScoredAKill() ? 1
					: tier + 2;
				break;
			case 2:
				line = WaypointAt(RouteCursor + 1) != null ? tier + 6
					: tier == 0 && TargetCondition() == ConditionLost ? 1
					: tier + 2;
				break;
			case 3:
				line = Members is [IEs1SimObject { Destroyed: false }, ..] && AnyMemberDataLinked() ? tier + 1 : tier + 5;
				break;
			case 4: {
				short c = TargetCondition();
				bool a = TargetArrivedAndClear(globals);
				line = !a || c > 2 ? tier + 5
					: c == 0 && tier < 2 ? 1
					: Math.Max((int)tier, 1) + 1;
				break;
			}
			case 5:
				line = !TargetArrivedAndClear(globals) || !AnyMemberObjectiveSighted() || TargetCondition() == ConditionLost
					? tier + 5
					: tier + 1;
				break;
			case 6:
				line = TargetCondition() != ConditionLost ? tier + 6
					: !AnyMemberEngaged() && tier == 0 ? 1
					: tier + 2;
				break;
			case 7: {
				short c = TargetCondition();
				bool a = TargetArrivedAndClear(globals);
				line = !a || c == ConditionLost ? tier + 5
					: c == 0 && tier < 2 ? 1
					: c < 2 && tier <= 2 ? 2
					: c < 3 && tier <= 2 ? 3
					: 4;
				break;
			}
			case 8: {
				short c = TargetCondition();
				line = c == ConditionLost ? tier + 1
					: c >= 1 && tier == 0 ? 5
					: tier + 5;
				break;
			}
			case 9: {
				short c = TargetCondition();
				line = c == ConditionLost && tier == 0 ? 1
					: c >= 2 ? Math.Max((int)tier, 1) + 1
					: c == 1 && tier < 2 ? tier + 5
					: Math.Max((int)tier, 2) + 5;
				break;
			}
			default:
				line = -1;
				break;
		}

		return (short)(line - 1);
	}

	/// <summary>
	/// <c>Group_ApplyOutnumberedDamage</c> (<c>0001d4d0</c>)(group): <c>Mech_ApplyOutnumberedDamage</c>
	/// (<see cref="Es1MechDamage.ApplyOutnumberedDamage"/>) on each member in order
	/// (docs/es1/simulation/mission-groups.md#going-out-of-the-fight). Retail takes every member as a machine; one that is
	/// not a HERC is passed over here.
	/// </summary>
	public void ApplyOutnumberedDamage(Es1SimGlobals globals) {
		foreach (var entry in Members) {
			if (entry is Es1Mech member) {
				Es1MechDamage.ApplyOutnumberedDamage(member, globals);
			}
		}
	}

	/// <summary>The reach within which <see cref="NearestLiveMember"/> takes a member.</summary>
	public const int NearestMemberRange = 100000;

	/// <summary>
	/// <c>Group_NearestLiveMember</c> (<c>0001cea4</c>)(group, <paramref name="subject"/>)
	/// (docs/es1/simulation/mission-groups.md#the-groups-target): of the members other than the subject that are neither
	/// immobilized nor destroyed, the first nearest it (<c>Math_DistanceBetweenPoints</c>) under
	/// <see cref="NearestMemberRange"/>, or null. Retail reads every member unchecked; a missing one is passed over here.
	/// </summary>
	public IEs1SimObject? NearestLiveMember(IEs1SimObject subject) {
		int nearest = NearestMemberRange;
		IEs1SimObject? chosen = null;
		foreach (var entry in Members) {
			if (entry is not IEs1SimObject member || ReferenceEquals(member, subject) || member.Immobilized || member.Destroyed) {
				continue;
			}

			int distance = subject.Position.ApproxDistanceTo(member.Position);
			if (distance < nearest) {
				nearest = distance;
				chosen = member;
			}
		}

		return chosen;
	}

	/// <summary>
	/// <c>Group_ReportIfAllOutOfAction</c> (<c>0001d524</c>)(group, <paramref name="subject"/>)
	/// (docs/es1/simulation/mission-groups.md#going-out-of-the-fight): nothing while a member other than the subject is
	/// neither immobilized nor destroyed; otherwise, with the flag number of <see cref="OutOfAction"/> not negative, that
	/// flag written by its operation (<see cref="ApplyOutOfAction"/>). Retail reads every member unchecked; a missing one
	/// is passed over here.
	/// </summary>
	public void ReportIfAllOutOfAction(IEs1SimObject subject, Es1SimGlobals globals) {
		foreach (var entry in Members) {
			if (entry is IEs1SimObject member && !ReferenceEquals(member, subject) && !member.Immobilized && !member.Destroyed) {
				return;
			}
		}

		if (OutOfAction.Flag >= 0) {
			ApplyOutOfAction(OutOfAction.Flag, OutOfAction.Operation, globals.MissionVars);
		}
	}

	/// <summary>The out-of-action operations: add 1, clear, and the first and last that store the operation less <see cref="StoreBase"/>.</summary>
	public const short AddOne = 2, Clear = 1, FirstStore = 0xd, LastStore = 0x10, StoreBase = 0xc;

	/// <summary>
	/// The write both out-of-action reports make, each looking <paramref name="operation"/> up by <c>REPNE SCASD</c>
	/// (docs/es1/simulation/sim-object-base.md#the-out-of-action-report--mech_reportoutofaction-0000d77c): flag
	/// <paramref name="flag"/> of <paramref name="vars"/> (<c>MissionVars</c>) up by 1 for <see cref="AddOne"/>, 0 for
	/// <see cref="Clear"/>, the operation less <see cref="StoreBase"/> for <see cref="FirstStore"/> to
	/// <see cref="LastStore"/>; any other operation writes nothing. Retail writes a flag past the array unchecked; nothing
	/// is written for one here.
	/// </summary>
	public static void ApplyOutOfAction(short flag, short operation, short[] vars) {
		if (flag < 0 || flag >= vars.Length) {
			return;
		}

		if (operation == AddOne) {
			vars[flag]++;
		} else if (operation == Clear) {
			vars[flag] = 0;
		} else if (operation is >= FirstStore and <= LastStore) {
			vars[flag] = (short)(operation - StoreBase);
		}
	}

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
		return RotateAndAddOffset(mech, group, position, new Vec3i(offset.X, offset.Y, offset.Z));
	}

	/// <summary>
	/// <c>Formation_RotateAndAddOffset</c> (<c>0000d9a0</c>)(<paramref name="member"/>, <paramref name="position"/>,
	/// <paramref name="offset"/>): the offset's X and Y turned by the frame of the group's first member, and its Z unturned,
	/// added to the position (docs/es1/formats/formations-dat.md#where-a-member-stands). Retail reads the first member
	/// unchecked; with none the member's own frame turns it here, an engine guard.
	/// </summary>
	public static Vec3i RotateAndAddOffset(IEs1SimObject member, Es1MissionGroup group, Vec3i position, Vec3i offset) {
		var leader = group.Members.Count > 0 && group.Members[0] is IEs1SimObject first ? first : member;
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

/// <summary>
/// <c>Flyer_AttachToGroup</c> (<c>ES1DBSIM 0001b574</c>) and <c>Flyer_ApplyFormationOffset</c> (<c>0001b524</c>): a
/// flyer put on its group's heading and point and spread by its <c>FFORMS.DAT</c> slot
/// (docs/es1/simulation/flyers.md#joining-its-group--flyer_attachtogroup-0001b574,
/// docs/es1/formats/formations-dat.md#where-a-member-stands).
/// </summary>
public static class Es1FlyerGroups {
	/// <summary>
	/// <c>Flyer_AttachToGroup</c>(<paramref name="flyer"/>, <paramref name="slot"/>, <paramref name="group"/>): the group and
	/// slot stored; a rotation whose first angle holds the sentinel becomes (0, 0, the group's heading) with <c>+0x2c</c>
	/// cleared; a position whose Z is negative becomes the group's point, or the route's waypoint at the cursor, spread by
	/// the formation; then a Z of 0 becomes <see cref="Es1Flyer.DefaultAltitude"/>. A group with neither point nor
	/// waypoint leaves the position, where the original reads the waypoint of no route, an engine guard.
	/// </summary>
	public static void Attach(Es1Flyer flyer, int slot, Es1MissionGroup group, Es1FlyerFormations formations) {
		flyer.Group = group;
		flyer.MemberSlot = slot;
		if (flyer.Rotation.X == Es1Mech.RotationSentinel) {
			flyer.Rotation = (0, 0, group.Heading);
			flyer.RotationWord = 0;
		}

		if (flyer.Position.Z < 0 && (group.Point ?? group.WaypointAtCursor) is { } point) {
			flyer.Position = ApplyOffset(flyer, point, formations);
		}

		if (flyer.Position.Z == 0) {
			flyer.Position = new Vec3i(flyer.Position.X, flyer.Position.Y, Es1Flyer.DefaultAltitude);
		}
	}

	/// <summary>
	/// <c>Flyer_ApplyFormationOffset</c>(<paramref name="flyer"/>, <paramref name="position"/>): nothing for member slot 0;
	/// otherwise <c>FlyerFormation_GetSlotOffset</c> (<c>0001c7f0</c>)(slot, the group's formation), its three
	/// <c>int16</c> widened, through <see cref="Es1Formation.RotateAndAddOffset"/>. Neither number is checked: the slot
	/// is the formation's (slot − 1)th of three counted on through the table, so a member past the third takes the next
	/// formation's first slot, as <c>B0_C7_3A</c>'s group 148 does. An index outside the table, which retail reads past
	/// the array, takes no offset here, an engine guard.
	/// </summary>
	public static Vec3i ApplyOffset(Es1Flyer flyer, Vec3i position, Es1FlyerFormations formations) {
		if (flyer.MemberSlot == 0 || flyer.Group is not { } group) {
			return position;
		}

		int index = group.Formation * Es1FlyerFormation.SlotCount + flyer.MemberSlot - 1;
		int formation = index / Es1FlyerFormation.SlotCount;
		if (index < 0 || formation >= formations.Formations.Length
				|| index % Es1FlyerFormation.SlotCount >= formations.Formations[formation].Slots.Length) {
			return position;
		}

		var offset = formations.Formations[formation].Slots[index % Es1FlyerFormation.SlotCount];
		return Es1Formation.RotateAndAddOffset(flyer, group, position, new Vec3i(offset.X, offset.Y, offset.Z));
	}
}
