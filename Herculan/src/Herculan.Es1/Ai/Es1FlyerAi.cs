using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// A flyer's orders and thinks (docs/es1/simulation/flyers.md#orders--flyer_aiselectbehaviour-0001c46c,
/// docs/es1/simulation/flyers.md#the-thinks): the reassess that installs the group's order, the route the group's first
/// member flies and the station the others keep on it, and the sweeps that send the flight to <c>attacking</c>. The
/// leader is the first of the group's member array (<c>+0xe</c>).
/// </summary>
public static class Es1FlyerAi {
	/// <summary>The ground range to the next waypoint inside which <see cref="LeadRouteStep"/> steps the route cursor.</summary>
	public const int WaypointReach = 15000;

	/// <summary><see cref="LeadRouteStep"/>'s Q10 gain on the bearing error.</summary>
	public const short RouteSteerGain = 800;

	/// <summary>The height the route and the station are flown at.</summary>
	public const int CruiseAltitude = 10000;

	/// <summary>The formation throttle the leader sets, and the one a member beyond <see cref="ThrottleRange"/> sets.</summary>
	public const short LeaderThrottle = 0xb4, FarThrottle = 0x100;

	/// <summary><see cref="FormationThrottle"/>'s clamp.</summary>
	public const short MinThrottle = 0x8c, MaxThrottle = 0x100;

	/// <summary>How far ahead of the leader along its heading a member's station lies, and the base its throttle measures from.</summary>
	public const int StationLead = 0x2000;

	/// <summary>The range to the station inside which <see cref="FormationStep"/> may mirror the steering.</summary>
	public const int MirrorRange = 10000;

	/// <summary>The range to the station up to which <see cref="FormationStep"/> runs <see cref="FormationThrottle"/>.</summary>
	public const int ThrottleRange = 15000;

	/// <summary>The half-arc, as the unsigned test angle + <c>0x4000</c> &lt; <c>0x8000</c>, of <see cref="FormationStep"/>'s two tests.</summary>
	private const int HalfArc = 0x4000, Arc = 0x8000;

	/// <summary>The target sweep's countdown, the counter <c>+0x55</c> of the behaviour scratch's record <c>+0x54</c>, and its reload.</summary>
	public const int SweepCountdownOffset = 0x55;

	/// <inheritdoc cref="SweepCountdownOffset"/>
	public const short SweepInterval = 10000;

	/// <summary>The masks of the patrol's and search and destroy's sweeps.</summary>
	public const short PatrolMask = Es1Targets.OtherClassOnly, SearchDestroyMask = Es1Targets.OtherClassOnly | Es1Targets.DesignatedOnly;

	/// <summary>
	/// <c>Flyer_AiSelectBehaviour</c> (<c>ES1DBSIM 0001c46c</c>), the reassess: the state the group's order (<c>+4</c>)
	/// names (<see cref="StateForOrder"/>), installed with <c>Behaviour_SetState</c>; answers 1.
	/// </summary>
	public static short SelectBehaviour(Es1Flyer flyer, Es1SimGlobals globals) {
		// Retail reads the group through +0x3f unchecked; a flyer with no group is taken as one whose order has no case, an
		// engine guard.
		if (flyer.Group is { } group && StateForOrder(group.Order) is { } state) {
			flyer.Behaviour.SetState(Es1FlyerBehaviour.States[state], globals.DwellJitter);
		}

		// An order with no case installs what ESI holds, the address of this function's own code, as the
		// descriptor (docs/es1/simulation/flyers.md#orders--flyer_aiselectbehaviour-0001c46c). No retail flyer group has
		// such an order (HercWorks.Query orders: the 325 flyer group records carry 1, 6, 8, 10 or 11); here the flyer stays
		// in its state.
		return 1;
	}

