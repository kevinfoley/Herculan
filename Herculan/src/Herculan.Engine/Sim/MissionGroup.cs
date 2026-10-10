using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// A mission group — the in-memory record <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>) builds
/// from one <c>script.dat</c> block-11 entry, and the thing every object points at through
/// <c>obj+0x45</c>.
///
/// <para><b>The group, not the object, is what the AI is driven from.</b>
/// <c>Mech_AiTick</c>'s sole caller is <c>Group_OrderTick</c> (<c>00423a74</c>), which runs it over
/// every member of a group that has entered the mission — so a machine that is not a live group
/// member never thinks at all. That function also walks the group through its orders, which is what
/// gives each member a state to be in; see docs/retail/simulation/ai-goals.md for the whole layer and
/// docs/retail/simulation/ai-dispatch.md for what a verb turns into.</para>
/// </summary>
public sealed partial class MissionGroup {
	/// <summary>
	/// The verb <c>Mech_AiSelectBehaviour</c> substitutes for a null order entry — see
	/// <see cref="MissionOrder.VerbNone"/>.
	/// </summary>
	public const short NoOrder = MissionOrder.VerbNone;

	/// <summary>
	/// The condition tier <c>Group_OrderSubjectCondition</c> (<c>00412dc4</c>) answers for a subject
	/// that is gone. It is what every completion test asks about.
	/// </summary>
	public const int ConditionDestroyed = 4;

	/// <param name="index">Which block-11 record this is.</param>
	/// <param name="kind">Which roster its members came from — the record's own discriminator.</param>
	/// <param name="side">Whose side it is on.</param>
	/// <param name="orders">Its ten order slots.</param>
	/// <param name="deploymentAction">
	/// <c>group+0x14</c> — the block-5 action the group is waiting on, or null for a group that is in
	/// the mission from the start. <b>This is the gate</b>: while it is set the group is not in the
	/// world, and clearing it is what puts it there. See <see cref="AwaitingDeployment"/>.
	/// </param>
	public MissionGroup(int index, MissionUnitKind kind, MissionSide side,
			IReadOnlyList<MissionOrder?> orders, MissionActionState? deploymentAction = null) {
		Index = index;
		Kind = kind;
		Side = side;
		_orders = orders;
		_deploymentAction = deploymentAction;
		_subjectGroups = new MissionGroup?[orders.Count];
		_subjectObjects = new SimObject?[orders.Count];
		_orderActions = new MissionActionState?[orders.Count];
		_completed = new bool[orders.Count];
		Route = orders.Count > 0 ? orders[0]?.Route ?? Array.Empty<Vec3i>() : Array.Empty<Vec3i>();
	}

	/// <summary>Which block-11 record this is, which is also what a placement's group index names.</summary>
	public int Index { get; }

	/// <summary><c>group+0x12</c> — the side every member of the group is on.</summary>
	public MissionSide Side { get; }

	/// <summary>
	/// <c>group+0x00</c> — the block-11 record's roster discriminator, and so what class every member
	/// of the group is. Read by the three trigger subject types that sweep by class; see
	/// <see cref="MissionTriggers.Evaluate"/>.
	/// </summary>
	public MissionUnitKind Kind { get; }

	/// <summary>
	/// <c>group+0x0c</c>/<c>+0x10</c> — the member array and its count, in attachment order.
	/// </summary>
	public IReadOnlyList<SimObject> Members => _members;

	/// <summary>
	/// The group's first member, <c>**(group+0x0c)</c>. The AI reads it for two decisions and both
	/// only ask whether it is the local player: whether this group is the player's own squad (which
	/// keeps its radar passive) and whether a machine is the one that drags the rest into a fight.
	/// </summary>
	public SimObject? Leader => _members.Count > 0 ? _members[0] : null;

	/// <summary>Whether the group is led by the machine the player is flying.</summary>
	public bool LedByPlayer => Leader is { LocallyPiloted: true };

	/// <summary><c>group+0x44</c> — the ten order slots, unset ones left null.</summary>
	public IReadOnlyList<MissionOrder?> Orders => _orders;

	/// <summary><c>group+0x6c</c> — which slot the group is working.</summary>
	public int OrderIndex { get; private set; }

	/// <summary>The order in force, or null when the slot is empty.</summary>
	public MissionOrder? CurrentOrder =>
		OrderIndex >= 0 && OrderIndex < _orders.Count ? _orders[OrderIndex] : null;

