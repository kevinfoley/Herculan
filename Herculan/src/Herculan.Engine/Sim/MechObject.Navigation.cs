using Herculan.Engine.Numerics;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The AI navigation slice: the four movement primitives every walking state is built out of, the
/// obstacle avoidance the control law folds into them, and the five behaviour thinks that are
/// navigation rather than combat. The derivation is docs/simulation/ai-navigation.md.
///
/// <para><b>Movement is the think slot's job.</b> The move slot 18 of the 22 states share is
/// <c>Mech_MovementTick</c>, which integrates and collides and decides nothing; every steering
/// decision in the game is one of these functions calling <see cref="LocomotionTick"/> with a turn
/// axis and a desired speed.</para>
/// </summary>
public partial class MechObject {
	/// <summary>
	/// Whether the behaviour state this machine holds drives the control law from its own think, in
	/// which case <see cref="Tick"/> must leave <see cref="Controls"/> alone. In the original the
	/// split is by machine — <c>Sim_PollPlayerInput</c> runs the throttle path for
	/// <c>LocalPlayerMech</c> and nothing else — but the states whose think is not ported drive
	/// nothing, and a machine in one of those is better left on the pilot path than pinned still.
	/// </summary>
	private bool UnderAiControl => !IsPlayer && Behaviour.State is { Think: not ThinkSlot.None };

	/// <summary>
	/// <c>Ai_NavigationStep</c> (<c>0041d598</c>) — the movement half that <c>patrolling</c>,
	/// <c>search/destroy</c> and <c>guarding</c> share.
	///
	/// <para><b>Only the group leader reads the route.</b> Everyone else steers off the leader, which
	/// is what keeps a group together over a route one machine is following, and what stops a group
	/// navigating at all once its leader is gone — the leader is whatever sits in the member array's
	/// slot 0, and that array is never compacted.</para>
	/// </summary>
	private void NavigationStep(SimWorld world) {
		if (SquadOrderVerb != SquadOrderNone) {
			// A standing squad order overrides the group's route with its own destination.
			DriveToPoint(world, SquadOrderDestination);
		} else if (Group is { } group && ReferenceEquals(group.Leader, this)) {
			FollowRoute(world, group);
		} else {
			KeepFormation(world);
		}

		UpdateRadarMode();
		CenterTorsoTick();
	}

	/// <summary>
	/// <c>Ai_DriveToPoint</c> (<c>0041fac4</c>) — the AI's one steering primitive, and the shape every
	/// other steering decision in the simulation is a variation on. Returns whether the machine has
	/// arrived.
	///
	/// <para>Two things it fixes for everything downstream. <b>Steering is the bearing error divided
	/// by 64</b>, against a turn axis the control law clamps at <see cref="MechControls.AxisFull"/> —
	/// so the stick is hard over past a quarter turn of error and proportional inside it. And
	/// <b>range is measured on the ground plane</b>, so a waypoint on a hilltop is as near as one at
	/// its foot.</para>
	///
	/// <para><b>The sprint is gated on a standing squad order</b>, so a machine on mission orders
	/// never uses it however far it has to go: only one the player has given an order to drives at
	/// <see cref="SprintSpeed"/> and engages its Turbo Pod with it.</para>
	/// </summary>
	private bool DriveToPoint(SimWorld world, Vec3i point) {
		short bearing = Detection.HeadingToward(point, Position);
		int distance = GroundDistanceTo(point);
		short speed = CruiseSpeed != 0 ? CruiseSpeed : DefaultCruiseSpeed;

		if (SquadOrderVerb != SquadOrderNone && distance > SprintRange) {
			Pods.TurboPodMount?.EngageTurbo();
			speed = SprintSpeed;
		}

		LocomotionTick(world, SteerToward(bearing), speed);
		return distance < ArrivalRange;
	}

	/// <summary>
	/// How far off a point has to be before <see cref="DriveToPoint"/> will sprint at it — the
	/// original's literal 30000 ground units.
	/// </summary>
	public const int SprintRange = 30000;

	/// <summary>The speed it drives at once it does: a flat <c>0x100</c>, replacing the cruise speed.</summary>
	public const short SprintSpeed = 0x100;