	/// <summary>
	/// The jump table <c>00092084</c> over the orders 0–11: 1 <c>search and destroy</c>, 6 and 10 <c>sleeping</c>, 7 and 11
	/// <c>scouting</c>, 8 <c>patrolling</c>; null for an order with no case.
	/// </summary>
	public static int? StateForOrder(short order) => order switch {
		1 => Es1FlyerBehaviour.SearchAndDestroy,
		6 or 10 => Es1FlyerBehaviour.Sleeping,
		7 or 11 => Es1FlyerBehaviour.Scouting,
		8 => Es1FlyerBehaviour.Patrolling,
		_ => null,
	};

	/// <summary>
	/// <c>Flyer_FollowStep</c> (<c>0001c114</c>): <see cref="LeadRouteStep"/> for the group's first member,
	/// <see cref="FormationStep"/> for the others; then the radar <c>+0x90</c> cleared.
	/// </summary>
	public static void FollowStep(Es1Flyer flyer, Es1SimGlobals globals) {
		if (IsLeader(flyer)) {
			LeadRouteStep(flyer);
		} else {
			FormationStep(flyer, globals);
		}

		flyer.RadarOn = false;
	}

	/// <summary>
	/// <c>Flyer_LeadRouteStep</c> (<c>0001b9c4</c>): the waypoint one past the group's route cursor
	/// (<c>Route_WaypointAt</c>), the cursor stepped (<c>Route_AdvanceCursor</c>) once that waypoint is within
	/// <see cref="WaypointReach"/> (<c>Math_DistanceBetweenPoints2D</c>), or the cursor's own waypoint when there is none
	/// past it; <see cref="Es1FlyerFlight.SteerAndFly"/>(flyer, none, Q10(<see cref="RouteSteerGain"/>, the bearing error
	/// to that waypoint)); the formation throttle <see cref="LeaderThrottle"/>; and
	/// <see cref="Es1FlyerFlight.PitchToAltitude"/>(flyer, <see cref="CruiseAltitude"/>). The flyer steers at the waypoint
	/// it read even when the cursor has just stepped past it.
	/// </summary>
	public static void LeadRouteStep(Es1Flyer flyer) {
		// Retail reads the group through +0x3f unchecked; a flyer with no group has no waypoint here.
		var group = flyer.Group;
		var waypoint = group?.WaypointAt(group.RouteCursor + 1);
		if (waypoint is { } next) {
			if (Es1PlayerThink.DistanceBetweenPoints2D(flyer.Position, next) < WaypointReach) {
				group!.AdvanceCursor();
			}
		} else {
			waypoint = group?.WaypointAt(group.RouteCursor);
		}

		// Retail takes the bearing to the point of no waypoint, through a null pointer, for a group with no route. Every
		// flyer group without one sleeps (HercWorks.Query orders), so none reaches it; here the steering is 0, an engine
		// guard.
		short steering = waypoint is { } point
			? (short)SimMath.Q10MultiplyRounded(RouteSteerGain, (short)(Es1MissionGroup.HeadingToward(flyer.Position, point) - flyer.Heading))
			: (short)0;
		Es1FlyerFlight.SteerAndFly(flyer, null, steering);
		flyer.FormationThrottle = LeaderThrottle;
		Es1FlyerFlight.PitchToAltitude(flyer, CruiseAltitude);
	}