	/// <summary>
	/// The current order's verb, or <see cref="NoOrder"/> when the slot is empty — the
	/// "<c>or 0x0b</c>" idiom the original's verb readers spell out inline.
	/// </summary>
	public short OrderVerb => CurrentOrder?.Verb ?? NoOrder;

	/// <summary>
	/// <c>group+0x06</c> — the waypoint group the route cursor runs over. Loaded once, from order
	/// slot 0, and kept however many orders the group works through: no writer that re-points it has
	/// been found. See docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once.
	/// </summary>
	public IReadOnlyList<Vec3i> Route { get; }

	/// <summary>
	/// <c>group+0x04</c> — the index of the waypoint last reached, shared by the whole group. The four
	/// callers of <c>Route_AdvanceCursor</c> advance it — <c>Ai_FollowRoute</c>, the player's own
	/// think, and the flyer and ground-vehicle leaders' route steps — and moving on to the next order
	/// does not reset it; see docs/retail/simulation/player-waypoints.md.
	/// </summary>
	public int RouteCursor { get; private set; }

	/// <summary>
	/// <c>Route_AdvanceCursor</c> (<c>0042313c</c>) — steps the cursor by one and <b>wraps it back to
	/// zero when the route closes on itself</b>: with more than one waypoint, an index that has just
	/// landed on the last one whose point is the same as the first restarts at zero. A closed route is
	/// a patrol that never ends, and never completes its order; an open one runs out, and running out
	/// is what finishes a movement order.
	///
	/// <para>The original compares the two waypoints' resolved <i>pointers</i>, which is identity. The
	/// route here is a list of coordinates, so this compares positions — the same answer for a mission
	/// that closes a route by naming one point twice, which is how every retail one does it.</para>
	/// </summary>
	internal void AdvanceRouteCursor() {
		RouteCursor++;

		if (Route.Count > 1 && RouteCursor == Route.Count - 1
				&& Route[Route.Count - 1] == Route[0]) {
			RouteCursor = 0;
		}
	}

	/// <summary>
	/// <c>Route_WaypointAt</c> (<c>00423b0c</c>) — a waypoint of the group's route, or null when
	/// there is no route or the index is past its end. Asking for <see cref="RouteCursor"/><c> + 1</c>
	/// is how every "is there anywhere left to go" test in the AI is spelled.
	/// </summary>
	public Vec3i? WaypointAt(int index) =>
		index >= 0 && index < Route.Count ? Route[index] : null;

	/// <summary>
	/// <c>Route_NextWaypointClosesRoute</c> (<c>00423170</c>) — whether the waypoint after the cursor is the duplicate a closed route
	/// ends with: the one <see cref="AdvanceRouteCursor"/> would immediately wrap off. Only the
	/// player's think asks, and it is what stops a patrol circuit announcing a waypoint the player has
	/// already been told about, at the point it started from.
	/// </summary>
	public bool NextWaypointClosesRoute =>
		Route.Count > 1 && RouteCursor + 1 == Route.Count - 1
			&& Route[RouteCursor + 1] == Route[0];

	/// <summary>
	/// <c>group+0x70</c> — whether the order in that slot has been flagged finished. Set only by the
	/// completion path, not by a mission action firing under an unfinished order. The objective layer
	/// reads it through <see cref="OrderCompletedForRoute"/>; no AI reader has been found — see
	/// docs/retail/simulation/ai-goals.md#open.
	/// </summary>
	public bool OrderCompleted(int slot) =>
		slot >= 0 && slot < _completed.Length && _completed[slot];

	/// <summary>
	/// <c>Mission_GroupOrderCompleteOnRoute</c> (<c>0041324c</c>) — whether the order slot that runs
	/// a given block-3 waypoint group has been flagged finished. The mission objective layer is the
	/// only caller, and it is how a "get there" objective names <i>which</i> of a group's ten orders
	/// it is about: not by slot, but by the route that slot runs on.
	///
	/// <para>The original matches the resolved waypoint-group <i>pointer</i>; the ref is compared
	/// here, which is the same identity for a table nothing rebuilds. A slot with no order, or one
	/// whose order names no route, matches nothing — including an objective that names no route
	/// either, since a negative ref cannot equal a slot's.</para>
	/// </summary>
	public bool OrderCompletedForRoute(int routeRef) {
		if (routeRef < 0) {
			return false;
		}

		for (int slot = 0; slot < _orders.Count && slot < MissionOrder.Slots; slot++) {
			if (_orders[slot] is { } order && order.RouteRef == routeRef) {
				return OrderCompleted(slot);
			}
		}

		return false;
	}