	/// <summary>
	/// <c>Ai_FollowRoute</c> (<c>0041fb60</c>) — walk the group's route. One of four places that
	/// advance a group's cursor, alongside <see cref="PlayerThink"/> and the flyer and ground-vehicle
	/// route steps; all four step the cursor of the group they belong to.
	///
	/// <para>It always drives at the waypoint <i>after</i> the cursor, so the cursor names the last
	/// one reached and a fresh group walks at waypoint 1. A route that has run out leaves the machine
	/// standing on the spot with its throttle at zero — and, through
	/// <see cref="MissionGroup.AiTick"/>'s completion test, ends the order.</para>
	/// </summary>
	private void FollowRoute(SimWorld world, MissionGroup group) {
		if (group.WaypointAt(group.RouteCursor + 1) is not { } next) {
			LocomotionTick(world, 0, 0);
			return;
		}

		if (DriveToPoint(world, next)) {
			group.AdvanceRouteCursor();
		}
	}

	/// <summary>
	/// <c>Ai_KeepFormation</c> (<c>0041fbb8</c>) — hold this machine's formation slot on the group
	/// leader. The post is the leader's position with this machine's own spread offset rotated by the
	/// <i>leader's current heading</i> (<c>Mech_ApplyFormationOffset</c>, mech vtable <c>+0x78</c>),
	/// so the formation turns with the leader rather than being a set of fixed world points.
	///
	/// <para>Four arms. Parked on station it matches the leader's <i>heading</i> rather than its
	/// bearing to the post, which is what keeps a stopped formation dressed. In motion it flies
	/// formation properly: the delta from the post is taken into the leader's own frame by inverting
	/// its rotation, and the lateral half steers while the longitudinal half trims the speed off the
	/// leader's own. A member facing more than 45° away backs out of it at full reverse instead of
	/// driving a wide arc. Everything else closes the gap.</para>
	/// </summary>
	private void KeepFormation(SimWorld world) {
		if (Group?.Leader is not MechObject leader) {
			LocomotionTick(world, 0, 0);
			return;
		}

		var post = FormationPost(leader);
		short headingError = (short)((short)Heading - (short)leader.Heading);
		int distance = GroundDistanceTo(post);

		if (distance < FormationStationRange) {
			LocomotionTick(world, (short)(-headingError >> 6), 0);
			return;
		}

		// The unsigned form the original tests in: a leader whose speed is inside [-25, 24] counts as
		// stopped, and every member falls through to the plain close-the-gap arm.
		bool leaderMoving = (ushort)(leader.Speed + 0x19) > 0x31;
		short bearing = Detection.HeadingToward(post, Position);

		if (distance > FormationTrailRange || !leaderMoving) {
			short closing = distance < FormationTrailRange
				? (short)(distance >> 7)
				: MechControls.AxisFull;

			LocomotionTick(world, SteerToward(bearing), closing);
			return;
		}

		if (Abs(headingError) >= FormationBreakoutError) {
			LocomotionTick(world, headingError < 1 ? MechControls.AxisFull : (short)-MechControls.AxisFull,
				(short)-MechControls.AxisFull);
			return;
		}

		var frame = leader.Rotation();
		frame.TransposeRotation();
		var local = frame.RotateVector(Position.X - post.X, Position.Y - post.Y, Position.Z - post.Z);

		LocomotionTick(world,
			(short)-((headingError >> 6) + (-local.X >> 5)),
			(short)(leader.Speed + (-local.Y >> 5)));
	}

	/// <summary>
	/// <c>Mech_ApplyFormationOffset</c> (<c>00417898</c>) applied to the leader's live position: slot
	/// 0 stands on the point exactly, and every other slot takes its <c>MFORMS.DAT</c> offset rotated
	/// through <c>Formation_RotateAndAddOffset</c> (<c>00411d64</c>) by the leader's own heading. The
	/// offset itself is resolved once, at mission load, by the same table lookup that spread the
	/// group over its spawn point.
	/// </summary>
	private Vec3i FormationPost(MechObject leader) =>
		FormationPostAround(leader.Position, leader.Heading);