	/// <summary>
	/// <c>Flyer_FormationStep</c> (<c>0001ba88</c>): the station is the leader's position moved <see cref="StationLead"/>
	/// along its heading (<c>Math_OffsetPointByBearing</c>) and by the flyer's slot (<c>Flyer_ApplyFormationOffset</c>),
	/// its distance <c>Math_DistanceBetweenPoints</c> and the steering the bearing error to it. The steering is mirrored,
	/// −<c>0x8000</c> − steering, while the flyer's heading is within <see cref="HalfArc"/> of the leader's, the station is
	/// outside it of the nose, and the station is nearer than <see cref="MirrorRange"/>, each arc the unsigned test angle +
	/// <c>0x4000</c> &lt; <c>0x8000</c>. Then <see cref="Es1FlyerFlight.SteerAndFly"/>(flyer, leader, steering),
	/// <see cref="Es1FlyerFlight.PitchToAltitude"/>(flyer, <see cref="CruiseAltitude"/>), so the slot's Z does not lift it,
	/// and <see cref="FormationThrottle"/> within <see cref="ThrottleRange"/> of the station, <see cref="FarThrottle"/>
	/// beyond. The leader is read whether or not it is destroyed.
	/// </summary>
	public static void FormationStep(Es1Flyer flyer, Es1SimGlobals globals) {
		// Retail reads the leader through the group unchecked; a flyer with none is left as it is here, an engine guard.
		if (Leader(flyer) is not { } leader) {
			return;
		}

		var station = Es1CombatStates.OffsetPointByBearing(leader.Position, leader.Heading, StationLead);
		station = Es1FlyerGroups.ApplyOffset(flyer, station, globals.FlyerFormations);
		int distance = flyer.Position.ApproxDistanceTo(station);
		short steering = (short)(Es1MissionGroup.HeadingToward(flyer.Position, station) - flyer.Heading);
		short fromLeader = (short)(flyer.Heading - leader.Heading);
		if ((uint)(fromLeader + HalfArc) < Arc && (uint)(steering + HalfArc) >= Arc && distance < MirrorRange) {
			steering = unchecked((short)(-0x8000 - steering));
		}

		Es1FlyerFlight.SteerAndFly(flyer, leader, steering);
		Es1FlyerFlight.PitchToAltitude(flyer, CruiseAltitude);
		if (distance <= ThrottleRange) {
			FormationThrottle(flyer, StationLead, steering, (short)distance, leader);
		} else {
			flyer.FormationThrottle = FarThrottle;
		}
	}

	/// <summary>
	/// <c>Flyer_FormationThrottle</c> (<c>0001b824</c>)(<paramref name="flyer"/>, <paramref name="baseline"/>,
	/// <paramref name="steering"/>, <paramref name="distance"/>, <paramref name="leader"/>): the throttle <c>+0x1b2</c> plus
	/// 8 × (the leader's forward speed less the flyer's) plus half of (Q14(distance, cos(steering + heading − the leader's
	/// heading)) − baseline), all in 16 bits, held to [<see cref="MinThrottle"/>, <see cref="MaxThrottle"/>]. The move
	/// reloads both speeds from the type record, so the speed term is 0 within a type.
	/// </summary>
	/// <remarks>
	/// The throttle has no reader found in the flyer code (docs/es1/simulation/flyers.md#open), so it changes nothing a
	/// player sees. <c>Flyer_ClearFormationThrottle</c> (<c>0001c0fc</c>), which zeroes it, has no caller found and is not
	/// ported.
	/// </remarks>
	public static void FormationThrottle(Es1Flyer flyer, short baseline, short steering, short distance, Es1Flyer leader) {
		short angle = (short)(steering + flyer.Heading - leader.Heading);
		short excess = (short)(SimMath.Q14MultiplyRounded(distance, SimTrig.Cos(angle)) - baseline);
		short speedTerm = (short)((short)(leader.Velocity.Y - flyer.Velocity.Y) * 8 + (excess >> 1));
		short throttle = (short)(flyer.FormationThrottle + speedTerm);
		flyer.FormationThrottle = throttle >= MaxThrottle ? MaxThrottle : throttle <= MinThrottle ? MinThrottle : throttle;
	}

	/// <summary>
	/// <c>Flyer_BehaviourPatrolThink</c> (<c>0001c21c</c>), <c>patrolling</c>: <see cref="FollowStep"/>; then for the
	/// leader, when the sweep's countdown (<see cref="SweepCountdownOffset"/>) runs out, it is reloaded with
	/// <see cref="SweepInterval"/> and <see cref="Es1Targets.SelectTarget"/> with <see cref="PatrolMask"/> becomes the
	/// target, none included; with a target, <see cref="EngageWithFlight"/>. Answers 0.
	/// </summary>
	public static short PatrolThink(Es1Flyer flyer, Es1SimGlobals globals) {
		FollowStep(flyer, globals);
		if (IsLeader(flyer) && SweepDue(flyer, globals)) {
			flyer.InstallTarget(Es1Targets.SelectTarget(flyer, PatrolMask, globals));
			if (flyer.Target != null) {
				EngageWithFlight(flyer, globals);
			}
		}

		return 0;
	}