	/// <summary>The group the current order names, when it names one.</summary>
	public MissionGroup? OrderSubjectGroup =>
		OrderIndex >= 0 && OrderIndex < _subjectGroups.Length ? _subjectGroups[OrderIndex] : null;

	/// <summary>The object the current order names, when it names one.</summary>
	public SimObject? OrderSubjectObject =>
		OrderIndex >= 0 && OrderIndex < _subjectObjects.Length ? _subjectObjects[OrderIndex] : null;

	/// <summary>
	/// <c>Group_OrderTargetObject</c> (<c>004238a0</c>) — the object the current order names: the
	/// first member of the group it holds, or the object directly.
	/// </summary>
	public SimObject? OrderTarget => CurrentOrder?.SubjectKind switch {
		MissionOrderSubject.Group => OrderSubjectGroup?.Leader,
		MissionOrderSubject.Mech or MissionOrderSubject.Flyer or MissionOrderSubject.Base =>
			OrderSubjectObject,
		_ => null
	};

	/// <summary>
	/// <c>Group_OrderTargetPosition</c> (<c>004238d4</c>) — the position the current order works to:
	/// the subject's own origin, falling back to the first waypoint of the group's route when the
	/// order names nothing.
	/// </summary>
	public Vec3i? OrderTargetPosition => CurrentOrder?.SubjectKind switch {
		MissionOrderSubject.Group => OrderSubjectGroup?.Leader?.Position,
		MissionOrderSubject.Mech or MissionOrderSubject.Flyer or MissionOrderSubject.Base =>
			OrderSubjectObject?.Position,
		_ => WaypointAt(0)
	} ?? WaypointAt(0);

	/// <summary>
	/// <c>Group_IsOrderTarget</c> (<c>00423918</c>) — whether an object is what the current order
	/// names, by group identity for a group-valued order and by object identity otherwise.
	/// </summary>
	public bool IsOrderTarget(SimObject candidate) => CurrentOrder?.SubjectKind switch {
		MissionOrderSubject.Group => OrderSubjectGroup is { } subject
			&& ReferenceEquals(candidate.Group, subject),
		MissionOrderSubject.Mech or MissionOrderSubject.Flyer or MissionOrderSubject.Base =>
			OrderSubjectObject is { } subject && ReferenceEquals(candidate, subject),
		_ => false
	};

	/// <summary>
	/// <c>Group_NearestLiveMember</c> (<c>00423974</c>) — the live member nearest a given object, within 100000 units, excluding
	/// that object itself. The raycast's friendly-fire path asks the player's own group for the member
	/// nearest the player, to pick who complains when he shoots a friendly outside the group.
	/// </summary>
	public SimObject? NearestLiveMember(SimObject excluding) {
		SimObject? best = null;
		int bestDistance = 100000;

		for (int i = 0; i < _members.Count; i++) {
			var member = _members[i];
			if (ReferenceEquals(member, excluding) || member.Neutralised) {
				continue;
			}

			int distance = member.Position.ApproxDistanceTo(excluding.Position);
			if (distance < bestDistance) {
				best = member;
				bestDistance = distance;
			}
		}

		return best;
	}

	/// <summary>
	/// <c>group+0x1c</c>/<c>+0x30</c> — the mission-counter writes the group makes once all of it is out
	/// of the fight, from its block-11 record. See <see cref="ReportIfAllOthersOutOfAction"/>.
	/// </summary>
	public OutOfActionReport OutOfActionReport { get; set; } = OutOfActionReport.None;

	/// <summary>
	/// <c>Group_ReportIfAllOutOfAction</c> (<c>00423f30</c>) — runs the group's writes if every member
	/// but <paramref name="reporting"/> is destroyed or immobilised. The one reporting is skipped
	/// rather than tested because its own flag is not set yet at every call site.
	///
	/// <para>Nothing latches it. It runs each time a member reports, and only the report of the last
	/// member still standing finds all the others down, so a group writes once — a member reports only
	/// on going out of the fight, and never twice.</para>
	/// </summary>
	internal void ReportIfAllOthersOutOfAction(SimWorld world, SimObject reporting) {
		for (int i = 0; i < _members.Count; i++) {
			var member = _members[i];
			if (!ReferenceEquals(member, reporting) && !member.Neutralised) {
				return;
			}
		}

		world.Mission.ApplyOutOfActionReport(OutOfActionReport);
	}

	/// <summary>
	/// Attaches an object to the group, in the order the mission places them — the first one attached
	/// is the <see cref="Leader"/>.
	/// </summary>
	public void Add(SimObject member) {
		_members.Add(member);
		member.Group = this;
	}