	/// <summary>
	/// The same offset applied to an arbitrary anchor and heading rather than to the leader's live
	/// ones — what a group arriving on foot dresses its members with, where the anchor is the point
	/// the arrival picked. See <see cref="MissionGroup.PlaceOnFoot"/>.
	/// </summary>
	internal Vec3i FormationPostAround(Vec3i anchor, int heading) {
		if (FormationOffset is not { } offset) {
			return anchor;
		}

		short cos = BinaryAngle.Cos(heading);
		short sin = BinaryAngle.Sin(heading);

		return new Vec3i(
			anchor.X + (int)(((long)offset.X * cos - (long)offset.Y * sin + 0x2000) >> 14),
			anchor.Y + (int)(((long)offset.X * sin + (long)offset.Y * cos + 0x2000) >> 14),
			anchor.Z);
	}

	/// <summary>
	/// <c>Mech_AiObstacleAvoidance</c> (<c>00416274</c>) — amends a steering and speed decision the
	/// think function has already made. <see cref="LocomotionTick"/> calls it for every machine but
	/// the player's, so it runs on the player's own squadmates as well as on the enemy.
	///
	/// <para>It reduces to the range of the nearest obstruction on each side, both starting at
	/// <see cref="AvoidanceRange"/> for "nothing there", and turns the closer of the two into a steer
	/// away from it that grows linearly with how close it is. <b>The speed is only ever cut for the
	/// player's line of fire</b>: the distance threshold that gates the override is zero for the
	/// other two sources, so terrain and machines are steered around at unchanged throttle.</para>
	///
	/// <para>Two of the original's three obstruction sources are here. The two body-space probes test
	/// terrain but not shapes — <c>Sim_RaycastShapes</c> (<c>00404ca0</c>) collects static structures
	/// and <i>wrecked</i> animated ones, the exact set whose <see cref="SimObject.CollisionRadius"/>
	/// is zero, and the engine has no swept-shape cast, so a wreck is left to the collision test and
	/// a structure is picked up by the proximity sweep instead at a coarser resolution.</para>
	///
	/// <para>The terrain half goes through <see cref="Terrain.HeightGrid.RayWalkVolume"/>, the
	/// original's own mode 1, and it has to: the probes lie flat on the ground, so the thin-ray query
	/// would report a graze on every tick and pin the steer hard over. Mode 1 asks whether the face
	/// is too steep to walk, which is the question this is trying to answer.</para>
	///
	/// <para>The original also carries a complete mirrored mode for a machine walking backwards — the
	/// probe length goes negative and every bearing test rotates by a half turn — behind a flag it
	/// writes zero at the top of the function and never anywhere else. It is not transcribed, and
	/// neither is the half-speed arm of the speed override, which only that flag can reach.</para>
	/// </summary>
	private void ObstacleAvoidance(SimWorld world, ref short turn, ref short desired) {
		if ((Speed == 0 && desired == 0) || (desired < 0 && _backoffTimer == 0)) {
			return;
		}

		int nearLeft = AvoidanceRange;
		int nearRight = AvoidanceRange;
		bool leftFiringLine = false;
		bool rightFiringLine = false;

		var frame = Rotation();
		int ground = world.Terrain.HeightAtWorld(Position.X, Position.Y);

		Probe(world, frame, ground, -ProbeInnerOffset, -ProbeOuterOffset, ref nearLeft);
		Probe(world, frame, ground, ProbeInnerOffset, ProbeOuterOffset, ref nearRight);

		var objects = world.Objects;
		for (int i = 0; i < objects.Count; i++) {
			var other = objects[i];
			if (ReferenceEquals(other, this) || other.AwaitingDeployment || other.CollisionRadius == 0) {
				continue;
			}

			// The one range in this layer the original takes in three dimensions.
			int range = SimMath.Q10Multiply(ObjectRangeGain, Position.ApproxDistanceTo(other.Position));
			if (range >= nearLeft && range >= nearRight) {
				continue;
			}

			short error = (short)(Detection.HeadingToward(other.Position, Position) - (short)Heading);
			if (Abs(error) >= ObjectArc) {
				continue;
			}

			if (error < 0) {
				if (range < nearRight) {
					nearRight = range;
				}
			} else if (range < nearLeft) {
				nearLeft = range;
			}
		}

		// The player's own line of fire: the points Mech_PlayerFireTick stamps along his turret
		// bearing while the trigger is producing shots. Nothing draws them; this is their only reader,
		// and what it does is walk the squad out of the way.
		if (Group is { LedByPlayer: true }) {
			var line = world.PlayerFiringLine;
			for (int i = 0; i < line.Count; i++) {
				int range = SimMath.Q10Multiply(FiringLineRangeGain, GroundDistanceTo(line[i]));
				if (range >= nearLeft && range >= nearRight) {
					continue;
				}

				short error = (short)(Detection.HeadingToward(line[i], Position) - (short)Heading);
				if (Abs(error) >= FiringLineArc) {
					continue;
				}

				if (error < 0) {
					if (range < nearRight) {
						nearRight = range;
						rightFiringLine = true;
					}
				} else if (range < nearLeft) {
					nearLeft = range;
					leftFiringLine = true;
				}
			}
		}

		if (_backoffTimer != 0 && nearLeft == AvoidanceRange && nearRight == AvoidanceRange) {
			// Nothing seen while backing out of something: invent a reading a quarter nearer on the
			// side the collision picked, so the machine still turns as it clears.
			if (_backoffSide > 0) {
				nearLeft -= nearLeft >> 2;
			} else {
				nearRight -= nearRight >> 2;
			}
		}

		if (nearLeft >= AvoidanceRange && nearRight >= AvoidanceRange) {
			return;
		}

		if (_backoffTimer != 0 && nearLeft < AvoidanceRange && nearRight < AvoidanceRange) {
			nearLeft += _backoffSide;
		}

		bool leftIsCloser = nearLeft <= nearRight;
		int closest = leftIsCloser ? nearLeft : nearRight;
		bool firingLine = leftIsCloser ? leftFiringLine : rightFiringLine;

		int gain = firingLine ? FiringLineSteerGain : ObstacleSteerGain;
		int steer = (AvoidanceRange - closest) * gain / AvoidanceRange;
		turn = (short)(turn + (leftIsCloser ? -steer : steer));

		if (firingLine && closest < FiringLineStandoff) {
			// Walking into the player's shots is the one obstruction that stops a machine.
			desired = Type.MaxReverse;
		}
	}

