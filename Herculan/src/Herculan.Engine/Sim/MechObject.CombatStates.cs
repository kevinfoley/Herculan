using Herculan.Engine.Numerics;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The nine behaviour thinks that are not navigation: the five a machine fights in, the two it
/// disengages in, and the two it stands still in. The derivation is
/// docs/simulation/ai-combat-states.md.
///
/// <para><b>A combat state decides where to stand and nothing else.</b> Every one of them ends in
/// <see cref="CombatMoveStep"/> and <see cref="AimAndFire"/>, and differs only in the steering and
/// the standoff pair it writes into the geometry block first.</para>
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// <c>Ai_BuildCombatGeometry</c>'s <c>0x18</c>-byte stack block (<c>0041e758</c>) — everything a
	/// combat state knows about where its target is, built once per tick and then amended by the
	/// state before <see cref="CombatMoveStep"/> reads it.
	/// </summary>
	private struct CombatGeometry {
		/// <summary><c>+0x00</c> — bearing to the target.</summary>
		public short Bearing;

		/// <summary><c>+0x02</c> — that bearing less this machine's heading. The field a state rewrites to steer.</summary>
		public short BearingError;

		/// <summary><c>+0x04</c> — <see cref="BearingError"/>'s magnitude, saturating at <c>0x7fff</c>.</summary>
		public short BearingMagnitude;

		/// <summary>
		/// <c>+0x06</c> — the target's aspect: the bearing back to this machine in the target's own
		/// turret frame. See <see cref="AspectOf"/>.
		/// </summary>
		public short Aspect;

		/// <summary><c>+0x08</c> — <see cref="Aspect"/>'s magnitude.</summary>
		public short AspectMagnitude;

		/// <summary>
		/// <c>+0x0a</c> — the <b>3D</b> range. Every range a navigation steer is computed from is the
		/// ground-plane one; this is not.
		/// </summary>
		public int Range;

		/// <summary><c>+0x0e</c> — 1 forward, −1 reverse, 0 stand.</summary>
		public short Approach;

		/// <summary><c>+0x10</c> — inside this the machine opens the range.</summary>
		public int NearStandoff;

		/// <summary><c>+0x14</c> — outside this it closes it.</summary>
		public int FarStandoff;
	}

	/// <summary>
	/// <c>Ai_BuildCombatGeometry</c> (<c>0041e758</c>). The standoff pair it starts with is the ring
	/// <c>attacking</c> keeps; every other state overwrites it.
	/// </summary>
	private CombatGeometry BuildCombatGeometry(SimObject target) {
		var geometry = new CombatGeometry {
			Bearing = Detection.HeadingToward(target.Position, Position),
			Range = Position.ApproxDistanceTo(target.Position),
			Approach = 0,
			NearStandoff = DefaultNearStandoff,
			FarStandoff = DefaultFarStandoff
		};

		geometry.BearingError = (short)(geometry.Bearing - (short)Heading);
		geometry.Aspect = (short)(target.AimTwist
			+ (short)(geometry.Bearing + BinaryAngle.HalfTurn) - (short)target.Heading);
		geometry.BearingMagnitude = (short)Abs(geometry.BearingError);
		geometry.AspectMagnitude = (short)Abs(geometry.Aspect);
		return geometry;
	}

	/// <summary>
	/// <c>Ai_CombatMoveStep</c> (<c>0041e828</c>) — the walk every combat state ends in.
	///
	/// <para><b>The standoff pair is a ring and the approach flag is how the machine holds it.</b>
	/// Too near and it moves away, too far and it moves toward, and the sign is chosen so that either
	/// happens whichever way the machine is facing — a machine with its back to something it wants to
	/// leave drives forward, one facing it reverses. Between the two the state's own flag stands, and
	/// zero means stand and shoot.</para>
	///
	/// <para>Both range tests read the magnitude the geometry built, <b>before</b> the recompute
	/// below them, so they judge by where the target is while the steer follows wherever the state
	/// has pointed the machine. Speed is <c>±0x100</c> or nothing: a combat state never cruises.</para>
	/// </summary>
	private void CombatMoveStep(SimWorld world, ref CombatGeometry geometry) {
		if (geometry.Range < geometry.NearStandoff) {
			geometry.Approach = (short)(geometry.BearingMagnitude > 0x3fff ? 1 : -1);
		} else if (geometry.Range > geometry.FarStandoff) {
			geometry.Approach = (short)(geometry.BearingMagnitude < 0x4001 ? 1 : -1);
		}

		geometry.BearingMagnitude = (short)Abs(geometry.BearingError);

		short speed = 0;

		if (geometry.Approach != 0) {
			// The original also stores the flag at mech+0x5a here, which nothing reads — but those bytes
			// are the low half of the countdown `fleeing` steps at mech+0x5b, so the store rewrites that
			// clock's low byte every tick. The store is left out and the aliasing reproduced in
			// FleeThink, which is the only place it has an effect.
			speed = geometry.Approach > 0 ? MechControls.AxisFull : (short)-MechControls.AxisFull;
		}

		LocomotionTick(world, (short)(geometry.BearingError >> 6), speed);
	}

	/// <summary>
	/// <c>Mech_BehaviourAttackThink</c> (<c>0041c594</c>) — behaviour state 3. The only state that
	/// reads the <i>target's</i> facing to decide where to stand.
	///
	/// <para>When the target is looking at this machine it walks at a point 18000 units off the
	/// target's own beam, picking the beam that does not cross the target's nose. When it is not, no
	/// point is built at all: the machine squares up and shoots, giving ground only while it is still
	/// turning.</para>
	///
	/// <para>The reverse arm <b>reflects the aim point through the machine's own position</b>, so a
	/// machine with the target nearly astern backs onto the same place rather than turning around to
	/// walk at it.</para>
	/// </summary>
	private bool AttackThink(SimWorld world) {
		if (BeginSkirtIfBlocked() || Target is not { } target) {
			return false;
		}

		var geometry = BuildCombatGeometry(target);

		if (geometry.AspectMagnitude < AttackBeamAspect) {
			geometry.Approach = (short)(BinaryAngle.HalfTurn - geometry.BearingMagnitude
				>= AttackAsternMargin ? 1 : -1);

			short side = (short)((geometry.BearingError < 0) == (geometry.BearingMagnitude > 0x4000)
				? BinaryAngle.QuarterTurn
				: -BinaryAngle.QuarterTurn);

			var point = SimTrig.OffsetPointByBearing(target.Position, (short)((short)target.Heading + side),
				AttackBeamOffset);

			if (geometry.Approach == -1) {
				point = new Vec3i(
					2 * Position.X - point.X, 2 * Position.Y - point.Y, 2 * Position.Z - point.Z);
			}

			geometry.BearingError =
				(short)(Detection.HeadingToward(point, Position) - (short)Heading);
		} else {
			geometry.Approach = (short)(geometry.BearingMagnitude > AttackSquareUpArc ? -1 : 0);
		}

		return CombatTail(world, ref geometry, target);
	}

	/// <summary>
	/// <c>Mech_BehaviourFlankThink</c> (<c>0041d4e4</c>) — behaviour state 4. The shared shape with
	/// <see cref="CircleStep"/> between the geometry and the move step, and nothing else.
	///
	/// <para><b>No retail chassis can reach it</b>: the combat reassess gates <c>flanking</c> on a
	/// type field that reads zero on all 21 of them, so a machine that is outgunned takes
	/// <c>facing off</c> instead. It is ported because <c>attacking base</c> shares the circling step,
	/// and because a modded chassis could open the gate.</para>
	/// </summary>
	private bool FlankThink(SimWorld world) {
		if (BeginSkirtIfBlocked() || Target is not { } target) {
			return false;
		}

		var geometry = BuildCombatGeometry(target);
		CircleStep(ref geometry, target);
		return CombatTail(world, ref geometry, target);
	}

	/// <summary>
	/// <c>Mech_BehaviourFaceOffThink</c> (<c>0041d41c</c>) — behaviour state 5, and the whole of
	/// state 16. Walk in when pointed at the target, back off when more than 45° off it, and hold a
	/// slightly wider ring than <see cref="AttackThink"/>'s. It never steers anywhere but at the
	/// target, which is what facing off is: the machine that is outgunned keeps its front armour
	/// toward the machine that outguns it.
	/// </summary>
	private bool FaceOffThink(SimWorld world) {
		if (BeginSkirtIfBlocked() || Target is not { } target) {
			return false;
		}

		var geometry = BuildCombatGeometry(target);
		geometry.Approach = (short)(geometry.BearingMagnitude > FaceOffArc ? -1 : 1);
		geometry.NearStandoff = FaceOffNearStandoff;
		geometry.FarStandoff = FaceOffFarStandoff;
		return CombatTail(world, ref geometry, target);
	}

	/// <summary>
	/// <c>Mech_BehaviourDriveOffThink</c> (<c>0041def0</c>) — behaviour state 16, which is
	/// <see cref="FaceOffThink"/> verbatim. What separates the two states is entirely in their
	/// descriptors: this one holds a place and reassesses through <see cref="SelectBehaviour"/>, so
	/// after its dwell it goes back to its order instead of looking for another fight.
	/// </summary>
	private bool DriveOffThink(SimWorld world) => FaceOffThink(world);

	/// <summary>
	/// <c>Mech_BehaviourAttackBaseThink</c> (<c>0041c86c</c>) — behaviour state 6.
	///
	/// <para>The far standoff is <b>negative</b>, which a range can never fall below, so one of the
	/// move step's two arms fires on every tick: the machine holds a 25000-unit ring and is never
	/// allowed to stand still.</para>
	///
	/// <para>Forcing the aspect makes <see cref="CircleStep"/> see a target that is not facing it
	/// whatever the geometry says, so the machine stands off rather than circling. Which structures
	/// escape that is <see cref="BaseType.ThreatensAttackers"/>.</para>
	/// </summary>
	private bool AttackBaseThink(SimWorld world) {
		if (BeginSkirtIfBlocked() || Target is not { } target) {
			return false;
		}

		var geometry = BuildCombatGeometry(target);

		if (target is not BaseObject { Type.ThreatensAttackers: true }) {
			geometry.Aspect = HarmlessBaseAspect;
			geometry.AspectMagnitude = HarmlessBaseAspect;
		}

		CircleStep(ref geometry, target);
		geometry.NearStandoff = AttackBaseStandoff;
		geometry.FarStandoff = AttackBaseFarStandoff;

		CombatMoveStep(world, ref geometry);
		AimAndFire(world, target, geometry.Aspect);

		// The one completion test that is not simply "is my target finished". A structure the mission
		// itself named has to be destroyed; any other only has to be out of action. So a machine sent
		// to level a specific building stays on it to the end.
		bool done = SquadOrderVerb == SquadOrderEngage && SquadOrderTarget != null
				|| Group is { } group && group.IsOrderTarget(target)
			? target.Destroyed
			: target.OutOfAction;

		return done || OutOfAction ? ClearSquadEngageOrder() : false;
	}

	/// <summary>
	/// <c>Mech_BehaviourAttackFlyerThink</c> (<c>0041c9cc</c>) — behaviour state 7. No skirt gate,
	/// there being nothing to walk around under a flyer, and standoffs that put both of the move
	/// step's arms out of reach so the state owns the approach flag outright.
	///
	/// <para>A machine only advances on a flyer it is pointed within 11° of <i>and</i> that is both
	/// far off and not pointed back at it, so turning under one is mostly what it does. It gives up
	/// on range before it looks at whether the flyer is even alive.</para>
	/// </summary>
	private bool AttackFlyerThink(SimWorld world) {
		if (Target is not { } target) {
			return false;
		}

		var geometry = BuildCombatGeometry(target);

		bool aimed = geometry.BearingMagnitude < FlyerBearingGate;
		bool offAspect = geometry.AspectMagnitude > FlyerAspectGate;
		bool distant = geometry.Range > FlyerApproachRange;

		geometry.Approach = (short)(!aimed ? -1
			: offAspect && distant ? 1
			: offAspect || distant ? 0
			: -1);

		geometry.NearStandoff = FlyerNearStandoff;
		geometry.FarStandoff = FlyerFarStandoff;

		CombatMoveStep(world, ref geometry);
		AimAndFire(world, target, geometry.Aspect);

		if (geometry.Range > FlyerAbandonRange) {
			return true;
		}

		return target.Destroyed || OutOfAction ? ClearSquadEngageOrder() : false;
	}

	/// <summary>
	/// <c>Mech_BehaviourFleeThink</c> (<c>0041d2c4</c>) — behaviour state 18. It runs <i>from</i>
	/// something, which is not the same as having it as a target: the first thing it does is stash
	/// the selection and release it, so a fleeing machine holds no target and is not counted among
	/// its threat's holders.
	///
	/// <para>Two legs. While the threat is still within 67.5° of the nose the machine reverses with
	/// its steering pointed a half turn away, which turns it; once turned, the other leg runs it off
	/// at 135° to the threat, flipping sides every four seconds so it does not run in a straight
	/// line.</para>
	///
	/// <para><b>It still shoots at what it is running from</b>, and <see cref="FleeCheck"/> has
	/// already put <see cref="Fear"/> high enough to drop <see cref="ChooseWeapon"/>'s score floor to
	/// near zero, so it fires almost anything it has left.</para>
	/// </summary>
	private bool FleeThink(SimWorld world) {
		if (Target is { } selected) {
			_fleeingFrom = selected;
			Target = null;
		}

		if (_fleeingFrom is not { } threat) {
			return false;
		}

		var geometry = BuildCombatGeometry(threat);

		if (geometry.BearingMagnitude < FleeTurnAwayArc) {
			geometry.Approach = -1;
			geometry.BearingError = (short)(geometry.BearingError + BinaryAngle.HalfTurn);
		} else {
			if (SimMath.TimerCountDown(ref _fleeSideTimer) == 0) {
				_fleeSideTimer = FleeSideInterval;
				_fleeToTheLeft = world.Random.NextMasked(1) != 0;
			}

			geometry.Approach = 1;
			geometry.BearingError = (short)(geometry.BearingError
				+ (_fleeToTheLeft ? -FleeRunOutAngle : FleeRunOutAngle));
		}

		geometry.NearStandoff = FleeNearStandoff;
		geometry.FarStandoff = FleeFarStandoff;

		// The Turbo Pod goes on here, on every tick of the run — the one place in the AI that engages
		// one without a standing squad order behind it. Silent: the original gates the engage tone on
		// the pod having a cockpit gauge, which only the player's has.
		Pods.TurboPodMount?.EngageTurbo();

		CombatMoveStep(world, ref geometry);

		// The approach flag lands on the low byte of the countdown above, which is why the reload is
		// rounded rather than exact. It is the original's own scratch aliasing, and it moves a 4000 ms
		// clock between 3840 and 4095.
		_fleeSideTimer = (_fleeSideTimer & ~0xff) | (geometry.Approach > 0 ? 0x00 : 0xff);

		AimAndFire(world, threat, geometry.Aspect);
		return threat.OutOfAction;
	}

	/// <summary>
	/// <c>Mech_BehaviourSkirtThink</c> (<c>0041dd64</c>) — behaviour state 14, the excursion a machine
	/// makes when its own shots are stopping on something that is not what it aimed at.
	///
	/// <para>It walks an arc around the obstruction: a point 90° off its own line to the stashed
	/// position, at the range it currently stands at, always to the side chosen on the first tick.
	/// The line of sight is re-tested every five seconds and only a <i>clear</i> reading ends the
	/// state — and <b>only a shape block gets the arc</b>. Blocked by ground, the machine walks
	/// straight at the stash, which is it driving up the hill that is in the way.</para>
	///
	/// <para>It does not shoot, and the stash is a position taken once — so a machine skirting after
	/// a moving target walks to where that target was.</para>
	/// </summary>
	private bool SkirtThink(SimWorld world) {
		if (!_skirtStarted) {
			_skirtToTheLeft =
				(short)(Detection.HeadingToward(_skirtPoint, Position) - (short)Heading) < 0;
			_skirtStarted = true;
			_skirtRecheckTimer = SkirtRecheckInterval;
			_skirtBlocking = LineOfSight.BlockedByShape;
		}

		if (SimMath.CountdownTimerTick(ref _skirtRecheckTimer) == 0) {
			_skirtRecheckTimer = SkirtRecheckInterval;
			_skirtBlocking = LineOfSightToTarget(world);

			if (_skirtBlocking == LineOfSight.Clear) {
				SetBehaviourState(_skirtReturnState ?? BehaviourState.Deciding);
				LineOfFireBlocked = false;
			} else {
				_skirtRange = GroundDistanceTo(_skirtPoint);
			}
		}

		var point = _skirtPoint;

		if (_skirtBlocking == LineOfSight.BlockedByShape) {
			short outward = Detection.HeadingToward(Position, point);
			point = SimTrig.OffsetPointByBearing(point,
				(short)(outward + (_skirtToTheLeft ? -BinaryAngle.QuarterTurn : BinaryAngle.QuarterTurn)),
				_skirtRange);
		}

		short bearing = Detection.HeadingToward(point, Position);
		LocomotionTick(world, SteerToward(bearing), MechControls.AxisFull);
		CenterTorsoTick();
		return false;
	}

	/// <summary>
	/// <c>Mech_BehaviourSleepThink</c> (<c>0041c418</c>) — behaviour state 13. A sleeping machine
	/// stands with its throttle at zero and its radar on whatever the mission file set, and holds no
	/// target — but it is still ticked, still detectable, and still answers fire.
	/// </summary>
	private bool SleepThink(SimWorld world) {
		Target = null;
		LocomotionTick(world, 0, 0);
		UpdateRadarMode();
		return false;
	}

	/// <summary>
	/// <c>Mech_BehaviourInertThink</c> (<c>0041e554</c>) — behaviour states 20 and 21. The two share
	/// the think and differ only in their descriptors.
	/// </summary>
	private bool InertThink(SimWorld world) {
		LocomotionTick(world, 0, 0);
		return false;
	}

	/// <summary>
	/// <c>Ai_CircleStep</c> (<c>0041c72c</c>) — the step <c>flanking</c> and <c>attacking base</c>
	/// share, and the only thing in the AI that alternates between two manoeuvres on a clock.
	///
	/// <para><b>The circle is a point 15000 units out from the target on this machine's own approach
	/// line, rotated 33° to one side</b> — near enough a tangent, so the machine walks a wide arc
	/// around a target that is pointing at it and closes on one that is not. Which side it goes is
	/// re-chosen every tick from the way it is already turning, so a machine that overshoots reverses
	/// its arc rather than committing.</para>
	///
	/// <para><b>The break-off is bought with damage.</b> While <see cref="DamageTaken"/> — the damage
	/// taken in the last two seconds or so — is past 100, the machine spends about half a second in
	/// every two reversing in a straight line with no steering at all; one that stops taking fire stops
	/// breaking off when that window next expires.</para>
	///
	/// <para>The square-up arm gates on this machine's <i>own</i> turret twist rather than on any
	/// range, so it walks backwards until its hull has caught up with where its guns already
	/// point.</para>
	/// </summary>
	private void CircleStep(ref CombatGeometry geometry, SimObject target) {
		if (SimMath.CountdownTimerTick(ref _circleBreakOffTimer) != 0) {
			geometry.NearStandoff = 0;
			geometry.Approach = -1;
			geometry.BearingError = 0;
			_circleSquaredUp = false;
			_circleIntervalTimer = CircleBreakOffInterval;
			return;
		}

		if (SimMath.CountdownTimerTick(ref _circleIntervalTimer) == 0
				&& DamageTaken > CircleBreakOffDamage) {
			_circleBreakOffTimer = CircleBreakOffDuration;
		}

		short threshold = _circleSquaredUp ? CircleSquaredUpAspect : CircleEngagedAspect;

		if (geometry.AspectMagnitude < threshold) {
			short side = geometry.BearingError >= 0 ? CircleTangent : (short)-CircleTangent;

			var point = SimTrig.OffsetPointByBearing(target.Position,
				(short)(geometry.Bearing + BinaryAngle.HalfTurn + side), CircleRadius);

			geometry.NearStandoff = 0;
			geometry.Approach = 1;
			geometry.BearingError =
				(short)(Detection.HeadingToward(point, Position) - (short)Heading);
			_circleSquaredUp = false;
		} else {
			_circleSquaredUp = true;
			geometry.Approach = (short)(Abs(TorsoTwistAngle) < CircleSquareUpTwist ? 0 : -1);
		}
	}

	/// <summary>
	/// The tail the five plain combat states share: walk, shoot, then let go if the target is finished
	/// or this machine is.
	/// </summary>
	private bool CombatTail(SimWorld world, ref CombatGeometry geometry, SimObject target) {
		CombatMoveStep(world, ref geometry);
		AimAndFire(world, target, geometry.Aspect);

		return AiTargeting.ShouldAbandonTarget(this) || OutOfAction
			? ClearSquadEngageOrder()
			: false;
	}

	/// <summary>
	/// <c>Ai_BeginSkirtIfBlocked</c> (<c>0041de9c</c>) — the gate at the top of every combat think but
	/// <c>attacking flyer</c>'s. It stashes the state it interrupted <i>in the scratch it has just
	/// cleared</i>, which is what makes <c>skirting</c> an excursion rather than a decision: the
	/// machine goes back to exactly the state it left.
	/// </summary>
	private bool BeginSkirtIfBlocked() {
		if (!LineOfFireBlocked) {
			return false;
		}

		var interrupted = Behaviour.State;
		SetBehaviourState(BehaviourState.Skirting);
		_skirtReturnState = interrupted;
		_skirtRange = Position.ApproxDistanceTo(_skirtPoint);
		return true;
	}

	/// <summary>
	/// <c>Behaviour_SetState</c> (<c>00413e50</c>) as the machine sees it: the descriptor and its
	/// countdown go into <see cref="Behaviour"/>, and the block's <c>0x28</c>-byte scratch — which
	/// here is the named per-state fields below and in MechObject.Navigation.cs — is zeroed.
	///
	/// <para>Every state change in the engine goes through this rather than through
	/// <see cref="BehaviourBlock.SetState"/> directly, because a state that inherited the previous
	/// one's timers and latches would not be the state the original installs.</para>
	/// </summary>
	private void SetBehaviourState(BehaviourState state) {
		_navDecisionTimer = 0;
		LookAt = null;
		_fleeingFrom = null;
		_fleeSideTimer = 0;
		_fleeToTheLeft = false;
		_skirtStarted = false;
		_skirtToTheLeft = false;
		_skirtBlocking = LineOfSight.Clear;
		_skirtRecheckTimer = 0;
		_skirtRange = 0;
		_skirtReturnState = null;
		_circleBreakOffTimer = 0;
		_circleIntervalTimer = 0;
		_circleSquaredUp = false;
		_ramRetargetTimer = 0;
		_ramCharging = false;
		_ramPhaseTimer = 0;

		Behaviour.SetState(state);
	}

	/// <summary>
	/// <c>Mech_AiOnLineOfFireBlocked</c> (<c>0041dd2c</c>, mech vtable <c>+0x64</c>) — one of this
	/// machine's own shots stopped on something that is not what it aimed at.
	/// <see cref="SimWorld.Raycast"/> is the only caller; the base and flyer classes leave the slot
	/// empty, so only a machine reacts.
	/// </summary>
	public void OnLineOfFireBlocked() {
		if (Target is not { } target) {
			return;
		}

		_skirtPoint = target.Position;
		LineOfFireBlocked = true;
	}

	/// <summary>
	/// <c>Ai_LineOfSightBlocked</c> (<c>0041dc24</c>)'s three answers. See
	/// docs/simulation/ai-combat-states.md.
	/// </summary>
	private enum LineOfSight {
		/// <summary>Nothing in the way.</summary>
		Clear,

		/// <summary>A shape is in the way — the reading that makes <c>skirting</c> walk an arc.</summary>
		BlockedByShape,

		/// <summary>Terrain as well, which <c>skirting</c> walks straight at.</summary>
		BlockedByTerrain
	}

	/// <summary>
	/// <c>mech+0xad</c> — this machine's line of fire is blocked, and the next combat think will turn
	/// that into <c>skirting</c>. Written by <see cref="OnLineOfFireBlocked"/> and cleared when the
	/// state ends.
	/// </summary>
	public bool LineOfFireBlocked { get; private set; }

	// mech+0x61 in `fleeing` — the object being run from, which is deliberately not Target.
	private SimObject? _fleeingFrom;

	// mech+0x5b — `fleeing`'s side-switch countdown.
	private int _fleeSideTimer;

	// mech+0x5f — which side `fleeing` runs to.
	private bool _fleeToTheLeft;

	// mech+0x5e/+0x60/+0x61/+0x63/+0x67 and +0x31e — `skirting`'s scratch and its stashed point.
	private bool _skirtStarted;
	private bool _skirtToTheLeft;
	private LineOfSight _skirtBlocking;
	private short _skirtRecheckTimer;
	private int _skirtRange;
	private Vec3i _skirtPoint;
	private BehaviourState? _skirtReturnState;

	// mech+0x60/+0x63/+0x66 — Ai_CircleStep's two clocks and its hysteresis.
	private short _circleBreakOffTimer;
	private short _circleIntervalTimer;
	private bool _circleSquaredUp;

	/// <summary>The near half of the ring <c>Ai_BuildCombatGeometry</c> starts every state with.</summary>
	private const int DefaultNearStandoff = 15000;

	/// <summary>The far half of it.</summary>
	private const int DefaultFarStandoff = 30000;

	/// <summary>Aspect magnitude under which <c>attacking</c> counts the target as looking at it — 44°.</summary>
	private const short AttackBeamAspect = 8000;

	/// <summary>How far off the target's beam <c>attacking</c> puts its aim point.</summary>
	private const int AttackBeamOffset = 18000;

	/// <summary>How near dead astern the target has to be before <c>attacking</c> backs onto its point.</summary>
	private const int AttackAsternMargin = 4000;

	/// <summary>Bearing error past which <c>attacking</c> gives ground while it turns — 45°.</summary>
	private const short AttackSquareUpArc = 0x1fff;

	/// <summary>Bearing error past which <c>facing off</c> backs away — 45°.</summary>
	private const short FaceOffArc = 0x1fff;

	/// <summary>The near half of <c>facing off</c>'s ring.</summary>
	private const int FaceOffNearStandoff = 16000;

	/// <summary>The far half of it.</summary>
	private const int FaceOffFarStandoff = 35000;

	/// <summary>What <c>attacking base</c> forces the aspect to for a structure that cannot hurt it.</summary>
	private const short HarmlessBaseAspect = 32000;

	/// <summary>The ring <c>attacking base</c> holds.</summary>
	private const int AttackBaseStandoff = 25000;

	/// <summary>Its far standoff, which is negative so that a range is always outside it.</summary>
	private const int AttackBaseFarStandoff = -20536;

	/// <summary>Bearing error inside which <c>attacking flyer</c> will consider advancing — 11°.</summary>
	private const short FlyerBearingGate = 2000;

	/// <summary>Aspect magnitude past which the flyer counts as not pointed back.</summary>
	private const short FlyerAspectGate = 2000;

	/// <summary>Range past which <c>attacking flyer</c> closes rather than holding.</summary>
	private const int FlyerApproachRange = 35000;

	/// <summary>Range past which <c>attacking flyer</c> gives the flyer up entirely.</summary>
	private const int FlyerAbandonRange = 150000;

	/// <summary>Its near standoff, low enough that the move step's opening arm never fires.</summary>
	private const int FlyerNearStandoff = 0;

	/// <summary>Its far standoff, high enough that the closing arm never fires either.</summary>
	private const int FlyerFarStandoff = 1000000;

	/// <summary>Bearing error inside which <c>fleeing</c> is still turning away — 67.5°.</summary>
	private const short FleeTurnAwayArc = 0x3000;

	/// <summary>The angle off the threat that <c>fleeing</c> runs at once it has turned — 135°.</summary>
	private const short FleeRunOutAngle = 0x6000;

	/// <summary>
	/// How often <c>fleeing</c> swaps the side it runs to, in
	/// <see cref="SimMath.TimerCountDown"/>'s unit rather than milliseconds: about two seconds.
	/// </summary>
	private const int FleeSideInterval = 4000;

	/// <summary><c>fleeing</c>'s near standoff.</summary>
	private const int FleeNearStandoff = 0;

	/// <summary><c>fleeing</c>'s far standoff.</summary>
	private const int FleeFarStandoff = 10000000;

	/// <summary>
	/// How often <c>skirting</c> re-tests the line of sight, in the same unit — about 2.4 seconds.
	/// </summary>
	private const short SkirtRecheckInterval = 5000;

	/// <summary>Aspect threshold to <i>start</i> circling — 135°.</summary>
	private const short CircleEngagedAspect = 0x6000;

	/// <summary>Aspect threshold to keep circling once squared up — 90°, so the hysteresis is one-sided.</summary>
	private const short CircleSquaredUpAspect = 0x4000;

	/// <summary>How far out from the target the circling point sits.</summary>
	private const int CircleRadius = 15000;

	/// <summary>How far the circling point is rotated off the approach line — 33°.</summary>
	private const short CircleTangent = 6000;

	/// <summary>Turret twist inside which the square-up arm stops walking backwards.</summary>
	private const short CircleSquareUpTwist = 2000;

	/// <summary>How long a break-off lasts, in the same unit — about half a second.</summary>
	private const short CircleBreakOffDuration = 1000;

	/// <summary>How often one may be armed, in the same unit — about two seconds.</summary>
	private const short CircleBreakOffInterval = 4000;

	/// <summary><see cref="DamageTaken"/> past which an interval's expiry arms a break-off.</summary>
	private const int CircleBreakOffDamage = 100;
}