	/// <summary>
	/// Resolves one order slot's subject to the group or object it names. This is a separate step
	/// because an order may name a group that has not been built when its own group is, which is the
	/// same reason <c>DBSim_SpawnMissionObjects</c> resolves the order array before it builds any
	/// group record.
	/// </summary>
	public void BindOrderSubject(int slot, MissionGroup? subjectGroup, SimObject? subjectObject) {
		if (slot < 0 || slot >= _orders.Count) {
			return;
		}

		_subjectGroups[slot] = subjectGroup;
		_subjectObjects[slot] = subjectObject;
	}

	/// <summary>
	/// <c>Group_OrderTick</c> (<c>00423a74</c>) — advance the group through its orders, then run the
	/// AI over every member.
	///
	/// <para>An order ends in one of two ways: it finishes on its own terms, which also flags it at
	/// <c>group+0x70</c>, or the mission action it hangs on fires, which does not. Either way the
	/// group only moves on while the <i>next</i> slot holds an order, so a group that finishes its
	/// last one stays on it for the rest of the mission. Advancing zeroes every member's dwell
	/// countdown, which forces each one's reassess on the very next tick — that is what makes a new
	/// order take effect at once rather than after the old state's dwell.</para>
	///
	/// <para>The original ticks every member unconditionally; the filtering is <c>Mech_AiTick</c>'s,
	/// and it is by whether the machine has a behaviour descriptor at all, which is how the base
	/// groups pass through harmlessly. The <see cref="SimObject.Removed"/> test here stands in for
	/// that. There is no deployment test: a group that has not arrived never reaches this function —
	/// it runs <see cref="DeploymentCheck"/> instead.</para>
	/// </summary>
	public void AiTick(SimWorld world) {
		var order = CurrentOrder;
		bool advance = false;

		if (order != null) {
			if (IsOrderComplete(world, order)) {
				advance = true;
				_completed[OrderIndex] = true;
			} else if (order.GatedOnAction && ActionActivated) {
				advance = true;
			}
		}

		if (advance && OrderIndex + 1 < _orders.Count && _orders[OrderIndex + 1] != null) {
			OrderIndex++;

			for (int i = 0; i < _members.Count; i++) {
				switch (_members[i]) {
					case MechObject member:
						member.Behaviour.DwellCountdown = 0;
						break;
					case FlyerObject aircraft:
						aircraft.Behaviour.DwellCountdown = 0;
						break;
				}
			}
		}

		for (int i = 0; i < _members.Count; i++) {
			switch (_members[i]) {
				case MechObject { Removed: false } mech:
					mech.AiTick(world);
					break;
				case FlyerObject { Removed: false } aircraft:
					aircraft.AiTick(world);
					break;
			}
		}
	}

	/// <summary>
	/// <c>Group_IsOrderComplete</c> (<c>004239fc</c>) — switched on the verb. Verbs 1 and 4 have no
	/// test at all and can only be ended by their action firing; the three movement verbs end when
	/// the route runs out; the two subject verbs end when the subject does. See
	/// docs/retail/simulation/ai-goals.md for the table.
	/// </summary>
	private bool IsOrderComplete(SimWorld world, MissionOrder order) => order.Verb switch {
		MissionOrder.VerbSearchDestroy => SubjectCondition() == ConditionDestroyed,

		MissionOrder.VerbGuard => (OrderSubjectGroup != null || OrderSubjectObject != null)
			&& (SubjectCondition() == ConditionDestroyed || NoRivalOrderOnSubject(world)),

		MissionOrder.VerbPatrol or MissionOrder.VerbTravel or MissionOrder.VerbFollow =>
			WaypointAt(RouteCursor + 1) == null,

		_ => false
	};

	/// <summary>
	/// <c>Group_OrderSubjectCondition</c> (<c>00412dc4</c>) — how far gone the thing the current
	/// order names is, on a 0-4 scale where 4 is destroyed.
	///
	/// <para>An unresolved subject answers 0. The original would dereference a null there, so this
	/// is the engine's own answer, chosen because "not destroyed" keeps an order that names something
	/// the mission never placed from finishing the instant it starts.</para>
	/// </summary>
	private int SubjectCondition() {
		if (CurrentOrder?.SubjectKind == MissionOrderSubject.Group) {
			return OrderSubjectGroup is { } subject ? subject.ConditionTier() : 0;
		}

		if (OrderSubjectObject is not { } target) {
			return 0;
		}

		// Destroyed, not neutralised: the original reads +0x99 and +0xb4 here and NOT the
		// immobilised +0xa4, so a HERC whose legs are gone has not completed a "destroy this" order
		// against it. It is still standing and still shooting, and the order stays open until it is
		// actually killed.
		if (target.Destroyed || target is MechObject { Collapsed: true }) {
			return ConditionDestroyed;
		}

		int damage = target.OverallDamage;

		return damage < 0x32 ? 0 : damage < 0x80 ? 1 : damage < 0xc0 ? 2 : 3;
	}