	/// <summary>
	/// One of the two probes: a 20000-unit segment in body space splaying outward, flattened onto the
	/// machine's own terrain height at both ends — so it lies <i>on</i> the ground and only reports
	/// terrain the machine could not walk over. Cast twice, against shapes and against the ground,
	/// and whichever hit is nearer becomes that side's range.
	/// </summary>
	private void Probe(SimWorld world, in Transform3 frame, int ground, int nearX, int farX,
			ref int nearest) {
		var start = frame.TransformPoint(nearX, 0, 0);
		var end = frame.TransformPoint(farX, ProbeReach, 0);

		start = new Vec3i(start.X, start.Y, ground);
		end = new Vec3i(end.X, end.Y, ground);

		ProbeShapes(world, start, end, ref nearest);

		if (world.Terrain.RayWalkVolume(start, end, out var hit)) {
			int range = SimMath.FastMagnitude2D(hit.X - start.X, hit.Y - start.Y);
			if (range < nearest) {
				nearest = range;
			}
		}
	}

	/// <summary>
	/// The shape half of a probe — <c>Sim_RaycastShapes</c> (<c>00404ca0</c>). The candidate filter is
	/// the original's and it is the interesting part: <b>a structure always counts and a machine
	/// counts only when it is destroyed</b>, because live machines are handled by the proximity sweep
	/// instead. So what this finds is buildings and wrecks.
	///
	/// <para>The original then casts a swept volume against each candidate's shape. The engine has no
	/// such cast, so this stops at the coarse reject the original's own cast opens with: the
	/// candidate's bounding radius against the segment's closest approach. It reports a structure from
	/// slightly further out than the shape itself would, which errs toward steering earlier.</para>
	///
	/// <para>It has to be here, not only as a fidelity matter: an animated structure's
	/// <see cref="SimObject.CollisionRadius"/> is zero — it blocks by its volume instead — so the
	/// proximity sweep cannot see one at all, and without this a machine walks into a building and
	/// stands there for the rest of the mission.</para>
	/// </summary>
	private void ProbeShapes(SimWorld world, Vec3i start, Vec3i end, ref int nearest) =>
		ProbeShapes(world, start, end, ref nearest, out _);