	/// <summary>
	/// <c>Flyer_BehaviourSearchDestroyThink</c> (<c>0001c14c</c>), <c>search and destroy</c>: as
	/// <see cref="PatrolThink"/>, with <see cref="SearchDestroyMask"/>, and only a find the group's
	/// <c>Group_IsOrderTarget</c> accepts becomes the target and engages. Answers 0.
	/// </summary>
	public static short SearchDestroyThink(Es1Flyer flyer, Es1SimGlobals globals) {
		FollowStep(flyer, globals);
		if (IsLeader(flyer) && SweepDue(flyer, globals)
				&& Es1Targets.SelectTarget(flyer, SearchDestroyMask, globals) is { } found && flyer.Group!.IsOrderTarget(found)) {
			flyer.InstallTarget(found);
			EngageWithFlight(flyer, globals);
		}

		return 0;
	}

	/// <summary><c>Flyer_BehaviourScoutThink</c> (<c>0001c2d8</c>), <c>scouting</c>: <see cref="FollowStep"/>; answers 0.</summary>
	public static short ScoutThink(Es1Flyer flyer, Es1SimGlobals globals) {
		FollowStep(flyer, globals);
		return 0;
	}

	/// <summary>
	/// <c>Flyer_EngageWithFlight</c> (<c>0001c2ec</c>): <c>attacking</c> installed; then, for the leader, each other member
	/// of the array, up to its count <c>+0x12</c> or its first null, that is not destroyed takes the leader's target
	/// (<see cref="Es1Flyer.InstallTarget"/>) and goes through this in turn.
	/// </summary>
	public static void EngageWithFlight(Es1Flyer flyer, Es1SimGlobals globals) {
		flyer.Behaviour.SetState(Es1FlyerBehaviour.States[Es1FlyerBehaviour.Attacking], globals.DwellJitter);
		if (!IsLeader(flyer)) {
			return;
		}

		foreach (var entry in flyer.Group!.Members) {
			if (entry == null) {
				break;
			}

			// A group's members are of one kind, so every entry of a flyer's group is a flyer, as retail reads it.
			if (!ReferenceEquals(entry, flyer) && entry is Es1Flyer { Destroyed: false } member) {
				member.InstallTarget(flyer.Target);
				EngageWithFlight(member, globals);
			}
		}
	}

	/// <summary>
	/// The group's first member when it is a flyer; null for a flyer with no group or a group whose first ref built none,
	/// which retail would read through.
	/// </summary>
	public static Es1Flyer? Leader(Es1Flyer flyer) => flyer.Group is { Members: [Es1Flyer first, ..] } ? first : null;

	/// <summary>Whether the flyer is the first of its group's member array.</summary>
	public static bool IsLeader(Es1Flyer flyer) => ReferenceEquals(Leader(flyer), flyer);

	/// <summary>
	/// <c>Math_CountdownTimerTick</c> of the sweep's countdown, and when it answers 0 the countdown reloaded with
	/// <see cref="SweepInterval"/>; answers whether it ran out. The countdown lies in the behaviour scratch, so each
	/// <c>Behaviour_SetState</c> clears it and the first sweep of a state comes on its first think.
	/// </summary>
	private static bool SweepDue(Es1Flyer flyer, Es1SimGlobals globals) {
		var block = flyer.Behaviour;
		short countdown = block.ScratchInt16(SweepCountdownOffset);
		globals.Clock.CountdownTimerTick(ref countdown);
		block.SetScratchInt16(SweepCountdownOffset, countdown);
		if (countdown != 0) {
			return false;
		}

		block.SetScratchInt16(SweepCountdownOffset, SweepInterval);
		return true;
	}
}