	/// <summary>
	/// <c>Group_ConditionTier</c> (<c>00412c8c</c>) — a group's own 0-4 condition, from how many
	/// members it has lost and how hurt the whole roster is. The damage mean counts every member,
	/// the dead included, which is what lets a group be written off by damage alone.
	/// </summary>
	public int ConditionTier() {
		if (_members.Count == 0) {
			return 0;
		}

		int lost = 0;
		int damage = 0;

		for (int i = 0; i < _members.Count; i++) {
			var member = _members[i];

			if (member.Removed || member.Neutralised) {
				lost++;
			}

			damage += member.OverallDamage;
		}

		if (lost == _members.Count) {
			return ConditionDestroyed;
		}

		int average = damage / _members.Count;
		int lostFraction = (lost << 10) / _members.Count;

		return lostFraction >= 0x28a || average >= 0xc1 ? 3
			: lostFraction >= 0xfa || average >= 0x81 ? 2
			: average >= 0x33 ? 1
			: 0;
	}

	/// <summary>
	/// <c>Group_NoRivalOrderOnSubject</c> (<c>00412e74</c>) — whether no group on the other side,
	/// still holding a live member, has a current order naming the same subject. The other half of a
	/// guard order's completion: the post is finished when nothing is assigned against it any more,
	/// not merely when it survives.
	/// </summary>
	private bool NoRivalOrderOnSubject(SimWorld world) {
		for (int i = 0; i < world.Groups.Count; i++) {
			var rival = world.Groups[i];

			if (ReferenceEquals(rival, this) || rival.Side == Side || rival.CurrentOrder == null) {
				continue;
			}

			bool sameSubject = ReferenceEquals(rival.OrderSubjectGroup, OrderSubjectGroup)
				&& ReferenceEquals(rival.OrderSubjectObject, OrderSubjectObject);

			if (sameSubject && !rival.IsWipedOut()) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <c>Group_IsWipedOut</c> (<c>00412be4</c>) — whether every member is out of the fight. One
	/// member still in it is enough to answer no.
	///
	/// <para><b>It reads <see cref="SimObject.OutOfAction"/>, not <see cref="SimObject.Neutralised"/></b>
	/// — the original's test is all three of <c>+0x99</c>, <c>+0xa4</c> and <c>+0xa5</c>, so a
	/// machine that is alive and mobile but has lost every weapon counts as gone. That is the point
	/// of the question rather than an oversight: the only caller asks whether an enemy group can
	/// still contest a guard post, and a disarmed machine cannot. Despite the name this is not a
	/// "has this group been destroyed" test and must not be reused as one — see
	/// docs/retail/simulation/ai-goals.md.</para>
	/// </summary>
	private bool IsWipedOut() {
		for (int i = 0; i < _members.Count; i++) {
			var member = _members[i];

			if (!member.Removed && !member.OutOfAction) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <c>order+0x12</c> — whether the mission action the current order hangs on has activated. An
	/// order gated on one ends when it activates, whether or not the order finished on its own terms; see
	/// <see cref="AiTick"/>.
	/// </summary>
	private bool ActionActivated =>
		OrderIndex >= 0 && OrderIndex < _orderActions.Length
			&& _orderActions[OrderIndex] is { Activated: true };

	/// <summary>
	/// Resolves one order slot's <c>+0x12</c> action, the same separate step
	/// <see cref="BindOrderSubject"/> is and for the same reason.
	/// </summary>
	public void BindOrderAction(int slot, MissionActionState? action) {
		if (slot >= 0 && slot < _orderActions.Length) {
			_orderActions[slot] = action;
		}
	}

	private readonly IReadOnlyList<MissionOrder?> _orders;
	private readonly MissionGroup?[] _subjectGroups;
	private readonly SimObject?[] _subjectObjects;
	private readonly MissionActionState?[] _orderActions;
	private readonly bool[] _completed;
	private readonly List<SimObject> _members = new();
}