	/// <inheritdoc cref="ProbeShapes(SimWorld, Vec3i, Vec3i, ref int)"/>
	/// <param name="struck">
	/// The nearest candidate the segment reached, which <see cref="LineOfSightToTarget"/> needs and
	/// the avoidance probe does not — the original's raycast context carries it either way.
	/// </param>
	private void ProbeShapes(SimWorld world, Vec3i start, Vec3i end, ref int nearest,
			out SimObject? struck) {
		struck = null;
		int spanX = end.X - start.X;
		int spanY = end.Y - start.Y;
		int spanLength = SimMath.FastMagnitude2D(spanX, spanY);

		if (spanLength == 0) {
			return;
		}

		var objects = world.Objects;
		for (int i = 0; i < objects.Count; i++) {
			var other = objects[i];

			if (other.Removed || other.AwaitingDeployment || ReferenceEquals(other, this)
					|| (other is MechObject machine && !machine.Destroyed)) {
				continue;
			}

			int reach = other.ShapeRadius != 0 ? other.ShapeRadius : other.HitRadius;
			if (reach == 0) {
				continue;
			}

			// Where along the segment the candidate is nearest it, clamped to the segment's ends.
			long along = ((long)(other.Position.X - start.X) * spanX
				+ (long)(other.Position.Y - start.Y) * spanY) / spanLength;
			int range = along <= 0 ? 0 : along >= spanLength ? spanLength : (int)along;

			int atX = start.X + (int)((long)spanX * range / spanLength);
			int atY = start.Y + (int)((long)spanY * range / spanLength);

			if (SimMath.FastMagnitude2D(other.Position.X - atX, other.Position.Y - atY) >= reach) {
				continue;
			}

			if (range < nearest) {
				nearest = range;
				struck = other;
			}
		}
	}

	/// <summary>
	/// <c>Ai_LineOfSightBlocked</c> (<c>0041dc24</c>) — whether this machine can see its target, and
	/// the trigger for <c>skirting</c>. Both endpoints are lifted to their objects' aim-node heights
	/// before the cast. See docs/simulation/ai-navigation.md.
	///
	/// <para><b>The two nonzero answers are not "shape" and "terrain".</b>
	/// <see cref="LineOfSight.BlockedByShape"/> is anything the machine cannot get past — a shape, or
	/// ground whose slope the mode-1 walk says it could not climb — and
	/// <see cref="LineOfSight.BlockedByTerrain"/> is ground it <i>could</i>: the thin ray grazes a
	/// rise the machine can simply crest.</para>
	/// </summary>
	private LineOfSight LineOfSightToTarget(SimWorld world) {
		if (Target is not { } target) {
			return LineOfSight.Clear;
		}

		var from = new Vec3i(Position.X, Position.Y, Position.Z + SightHeight);
		var to = new Vec3i(target.Position.X, target.Position.Y,
			target.Position.Z + target.SightHeight);

		bool tooSteep = world.Terrain.RayWalkVolume(from, to, out _);

		int nearest = int.MaxValue;
		ProbeShapes(world, from, to, ref nearest, out var struck);

		if (struck != null && !ReferenceEquals(struck, target)) {
			return LineOfSight.BlockedByShape;
		}

		if (!world.Terrain.RayWalk(from, to, out _)) {
			return LineOfSight.Clear;
		}

		return tooSteep ? LineOfSight.BlockedByShape : LineOfSight.BlockedByTerrain;
	}

	/// <summary>
	/// <c>Ai_UpdateWeaponsFree</c> (<c>0041c3c8</c>) — the AI's radar switch, misnamed after the
	/// mission-file field that feeds it. A machine in the player's squad takes its mode from
	/// <see cref="RadarForcedActive"/>, which is what SCAN FOR HOSTILES and EMCON write; everything
	/// else from <see cref="RadarOrder"/>, the mission file's own standing setting. See
	/// docs/simulation/target-selection.md ("How an AI machine's radar is set").
	/// </summary>
	private void UpdateRadarMode() =>
		Scanner = Group is { LedByPlayer: true } ? RadarForcedActive : RadarOrder;

	/// <summary>
	/// <c>Mech_BehaviourPatrolThink</c> (<c>0041d7d0</c>) — behaviour state 8.
	///
	/// <para>The gate after the movement is the state's shape: <b>a machine that is neither the group
	/// leader nor under a standing squad order does nothing else</b>. Only the leader ever acquires,
	/// and it is the combat reassess's leader sweep that drags the rest of the group in once it has
	/// found a fight — unless the player has told this one to <see cref="FireAtWill"/>, which is
	/// exactly what that order buys.</para>
	/// </summary>
	private bool PatrolThink(SimWorld world) {
		// Sampled before the movement, because the movement can clear it.
		short standing = SquadOrderVerb;
		NavigationStep(world);
		Target = null;

		if (Group is not { } group
				|| (!FireAtWill && standing == SquadOrderNone && !ReferenceEquals(group.Leader, this))) {
			return false;
		}

		if (SimMath.TimerCountDown(ref _navDecisionTimer) != 0) {
			return false;
		}

		_navDecisionTimer = NavDecisionInterval;

		if (OutOfAction) {
			Target = AiTargeting.SelectTarget(world, this, TargetFilter.None);

			if (Target != null) {
				AimComponentClear();
				SetBehaviourState(BehaviourState.Fleeing);
			}

			return false;
		}

		// A machine told to patrol a gridpoint acquires on its own account; otherwise only a group
		// whose own order is patrol does.
		if (standing != SquadOrderPatrol
				&& (standing != SquadOrderNone || group.OrderVerb != MissionOrder.VerbPatrol)) {
			return false;
		}

		Target = AiTargeting.SelectTarget(world, this, TargetFilter.MissionTargetOnly);

		if (Target != null) {
			_targetHandedOver = true;
			CombatReassess(world);
		}

		return false;
	}

	/// <summary>
	/// <c>Mech_BehaviourSearchDestroyThink</c> (<c>0041d60c</c>) — behaviour state 12.
	/// <see cref="PatrolThink"/>'s shape and the same leader gate, differing in the one thing that is
	/// the state: what it acquires must be what the group's order names, so a search-and-destroy
	/// group walks past everything else.
	/// </summary>
	private bool SearchDestroyThink(SimWorld world) {
		NavigationStep(world);
		Target = null;

		if (Group is not { } group || (!FireAtWill && !ReferenceEquals(group.Leader, this))) {
			return false;
		}

		if (SimMath.TimerCountDown(ref _navDecisionTimer) != 0) {
			return false;
		}

		_navDecisionTimer = NavDecisionInterval;

		if (OutOfAction) {
			Target = AiTargeting.SelectTarget(world, this, TargetFilter.None);

			if (Target != null) {
				AimComponentClear();
				SetBehaviourState(BehaviourState.Fleeing);
			}

			return false;
		}

		if (AiTargeting.SelectTarget(world, this, TargetFilter.MissionTargetOnly) is not { } candidate
				|| !group.IsOrderTarget(candidate)) {
			return false;
		}

		Target = candidate;
		_targetHandedOver = true;
		CombatReassess(world);
		return false;
	}

	/// <summary>
	/// <c>Mech_BehaviourTravelThink</c> (<c>0041d9cc</c>) — behaviour states 9 and 11.
	///
	/// <para>It calls <see cref="FollowRoute"/> <b>directly</b> rather than through
	/// <see cref="NavigationStep"/>, so in this one state every member reads the route itself and
	/// every member can advance the shared cursor. Formation is not held while travelling, and the
	/// cursor can be stepped several times in a tick — once by each member already inside arrival
	/// range of the waypoint the previous step just made current.</para>
	/// </summary>
	private bool TravelThink(SimWorld world) {
		if (Group is { } group) {
			FollowRoute(world, group);
		}

		Target = null;
		LookAtTick(world);
		return false;
	}

	/// <summary>
	/// <c>Mech_BehaviourFollowThink</c> (<c>0041daac</c>) — behaviour state 10.
	/// <see cref="TravelThink"/> with the route replaced by the order subject's position and a
	/// 25000-unit stop. It touches no route cursor, while verb 6's completion test <i>is</i> a route
	/// test — so a following order can only ever complete on a group whose route was empty from the
	/// start.
	/// </summary>
	private bool FollowThink(SimWorld world) {
		if (Group?.OrderTarget is { } subject) {
			if (Position.ApproxDistanceTo(subject.Position) < FollowStandoffRange) {
				LocomotionTick(world, 0, 0);
			} else {
				DriveToPoint(world, subject.Position);
			}
		}

		Target = null;
		LookAtTick(world);
		return false;
	}

	/// <summary>
	/// The tail <see cref="TravelThink"/> and <see cref="FollowThink"/> share: on the same 10000-count (about 4.9 s) clock,
	/// pick something worth watching into <see cref="LookAt"/> and point the turret at it.
	///
	/// <para><b>It is not a target.</b> The original never writes it to <c>mech+0x1a4</c> — but it
	/// does shoot at it: the state closes with <see cref="AimAndFire"/>, the same tail the combat
	/// states use. See docs/simulation/ai-weapons.md.</para>
	/// </summary>
	private void LookAtTick(SimWorld world) {
		if (SimMath.TimerCountDown(ref _navDecisionTimer) == 0) {
			LookAt = AiTargeting.SelectTarget(world, this, TargetFilter.None);
			_navDecisionTimer = NavDecisionInterval;
			Scanner = LookAt != null || RadarOrder;
		}

		if (LookAt is { } watched) {
			AimAndFire(world, watched, AspectOf(watched));
		}
	}

	/// <summary>
	/// <c>Mech_BehaviourGuardThink</c> (<c>0041e224</c>) — behaviour state 15. A player squadmate with
	/// no standing order skips the state entirely and holds formation; everything else works a
	/// standoff ring around <see cref="GoalPosition"/>, approaching past 30000, holding still between
	/// 10000 and 30000, and walking outward below 10000.
	///
	/// <para>The half turn in the steering term is what makes a guard <b>face away from</b> the thing
	/// it is guarding. The ring is the whole of the state's movement — there is no patrol of a
	/// perimeter.</para>
	/// </summary>
	private bool GuardThink(SimWorld world) {
		var post = GoalPosition();
		bool holdsPost = Group is not { LedByPlayer: true } || SquadOrderVerb != SquadOrderNone;

		Target = null;
		SimObject? defence = null;

		if ((holdsPost || FireAtWill) && SimMath.TimerCountDown(ref _navDecisionTimer) == 0) {
			defence = SquadOrderVerb == SquadOrderEngage && SquadOrderTarget != null
				? SquadOrderTarget
				: AiTargeting.SelectDefenceTarget(world, this, post,
					Group?.OrderTarget != null ? AiTargeting.DefenceRange : AiTargeting.OpenDefenceRange);
		}

		if (defence == null) {
			if (!holdsPost) {
				KeepFormation(world);
				UpdateRadarMode();
				CenterTorsoTick();
				return false;
			}

			CenterTorsoTick();
			UpdateRadarMode();

			int range = GroundDistanceTo(post);

			if (range > GuardApproachRange) {
				DriveToPoint(world, post);
				return false;
			}

			short outward = (short)(Detection.HeadingToward(post, Position) - (short)Heading
				- GuardFacingHalfTurn);

			LocomotionTick(world, (short)(outward >> 6),
				range >= GuardHoldRange ? (short)0 : MechControls.AxisFull);
			return false;
		}

		Target = defence;

		if (OutOfAction) {
			AimComponentClear();
			SetBehaviourState(BehaviourState.Fleeing);
			return false;
		}

		SetBehaviourState(BehaviourState.DrivingOffEnemy);
		SelectAimComponent(world);

		if (Group is not { LedByPlayer: true } || RadarForcedActive) {
			Scanner = true;
		}

		return false;
	}

	/// <summary>
	/// The steering expression every navigation decision in the game shares: the bearing error taken
	/// as a signed 16-bit angle, divided by 64, and left for <see cref="LocomotionTick"/> to clamp at
	/// the axis stop.
	/// </summary>
	private short SteerToward(short bearing) => (short)((short)(bearing - (short)Heading) >> 6);

	/// <summary>The original's own <c>|x|</c>, with <c>-0x8000</c> saturating rather than wrapping.</summary>
	private static int Abs(short value) => value == short.MinValue ? short.MaxValue
		: value < 0 ? -value : value;

	/// <summary>
	/// <c>mech+0x252</c> — this machine's cruise speed, out of its own mission-file record (block 7
	/// <c>+0x02</c>). Zero, which is 91% of retail records, means <see cref="DefaultCruiseSpeed"/>.
	/// </summary>
	public short CruiseSpeed { get; set; }

	/// <summary>
	/// <c>mech+0x97</c> — the mission file's standing radar setting for this machine (block 7
	/// <c>+0x00</c>), which is the PASSIVE/ACTIVE <see cref="UpdateRadarMode"/> re-asserts each tick.
	/// </summary>
	public bool RadarOrder { get; set; }

	/// <summary>
	/// <c>mech+0x5f</c> — what <c>travelling</c> and <c>following</c> point the turret at while they
	/// walk. Deliberately not <see cref="Target"/>: the original never writes it there.
	/// </summary>
	public SimObject? LookAt { get; private set; }

	/// <summary>
	/// This machine's unrotated <c>MFORMS.DAT</c> spread offset, or null for the group's slot 0 and
	/// for a machine whose formation names none. Resolved at mission load and applied to the leader's
	/// live position by <see cref="FormationPost"/>.
	/// </summary>
	public (int X, int Y)? FormationOffset { get; set; }

	/// <summary>Clears the aim component, which the original does with the same <c>-1</c> immediate.</summary>
	private void AimComponentClear() => AimComponent = NoAimComponent;

	// mech+0x5a — the navigation states' own decision clock, in the behaviour block's scratch. The
	// original reloads it with the same 10000 through a short in three states and an int in two; the
	// value fits either.
	private int _navDecisionTimer;

	/// <summary>The cruise speed a machine with no mission-file setting of its own walks at.</summary>
	private const short DefaultCruiseSpeed = 0xaa;

	/// <summary>Ground range inside which <see cref="DriveToPoint"/> reports arrival — 60 metres.</summary>
	private const int ArrivalRange = 10000;

	/// <summary>
	/// How often a navigation state re-decides, in <see cref="SimMath.TimerCountDown"/>'s unit
	/// rather than milliseconds: about 4.9 seconds.
	/// </summary>
	private const int NavDecisionInterval = 10000;

	/// <summary>Ground range inside which a formation member is on station and stops.</summary>
	private const int FormationStationRange = 2000;

	/// <summary>Ground range past which a member closes on the post at full throttle.</summary>
	private const int FormationTrailRange = 25000;

	/// <summary>Heading error against the leader past which a member backs out rather than turning through.</summary>
	private const short FormationBreakoutError = 0x2000;

	/// <summary>
	/// Range at which <c>following</c> stops short of what it is following. Three-dimensional, unlike
	/// every range a steer is computed from — see docs/simulation/ai-navigation.md.
	/// </summary>
	private const int FollowStandoffRange = 25000;

	/// <summary>Ground range past which a guard walks back to its post.</summary>
	private const int GuardApproachRange = 30000;

	/// <summary>Ground range at which a guard stops walking outward and holds.</summary>
	private const int GuardHoldRange = 10000;

	/// <summary>The half turn that makes a guard face away from what it is guarding.</summary>
	private const short GuardFacingHalfTurn = -0x8000;

	/// <summary>"Nothing there" for either side of the avoidance, and the range its steer scales over.</summary>
	private const int AvoidanceRange = 22000;

	/// <summary>How far ahead the two avoidance probes reach.</summary>
	private const int ProbeReach = 20000;

	/// <summary>How far to each side a probe starts.</summary>
	private const int ProbeInnerOffset = 1500;

	/// <summary>How far to each side a probe ends.</summary>
	private const int ProbeOuterOffset = 10000;

	/// <summary>Q10 gain on a machine's range — near enough double, so one reads as an obstruction twice as far off.</summary>
	private const int ObjectRangeGain = 2000;

	/// <summary>Half-arc, either side of dead ahead, inside which a machine claims a side. 45°.</summary>
	private const short ObjectArc = 0x2000;

	/// <summary>Q10 gain on a firing-line point's range — near enough the true one.</summary>
	private const int FiringLineRangeGain = 1000;

	/// <summary>Half-arc inside which a firing-line point claims a side. 67.5°, wider than a machine's.</summary>
	private const short FiringLineArc = 0x3000;

	/// <summary>Steer gain against terrain and machines.</summary>
	private const int ObstacleSteerGain = 700;

	/// <summary>Steer gain against the player's line of fire, and the only source that also cuts speed.</summary>
	private const int FiringLineSteerGain = 1000;

	/// <summary>Range to a firing-line point inside which the machine backs off instead of only steering.</summary>
	private const int FiringLineStandoff = 3000;
}
